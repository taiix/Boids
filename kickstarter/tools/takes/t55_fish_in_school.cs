// Fish: blends into the school straight away and rides with it while the camera circles, so the player is one
// fish among hundreds.
Calm();
Extend();
var s = BigSchool();
var from = s + new UnityEngine.Vector3(3f, 0.5f, -3f);
Put(from, s);
Feed();
var c = Rec("55_fish_in_school", 6f, 0.6f);
c.hunt = true; c.throttle = 0.5f; c.boost = false; c.turnRate = 40f; c.blendAt = 0.05f;
c.freeYawFrom = 160f; c.freeYawTo = 40f; c.freePitch = 12f;
return "fish in school at " + from.ToString("F0") + " n=" + schoolSize;
