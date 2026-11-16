// Trawler: wait position ahead of the net, facing the way it comes from. Returns how far away the net is.
var trawl = Get(WE, "_trawl") as UnityEngine.Component;
var layout = Get(WE, "_layout");
var start = (UnityEngine.Vector3)Get(layout, "start");
var travel = (UnityEngine.Vector3)Get(layout, "travel");
float bottom = (float)Get(layout, "bottom"), top = (float)Get(layout, "top");
UnityEngine.Vector3 net = trawl != null ? trawl.transform.position : start;
return "event " + M.Event + " starts in " + (M.EventStart - FishGame.MatchManager.Now).ToString("0.0") + "s net " + net.ToString("F0")
    + " travel " + travel.ToString("F2") + " band " + bottom.ToString("F0") + ".." + top.ToString("F0");
