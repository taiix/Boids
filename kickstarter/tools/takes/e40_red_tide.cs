// Fish swims into the red tide: rust cloud ahead, then red murk and the "toxic water" warning.
var tide = (UnityEngine.Vector3)Get(RC, "TideCentre");
var dir = (UnityEngine.Vector3)Get(RC, "_dir"); dir.y = 0f; if (dir.sqrMagnitude < 0.01f) dir = UnityEngine.Vector3.forward; dir.Normalize();
// Come in from the side the cloud drifts toward, so it moves into the camera.
var from = tide + dir * 45f; from.y = tide.y - 6f; var aim = new UnityEngine.Vector3(tide.x, tide.y - 3.5f, tide.z);
Put(from, aim);
Feed();
var c = Rec("40_event_red_tide", 10f, 0.8f);
c.goal = aim; c.throttle = 1f; c.boost = true; c.turnRate = 30f;
return "event " + M.Event + " tide " + tide.ToString("F0") + " ground " + Ground(tide).ToString("F0") + " from " + from.ToString("F0");


