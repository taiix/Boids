// Fish: swims up to the big school and blends into it.
var s = BigSchool();
var from = s + new UnityEngine.Vector3(14f, 1.5f, -12f);
Put(from, s);
Feed();
var c = Rec("25_fish_join_school", 7f, 0.6f);
c.hunt = true; c.throttle = 0.75f; c.boost = false; c.blendAt = 2.6f; c.turnRate = 60f;
return "recording from " + from.ToString("F0") + " to school n=" + schoolSize;
