using System.Collections.Generic;
using System.IO;
using FishGame;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the reef's fish species (Resources/FishSpecies.asset) from the Underwater Life Deluxe models.
///
/// Those rigs move a whole spine of bones, so they can't use the school fish's one-bone baked swim
/// (FishSwimBaker). Instead each model is posed at rest, turned nose along +Z / back along +Y, scaled to one
/// unit long, and given the procedural body wave of FishSwim.hlsl in TEXCOORD2/3. That mesh, with an unlit
/// swim material made from the model's own texture, draws both the species' school fish and a player of
/// that species, so a player looks exactly like the school they hide in.
///
/// Each school prefab is laid out like PinkFish: Boid + Edible + FishSwimRig on the root, the shader-swim
/// mesh, and the original rig (Animator + skinned mesh, switched off) that FishSwimRig brings back while
/// the fish thrashes in a shark's mouth.
/// </summary>
public static class FishSpeciesBuilder
{
    const string SourceDir = "Assets/Underwater life deluxe/Prefabs/Animals/";
    const string OutDir = "Assets/Models/FishSpecies";
    const string PrefabDir = "Assets/Prefabs/Species";
    const string LibraryPath = "Assets/Resources/FishSpecies.asset";
    const string TemplateMaterial = "Assets/Models/Materials/Materials/FishSwim.mat";
    const string PinkFishPrefab = "Assets/Prefabs/PinkFish.prefab";

    struct Def
    {
        public string name, source;
        public bool playable;
        public float schoolLength;   // metres, nose to tail, as a school fish (PinkFish is ~0.75 m)
        public float playerSize;     // relative to the original player fish
        public float rate, swing;     // tail beats per second; the tail's swing either way, in degrees
        public float hinge;           // where the rigid body ends and the tail begins (0 nose .. 1 tail tip)
        public float speed, spacing;  // school tuning relative to the scene's

        public Def(string name, string source, bool playable, float schoolLength, float playerSize,
                   float rate, float swing, float hinge, float speed, float spacing)
        {
            this.name = name; this.source = source; this.playable = playable;
            this.schoolLength = schoolLength; this.playerSize = playerSize;
            this.rate = rate; this.swing = swing; this.hinge = hinge; this.speed = speed; this.spacing = spacing;
        }
    }

    // Index order is the library's: the school cycle below refers to it.
    static readonly Def[] Defs =
    {
        // Deep-bodied reef fish (tangs, idols, butterflyfish) keep most of the body rigid and flick the tail;
        // a mackerel bends from further forward.
        //       name             source                    play   length player  rate swing hinge speed spacing
        new Def("Clownfish",     "Clownfish_prefab",       true,  0.62f, 0.95f, 2.4f, 26f, 0.50f, 0.90f, 0.85f),
        new Def("YellowTang",    "Yellowtang_prefab",      false, 0.75f, 1.05f, 1.9f, 22f, 0.56f, 1.00f, 1.00f),
        new Def("Angelfish",     "Emperor_angelfish_prefab", true, 0.80f, 1.05f, 1.8f, 22f, 0.58f, 0.95f, 1.05f),
        new Def("MoorishIdol",   "Moorish_idol_prefab",    false, 0.85f, 1.00f, 1.5f, 20f, 0.60f, 0.85f, 1.10f),
        new Def("Mackerel",      "Mackerel_prefab",        false, 1.05f, 1.00f, 2.8f, 20f, 0.42f, 1.30f, 1.20f),
        new Def("Butterflyfish", "Butterfly_fish_prefab",  true,  0.60f, 1.00f, 2.0f, 22f, 0.58f, 0.80f, 0.90f),
    };
    const int PinkIndex = 6; // appended after the deluxe species: the original school fish

    // Schools take species in this order, biggest school first. The three playable fish come first and
    // recur, so every player has schools of their own kind to hide in; the others add variety.
    // The Yellow Tang (1) is out since 2026-11-17: its texture is plain yellow and read as a broken shader in game.
    // The Butterflyfish (5) took its place, as a playable fish and in its schools.
    static readonly int[] SchoolCycle = { 5, 2, 0, 3, 4, 5, 5, 2, 0, PinkIndex, 5, 2, 0 };

    [MenuItem("Tools/Fish/Build Species")]
    public static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var report = new System.Text.StringBuilder("[FishSpeciesBuilder] ");
        Directory.CreateDirectory(OutDir);
        Directory.CreateDirectory(PrefabDir);
        Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));

        var template = AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterial);
        if (template == null) return report.Append("missing ").Append(TemplateMaterial).ToString();
        var pink = AssetDatabase.LoadAssetAtPath<GameObject>(PinkFishPrefab);
        int layer = pink != null ? pink.layer : 0;

        var lib = AssetDatabase.LoadAssetAtPath<FishSpecies>(LibraryPath);
        bool newLib = lib == null;
        if (newLib) lib = ScriptableObject.CreateInstance<FishSpecies>();
        lib.species = new List<FishSpecies.Species>();

        foreach (var d in Defs)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SourceDir + d.source + ".prefab");
            if (src == null) { report.Append(d.name).Append(": source missing; "); lib.species.Add(new FishSpecies.Species { name = d.name }); continue; }
            var sp = BuildSpecies(d, src, template, layer, out string note);
            lib.species.Add(sp);
            report.Append(d.name).Append(": ").Append(note).Append("; ");
        }

        lib.species.Add(new FishSpecies.Species
        {
            name = "PinkFish", schoolPrefab = pink, playable = false, playerSize = 1f, speedScale = 1f, spacingScale = 1f,
        });
        lib.schoolCycle = new List<int>(SchoolCycle);

        if (newLib) AssetDatabase.CreateAsset(lib, LibraryPath);
        else EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        return report.ToString();
    }

    static FishSpecies.Species BuildSpecies(Def d, GameObject src, Material template, int layer, out string note)
    {
        var go = Object.Instantiate(src);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        try
        {
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var anim = go.GetComponentInChildren<Animator>(true);

            // ---- rest pose, in the prefab's (= world) space ----------------------------------------
            var rest = Bake(smr, out Vector3[] restN, out Mesh baked);
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var p in rest) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            Vector3 size = max - min, mid = (min + max) * 0.5f;

            // Length along the longest axis; the models stand upright (+Y up).
            int lengthAxis = size.x >= size.y && size.x >= size.z ? 0 : size.z >= size.y ? 2 : 1;
            Vector3 axis = lengthAxis == 0 ? Vector3.right : lengthAxis == 2 ? Vector3.forward : Vector3.up;

            // Which end is the head: over the swim clip the tail end moves far more than the nose.
            float headSign = HeadSign(go, smr, anim, rest, mid, axis, size[lengthAxis]);
            Vector3 head = axis * headSign;
            var toCanon = Quaternion.Inverse(Quaternion.LookRotation(head, Vector3.up));
            float length = size[lengthAxis];

            // ---- shader-swim mesh: nose +Z, one unit long, centred -----------------------------------
            int n = rest.Length;
            var verts = new Vector3[n];
            var norms = new Vector3[n];
            var uv2 = new List<Vector2>(n);
            var uv3 = new List<Vector2>(n);
            float maxX = 0f;
            for (int i = 0; i < n; i++)
            {
                var v = toCanon * (rest[i] - mid) / length;
                verts[i] = v;
                norms[i] = (toCanon * restN[i]).normalized;
                float s = Mathf.Clamp01(0.5f - v.z);
                // Rigid in front of the hinge; behind it the tail swings round it (FishSwim.hlsl).
                uv2.Add(new Vector2(s, d.hinge));
                uv3.Add(new Vector2(d.rate, 2f + d.swing * Mathf.Deg2Rad));
                maxX = Mathf.Max(maxX, Mathf.Abs(v.x));
            }

            string meshPath = $"{OutDir}/{d.name}_Swim.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            mesh.Clear();
            mesh.name = d.name + "_Swim";
            mesh.indexFormat = baked.indexFormat;
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = baked.uv;
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.subMeshCount = baked.subMeshCount;
            for (int s = 0; s < baked.subMeshCount; s++) mesh.SetTriangles(baked.GetTriangles(s), s);
            mesh.RecalculateBounds();
            var b = mesh.bounds;
            // The tail swings out of the rest pose (up to twice the baked swing when a player sprints).
            b.Expand(new Vector3(2f * (1f - d.hinge) * Mathf.Sin(Mathf.Min(2f * d.swing, 70f) * Mathf.Deg2Rad) + 0.02f, 0.02f, 0.02f));
            mesh.bounds = b;
            FishFinBaker.Bake(mesh, src);   // the side fins' flap data (vertex colours)
            mesh.UploadMeshData(false);
            if (isNew) AssetDatabase.CreateAsset(mesh, meshPath);
            else EditorUtility.SetDirty(mesh);
            Object.DestroyImmediate(baked);

            // ---- material: the swim shader with the model's own texture ------------------------------
            string matPath = $"{OutDir}/{d.name}_Swim.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(template) { name = d.name + "_Swim" };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else mat.CopyPropertiesFromMaterial(template);
            var srcMat = smr.sharedMaterial;
            Texture tex = srcMat != null && srcMat.HasProperty("_BaseColorMap") ? srcMat.GetTexture("_BaseColorMap") : null;
            if (tex == null && srcMat != null && srcMat.HasProperty("_MainTex")) tex = srcMat.GetTexture("_MainTex");
            mat.SetTexture("_UnlitColorMap", tex);
            mat.SetColor("_UnlitColor", Color.white);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);

            // ---- school prefab -------------------------------------------------------------------------
            var schoolPath = $"{PrefabDir}/{d.name}School.prefab";
            var prefab = BuildSchoolPrefab(d, src, mesh, mat, layer, toCanon, mid, length, schoolPath);

            note = $"{n} verts, length axis {"XYZ"[lengthAxis]}, head {(headSign > 0 ? "+" : "-")}";
            return new FishSpecies.Species
            {
                name = d.name, schoolPrefab = prefab, swimMesh = mesh, swimMaterial = mat, playable = d.playable,
                playerSize = d.playerSize, speedScale = d.speed, spacingScale = d.spacing,
            };
        }
        finally { Object.DestroyImmediate(go); }
    }

    // The skinned mesh as it's drawn right now, in world space (the instance sits at the origin).
    static Vector3[] Bake(SkinnedMeshRenderer smr, out Vector3[] normals, out Mesh baked)
    {
        baked = new Mesh();
        // useScale: true leaves the renderer's own scale out of the vertices (localToWorldMatrix adds it);
        // false bakes it in, and the matrix would then apply it twice.
        smr.BakeMesh(baked, true);
        var m = smr.transform.localToWorldMatrix;
        var v = baked.vertices;
        var nr = baked.normals;
        var outV = new Vector3[v.Length];
        normals = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            outV[i] = m.MultiplyPoint3x4(v[i]);
            normals[i] = i < nr.Length ? m.MultiplyVector(nr[i]).normalized : Vector3.up;
        }
        return outV;
    }

    // +1 if the nose is toward +axis, -1 if toward -axis: compares how far each end's vertices travel over
    // the swim clip (the tail sweeps, the nose hardly moves).
    static float HeadSign(GameObject go, SkinnedMeshRenderer smr, Animator anim, Vector3[] rest, Vector3 mid, Vector3 axis, float length)
    {
        AnimationClip clip = null;
        if (anim != null && anim.runtimeAnimatorController != null)
            foreach (var c in anim.runtimeAnimatorController.animationClips)
                if (c != null && (clip == null || c.name.ToLowerInvariant().EndsWith(" swim"))) clip = c;
        if (clip == null) return 1f;

        int n = rest.Length;
        var travel = new float[n];
        for (int k = 0; k < 8; k++)
        {
            clip.SampleAnimation(go, clip.length * k / 8f);
            var pose = Bake(smr, out _, out Mesh tmp);
            Object.DestroyImmediate(tmp);
            for (int i = 0; i < n && i < pose.Length; i++) travel[i] = Mathf.Max(travel[i], (pose[i] - rest[i]).magnitude);
        }
        // Put the rig back at rest so the prefab copy isn't left mid-stroke.
        clip.SampleAnimation(go, 0f);

        float plus = 0f, minus = 0f;
        int np = 0, nm = 0;
        for (int i = 0; i < n; i++)
        {
            float a = Vector3.Dot(rest[i] - mid, axis) / length;
            if (a > 0.3f) { plus += travel[i]; np++; }
            else if (a < -0.3f) { minus += travel[i]; nm++; }
        }
        plus /= Mathf.Max(1, np);
        minus /= Mathf.Max(1, nm);
        return plus < minus ? 1f : -1f; // the end that moves less is the head
    }

    static GameObject BuildSchoolPrefab(Def d, GameObject src, Mesh mesh, Material mat, int layer,
                                        Quaternion toCanon, Vector3 mid, float length, string path)
    {
        var root = new GameObject(d.name + "School") { layer = layer };
        try
        {
            // Same component order as PinkFish: Boid (driven by BoidsManager), Edible, FishSwimRig.
            root.AddComponent<Boid>().enabled = false;
            var edible = root.AddComponent<Edible>();

            var swim = new GameObject("SwimMesh") { layer = layer };
            swim.transform.SetParent(root.transform, false);
            swim.transform.localScale = Vector3.one * d.schoolLength;
            swim.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = swim.AddComponent<MeshRenderer>();
            var mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;

            // The authored rig, lined up with the swim mesh, for the in-mouth thrash.
            var rig = (GameObject)Object.Instantiate(src);
            rig.name = "Rig";
            rig.transform.SetParent(root.transform, false);
            float k = d.schoolLength / length;
            rig.transform.localRotation = toCanon;
            rig.transform.localScale = Vector3.one * k;
            rig.transform.localPosition = toCanon * (-mid * k);
            foreach (var t in rig.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            var smr = rig.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var anim = rig.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;
                anim.enabled = false;
            }
            if (smr != null)
            {
                smr.enabled = false;
                mr.shadowCastingMode = smr.shadowCastingMode;
                mr.receiveShadows = smr.receiveShadows;
            }

            var swimRig = root.AddComponent<FishSwimRig>();
            swimRig.Setup(mr, smr, anim);

            var so = new SerializedObject(edible);
            so.FindProperty("animator").objectReferenceValue = anim;
            so.FindProperty("disableCollidersOnEaten").boolValue = false;
            so.FindProperty("despawnDelay").floatValue = 2f;
            so.ApplyModifiedPropertiesWithoutUndo();

            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
