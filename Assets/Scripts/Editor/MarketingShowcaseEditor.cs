using System.Collections.Generic;
using System.IO;
using FishGame;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Tools > Marketing: the Steam screenshot workflow around <see cref="MarketingShowcase"/>.
///   * Open the showcase scene, show / capture one shot or all of them (Play mode is entered for you),
///     step through shots, build a contact sheet, open the output folder.
///   * Shot inspector: set the shot's camera from the Scene view (or move the Scene view to it), and
///     Show / Capture buttons in Play mode.
///   * Create Showcase Scene: rebuilds the scene from IslandTest (the island as the game plays it) minus
///     the network players, HUD canvases and randomly placed task props. It does not touch IslandTest.
/// Screenshots go to Marketing/Screenshots next to Assets, numbered in shot order.
/// </summary>
static class MarketingShowcaseMenu
{
    public const string ScenePath = "Assets/Scenes/MarketingShowcase.unity";
    const string SourceScene = "Assets/Scenes/IslandTest.unity";
    const string Menu = "Tools/Marketing/";

    [MenuItem(Menu + "Open Showcase Scene", false, 0)]
    static void OpenScene()
    {
        if (!File.Exists(ScenePath)) { Debug.LogWarning($"[Marketing] {ScenePath} doesn't exist yet: use Create Showcase Scene."); return; }
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem(Menu + "Show Current Shot", false, 20)]
    static void ShowCurrent() => Request("show", Selected());

    [MenuItem(Menu + "Next Shot %&]", false, 21)]
    static void Next() => Step(+1);

    [MenuItem(Menu + "Previous Shot %&[", false, 22)]
    static void Previous() => Step(-1);

    [MenuItem(Menu + "Capture Current Shot %&k", false, 40)]
    static void CaptureCurrent() => Request("capture", Selected());

    [MenuItem(Menu + "Capture All Shots", false, 41)]
    static void CaptureAll() => Request("all", 0);

    [MenuItem(Menu + "Capture Capsule Plates", false, 43)]
    static void CapturePlates() => Request("plates", 0);

    [MenuItem(Menu + "Resume Time", false, 42)]
    static void Resume() { if (MarketingShowcase.Instance != null) MarketingShowcase.Instance.Resume(); }

    [MenuItem(Menu + "Build Contact Sheet", false, 60)]
    static void ContactSheet() => BuildContactSheet();

    [MenuItem(Menu + "Open Screenshots Folder", false, 61)]
    static void OpenFolder()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Marketing", "Screenshots"));
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir + Path.DirectorySeparatorChar);
    }

    // The shot to act on: the selected one, else the one on screen, else the director's start shot.
    static int Selected()
    {
        var director = Director();
        if (director == null) return 0;
        var shots = director.Shots;
        if (Selection.activeGameObject != null)
        {
            var s = Selection.activeGameObject.GetComponentInParent<ShowcaseShot>(true);
            if (s != null && shots.IndexOf(s) >= 0) return shots.IndexOf(s);
        }
        if (Application.isPlaying && director.Current >= 0) return director.Current;
        return director.startShot;
    }

    static void Step(int d)
    {
        var director = Director();
        if (director == null) return;
        int n = director.Shots.Count;
        if (n == 0) return;
        int i = ((Application.isPlaying && director.Current >= 0 ? director.Current : Selected()) + d + n) % n;
        Selection.activeGameObject = director.Shots[i].gameObject;
        if (Application.isPlaying) director.Show(i);
        else SceneView.lastActiveSceneView?.AlignViewToObject(director.Shots[i].transform);
    }

    static MarketingShowcase Director() =>
        MarketingShowcase.Instance != null ? MarketingShowcase.Instance : Object.FindAnyObjectByType<MarketingShowcase>(FindObjectsInactive.Include);

    /// <summary>Run a request now in Play mode, or enter Play mode in the showcase scene and run it there.</summary>
    public static void Request(string what, int shot)
    {
        if (Application.isPlaying)
        {
            var d = MarketingShowcase.Instance;
            if (d == null) { Debug.LogWarning("[Marketing] No MarketingShowcase in the playing scene."); return; }
            if (what == "all") d.CaptureAll();
            else if (what == "plates") d.CaptureAllPlates();
            else if (what == "capture") d.Capture(shot);
            else d.Show(shot);
            return;
        }
        if (Director() == null)
        {
            if (!File.Exists(ScenePath) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        SessionState.SetString(MarketingShowcase.PendingKey, what == "all" || what == "plates" ? what : $"{what}:{shot}");
        EditorApplication.EnterPlaymode();
    }

    // ------------------------------------------------------------------ contact sheet

    /// <summary>All screenshots in one image (3 across, in file order) at Marketing/ContactSheet.png.</summary>
    public static string BuildContactSheet()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string dir = Path.Combine(root, "Marketing", "Screenshots");
        if (!Directory.Exists(dir)) { Debug.LogWarning("[Marketing] No screenshots yet."); return null; }
        var files = new List<string>(Directory.GetFiles(dir, "*.png"));
        files.Sort(System.StringComparer.OrdinalIgnoreCase);
        if (files.Count == 0) { Debug.LogWarning("[Marketing] No screenshots yet."); return null; }

        const int cols = 3, tw = 960, th = 540, pad = 24;
        int rows = (files.Count + cols - 1) / cols;
        int W = cols * tw + (cols + 1) * pad, H = rows * th + (rows + 1) * pad;
        var sheet = new Texture2D(W, H, TextureFormat.RGB24, false);
        var bg = new Color32(16, 22, 30, 255);
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = bg;

        for (int k = 0; k < files.Count; k++)
        {
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes(files[k]));
            int col = k % cols, row = k / cols;
            int ox = pad + col * (tw + pad);
            int oy = H - (pad + row * (th + pad)) - th;   // first row at the top
            for (int y = 0; y < th; y++)
                for (int x = 0; x < tw; x++)
                    px[(oy + y) * W + ox + x] = src.GetPixelBilinear((x + 0.5f) / tw, (y + 0.5f) / th);
            Object.DestroyImmediate(src);
        }
        sheet.SetPixels32(px);
        sheet.Apply(false);
        string path = Path.Combine(root, "Marketing", "ContactSheet.png");
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        Debug.Log($"[Marketing] Contact sheet of {files.Count} screenshots: {path}");
        return path;
    }

    // ------------------------------------------------------------------ scene

    [MenuItem(Menu + "Create Showcase Scene (from IslandTest)", false, 80)]
    static void CreateSceneMenu()
    {
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Marketing", $"{ScenePath} exists. Replace it (its shots are lost)?", "Replace", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        CreateScene();
    }

    // Objects dropped from the copy: network players (they'd become the local player), HUD / menu canvases,
    // and tasks that place their props at a new random spot every run (the shots place their own).
    static readonly string[] Strip =
    {
        "PlayerFish", "PlayerShark", "PauseMenuCanvas", "SequenceTaskCanvas", "SequenceTask", "TaskStation_Reef",
        "TrashTask", "TurtleRescueTask", "FoodSpawner", "Start Point",
    };

    /// <summary>Copy IslandTest to the showcase scene, strip it, and add the director and its camera.</summary>
    public static void CreateScene()
    {
        AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(SourceScene, ScenePath)) { Debug.LogError("[Marketing] Couldn't copy " + SourceScene); return; }
        var scene = EditorSceneManager.OpenScene(ScenePath);

        foreach (var root in scene.GetRootGameObjects())
        {
            if (System.Array.IndexOf(Strip, root.name) >= 0) { Object.DestroyImmediate(root); continue; }
            // The island-wide school roams on the clock; each shot brings its own seeded schools instead.
            if (root.GetComponent<BoidsManager>() != null) root.SetActive(false);
        }

        var rig = new GameObject("Marketing Showcase");
        var director = rig.AddComponent<MarketingShowcase>();
        director.sharkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/MenuShark.prefab");
        director.fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PinkFish.prefab");
        director.schoolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/_BoidsManager.prefab");
        director.tangledTurtlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TaskFolder/TurtleTask/TangledTurtle.prefab");
        director.swimmingTurtlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TaskFolder/TurtleTask/SwimmingTurtle.prefab");

        // The camera renders exactly like the player's: settings copied from the player shark's camera.
        var camGo = new GameObject("Showcase Camera") { tag = "MainCamera" };
        camGo.transform.SetParent(rig.transform, false);
        var cam = camGo.AddComponent<Camera>();
        var hd = camGo.AddComponent<HDAdditionalCameraData>();
        camGo.AddComponent<AudioListener>();
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerShark.prefab");
        var playerCam = player != null ? player.GetComponentInChildren<Camera>(true) : null;
        if (playerCam != null)
        {
            cam.CopyFrom(playerCam);
            if (playerCam.TryGetComponent(out HDAdditionalCameraData playerHd)) playerHd.CopyTo(hd);
            if (playerCam.TryGetComponent(out CreatureVision playerVision))
                EditorUtility.CopySerialized(playerVision, camGo.AddComponent<CreatureVision>());
        }
        cam.depth = 100f;
        cam.targetTexture = null;
        director.showcaseCamera = cam;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Marketing] Created {ScenePath} from {SourceScene}. Add ShowcaseShot children under 'Marketing Showcase'.");
    }
}

[CustomEditor(typeof(MarketingShowcase))]
class MarketingShowcaseInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var d = (MarketingShowcase)target;
        var shots = d.Shots;
        EditorGUILayout.LabelField("Shots", EditorStyles.boldLabel);
        if (Application.isPlaying) EditorGUILayout.HelpBox(d.Status, MessageType.None);
        for (int i = 0; i < shots.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            string label = (shots[i].output == ShowcaseOutput.CapsulePlate ? "plate  " : $"{i + 1:00}  ") + shots[i].slug + (Application.isPlaying && d.Current == i ? "   (on screen)" : "");
            if (GUILayout.Button(label, EditorStyles.label)) Selection.activeGameObject = shots[i].gameObject;
            if (GUILayout.Button("Show", GUILayout.Width(52))) MarketingShowcaseMenu.Request("show", i);
            if (GUILayout.Button("Capture", GUILayout.Width(64))) MarketingShowcaseMenu.Request("capture", i);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Capture All", GUILayout.Height(26))) MarketingShowcaseMenu.Request("all", 0);
        if (GUILayout.Button("Capsule Plates", GUILayout.Height(26))) MarketingShowcaseMenu.Request("plates", 0);
        if (GUILayout.Button("Contact Sheet", GUILayout.Height(26))) MarketingShowcaseMenu.BuildContactSheet();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
        DrawDefaultInspector();
        if (Application.isPlaying) Repaint();
    }
}

[CustomEditor(typeof(ShowcaseShot))]
class ShowcaseShotInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var shot = (ShowcaseShot)target;
        var view = SceneView.lastActiveSceneView;
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Set From Scene View") && view != null)
        {
            Undo.RecordObject(shot.transform, "Set shot from Scene view");
            shot.transform.SetPositionAndRotation(view.camera.transform.position, view.camera.transform.rotation);
        }
        if (GUILayout.Button("Move Scene View To Shot") && view != null) view.AlignViewToObject(shot.transform);
        EditorGUILayout.EndHorizontal();

        var director = shot.GetComponentInParent<MarketingShowcase>(true);
        if (director != null)
        {
            int i = director.Shots.IndexOf(shot);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(Application.isPlaying ? "Show (Play)" : "Play && Show")) MarketingShowcaseMenu.Request("show", i);
            if (GUILayout.Button(Application.isPlaying ? "Capture" : "Play && Capture")) MarketingShowcaseMenu.Request("capture", i);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}
