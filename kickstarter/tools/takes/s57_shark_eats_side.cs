// Shark: closes on the school from behind, lunges and eats a fish.
Calm();
Extend();
var s = BigSchool();
var from = s + new UnityEngine.Vector3(9f, 1.5f, -12f);
Put(from, s);
Feed();
var c = Rec("57_shark_eats_side", 7f, 0.4f);
c.hunt = true; c.boost = true; c.throttle = 1f; c.lungeRange = 8f; c.turnRate = 80f;
c.freeYawFrom = 55f; c.freeYawTo = 35f; c.freePitch = 4f;
return "shark from " + from.ToString("F0") + " school n=" + schoolSize;
