var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor");
var w = UnityEditor.EditorWindow.GetWindow(gv);
w.maximized = true;
foreach (var p in UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.ProfilerWindow>()) { }
UnityEngine.Profiling.Profiler.enabled = false;
UnityEditorInternal.ProfilerDriver.enabled = false;
return "max=" + w.maximized + " size=" + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height;
