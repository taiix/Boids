// The quake: the view shakes and sand clouds rise off the seabed around the fish.
var me = Me().transform.position;
var s = BigSchool();
var from = Above(s + new UnityEngine.Vector3(-18f, 0f, 12f), 2.5f);
var to = Above(s + new UnityEngine.Vector3(6f, 0f, -4f), 2f);
Put(from, to);
Feed();
var c = Rec("43_event_sandslide", 7f, 0.2f);
c.goal = to; c.throttle = 0.5f; c.turnRate = 25f;
return "event " + M.Event + " from " + from.ToString("F0");
