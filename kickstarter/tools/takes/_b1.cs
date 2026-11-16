// Shark: rises from below at full speed, lunges and breaches clear of the surface, then falls back in.
Calm();
Extend();
var ws = UnityEngine.Object.FindAnyObjectByType<UnityEngine.Rendering.HighDefinition.WaterSurface>();
float wy = ws != null ? ws.transform.position.y : 0f;
var spot = new UnityEngine.Vector3(60f, 0f, -260f);
var dir = new UnityEngine.Vector3(1f, 0f, 0.4f).normalized;
var from = new UnityEngine.Vector3(spot.x, wy - 14f, spot.z);
Put(from, from + dir * 10f + UnityEngine.Vector3.up * 9f);
Feed();
var c = Rec("53_shark_breach", 7f, 0.4f);
c.hunt = false; c.goal = from + dir * 18f + UnityEngine.Vector3.up * 60f; c.boost = true; c.throttle = 1f;
c.pitchLimit = 55f; c.turnRate = 120f; c.lungeRange = 0f; c.lungeAt = 0.8f;
return "breach from " + from.ToString("F1") + " water y=" + wy;
