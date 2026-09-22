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

    [Header("Eating")]
    [Tooltip("How close a food pellet must be to the mouth to be eaten.")]
    [SerializeField] float eatRange = 1.6f;
    [Tooltip("Forward distance from the fish pivot to its 'mouth'.")]
    [SerializeField] float mouthOffset = 1.0f;
    private InputAction _eatAction;

    private CreatureVision _vision;

    // Whether this machine's input currently drives this body: local player AND the round is live.
    private bool _controlsLive;

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

    // Server-side, per game scene: the round has gone live. CustomNetworkManager resets it.
    private static bool s_RoundLive;

    private void OnRoleChanged(FishRole previous, FishRole current)
    {
        // TODO (shark milestone): enable SharkAbilities / swap model when current == Shark.

        // Only the local player has a camera, so this is a no-op on remotes.
        ApplyRoleVision(current);
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

    private void SetLocallyControlled(bool local)
    {
        _fishController.enabled = local;
        if (TryGetComponent(out FishMotor motor)) motor.enabled = local;
        if (TryGetComponent(out SharkAbilities shark)) shark.enabled = local;
        if (TryGetComponent(out Rigidbody rb)) rb.isKinematic = !local;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        // Controls stay off (and the body held still) until the round starts; Update switches them on.
        StartCoroutine(ReportLoadedWhenSettled());

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

        // Eat action (local player only): press E near a food pellet to eat it.
        _eatAction = new InputAction("Eat", InputActionType.Button);
        _eatAction.AddBinding("<Keyboard>/e");
        _eatAction.AddBinding("<Gamepad>/buttonWest");
        _eatAction.Enable();
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        bool live = RoundLive;
        if (live != _controlsLive)
        {
            _controlsLive = live;
            SetLocallyControlled(live);
            if (_flockBlend != null) _flockBlend.enabled = live;
        }

        if (_controlsLive && _eatAction != null && _eatAction.WasPressedThisFrame())
            CmdEat();
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
    private void CmdEat()
    {
        Vector3 mouth = transform.position + transform.forward * mouthOffset;
        FoodPellet best = null;
        float bestSqr = eatRange * eatRange;
        foreach (var pellet in FindObjectsByType<FoodPellet>(FindObjectsSortMode.None))
        {
            if (pellet == null || pellet.IsEaten) continue;
            float d = (pellet.transform.position - mouth).sqrMagnitude;
            if (d <= bestSqr) { bestSqr = d; best = pellet; }
        }
        if (best != null)
            best.Consume(GetComponent<FishVitals>());
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();

        if (_eatAction != null) { _eatAction.Disable(); _eatAction.Dispose(); _eatAction = null; }

        // The camera was detached from this fish, so it won't be destroyed with us. Clean it up.
        if (camera != null)
            Destroy(camera);
    }
    void OnDisable()
    {
        Debug.Log($"{gameObject.name} was disabled.", this);
        Debug.Log(System.Environment.StackTrace);
    }
}
