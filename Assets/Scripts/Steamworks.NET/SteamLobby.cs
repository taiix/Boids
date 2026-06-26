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

    private CustomNetworkManager networkManager;
    private CSteamID _currentLobbyId;



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

    }

    private void OnLobbyEntered(LobbyEnter_t lobby)
    {
        Debug.Log($"Joined lobby: {lobby.m_ulSteamIDLobby}");
    }

    private void OnLobbyCreated(LobbyCreated_t lobby)
    {
        _currentLobbyId = new CSteamID(lobby.m_ulSteamIDLobby);
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
        Player player = new Player();
        player.steamId = SteamUser.GetSteamID();
        player.name = SteamFriends.GetPersonaName();
        player.avatar = SteamManager.GetLocalSteamAvatar(steamId);
        player.isHost = isHost;
        ReefRunLobbyController controller = FindAnyObjectByType<ReefRunLobbyController>();
        if (controller != null)
        {
            controller.AddPlayer(player);
        }
        else
            Debug.LogWarning("ReefRunLobbyController not found in the scene.");
    }
    #endregion
}
