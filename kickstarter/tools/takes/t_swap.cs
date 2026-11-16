// The game's F8 path: swap the host player's role (fish <-> shark).
Calm();
Extend();
((CustomNetworkManager)Mirror.NetworkManager.singleton).ServerSwitchRole(Mirror.NetworkServer.localConnection);
return "role swap requested";
