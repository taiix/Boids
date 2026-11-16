// The game's F7 path: run the survival clock out now, so a wall lifts.
Calm();
typeof(FishGame.MatchManager).GetMethod("OpenExit", BF).Invoke(M, null);
return "phase " + M.Phase + " wall " + M.ExitWall;
