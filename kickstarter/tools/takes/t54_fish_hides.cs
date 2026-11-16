// Fish: swims into the big school and vanishes into it; the camera swings round to the side as it blends.
Calm();
Extend();
var s = BigSchool();
var from = s + new UnityEngine.Vector3(-6f, 0.5f, 5f);
Put(from, s);
Feed();
var c = Rec("54_fish_hides", 7f, 0.6f);
c.hunt = true; c.throttle = 0.8f; c.boost = true; c.blendAt = 1.1f; c.turnRate = 60f;
c.freeYawFrom = 0f; c.freeYawTo = 60f; c.freePitch = 10f;
return "fish from " + from.ToString("F0") + " school n=" + schoolSize;
