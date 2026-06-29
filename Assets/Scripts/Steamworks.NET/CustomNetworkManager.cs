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
}
