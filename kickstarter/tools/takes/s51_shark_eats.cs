// Shark: closes on the school from behind, lunges and eats a fish.
Calm();
Extend();
var s = BigSchool();
var from = s + new UnityEngine.Vector3(-13f, 1.5f, -8f);
Put(from, s);
Feed();
var c = Rec("51_shark_eats", 7f, 0.4f);
c.hunt = true; c.boost = true; c.throttle = 1f; c.lungeRange = 8f; c.turnRate = 80f;
return "shark from " + from.ToString("F0") + " school n=" + schoolSize;
