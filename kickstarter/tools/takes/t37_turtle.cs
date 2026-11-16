// Fish: swims slowly up to a tangled turtle (the rescue task), so its rope prompt shows.
Calm();
UnityEngine.Transform best = null; float bd = float.MaxValue;
var me0 = Me().transform.position;
foreach (var sw in UnityEngine.Object.FindObjectsByType<FishGame.TurtleSwimmer>(UnityEngine.FindObjectsSortMode.None))
    if (sw.name.StartsWith("TangledTurtle") && (sw.transform.position - me0).sqrMagnitude < bd) { bd = (sw.transform.position - me0).sqrMagnitude; best = sw.transform; }
if (best == null) return "no tangled turtle";
var t = best.position;
// Approach from the open side: away from the nearest school so the turtle isn't hidden behind fish.
var away = t - School(t); away.y = 0f; if (away.sqrMagnitude < 0.01f) away = UnityEngine.Vector3.forward; away.Normalize();
var side = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, away);
var from = t + away * 8f + side * 2f + UnityEngine.Vector3.up * 2.2f;
Put(from, t);
Feed();
var c = Rec("37_fish_turtle", 5f, 0.6f);
c.goal = t + UnityEngine.Vector3.up * 1.0f; c.throttle = 0.28f; c.turnRate = 40f;
return "tangled turtle at " + t.ToString("F0") + " from " + from.ToString("F0");

