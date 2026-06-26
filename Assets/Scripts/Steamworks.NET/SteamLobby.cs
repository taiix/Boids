using Mirror;
using ReefRun;
using Steamworks;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SteamLobby : MonoBehaviour
{
    public static SteamLobby instance { get; private set; }
    public string LobbySceneName = "ReefRunLobby";
    public CSteamID CurrentLobbyId => _currentLobbyId;

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
        lobbyCreated           = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        gameLobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
        lobbyEntered           = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        lobbyChatUpdate        = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        lobbyDataUpdate        = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
    }

    private void OnLobbyChatUpdate(LobbyChatUpdate_t update)
    {
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
        SteamMatchmaking.JoinLobby(param.m_steamIDLobby);
    }

    private void OnLobbyEntered(LobbyEnter_t lobby)
    {
        _currentLobbyId = new CSteamID(lobby.m_ulSteamIDLobby);
        if (NetworkServer.active) return; // host — already handled in OnLobbyCreated

        // Lobby metadata may not have arrived yet — request it and connect in OnLobbyDataUpdate
        SteamMatchmaking.RequestLobbyData(_currentLobbyId);
    }

    private void OnLobbyDataUpdate(LobbyDataUpdate_t update)
    {
        var lobbyId  = new CSteamID(update.m_ulSteamIDLobby);
        var memberId = new CSteamID(update.m_ulSteamIDMember);

        if (lobbyId != _currentLobbyId) return;

        // Member data changed (e.g. ready state) — memberId differs from lobbyId
        if (memberId != lobbyId)
        {
            var controller = FindAnyObjectByType<ReefRunLobbyController>();
            if (controller == null) return;
            string val = SteamMatchmaking.GetLobbyMemberData(lobbyId, memberId, "ready");
            controller.SetReady(memberId, val == "1");
            return;
        }

        // Lobby-level data changed — handle initial client connection only once
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
        SceneManager.LoadScene(LobbySceneName);
    }

    #region Helpers
    public void CreateLobby()
    {
        if (SteamManager.Initialized)
        {
            Debug.Log("bimbams");
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, networkManager.maxConnections);
        }
    }

    private void ConstructPlayer(bool isHost, CSteamID steamId)
    {
        string name = SteamFriends.GetFriendPersonaName(steamId);
        Player player = new Player
        {
            steamId  = steamId,
            name     = name,
            avatar   = SteamManager.GetLocalSteamAvatar(steamId),
            isHost   = isHost,
            isYou    = steamId == SteamUser.GetSteamID(),
        };
        ReefRunLobbyController controller = FindAnyObjectByType<ReefRunLobbyController>();
        if (controller != null)
            controller.AddPlayer(player);
        else
            Debug.LogWarning("ReefRunLobbyController not found in the scene.");
    }
    #endregion
}
