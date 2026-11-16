// Fish at night: swims along the reef past the school; anything moving leaves a glowing plankton trail.
var s = BigSchool();
var from = Above(s + new UnityEngine.Vector3(-22f, 0f, -6f), 4f);
var to = Above(s + new UnityEngine.Vector3(10f, 0f, 8f), 5f);
Put(from, to);
Feed();
var c = Rec("26_fish_nightfall", 7f, 1.0f);
c.goal = to; c.throttle = 0.85f; c.boost = true; c.turnRate = 30f;
return "event " + M.Event + " from " + from.ToString("F0");
