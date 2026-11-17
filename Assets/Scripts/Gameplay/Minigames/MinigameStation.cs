using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// A spot on the reef with a minigame (the leak, bleached coral, the tangled line). Swim into its
    /// trigger and press E (<see cref="FishInteract"/>): your fish stops and faces it, the camera turns to
    /// it, and the minigame's overlay opens (<see cref="MinigameHud"/>). Moving (WASD / stick / D-pad /
    /// down), pressing E again, dying or drifting off ends it. Solve it and the server checks you're
    /// really there, marks it done for everyone, takes time off the survival clock and tells the shark
    /// where (<see cref="MatchManager.TaskCompleted"/>) - finishing a task is loud.
    ///
    /// Subclasses build their own look (<see cref="BuildLooks"/>) - they're made in code, not prefabs
    /// (<see cref="MinigameStations"/> spawns them) - and run the game itself (<see cref="BeginGame"/> /
    /// <see cref="TickGame"/>), calling <see cref="Solve"/> when it's won.
    /// </summary>
    public abstract class MinigameStation : NetworkBehaviour, IInteractable
    {
        /// <summary>Every station in the world.</summary>
        public static readonly List<MinigameStation> All = new List<MinigameStation>();

        /// <summary>This machine's player is at (or playing) a station: E belongs to it.</summary>
        public static bool LocalNearAny => s_nearFrame >= Time.frameCount - 1;
        static int s_nearFrame = -10;
        static MinigameStation s_active;

        [SyncVar(hook = nameof(OnDoneChanged))] bool _done;
        public bool Done => _done;

        // ---- set by subclasses ----
        protected abstract string Layout { get; }          // Resources/Hud/Minigames/<Layout>.uxml
        protected abstract string Prompt { get; }          // "seal the leak" -> "Press E to seal the leak"
        protected abstract string TaskName { get; }        // shown to everyone (and the shark) when done
        protected virtual float RewardSeconds => 20f;      // off the survival clock
        protected virtual float TriggerRadius => 2.6f;
        protected virtual bool FreeCursor => false;
        protected virtual bool TakesMouse => false;
        protected virtual float MaxUseDistance => 8f;
        protected virtual bool UsesSideKeys => false;      // the game itself reads A / D
        protected virtual string DoneWord => "DONE";       // the shout when it's won

        /// <summary>Fills in the station's look (on every machine). Called once, before the first frame.</summary>
        protected abstract void BuildLooks();
        /// <summary>Done / not done (also on late joiners).</summary>
        protected virtual void ShowDone(bool done) { }
        /// <summary>The overlay is open: query it and reset the game.</summary>
        protected abstract void BeginGame(VisualElement ui);
        /// <summary>Every frame while playing.</summary>
        protected abstract void TickGame(float dt);
        /// <summary>The game was left (solved or not).</summary>
        protected virtual void EndGame() { }

        protected FishPlayer Fish { get; private set; }
        protected bool Playing { get; private set; }
        protected bool Offline => !NetworkServer.active && !NetworkClient.active;

        bool _focused, _built;
        float _startedAt;

        public bool CanInteract => !_done;
        public bool OnFocus => _focused;
        public bool IsBusy => Playing;

        protected virtual void Awake()
        {
            var trigger = GetComponent<SphereCollider>();
            if (trigger == null) trigger = gameObject.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = TriggerRadius;
        }

        void Start()
        {
            if (!_built) { _built = true; BuildLooks(); ShowDone(_done); }
        }

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable()
        {
            All.Remove(this);
            if (Playing) Leave("disabled");
        }

        void OnDoneChanged(bool _, bool done)
        {
            if (_built) ShowDone(done);
            if (done && Playing) Leave();
        }

        protected virtual void Update()
        {
            if (_focused || Playing) s_nearFrame = Time.frameCount;
            if (_focused && !Playing && !_done && s_active == null)
                MatchHud.ShowPrompt($"Press {GameInput.Interact.Token} to {Prompt}");
            if (!Playing) return;

            if (_done || Fish == null || !Fish.isActiveAndEnabled || Fish.IsOut || Fish.IsBeingEaten ||
                (Fish.TryGetComponent(out FishVitals v) && v.IsDead) ||
                (Fish.transform.position - transform.position).sqrMagnitude > MaxUseDistance * MaxUseDistance)
            {
                Leave(_done ? "done" : "fish gone, dead or too far");
                return;
            }
            if (Time.unscaledTime - _startedAt > 0.3f && PlayerWantsToMove(UsesSideKeys)) { Leave("swam off"); return; }

            // Face the station while parked (the body is kinematic, controls off).
            Vector3 look = transform.position - Fish.transform.position;
            if (look.sqrMagnitude > 1e-3f)
                Fish.transform.rotation = Quaternion.Slerp(Fish.transform.rotation,
                    Quaternion.LookRotation(look.normalized, Vector3.up), 1f - Mathf.Exp(-6f * Time.deltaTime));

            GameInput.ReportPuzzle();   // its keys go to the game, not to swimming
            if (PauseMenu.IsOpen) return;
            TickGame(Mathf.Min(Time.deltaTime, 0.05f));
        }

        // ------------------------------------------------------------------ IInteractable

        public void Focus() => _focused = true;

        public void LoseFocus()
        {
            _focused = false;
            if (Playing) Leave("lost focus");
        }

        public void Interact()
        {
            if (Playing) Leave("pressed E");
            else if (!_done) Join();
        }

        void Join()
        {
            Fish = LocalFish();
            if (Fish == null) return;
            if (s_active != null && s_active != this) s_active.Leave("another station");
            var ui = MinigameHud.Open(Layout, FreeCursor, TakesMouse);
            if (ui == null) { Fish = null; return; }

            s_active = this;
            Playing = true;
            _startedAt = Time.unscaledTime;
            MatchLog.Write("start", TaskName);
            Fish.SetTaskLocked(true);
            foreach (var cam in FindObjectsByType<FishOrbitCamera>(FindObjectsInactive.Exclude))
                if (cam.Target == Fish.transform) cam.TurnToward(transform.position - Fish.transform.position, 30f);   // look down on it, under the panel
            BeginGame(ui);
        }

        protected void Leave(string why = "")
        {
            if (!Playing) return;
            if (!_done)
            {
                Debug.Log($"[Minigame] {name}: left ({why}).");
                MatchLog.Write("left", TaskName, Time.unscaledTime - _startedAt, why);
            }
            Playing = false;
            EndGame();
            MinigameHud.Close();
            if (Fish != null) Fish.SetTaskLocked(false);
            Fish = null;
            if (s_active == this) s_active = null;
        }

        /// <summary>The local player won: the server checks and finishes it for everyone.</summary>
        protected void Solve()
        {
            if (_done) return;
            MatchLog.Write("solved", TaskName, Time.unscaledTime - _startedAt);
            if (Offline) ServerSolve();
            else CmdSolve();
            MinigameHud.Flash(DoneWord, true);
        }

        [Command(requiresAuthority = false)]
        void CmdSolve(NetworkConnectionToClient sender = null)
        {
            if (sender == null || sender.identity == null) return;
            if ((sender.identity.transform.position - transform.position).sqrMagnitude > MaxUseDistance * MaxUseDistance) return;
            ServerSolve();
        }

        void ServerSolve()
        {
            if (_done) return;
            _done = true;
            if (Offline) OnDoneChanged(false, true);   // no SyncVar hook offline
            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.ReduceRemaining(RewardSeconds);
                MatchManager.Instance.TaskCompleted(transform.position, TaskName);
            }
            Debug.Log($"[Minigame] {name}: {TaskName} - {RewardSeconds:0} s off the clock.");
        }

        // ------------------------------------------------------------------ helpers

        static FishPlayer LocalFish()
        {
            foreach (var p in TrashPusher.All)
                if (p != null && p.IsLocalFish && p.TryGetComponent(out FishPlayer fish)) return fish;
            return null;
        }

        /// <summary>Swimming off (WASD / stick / down) leaves a minigame. <paramref name="sideKeysTaken"/>: the
        /// game itself uses A / D, so only forward / back (W / S) and the stick's up / down count.</summary>
        public static bool PlayerWantsToMove(bool sideKeysTaken = false)
        {
            Vector2 keys = GameInput.Move.ReadPointer();
            if (Mathf.Abs(keys.y) > 0.45f || (!sideKeysTaken && Mathf.Abs(keys.x) > 0.45f)) return true;
            if (GameInput.Descend.IsPressed()) return true;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad == null) return false;
            Vector2 stick = pad.leftStick.ReadValue();
            return Mathf.Abs(stick.y) > 0.5f || (!sideKeysTaken && Mathf.Abs(stick.x) > 0.5f);
        }

        /// <summary>A primitive child with the given material (no collider), for building looks in code.</summary>
        protected Transform Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale,
                                 Material material, Quaternion? localRot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = localScale;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }
    }
}
