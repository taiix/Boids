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

    // What this body can do, regardless of the Role value (the offline sandbox shark keeps its authored role).
    private bool IsSharkBody => TryGetComponent(out SharkAbilities _);

    [Header("Eating")]
    [Tooltip("How close a food pellet must be to the mouth to be eaten.")]
    [SerializeField] float eatRange = 1.6f;
    [Tooltip("Forward distance from the fish pivot to its 'mouth'.")]
    [SerializeField] float mouthOffset = 1.0f;
    private InputAction _eatAction;

    private CreatureVision _vision;

    // Whether this machine's input currently drives this body: local player AND the round is live.
    private bool _controlsLive;

    // Offline sandbox: no host/client is running and DevHost re-enabled this scene-placed body, so we
    // self-drive as the local player here (Mirror's OnStart* callbacks never fire offline).
    private bool _offlineLocal;

    // Caught by the shark: controls stay off and the body rides the shark's jaws until the server
    // respawns this player. Set on the server when it accepts the bite and on clients by the RPC.
    private bool _eaten;
    public bool IsBeingEaten => _eaten;

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
        _fishController = GetComponent<FishController>();
        //_fishController.enabled = false;

        // Local-player-only ability; disabled on remotes so they don't react to the key.
        if (TryGetComponent(out _flockBlend))
            _flockBlend.enabled = false;

        // Fish bodies must be edible for the shark's bite to find them (the prefab doesn't carry one).
        if (!TryGetComponent(out SharkAbilities _) && !TryGetComponent(out Edible _))
            gameObject.AddComponent<Edible>();

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

        // Our shark's bites have to reach the host so the fish goes for everyone.
        if (TryGetComponent(out SharkAbilities shark))
        {
            shark.CaughtFlockFish += OnCaughtFlockFish;
            shark.CaughtPrey += OnCaughtPrey;
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
        if (!isLocalPlayer && !_offlineLocal) return;

        // Offline we're always live; networked, controls wait for the round to start. Never while
        // we're in the shark's jaws, parked at a task, or have the Escape menu open.
        bool live = (_offlineLocal || RoundLive) && !_eaten && !_taskLocked && !PauseMenu.IsOpen;
        if (live != _controlsLive)
            SetLocallyControlled(live);

        // Fish only (pellets are fish food): prompt when a pellet is in reach - unless we're next to a
        // task, where E joins it instead.
        if (_controlsLive && !IsSharkBody && !NetworkedSequenceTask.LocalNearAnyTask && PelletInReach() != null)
            MatchHud.ShowPrompt("Press E to eat");

        // E eats — unless we're next to a task, where E joins it instead (the task handles that press).
        if (_controlsLive && _eatAction != null && _eatAction.WasPressedThisFrame()
            && !NetworkedSequenceTask.LocalNearAnyTask)
        {
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
        // sees it). The server decides whether that's allowed. Networked only - offline has no server.
        if (_controlsLive && !_offlineLocal && Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            CmdSwitchRole();
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
    private void CmdEat() => ServerEatNearestPellet();

    // Eat the nearest pellet in mouth range. Runs on the server for a networked player, or directly on
    // the offline sandbox body (FoodPellet.Consume is offline-capable).
    private void ServerEatNearestPellet()
    {
        if (IsSharkBody) return; // pellets are fish food; the shark eats fish
        var best = PelletInReach();
        if (best != null)
            best.Consume(GetComponent<FishVitals>());
    }

    // The pellet a bite would take right now, or null. Shared by the eat itself and its prompt, so
    // "Press E to eat" only shows when E would actually eat.
    private FoodPellet PelletInReach()
    {
        Vector3 mouth = transform.position + transform.forward * mouthOffset;
        FoodPellet best = null;
        float bestSqr = eatRange * eatRange;
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
        if (victim == this || victim._eaten || victim.TryGetComponent(out SharkAbilities _)) return; // only fish
        if ((victim.transform.position - transform.position).sqrMagnitude > MaxBiteDistance * MaxBiteDistance) return;

        victim._eaten = true;
        victim.RpcEatenBy(netIdentity);
        if (TryGetComponent(out SharkAbilities shark)) shark.FeedFor(playerFish: true);
        if (NetworkManager.singleton is CustomNetworkManager nm) nm.ServerRespawnAfterEaten(victim);
    }

    /// <summary>
    /// Everyone: this fish was caught by <paramref name="shark"/>. Its control and position syncing stop,
    /// it's pulled into the shark's jaws, and on the victim's own screen the camera watches through the
    /// shark's view until the server respawns them.
    /// </summary>
    [ClientRpc]
    private void RpcEatenBy(NetworkIdentity shark)
    {
        _eaten = true;
        SetLocallyControlled(false);
        if (TryGetComponent(out NetworkTransformBase netTransform)) netTransform.enabled = false; // the jaws move us now

        var jaws = shark != null ? shark.GetComponent<SharkAbilities>() : null;
        if (!TryGetComponent(out Edible edible)) edible = gameObject.AddComponent<Edible>();
        edible.Devour(shark != null ? shark.gameObject : null, jaws != null ? jaws.MouthAnchor : null, despawn: false);

        if (isLocalPlayer && shark != null)
        {
            var orbit = camera != null ? camera.GetComponent<FishOrbitCamera>() : null;
            if (orbit == null && Camera.main != null) orbit = Camera.main.GetComponent<FishOrbitCamera>();
            if (orbit != null) orbit.Spectate(shark.transform);
            if (_vision != null) _vision.ApplyRole(FishRole.Shark); // the shark's eyes, not ours
        }
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
    private void CmdSwitchRole()
    {
        if (NetworkManager.singleton is CustomNetworkManager nm)
            nm.ServerSwitchRole(connectionToClient);
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();

        if (_eatAction != null) { _eatAction.Disable(); _eatAction.Dispose(); _eatAction = null; }
        if (TryGetComponent(out SharkAbilities shark))
        {
            shark.CaughtFlockFish -= OnCaughtFlockFish;
            shark.CaughtPrey -= OnCaughtPrey;
        }

        // The camera was detached from this fish, so it won't be destroyed with us. Clean it up.
        if (camera != null)
            Destroy(camera);
    }
    private void OnDestroy()
    {
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
