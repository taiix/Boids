using Mirror;
using Mirror.FizzySteam;

public class CustomNetworkManager : NetworkManager
{
    public override void Awake()
    {
        if(transport == null)
            transport = GetComponent<FizzySteamworks>();
        base.Awake();
    }
}
