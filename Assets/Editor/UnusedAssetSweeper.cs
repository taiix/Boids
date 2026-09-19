using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Finds assets nothing references and moves them into a quarantine folder, preserving their
/// original folder structure and writing a manifest so the whole sweep can be undone.
///
/// Moving is done through AssetDatabase so GUIDs survive — a wrongly-swept asset keeps working
/// and can be moved back. The dangerous part is not references, it is the folders Unity treats
/// specially: an Editor script moved out of an Editor folder lands in the runtime assembly and
/// fails to compile, and anything under Resources is loaded by path rather than by reference so
/// it looks unreferenced while being very much in use. Those are excluded outright.
/// </summary>
public class UnusedAssetSweeper : EditorWindow
{
    const string k_Quarantine = "Assets/No Used";
    const string k_Manifest = "Assets/No Used/_sweep-manifest.txt";

    // Assets nothing can reference by a normal dependency edge, or that break when relocated.
    static readonly string[] k_ProtectedFolders =
    {
        "/editor/", "/resources/", "/streamingassets/", "/plugins/", "/gizmos/",
        "/editor default resources/",
    };

    static readonly string[] k_ProtectedExtensions =
    {
        ".cs", ".asmdef", ".asmref", ".dll", ".so", ".a", ".bundle", ".jar", ".aar",
        ".rsp", ".xml", ".json", ".md", ".txt", ".preset", ".unity",
    };

    // Third-party trees where a partial move breaks the package and the "unused" reading is
    // unreliable. Swept only when explicitly opted into.
    static readonly string[] k_ThirdParty =
    {
        "assets/mirror/", "assets/steamworks.net/", "assets/textmesh pro/",
        "assets/ui toolkit/", "assets/scripttemplates/",
    };

    bool m_IncludeThirdParty;
    bool m_IncludeSamples = true;
    Vector2 m_Scroll;
    string m_Report = "";
    List<string> m_Candidates = new List<string>();

    [MenuItem("Tools/Unused Asset Sweeper")]
    static void Open() => GetWindow<UnusedAssetSweeper>("Unused Assets");

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Scans every asset, marks everything reachable from the scenes, Resources, " +
            "Always Included Shaders, preloaded assets and the render pipeline settings, then " +
            "offers to quarantine the rest.\n\n" +
            "Scripts, Editor folders, Resources, StreamingAssets and Plugins are never moved.",
            MessageType.Info);

        m_IncludeSamples = EditorGUILayout.Toggle("Include imported Samples", m_IncludeSamples);
        m_IncludeThirdParty = EditorGUILayout.Toggle("Include third-party packages", m_IncludeThirdParty);
        if (m_IncludeThirdParty)
            EditorGUILayout.HelpBox("Mirror, Steamworks, TextMesh Pro etc. contain assets reached " +
                "from code rather than references. Sweeping them is likely to take live files.",
                MessageType.Warning);

        EditorGUILayout.Space();
        if (GUILayout.Button("Scan (dry run)", GUILayout.Height(26))) Scan();

        using (new EditorGUI.DisabledScope(m_Candidates.Count == 0))
            if (GUILayout.Button($"Move {m_Candidates.Count} asset(s) to '{k_Quarantine}'", GUILayout.Height(26)))
                Sweep();

        if (Directory.Exists(k_Quarantine) && GUILayout.Button("Restore everything from manifest"))
            Restore();

        m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
        EditorGUILayout.TextArea(m_Report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    public List<string> Scan()
    {
        var all = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p))
            .ToList();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in CollectRoots())
            foreach (var dep in AssetDatabase.GetDependencies(root, true))
                used.Add(dep);

        m_Candidates = new List<string>();
        long bytes = 0;
        var perFolder = new Dictionary<string, (int count, long size)>();

        foreach (var p in all)
        {
            if (used.Contains(p)) continue;
            if (!IsSweepable(p)) continue;

            m_Candidates.Add(p);
            long len = 0;
            try { len = new FileInfo(p).Length; } catch { }
            bytes += len;

            var top = TopFolder(p);
            perFolder.TryGetValue(top, out var e);
            perFolder[top] = (e.count + 1, e.size + len);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Assets scanned:  {all.Count}");
        sb.AppendLine($"Reachable:       {all.Count(used.Contains)}");
        sb.AppendLine($"Sweepable:       {m_Candidates.Count}   ({bytes / 1048576f:F1} MB)");
        sb.AppendLine();
        sb.AppendLine("By folder:");
        foreach (var kv in perFolder.OrderByDescending(k => k.Value.size))
            sb.AppendLine($"   {kv.Value.count,6}  {kv.Value.size / 1048576f,8:F1} MB   {kv.Key}");

        m_Report = sb.ToString();
        Debug.Log("[UnusedAssetSweeper]\n" + m_Report);
        return m_Candidates;
    }

    /// <summary>Everything the game can reach without an explicit asset reference.</summary>
    static IEnumerable<string> CollectRoots()
    {
        // Every scene, not only the ones in Build Settings — an unlisted scene is still authored
        // content, and treating it as dead would sweep most of the project.
        foreach (var guid in AssetDatabase.FindAssets("t:Scene"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.StartsWith("Assets/")) yield return p;
        }

        // Scenes on disk only describe what was last saved. Anything referenced by unsaved edits
        // in an open scene would otherwise read as unused and be swept out from under the user,
        // so walk the live objects too.
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            var roots = scene.GetRootGameObjects();
            if (roots.Length == 0) continue;

            foreach (var dep in EditorUtility.CollectDependencies(roots))
            {
                if (dep == null) continue;
                var p = AssetDatabase.GetAssetPath(dep);
                if (!string.IsNullOrEmpty(p) && p.StartsWith("Assets/")) yield return p;
            }
        }

        // Resources are loaded by name at runtime, so nothing "references" them.
        foreach (var p in AssetDatabase.GetAllAssetPaths())
            if (p.StartsWith("Assets/") && p.Replace('\\', '/').ToLowerInvariant().Contains("/resources/"))
                yield return p;

        foreach (var obj in PlayerSettings.GetPreloadedAssets())
        {
            if (obj == null) continue;
            var p = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(p)) yield return p;
        }

        var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
        if (graphics != null)
        {
            var so = new SerializedObject(graphics);
            var shaders = so.FindProperty("m_AlwaysIncludedShaders");
            if (shaders != null)
                for (int i = 0; i < shaders.arraySize; i++)
                {
                    var o = shaders.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (o == null) continue;
                    var p = AssetDatabase.GetAssetPath(o);
                    if (!string.IsNullOrEmpty(p)) yield return p;
                }
        }

        // Render pipeline assets and their whole dependency tree.
        foreach (var guid in AssetDatabase.FindAssets("t:RenderPipelineAsset")) yield return AssetDatabase.GUIDToAssetPath(guid);
        foreach (var guid in AssetDatabase.FindAssets("t:RenderPipelineGlobalSettings")) yield return AssetDatabase.GUIDToAssetPath(guid);
        foreach (var guid in AssetDatabase.FindAssets("t:VolumeProfile")) yield return AssetDatabase.GUIDToAssetPath(guid);
    }

    bool IsSweepable(string path)
    {
        var lower = path.Replace('\\', '/').ToLowerInvariant();

        if (lower.StartsWith(k_Quarantine.ToLowerInvariant())) return false;
        if (k_ProtectedExtensions.Contains(Path.GetExtension(lower))) return false;
        foreach (var f in k_ProtectedFolders) if (lower.Contains(f)) return false;
        if (lower.StartsWith("assets/editor/")) return false;

        if (!m_IncludeThirdParty)
            foreach (var t in k_ThirdParty) if (lower.StartsWith(t)) return false;

        if (!m_IncludeSamples && lower.StartsWith("assets/samples/")) return false;

        return true;
    }

    static string TopFolder(string path)
    {
        var parts = path.Split('/');
        return parts.Length >= 2 ? parts[0] + "/" + parts[1] : path;
    }

    public void Sweep() => Sweep(true);

    public void Sweep(bool prompt)
    {
        if (m_Candidates.Count == 0) return;

        if (prompt && !EditorUtility.DisplayDialog("Quarantine unused assets",
            $"Move {m_Candidates.Count} asset(s) into '{k_Quarantine}'?\n\n" +
            "References are preserved (GUIDs survive a move), and a manifest is written so this " +
            "can be undone.", "Move", "Cancel")) return;

        var manifest = new StringBuilder();
        int moved = 0, failed = 0;

        // Deliberately NOT wrapped in Start/StopAssetEditing. Inside a batch, AssetDatabase does
        // not see folders created moments earlier, so IsValidFolder keeps returning false and
        // CreateFolder makes "Foo 1", "Foo 2", ... while every MoveAsset into them fails.
        try
        {
            for (int i = 0; i < m_Candidates.Count; i++)
            {
                var src = m_Candidates[i];
                if (i % 25 == 0)
                    EditorUtility.DisplayProgressBar("Quarantining", src, (float)i / m_Candidates.Count);

                var dst = k_Quarantine + "/" + src.Substring("Assets/".Length);
                EnsureFolder(Path.GetDirectoryName(dst).Replace('\\', '/'));

                var err = AssetDatabase.MoveAsset(src, dst);
                if (string.IsNullOrEmpty(err)) { manifest.AppendLine(dst + "|" + src); moved++; }
                else { Debug.LogWarning($"[UnusedAssetSweeper] {src}: {err}"); failed++; }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        EnsureFolder(k_Quarantine);
        File.WriteAllText(k_Manifest, manifest.ToString());
        AssetDatabase.Refresh();

        m_Report = $"Moved {moved}, failed {failed}. Manifest: {k_Manifest}";
        Debug.Log("[UnusedAssetSweeper] " + m_Report);
        m_Candidates.Clear();
    }

    void Restore()
    {
        if (!File.Exists(k_Manifest)) { Debug.LogWarning("[UnusedAssetSweeper] No manifest."); return; }

        int restored = 0, failed = 0;
        // Same reason as Sweep: no asset-editing batch around folder creation.
        foreach (var line in File.ReadAllLines(k_Manifest))
        {
            var parts = line.Split('|');
            if (parts.Length != 2) continue;
            EnsureFolder(Path.GetDirectoryName(parts[1]).Replace('\\', '/'));
            var err = AssetDatabase.MoveAsset(parts[0], parts[1]);
            if (string.IsNullOrEmpty(err)) restored++; else { Debug.LogWarning(err); failed++; }
        }

        AssetDatabase.Refresh();
        m_Report = $"Restored {restored}, failed {failed}.";
        Debug.Log("[UnusedAssetSweeper] " + m_Report);
    }

    static void EnsureFolder(string folder)
    {
        folder = folder.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
