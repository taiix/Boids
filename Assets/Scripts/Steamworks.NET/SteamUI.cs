using Steamworks;
using UnityEngine;

public class SteamUI : MonoBehaviour
{
    //Called when the user clicks the "Host Server" button in the Steam UI
    public void HostLobby()
    {
        if (!SteamManager.Initialized)
        {
            Debug.LogError("Steamworks is not initialized.");
            return;
        }
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4);
    }
}
