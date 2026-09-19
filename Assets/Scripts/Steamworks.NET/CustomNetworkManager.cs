using Mirror;
using Mirror.FizzySteam;
using UnityEngine;

public class CustomNetworkManager : NetworkManager
{
    private bool _gameStarted = false;
    private string gameSceneName = "Island";

    public override void Awake()
    {
        if (transport == null)
            transport = GetComponent<FizzySteamworks>();
        base.Awake();
    }

    public override void OnServerSceneChanged(string newSceneName)
    {
        base.OnServerChangeScene(newSceneName);
        _gameStarted = newSceneName == gameSceneName;
    }

    public override void OnServerReady(NetworkConnectionToClient conn)
    {
        base.OnServerReady(conn);
        if (!_gameStarted || conn.identity != null) return;        // skip lobby & double-spawn
        Transform start = GetStartPosition();                       // cycles NetworkStartPositions
        GameObject go = start != null
            ? Instantiate(playerPrefab, start.position, start.rotation)
            : Instantiate(playerPrefab);
        NetworkServer.AddPlayerForConnection(conn, go);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only: start a solo host in the CURRENT game scene without the Steam lobby. Called by
    /// <see cref="FishGame.DevHost"/> when you press Play directly in the Island scene. Sets
    /// <c>_gameStarted</c> so OnServerReady spawns the player even though no scene change occurred.
    /// </summary>
    public void DevStartHost(Transport devTransport)
    {
        if (NetworkServer.active || NetworkClient.active) return;
        if (devTransport != null) { transport = devTransport; Transport.active = devTransport; }
        _gameStarted = true; // we're already in the game scene, so allow player spawns
        StartHost();
    }
#endif
}
