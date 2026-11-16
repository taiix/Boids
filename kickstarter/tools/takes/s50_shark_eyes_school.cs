// Shark: stalks slowly into the big school, sizing it up, while the camera swings from behind to its flank.
Calm();
Extend();
var s = BigSchool();
var from = s + new UnityEngine.Vector3(-12f, 2f, -12f);
Put(from, s);
Feed();
var c = Rec("50_shark_eyes_school", 9f, 0.6f);
c.hunt = true; c.throttle = 0.3f; c.boost = false; c.lungeRange = 0f; c.turnRate = 22f; c.sonarAt = 1.2f;
c.freeYawFrom = 0f; c.freeYawTo = -80f; c.freePitch = 6f;
return "shark from " + from.ToString("F0") + " school n=" + schoolSize;
