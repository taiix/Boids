// Nearest orca to the shark (and keep the shark fed while it waits).
Feed();
var me = Me().transform.position; float best = 99999f; UnityEngine.Vector3 at = me;
foreach (var o in FishGame.OrcaHunter.All) if (o != null) { float dd = (o.transform.position - me).magnitude; if (dd < best) { best = dd; at = o.transform.position; } }
return "orcas " + FishGame.OrcaHunter.All.Count + " nearest " + best.ToString("0") + " event " + M.Event + " starts in " + (M.EventStart - FishGame.MatchManager.Now).ToString("0.0");
