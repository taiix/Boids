var sl = SteamLobby.instance;
var f = typeof(SteamLobby).GetField("_currentLobbyId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var nm = typeof(SteamLobby).GetField("networkManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
return "lobbyObj=" + (sl != null) + " lobbyId=" + (sl != null ? f.GetValue(sl).ToString() : "-")
    + " nm=" + (sl != null && nm.GetValue(sl) != null) + " steam=" + SteamManager.Initialized
    + " server=" + Mirror.NetworkServer.active + " frame=" + UnityEngine.Time.frameCount + " t=" + UnityEngine.Time.realtimeSinceStartup.ToString("0");
