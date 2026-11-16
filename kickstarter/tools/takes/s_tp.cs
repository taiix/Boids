// Back to third person (the POV take flips the camera's static first-person flag).
typeof(FishGame.FishOrbitCamera).GetField("s_firstPerson", BF).SetValue(null, false);
return "third person";
