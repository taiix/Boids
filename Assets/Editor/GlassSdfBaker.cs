using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes a narrow-band signed distance field of the scenery around a glass pane, so the glass
/// shader can smooth-union itself into whatever it touches.
///
/// Narrow band because the fillet only exists within a metre or two of the surface: the volume
/// is the pane's slab inflated by the fillet radius, then clipped to the geometry that is
/// actually nearby. For a 305 x 137 m pane sitting on a seabed that is only near the pane over
/// a ~20 m band, that is the difference between a few MB and a few hundred.
/// </summary>
public static class GlassSdfBaker
{
    // A long pane needs real resolution along its length, and once the volume is correctly
    // clipped in height it can afford it. The total-voxel cap is the actual budget.
    const int k_MaxAxis = 1024;
    const long k_MaxVoxels = 12_000_000;

    public struct Report
    {
        public bool Success;
        public string Message;
        public int3 Resolution;
        public int TriangleCount;
        public float MegaBytes;
        public List<string> SkippedUnreadable;
    }

    [MenuItem("Tools/Frosted Glass/Bake Contact SDF")]
    static void BakeSelected()
    {
        var volumes = new List<GlassSdfVolume>();
        foreach (var go in Selection.gameObjects)
            volumes.AddRange(go.GetComponentsInChildren<GlassSdfVolume>());

        if (volumes.Count == 0)
        {
            EditorUtility.DisplayDialog("Bake Contact SDF",
                "Select one or more objects carrying a GlassSdfVolume component.", "OK");
            return;
        }

        var skipped = new HashSet<string>();
        int ok = 0;
        try
        {
            for (int i = 0; i < volumes.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Baking contact SDF",
                    volumes[i].name + "  (" + (i + 1) + " / " + volumes.Count + ")",
                    (float)i / volumes.Count);

                var report = Bake(volumes[i], 0.25f);
                if (report.Success) ok++;
                else Debug.LogWarning("[GlassSdfBaker] " + volumes[i].name + ": " + report.Message, volumes[i]);
                if (report.SkippedUnreadable != null)
                    foreach (var s in report.SkippedUnreadable) skipped.Add(s);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        if (skipped.Count > 0)
        {
            Debug.LogWarning("[GlassSdfBaker] " + skipped.Count +
                " mesh(es) were skipped because Read/Write is disabled on their import settings, so the " +
                "glass will not blend with them: " + string.Join(", ", skipped));
        }
        Debug.Log("[GlassSdfBaker] Baked " + ok + " of " + volumes.Count + " volume(s).");
    }

    public static Report Bake(GlassSdfVolume volume, float voxelSize)
    {
        var report = new Report { SkippedUnreadable = new List<string>() };

        var tf = volume.transform;
        var scale = tf.lossyScale;
        var paneHalf = new float3(
            Mathf.Abs(scale.x) * 0.5f, Mathf.Abs(scale.y) * 0.5f, Mathf.Abs(scale.z) * 0.5f);

        float band = volume.filletRadius + voxelSize * 2f;

        // Everything below works in the pane's frame: origin at the pane, axes along the pane.
        // Orthonormal, so distances computed here are already world-space metres.
        var frame = Matrix4x4.TRS(tf.position, tf.rotation, Vector3.one);
        var worldToFrame = frame.inverse;

        // The widest region the fillet could possibly occupy.
        float3 searchHalf = paneHalf + band;
        var searchBoundsWS = OrientedBoundsToWorldAabb(tf.position, tf.rotation, searchHalf);

        // Collect triangles in frame space, keeping only those inside the search region.
        var verts = new List<float3>();
        var normals = new List<float3>();
        float3 geoMin = float.MaxValue, geoMax = float.MinValue;

        // Other panes are not scenery. Matching on the shader catches siblings that have no
        // GlassSdfVolume on them yet, which is otherwise an easy way to bake a neighbouring
        // pane into the field and blow the volume up to its full height.
        var ownRenderer = volume.GetComponent<Renderer>();
        var glassShader = ownRenderer != null && ownRenderer.sharedMaterial != null
            ? ownRenderer.sharedMaterial.shader : null;

        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
        {
            if (mf.GetComponent<GlassSdfVolume>() != null) continue;   // never blend glass with glass
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;

            var rend = mf.GetComponent<Renderer>();
            if (rend == null || !rend.enabled) continue;
            if (glassShader != null && UsesShader(rend, glassShader)) continue;
            if (!rend.bounds.Intersects(searchBoundsWS)) continue;

            if (!mesh.isReadable) { report.SkippedUnreadable.Add(mesh.name); continue; }

            var toFrame = worldToFrame * mf.transform.localToWorldMatrix;
            var mv = mesh.vertices;
            var mt = mesh.triangles;

            var local = new float3[mv.Length];
            for (int i = 0; i < mv.Length; i++)
            {
                var p = toFrame.MultiplyPoint3x4(mv[i]);
                local[i] = new float3(p.x, p.y, p.z);
            }

            for (int i = 0; i < mt.Length; i += 3)
            {
                float3 a = local[mt[i]], b = local[mt[i + 1]], c = local[mt[i + 2]];

                float3 lo = math.min(a, math.min(b, c)) - band;
                float3 hi = math.max(a, math.max(b, c)) + band;
                if (math.any(hi < -searchHalf) || math.any(lo > searchHalf)) continue;

                float3 n = math.cross(b - a, c - a);
                float len = math.length(n);
                if (len < 1e-12f) continue;          // degenerate

                verts.Add(a); verts.Add(b); verts.Add(c);
                normals.Add(n / len);
                geoMin = math.min(geoMin, math.max(lo, -searchHalf));
                geoMax = math.max(geoMax, math.min(hi, searchHalf));
            }
        }

        int triCount = normals.Count;
        report.TriangleCount = triCount;
        if (triCount == 0)
        {
            report.Message = "No readable geometry within " + band.ToString("F2") + "m of this pane.";
            return report;
        }

        // Clip the volume to where geometry actually is. This is what keeps a 137m-tall pane
        // from baking 137m of empty water.
        float3 volMin = math.max(geoMin, -searchHalf);
        float3 volMax = math.min(geoMax, searchHalf);
        float3 volSize = volMax - volMin;
        if (math.any(volSize <= 0f))
        {
            report.Message = "Geometry bounds collapsed; nothing to bake.";
            return report;
        }

        int3 res = (int3)math.ceil(volSize / voxelSize);
        res = math.clamp(res, 4, k_MaxAxis);
        while ((long)res.x * res.y * res.z > k_MaxVoxels)
            res = math.max(res / 2, 4);

        float3 center = (volMin + volMax) * 0.5f;
        float3 extents = volSize * 0.5f;

        int total = res.x * res.y * res.z;
        var dist = new NativeArray<float>(total, Allocator.TempJob);
        var conf = new NativeArray<float>(total, Allocator.TempJob);
        var jVerts = new NativeArray<float3>(verts.ToArray(), Allocator.TempJob);
        var jNormals = new NativeArray<float3>(normals.ToArray(), Allocator.TempJob);

        try
        {
            for (int i = 0; i < total; i++) { dist[i] = band; conf[i] = -1f; }

            var job = new SdfJob
            {
                Verts = jVerts,
                Normals = jNormals,
                Dist = dist,
                Conf = conf,
                Res = res,
                VolMin = volMin,
                VoxelSize = volSize / (float3)res,
                Band = band,
            };
            // One job per Z slice: each slice owns its own slab of the output, so triangles can
            // scatter into it without any atomics.
            job.Schedule(res.z, 1).Complete();

            var pixels = new half[total];
            for (int i = 0; i < total; i++)
                pixels[i] = (half)math.clamp(dist[i], -band, band);

            var tex = new Texture3D(res.x, res.y, res.z, TextureFormat.RHalf, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = volume.name + "_ContactSDF",
            };
            tex.SetPixelData(pixels, 0);
            tex.Apply(false, true);

            var path = AssetPathFor(volume);
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);

            Undo.RecordObject(volume, "Bake Contact SDF");
            volume.sdf = AssetDatabase.LoadAssetAtPath<Texture3D>(path);
            volume.bakeCenterWS = frame.MultiplyPoint3x4(new Vector3(center.x, center.y, center.z));
            volume.bakeExtentsWS = new Vector3(extents.x, extents.y, extents.z);
            volume.bakeRotationWS = tf.rotation;
            volume.bakedAtLocalToWorld = tf.localToWorldMatrix;
            volume.Apply();
            EditorUtility.SetDirty(volume);

            report.Success = true;
            report.Resolution = res;
            report.MegaBytes = total * 2f / (1024f * 1024f);
            report.Message = "Baked " + res.x + "x" + res.y + "x" + res.z +
                             " from " + triCount + " triangles (" + report.MegaBytes.ToString("F1") + " MB).";
            return report;
        }
        finally
        {
            dist.Dispose(); conf.Dispose(); jVerts.Dispose(); jNormals.Dispose();
        }
    }

    static bool UsesShader(Renderer rend, Shader shader)
    {
        var mats = rend.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].shader == shader) return true;
        return false;
    }

    static string AssetPathFor(GlassSdfVolume volume)
    {
        const string dir = "Assets/Settings/GlassSdf";
        if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Settings", "GlassSdf");

        // Hierarchy path keeps sibling panes with identical names from overwriting each other.
        var t = volume.transform;
        var name = t.name;
        while (t.parent != null) { t = t.parent; name = t.name + "_" + name; }
        var safe = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
        // Stable across sessions, unlike an instance id, so re-baking overwrites the same asset
        // instead of leaving an orphan behind every time the editor restarts.
        var id = GlobalObjectId.GetGlobalObjectIdSlow(volume).targetObjectId.ToString("X8");
        return dir + "/" + safe + "_" + id + "_SDF.asset";
    }

    static Bounds OrientedBoundsToWorldAabb(Vector3 center, Quaternion rot, float3 half)
    {
        var b = new Bounds(center, Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? -half.x : half.x,
                (i & 2) == 0 ? -half.y : half.y,
                (i & 4) == 0 ? -half.z : half.z);
            b.Encapsulate(center + rot * corner);
        }
        return b;
    }

    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
    struct SdfJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Verts;
        [ReadOnly] public NativeArray<float3> Normals;

        // Each execution index owns one Z slice, so these writes never overlap between threads.
        [NativeDisableParallelForRestriction] public NativeArray<float> Dist;
        [NativeDisableParallelForRestriction] public NativeArray<float> Conf;

        public int3 Res;
        public float3 VolMin;
        public float3 VoxelSize;
        public float Band;

        public void Execute(int z)
        {
            float zMin = VolMin.z + z * VoxelSize.z;
            int triCount = Normals.Length;

            for (int t = 0; t < triCount; t++)
            {
                float3 a = Verts[t * 3], b = Verts[t * 3 + 1], c = Verts[t * 3 + 2];
                float3 lo = math.min(a, math.min(b, c)) - Band;
                float3 hi = math.max(a, math.max(b, c)) + Band;

                if (zMin < lo.z || zMin > hi.z) continue;

                int x0 = (int)math.floor((lo.x - VolMin.x) / VoxelSize.x);
                int x1 = (int)math.ceil((hi.x - VolMin.x) / VoxelSize.x);
                int y0 = (int)math.floor((lo.y - VolMin.y) / VoxelSize.y);
                int y1 = (int)math.ceil((hi.y - VolMin.y) / VoxelSize.y);
                x0 = math.max(x0, 0); y0 = math.max(y0, 0);
                x1 = math.min(x1, Res.x - 1); y1 = math.min(y1, Res.y - 1);
                if (x1 < x0 || y1 < y0) continue;

                float3 n = Normals[t];

                for (int y = y0; y <= y1; y++)
                {
                    float py = VolMin.y + y * VoxelSize.y;
                    for (int x = x0; x <= x1; x++)
                    {
                        float3 p = new float3(VolMin.x + x * VoxelSize.x, py, zMin);
                        float3 closest = ClosestPointOnTriangle(p, a, b, c);
                        float3 delta = p - closest;
                        float d = math.length(delta);
                        if (d > Band) continue;

                        int idx = (z * Res.y + y) * Res.x + x;

                        // Sign from the face normal. Where two faces meet at an edge the nearest
                        // face is ambiguous, so prefer whichever sample hits its face most
                        // squarely; that is the one whose sign is trustworthy.
                        float ndot = d > 1e-6f ? math.abs(math.dot(delta / d, n)) : 1f;
                        float prev = math.abs(Dist[idx]);

                        bool better = d < prev - 1e-4f || (d < prev + 1e-4f && ndot > Conf[idx]);
                        if (!better) continue;

                        Dist[idx] = math.dot(delta, n) >= 0f ? d : -d;
                        Conf[idx] = ndot;
                    }
                }
            }
        }

        static float3 ClosestPointOnTriangle(float3 p, float3 a, float3 b, float3 c)
        {
            // Ericson, Real-Time Collision Detection, 5.1.5.
            float3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = math.dot(ab, ap), d2 = math.dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            float3 bp = p - b;
            float d3 = math.dot(ab, bp), d4 = math.dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));

            float3 cp = p - c;
            float d5 = math.dot(ab, cp), d6 = math.dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }
}

[CustomEditor(typeof(GlassSdfVolume))]
[CanEditMultipleObjects]
public class GlassSdfVolumeEditor : Editor
{
    float m_VoxelSize = 0.25f;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        var volume = (GlassSdfVolume)target;
        if (volume.sdf == null)
            EditorGUILayout.HelpBox("No field baked yet — the glass will render flat with no contact blend.",
                MessageType.Info);
        else if (volume.IsStale)
            EditorGUILayout.HelpBox("The pane has moved since this field was baked. The blend will be " +
                "misaligned until you bake again.", MessageType.Warning);

        m_VoxelSize = EditorGUILayout.Slider("Voxel Size (m)", m_VoxelSize, 0.05f, 1f);
        EditorGUILayout.HelpBox("Smaller voxels give a crisper fillet but cost memory cubed. " +
            "0.25 m suits a 1.5 m fillet.", MessageType.None);

        if (GUILayout.Button("Bake Contact SDF", GUILayout.Height(26)))
        {
            foreach (var t in targets)
            {
                var v = (GlassSdfVolume)t;
                var r = GlassSdfBaker.Bake(v, m_VoxelSize);
                Debug.Log("[GlassSdfBaker] " + v.name + ": " + r.Message, v);
                if (r.SkippedUnreadable != null && r.SkippedUnreadable.Count > 0)
                    Debug.LogWarning("[GlassSdfBaker] " + v.name + " skipped non-readable meshes: " +
                        string.Join(", ", r.SkippedUnreadable), v);
            }
        }
    }
}
