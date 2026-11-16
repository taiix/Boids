// Fish: the wall has lifted - make a run for the gap through the bubble curtain.
var walls = FishGame.ArenaWalls.Instance;
var me = Me().transform.position;
var exit = walls.ExitPointNear(M.ExitWall, me);
var inward = me - exit; inward.y = 0f; if (inward.sqrMagnitude < 0.01f) inward = UnityEngine.Vector3.forward; inward.Normalize();
var from = exit + inward * 65f; from.y = exit.y + 1.5f;
Put(from, exit);
Feed();
var c = Rec("29_fish_escape", 7f, 0.3f);
c.goal = exit; c.throttle = 0.9f; c.boost = true; c.turnRate = 35f;
return "exit " + exit.ToString("F0") + " from " + from.ToString("F0") + " wall " + walls.NameOf(M.ExitWall);

