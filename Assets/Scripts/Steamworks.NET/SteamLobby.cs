using Mirror;
using ReefRun;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SteamLobby : MonoBehaviour
{
    public static SteamLobby instance { get; private set; }
    public string LobbySceneName = "ReefRunLobby";
    public CSteamID CurrentLobbyId => _currentLobbyId;

    // Lobby data keys. AppID 480 (Spacewar) is shared by every Steamworks test project, so a public
    // lobby search returns other games' lobbies too: GameKey/GameTag marks ours and the browser filters on it.
    public const string GameKey = "game";
    public const string GameTag = "reefrun";
    public const string NameKey = "name";
    public const string PublicKey = "public";
    public const string MembersKey = "members";
    public const string StartingKey = "starting";

    /// <summary>A join attempt failed (lobby full, gone, or its dive already started). The argument is a player-facing reason.</summary>
    public static event System.Action<string> JoinFailed;

    /// <summary>True when the current lobby is listed in the public lobby browser.</summary>
    public bool IsPublic => _currentLobbyId.IsValid() && SteamMatchmaking.GetLobbyData(_currentLobbyId, PublicKey) == "1";

    protected Callback<LobbyCreated_t> lobbyCreated;
    protected Callback<GameLobbyJoinRequested_t> gameLobbyJoinRequested;
    protected Callback<LobbyEnter_t> lobbyEntered;
    protected Callback<LobbyChatUpdate_t> lobbyChatUpdate;
    protected Callback<LobbyDataUpdate_t> lobbyDataUpdate;

    private CustomNetworkManager networkManager;
    private CSteamID _currentLobbyId;
    private bool _hasConnected;



    private void Awake()
    {
        if (instance == null) instance = this;
        else Destroy(instance);
    }

    private void Start()
    {
        if (SteamManager.Initialized)
        {
            string name = SteamFriends.GetPersonaName();
            Debug.Log($"Steamworks is initialized. Player name: {name}");
        }
        else return;
        networkManager = GetComponent<CustomNetworkManager>();
    }

    private void OnEnable()
    {
        lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        gameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
        lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        lobbyDataUpdate = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
    }

    private void OnLobbyChatUpdate(LobbyChatUpdate_t update)
    {
        // The host keeps the member count in the lobby data so the browser can show it for lobbies
        // it isn't in (member lists are only readable from inside a lobby).
        var changedLobby = new CSteamID(update.m_ulSteamIDLobby);
        if (changedLobby == _currentLobbyId && SteamMatchmaking.GetLobbyOwner(changedLobby) == SteamUser.GetSteamID())
            SteamMatchmaking.SetLobbyData(changedLobby, MembersKey, SteamMatchmaking.GetNumLobbyMembers(changedLobby).ToString());

        var controller = FindAnyObjectByType<ReefRunLobbyController>();
        if (controller == null) return;

        var memberId = new CSteamID(update.m_ulSteamIDUserChanged);
        var change = (EChatMemberStateChange)update.m_rgfChatMemberStateChange;

        if (change == EChatMemberStateChange.k_EChatMemberStateChangeEntered)
        {
            Debug.Log($"Player {memberId} joined the lobby.");
            ConstructPlayer(false, memberId);
        }
        else if (change == EChatMemberStateChange.k_EChatMemberStateChangeLeft
            || change == EChatMemberStateChange.k_EChatMemberStateChangeDisconnected)
        {
            Debug.Log($"Player {memberId} left the lobby.");
            controller.RemovePlayer(memberId);
        }
    }

    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t param)
    {
        if (_currentLobbyId.IsValid() && _currentLobbyId != (CSteamID)param.m_steamIDLobby)
            LeaveLobby();

        SteamMatchmaking.JoinLobby(param.m_steamIDLobby);
    }

    private void OnLobbyEntered(LobbyEnter_t lobby)
    {
        var response = (EChatRoomEnterResponse)lobby.m_EChatRoomEnterResponse;
        if (response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            Debug.LogWarning($"[Lobby] Join failed: {response}");
            JoinFailed?.Invoke(response switch
            {
                EChatRoomEnterResponse.k_EChatRoomEnterResponseFull => "That lobby is full.",
                EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist => "That lobby has closed.",
                EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed => "That lobby is friends only.",
                _ => "Couldn't join that lobby.",
            });
            return;
        }

        _currentLobbyId = new CSteamID(lobby.m_ulSteamIDLobby);
        if (NetworkServer.active) return; // host — already handled in OnLobbyCreated

        // Lobby metadata may not have arrived yet — request it and connect in OnLobbyDataUpdate
        SteamMatchmaking.RequestLobbyData(_currentLobbyId);
    }

    private void OnLobbyDataUpdate(LobbyDataUpdate_t update)
    {
        var lobbyId = new CSteamID(update.m_ulSteamIDLobby);
        var memberId = new CSteamID(update.m_ulSteamIDMember);

        if (lobbyId != _currentLobbyId) return;

        var lobbyUi = FindAnyObjectByType<ReefRunLobbyController>();
        if (memberId != lobbyId)
        {
            if (lobbyUi == null) return;
            string val = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "ready");
            lobbyUi.SetReady(memberId, val == "1");
            return;
        }

        // Lobby-level data (e.g. the host's survival clock choice).
        if (lobbyUi != null) lobbyUi.RefreshLobbyData();

        // Host pressed Start — play the launch overlay on every client. Mirror
        // will pull clients into the game scene right after (host's StartMatch).
        if (!NetworkServer.active && SteamMatchmaking.GetLobbyData(lobbyId, StartingKey) == "1")
        {
            // Joined after the dive began (e.g. the browser list was stale): there's no mid-match join.
            if (!_hasConnected)
            {
                LeaveLobby();
                JoinFailed?.Invoke("That dive already started.");
                return;
            }
            LaunchOverlay.Instance?.Play();
            return;
        }

        if (NetworkServer.active || _hasConnected) return;

        string hostAddress = SteamMatchmaking.GetLobbyData(_currentLobbyId, "HostAddress");
        if (string.IsNullOrEmpty(hostAddress)) { Debug.LogError("HostAddress lobby data is empty."); return; }

        _hasConnected = true;
        networkManager.networkAddress = hostAddress;
        networkManager.StartClient();
        SceneManager.LoadScene(LobbySceneName);
    }

    private void OnLobbyCreated(LobbyCreated_t lobby)
    {
        if (lobby.m_eResult != EResult.k_EResultOK) { Debug.LogError("Failed to create lobby."); return; }

        _currentLobbyId = new CSteamID(lobby.m_ulSteamIDLobby);
        networkManager.StartHost();
        SteamMatchmaking.SetLobbyData(_currentLobbyId, "HostAddress", SteamUser.GetSteamID().ToString());
        SteamMatchmaking.SetLobbyData(_currentLobbyId, GameKey, GameTag);
        SteamMatchmaking.SetLobbyData(_currentLobbyId, NameKey, $"{SteamFriends.GetPersonaName()}'s reef");
        SteamMatchmaking.SetLobbyData(_currentLobbyId, PublicKey, "0");
        SteamMatchmaking.SetLobbyData(_currentLobbyId, MembersKey, "1");
        SteamMatchmaking.SetLobbyData(_currentLobbyId, StartingKey, "0");
        SceneManager.LoadScene(LobbySceneName);
    }

    #region Helpers
    // Leave the current Steam lobby and stop Mirror networking. The caller is
    // responsible for loading the menu scene afterwards (offlineScene is unset).
    public void LeaveLobby()
    {
        if (_currentLobbyId.IsValid())
        {
            SteamMatchmaking.LeaveLobby(_currentLobbyId);
            _currentLobbyId = CSteamID.Nil;
        }

        if (networkManager != null)
        {
            if (NetworkServer.active) networkManager.StopHost();   // host = server + local client
            else if (NetworkClient.active) networkManager.StopClient();
        }

        _hasConnected = false;
    }

    public void CreateLobby()
    {
        if (SteamManager.Initialized)
        {
            // Reef Run is 4 divers (3 fish + 1 shark); Steam turns away a 5th.
            networkManager.maxConnections = ReefRunLobbyController.MaxSlots;
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, ReefRunLobbyController.MaxSlots);
        }
    }

    /// <summary>
    /// Host only: list the lobby in the public browser, or take it back to friends only (friends can
    /// still join from their Steam friends list either way).
    /// </summary>
    public void SetPublic(bool isPublic)
    {
        if (!SteamManager.Initialized || !_currentLobbyId.IsValid()) return;
        if (SteamMatchmaking.GetLobbyOwner(_currentLobbyId) != SteamUser.GetSteamID()) return;
        SteamMatchmaking.SetLobbyType(_currentLobbyId, isPublic ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly);
        SteamMatchmaking.SetLobbyData(_currentLobbyId, PublicKey, isPublic ? "1" : "0");
    }

    /// <summary>
    /// Join a Steam lobby directly (e.g. a friend's, from the main menu). The rest is the same path as
    /// accepting an invite: OnLobbyEntered -> OnLobbyDataUpdate connects to the host and loads the lobby.
    /// </summary>
    public void JoinLobby(CSteamID lobbyId)
    {
        if (!SteamManager.Initialized || !lobbyId.IsValid()) return;
        if (_currentLobbyId.IsValid() && _currentLobbyId != lobbyId) LeaveLobby();
        SteamMatchmaking.JoinLobby(lobbyId);
    }

    private void ConstructPlayer(bool isHost, CSteamID steamId)
    {
        string name = SteamFriends.GetFriendPersonaName(steamId);
        Player player = new Player
        {
            steamId = steamId,
            name = name,
            avatar = SteamManager.GetLocalSteamAvatar(steamId),
            isHost = isHost,
            isYou = steamId == SteamUser.GetSteamID(),
        };
        ReefRunLobbyController controller = FindAnyObjectByType<ReefRunLobbyController>();
        if (controller != null)
            controller.AddPlayer(player);
        else
            Debug.LogWarning("ReefRunLobbyController not found in the scene.");
    }

    private void OnApplicationQuit()
    {
        LeaveLobby();
    }
    #endregion
}
