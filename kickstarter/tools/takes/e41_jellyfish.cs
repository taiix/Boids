// Fish drifts slowly toward the densest cluster of the jellyfish swarm.
var root = (UnityEngine.Transform)Get(RC, "_jellyRoot");
if (root == null || root.childCount == 0) return "no jellyfish yet";
var js = new System.Collections.Generic.List<UnityEngine.Vector3>();
foreach (UnityEngine.Transform j in root) if (j.gameObject.activeInHierarchy) js.Add(j.position);
int bestN = 0; UnityEngine.Vector3 at = js[0];
foreach (var a in js) { int n = 0; UnityEngine.Vector3 sum = UnityEngine.Vector3.zero; foreach (var b in js) if ((a - b).sqrMagnitude < 100f) { n++; sum += b; } if (n > bestN) { bestN = n; at = sum / n; } }
var from = at + new UnityEngine.Vector3(9f, -1.5f, -7f);
Put(from, at);
Feed();
var c = Rec("41_event_jellyfish", 7f, 0.8f);
c.goal = at; c.throttle = 0.3f; c.turnRate = 25f;
return "event " + M.Event + " jellies " + js.Count + " cluster n=" + bestN + " at " + at.ToString("F0");
