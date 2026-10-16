using System.Collections.Generic;
using FishGame;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(FishController))]
public class FishPlayer : NetworkBehaviour
{
    private FishController _fishController;
    private FishFlockBlend _flockBlend;
    public GameObject camera;

    // Assigned by MatchManager on the server at round start. The hook is where the shark
    // milestone will swap abilities/visuals; for now it just carries the role.
    [SyncVar(hook = nameof(OnRoleChanged))]
    public FishRole Role = FishRole.Fish;

    public bool IsShark => Role == FishRole.Shark;

    /// <summary>What this body can do, regardless of the Role value (the offline sandbox shark keeps its
    /// authored role).</summary>
    public bool IsSharkBody => TryGetComponent(out SharkAbilities _);

    /// <summary>
    /// How this player's round is going. Set by the server (MatchManager) when they escape, get eaten or
    /// die. Everyone then hides the body; the player themselves starts spectating the others.
    /// </summary>
    [SyncVar(hook = nameof(OnOutcomeChanged))]
    public PlayerOutcome Outcome = PlayerOutcome.Alive;

    /// <summary>Out of the round: escaped, eaten or dead.</summary>
    public bool IsOut => Outcome != PlayerOutcome.Alive;

    /// <summary>The name shown on the results screen and when spectating: their Steam name, else "Player n".</summary>
    [SyncVar] public string PlayerName = "";

    // Server: each connection's name, kept across new bodies (F8 swaps) and rounds.
    private static readonly Dictionary<int, string> s_Names = new Dictionary<int, string>();

    // Every body in the scene (players and their copies), for the round, the spectator and the shark.
    private static readonly List<FishPlayer> s_All = new List<FishPlayer>();
    public static IReadOnlyList<FishPlayer> All => s_All;

    // How long a dead fish (starved, cut) or a swallowed one stays in view before it goes.
    private const float DeadBodySeconds = 2f;
    private const float SwallowSeconds = 1.2f;

    [Header("Eating")]
    [Tooltip("How close a food pellet must be to the mouth to be eaten.")]
    [SerializeField] float eatRange = 1.6f;
    [Tooltip("Forward distance from the fish pivot to its 'mouth'.")]
    [SerializeField] float mouthOffset = 1.0f;
    [Tooltip("Extra reach the host allows a remote player's bite: that player sees the pellet (and the " +
             "host sees their fish) a moment late, so an exact check would miss bites that looked fine.")]
    [SerializeField] float remoteEatSlack = 1.5f;
    private InputAction _eatAction;

    private CreatureVision _vision;

    // Whether this machine's input currently drives this body: local player AND the round is live.
    private bool _controlsLive;

    // Offline sandbox: no host/client is running and DevHost re-enabled this scene-placed body, so we
    // self-drive as the local player here (Mirror's OnStart* callbacks never fire offline).
    private bool _offlineLocal;

    // In the shark's jaws: controls stay off and the body rides its mouth - while it struggles to break
    // free, and after it's swallowed. Set on the server when it accepts the bite and on clients by the RPC.
    private bool _eaten;
    public bool IsBeingEaten => _eaten;

    [Header("Caught by the shark")]
    [Tooltip("How long the shark shakes a caught fish before swallowing it (seconds) - the time it has to break free.")]
    [SerializeField] float struggleSeconds = 4.5f;
    [Tooltip("How much one press fills the break-free meter (free at 1).")]
    [SerializeField] float struggleGainPerPress = 0.1f;
    [Tooltip("Presses per second that only just hold the meter where it is: x at full health, y near death. " +
             "Mash faster than that to fill it - the weaker the fish, the faster it has to mash.")]
    [SerializeField] Vector2 struggleHoldRate = new Vector2(3.5f, 9f);
    [Tooltip("Damage taken tearing free of the jaws (it bleeds as well). It never kills on its own: at least " +
             "Break Free Min Health is left.")]
    [SerializeField] float breakFreeDamage = 30f;
    [SerializeField] float breakFreeMinHealth = 5f;
    [Tooltip("How fast a fish that breaks free shoots out of the jaws (m/s).")]
    [SerializeField] float breakFreeSpeed = 10f;
    [Tooltip("Seconds the shark can't bite again after a fish breaks free, so it gets a head start.")]
    [SerializeField] float sharkStunSeconds = 1.5f;

    [Tooltip("How long the shark shakes a fish caught while still wounded from a graze before swallowing it. " +
             "It can't tear free.")]
    [SerializeField] float woundedSwallowSeconds = 1.5f;

    // The shark holding us (server: who to swallow into / release from), and the break-free meter, 0..1.
    [SyncVar] private uint _heldBy;
    [SyncVar] private float _struggle;
    // Caught while wounded from a graze: no tearing free this time.
    [SyncVar] private bool _noEscape;
    private double _woundedUntil;       // server, shared clock: until when a graze leaves us too weak to tear free
    private float _nextGrazeTime;       // server, on the shark: one graze per bite at most
    private double _swallowAt;          // shared clock; set by the host, everyone gets it with the catch
    private bool _inJaws;               // every machine: caught and still struggling (not yet swallowed)
    private float _lastStrugglePress;   // server: presses are capped at ~20 a second
    private InputAction _struggleAction;
    private const float MinStruggleGap = 0.05f;
    private float _nextSeabedCheck;
    private float _underSince = -1f;   // when the body was first found under the seabed (-1: it isn't)

    /// <summary>Caught and struggling in the shark's jaws (until it's swallowed or breaks free).</summary>
    public bool IsHeld => _inJaws;
    /// <summary>The break-free meter, 0..1 (free when full).</summary>
    public float StruggleMeter => _struggle;
    /// <summary>Seconds left before the shark swallows.</summary>
    public float StruggleSecondsLeft => _inJaws ? Mathf.Max(0f, (float)(_swallowAt - NetworkTime.time)) : 0f;
    public float StruggleSeconds => _noEscape ? woundedSwallowSeconds : struggleSeconds;
    /// <summary>False when we were caught still wounded from a graze: mashing does nothing.</summary>
    public bool CanBreakFree => !_noEscape;
    /// <summary>Local: when we last mashed (for the UI's kick).</summary>
    public float LastStrugglePress { get; private set; } = -10f;

    /// <summary>Presses per second that only hold the meter steady - more the weaker this fish is.</summary>
    public float StruggleHoldRate
    {
        get
        {
            float health = TryGetComponent(out FishVitals v) ? v.HealthNorm : 1f;
            return Mathf.Lerp(struggleHoldRate.y, struggleHoldRate.x, Mathf.Clamp01(health));
        }
    }

    // Parked at a task (e.g. the comms puzzle): controls off until the task lets go. Local only.
    private bool _taskLocked;

    /// <summary>Local player: park the body at a task (controls off) or let it swim again.</summary>
    public void SetTaskLocked(bool locked) => _taskLocked = locked;

    // How far apart shark and fish may be on the server for a bite to count (lag allowance).
    private const float MaxBiteDistance = 6f;
    private bool IsOffline => !NetworkServer.active && !NetworkClient.active;

    /// <summary>
    /// Server-side only: this player's client has spawned its body and got past the heavy first frames
    /// of the game scene. The round is held until every player reports this, so a fast machine doesn't
    /// start swimming while a slow one is still on the loading overlay.
    /// </summary>
    public bool ClientLoaded { get; private set; }

    /// <summary>
    /// Set by the server on every player at once when the last one finishes loading. Controls and the
    /// launch overlay wait on the local player's copy. Spawns after that point start live.
    /// </summary>
    [SyncVar] public bool RoundLive;

    /// <summary>
    /// Which school this player is blended into (FishFlockBlend): 0 = none, else that school's
    /// NetId + 1. Synced so every player's flock sim reacts to them the same way - the host's above
    /// all, since it's the one everyone is corrected toward; if it ignored a hiding player, their
    /// school would be pulled off them. (Position and the shrink to school size travel with the
    /// NetworkTransform, which syncs scale on PlayerFish.)
    /// </summary>
    [SyncVar(hook = nameof(OnBlendFlockChanged))] public byte BlendFlock;
    private byte _sentBlendFlock;
    private BoidsManager _influencedFlock; // other players' copies of us: the school we registered with

    // Server-side, per game scene: the round has gone live. CustomNetworkManager resets it.
    private static bool s_RoundLive;

    /// <summary>Server: every player has loaded into the game scene (the round's clock may start).</summary>
    public static bool ServerRoundLive => s_RoundLive;

    private void OnRoleChanged(FishRole previous, FishRole current)
    {
        // TODO (shark milestone): enable SharkAbilities / swap model when current == Shark.

        // Only the local player has a camera, so this is a no-op on remotes.
        ApplyRoleVision(current);
    }

    private void OnBlendFlockChanged(byte _, byte now)
    {
        // Our own copy registers itself in FishFlockBlend; this is everyone else's view of us.
        if (isLocalPlayer) return;
        StopInfluencing();
        if (now == 0) return;
        _influencedFlock = BoidsManager.ById((byte)(now - 1));
        if (_influencedFlock != null) _influencedFlock.RegisterInfluencer(transform);
    }

    private void StopInfluencing()
    {
        if (_influencedFlock != null) _influencedFlock.UnregisterInfluencer(transform);
        _influencedFlock = null;
    }

    /// <summary>Point the local camera's post-processing and view range at the current role.</summary>
    private void ApplyRoleVision(FishRole role)
    {
        // Deliberately not routed through the `camera` field: it is not assigned on every prefab,
        // and OnStartLocalPlayer may already have detached the camera from us. _vision is cached in
        // Awake, while the camera is still a child, so it is valid either way.
        if (!isLocalPlayer || _vision == null) return;

        _vision.ApplyRole(role);
    }

    private void Awake()
    {
        s_All.Add(this);
        _fishController = GetComponent<FishController>();
        //_fishController.enabled = false;

        // Local-player-only ability; disabled on remotes so they don't react to the key.
        if (TryGetComponent(out _flockBlend))
            _flockBlend.enabled = false;

        // Fish bodies must be edible for the shark's bite to find them (the prefab doesn't carry one).
        if (!TryGetComponent(out SharkAbilities _) && !TryGetComponent(out Edible _))
            gameObject.AddComponent<Edible>();

        // The water's reactions, on every copy so everyone sees them: bubbles and a wake when swimming
        // fast, splashes in and out of the surface, and the shark's violent eating.
        if (!TryGetComponent(out SwimTrail _)) gameObject.AddComponent<SwimTrail>();
        if (!TryGetComponent(out SurfaceSplash _)) gameObject.AddComponent<SurfaceSplash>();
        if (TryGetComponent(out SharkAbilities _) && !TryGetComponent(out SharkEatFx _)) gameObject.AddComponent<SharkEatFx>();

        // Cache now, before OnStartLocalPlayer can unparent the camera.
        _vision = GetComponentInChildren<CreatureVision>(true);
        if (_vision == null && camera != null) _vision = camera.GetComponent<CreatureVision>();

        // Both player prefabs carry their own child camera but leave this field empty. Without it,
        // every spawned copy's camera stays on, so another player's camera renders over ours and
        // its orbit script reads our mouse.
        if (camera == null)
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam != null) camera = cam.gameObject;
        }
    }

    /// <summary>
    /// Runs for every networked copy, including the host's view of other players. The camera,
    /// FishController and SharkAbilities all read this machine's mouse/keyboard, so left on in a
    /// remote copy they hijack our view and steer someone else's body. The motor goes off and the body
    /// kinematic so local physics doesn't fight the owner-authoritative NetworkTransform.
    /// OnStartLocalPlayer (which runs after this) turns it all back on for the one copy we own.
    /// Offline scene-placed creatures never get either callback.
    /// </summary>
    public override void OnStartServer()
    {
        base.OnStartServer();
        // Late joiner (or a respawn) after the round went live: nothing to wait for. Set here so it
        // ships in the spawn payload.
        if (s_RoundLive) RoundLive = true;

        // Who this is (kept per connection, so a new body keeps the name), then their line in the round.
        if (connectionToClient != null)
        {
            int id = connectionToClient.connectionId;
            if (!s_Names.TryGetValue(id, out string name))
            {
                name = $"Player {s_Names.Count + 1}";
                s_Names[id] = name;
            }
            PlayerName = name;
        }
        if (MatchManager.Instance != null) MatchManager.Instance.ServerTrackPlayer(this);
    }

    // Our Steam name, for the results screen; the server makes one up when there's no Steam (editor tests).
    [Command]
    private void CmdSetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Replace("<", "").Replace(">", "").Trim();
        if (name.Length > 24) name = name.Substring(0, 24);
        if (name.Length == 0) return;
        if (connectionToClient != null) s_Names[connectionToClient.connectionId] = name;
        PlayerName = name;
        if (MatchManager.Instance != null) MatchManager.Instance.ServerRenamePlayer(this);
    }

    private static string LocalSteamName()
    {
        try { return SteamManager.Initialized ? Steamworks.SteamFriends.GetPersonaName() : null; }
        catch { return null; }
    }

    /// <summary>Server: forget the previous round's "live" state (new game scene / new host).</summary>
    public static void ServerResetRound() => s_RoundLive = false;

    /// <summary>
    /// Server: make every player's round live once all connections have a body whose client reported
    /// it has loaded. <paramref name="force"/> skips that check (timeout for a client that never loads).
    /// </summary>
    public static void ServerTryStartRound(bool force = false)
    {
        if (!NetworkServer.active || s_RoundLive) return;

        var players = new System.Collections.Generic.List<FishPlayer>();
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn == null) continue;
            // No identity yet = still loading the scene, which holds the round too.
            bool loaded = conn.identity != null && conn.identity.TryGetComponent(out FishPlayer p) && p.ClientLoaded;
            if (!loaded && !force) return;
            if (conn.identity != null && conn.identity.TryGetComponent(out FishPlayer fp)) players.Add(fp);
        }
        if (players.Count == 0) return;

        s_RoundLive = true;
        foreach (var fp in players) fp.RoundLive = true;
        Debug.Log(force ? $"[Net] Round started without waiting for every player ({players.Count} spawned)."
                        : $"[Net] All {players.Count} players loaded; round started.");
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (camera != null) camera.SetActive(false);
        SetLocallyControlled(false);
    }

    // Tracks the state it sets, so whoever turns control off last (OnStartClient can run a frame
    // after Update already turned it on - e.g. after a role swap on the host) is noticed and
    // Update switches it back on.
    private void SetLocallyControlled(bool local)
    {
        _controlsLive = local;
        if (_flockBlend != null) _flockBlend.enabled = local;
        _fishController.enabled = local;
        if (TryGetComponent(out FishMotor motor)) motor.enabled = local;
        if (TryGetComponent(out SharkAbilities shark)) shark.enabled = local;
        if (TryGetComponent(out FishGame.SharkTargeting targeting)) targeting.enabled = local;
        if (TryGetComponent(out Rigidbody rb)) rb.isKinematic = !local;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        // Controls stay off (and the body held still) until the round starts; Update switches them on.
        StartCoroutine(ReportLoadedWhenSettled());
        PrepareLocalControl();

        string steamName = LocalSteamName();
        if (!string.IsNullOrEmpty(steamName) && steamName != PlayerName) CmdSetName(steamName);

        // Our shark's bites have to reach the host so the fish goes for everyone.
        if (TryGetComponent(out SharkAbilities shark))
        {
            shark.CaughtFlockFish += OnCaughtFlockFish;
            shark.CaughtPrey += OnCaughtPrey;
            shark.GrazedPrey += OnGrazedPrey;
            shark.Attacked += CmdShowAttack;
            shark.Sonared += CmdSonar;
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        StopInfluencing();
    }

    /// <summary>
    /// Camera, view grading, orbit follow and the eat action for the copy this machine drives. Shared
    /// by the networked local player (OnStartLocalPlayer) and the offline sandbox (Start), so pressing
    /// Play in a game scene with no host still yields a fully controllable body.
    /// </summary>
    private void PrepareLocalControl()
    {
        if (camera != null)
        {
            // Detach from the fish so the camera follows purely by script. Parenting it to the
            // interpolated Rigidbody causes jitter on fast turns; following via FishOrbitCamera
            // (in LateUpdate) is smooth. The prefab still carries the camera for easy spawning.
            camera.transform.SetParent(null, true);
            camera.SetActive(true);
        }

        // The Role SyncVar usually arrives before this point, when there was no local camera to
        // grade yet, so apply it here too rather than relying on the hook alone.
        ApplyRoleVision(Role);

        var orbit = camera != null ? camera.GetComponent<FishOrbitCamera>() : null;
        if (orbit == null && Camera.main != null) orbit = Camera.main.GetComponent<FishOrbitCamera>();
        if (orbit != null) orbit.SetTarget(transform);

        // Eat action: press E near a food pellet to eat it.
        if (_eatAction == null)
        {
            _eatAction = new InputAction("Eat", InputActionType.Button);
            _eatAction.AddBinding("<Keyboard>/e");
            _eatAction.AddBinding("<Gamepad>/buttonWest");
            _eatAction.Enable();
        }

        // Health / hunger / round clock for whoever we are (fish or shark).
        MatchHud.Show();
    }

    /// <summary>
    /// Offline sandbox entry point. With no host or client running, Mirror's OnStart* callbacks never
    /// fire, so a scene-placed body (which DevHost re-enables) would just sit there inert. Set it up as
    /// the local player here. Networked play skips this - the server/client is active by spawn time.
    /// </summary>
    private void Start()
    {
        if (!IsOffline) return;
        _offlineLocal = true;
        PrepareLocalControl();
        // Update turns controls on via the _controlsLive transition (live is always true offline).
    }

    private void Update()
    {
        if (isServer && _heldBy != 0) ServerStruggleTick();
        if (!isLocalPlayer && !_offlineLocal) return;

        if (_controlsLive && Time.time >= _nextSeabedCheck)
        {
            _nextSeabedCheck = Time.time + 0.1f;
            KeepAboveSeabed();
        }

        // In the jaws: mash to break free.
        if (_inJaws && !_offlineLocal)
        {
            if (_struggleAction == null)
            {
                _struggleAction = new InputAction("BreakFree", InputActionType.Button);
                _struggleAction.AddBinding("<Keyboard>/space");
                _struggleAction.AddBinding("<Gamepad>/buttonSouth");
                _struggleAction.Enable();
            }
            if (_struggleAction.WasPressedThisFrame() && !PauseMenu.IsOpen && CanBreakFree)
            {
                LastStrugglePress = Time.unscaledTime;
                CmdStruggle();
            }
        }

        // Offline we're always live; networked, controls wait for the round to start. Never while
        // we're in the shark's jaws, out of the round, parked at a task, or have a menu / the results open.
        var match = MatchManager.Instance;
        bool live = (_offlineLocal || RoundLive) && !_eaten && !IsOut && !_taskLocked && !PauseMenu.IsOpen
                    && !(match != null && match.IsOver);
        if (live != _controlsLive)
            SetLocallyControlled(live);

        // Fish only (pellets are fish food): prompt when a pellet is in reach - unless we're next to a
        // task, where E joins it (or bites the turtle's rope) instead.
        bool taskOwnsE = NetworkedSequenceTask.LocalNearAnyTask || TangledTurtle.LocalAtRope;
        if (_controlsLive && !IsSharkBody && !taskOwnsE && PelletInReach() != null)
            MatchHud.ShowPrompt("Press E to eat");

        // E eats — unless we're next to a task, where E joins it instead (the task handles that press).
        if (_controlsLive && _eatAction != null && _eatAction.WasPressedThisFrame() && !taskOwnsE)
        {
            if (PelletInReach(_offlineLocal ? 0f : remoteEatSlack) != null && GameAudio.Lib != null)
                GameAudio.Play(GameAudio.Lib.pellet, 0.6f, 0.1f); // instant, on our own screen
            if (_offlineLocal) ServerEatNearestPellet(); // offline: eat directly (no Command/server)
            else CmdEat();
        }

        // Tell everyone when we blend into / out of a school, so their flock sims react to us too.
        if (!_offlineLocal && _flockBlend != null)
        {
            var flock = _flockBlend.ActiveFlock;
            byte blendFlock = flock != null ? (byte)(flock.NetId + 1) : (byte)0;
            if (blendFlock != _sentBlendFlock)
            {
                _sentBlendFlock = blendFlock;
                CmdSetBlendFlock(blendFlock);
            }
        }

        // F8 swaps this player between fish and shark (the server re-spawns us as the other; everyone
        // sees it). The server decides whether that's allowed. Offline there's no server, so we swap here.
        if (_controlsLive && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            if (_offlineLocal) SwapBodyOffline();
            else CmdSwitchRole();
        }
    }

    /// <summary>True for a body driven by the offline sandbox (no host or client running).</summary>
    public bool IsOfflineSandboxBody => _offlineLocal;

    // Offline sandbox F8: what the server does online - a fresh copy of the other body (fresh vitals)
    // in our place, and this one goes (its OnDestroy takes our detached camera with it).
    private void SwapBodyOffline()
    {
        if (!(NetworkManager.singleton is CustomNetworkManager nm)) return;
        FishRole next = IsSharkBody ? FishRole.Fish : FishRole.Shark;
        GameObject prefab = nm.PrefabFor(next);
        if (prefab == null) return;

        var body = Instantiate(prefab, transform.position, transform.rotation);
        if (body.TryGetComponent(out FishPlayer fp)) fp.Role = next; // before its Start sets up the view
        Destroy(gameObject);
    }

    // The spawn lands during the scene's first, very long frames; waiting a few more means "loaded" is
    // reported when this machine is actually rendering, which is also when its overlay starts fading.
    private System.Collections.IEnumerator ReportLoadedWhenSettled()
    {
        for (int i = 0; i < 5; i++) yield return null;
        CmdReportLoaded();
    }

    [Command]
    private void CmdReportLoaded()
    {
        ClientLoaded = true;
        ServerTryStartRound();
    }

    // Server validates that a pellet is actually in mouth range before consuming it.
    [Command]
    private void CmdEat() => ServerEatNearestPellet(isLocalPlayer ? 0f : remoteEatSlack);

    // Eat the nearest pellet in mouth range. Runs on the server for a networked player, or directly on
    // the offline sandbox body (FoodPellet.Consume is offline-capable).
    private void ServerEatNearestPellet(float extraReach = 0f)
    {
        if (IsSharkBody) return; // pellets are fish food; the shark eats fish
        var best = PelletInReach(extraReach);
        if (best != null)
            best.Consume(GetComponent<FishVitals>());
    }

    // The pellet a bite would take right now, or null. Shared by the eat itself and its prompt, so
    // "Press E to eat" only shows when E would actually eat.
    private FoodPellet PelletInReach(float extraReach = 0f)
    {
        Vector3 mouth = transform.position + transform.forward * mouthOffset;
        FoodPellet best = null;
        float reach = eatRange + extraReach;
        float bestSqr = reach * reach;
        foreach (var pellet in FoodPellet.All)
        {
            if (pellet == null || pellet.IsEaten) continue;
            float d = (pellet.transform.position - mouth).sqrMagnitude;
            if (d <= bestSqr) { bestSqr = d; best = pellet; }
        }
        return best;
    }

    [Command]
    private void CmdSetBlendFlock(byte blendFlock) => BlendFlock = blendFlock;

    // Our shark just devoured a flock fish locally; make it disappear for everyone else too.
    private void OnCaughtFlockFish(BoidsManager flock, int fishId)
    {
        if (flock == null) return;
        if (isServer)
        {
            FlockNetSync.ServerAnnounceEaten(flock.NetId, (ushort)fishId, netId); // host: already gone here
            if (TryGetComponent(out SharkAbilities shark)) shark.FeedFor(playerFish: false);
        }
        else CmdEatFlockFish(flock.NetId, (ushort)fishId);
    }

    // Our shark attacked or lunged: everyone else sees the swing and the dash's bubbles (we already did).
    [Command]
    private void CmdShowAttack(bool lunge) => RpcShowAttack(lunge);

    [ClientRpc(includeOwner = false)]
    private void RpcShowAttack(bool lunge)
    {
        if (TryGetComponent(out SharkAbilities shark)) shark.PlayAttackFx(lunge);
    }

    // Our shark sent a sonar pulse (it already saw what it caught): everyone else sees the pulse, and fish it
    // swept over are warned.
    [Command]
    private void CmdSonar() => RpcSonar();

    [ClientRpc(includeOwner = false)]
    private void RpcSonar()
    {
        if (!TryGetComponent(out SharkAbilities shark)) return;
        shark.PlaySonarFx();
        var me = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<FishPlayer>() : null;
        if (me == null || me.IsSharkBody || me.IsOut) return;
        if ((me.transform.position - transform.position).sqrMagnitude <= shark.SonarRadius * shark.SonarRadius)
            MatchHud.Flash("SONAR! The shark sees fish that move - keep still", 2.5f);
    }

    [Command]
    private void CmdEatFlockFish(byte flock, ushort fishId) =>
        FlockNetSync.ServerEatRequest(flock, fishId, netIdentity);

    // Our shark bit something edible. Another player goes through the server (it checks the bite,
    // then everyone sees it); anything else is just eaten here.
    private void OnCaughtPrey(Edible prey)
    {
        if (prey == null) return;
        var victim = prey.GetComponentInParent<FishPlayer>();
        if (victim != null && victim != this) CmdEatPlayer(victim.netId);
        else if (TryGetComponent(out SharkAbilities shark)) prey.Devour(gameObject, shark.MouthAnchor);
    }

    [Command]
    private void CmdEatPlayer(uint victimNetId)
    {
        if (_eaten || !NetworkServer.spawned.TryGetValue(victimNetId, out var identity) ||
            !identity.TryGetComponent(out FishPlayer victim)) return;
        if (victim == this || victim._eaten || victim.IsOut || victim.TryGetComponent(out SharkAbilities _)) return; // only fish in the water
        if ((victim.transform.position - transform.position).sqrMagnitude > MaxBiteDistance * MaxBiteDistance) return;
        var match = MatchManager.Instance;
        if (match != null && match.IsOver) return;
        if (PreyOf(netId) != null) return; // one fish in the jaws at a time

        victim.ServerCatch(this);
    }

    // ---- Trawl net (WorldEvents): caught, we're hurt and carried along for a moment, then it drops us behind it ----

    /// <summary>Authority: the trawl net swept into us. Hurts (fish and shark alike) and tangles us for
    /// <paramref name="seconds"/>, carried at <paramref name="carry"/>; then we're dropped behind the net.</summary>
    public void ServerNetCaught(float damage, float seconds, Vector3 carry)
    {
        if (TryGetComponent(out FishVitals vitals)) vitals.Damage(damage);
        if (IsOffline) Entangled(seconds, carry);
        else if (connectionToClient != null) TargetEntangled(connectionToClient, seconds, carry);
    }

    [TargetRpc]
    private void TargetEntangled(NetworkConnectionToClient target, float seconds, Vector3 carry) => Entangled(seconds, carry);

    // The machine driving this body.
    private void Entangled(float seconds, Vector3 carry)
    {
        if (!TryGetComponent(out FishMotor motor)) return;
        motor.Entangle(seconds, carry);
        if (GameAudio.Lib != null) GameAudio.Play(GameAudio.Lib.netCatch, 0.9f, 0.05f);
        MatchHud.Flash(IsSharkBody ? "CAUGHT IN THE NET!" : "CAUGHT IN THE NET! You're hurt", 2.5f);
        StartCoroutine(DropBehindNet(seconds, carry));
    }

    private System.Collections.IEnumerator DropBehindNet(float seconds, Vector3 carry)
    {
        yield return new WaitForSeconds(seconds);
        if (IsOut || _eaten || carry.sqrMagnitude < 1e-4f) yield break;
        // The net sweeps on; we slip out under its back.
        Vector3 p = transform.position - carry.normalized * 3.5f + Vector3.down * 0.5f;
        p = AboveSeabed(p);
        transform.position = p;
        if (TryGetComponent(out Rigidbody rb)) rb.position = p;
    }

    /// <summary>Authority: a jellyfish stung us (fish and shark alike): hurt, and slowed for a moment.</summary>
    public void ServerStung(float damage, float slowSeconds, float slowFactor)
    {
        if (TryGetComponent(out FishVitals vitals)) vitals.Damage(damage, 6f);
        if (IsOffline) Stung(slowSeconds, slowFactor);
        else if (connectionToClient != null) TargetStung(connectionToClient, slowSeconds, slowFactor);
    }

    [TargetRpc]
    private void TargetStung(NetworkConnectionToClient target, float seconds, float factor) => Stung(seconds, factor);

    private void Stung(float seconds, float factor)
    {
        if (TryGetComponent(out FishMotor motor)) motor.SlowFor(seconds, factor);
        if (GameAudio.Lib != null)
        {
            GameAudio.Play(GameAudio.Lib.sting, 0.8f, 0.05f);
            GameAudio.Play(GameAudio.Lib.stingHit, 0.6f, 0.05f);
        }
        MatchHud.Flash("STUNG BY A JELLYFISH!", 1.8f);
    }

    // ---- Ghost hint: an eaten or dead fish can, once a round, show the shark to the fish still in the water ----

    [Header("Ghost hint")]
    [Tooltip("How long the living fish see the shark (through rocks and fog) when a ghost reveals it.")]
    [SerializeField] float ghostHintSeconds = 3f;
    [SyncVar] private bool _ghostHintUsed;

    /// <summary>Every machine: a ghost revealed this shark to the fish - for how long, and who did it.</summary>
    public static event System.Action<FishPlayer, float, string> GhostRevealed;

    /// <summary>Local player: eaten or dead, the round still on, and the one hint not used yet.</summary>
    public bool CanGhostHint
    {
        get
        {
            var match = MatchManager.Instance;
            return !_ghostHintUsed && (Outcome == PlayerOutcome.Eaten || Outcome == PlayerOutcome.Died) &&
                   match != null && match.RoundRunning && !IsOffline;
        }
    }

    /// <summary>Local ghost: use the hint.</summary>
    public void RequestGhostHint()
    {
        if (CanGhostHint) CmdGhostHint();
    }

    [Command]
    private void CmdGhostHint()
    {
        if (!CanGhostHint) return;
        _ghostHintUsed = true;
        foreach (var fp in s_All)
            if (fp != null && fp.netId != 0 && fp.IsSharkBody && !fp.IsOut)
                fp.RpcGhostRevealed(ghostHintSeconds, PlayerName);
    }

    [ClientRpc]
    private void RpcGhostRevealed(float seconds, string by) => GhostRevealed?.Invoke(this, seconds, by);

    // Our shark's bite caught nothing but passed close to a player fish: the host grazes it.
    private void OnGrazedPrey(Edible prey)
    {
        var victim = prey != null ? prey.GetComponentInParent<FishPlayer>() : null;
        if (victim != null && victim != this) CmdGrazePlayer(victim.netId);
    }

    [Command]
    private void CmdGrazePlayer(uint victimNetId)
    {
        if (_eaten || Time.time < _nextGrazeTime || !NetworkServer.spawned.TryGetValue(victimNetId, out var identity) ||
            !identity.TryGetComponent(out FishPlayer victim)) return;
        if (victim == this || victim._eaten || victim.IsOut || victim.TryGetComponent(out SharkAbilities _)) return;
        if (!TryGetComponent(out SharkAbilities jaws)) return;
        if ((victim.transform.position - transform.position).sqrMagnitude > MaxBiteDistance * MaxBiteDistance) return;
        var match = MatchManager.Instance;
        if (match != null && match.IsOver) return;
        if (PreyOf(netId) != null) return;
        _nextGrazeTime = Time.time + 0.4f; // at most once per bite (the quick bite's cooldown is 0.6 s)

        victim.ServerGraze(jaws.GrazeDamage, jaws.GrazeWoundSeconds);
    }

    /// <summary>Server: the shark's teeth raked us. Hurt and bleeding (never killed outright), and too weak to
    /// tear free if it catches us before the wound closes.</summary>
    [Server]
    private void ServerGraze(float damage, float woundSeconds)
    {
        if (TryGetComponent(out FishVitals vitals))
            vitals.Damage(Mathf.Max(0.01f, Mathf.Min(damage, vitals.Health - breakFreeMinHealth)), woundSeconds);
        _woundedUntil = NetworkTime.time + woundSeconds;
        RpcGrazed(BloodInWater.SeedFor((int)netId, Random.Range(1, int.MaxValue)));
    }

    // Everyone: a puff of blood where the teeth caught us. We get told we're wounded.
    [ClientRpc]
    private void RpcGrazed(int bloodSeed)
    {
        BloodInWater.Spill(transform.position, null, bloodSeed);
        var lib = GameAudio.Lib;
        if (lib != null) GameAudio.PlayAt(lib.graze, transform.position, 0.9f, 30f);
        if (isLocalPlayer) MatchHud.Flash("GRAZED! You're bleeding - one more bite and you can't break free", 3f);
    }

    // ---- Caught: the shark shakes us while we mash to break free; then we're swallowed or we tear loose ----

    /// <summary>The fish this shark is holding, if any (server).</summary>
    private static FishPlayer PreyOf(uint sharkNetId)
    {
        foreach (var fp in s_All)
            if (fp != null && fp._heldBy == sharkNetId) return fp;
        return null;
    }

    [Server]
    private void ServerCatch(FishPlayer shark)
    {
        _eaten = true;
        _heldBy = shark.netId;
        _struggle = 0f;
        _lastStrugglePress = 0f;
        // Still wounded from a graze: too weak to tear free, and swallowed quickly.
        _noEscape = NetworkTime.time < _woundedUntil;
        _swallowAt = NetworkTime.time + (_noEscape ? woundedSwallowSeconds : struggleSeconds);
        // The host picks the blood's seed and hands it to everyone, so every player sees the same cloud.
        RpcCaught(shark.netIdentity, BloodInWater.SeedFor((int)netId, (int)shark.netId), _swallowAt);
    }

    /// <summary>
    /// Everyone: this fish was caught by <paramref name="shark"/>. Its control and position syncing stop, it's
    /// pulled into the jaws and the shark starts shaking it. The victim watches from behind the shark and
    /// mashes to break free before <paramref name="swallowAt"/>.
    /// </summary>
    [ClientRpc]
    private void RpcCaught(NetworkIdentity shark, int bloodSeed, double swallowAt)
    {
        _eaten = true;
        _inJaws = true;
        _swallowAt = swallowAt;
        SetLocallyControlled(false);
        if (TryGetComponent(out NetworkTransformBase netTransform)) netTransform.enabled = false; // the jaws move us now

        var jaws = shark != null ? shark.GetComponent<SharkAbilities>() : null;
        if (!TryGetComponent(out Edible edible)) edible = gameObject.AddComponent<Edible>();
        edible.Devour(shark != null ? shark.gameObject : null, jaws != null ? jaws.MouthAnchor : null, despawn: false, bloodSeed);
        if (jaws != null) jaws.SetHolding(true);
        var lib = GameAudio.Lib;
        if (lib != null) GameAudio.PlayAt(lib.scream, transform.position, 0.8f, 30f, 0.1f);

        if (isLocalPlayer && shark != null)
        {
            var orbit = LocalOrbit();
            if (orbit != null) orbit.Spectate(shark.transform);
        }
    }

    // A press of the break-free button. The meter drains on its own (ServerStruggleTick), faster the weaker we are.
    [Command]
    private void CmdStruggle()
    {
        if (_heldBy == 0 || _noEscape || Time.unscaledTime - _lastStrugglePress < MinStruggleGap) return;
        _lastStrugglePress = Time.unscaledTime;
        _struggle = Mathf.Min(1f, _struggle + struggleGainPerPress);
        if (_struggle >= 1f) ServerBreakFree(hurt: true);
    }

    [Server]
    private void ServerStruggleTick()
    {
        var shark = Holder();
        if (shark == null) { ServerBreakFree(hurt: false); return; } // the shark left the game
        _struggle = Mathf.Max(0f, _struggle - StruggleHoldRate * struggleGainPerPress * Time.deltaTime);
        if (NetworkTime.time >= _swallowAt) ServerSwallow(shark);
    }

    private FishPlayer Holder()
    {
        if (_heldBy == 0) return null;
        var spawned = NetworkServer.active ? NetworkServer.spawned : NetworkClient.spawned;
        return spawned.TryGetValue(_heldBy, out var id) && id != null ? id.GetComponent<FishPlayer>() : null;
    }

    /// <summary>Server: let go of whatever this shark holds (it's leaving the game or changing body).</summary>
    public void ServerReleasePrey()
    {
        var prey = PreyOf(netId);
        if (prey != null) prey.ServerBreakFree(hurt: false);
    }

    // Time's up: the shark swallows. Out of the round.
    [Server]
    private void ServerSwallow(FishPlayer shark)
    {
        _heldBy = 0;
        _struggle = 0f;
        _noEscape = false;
        RpcSwallowed(shark.netIdentity);
        if (shark.TryGetComponent(out SharkAbilities jaws)) jaws.FeedFor(playerFish: true);
        // Out of the round: they watch from here on (no respawn), and it may have been the last fish.
        if (MatchManager.Instance != null) MatchManager.Instance.ServerSetOutcome(this, PlayerOutcome.Eaten);
    }

    [ClientRpc]
    private void RpcSwallowed(NetworkIdentity shark)
    {
        _inJaws = false;
        var jaws = shark != null ? shark.GetComponent<SharkAbilities>() : null;
        if (jaws != null)
        {
            jaws.SetHolding(false);
            jaws.PlayDevour(); // the violent gulp
        }
        var lib = GameAudio.Lib;
        if (lib != null) GameAudio.PlayAt(lib.devour, transform.position, 1f, 35f);
        // Once it's swallowed the body goes, so it doesn't ride in the shark's jaws for the rest of the round.
        StartCoroutine(VanishAfter(SwallowSeconds, leaveJaws: true));
    }

    // Mashed hard enough (or the shark is gone): out of the jaws, hurt and bleeding, flung clear.
    [Server]
    private void ServerBreakFree(bool hurt)
    {
        var shark = Holder();
        _heldBy = 0;
        _struggle = 0f;
        _noEscape = false;

        if (hurt && TryGetComponent(out FishVitals vitals))
        {
            // Hurts, and bleeds (any damage starts the bleeding), but never kills by itself.
            float damage = Mathf.Min(breakFreeDamage, Mathf.Max(0f, vitals.Health - breakFreeMinHealth));
            vitals.Damage(Mathf.Max(damage, 0.01f));
        }

        // Out to one side of the jaws, a little forward and up, so it isn't bitten again straight away.
        Vector3 fling = Vector3.up * 0.2f;
        if (shark != null)
        {
            Transform t = shark.transform;
            float side = Random.value < 0.5f ? -1f : 1f;
            fling = (t.right * side + t.forward * 0.5f + Vector3.up * 0.25f).normalized;
        }
        fling *= breakFreeSpeed;

        // Out of the shark's hierarchy now, on the server too: it may be destroyed this very frame.
        LeaveJaws(shark != null ? shark.netIdentity : null, fling);
        RpcBrokeFree(shark != null ? shark.netIdentity : null, fling);
    }

    [ClientRpc]
    private void RpcBrokeFree(NetworkIdentity shark, Vector3 fling) => LeaveJaws(shark, fling);

    // Every machine (twice on the host, so it's safe to repeat): out of the jaws and swimming again.
    private void LeaveJaws(NetworkIdentity shark, Vector3 fling)
    {
        bool wasInJaws = _inJaws;
        _inJaws = false;
        _eaten = false;

        var jaws = shark != null ? shark.GetComponent<SharkAbilities>() : null;
        if (jaws != null)
        {
            jaws.SetHolding(false);
            jaws.Stun(sharkStunSeconds);
        }
        if (wasInJaws && GameAudio.Lib != null)
        {
            GameAudio.PlayAt(GameAudio.Lib.breakFree, transform.position, 0.9f, 30f);
            GameAudio.PlayAt(GameAudio.Pick(GameAudio.Lib.bubbles), transform.position, 0.8f, 25f);
        }
        if (!TryGetComponent(out Edible edible) || !edible.IsEaten) return; // already out
        edible.Release();
        if (TryGetComponent(out NetworkTransformBase netTransform)) netTransform.enabled = true;

        if (!isLocalPlayer || !wasInJaws) return;
        // We drive this body: pop clear of the teeth and shoot away; Update puts the controls back. Out and a
        // little up, never down - and never left under the seabed: shaking its catch on the bottom, the shark's
        // head (and our body with it) can be dragged into the sand, whose surface only stops things from above.
        Vector3 dir = fling.sqrMagnitude > 1e-4f ? fling.normalized : Vector3.up;
        if (dir.y < 0.3f) { dir.y = 0.3f; dir.Normalize(); }
        fling = dir * fling.magnitude;
        Vector3 from = jaws != null && jaws.MouthAnchor != null ? jaws.MouthAnchor.position : transform.position;
        transform.position = AboveSeabed(from + dir * 1.5f);
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        if (TryGetComponent(out Rigidbody rb))
        {
            rb.position = transform.position;
            rb.rotation = transform.rotation;
        }
        SetLocallyControlled(true);
        var orbit = LocalOrbit();
        if (orbit != null) orbit.SetTarget(transform);
        // The motor owns the velocity, so the burst out of the jaws is a short dash, not a push.
        if (TryGetComponent(out FishMotor motor)) motor.BeginDash(fling.magnitude, 0.45f, 0.4f);
    }

    // ---- Never under the seabed ----

    // Its mesh surface only stops things coming from above: a body that ends up beneath it (released from the
    // jaws inside the sand, a dash clipping through) would just sink out of the world. Put it back on top.
    private void KeepAboveSeabed()
    {
        // The body, not the pivot: a fish's pivot sits under its body (0.4 m below the middle of its capsule),
        // so swimming along the bottom it's in the sand while the fish is fine. Under the world means all of
        // it is below the seabed - and still is a moment later, not just for one physics step.
        Vector3 p = transform.position;
        Bounds body = TryGetComponent(out Collider solid) ? solid.bounds : new Bounds(p, Vector3.one * 0.4f);
        bool under = !WaterFx.GroundBelow(body.center)
                     && WaterFx.SeabedHeight(body.center, out float ground)      // (none: off the edge, not under it)
                     && body.max.y < ground - 0.05f;
        if (!under) { _underSince = -1f; return; }
        if (_underSince < 0f) _underSince = Time.time;
        if (Time.time - _underSince < 0.3f) return;
        _underSince = -1f;

        Vector3 safe = AboveSeabed(p);
        transform.position = safe;
        if (TryGetComponent(out Rigidbody rb))
        {
            rb.position = safe;
            if (!rb.isKinematic) rb.linearVelocity = Vector3.zero;
        }
        if (TryGetComponent(out FishMotor motor)) motor.SetVelocity(Vector3.zero);
        Debug.Log($"[FishPlayer] {name} was under the seabed at {p}; moved back on top ({safe}).");
    }

    // The point, lifted clear of the seabed by the body's own height if it's below it.
    private Vector3 AboveSeabed(Vector3 p)
    {
        if (!WaterFx.SeabedHeight(p, out float ground)) return p;
        float clearance = 0.5f;
        if (TryGetComponent(out Collider body)) clearance += body.bounds.extents.y;
        if (p.y < ground + clearance) p.y = ground + clearance;
        return p;
    }

    // ---- Out of the round ----

    private void OnOutcomeChanged(PlayerOutcome _, PlayerOutcome now) => ApplyOutcome(now);

    /// <summary>
    /// Every machine: this player is out. An escaped fish is simply gone from the reef; a dead one lingers a
    /// moment first; an eaten one is handled by the swallow (RpcSwallowed). Our own player starts spectating.
    /// </summary>
    private void ApplyOutcome(PlayerOutcome outcome)
    {
        if (outcome == PlayerOutcome.Alive) return;
        SetLocallyControlled(false);

        if (outcome == PlayerOutcome.Escaped) Vanish();
        else if (outcome == PlayerOutcome.Died) StartCoroutine(VanishAfter(DeadBodySeconds, leaveJaws: false));

        if (isLocalPlayer) StartSpectating();
    }

    private void StartSpectating()
    {
        var orbit = LocalOrbit();
        if (orbit == null) return;
        if (!orbit.TryGetComponent(out Spectator spectator)) spectator = orbit.gameObject.AddComponent<Spectator>();
        spectator.Begin(this, orbit, _vision);
    }

    private FishOrbitCamera LocalOrbit()
    {
        var orbit = camera != null ? camera.GetComponent<FishOrbitCamera>() : null;
        if (orbit == null && Camera.main != null) orbit = Camera.main.GetComponent<FishOrbitCamera>();
        return orbit;
    }

    private System.Collections.IEnumerator VanishAfter(float seconds, bool leaveJaws)
    {
        yield return new WaitForSeconds(seconds);
        if (leaveJaws) transform.SetParent(null, true);
        Vanish();
    }

    // Hidden and inert for the rest of the round (the object stays: it's this player's connection and
    // carries their camera). Nothing can see, bite, push or be pushed by it.
    private void Vanish()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var p in GetComponentsInChildren<VegetationPusher>(true)) p.enabled = false;
        if (TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
    }

    // ---- Networked comms task relays (the task is server-owned, so its client code routes
    //      Commands through us, the local player, which has authority). ----

    [Command]
    public void CmdJoinSequenceTask(uint taskNetId)
    {
        if (NetworkServer.spawned.TryGetValue(taskNetId, out var id) &&
            id.TryGetComponent(out NetworkedSequenceTask task))
            task.ServerJoin(this);
    }

    [Command]
    public void CmdLeaveSequenceTask(uint taskNetId)
    {
        if (NetworkServer.spawned.TryGetValue(taskNetId, out var id) &&
            id.TryGetComponent(out NetworkedSequenceTask task))
            task.ServerLeave(this);
    }

    [Command]
    public void CmdSubmitSequence(uint taskNetId, int[] dirs)
    {
        if (NetworkServer.spawned.TryGetValue(taskNetId, out var id) &&
            id.TryGetComponent(out NetworkedSequenceTask task))
            task.ServerSubmit(this, dirs);
    }

    [Command]
    public void CmdResetSequenceTask(uint taskNetId)
    {
        if (NetworkServer.spawned.TryGetValue(taskNetId, out var id) &&
            id.TryGetComponent(out NetworkedSequenceTask task))
            task.ServerResetForTesting();
    }

    [Command]
    private void CmdSwitchRole()
    {
        if (NetworkManager.singleton is CustomNetworkManager nm)
            nm.ServerSwitchRole(connectionToClient);
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();

        if (_eatAction != null) { _eatAction.Disable(); _eatAction.Dispose(); _eatAction = null; }
        if (_struggleAction != null) { _struggleAction.Disable(); _struggleAction.Dispose(); _struggleAction = null; }
        if (TryGetComponent(out SharkAbilities shark))
        {
            shark.CaughtFlockFish -= OnCaughtFlockFish;
            shark.CaughtPrey -= OnCaughtPrey;
            shark.GrazedPrey -= OnGrazedPrey;
            shark.Attacked -= CmdShowAttack;
            shark.Sonared -= CmdSonar;
        }

        // The camera was detached from this fish, so it won't be destroyed with us. Clean it up.
        if (camera != null)
            Destroy(camera);
    }
    private void OnDestroy()
    {
        s_All.Remove(this);
        // Gone while in the jaws (left the game): the shark stops shaking nothing.
        if (_inJaws && Holder() is FishPlayer holder && holder.TryGetComponent(out SharkAbilities jaws))
            jaws.SetHolding(false);
        // An offline-sandbox body detached its camera to follow it (PrepareLocalControl) and never gets
        // OnStopLocalPlayer - e.g. when it's removed so F9 can host - so take the camera with us.
        if (_offlineLocal && camera != null) Destroy(camera);
    }

    void OnDisable()
    {
        Debug.Log($"{gameObject.name} was disabled.", this);
        Debug.Log(System.Environment.StackTrace);
    }
}
