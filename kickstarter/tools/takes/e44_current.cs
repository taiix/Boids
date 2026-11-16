// Strong current: the fish lets the flow carry it, looking straight across it, low over the reef next to the
// school, so the seabed slides past sideways and the school streams along with it.
var s = BigSchool();
var flow = M.EventDirection; flow.y = 0f; if (flow.sqrMagnitude < 0.01f) flow = UnityEngine.Vector3.right; flow.Normalize();
var across = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, flow);
var from = Above(s - across * 13f - flow * 6f, 4.5f);
var look = from + across * 1000f + UnityEngine.Vector3.down * 120f;
Put(from, look);
Feed();
var c = Rec("44_event_current", 7f, 0.5f);
c.goal = look; c.throttle = 0f; c.boost = false; c.turnRate = 10f;
return "event " + M.Event + " flow " + flow.ToString("F1") + " school n=" + schoolSize + " from " + from.ToString("F0");
