using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame.EditorTools
{
    /// <summary>
    /// Builds the seabed crabs' assets (FishGame > Build Crab Assets):
    ///   * Models/Crab/Crab.fbx - exported from the crab .blend by Blender/Crab/export_crab.py (thinned mesh,
    ///     Walk / Idle / Threat / Turn / Groom / Wave / Startle / Dig / Hidden / Emerge takes): import settings,
    ///     the ongoing ones looping.
    ///   * Its textures, copied in from the model's folder next to the project (animated-crab-rigged-free),
    ///     and HDRP's mask map made from the source's metallic and roughness.
    ///   * Crab.mat (HDRP/Lit), Crab.controller (a state per take; the walk's and the turn's speed and direction
    ///     from the WalkSpeed / TurnSpeed parameters, so they play backward to go the other way).
    ///   * Prefabs/Wildlife/SeabedCrab.prefab - the crab facing +z, feet at the root, with its CrabWalker.
    /// Safe to run again: it updates what's there.
    /// </summary>
    public static class CrabAssets
    {
        const string Root = "Assets/Models/Crab";
        const string Textures = Root + "/Textures";
        const string Model = Root + "/Crab.fbx";
        const string ControllerPath = Root + "/Crab.controller";
        const string MaterialPath = Root + "/Crab.mat";
        const string PrefabPath = "Assets/Prefabs/Wildlife/SeabedCrab.prefab";
        static readonly string[] Source = { "..", "..", "animated-crab-rigged-free", "source", "skc" };
        const float StrideSpeed = 0.443f;   // export_crab.py: the walk's feet push back at this, at full size
        const bool WalkGoesRight = true;    // export_crab.py: played forward, it walks toward its right
        static readonly string[] Takes = { "Idle", "Walk", "Threat", "Turn", "Groom", "Wave", "Startle", "Dig", "Hidden", "Emerge" };
        static readonly string[] Looping = { "Idle", "Walk", "Threat", "Turn", "Hidden" };

        [MenuItem("FishGame/Build Crab Assets")]
        public static void Build()
        {
            Folder(Root, "Textures");
            Folder("Assets/Prefabs", "Wildlife");

            var color = CopyTexture("Crab_BaseColor.jpg", "Crab_BaseColor.jpg", TextureImporterType.Default, srgb: true);
            var normal = CopyTexture("Crab_Normal.jpg", "Crab_Normal.jpg", TextureImporterType.NormalMap, srgb: false);
            var mask = MaskMap(Textures + "/Crab_Mask.png");
            SetupModel();

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("HDRP/Lit")) { name = "Crab" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseColorMap", color);
            mat.SetTexture("_NormalMap", normal);
            mat.SetFloat("_NormalScale", 1f);
            mat.SetTexture("_MaskMap", mask);
            mat.SetFloat("_MetallicRemapMin", 0f);
            mat.SetFloat("_MetallicRemapMax", 1f);
            mat.SetFloat("_SmoothnessRemapMin", 0.05f);
            mat.SetFloat("_SmoothnessRemapMax", 0.85f);    // wet shell
            mat.SetFloat("_AORemapMin", 0f);
            mat.SetFloat("_AORemapMax", 1f);
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);

            var controller = BuildController();
            MeasureTurn(out float turnStride, out bool turnClockwise);
            BuildPrefab(mat, controller, turnStride, turnClockwise);
            AssetDatabase.SaveAssets();
            Debug.Log("[Crabs] Built the crab assets.");
        }

        static string SourcePath(string file) =>
            Path.GetFullPath(Path.Combine(new[] { Application.dataPath }.Concat(Source).Append(file).ToArray()));

        static Texture2D CopyTexture(string file, string to, TextureImporterType type, bool srgb)
        {
            string from = SourcePath(file), dest = Textures + "/" + to;
            if (File.Exists(from)) File.Copy(from, dest, overwrite: true);
            if (!File.Exists(dest)) { Debug.LogWarning($"[Crabs] Missing texture {file}."); return null; }
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(dest);
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dest);
        }

        // HDRP's mask map: metallic in red, occlusion in green (none), smoothness in alpha (1 - roughness).
        static Texture2D MaskMap(string dest)
        {
            string roughPath = SourcePath("Crab_Roughness.jpg"), metalPath = SourcePath("Crab_Metallic.jpg");
            if (File.Exists(roughPath))
            {
                const int n = 2048;
                var rough = Load(roughPath);
                var metal = File.Exists(metalPath) ? Load(metalPath) : null;
                var outTex = new Texture2D(n, n, TextureFormat.RGBA32, false, true);
                var pixels = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                        float r = rough.GetPixelBilinear(u, v).r;
                        float m = metal != null ? metal.GetPixelBilinear(u, v).r : 0f;
                        pixels[y * n + x] = new Color(m, 1f, 0f, 1f - r);
                    }
                outTex.SetPixels32(pixels);
                File.WriteAllBytes(dest, outTex.EncodeToPNG());
                Object.DestroyImmediate(outTex);
                Object.DestroyImmediate(rough);
                if (metal != null) Object.DestroyImmediate(metal);
            }
            if (!File.Exists(dest)) return null;
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(dest);
            importer.sRGBTexture = false;                  // data, not colour
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dest);
        }

        static Texture2D Load(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(path));
            return tex;
        }

        static void SetupModel()
        {
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.addCollider = false;
            importer.bakeAxisConversion = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                // Takes come in as "Crab_Rig|Walk" and so on.
                clip.name = clip.takeName.Contains("|") ? clip.takeName.Substring(clip.takeName.LastIndexOf('|') + 1) : clip.takeName;
                clip.loopTime = Looping.Contains(clip.name);   // the rest play once
                clip.loopPose = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static AnimatorController BuildController()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
                                     .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            // The same asset, emptied and filled again (deleting it would leave the prefab pointing at nothing).
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var old = controller.layers[0].stateMachine;
            foreach (var st in old.states) old.RemoveState(st.state);
            while (controller.parameters.Length > 0) controller.RemoveParameter(0);
            foreach (var param in new[] { "WalkSpeed", "TurnSpeed" })
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = param, type = AnimatorControllerParameterType.Float, defaultFloat = 1f,
                });
            var machine = controller.layers[0].stateMachine;
            foreach (var name in Takes)
            {
                var state = machine.AddState(name);
                state.motion = clips.TryGetValue(name, out var clip) ? clip : null;
                if (clip == null) Debug.LogWarning($"[Crabs] No {name} take in {Model}.");
                if (name == "Walk" || name == "Turn")
                {
                    state.speedParameterActive = true;   // pace and direction (negative: the other way)
                    state.speedParameter = name + "Speed";
                }
                if (name == "Idle") machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // Which way, and how fast, the Turn take carries the crab round: a foot on the ground slides round the
        // body the opposite way. Sampled from the take itself, so the export's axes don't need second-guessing.
        static void MeasureTurn(out float degreesPerSecond, out bool clockwise)
        {
            degreesPerSecond = 53f;
            clockwise = true;
            var clip = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Turn");
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            try
            {
                var foot = Find(go.transform, "leg1.1.L");     // in the set that's on the ground first
                var body = go.GetComponentInChildren<SkinnedMeshRenderer>();
                if (clip == null || foot == null || body == null) return;
                Vector3 centre = body.bounds.center;
                clip.SampleAnimation(go, 0.02f);
                Vector3 a = foot.position - centre;
                clip.SampleAnimation(go, 0.12f);
                Vector3 b = foot.position - centre;
                a.y = b.y = 0f;
                float footTurn = Vector3.SignedAngle(a, b, Vector3.up);   // + = clockwise from above
                degreesPerSecond = Mathf.Abs(footTurn) / 0.1f;
                clockwise = footTurn < 0f;                                 // the body goes the other way
                Debug.Log($"[Crabs] Turn: {degreesPerSecond:0} degrees a second, {(clockwise ? "clockwise" : "anticlockwise")} from above.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void BuildPrefab(Material mat, AnimatorController controller, float turnStride, bool turnClockwise)
        {
            var root = new GameObject("SeabedCrab");
            var walker = root.AddComponent<CrabWalker>();
            // Keep how it's been tuned (speeds, ranges, timings): only what comes from the model is set below.
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (old != null && old.TryGetComponent(out CrabWalker tuned)) EditorUtility.CopySerialized(tuned, walker);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            var body = model.GetComponentInChildren<SkinnedMeshRenderer>();
            body.sharedMaterials = Enumerable.Repeat(mat, body.sharedMaterials.Length).ToArray();
            body.updateWhenOffscreen = false;

            // Eyes (and claws) along +z, its body over the root, feet on it. From the rig, not the skinned
            // mesh's bounds (they reach well below its feet): the model stands on its own origin, as in Blender.
            var eyeL = Find(model.transform, "Eye.3.L");
            var eyeR = Find(model.transform, "Eye.3.R");
            var bodyBone = Find(model.transform, "Body");
            var idle = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Idle");
            if (idle != null) idle.SampleAnimation(model, 0f);
            Vector3 middle = bodyBone != null ? bodyBone.position : body.bounds.center;
            if (eyeL != null && eyeR != null)
            {
                Vector3 toEyes = (eyeL.position + eyeR.position) * 0.5f - middle;
                toEyes.y = 0f;
                float yaw = Vector3.SignedAngle(toEyes, Vector3.forward, Vector3.up);
                model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.transform.localRotation;
                middle = bodyBone != null ? bodyBone.position : middle;
            }
            model.transform.position -= new Vector3(middle.x, 0f, middle.z) - new Vector3(root.transform.position.x, 0f, root.transform.position.z);
            body.ResetBounds();
            Bounds shell = body.bounds;

            var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var so = new SerializedObject(walker);
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("strideSpeed").floatValue = StrideSpeed;
            so.FindProperty("walkGoesRight").boolValue = WalkGoesRight;
            so.FindProperty("turnStride").floatValue = turnStride;
            so.FindProperty("turnGoesClockwise").boolValue = turnClockwise;
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();                     // the controller written before the prefab points at it
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[Crabs] Prefab: {shell.size.x:0.00} x {shell.size.z:0.00} m, {body.sharedMesh.triangles.Length / 3} triangles.");
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var hit = Find(c, name);
                if (hit != null) return hit;
            }
            return null;
        }

        static void Folder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
