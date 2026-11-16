// Cold layer: the fish swims down into the cold band (it slows inside, and the water hazes).
float y = (float)Get(RC, "_coldY");
var s = BigSchool();
var from = new UnityEngine.Vector3(s.x - 16f, y + 9f, s.z - 10f);
var to = new UnityEngine.Vector3(s.x + 4f, y, s.z + 4f);
Put(from, to);
Feed();
var c = Rec("42_event_cold_layer", 7f, 0.8f);
c.goal = to; c.throttle = 0.8f; c.turnRate = 30f;
return "event " + M.Event + " coldY " + y.ToString("F1") + " ground " + Ground(to).ToString("F1");
