// Storm: the fish races over the school near the surface while lightning lights the reef and the water churns.
var s = BigSchool();
var from = s + new UnityEngine.Vector3(-20f, 6f, -8f);
var to = s + new UnityEngine.Vector3(14f, 3f, 10f);
Put(from, to);
Feed();
var c = Rec("27_fish_storm", 8f, 0.6f);
c.goal = to; c.hunt = false; c.throttle = 1f; c.boost = true; c.turnRate = 35f;
c.freeYawFrom = 25f; c.freeYawTo = -20f; c.freePitch = -6f;
return "event " + M.Event + " from " + from.ToString("F0");
