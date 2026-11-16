UnityEngine.FrameTimingManager.CaptureFrameTimings();
var ft = new UnityEngine.FrameTiming[8]; uint n = UnityEngine.FrameTimingManager.GetLatestTimings(8, ft);
var sb = new System.Text.StringBuilder();
for (int i = 0; i < n; i++) sb.Append($"cpu={ft[i].cpuFrameTime:F1} main={ft[i].cpuMainThreadFrameTime:F1} render={ft[i].cpuRenderThreadFrameTime:F1} gpu={ft[i].gpuFrameTime:F1} | ");
var wins = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEditor.EditorWindow>();
foreach (var w in wins) sb.Append(w.GetType().Name + (w.maximized ? "(MAX)" : "") + ",");
return sb.ToString();
