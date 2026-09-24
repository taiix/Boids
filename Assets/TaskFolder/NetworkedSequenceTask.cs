using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FishGame
{
    /// <summary>
    /// Server-authoritative 2-player comms task. Two fish activate ONE shared station (an "n/2 players"
    /// prompt). Then across 3 rounds (lengths 3, 4, 5) one fish SEES the sequence for a few seconds and
    /// the OTHER inputs it — swapping which fish does which each round. The seer only ever reads the
    /// arrows and relays them (voice/Discord); the inputter never sees them. All rounds correct → the
    /// task completes (shaves time off the survival clock and removes its props, e.g. the sea urchins).
    ///
    /// Place this on a scene object with a NetworkIdentity + a trigger collider; the server owns it.
    /// The client UI is a screen-space arrow panel each client already has in the scene.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class NetworkedSequenceTask : NetworkBehaviour
    {
        enum Phase : byte { Idle, Show, Input, Done }

        [Header("Rounds")]
        [SerializeField] int[] roundLengths = { 3, 4, 5 };
        [Tooltip("Seconds the seer sees the sequence before the inputter's turn.")]
        [SerializeField] float showSeconds = 10f;
        [Tooltip("Pause between rounds / after feedback so it's readable.")]
        [SerializeField] float betweenRounds = 1.5f;

        [Header("Reward / props")]
        [SerializeField] float timeReward = 40f;
        [Tooltip("Objects hidden on all clients when the task completes (e.g. the sea urchins).")]
        [SerializeField] GameObject[] propsToRemove;

        [Header("Props highlight (so the station can be found)")]
        [SerializeField] bool glowProps = true;
        [SerializeField] Color glowColor = new Color(0.2f, 1f, 0.8f);
        [Tooltip("Brightness of the glow at the top of a pulse.")]
        [SerializeField] float glowIntensity = 6f;
        [Tooltip("Pulses per second.")]
        [SerializeField] float glowPulseRate = 0.6f;

        [Header("Join")]
        [Tooltip("How close a fish must be to join / act - measured to the station or any of its props " +
                 "(the urchins), whichever is nearest.")]
        [SerializeField] float joinRadius = 6f;

        [Header("Client UI (screen-space, shared per client)")]
        [Tooltip("Panel with >= max-length slot children, each holding an arrow child.")]
        [SerializeField] GameObject arrowPanel;
        [SerializeField] GameObject statusRoot;
        [SerializeField] Text statusLabel;

        // ---- synced state (drives the UI on every client) ----
        [SyncVar(hook = nameof(OnIntChanged))] int _joined;
        [SyncVar(hook = nameof(OnPhaseChanged))] Phase _phase = Phase.Idle;
        [SyncVar(hook = nameof(OnIntChanged))] int _roundIndex;
        [SyncVar(hook = nameof(OnUintChanged))] uint _seerNetId;
        [SyncVar(hook = nameof(OnUintChanged))] uint _inputterNetId;
        [SyncVar] bool _consumed;
        // Who has joined (0 = empty seat), so each client knows whether it's the one waiting here.
        [SyncVar(hook = nameof(OnUintChanged))] uint _player1;
        [SyncVar(hook = nameof(OnUintChanged))] uint _player2;

        // ---- server state ----
        readonly List<FishPlayer> _players = new List<FishPlayer>();
        readonly List<Direction> _sequence = new List<Direction>();
        int[] _submitted;
        bool _gotSubmission;

        // ---- client interaction ----
        static readonly Dictionary<Direction, int> Rot = new Dictionary<Direction, int>
        { { Direction.Down, 0 }, { Direction.Right, 90 }, { Direction.Up, 180 }, { Direction.Left, 270 } };

        /// <summary>True while the local fish is next to this (or any) task and can press E to join —
        /// FishPlayer checks it so E joins the task instead of eating.</summary>
        public static bool LocalNearAnyTask;

        bool _localInRange;
        float _localDistance = float.MaxValue;
        readonly List<Direction> _inputBuffer = new List<Direction>();
        InputAction _interact, _dir, _submit, _delete;
        FishPlayer _parkedFish; // the local body we parked (controls off) while it's part of the task
        Renderer[] _propRenderers;
        MaterialPropertyBlock _glowBlock;
        static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
        static readonly int EmissiveWeightId = Shader.PropertyToID("_EmissiveExposureWeight");

        FishPlayer LocalFish =>
            NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<FishPlayer>() : null;

        int CurrentLen => (_roundIndex >= 0 && _roundIndex < roundLengths.Length) ? roundLengths[_roundIndex] : 0;
        bool IAmSeer { get { var lf = LocalFish; return lf != null && lf.netId == _seerNetId; } }
        bool IAmInputter { get { var lf = LocalFish; return lf != null && lf.netId == _inputterNetId; } }
        bool IAmJoined { get { var lf = LocalFish; return lf != null && lf.netId != 0 && (lf.netId == _player1 || lf.netId == _player2); } }

        // ======================================================================= client input
        void Awake()
        {
            _interact = new InputAction("TaskJoin", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonNorth");

            _dir = new InputAction("TaskDir", InputActionType.Value, "Vector2");
            _dir.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _dir.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _dir.performed += OnDir;

            _submit = new InputAction("TaskSubmit", InputActionType.Button);
            _submit.AddBinding("<Keyboard>/enter"); _submit.AddBinding("<Keyboard>/numpadEnter");
            _submit.performed += _ => TrySubmit();

            _delete = new InputAction("TaskDelete", InputActionType.Button);
            _delete.AddBinding("<Keyboard>/backspace");
            _delete.performed += _ => TryDelete();

            HidePanel();
            SetStatus(null);
        }

        void OnEnable() { _interact.Enable(); _dir.Enable(); _submit.Enable(); _delete.Enable(); }
        void OnDisable()
        {
            _interact.Disable(); _dir.Disable(); _submit.Disable(); _delete.Disable();
            Park(null); // never leave the local fish stuck
        }

        void Update()
        {
            if (isServer) ServerPruneWaiting();
            if (!NetworkClient.active) return;
            if (glowProps && !_consumed) GlowProps();

            var lf = LocalFish;
            bool joined = IAmJoined;
            bool wasInRange = _localInRange;
            _localDistance = lf != null && !lf.IsShark ? DistanceTo(lf.transform.position) : float.MaxValue;
            _localInRange = _localDistance <= joinRadius;
            LocalNearAnyTask = (_localInRange || joined) && _phase == Phase.Idle && !_consumed;
            if (_localInRange && !wasInRange) Debug.Log($"[Task] {name}: in range ({_localDistance:0.0} m) - E to join");

            // E joins; while waiting for a partner, E again leaves (and we can swim away).
            if (_phase == Phase.Idle && !_consumed && lf != null && _interact.WasPressedThisFrame())
            {
                if (joined) lf.CmdLeaveSequenceTask(netId);
                else if (_localInRange) lf.CmdJoinSequenceTask(netId);
            }

            // Parked from joining until the task is done (or we leave / it resets): WASD are arrows then.
            Park(joined && !_consumed && _phase != Phase.Done ? lf : null);

            if (_phase == Phase.Idle) RefreshUI(); // keep the "n/2 / press E" prompt live as we move
        }

        // Soft pulsing glow on the props so the station stands out on the dark seabed. Exposure weight 0
        // keeps HDRP's camera exposure from dimming the emission to nothing underwater.
        void GlowProps()
        {
            if (_propRenderers == null)
            {
                var list = new List<Renderer>();
                if (propsToRemove != null)
                    foreach (var p in propsToRemove)
                        if (p != null) list.AddRange(p.GetComponentsInChildren<Renderer>(true));
                _propRenderers = list.ToArray();
                _glowBlock = new MaterialPropertyBlock();
            }

            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * glowPulseRate * 2f * Mathf.PI);
            Color emissive = glowColor * (glowIntensity * pulse);
            foreach (var r in _propRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_glowBlock);
                _glowBlock.SetColor(EmissiveColorId, emissive);
                _glowBlock.SetFloat(EmissiveWeightId, 0f);
                r.SetPropertyBlock(_glowBlock);
            }
        }

        // Distance from a point to the station: its centre or any of its props (the urchins) - you've
        // "reached" it when you're at the urchins, wherever on the cluster, however big the fish is.
        float DistanceTo(Vector3 p)
        {
            float best = (p - transform.position).sqrMagnitude;
            if (propsToRemove != null)
                foreach (var prop in propsToRemove)
                    if (prop != null && prop.activeInHierarchy)
                        best = Mathf.Min(best, (p - prop.transform.position).sqrMagnitude);
            return Mathf.Sqrt(best);
        }

        // Turn controls off for the given local body (or release whichever we parked, when null).
        void Park(FishPlayer body)
        {
            if (_parkedFish == body) return;
            if (_parkedFish != null) _parkedFish.SetTaskLocked(false);
            _parkedFish = body;
            if (body != null) body.SetTaskLocked(true);
        }

        void OnDir(InputAction.CallbackContext ctx)
        {
            if (_phase != Phase.Input || !IAmInputter) return;
            Vector2 v = ctx.ReadValue<Vector2>();
            Direction d;
            if (v == Vector2.up) d = Direction.Up;
            else if (v == Vector2.down) d = Direction.Down;
            else if (v == Vector2.left) d = Direction.Left;
            else if (v == Vector2.right) d = Direction.Right;
            else return;

            if (_inputBuffer.Count >= CurrentLen) return;
            _inputBuffer.Add(d);
            SetArrow(_inputBuffer.Count - 1, d, true);
            UpdateInputStatus();
        }

        void TryDelete()
        {
            if (_phase != Phase.Input || !IAmInputter || _inputBuffer.Count == 0) return;
            SetArrow(_inputBuffer.Count - 1, Direction.Up, false);
            _inputBuffer.RemoveAt(_inputBuffer.Count - 1);
            UpdateInputStatus();
        }

        void TrySubmit()
        {
            if (_phase != Phase.Input || !IAmInputter || _inputBuffer.Count < CurrentLen) return;
            var dirs = new int[_inputBuffer.Count];
            for (int i = 0; i < dirs.Length; i++) dirs[i] = (int)_inputBuffer[i];
            LocalFish.CmdSubmitSequence(netId, dirs);
        }

        // ======================================================================= client UI
        void OnIntChanged(int _, int __) => RefreshUI();
        void OnPhaseChanged(Phase _, Phase __) => RefreshUI();
        void OnUintChanged(uint _, uint __) => RefreshUI();

        void RefreshUI()
        {
            var lf = LocalFish;
            if (lf == null || lf.IsShark) { HidePanel(); SetStatus(null); return; }

            switch (_phase)
            {
                case Phase.Idle:
                    HidePanel();
                    if (_consumed) SetStatus(null);
                    else if (IAmJoined) SetStatus($"{_joined}/2 players ready — waiting for a partner   •   E to leave");
                    else if (_localInRange) SetStatus($"Reef puzzle — press E to join   ({_joined}/2 players ready)");
                    else SetStatus(null);
                    break;
                case Phase.Show:
                    if (IAmSeer) { ShowPanel(); SetStatus($"Round {_roundIndex + 1}/3 — read the arrows to your partner!"); }
                    else if (IAmInputter) { HidePanel(); SetStatus($"Round {_roundIndex + 1}/3 — your partner is reading the sequence…"); }
                    else { HidePanel(); SetStatus(null); }
                    break;
                case Phase.Input:
                    if (IAmInputter) { ShowPanel(); SetStatus($"Enter what your partner says   (0/{CurrentLen})   •   Backspace delete   •   Enter submit"); }
                    else if (IAmSeer) { HidePanel(); SetStatus("Your partner is entering it…"); }
                    else SetStatus(null);
                    break;
                default:
                    HidePanel(); SetStatus(null);
                    break;
            }
        }

        void UpdateInputStatus()
        {
            if (statusLabel == null) return;
            SetStatus(_inputBuffer.Count >= CurrentLen
                ? "All entered — press ENTER to submit   •   Backspace delete"
                : $"Enter what your partner says   ({_inputBuffer.Count}/{CurrentLen})   •   Backspace delete   •   Enter submit");
        }

        [TargetRpc]
        void TargetShowSequence(NetworkConnectionToClient target, int[] dirs)
        {
            ShowPanel();
            ClearArrows();
            for (int i = 0; i < dirs.Length; i++) SetArrow(i, (Direction)dirs[i], true);
        }

        [ClientRpc]
        void RpcBeginInput()
        {
            _inputBuffer.Clear();
            ClearArrows();
            RefreshUI();
        }

        [ClientRpc]
        void RpcFeedback(string msg, bool good)
        {
            if (statusLabel != null)
            {
                statusLabel.color = good ? new Color(0.35f, 0.9f, 0.45f) : new Color(0.95f, 0.4f, 0.4f);
                SetStatus(msg);
            }
        }

        [ClientRpc]
        void RpcRemoveProps()
        {
            if (propsToRemove != null)
                foreach (var p in propsToRemove) if (p != null) p.SetActive(false);
        }

        // ---- small UI helpers (operate on this client's copy of the shared panel) ----
        void ShowPanel() { if (arrowPanel != null) arrowPanel.SetActive(true); }
        void HidePanel() { if (arrowPanel != null) arrowPanel.SetActive(false); }
        void ClearArrows()
        {
            if (arrowPanel == null) return;
            for (int i = 0; i < arrowPanel.transform.childCount; i++)
                arrowPanel.transform.GetChild(i).GetChild(0).gameObject.SetActive(false);
        }
        void SetArrow(int slot, Direction d, bool on)
        {
            if (arrowPanel == null || slot < 0 || slot >= arrowPanel.transform.childCount) return;
            var arrow = arrowPanel.transform.GetChild(slot).GetChild(0);
            arrow.gameObject.SetActive(on);
            if (on) arrow.GetComponent<RectTransform>().localRotation = Quaternion.Euler(0f, 0f, Rot[d]);
        }
        void SetStatus(string s)
        {
            if (statusLabel != null && s != null) statusLabel.text = s;
            if (statusRoot != null) statusRoot.SetActive(!string.IsNullOrEmpty(s));
        }

        // ======================================================================= server
        [Server]
        public void ServerJoin(FishPlayer fp)
        {
            if (_phase != Phase.Idle || _consumed || fp == null || fp.IsShark || _players.Contains(fp)) return;
            // Same reach rule as the client, plus a little slack for the lag between the two.
            if (DistanceTo(fp.transform.position) > joinRadius + 2f)
            {
                Debug.Log($"[Task] {name}: rejected join from {fp.name}, {DistanceTo(fp.transform.position):0.0} m away");
                return;
            }

            _players.Add(fp);
            SyncSeats();
            if (_players.Count >= 2) StartCoroutine(ServerRun());
        }

        /// <summary>A waiting player changed their mind (E again) - free their seat.</summary>
        [Server]
        public void ServerLeave(FishPlayer fp)
        {
            if (_phase != Phase.Idle || !_players.Remove(fp)) return;
            SyncSeats();
        }

        // While waiting, drop seats whose body is gone (left the game, swapped role, got eaten).
        [Server]
        void ServerPruneWaiting()
        {
            if (_phase != Phase.Idle || _players.Count == 0) return;
            if (_players.RemoveAll(p => p == null) > 0) SyncSeats();
        }

        [Server]
        void SyncSeats()
        {
            _joined = _players.Count;
            _player1 = _players.Count > 0 && _players[0] != null ? _players[0].netId : 0u;
            _player2 = _players.Count > 1 && _players[1] != null ? _players[1].netId : 0u;
        }

        [Server]
        public void ServerSubmit(FishPlayer fp, int[] dirs)
        {
            if (_phase != Phase.Input || fp == null || fp.netId != _inputterNetId) return;
            _submitted = dirs;
            _gotSubmission = true;
        }

        [Server]
        IEnumerator ServerRun()
        {
            _roundIndex = 0;
            while (_roundIndex < roundLengths.Length)
            {
                // Swap who sees / inputs each round.
                FishPlayer seer = _players[_roundIndex % 2];
                FishPlayer inputter = _players[(_roundIndex + 1) % 2];
                if (seer == null || inputter == null) { AbortLeft(); yield break; } // someone left: reset, don't reward
                _seerNetId = seer.netId;
                _inputterNetId = inputter.netId;

                // Generate + show to the seer only.
                _sequence.Clear();
                int len = roundLengths[_roundIndex];
                for (int i = 0; i < len; i++) _sequence.Add((Direction)Random.Range(0, 4));

                _phase = Phase.Show;
                var dirs = new int[len];
                for (int i = 0; i < len; i++) dirs[i] = (int)_sequence[i];
                TargetShowSequence(seer.connectionToClient, dirs);
                yield return new WaitForSeconds(showSeconds);

                // Inputter's turn.
                _gotSubmission = false; _submitted = null;
                _phase = Phase.Input;
                RpcBeginInput();

                float deadline = Time.time + 120f;
                while (!_gotSubmission && Time.time < deadline)
                {
                    if (_players[0] == null || _players[1] == null) { AbortLeft(); yield break; }
                    yield return null;
                }

                bool correct = _submitted != null && Matches(_submitted, _sequence);
                if (correct)
                {
                    RpcFeedback($"Round {_roundIndex + 1} correct!", true);
                    _roundIndex++;
                }
                else
                {
                    RpcFeedback("Wrong — try that round again.", false);
                    // stay on the same round; regenerate next loop
                }
                yield return new WaitForSeconds(betweenRounds);
            }

            _phase = Phase.Done;
            _consumed = true;
            _players.Clear();
            SyncSeats(); // everyone's released
            if (MatchManager.Instance != null) MatchManager.Instance.ReduceRemaining(timeReward);
            RpcRemoveProps();
            RpcFeedback("Reef repaired!", true);
        }

        [Server]
        void AbortLeft()
        {
            _phase = Phase.Idle;
            _players.Clear();
            SyncSeats();
            RpcFeedback("A partner left — task reset.", false);
        }

        static bool Matches(int[] input, List<Direction> seq)
        {
            if (input == null || input.Length != seq.Count) return false;
            for (int i = 0; i < seq.Count; i++) if ((int)seq[i] != input[i]) return false;
            return true;
        }
    }
}
