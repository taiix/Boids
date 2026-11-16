// Recording prep: only the Game view renders, no vsync cap, no Profiler, no Editor-only job safety checks.
// (Undo with perf_off.cs after the session.)
var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor");
var w = UnityEditor.EditorWindow.GetWindow(gv);
w.maximized = true;
UnityEngine.QualitySettings.vSyncCount = 0;
UnityEditorInternal.ProfilerDriver.enabled = false;
Unity.Burst.BurstCompiler.Options.EnableBurstSafetyChecks = false;
Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobDebuggerEnabled = false;
Unity.Collections.NativeLeakDetection.Mode = Unity.Collections.NativeLeakDetectionMode.Disabled;
UnityEngine.Application.runInBackground = true;
return "perf: game view max=" + w.maximized + " " + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height;
