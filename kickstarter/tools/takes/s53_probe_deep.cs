// Where is the water deepest inside the arena? (for the breach)
var ws = UnityEngine.Object.FindAnyObjectByType<UnityEngine.Rendering.HighDefinition.WaterSurface>();
float wy = ws != null ? ws.transform.position.y : 0f;
var best = UnityEngine.Vector3.zero; float bestD = 0f;
for (float x = -140f; x <= 140f; x += 10f)
for (float z = -480f; z <= -200f; z += 10f)
{
    var p = new UnityEngine.Vector3(x, 0f, z); float d = wy - Ground(p);
    if (d > bestD) { bestD = d; best = p; }
}
var sb = BigSchool();
return "water y=" + wy + " deepest " + best.ToString("F0") + " depth=" + bestD.ToString("F1") + " | school " + sb.ToString("F0") + " depth there=" + (wy - Ground(sb)).ToString("F1");
