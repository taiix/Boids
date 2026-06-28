using Mirror;
using Mirror.FizzySteam;
using UnityEngine;

public class CustomNetworkManager : NetworkManager
{
    public Transform startPoint;

    public override void Awake()
    {
        if (transport == null)
            transport = GetComponent<FizzySteamworks>();
        base.Awake();
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        Instantiate(playerPrefab, startPoint.position, Quaternion.identity);
        NetworkServer.AddPlayerForConnection(conn, playerPrefab);
    }
}
