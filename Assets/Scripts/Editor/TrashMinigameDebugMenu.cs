using FishGame;
using UnityEditor;

/// <summary>Play-mode test switch for the trash minigame UI, without wiring it to any trash.</summary>
static class TrashMinigameDebugMenu
{
    const string Path = "Tools/Reef Run/Toggle Trash Minigame %#t";   // Ctrl+Shift+T

    [MenuItem(Path)]
    static void Toggle()
    {
        if (TrashMinigameUI.IsOpen) TrashMinigameUI.Close();
        else TrashMinigameUI.Open();
    }

    [MenuItem(Path, true)]
    static bool Validate() => EditorApplication.isPlaying;
}
