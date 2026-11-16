// Shark, first person: the bite seen from the shark's own eyes.
Calm();
Extend();
typeof(FishGame.FishOrbitCamera).GetField("s_firstPerson", BF).SetValue(null, true);
var s = BigSchool();
var from = s + new UnityEngine.Vector3(6f, 0.5f, -5f);
Put(from, s);
Feed();
var c = Rec("52_shark_eats_pov", 4f, 0.4f);
c.hunt = true; c.boost = true; c.throttle = 1f; c.lungeRange = 7f; c.turnRate = 80f;
return "pov shark from " + from.ToString("F0") + " school n=" + schoolSize;
