using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    public enum MatchPhase : byte
    {
        Warmup = 0,   // waiting to start
        Playing = 1,  // the survival clock is ticking
        FishWin = 2,  // clock ran out with fish still alive
        SharkWin = 3, // every fish was eaten
    }

    /// <summary>
    /// Server-authoritative match state: the survival countdown the fish must outlast, role
    /// assignment (who is the shark) and win/lose resolution. Completing tasks calls
    /// <see cref="ReduceRemaining"/> to shave time off the clock so the fish win sooner.
    ///
    /// Runs in three modes:
    ///   * Host / dedicated server - simulates and replicates state through SyncVars.
    ///   * Remote client           - display only; SyncVars arrive from the server.
    ///   * Offline                  - no NetworkManager running (e.g. the FishControls sandbox):
    ///                                simulates locally so the round can be tested without hosting.
    ///
    /// Place ONE instance (it carries a NetworkIdentity) in each gameplay scene. Mirror auto-spawns
    /// scene-placed NetworkIdentities when the server starts, so no manual spawn code is needed.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        [Header("Round")]
        [Tooltip("How long the fish must survive, in seconds (600 = 10 min, 900 = 15 min).")]
        [SerializeField] float roundDurationSeconds = 600f;
        [Tooltip("Auto-start the round this many seconds after the scene is ready. Negative = wait for StartRound().")]
        [SerializeField] float autoStartDelay = 2f;

        [Header("Debug")]
        [SerializeField] bool logTransitions = true;

        [SyncVar] double _roundEndTime;
        [SyncVar] double _roundStartTime;
        [SyncVar(hook = nameof(OnPhaseChanged))] MatchPhase _phase = MatchPhase.Warmup;

        // Every living/dead fish registers here so the server knows when they are all gone.
        static readonly HashSet<FishVitals> Fish = new HashSet<FishVitals>();
        double _autoStartAt = -1;

        public MatchPhase Phase => _phase;
        public bool IsPlaying => _phase == MatchPhase.Playing;
        public float RoundDuration => roundDurationSeconds;

        /// <summary>No NetworkManager running at all -> this instance is the local authority.</summary>
        public static bool Offline => !NetworkServer.active && !NetworkClient.active;
        bool Authority => Offline || isServer;

        // Networked play shares the server clock; offline falls back to local time.
        static double Now => (NetworkServer.active || NetworkClient.active)
            ? NetworkTime.time
            : Time.timeAsDouble;

        /// <summary>Seconds left on the survival clock (0 once the round is decided).</summary>
        public float RemainingSeconds
        {
            get
            {
                if (_phase == MatchPhase.Warmup) return roundDurationSeconds;
                if (_phase != MatchPhase.Playing) return 0f;
                return Mathf.Max(0f, (float)(_roundEndTime - Now));
            }
        }

        void Awake() => Instance = this; // one per scene; last writer wins

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (autoStartDelay >= 0f) _autoStartAt = Now + autoStartDelay;
        }

        void Start()
        {
            // Offline sandbox: OnStartServer never fires, so arm the auto-start here instead.
            if (Offline && autoStartDelay >= 0f) _autoStartAt = Now + autoStartDelay;
        }

        void Update()
        {
            if (!Authority) return;

            if (_phase == MatchPhase.Warmup && _autoStartAt >= 0 && Now >= _autoStartAt)
                StartRound();

            if (_phase == MatchPhase.Playing && Now >= _roundEndTime)
                EndRound(MatchPhase.FishWin);
        }

        /// <summary>Begin the survival round: assign roles and start the clock.</summary>
        public void StartRound()
        {
            if (!Authority || _phase == MatchPhase.Playing) return;
            AssignRoles();
            _roundStartTime = Now;
            _roundEndTime = Now + roundDurationSeconds;
            SetPhase(MatchPhase.Playing);
        }

        /// <summary>Completing a task shaves time off the survival clock. Authority-only.</summary>
        public void ReduceRemaining(float seconds)
        {
            if (!Authority || _phase != MatchPhase.Playing || seconds <= 0f) return;
            _roundEndTime -= seconds;
            double min = Now + 1.0;              // never let a single task instantly end the round
            if (_roundEndTime < min) _roundEndTime = min;
            if (logTransitions) Debug.Log($"[Match] Task shaved {seconds:0.#}s -> {RemainingSeconds:0}s left");
        }

        /// <summary>Called by a fish when it dies; if none are left alive the shark wins. Authority-only.</summary>
        public void NotifyFishDied(FishVitals who)
        {
            if (!Authority || _phase != MatchPhase.Playing) return;
            foreach (var f in Fish)
                if (f != null && !f.IsDead && !f.TryGetComponent(out SharkAbilities _)) return; // a fish is still alive (sharks have vitals too, but don't count)
            EndRound(MatchPhase.SharkWin);
        }

        void EndRound(MatchPhase result)
        {
            SetPhase(result);
            if (logTransitions) Debug.Log($"[Match] Round over: {result}");
        }

        /// <summary>
        /// Who is the shark is decided by the body they spawned in: CustomNetworkManager draws the shark
        /// and gives them the shark prefab (and F8 swaps bodies). Rolling roles again here used to leave
        /// players with a shark body but a Fish role (or vice versa), so just make each Role match.
        /// </summary>
        void AssignRoles()
        {
            // Offline sandbox keeps whatever roles the scene objects were authored with.
            if (Offline || !isServer) return;

            int players = 0, sharks = 0;
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn?.identity == null || !conn.identity.TryGetComponent(out FishPlayer fp)) continue;
                bool shark = fp.TryGetComponent(out SharkAbilities _);
                fp.Role = shark ? FishRole.Shark : FishRole.Fish;
                players++;
                if (shark) sharks++;
            }

            if (logTransitions) Debug.Log($"[Match] Round start: {sharks} shark(s) / {players} players");
        }

        void SetPhase(MatchPhase p)
        {
            MatchPhase prev = _phase;
            _phase = p;                       // SyncVar; hook fires on remote clients
            if (Offline) OnPhaseChanged(prev, p); // no replication offline, so fire it ourselves
        }

        void OnPhaseChanged(MatchPhase _, MatchPhase now)
        {
            if (logTransitions) Debug.Log($"[Match] Phase -> {now}");
        }

        public static void Register(FishVitals v) => Fish.Add(v);
        public static void Unregister(FishVitals v) => Fish.Remove(v);
    }
}
