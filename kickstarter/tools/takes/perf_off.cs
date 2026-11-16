// Restore the Editor after a recording session.
var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor");
foreach (UnityEditor.EditorWindow w in UnityEngine.Resources.FindObjectsOfTypeAll(gv)) w.maximized = false;
Unity.Burst.BurstCompiler.Options.EnableBurstSafetyChecks = true;
Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = true;
Unity.Collections.NativeLeakDetection.Mode = Unity.Collections.NativeLeakDetectionMode.Enabled;
return "editor restored";
