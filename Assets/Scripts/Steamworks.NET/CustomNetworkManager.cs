using FishGame;
using Mirror;
using Mirror.FizzySteam;
using System.Collections;
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

    [Tooltip("Any player can press F8 mid-match to switch between shark and fish; everyone sees the new " +
             "body. Handy for testing (e.g. two fish for the comms task); turn off for real matches.")]
    [SerializeField] private bool allowRoleSwap = true;

    [Tooltip("Testing stand-in for death: a player fish eaten by the shark watches through the shark's " +
             "view for this many seconds, then respawns as a fresh fish.")]
    [SerializeField] private float respawnAfterEatenSeconds = 3f;

    /// <summary>The scene the lobby loads when the host starts the match.</summary>
    public string GameSceneName => gameSceneName;

    private const float RoleSwapCooldown = 1f;
    private readonly Dictionary<int, float> _lastRoleSwap = new Dictionary<int, float>();

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
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnAnySceneLoaded;
    }

    public override void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnAnySceneLoaded;
        base.OnDestroy();
    }

    /// <summary>
    /// A test scene may have a shark/fish body placed in it for driving offline. When a server loads it
    /// those must go before Mirror spawns the scene's NetworkIdentities, or they'd appear on top of the
    /// players' real bodies. sceneLoaded fires before Mirror's FinishLoadScene spawns them; Immediate,
    /// because that can be later in this same frame. (The editor's DevHost does the same for hosts it
    /// starts inside an already-loaded scene.)
    /// </summary>
    private static void OnAnySceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                         UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!NetworkServer.active) return;
        var doomed = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var fp in root.GetComponentsInChildren<FishPlayer>(true))
                if (fp.TryGetComponent(out NetworkIdentity id) && id.sceneId != 0) doomed.Add(fp.gameObject);
        foreach (var go in doomed)
        {
            Debug.Log($"[Net] Removing scene-placed '{go.name}' - players get spawned bodies.");
            DestroyImmediate(go);
        }
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
        _lastRoleSwap.Remove(conn.connectionId);
    }

    private const string MainMenuScene = "01 - MainMenu";
    private bool _clientWasConnected;

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        _clientWasConnected = true;
    }

    /// <summary>
    /// A pure client lost the host (it quit or left to the main menu, or the connection dropped): go to
    /// the main menu rather than sit in a dead lobby/game scene. Checked a moment later so a deliberate
    /// leave (lobby Back, the Escape menu), which loads the menu itself, isn't doubled up. Failed
    /// connection attempts (never connected) are left alone - the dev auto-join retries those.
    /// </summary>
    public override void OnClientDisconnect()
    {
        bool lostHost = _clientWasConnected && mode == NetworkManagerMode.ClientOnly;
        _clientWasConnected = false;
        base.OnClientDisconnect();
        if (lostHost) Invoke(nameof(ReturnToMenuIfStranded), 0.25f);
    }

    private void ReturnToMenuIfStranded()
    {
        if (NetworkClient.active || NetworkServer.active) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == MainMenuScene) return;
        Debug.Log("[Net] Lost the host - returning to the main menu.");
        SteamLobby.instance?.LeaveLobby();
        UnityEngine.SceneManagement.SceneManager.LoadScene(MainMenuScene);
    }

    /// <summary>
    /// Server: swap a player between shark and fish by re-spawning them as the other prefab at the same
    /// spot. ReplacePlayerForConnection spawns the new body on every client and destroys the old one, so
    /// everyone sees the change. Requested with F8 (FishPlayer.CmdSwitchRole).
    /// </summary>
    public void ServerSwitchRole(NetworkConnectionToClient conn)
    {
        if (!allowRoleSwap || !_gameStarted) return;
        if (conn?.identity == null || !conn.identity.TryGetComponent(out FishPlayer cur)) return;
        if (_lastRoleSwap.TryGetValue(conn.connectionId, out float last) && Time.unscaledTime - last < RoleSwapCooldown) return;
        // Not while being eaten (the respawn handles that body), nor while a shark has someone in its
        // jaws - the prey is parented to its mouth and would be destroyed along with the old body.
        if (cur.IsBeingEaten || HasPreyInJaws(cur)) return;

        FishRole newRole = cur.Role == FishRole.Shark ? FishRole.Fish : FishRole.Shark;
        GameObject prefab = newRole == FishRole.Shark ? sharkPrefab : playerPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[Net] No prefab assigned for {newRole}; can't switch.", this);
            return;
        }

        _lastRoleSwap[conn.connectionId] = Time.unscaledTime;
        Transform from = conn.identity.transform;
        GameObject go = Instantiate(prefab, from.position, from.rotation);
        if (go.TryGetComponent(out FishPlayer fp)) fp.Role = newRole; // ships in the spawn payload
        NetworkServer.ReplacePlayerForConnection(conn, go, ReplacePlayerOptions.Destroy);
        Debug.Log($"[Net] Connection {conn.connectionId} is now a {newRole}.");
    }

    private static bool HasPreyInJaws(FishPlayer body)
    {
        foreach (var fp in body.GetComponentsInChildren<FishPlayer>())
            if (fp != body) return true;
        return false;
    }

    /// <summary>Server: an eaten player gets a fresh fish body once they've watched the shark for a bit.</summary>
    public void ServerRespawnAfterEaten(FishPlayer victim)
    {
        if (victim == null || victim.connectionToClient == null) return;
        StartCoroutine(RespawnAfterEaten(victim.connectionToClient, victim.netIdentity));
    }

    private IEnumerator RespawnAfterEaten(NetworkConnectionToClient conn, NetworkIdentity eatenBody)
    {
        yield return new WaitForSeconds(respawnAfterEatenSeconds);

        // Left, or already in another body (not the one that was eaten)? Nothing to do.
        if (!NetworkServer.active || conn == null || !NetworkServer.connections.ContainsKey(conn.connectionId)) yield break;
        if (conn.identity != null && conn.identity != eatenBody) yield break;

        GameObject go = TryGetSpawnPose(FishRole.Fish, out Vector3 pos, out Quaternion rot)
            ? Instantiate(playerPrefab, pos, rot)
            : Instantiate(playerPrefab);
        if (go.TryGetComponent(out FishPlayer fp)) fp.Role = FishRole.Fish;

        if (conn.identity != null) NetworkServer.ReplacePlayerForConnection(conn, go, ReplacePlayerOptions.Destroy);
        else NetworkServer.AddPlayerForConnection(conn, go);
        Debug.Log($"[Net] Connection {conn.connectionId} was eaten; respawned as a fresh fish.");
    }

    // A role's spawn zone if the scene has one, else Mirror's start positions (NetworkStartPosition).
    private bool TryGetSpawnPose(FishRole role, out Vector3 pos, out Quaternion rot)
    {
        if (RoleSpawnZone.TryGetSpawn(role, out pos, out rot)) return true;
        Transform start = GetStartPosition();
        pos = start != null ? start.position : Vector3.zero;
        rot = start != null ? start.rotation : Quaternion.identity;
        return start != null;
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
