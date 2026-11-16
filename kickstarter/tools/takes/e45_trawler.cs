// Trawler: the fish waits in the net's path, facing it, and swims slowly into the sweeping net.
var layout = Get(WE, "_layout");
var start = (UnityEngine.Vector3)Get(layout, "start");
var travel = (UnityEngine.Vector3)Get(layout, "travel");
float distance = (float)Get(layout, "distance");
double now = FishGame.MatchManager.Now;
float speed = distance / (float)System.Math.Max(1.0, M.EventEnd - M.EventStart);
float moved = (float)System.Math.Max(0.0, now - M.EventStart) * speed;
var net = start + travel * moved;                         // where the net's top edge is now
float y = start.y - 14f;
// Where the net will be ~3.5 s into the take, plus 10 m: the fish meets it about then.
var meet = net + travel * (speed * (float)System.Math.Max(0.0, M.EventStart - now) + speed * 3.5f);
var from = new UnityEngine.Vector3(meet.x, y, meet.z) + travel * 10f;
var face = new UnityEngine.Vector3(net.x, y, net.z);
Put(from, face);
Feed();
var c = Rec("45_event_trawler", 7f, 0.3f);
c.goal = face; c.throttle = 0.25f; c.turnRate = 20f;
return "net speed " + speed.ToString("F1") + " m/s, starts in " + (M.EventStart - now).ToString("0.0") + "s, fish at " + from.ToString("F0") + " net now " + net.ToString("F0");
