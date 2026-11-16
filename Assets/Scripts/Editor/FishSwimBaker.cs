using System.Collections.Generic;
using System.IO;
using System.Text;
using FishGame;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns the school fish's skinned swim into a shader swim (see Assets/Shaders/FishSwim.hlsl).
///
/// The swim clip only swings one tail joint about a fixed axis, so the bake reads the rig itself:
/// which bones move, the rotation axis and pivot, and the swing over the clip (fitted as a cosine). It
/// writes a mesh with the rest pose in POSITION and the tail pose + tail weight in TEXCOORD2/3, the rig
/// constants to FishSwimRig.hlsl, and checks the result against Unity's own skinning (BakeMesh) so the
/// shader fish match the animated ones.
///
/// "Set Up School Fish Prefab" then gives PinkFish a MeshRenderer child with that mesh and the swim
/// material, and switches its Animator and SkinnedMeshRenderer off (FishSwimRig turns them back on
/// only while the fish is being eaten).
/// </summary>
public static class FishSwimBaker
{
    const string PrefabPath = "Assets/Prefabs/PinkFish.prefab";
    const string ClipPath = "Assets/Models/Materials/swim.anim";
    const string MeshPath = "Assets/Models/FishSwim/FishSwimMesh.asset";
    const string RigHlslPath = "Assets/Shaders/FishSwimRig.hlsl";
    const string MaterialPath = "Assets/Models/Materials/Materials/FishSwim.mat";
    const string ShaderName = "HDRP/Fish Swim (Unlit)";
    const string SwimChild = "SwimMesh";
    const int Samples = 64;

    struct Rig
    {
        public Matrix4x4 staticM, refM;
        public bool[] moving;
        public Vector3 pivot, axis;
        public float meanDeg, cosDeg, sinDeg, rate, fitErrDeg;
    }

    [MenuItem("Tools/Fish/Bake Swim Mesh")]
    public static void BakeMenu() => Debug.Log(Bake());

    [MenuItem("Tools/Fish/Set Up School Fish Prefab")]
    public static void SetupMenu() => Debug.Log(SetupPrefab());

    /// <summary>Bakes the mesh and rig constants; returns a report including the error against BakeMesh.</summary>
    public static string Bake()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var go = Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var src = smr.sharedMesh;
            var rig = ReadRig(go, smr, clip);

            // ---- mesh -------------------------------------------------------------------------
            var v = src.vertices;
            var bw = src.boneWeights;
            int n = v.Length;
            var rest = new Vector3[n];
            var tail = new List<Vector4>(n);
            var meanRot = Quaternion.AngleAxis(rig.meanDeg, rig.axis);
            for (int i = 0; i < n; i++)
            {
                float w = 0f;
                var b = bw[i];
                if (rig.moving[b.boneIndex0]) w += b.weight0;
                if (rig.moving[b.boneIndex1]) w += b.weight1;
                if (rig.moving[b.boneIndex2]) w += b.weight2;
                if (rig.moving[b.boneIndex3]) w += b.weight3;
                rest[i] = rig.staticM.MultiplyPoint3x4(v[i]);
                // The tail pose at the clip's mean angle, so the shader swings it +-amplitude about zero.
                Vector3 q = meanRot * (rig.refM.MultiplyPoint3x4(v[i]) - rig.pivot) + rig.pivot;
                tail.Add(new Vector4(q.x, q.y, q.z, Mathf.Clamp01(w)));
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "FishSwimMesh";
            mesh.indexFormat = src.indexFormat;
            mesh.vertices = rest;
            mesh.normals = src.normals;
            mesh.uv = src.uv;
            // HDRP's mesh attributes are float2 per texcoord: tail pose xy in uv2, (tail pose z, weight) in uv3.
            var uv2 = new List<Vector2>(n);
            var uv3 = new List<Vector2>(n);
            foreach (var t4 in tail) { uv2.Add(new Vector2(t4.x, t4.y)); uv3.Add(new Vector2(t4.z, t4.w)); }
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++) mesh.SetTriangles(src.GetTriangles(s), s);

            // Bounds over the whole swing, so the fish isn't culled with its tail out.
            var bounds = new Bounds(rest[0], Vector3.zero);
            float amp = Mathf.Sqrt(rig.cosDeg * rig.cosDeg + rig.sinDeg * rig.sinDeg);
            for (int k = 0; k <= 8; k++)
            {
                var r = Quaternion.AngleAxis(Mathf.Lerp(-amp, amp, k / 8f), rig.axis);
                for (int i = 0; i < n; i++)
                {
                    Vector3 q = tail[i];
                    bounds.Encapsulate(Vector3.Lerp(rest[i], r * (q - rig.pivot) + rig.pivot, tail[i].w));
                }
            }
            mesh.bounds = bounds;
            mesh.UploadMeshData(false);

            Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));
            if (isNew) AssetDatabase.CreateAsset(mesh, MeshPath);
            else EditorUtility.SetDirty(mesh);

            // ---- rig constants for the shader -------------------------------------------------
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string F(float x) => x.ToString("R", inv);
            string V(Vector3 x) => $"float3({F(x.x)}, {F(x.y)}, {F(x.z)})";
            var hl = new StringBuilder();
            hl.AppendLine("// GENERATED by Tools > Fish > Bake Swim Mesh (FishSwimBaker.cs) from " + ClipPath + ". Do not edit.");
            hl.AppendLine("#ifndef FISH_SWIM_RIG_HLSL");
            hl.AppendLine("#define FISH_SWIM_RIG_HLSL");
            hl.AppendLine($"#define FISH_SWIM_PIVOT {V(rig.pivot)}");
            hl.AppendLine($"#define FISH_SWIM_AXIS {V(rig.axis)}");
            hl.AppendLine($"#define FISH_SWIM_AMPLITUDE {F(amp * Mathf.Deg2Rad)} // {amp:F2} degrees");
            hl.AppendLine($"#define FISH_SWIM_RATE {F(rig.rate)} // tail beats per second");
            hl.AppendLine("#endif");
            File.WriteAllText(RigHlslPath, hl.ToString().Replace("\r\n", "\n"));

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(RigHlslPath);

            // ---- check against Unity's skinning ------------------------------------------------
            float maxErr = Verify(go, smr, clip, rig, rest, tail);
            float length = src.bounds.size.magnitude;
            return $"[FishSwimBaker] baked {n} verts. pivot={rig.pivot:F4} axis={rig.axis:F3} mean={rig.meanDeg:F2} " +
                   $"amp={amp:F2}deg rate={rig.rate:F2}/s cosine fit err={rig.fitErrDeg:F2}deg. " +
                   $"Max vertex error vs skinned = {maxErr:F5} ({100f * maxErr / length:F2}% of the fish).";
        }
        finally { Object.DestroyImmediate(go); }
    }

    static Matrix4x4 SkinMatrix(SkinnedMeshRenderer smr, int bone) =>
        smr.transform.worldToLocalMatrix * smr.bones[bone].localToWorldMatrix * smr.sharedMesh.bindposes[bone];

    static bool Same(Matrix4x4 a, Matrix4x4 b, float eps = 1e-4f)
    {
        for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > eps) return false;
        return true;
    }

    static Rig ReadRig(GameObject go, SkinnedMeshRenderer smr, AnimationClip clip)
    {
        int nb = smr.bones.Length;
        var rig = new Rig { moving = new bool[nb], rate = 1f / Mathf.Max(0.01f, clip.length) };

        // Which bones move over the clip?
        var first = new Matrix4x4[nb];
        clip.SampleAnimation(go, 0f);
        for (int b = 0; b < nb; b++) first[b] = SkinMatrix(smr, b);
        for (int s = 1; s < Samples; s++)
        {
            clip.SampleAnimation(go, clip.length * s / Samples);
            for (int b = 0; b < nb; b++) if (!Same(first[b], SkinMatrix(smr, b))) rig.moving[b] = true;
        }

        int anyStatic = -1, anyMoving = -1;
        for (int b = 0; b < nb; b++) { if (rig.moving[b]) anyMoving = b; else anyStatic = b; }
        if (anyStatic < 0 || anyMoving < 0) throw new System.Exception("Expected some static and some moving bones.");

        // All static bones must agree, and all moving bones must move together (one rigid tail).
        clip.SampleAnimation(go, clip.length * 0.25f);
        rig.staticM = SkinMatrix(smr, anyStatic);
        rig.refM = SkinMatrix(smr, anyMoving);
        for (int b = 0; b < nb; b++)
        {
            var m = SkinMatrix(smr, b);
            if (!Same(m, rig.moving[b] ? rig.refM : rig.staticM, 1e-3f))
                throw new System.Exception($"Bone {smr.bones[b].name} doesn't match the one-moving-bone model; the shader swim can't reproduce this rig.");
        }

        // The tail bone turns about its own origin: that's the pivot.
        // (the topmost moving bone: the others just ride along with it)
        Transform tailBone = null;
        for (int b = 0; b < nb && tailBone == null; b++)
        {
            if (!rig.moving[b]) continue;
            bool top = true;
            for (int o = 0; o < nb; o++)
                if (o != b && rig.moving[o] && smr.bones[b].IsChildOf(smr.bones[o])) top = false;
            if (top) tailBone = smr.bones[b];
        }
        rig.pivot = smr.transform.InverseTransformPoint(tailBone.position);

        // Axis: from the biggest swing relative to the reference.
        var refInv = rig.refM.inverse;
        float bestAngle = 0f;
        var angles = new float[Samples];
        var rels = new Quaternion[Samples];
        for (int s = 0; s < Samples; s++)
        {
            clip.SampleAnimation(go, clip.length * s / Samples);
            rels[s] = (SkinMatrix(smr, anyMoving) * refInv).rotation;
            rels[s].ToAngleAxis(out float a, out Vector3 ax);
            if (a > 180f) a -= 360f;
            if (Mathf.Abs(a) > bestAngle) { bestAngle = Mathf.Abs(a); rig.axis = (a < 0 ? -ax : ax).normalized; }
        }
        // Signed angle about that axis at every sample, then a one-harmonic Fourier fit.
        for (int s = 0; s < Samples; s++)
        {
            rels[s].ToAngleAxis(out float a, out Vector3 ax);
            if (a > 180f) a -= 360f;
            angles[s] = Vector3.Dot(ax, rig.axis) < 0f ? -a : a;
        }
        float mean = 0f, c = 0f, sn = 0f;
        for (int s = 0; s < Samples; s++)
        {
            float ph = 2f * Mathf.PI * s / Samples;
            mean += angles[s];
            c += angles[s] * Mathf.Cos(ph);
            sn += angles[s] * Mathf.Sin(ph);
        }
        rig.meanDeg = mean / Samples;
        rig.cosDeg = 2f * c / Samples;
        rig.sinDeg = 2f * sn / Samples;
        for (int s = 0; s < Samples; s++)
        {
            float ph = 2f * Mathf.PI * s / Samples;
            float fit = rig.meanDeg + rig.cosDeg * Mathf.Cos(ph) + rig.sinDeg * Mathf.Sin(ph);
            rig.fitErrDeg = Mathf.Max(rig.fitErrDeg, Mathf.Abs(fit - angles[s]));
        }
        return rig;
    }

    // Max distance between the shader formula and Unity's skinning over the clip.
    static float Verify(GameObject go, SkinnedMeshRenderer smr, AnimationClip clip, Rig rig, Vector3[] rest, List<Vector4> tail)
    {
        var baked = new Mesh();
        float maxErr = 0f;
        for (int s = 0; s < 20; s++)
        {
            float t = s / 20f;
            clip.SampleAnimation(go, clip.length * t);
            smr.BakeMesh(baked, true);
            var skinned = baked.vertices;
            float ph = 2f * Mathf.PI * t;
            float delta = rig.cosDeg * Mathf.Cos(ph) + rig.sinDeg * Mathf.Sin(ph);   // about the mean
            var r = Quaternion.AngleAxis(delta, rig.axis);
            for (int i = 0; i < rest.Length; i++)
            {
                Vector3 q = tail[i];
                var p = Vector3.Lerp(rest[i], r * (q - rig.pivot) + rig.pivot, tail[i].w);
                maxErr = Mathf.Max(maxErr, (p - skinned[i]).magnitude);
            }
        }
        Object.DestroyImmediate(baked);
        return maxErr;
    }

    /// <summary>Adds the shader-swim renderer to PinkFish and turns the rig off (it's kept for the eaten clip).</summary>
    public static string SetupPrefab()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null) return "[FishSwimBaker] bake the swim mesh first.";
        var shader = Shader.Find(ShaderName);
        if (shader == null) return "[FishSwimBaker] shader " + ShaderName + " not found (not compiled yet?).";

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var anim = root.GetComponentInChildren<Animator>(true);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(smr.sharedMaterial) { name = "FishSwim" };
                mat.shader = shader;
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else mat.shader = shader;
            mat.enableInstancing = true;

            var child = root.transform.Find(SwimChild);
            if (child == null)
            {
                child = new GameObject(SwimChild).transform;
                child.SetParent(root.transform, false);
            }
            // Same place as the skinned mesh: the baked rest pose is in its local space.
            child.localPosition = smr.transform.localPosition;
            child.localRotation = smr.transform.localRotation;
            child.localScale = smr.transform.localScale;
            child.gameObject.layer = smr.gameObject.layer;

            var mf = child.GetComponent<MeshFilter>();
            if (mf == null) mf = child.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = child.GetComponent<MeshRenderer>();
            if (mr == null) mr = child.gameObject.AddComponent<MeshRenderer>();
            var mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = smr.shadowCastingMode;
            mr.receiveShadows = smr.receiveShadows;
            mr.lightProbeUsage = smr.lightProbeUsage;
            mr.reflectionProbeUsage = smr.reflectionProbeUsage;
            mr.renderingLayerMask = smr.renderingLayerMask;
            // Per-object motion vectors: HDRP re-runs the swim with last frame's time for them.
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;

            smr.enabled = false;
            if (anim != null) anim.enabled = false;

            var swimRig = root.GetComponent<FishSwimRig>();
            if (swimRig == null) swimRig = root.AddComponent<FishSwimRig>();
            swimRig.Setup(mr, smr, anim);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return "[FishSwimBaker] PinkFish now swims by shader (rig kept, off, for the eaten clip).";
    }
}
