using FishGame;
using Mirror;
using Mirror.FizzySteam;
using System.Collections.Generic;
using UnityEngine;

public class CustomNetworkManager : NetworkManager
{
    [Header("Game scene")]
    [Tooltip("Name of the scene that counts as 'in game'. Must match the scene asset's name exactly " +
             "(it is '03 - Island', not 'Island').")]
    [SerializeField] private string gameSceneName = "03 - Island";

    [Header("Roles")]
    [Tooltip("Prefab spawned for the one player drawn as the shark. It must have a NetworkIdentity " +
             "and a FishPlayer, and must also be listed in Spawnable Prefabs below.")]
    [SerializeField] private GameObject sharkPrefab;

    [Tooltip("How many players must be connected before anyone is made the shark. Below this, " +
             "everyone stays a fish - so testing a build on your own does not always drop you into " +
             "the shark's seat. Set to 1 if you deliberately want a solo shark.")]
    [SerializeField] private int minPlayersForShark = 2;

#if UNITY_EDITOR
    [Header("Editor testing")]
    [Tooltip("Editor only. When set to Fish or Shark, YOUR host player always spawns in that role " +
             "so you can check both looks without a second client. Other players are unaffected. " +
             "Leave on Unassigned for normal random selection.")]
    [SerializeField] private FishRole editorForcedRole = FishRole.Unassigned;
#endif

    [Header("Round start")]
    [Tooltip("Start the round anyway after this many seconds if a player never finishes loading, " +
             "so one stuck client can't keep everyone frozen on the loading screen.")]
    [SerializeField] private float maxWaitForPlayersSeconds = 90f;

    private bool _gameStarted = false;

    // Drawn once per round, the moment the game scene finishes loading on the server, when the whole
    // roster is already connected. -1 means "not drawn yet"; late joiners never match it and so are
    // always fish, which is the behaviour we want.
    private int _sharkConnectionId = -1;

#if UNITY_EDITOR
    // Local Multiplayer Play Mode test (see DevHost): in the real game everyone is connected before the
    // game scene loads, so the shark draw sees the whole roster. Here the host is already in the scene
    // and virtual players join a moment later, so spawns are held until they have all arrived.
    private int _devExpectedPlayers;
    private float _devHoldUntil;
    private const float DevMaxHoldSeconds = 30f;
#endif

    public override void Awake()
    {
        if (transport == null)
            transport = GetComponent<FizzySteamworks>();
        base.Awake();
    }

    public override void OnServerSceneChanged(string newSceneName)
    {
        base.OnServerSceneChanged(newSceneName);
        _gameStarted = newSceneName == gameSceneName;

        if (_gameStarted)
        {
            RoleSpawnZone.ResetClaims();
            DrawShark();
        }
        else _sharkConnectionId = -1;

        ArmRoundGate();
    }

    /// <summary>
    /// Players spawn frozen and the round goes live only when every client reports it has loaded
    /// (FishPlayer.ServerTryStartRound), so a faster machine gets no head start. This resets that
    /// state for the new scene and arms the timeout.
    /// </summary>
    private void ArmRoundGate()
    {
        FishPlayer.ServerResetRound();
        CancelInvoke(nameof(ForceStartRound));
        if (_gameStarted) Invoke(nameof(ForceStartRound), maxWaitForPlayersSeconds);
    }

    private void ForceStartRound()
    {
        if (_gameStarted) FishPlayer.ServerTryStartRound(force: true);
    }

    /// <summary>
    /// Pick exactly one connection to play the shark.
    ///
    /// Drawing an id out of the live connection list (rather than comparing ids against a random
    /// number) matters: Mirror's connectionIds are not a 0..N-1 range. The host is always 0 and
    /// remote ids keep climbing and are never reused, so after any churn you get gaps like 0, 3, 7.
    /// Picking from the list keeps every player equally eligible and guarantees exactly one shark.
    /// </summary>
    private void DrawShark()
    {
        _sharkConnectionId = -1;

        var ids = new List<int>();
        foreach (var conn in NetworkServer.connections.Values)
            if (conn != null) ids.Add(conn.connectionId);

        // A lone player becomes the shark with nobody to hunt, which is never what you want when
        // you launch a build to check something.
        if (ids.Count < Mathf.Max(1, minPlayersForShark)) return;

        _sharkConnectionId = ids[Random.Range(0, ids.Count)];
    }

    public override void OnServerReady(NetworkConnectionToClient conn)
    {
        base.OnServerReady(conn);
        if (!_gameStarted || conn.identity != null) return;        // skip lobby & double-spawn

#if UNITY_EDITOR
        if (DevHoldingSpawns()) return;   // spawned in Update once every expected player is ready
#endif
        SpawnPlayer(conn);
    }

    private void SpawnPlayer(NetworkConnectionToClient conn)
    {
        // Safety net: the dev host starts already inside the game scene, so OnServerSceneChanged
        // never runs and no draw has happened. Drawing here covers that without a second code path.
        if (_sharkConnectionId == -1) DrawShark();

        FishRole role = conn.connectionId == _sharkConnectionId ? FishRole.Shark : FishRole.Fish;

#if UNITY_EDITOR
        // Host connection only, so a second client in a local test still gets a real draw.
        if (editorForcedRole != FishRole.Unassigned && conn.connectionId == 0)
            role = editorForcedRole;
#endif

        if (role == FishRole.Shark && sharkPrefab == null)
        {
            Debug.LogWarning("[Net] This player was drawn as the shark but no Shark Prefab is " +
                             "assigned on the NetworkManager; spawning the fish prefab instead.", this);
            role = FishRole.Fish;
        }

        GameObject prefab = role == FishRole.Shark ? sharkPrefab : playerPrefab;

        GameObject go;
        if (RoleSpawnZone.TryGetSpawn(role, out Vector3 pos, out Quaternion rot))
        {
            go = Instantiate(prefab, pos, rot);
        }
        else
        {
            // Only touch Mirror's round-robin when no zone covers this role — calling
            // GetStartPosition() unconditionally still bumps its index and skews the cycle.
            Transform start = GetStartPosition();
            go = start != null
                ? Instantiate(prefab, start.position, start.rotation)
                : Instantiate(prefab);
        }

        // Set before spawning so the value ships with the initial state and every client sees the
        // correct role from the very first frame.
        if (go.TryGetComponent(out FishPlayer fp)) fp.Role = role;

        NetworkServer.AddPlayerForConnection(conn, go);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // If the chosen shark drops before ever spawning, redraw so the round is not left sharkless.
        bool sharkLeftBeforeSpawning = conn.connectionId == _sharkConnectionId && conn.identity == null;

        base.OnServerDisconnect(conn);

        if (_gameStarted && sharkLeftBeforeSpawning) DrawShark();

        // The player everyone was waiting on may be the one who just left.
        if (_gameStarted) FishPlayer.ServerTryStartRound();
    }

#if UNITY_EDITOR
    public override void Update()
    {
        base.Update();
        if (_devExpectedPlayers <= 0 || !NetworkServer.active) return;

        int ready = CountReadyConnections();
        bool timedOut = Time.unscaledTime >= _devHoldUntil;
        if (ready < _devExpectedPlayers && !timedOut) return;

        if (ready < _devExpectedPlayers)
            Debug.LogWarning($"[DevHost] Only {ready}/{_devExpectedPlayers} players joined within {DevMaxHoldSeconds:0}s; starting with them.");
        else
            Debug.Log($"[DevHost] All {ready} players connected; drawing roles.");

        _devExpectedPlayers = 0;
        DrawShark();
        foreach (var conn in NetworkServer.connections.Values)
            if (conn != null && conn.isReady && conn.identity == null) SpawnPlayer(conn);
    }

    private bool DevHoldingSpawns() => _devExpectedPlayers > 0;

    private static int CountReadyConnections()
    {
        int n = 0;
        foreach (var conn in NetworkServer.connections.Values)
            if (conn != null && conn.isReady) n++;
        return n;
    }

    /// <summary>
    /// Editor-only: join a DevHost as a client (a Multiplayer Play Mode virtual player). The client is
    /// already in the game scene and the host never changes scene, so no scene message is exchanged;
    /// Mirror readies the connection and the host spawns our body in OnServerReady.
    /// </summary>
    public void DevStartClient(Transport devTransport, string address)
    {
        if (NetworkServer.active || NetworkClient.active) return;
        if (devTransport != null) { transport = devTransport; Transport.active = devTransport; }
        networkAddress = address;
        StartClient();
    }

    /// <summary>
    /// Editor-only: start a solo host in the CURRENT game scene without the Steam lobby. Called by
    /// <see cref="FishGame.DevHost"/> when you press Play directly in the Island scene. Sets
    /// <c>_gameStarted</c> so OnServerReady spawns the player even though no scene change occurred.
    /// </summary>
    /// <param name="expectedPlayers">Total players including the host. Above 1, spawns (and so the
    /// shark draw) wait until that many are connected, or <see cref="DevMaxHoldSeconds"/>.</param>
    public void DevStartHost(Transport devTransport, int expectedPlayers = 1)
    {
        if (NetworkServer.active || NetworkClient.active) return;
        if (devTransport != null) { transport = devTransport; Transport.active = devTransport; }
        _gameStarted = true; // we're already in the game scene, so allow player spawns
        _sharkConnectionId = -1;
        _devExpectedPlayers = expectedPlayers > 1 ? expectedPlayers : 0;
        _devHoldUntil = Time.unscaledTime + DevMaxHoldSeconds;
        RoleSpawnZone.ResetClaims();
        ArmRoundGate();

        // StartHost can make the local connection ready synchronously, and the host connection does
        // not exist until it runs - so the draw is left to OnServerReady's safety net above.
        StartHost();

        // ServerChangeScene never runs here, so Mirror has no network scene and would not tell joining
        // clients where to go. Set it (after StartHost, so the host's own client isn't sent it) and
        // Mirror sends each new client a SceneMessage for this scene on connect - which pulls a virtual
        // player into the Island even if its editor had some other scene open. StopServer clears it.
        networkSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }
#endif
}
