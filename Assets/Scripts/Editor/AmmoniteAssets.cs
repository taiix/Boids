using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame.EditorTools
{
    /// <summary>
    /// Builds the ambient ammonites (FishGame > Build Ammonite Assets):
    ///   * Models/Ammonite/Ammonite.fbx - exported from the downloaded .glb by Blender/Ammonite/export_ammonite.py:
    ///     import settings, the swims / Idle / Float looping. Its textures (Models/Ammonite/Textures).
    ///   * Ammonite_Body.mat, Ammonite_Shell.mat (HDRP/Lit).
    ///   * Ammonite.controller: one blend tree - Speed picks Idle (0) / the swims (1) / the fast swims (2), and in
    ///     each, Turn and Climb blend in its Left / Right / Up / Down takes. Which take bends which way is measured
    ///     from the takes (its tentacles trail round the inside of a turn), not taken from their names.
    ///   * Prefabs/Wildlife/Ammonite.prefab - shell first along +z (it swims shell first, tentacles trailing), its
    ///     shell's middle at the root, its AmmoniteSwimmer.
    ///   * In the open game scene, an "Ammonites" object (AmmoniteSchools) with the prefab; the scene is saved.
    /// Safe to run again: it updates what's there and keeps the tuning.
    /// </summary>
    public static class AmmoniteAssets
    {
        const string Root = "Assets/Models/Ammonite";
        const string ModelPath = Root + "/Ammonite.fbx";
        const string Textures = Root + "/Textures";
        const string ControllerPath = Root + "/Ammonite.controller";
        const string PrefabPath = "Assets/Prefabs/Wildlife/Ammonite.prefab";
        static readonly string[] Looping = { "Swim", "SwimFast", "SwimLeft", "SwimRight", "SwimUp", "SwimDown", "SwimFastLeft",
                                             "SwimFastRight", "SwimFastUp", "SwimFastDown", "Idle", "Float" };

        [MenuItem("FishGame/Build Ammonite Assets")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Wildlife")) AssetDatabase.CreateFolder("Assets/Prefabs", "Wildlife");
            SetupTextures();
            SetupModel();
            var body = Material("Ammonite_Body", "BakedAmmoniteStripe", "bakedAmmoniteLOD0N", 0.55f);
            var shell = Material("Ammonite_Shell", "PeltocerasRockA", "PeltocerasRockLOD0N", 0.35f);
            shell.SetColor("_BaseColor", new Color(0.78f, 0.72f, 0.66f));   // a touch darker and warmer: it read pale under the caustics
            EditorUtility.SetDirty(shell);

            var root = new GameObject("Ammonite");
            GameObject prefab;
            try
            {
                var swimmer = root.AddComponent<AmmoniteSwimmer>();
                var old = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (old != null && old.TryGetComponent(out AmmoniteSwimmer tuned)) EditorUtility.CopySerialized(tuned, swimmer);

                var model = AddModel(root, body, shell);
                var turns = MeasureTurns(model, root.transform);
                var controller = BuildController(turns);
                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullCompletely;   // it moves by script; the look can stop offscreen

                var so = new SerializedObject(swimmer);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.ApplyModifiedPropertiesWithoutUndo();

                AssetDatabase.SaveAssets();   // the controller written before the prefab points at it
                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[Ammonites] Built the ammonite: " + string.Join(", ", turns.Select(kv => $"{kv.Key} {kv.Value}")));
            }
            finally
            {
                Object.DestroyImmediate(root);   // (built in the open scene: gone before the scene's saved)
            }
            PlaceInScene(prefab);
        }

        static void SetupTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Textures }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool normal = path.Contains("LOD0N");
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normal;
                importer.maxTextureSize = 1024;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        static void SetupModel()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.bakeAxisConversion = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                // Takes come in as "Root|Swim" and so on.
                clip.name = clip.takeName.Contains("|") ? clip.takeName.Substring(clip.takeName.LastIndexOf('|') + 1) : clip.takeName;
                clip.loopTime = Looping.Contains(clip.name);
                clip.loopPose = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static Material Material(string name, string color, string normal, float smoothness)
        {
            string path = $"{Root}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("HDRP/Lit")) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{color}.png"));
            mat.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Textures}/{normal}.png"));
            mat.SetFloat("_NormalScale", 1f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", smoothness);
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // The ammonite under root: shell first along +z, its shell's middle at the root.
        static GameObject AddModel(GameObject root, Material body, Material shell)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            skin.sharedMaterial = body;
            skin.updateWhenOffscreen = false;
            MeshRenderer shellRenderer = null;
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = shell;
                shellRenderer = r;
            }

            Clip("Swim")?.SampleAnimation(model, 0f);
            Vector3 travel = Find(model.transform, "Shell").position - TipsMiddle(model.transform);
            travel.y = 0f;
            model.transform.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(travel, Vector3.forward, Vector3.up), 0f)
                                            * model.transform.localRotation;
            Vector3 middle = shellRenderer != null ? shellRenderer.bounds.center : skin.bounds.center;
            model.transform.position -= middle - root.transform.position;

            // Its tentacles reach well past the bind pose's box as they swim: make the box roomier, so it isn't
            // culled while a tentacle's still on screen.
            var b = skin.localBounds;
            b.extents *= 1.6f;
            skin.localBounds = b;
            return model;
        }

        static Vector3 TipsMiddle(Transform model)
        {
            var tips = model.GetComponentsInChildren<Transform>()
                            .Where(t => t.name.StartsWith("Tentacle") && !t.Cast<Transform>().Any(c => c.name.StartsWith("Tentacle"))).ToList();
            Vector3 sum = Vector3.zero;
            foreach (var t in tips) sum += t.position;
            return tips.Count > 0 ? sum / tips.Count : model.position;
        }

        // Where each turning take puts it in the blend: (turn, climb), +turn = turning right, +climb = climbing.
        // Its tentacles trail round the inside of a turn: tips out to the right = turning right; tips hanging
        // below = climbing. Measured against the straight swim, in the prefab's space (+z ahead, +x right).
        static Dictionary<string, Vector2> MeasureTurns(GameObject model, Transform root)
        {
            Vector3 Tips(string clip)
            {
                var c = Clip(clip);
                if (c == null) return Vector3.zero;
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < 8; i++)
                {
                    c.SampleAnimation(model, c.length * i / 8f);
                    sum += root.InverseTransformPoint(TipsMiddle(model.transform)) - root.InverseTransformPoint(Find(model.transform, "Shell").position);
                }
                return sum / 8f;
            }

            var result = new Dictionary<string, Vector2>();
            foreach (var (straight, prefix) in new[] { ("Swim", "Swim"), ("SwimFast", "SwimFast") })
            {
                Vector3 baseTips = Tips(straight);
                foreach (var side in new[] { "Left", "Right" })
                {
                    float dx = Tips(prefix + side).x - baseTips.x;
                    result[prefix + side] = new Vector2(Mathf.Sign(dx), 0f);
                }
                foreach (var way in new[] { "Up", "Down" })
                {
                    float dy = Tips(prefix + way).y - baseTips.y;
                    result[prefix + way] = new Vector2(0f, -Mathf.Sign(dy));
                }
            }
            Clip("Swim")?.SampleAnimation(model, 0f);
            return result;
        }

        static AnimatorController BuildController(Dictionary<string, Vector2> turns)
        {
            // The same asset, emptied and filled again (deleting it would leave the prefab pointing at nothing).
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var st in machine.states) machine.RemoveState(st.state);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>())
                Object.DestroyImmediate(sub, true);   // the old trees, left over in the asset
            while (controller.parameters.Length > 0) controller.RemoveParameter(0);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Turn", AnimatorControllerParameterType.Float);
            controller.AddParameter("Climb", AnimatorControllerParameterType.Float);

            var state = controller.CreateBlendTreeInController("Swimming", out BlendTree tree, 0);
            machine.defaultState = state;
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("Idle"), 0f);
            Directional(tree.CreateBlendTreeChild(1f), "Swim", turns);
            Directional(tree.CreateBlendTreeChild(2f), "SwimFast", turns);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static void Directional(BlendTree tree, string prefix, Dictionary<string, Vector2> turns)
        {
            tree.name = prefix;
            tree.blendType = BlendTreeType.SimpleDirectional2D;
            tree.blendParameter = "Turn";
            tree.blendParameterY = "Climb";
            tree.AddChild(Clip(prefix), Vector2.zero);
            foreach (var way in new[] { "Left", "Right", "Up", "Down" })
                if (turns.TryGetValue(prefix + way, out var at) && Clip(prefix + way) != null)
                    tree.AddChild(Clip(prefix + way), at);
        }

        // The schools in the open game scene (the one with the seabed).
        static void PlaceInScene(GameObject prefab)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Object.FindAnyObjectByType<TestTerrain>() == null)
            {
                Debug.LogWarning($"[Ammonites] '{scene.name}' has no seabed: open the game scene and build again to add them to it.");
                return;
            }
            AmmoniteSchools schools = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.TryGetComponent(out schools)) break;
            if (schools == null) schools = new GameObject("Ammonites").AddComponent<AmmoniteSchools>();
            var so = new SerializedObject(schools);
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

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
    }
}
