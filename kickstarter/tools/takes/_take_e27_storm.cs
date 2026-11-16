// ---- shared staging helpers (prepended to every take) ----
var BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
UnityEngine.Application.runInBackground = true;
FishPlayer Me() { foreach (var fp in FishPlayer.All) if (fp != null && fp.isLocalPlayer) return fp; return null; }
object Get(object o, string f)
{
    var t = o as System.Type ?? o.GetType(); object inst = o is System.Type ? null : o;
    var fi = t.GetField(f, BF); if (fi != null) return fi.GetValue(inst);
    var pi = t.GetProperty(f, BF); if (pi != null) return pi.GetValue(inst);
    var mi = t.GetMethod(f, BF, null, System.Type.EmptyTypes, null); return mi?.Invoke(inst, null);
}
void Set(object o, string f, object v) { o.GetType().GetField(f, BF).SetValue(o, v); }
// The seabed under p: the lowest solid surface a ray from high above meets (fish, nets and water are higher).
float Ground(UnityEngine.Vector3 p)
{
    float best = float.MaxValue;
    foreach (var h in UnityEngine.Physics.RaycastAll(new UnityEngine.Vector3(p.x, 400f, p.z), UnityEngine.Vector3.down, 1200f, ~0, UnityEngine.QueryTriggerInteraction.Ignore))
        if (h.rigidbody == null && h.point.y < best) best = h.point.y;
    return best == float.MaxValue ? p.y - 8f : best;
}
UnityEngine.Vector3 Above(UnityEngine.Vector3 p, float h) => new UnityEngine.Vector3(p.x, Ground(p) + h, p.z);
FishGame.FishOrbitCamera Cam()
{
    var me = Me(); var c = me != null ? me.GetComponentInChildren<FishGame.FishOrbitCamera>(true) : null;
    return c != null ? c : UnityEngine.Object.FindAnyObjectByType<FishGame.FishOrbitCamera>();
}
// Teleport the local body (transform AND rigidbody, or the interpolated body snaps back) and aim the camera.
void Put(UnityEngine.Vector3 pos, UnityEngine.Vector3 look)
{
    var me = Me();
    var d = look - pos; if (d.sqrMagnitude < 0.01f) d = me.transform.forward;
    var rot = UnityEngine.Quaternion.LookRotation(d.normalized, UnityEngine.Vector3.up);
    me.transform.SetPositionAndRotation(pos, rot);
    var rb = me.GetComponent<UnityEngine.Rigidbody>();
    if (rb != null) { rb.position = pos; rb.rotation = rot; rb.linearVelocity = UnityEngine.Vector3.zero; rb.angularVelocity = UnityEngine.Vector3.zero; }
    var cam = Cam(); var e = rot.eulerAngles;
    if (cam != null) { Set(cam, "_yaw", e.y); Set(cam, "_pitch", UnityEngine.Mathf.Clamp(UnityEngine.Mathf.DeltaAngle(0f, e.x), -40f, 40f)); }
}
void Feed()
{
    var v = Me().GetComponent<FishGame.FishVitals>(); if (v == null) return;
    Set(v, "_hunger", Get(v, "maxHunger")); Set(v, "_health", Get(v, "maxHealth"));
}
var allFish = new System.Collections.Generic.List<UnityEngine.Vector3>();
foreach (var bm in BoidsManager.All)
    if (bm != null && bm.allFish != null)
        foreach (var f in bm.allFish) if (f != null && f.activeInHierarchy) allFish.Add(f.transform.position);
// Centre of the school nearest p: the nearest school fish at least minDist away, averaged with its
// neighbours within 12 m. SchoolSize says how many fish that is.
int schoolSize = 0;
UnityEngine.Vector3 School(UnityEngine.Vector3 p, float minDist = 0f)
{
    float best = float.MaxValue; UnityEngine.Vector3 seed = p;
    foreach (var f in allFish) { float dd = (f - p).sqrMagnitude; if (dd >= minDist * minDist && dd < best) { best = dd; seed = f; } }
    UnityEngine.Vector3 sum = UnityEngine.Vector3.zero; int n = 0;
    foreach (var f in allFish) if ((f - seed).sqrMagnitude < 144f) { sum += f; n++; }
    schoolSize = n;
    return n > 0 ? sum / n : seed;
}
// The biggest school (most fish within 12 m of each other), sampling every 7th fish as a seed.
UnityEngine.Vector3 BigSchool()
{
    int bestN = 0; UnityEngine.Vector3 at = UnityEngine.Vector3.zero;
    for (int i = 0; i < allFish.Count; i += 7)
    {
        UnityEngine.Vector3 sum = UnityEngine.Vector3.zero; int n = 0;
        foreach (var f in allFish) if ((f - allFish[i]).sqrMagnitude < 144f) { sum += f; n++; }
        if (n > bestN) { bestN = n; at = sum / n; }
    }
    schoolSize = bestN;
    return at;
}
FishGame.GameplayClip Rec(string name, float seconds, float warmup = 0.4f)
{
    var c = FishGame.GameplayClip.Record(name, seconds, warmup, 60, true);
    c.hunt = false; c.boost = false; c.throttle = 0.6f; c.lungeRange = 0f;
    return c;
}
var M = FishGame.MatchManager.Instance;
var WE = typeof(FishGame.WorldEvents).GetField("s_instance", BF).GetValue(null);
var RC = typeof(FishGame.ReefConditions).GetField("s_instance", BF).GetValue(null);
// End whatever event is on and keep the scheduler from starting another for a long while.
void Calm()
{
    double now = FishGame.MatchManager.Now;
    M.ServerStartEvent(FishGame.WorldEventKind.None, now, now, UnityEngine.Vector3.zero, 0);
    Set(WE, "_nextAt", now + 100000.0);
}
// Keep the survival clock from running out mid-session (the escape is recorded on purpose, last).
void Extend(double seconds = 540.0) { if (M.Phase == FishGame.MatchPhase.Playing) Set(M, "_roundEndTime", FishGame.MatchManager.Now + seconds); }
// Start an event now (the game's own test path), then hold the scheduler back again afterwards.
void Event(FishGame.WorldEventKind k) { FishGame.WorldEvents.DevStart(k); }
// ---- take ----

// Storm: the fish races over the school near the surface while lightning lights the reef and the water churns.
var s = BigSchool();
var from = s + new UnityEngine.Vector3(-20f, 6f, -8f);
var to = s + new UnityEngine.Vector3(14f, 3f, 10f);
Put(from, to);
Feed();
var c = Rec("27_fish_storm", 8f, 0.6f);
c.goal = to; c.hunt = false; c.throttle = 1f; c.boost = true; c.turnRate = 35f;
c.freeYawFrom = 25f; c.freeYawTo = -20f; c.freePitch = -6f;
return "event " + M.Event + " from " + from.ToString("F0");

