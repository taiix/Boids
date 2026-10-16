using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    public enum MatchPhase : byte
    {
        Warmup = 0,   // waiting for every player to load in
        Playing = 1,  // the survival clock is ticking
        FishWin = 2,  // round over: more than half the fish escaped
        SharkWin = 3, // round over: half or more of the fish didn't make it
        Escape = 4,   // the clock ran out: a wall has lifted and the fish make a run for it
    }

    /// <summary>Something that happens to the whole reef mid-round (<see cref="WorldEvents"/>).</summary>
    public enum WorldEventKind : byte
    {
        None = 0,
        Current = 1, // a strong current pushes everyone one way
        Trawler = 2, // a trawler drags a net across the arena
        Nightfall = 3, // the reef goes dark; anything moving leaves a glowing plankton trail
        Storm = 4,     // lightning flashes light the reef; the water churns near the surface
        ColdLayer = 5, // a band of cold water slows everyone inside it
        Jellyfish = 6, // a swarm drifts through; touching one stings
        SandSlide = 7, // a quake raises sand clouds along the seabed
        RedTide = 8,   // a toxic patch drifts across; inside it you lose health and can't see far
    }

    /// <summary>
    /// Server-authoritative round: the fish survive a countdown (tasks shave time off it), then one of the
    /// walls around the seabed lifts (<see cref="ArenaWalls"/>) and they have to swim out through the gap.
    /// From that moment the shark sees every fish. The round ends once no fish is left in the water -
    /// each one escaped, was eaten, or died - and the fish win if more than half of them escaped.
    /// Every player's outcome is kept in <see cref="Roster"/> for the results screen.
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

        /// <summary>
        /// Survival clock chosen by the host in the lobby, in seconds. When set (> 0) the server uses it
        /// instead of <see cref="roundDurationSeconds"/>; it lives in a static so it survives the scene load.
        /// </summary>
        public static float RoundSecondsOverride = -1f;

        [Header("Round")]
        [Tooltip("How long the fish must survive before the exit opens, in seconds (600 = 10 min).")]
        [SerializeField] float roundDurationSeconds = 600f;
        [Tooltip("Offline sandbox only: start the round this many seconds after the scene is ready. Negative = " +
                 "wait for StartRound(). Online the clock starts when every player has loaded in.")]
        [SerializeField] float autoStartDelay = 2f;

        [Header("Testing")]
        [Tooltip("Development builds and the editor only: the host presses this to run the clock out now, so " +
                 "the escape can be tested without waiting.")]
        [SerializeField] Key skipClockKey = Key.F7;

        [Header("Debug")]
        [SerializeField] bool logTransitions = true;

        [SyncVar] double _roundEndTime;
        [SyncVar] double _roundStartTime;
        [SyncVar(hook = nameof(OnPhaseChanged))] MatchPhase _phase = MatchPhase.Warmup;
        // Which wall lifted (ArenaWalls index, -1 = none yet) and when, on the shared clock.
        [SyncVar] sbyte _exitWall = -1;
        [SyncVar] double _exitOpenedAt;
        // The world event (WorldEvents decides; everyone plays it from these): what, when, which way, and one
        // number more (the trawler's starting wall).
        [SyncVar] WorldEventKind _event;
        [SyncVar] double _eventStart;
        [SyncVar] double _eventEnd;
        [SyncVar] Vector3 _eventDir;
        [SyncVar] int _eventParam;

        // One line per player for the results screen. Server-side, each player's line is found by
        // their connection, so a new body (F8 swap) updates the same line.
        readonly SyncList<RoundPlayer> _roster = new SyncList<RoundPlayer>();
        readonly Dictionary<int, int> _rosterLine = new Dictionary<int, int>();

        // Every living/dead fish registers here so the offline sandbox knows when they are all gone.
        static readonly HashSet<FishVitals> Fish = new HashSet<FishVitals>();
        double _autoStartAt = -1;

        public MatchPhase Phase => _phase;
        public bool IsPlaying => _phase == MatchPhase.Playing;
        /// <summary>The round is underway: the clock is ticking or the fish are escaping.</summary>
        public bool RoundRunning => _phase == MatchPhase.Playing || _phase == MatchPhase.Escape;
        public bool IsOver => _phase == MatchPhase.FishWin || _phase == MatchPhase.SharkWin;
        /// <summary>The exit is open: every fish is shown to the shark, wherever it is.</summary>
        public bool FishRevealed => _phase == MatchPhase.Escape;
        public float RoundDuration => roundDurationSeconds;
        /// <summary>The wall that lifted (an <see cref="ArenaWalls"/> index), or -1.</summary>
        public int ExitWall => _exitWall;
        /// <summary>When the exit started opening, on <see cref="Now"/>'s clock.</summary>
        public double ExitOpenedAt => _exitOpenedAt;
        /// <summary>When the survival clock started, on <see cref="Now"/>'s clock.</summary>
        public double RoundStartTime => _roundStartTime;
        public WorldEventKind Event => _event;
        public double EventStart => _eventStart;
        public double EventEnd => _eventEnd;
        public Vector3 EventDirection => _eventDir;
        public int EventParam => _eventParam;

        /// <summary>Authority: start a world event (it's announced at once and plays from <paramref name="start"/>
        /// to <paramref name="end"/>).</summary>
        public void ServerStartEvent(WorldEventKind kind, double start, double end, Vector3 direction, int param)
        {
            if (!Authority) return;
            _event = kind;
            _eventStart = start;
            _eventEnd = end;
            _eventDir = direction;
            _eventParam = param;
            if (logTransitions) Debug.Log($"[Match] World event: {kind} in {start - Now:0.#}s for {end - start:0}s");
        }

        /// <summary>Every player in this round and how it went for them.</summary>
        public IReadOnlyList<RoundPlayer> Roster => _roster;

        /// <summary>No NetworkManager running at all -> this instance is the local authority.</summary>
        public static bool Offline => !NetworkServer.active && !NetworkClient.active;
        bool Authority => Offline || isServer;

        /// <summary>The round's clock: the server's when networked (the same on every machine), local time offline.</summary>
        public static double Now => (NetworkServer.active || NetworkClient.active)
            ? NetworkTime.time
            : Time.timeAsDouble;

        /// <summary>Seconds left on the survival clock (0 once the exit is open).</summary>
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
            if (RoundSecondsOverride > 0f) roundDurationSeconds = RoundSecondsOverride;
            if (autoStartDelay >= 0f) _autoStartAt = Now + autoStartDelay;
        }

        void Start()
        {
            // Offline sandbox: nobody has to load in, so arm the auto-start here.
            if (Offline && autoStartDelay >= 0f) _autoStartAt = Now + autoStartDelay;
        }

        void Update()
        {
            if (!Authority) return;

            // Online the clock starts the moment every player has loaded (when the launch overlay lifts).
            if (_phase == MatchPhase.Warmup && (Offline ? _autoStartAt >= 0 && Now >= _autoStartAt : FishPlayer.ServerRoundLive))
                StartRound();

            if (_phase == MatchPhase.Playing && (Now >= _roundEndTime || SkipClockPressed()))
                OpenExit();

            if (_phase == MatchPhase.Escape)
                ServerCheckEscapes();
        }

        bool SkipClockPressed() =>
            Debug.isDebugBuild && Keyboard.current != null && Keyboard.current[skipClockKey].wasPressedThisFrame;

        /// <summary>Begin the survival round: assign roles and start the clock.</summary>
        public void StartRound()
        {
            if (!Authority || _phase != MatchPhase.Warmup) return;
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

        /// <summary>Every machine: the fish finished a task at this spot (what it was, for a label). The shark
        /// is shown where, so finishing a task is a risk as well as time off the clock.</summary>
        public static event System.Action<Vector3, string> TaskDone;

        /// <summary>A task was completed at <paramref name="where"/>. Authority-only; tells every machine.</summary>
        public void TaskCompleted(Vector3 where, string what)
        {
            if (!Authority || !RoundRunning) return;
            if (logTransitions) Debug.Log($"[Match] Task done: {what}");
            if (Offline) TaskDone?.Invoke(where, what);
            else RpcTaskCompleted(where, what);
        }

        [ClientRpc]
        void RpcTaskCompleted(Vector3 where, string what) => TaskDone?.Invoke(where, what);

        // ------------------------------------------------------------------ the escape

        /// <summary>The clock ran out: lift a random wall. From here on the fish can leave through it.</summary>
        void OpenExit()
        {
            var walls = ArenaWalls.Instance;
            if (walls == null || walls.Count == 0)
            {
                // Nothing to lift in this scene, so running the clock out is the escape.
                Debug.LogWarning("[Match] No ArenaWalls in this scene - every fish still alive escapes.");
                SetPhase(MatchPhase.Escape);
                foreach (var fp in FishPlayer.All)
                    if (InTheWater(fp)) ServerSetOutcome(fp, PlayerOutcome.Escaped);
                return;
            }

            _exitWall = (sbyte)Random.Range(0, walls.Count);
            _exitOpenedAt = Now;
            SetPhase(MatchPhase.Escape);
            if (logTransitions) Debug.Log($"[Match] Time's up - the {walls.NameOf(_exitWall)} wall is lifting.");
            CheckRoundOver(); // a round with no fish left in it ends here
        }

        // A fish that has swum out past where the open wall stood has escaped.
        void ServerCheckEscapes()
        {
            var walls = ArenaWalls.Instance;
            if (walls == null || _exitWall < 0) return;
            foreach (var fp in FishPlayer.All)
                if (InTheWater(fp) && walls.IsPastExit(_exitWall, fp.transform.position))
                    ServerSetOutcome(fp, PlayerOutcome.Escaped);
        }

        static bool InTheWater(FishPlayer fp) => fp != null && !fp.IsSharkBody && !fp.IsOut && !fp.IsBeingEaten;

        // ------------------------------------------------------------------ who's still in

        /// <summary>
        /// Server: a body was spawned for a player (round start, late join, F8 swap). Notes who they are and
        /// which side they're on. A swap keeps the same line; someone already out stays out.
        /// </summary>
        public void ServerTrackPlayer(FishPlayer fp)
        {
            if (!NetworkServer.active || fp == null || fp.connectionToClient == null || IsOver) return;
            int conn = fp.connectionToClient.connectionId;
            var line = new RoundPlayer
            {
                name = fp.PlayerName,
                role = fp.IsSharkBody ? FishRole.Shark : FishRole.Fish,
                outcome = PlayerOutcome.Alive,
            };
            if (_rosterLine.TryGetValue(conn, out int i))
            {
                if (_roster[i].outcome != PlayerOutcome.Alive) return;
                _roster[i] = line;
            }
            else
            {
                _rosterLine[conn] = _roster.Count;
                _roster.Add(line);
            }
        }

        /// <summary>Server: a player told us their (Steam) name.</summary>
        public void ServerRenamePlayer(FishPlayer fp)
        {
            if (!NetworkServer.active || fp == null || fp.connectionToClient == null) return;
            if (!_rosterLine.TryGetValue(fp.connectionToClient.connectionId, out int i)) return;
            var line = _roster[i];
            line.name = fp.PlayerName;
            _roster[i] = line;
        }

        /// <summary>
        /// Server: this player is out of the round - escaped, eaten or died. Their body goes (everyone),
        /// they start spectating (them), and if that was the last fish in the water the round ends.
        /// </summary>
        public void ServerSetOutcome(FishPlayer fp, PlayerOutcome outcome)
        {
            if (!NetworkServer.active || fp == null || fp.IsOut || IsOver || outcome == PlayerOutcome.Alive) return;
            fp.Outcome = outcome; // SyncVar: its hook hides the body / starts spectating on every machine
            if (fp.connectionToClient != null) SetOutcome(fp.connectionToClient.connectionId, outcome);
            if (logTransitions) Debug.Log($"[Match] {fp.PlayerName}: {outcome}");
            CheckRoundOver();
        }

        /// <summary>Server: a player disconnected. They no longer count for either side.</summary>
        public void ServerPlayerLeft(int connectionId)
        {
            if (!NetworkServer.active || IsOver) return;
            if (!_rosterLine.TryGetValue(connectionId, out int i) || _roster[i].outcome != PlayerOutcome.Alive) return;
            SetOutcome(connectionId, PlayerOutcome.Left);
            CheckRoundOver();
        }

        void SetOutcome(int connectionId, PlayerOutcome outcome)
        {
            if (!_rosterLine.TryGetValue(connectionId, out int i)) return;
            var line = _roster[i];
            line.outcome = outcome;
            _roster[i] = line;
        }

        /// <summary>Called by a fish or the shark when it dies (starving, cuts). Authority-only.</summary>
        public void NotifyFishDied(FishVitals who)
        {
            if (!Authority || !RoundRunning || who == null) return;
            bool shark = who.TryGetComponent(out SharkAbilities _);

            if (Offline)
            {
                // No players to keep track of in the sandbox: the fish win if the shark starves, the shark
                // once no fish has vitals left.
                if (shark) { EndRound(MatchPhase.FishWin); return; }
                foreach (var f in Fish)
                    if (f != null && !f.IsDead && !f.TryGetComponent(out SharkAbilities _)) return;
                EndRound(MatchPhase.SharkWin);
                return;
            }

            if (!who.TryGetComponent(out FishPlayer fp)) return;
            // A starving shark lets go of its catch first, so the fish isn't left stuck in a dead shark's jaws.
            if (shark) fp.ServerReleasePrey();
            ServerSetOutcome(fp, PlayerOutcome.Died);
        }

        /// <summary>
        /// The round is over once no fish is left in the water, or no shark is left to hunt them (it starved or
        /// quit - every fish still in the water then survived). Escaping or surviving counts the same: more
        /// than half of the fish making it is a win for them; anything less, the shark's. Players who left
        /// don't count either way.
        /// </summary>
        void CheckRoundOver()
        {
            if (!RoundRunning) return;
            int fish = 0, inWater = 0, escaped = 0, sharks = 0, sharksIn = 0;
            bool anyFish = false;
            foreach (var p in _roster)
            {
                if (p.role == FishRole.Shark)
                {
                    sharks++;
                    if (p.outcome == PlayerOutcome.Alive) sharksIn++;
                    continue;
                }
                anyFish = true;
                if (p.outcome == PlayerOutcome.Left) continue;
                fish++;
                if (p.outcome == PlayerOutcome.Alive) inWater++;
                else if (p.outcome == PlayerOutcome.Escaped || p.outcome == PlayerOutcome.Survived) escaped++;
            }

            // The shark is gone (and there was one - not a lone fish testing): whoever is still swimming made it.
            bool sharkGone = sharks > 0 && sharksIn == 0;
            if (sharkGone && inWater > 0)
            {
                ServerMarkSurvivors();
                escaped += inWater;
                inWater = 0;
            }
            // No fish at all (a lone shark testing), or some are still in the water - nothing to decide.
            if (!anyFish || inWater > 0) return;

            EndRound(escaped * 2 > fish ? MatchPhase.FishWin : MatchPhase.SharkWin);
            if (logTransitions)
                Debug.Log(sharkGone ? $"[Match] No shark left - {escaped} of {fish} fish made it."
                                    : $"[Match] {escaped} of {fish} fish escaped.");
        }

        // No shark left: every fish still in the water survived the round. Set directly rather than through
        // ServerSetOutcome, which would check the round again from inside CheckRoundOver.
        void ServerMarkSurvivors()
        {
            foreach (var fp in FishPlayer.All)
            {
                if (fp == null || fp.IsSharkBody || fp.IsOut) continue;
                fp.Outcome = PlayerOutcome.Survived;
                if (fp.connectionToClient != null) SetOutcome(fp.connectionToClient.connectionId, PlayerOutcome.Survived);
            }
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
                bool shark = fp.IsSharkBody;
                fp.Role = shark ? FishRole.Shark : FishRole.Fish;
                ServerTrackPlayer(fp);
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
