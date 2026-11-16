// Presses a UI Toolkit button: runs the handlers wired to its Clickable.clicked, which is what a mouse click
// or the gamepad's submit ends up calling. (A synthetic NavigationSubmitEvent no longer reaches them.)
UnityEngine.Application.runInBackground = true;
string want = "ready-btn";
foreach (var doc in UnityEngine.Object.FindObjectsByType<UnityEngine.UIElements.UIDocument>(UnityEngine.FindObjectsSortMode.None))
{
    if (doc == null || doc.rootVisualElement == null) continue;
    var btn = UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(doc.rootVisualElement, want);
    if (btn == null || !btn.enabledInHierarchy || btn.resolvedStyle.display == UnityEngine.UIElements.DisplayStyle.None) continue;
    var click = btn.clickable;
    var field = typeof(UnityEngine.UIElements.Clickable).GetField("clicked",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var handlers = field?.GetValue(click) as System.Action;
    if (handlers == null) return "no clicked handlers on " + want;
    handlers.Invoke();
    return "pressed " + want + " in " + doc.gameObject.name + " (" + handlers.GetInvocationList().Length + " handlers)";
}
return "no visible " + want;

