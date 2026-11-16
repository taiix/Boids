var go = UnityEngine.GameObject.Find("__fps") ?? new UnityEngine.GameObject("__fps");
return UnityEngine.Time.frameCount + " " + UnityEngine.Time.realtimeSinceStartupAsDouble.ToString("F3") + " vsync=" + UnityEngine.QualitySettings.vSyncCount + " target=" + UnityEngine.Application.targetFrameRate + " dt=" + UnityEngine.Time.smoothDeltaTime.ToString("F4");
