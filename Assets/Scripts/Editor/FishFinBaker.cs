using System.Collections.Generic;
using FishGame;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Fish > Bake Fin Data: marks each shader-swim species mesh's side fins (pectorals) so FishSwim.hlsl
/// can flap them. The deluxe fish rigs all carry the pectorals on Bone023/Bone025 (tips Bone024/Bone026).
/// Per vertex, the mesh's COLOR holds where its fin joins the body and how much of the flap it takes:
///     rgb = (root y + 0.5, root z + 0.5, |root x|)   in the swim mesh's space (nose +Z, one unit long)
///     a   = 0.9 * flap weight                       0 at the root .. 0.9 at the tip; body vertices 0
/// Only the colours are written, so the meshes, materials and prefabs Build Species made are left alone.
/// Build Species calls it too, so a rebuild keeps the fins.
/// </summary>
public static class FishFinBaker
{
    static readonly string[] FinBones = { "Bone023", "Bone024", "Bone025", "Bone026" };

    // The rigged deluxe model each species was baked from (as in FishSpeciesBuilder's table).
    static readonly Dictionary<string, string> Sources = new Dictionary<string, string>
    {
        { "Clownfish", "Clownfish_prefab" }, { "YellowTang", "Yellowtang_prefab" },
        { "Angelfish", "Emperor_angelfish_prefab" }, { "MoorishIdol", "Moorish_idol_prefab" },
        { "Mackerel", "Mackerel_prefab" }, { "Butterflyfish", "Butterfly_fish_prefab" },
    };

    static GameObject Source(string species)
    {
        if (!Sources.TryGetValue(species, out var name)) return null;
        foreach (var g in AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/Underwater life deluxe" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    [MenuItem("Tools/Fish/Bake Fin Data")]
    public static void BakeMenu() => Debug.Log(BakeAll());

    public static string BakeAll()
    {
        var lib = Resources.Load<FishSpecies>("FishSpecies");
        if (lib == null) return "[Fins] No Resources/FishSpecies.asset.";
        var report = new System.Text.StringBuilder("[Fins] ");
        foreach (var sp in lib.species)
        {
            if (sp == null || sp.swimMesh == null) continue;
            var src = Source(sp.name);
            if (src == null) continue;   // the pink school fish: its own rigged swim, no procedural fins
            report.Append(sp.name).Append(": ").Append(Bake(sp.swimMesh, src)).Append("; ");
        }
        AssetDatabase.SaveAssets();
        return report.ToString();
    }

    /// <summary>Write the fin colours into <paramref name="swim"/> from the rig in <paramref name="source"/>.</summary>
    public static string Bake(Mesh swim, GameObject source)
    {
        var smr = source.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null || smr.sharedMesh == null) return "no rig (left as is)";
        var mesh = smr.sharedMesh;
        if (mesh.vertexCount != swim.vertexCount) return $"vertex count differs ({mesh.vertexCount} vs {swim.vertexCount})";

        var finIdx = new HashSet<int>();
        for (int b = 0; b < smr.bones.Length; b++)
            if (smr.bones[b] != null && System.Array.IndexOf(FinBones, smr.bones[b].name) >= 0) finIdx.Add(b);
        if (finIdx.Count == 0) return "no fin bones";

        var bw = mesh.boneWeights;
        var v = swim.vertices;
        int n = v.Length;
        var w = new float[n];
        for (int i = 0; i < n; i++)
        {
            var b = bw[i];
            float s = 0f;
            if (finIdx.Contains(b.boneIndex0)) s += b.weight0;
            if (finIdx.Contains(b.boneIndex1)) s += b.weight1;
            if (finIdx.Contains(b.boneIndex2)) s += b.weight2;
            if (finIdx.Contains(b.boneIndex3)) s += b.weight3;
            w[i] = s;
        }

        // Each side's fin: where it meets the body (its vertex nearest the midline) and how far out its tip is.
        var colors = new Color[n];
        int marked = 0;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 root = Vector3.zero; float rootX = float.MaxValue, tipX = 0f; int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (w[i] < 0.5f || Mathf.Sign(v[i].x) != side) continue;
                float ax = Mathf.Abs(v[i].x);
                if (ax < rootX) { rootX = ax; root = v[i]; }
                tipX = Mathf.Max(tipX, ax);
                count++;
            }
            if (count == 0) continue;
            float span = Mathf.Max(1e-4f, tipX - rootX);
            for (int i = 0; i < n; i++)
            {
                if (w[i] <= 0f || Mathf.Sign(v[i].x) != side) continue;
                // the base stays on the body; the flap grows toward the tip
                float out01 = Mathf.Clamp01((Mathf.Abs(v[i].x) - rootX) / span);
                float flap = w[i] * Mathf.SmoothStep(0f, 1f, out01);
                colors[i] = new Color(root.y + 0.5f, root.z + 0.5f, rootX, 0.9f * flap);
                marked++;
            }
        }
        swim.colors = colors;
        EditorUtility.SetDirty(swim);
        return $"{marked} fin vertices";
    }
}
