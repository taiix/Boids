// Editor-only tool, but it lives outside an Editor folder - without this guard, player builds fail to compile.
#if UNITY_EDITOR
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

    // Shortcut: Ctrl/Cmd + Shift + 4
    [MenuItem("Tools/Capture 4K Screenshot %#4")]
    static void Quick4K() => Capture4K(EditorPrefs.GetString("KeyArtFolder", DefaultFolder()));

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
            "Capture 4K renders the game camera straight to 3840 x 2160, whatever size the Game view is " +
            "(no HUD: clean shots for the store page). Shortcut: Ctrl+Shift+4.\n" +
            "Capture Game View saves the Game view at its own size, HUD included. Both work while paused.",
            MessageType.Info);

        if (GUILayout.Button("Capture 4K (3840 x 2160)", GUILayout.Height(40)))
            lastPath = Capture4K(folder);

        if (GUILayout.Button("Capture Game View", GUILayout.Height(28)))
            lastPath = CaptureGameView(folder);

        if (!string.IsNullOrEmpty(lastPath))
        {
            EditorGUILayout.LabelField("Last:", Path.GetFileName(lastPath));
            if (GUILayout.Button("Show in Folder"))
                EditorUtility.RevealInFinder(lastPath);
        }
    }

    const int Width4K = 3840, Height4K = 2160;

    // The camera the Game view shows: Camera.main if there is one (the game's camera isn't tagged), else the
    // game camera drawn last to the screen.
    static Camera GameCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled) return Camera.main;
        Camera best = null;
        foreach (var c in Camera.allCameras)
            if (c.targetTexture == null && c.cameraType == CameraType.Game && (best == null || c.depth >= best.depth)) best = c;
        return best;
    }

    /// <summary>Render the game camera at 3840 x 2160 into a PNG in <paramref name="dir"/>.</summary>
    public static string Capture4K(string dir)
    {
        var cam = GameCamera();
        if (cam == null) { Debug.LogWarning("[4K] No game camera to capture: enter Play mode or open a scene with a camera."); return null; }
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"Screenshot4K_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(Width4K, Height4K, RenderTextureFormat.ARGB32, 24) { sRGB = true });
        var prevTarget = cam.targetTexture;
        var tex = new Texture2D(Width4K, Height4K, TextureFormat.RGB24, false);
        try
        {
            // HDRP: a standard render request draws the whole pipeline (lighting, fog, post) into our texture.
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
            else
            {
                cam.targetTexture = rt;
                cam.Render();
            }
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width4K, Height4K), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally
        {
            cam.targetTexture = prevTarget;
            RenderTexture.ReleaseTemporary(rt);
            DestroyImmediate(tex);
        }
        Debug.Log($"4K screenshot saved: {path}");
        return path;
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
#endif
