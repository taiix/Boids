// Saves what a player sees (UI included) to the scratchpad; the file appears at the end of the next frame.
string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claude", "183ce416447f2773", "00adc857-5187-476b-933b-d74354eb0fec", "scratchpad", "rec", "screen.png");
UnityEngine.ScreenCapture.CaptureScreenshot(path, 1);
var names = new System.Collections.Generic.List<string>();
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) names.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
var docs = new System.Collections.Generic.List<string>();
foreach (var doc in UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(UnityEngine.FindObjectsSortMode.None))
    docs.Add(doc.gameObject.name + (doc.isActiveAndEnabled ? "" : "(off)"));
return "scenes: " + string.Join(", ", names) + " | docs: " + string.Join(", ", docs);
