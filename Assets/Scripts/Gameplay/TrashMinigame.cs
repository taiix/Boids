using System.Collections.Generic;
using FishGame;
using Mirror;
using UnityEngine;

/// <summary>
/// Timing minigame on a piece of trash (next to its <see cref="PushableTrash"/>, with a trigger collider
/// for <see cref="FishInteract"/>). Swim up and press E: your fish is parked behind the trash and the
/// minigame UI opens (<see cref="TrashMinigameUI"/>). Press Space as the sweeping needle passes through
/// the orange boxes: the better your timing, the fuller the speed circle and the faster the trash rolls
/// toward the nearest sinkhole. E again lets go. Your fish rides along behind the trash.
///
/// Networking: each player's own machine runs the UI and sends its speed to the server a few times a
/// second (<see cref="CmdPush"/>); the server adds up everyone balancing on this piece and hands it to
/// <see cref="PushableTrash.MinigameSpeed"/>, which moves it for everyone. Offline it does that directly.
/// </summary>
public class TrashMinigame : NetworkBehaviour, IInteractable
{
    [Tooltip("How often the local player's speed is sent to the server (per second).")]
    [SerializeField] float sendRate = 10f;
    [Tooltip("Server: how far a player's fish may be from the trash and still push it (lag allowance).")]
    [SerializeField] float maxPushDistance = 8f;

    [Header("Pushing pose (local fish)")]
    [Tooltip("Seconds for the fish to swing round behind the trash when the minigame starts.")]
    [SerializeField] float settleSeconds = 0.4f;
    [Tooltip("How far a hit shoves the fish into the trash (m).")]
    [SerializeField] float shoveDistance = 0.35f;
    [Tooltip("Back-and-forth pumping while it pushes (m at full speed) - it also makes the tail beat.")]
    [SerializeField] float pumpDistance = 0.08f;
    [SerializeField] float pumpRate = 9f;

    /// <summary>This machine's player is next to (or playing) a trash minigame - E belongs to it.</summary>
    public static bool LocalNearAny => s_nearFrame >= Time.frameCount - 1;
    static int s_nearFrame = -10;
    static TrashMinigame s_active;   // the one this machine's player is playing

    PushableTrash _trash;
    Transform _closestSinkhole;
    bool _focused;
    bool _isMinigameActive;

    // local player while playing
    FishPlayer _fish;
    Vector3 _poseStart, _pushDir;
    float _reach, _poseTime, _shove, _startedAt;
    float _nextSend;
    float _lastSent = -1f;

    // server: speed per player
    readonly Dictionary<int, (float speed, float time)> _pushers = new Dictionary<int, (float, float)>();

    // ---- pushing together: heavy trash (PushableTrash.FishNeeded > 1) ----
    // Every pusher plays the same gauge, but a hit only counts when all the fish needed hit within
    // SyncWindow of each other: then the shared speed goes up and everyone sees "IN SYNC". With fewer live
    // fish in the match than it needs (the offline sandbox), it needs as many as there are.
    [Tooltip("Seconds within which the pushers' hits count as together.")]
    [SerializeField] float syncWindow = 0.3f;
    int _logHits, _logMisses;     // this push, for MatchLog
    float _logStartDist;
    [Tooltip("Shared speed added by a perfectly-timed hit together (0-1).")]
    [SerializeField] float syncGain = 0.25f;
    [Tooltip("Shared speed lost per second.")]
    [SerializeField] float syncDrain = 0.06f;

    [SyncVar] float _sharedSpeed;
    [SyncVar] byte _needed = 1;
    [SyncVar] byte _playing;
    readonly Dictionary<int, (float acc, float time)> _hits = new Dictionary<int, (float, float)>();
    readonly List<(float at, float acc)> _fakeHits = new List<(float, float)>();
    int _shownSyncs;
    [SyncVar] int _syncs;

    /// <summary>Testing: needs this many fish whatever the match has (0 = normal).</summary>
    public static int DebugForceNeeded;
    /// <summary>Testing: a pretend partner hits this many seconds after each of ours (negative = no partner).</summary>
    public static float DebugPartnerOffset = -1f;

    bool Together => _needed > 1;

    bool Offline => !NetworkServer.active && !NetworkClient.active;

    public bool CanInteract => _trash != null && _trash.CanBePushed;
    public bool OnFocus => _focused;
    public bool IsBusy => _isMinigameActive;

    void Awake()
    {
        _trash = GetComponent<PushableTrash>();
    }

    void Start()
    {
        float best = float.MaxValue;
        foreach (var hole in FindObjectsByType<Sinkhole>(FindObjectsInactive.Exclude))
        {
            float d = Vector3.Distance(transform.position, hole.transform.position);
            if (d < best) { best = d; _closestSinkhole = hole.transform; }
        }
    }

    void OnDisable()
    {
        if (_isMinigameActive) EndMinigame();
        if (_trash != null) _trash.MinigameSpeed = 0f;
    }

    void Update()
    {
        if (_focused || _isMinigameActive) s_nearFrame = Time.frameCount;

        if (_focused && !_isMinigameActive && CanInteract && s_active == null)
            MatchHud.ShowPrompt($"Press {GameInput.Interact.Token} to push the trash");

        if (_isMinigameActive) Minigame();

        if (Offline || isServer) ServerUpdate();
    }

    // ------------------------------------------------------------------ local player

    void Minigame()
    {
        // Stop when the trash is gone, or our fish can't play any more.
        if (!CanInteract || _fish == null || !_fish.isActiveAndEnabled ||
            (_fish.TryGetComponent(out FishVitals vitals) && vitals.IsDead) ||
            (_fish.transform.position - transform.position).sqrMagnitude > maxPushDistance * maxPushDistance)
        {
            EndMinigame();
            return;
        }

        // Swimming off (WASD / stick / D-pad / down) lets go of the trash.
        if (Time.unscaledTime - _startedAt > 0.3f && PlayerWantsToMove())
        {
            EndMinigame();
            return;
        }

        // Pushing together: the ring shows the shared speed, and who's on it.
        TrashMinigameUI.SetTogether(Together, _playing, _needed, _sharedSpeed);
        if (_syncs != _shownSyncs)
        {
            _shownSyncs = _syncs;
            if (Together) { TrashMinigameUI.ShowFeedback("TOGETHER!", true); GameFeel.Shake(0.08f); _shove = 1f; }
        }

        PushPose(Time.deltaTime);

        if (_closestSinkhole != null)
        {
            Vector3 flat = _closestSinkhole.position - transform.position;
            flat.y = 0f;
            TrashMinigameUI.SetHint($"{flat.magnitude:0} m");
        }

        if (PauseMenu.IsOpen) { SendSpeed(0f, true); return; }
        // together, the server only needs to know we're on it (the speed is shared)
        SendSpeed(Together ? Mathf.Max(0.05f, TrashMinigameUI.Speed) : TrashMinigameUI.Speed, false);
    }

    void SendSpeed(float speed, bool force)
    {
        if (!force && Time.unscaledTime < _nextSend) return;
        if (force && Mathf.Approximately(speed, _lastSent)) return;
        _nextSend = Time.unscaledTime + 1f / Mathf.Max(sendRate, 1f);
        _lastSent = speed;
        if (Offline) ServerSetPush(0, speed);
        else CmdPush(speed);
    }

    void OnPressed(bool hit)
    {
        if (s_active != this) return;
        if (hit) _logHits++; else _logMisses++;
        if (!hit) return;
        if (Together) return;   // together, the shove waits for IN SYNC
        GameFeel.Shake(0.05f);
        _shove = 1f;   // a shove into the trash
        if (_fish != null && _fish.TryGetComponent(out BodyJuice juice)) juice.Punch(0.06f);
    }

    void OnHit(float accuracy)
    {
        if (s_active != this || !Together) return;
        if (Offline) ServerHit(0, accuracy);
        else CmdHit(accuracy);
    }

    // The fish sits nose-first against the trash, on the side away from the sinkhole, and leans into
    // it: a shove on every hit, and a steady pumping that grows with speed (the tail beats with it - the
    // swim animation reads how the parked body moves). The body is parked (kinematic, controls off).
    void PushPose(float dt)
    {
        Vector3 centre = transform.position;
        Vector3 want = PushDirection(_fish.transform.position);
        _pushDir = Vector3.Slerp(_pushDir, want, 1f - Mathf.Exp(-3f * dt)).normalized;

        float effort = Together ? _sharedSpeed : TrashMinigameUI.Speed;
        _shove = Mathf.MoveTowards(_shove, 0f, dt * 4f);
        float pump = Mathf.Sin(Time.time * pumpRate) * pumpDistance * (0.25f + effort);
        float back = _reach - _shove * shoveDistance + pump;
        Vector3 target = centre - _pushDir * back;

        _poseTime += dt / Mathf.Max(0.01f, settleSeconds);
        float k = Mathf.Clamp01(_poseTime);
        k = k * k * (3f - 2f * k);
        Vector3 pos = Vector3.Lerp(_poseStart, target, k);
        _fish.transform.position = pos;

        Vector3 look = centre - pos;
        if (look.sqrMagnitude > 1e-4f)
            _fish.transform.rotation = Quaternion.Slerp(_fish.transform.rotation,
                Quaternion.LookRotation(look.normalized, Vector3.up), 1f - Mathf.Exp(-8f * dt));
    }

    // Flat direction the trash should go: to the nearest sinkhole, else away from the fish.
    Vector3 PushDirection(Vector3 fishPos)
    {
        var hole = Sinkhole.Nearest(transform.position, out float d);
        Vector3 dir = hole != null && d > 0.1f ? hole.Centre - transform.position : transform.position - fishPos;
        dir.y = 0f;
        return dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
    }

    static bool PlayerWantsToMove()
    {
        if (GameInput.Move.ReadPointer().sqrMagnitude > 0.2f) return true;                 // WASD
        if (GameInput.PuzzleUp.IsPressed() || GameInput.PuzzleDown.IsPressed() ||
            GameInput.PuzzleLeft.IsPressed() || GameInput.PuzzleRight.IsPressed()) return true;   // + D-pad
        if (GameInput.Descend.IsPressed()) return true;
        var pad = UnityEngine.InputSystem.Gamepad.current;
        return pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.25f;
    }

    void StartMinigame()
    {
        _fish = FindLocalFish();
        if (_fish == null) return;
        if (s_active != null && s_active != this) s_active.EndMinigame();

        s_active = this;
        _isMinigameActive = true;
        _fish.SetTaskLocked(true);
        _startedAt = Time.unscaledTime;
        _poseStart = _fish.transform.position;
        _poseTime = 0f;
        _shove = 0f;
        _pushDir = PushDirection(_fish.transform.position);
        // nose against the surface: trash radius + half the fish's length
        float half = 0.4f;
        foreach (var c in _fish.GetComponentsInChildren<Collider>())
            if (!c.isTrigger) half = Mathf.Max(half, Mathf.Max(c.bounds.extents.x, c.bounds.extents.z));
        _reach = (_trash != null ? _trash.Radius : 1f) + half * 0.9f;

        // Look past the fish and the trash toward the sinkhole, so neither hides the other.
        foreach (var cam in FindObjectsByType<FishOrbitCamera>(FindObjectsInactive.Exclude))
            if (cam.Target == _fish.transform) cam.TurnToward(_pushDir);
        _shownSyncs = _syncs;
        _logHits = _logMisses = 0;
        _logStartDist = _closestSinkhole != null ? Vector3.Distance(transform.position, _closestSinkhole.position) : -1f;
        MatchLog.Write("start", "Trash push", -1f, $"needed={_needed} dist={_logStartDist:0}");
        TrashMinigameUI.Open();
        TrashMinigameUI.Pressed += OnPressed;
        TrashMinigameUI.Hit += OnHit;
    }

    void EndMinigame()
    {
        if (!_isMinigameActive) return;
        _isMinigameActive = false;
        float moved = _closestSinkhole != null && _logStartDist >= 0f
            ? _logStartDist - Vector3.Distance(transform.position, _closestSinkhole.position) : 0f;
        MatchLog.Write(CanInteract ? "left" : "sunk", "Trash push", Time.unscaledTime - _startedAt,
            $"hits={_logHits} misses={_logMisses} moved={moved:0.0} together={Together}");
        TrashMinigameUI.Pressed -= OnPressed;
        TrashMinigameUI.Hit -= OnHit;
        TrashMinigameUI.Close();
        if (_fish != null) _fish.SetTaskLocked(false);
        _fish = null;
        if (s_active == this) s_active = null;
        // let the server know right away (a destroyed object can't send)
        if (isActiveAndEnabled && (Offline || NetworkClient.isConnected)) { _lastSent = -1f; SendSpeed(0f, true); }
    }

    static FishPlayer FindLocalFish()
    {
        foreach (var p in TrashPusher.All)
            if (p != null && p.IsLocalFish && p.TryGetComponent(out FishPlayer fish)) return fish;
        return null;
    }

    // ------------------------------------------------------------------ IInteractable

    public void Interact()
    {
        if (_isMinigameActive) EndMinigame();
        else if (CanInteract) StartMinigame();
    }

    /// <summary>The fish swam into our trigger.</summary>
    public void Focus() => _focused = true;

    public void LoseFocus()
    {
        _focused = false;
        // Called once FishInteract lets go; while playing it keeps hold, so this is the end of it.
        if (_isMinigameActive) EndMinigame();
    }

    // ------------------------------------------------------------------ server

    [Command(requiresAuthority = false)]
    void CmdPush(float speed, NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null) return;
        // Only a fish that's really at the trash counts.
        if ((sender.identity.transform.position - transform.position).sqrMagnitude > maxPushDistance * maxPushDistance)
            speed = 0f;
        ServerSetPush(sender.connectionId, speed);
    }

    void ServerSetPush(int who, float speed)
    {
        speed = Mathf.Clamp01(speed);
        if (speed <= 0f) _pushers.Remove(who);
        else _pushers[who] = (speed, Time.time);
    }

    void ServerUpdate()
    {
        if (_trash == null) return;
        float total = 0f;
        List<int> stale = null;
        foreach (var kv in _pushers)
        {
            if (Time.time - kv.Value.time > 1f) (stale ??= new List<int>()).Add(kv.Key);  // stopped reporting
            else total += kv.Value.speed;
        }
        if (stale != null) foreach (var k in stale) _pushers.Remove(k);

        // how many it needs: what the trash says, but never more fish than are in the match
        int live = 0;
        foreach (var fp in FishPlayer.All)
            if (fp != null && fp.isActiveAndEnabled && !fp.IsSharkBody && !fp.IsOut) live++;
        int needed = DebugForceNeeded > 0 ? DebugForceNeeded : Mathf.Clamp(Mathf.Min(_trash.FishNeeded, live), 1, 4);
        if (_needed != needed) _needed = (byte)needed;
        if (_playing != (byte)_pushers.Count) _playing = (byte)_pushers.Count;

        if (!Together) { _trash.MinigameSpeed = total; return; }

        // the pretend partner (testing)
        for (int i = _fakeHits.Count - 1; i >= 0; i--)
            if (Time.time >= _fakeHits[i].at) { _hits[-1] = (_fakeHits[i].acc, Time.time); _fakeHits.RemoveAt(i); TrySync(); }

        float speed = Mathf.Max(0f, _sharedSpeed - syncDrain * Time.deltaTime);
        if (_pushers.Count == 0) speed = Mathf.Max(0f, speed - syncDrain * 3f * Time.deltaTime);
        if (!Mathf.Approximately(speed, _sharedSpeed)) _sharedSpeed = speed;
        _trash.MinigameSpeed = _sharedSpeed;
    }

    [Command(requiresAuthority = false)]
    void CmdHit(float accuracy, NetworkConnectionToClient sender = null)
    {
        if (sender == null || sender.identity == null) return;
        if ((sender.identity.transform.position - transform.position).sqrMagnitude > maxPushDistance * maxPushDistance) return;
        ServerHit(sender.connectionId, accuracy);
    }

    void ServerHit(int who, float accuracy)
    {
        _hits[who] = (Mathf.Clamp01(accuracy), Time.time);
        if (DebugPartnerOffset >= 0f) _fakeHits.Add((Time.time + DebugPartnerOffset, accuracy));
        TrySync();
    }

    // Enough pushers hit within the window: that's a push together.
    void TrySync()
    {
        int count = 0;
        float acc = 0f;
        foreach (var kv in _hits)
            if (Time.time - kv.Value.time <= syncWindow) { count++; acc += kv.Value.acc; }
        if (count < _needed) return;
        acc /= count;
        _hits.Clear();
        _sharedSpeed = Mathf.Min(1f, _sharedSpeed + syncGain * Mathf.Lerp(0.4f, 1f, acc));
        _syncs++;
    }
}
