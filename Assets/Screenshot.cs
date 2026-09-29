using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class KeyArtCaptureWindow : EditorWindow
{
    string folder;
    string lastPath;

    [MenuItem("Tools/Key Art Capture")]
    static void Open() => GetWindow<KeyArtCaptureWindow>("Key Art Capture");

    // Shortcut: Ctrl/Cmd + Shift + K
    [MenuItem("Tools/Capture Game View %#k")]
    static void QuickCapture() => CaptureGameView(DefaultFolder());

    void OnEnable() => folder = EditorPrefs.GetString("KeyArtFolder", DefaultFolder());

    void OnGUI()
    {
        EditorGUILayout.LabelField("Save folder", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        folder = EditorGUILayout.TextField(folder);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            var picked = EditorUtility.OpenFolderPanel("Save screenshots to", folder, "");
            if (!string.IsNullOrEmpty(picked)) folder = picked;
        }
        EditorGUILayout.EndHorizontal();
        EditorPrefs.SetString("KeyArtFolder", folder);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Captures the Game view at its set resolution.\n" +
            "Set the Game view to 3840x2160 for 4K. Works while paused.",
            MessageType.Info);

        if (GUILayout.Button("Capture Game View", GUILayout.Height(40)))
            lastPath = CaptureGameView(folder);

        if (!string.IsNullOrEmpty(lastPath))
        {
            EditorGUILayout.LabelField("Last:", Path.GetFileName(lastPath));
            if (GUILayout.Button("Show in Folder"))
                EditorUtility.RevealInFinder(lastPath);
        }
    }

    static string DefaultFolder() =>
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    static string CaptureGameView(string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"KeyArt_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        ScreenCapture.CaptureScreenshot(path);

        // Force the Game view to render a frame so the capture happens even when paused
        var gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
        foreach (var gv in Resources.FindObjectsOfTypeAll(gameViewType))
            ((EditorWindow)gv).Repaint();

        Debug.Log($"Key art saved: {path}");
        return path;
    }
}