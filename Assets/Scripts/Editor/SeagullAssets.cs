using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace FishGame.EditorTools
{
    /// <summary>
    /// Builds the ambient seagulls (FishGame > Build Seagull Assets):
    ///   * Models/Seagull/Seagull.fbx - the user's gull (Coral Reef Decor Pack/Seagull) with takes made for the sea
    ///     by Blender/Seagull/export_seagull.py: Float, Fly and Glide looping; the floating extras (FloatPreen,
    ///     FloatShake, FloatFlap), TakeOff and Land once.
    ///   * Seagull.mat (HDRP/Lit) from the pack's textures (its normal map imported as one).
    ///   * Seagull.controller: a state per take, no transitions - <see cref="Seagull"/> cross-fades between them.
    ///   * Prefabs/Wildlife/Seagull.prefab - beak along +z, its origin the waterline (where it floats), its Seagull.
    ///   * In the game scene (IslandTest, opened alongside whatever's open if it isn't already, saved, closed
    ///     again), a "Seagulls" object (<see cref="Seagulls"/>) with the prefab.
    /// Safe to run again: it updates what's there and keeps the tuning.
    /// </summary>
    public static class SeagullAssets
    {
        const string Root = "Assets/Models/Seagull";
        const string ModelPath = Root + "/Seagull.fbx";
        const string MaterialPath = Root + "/Seagull.mat";
        const string ControllerPath = Root + "/Seagull.controller";
        const string PrefabPath = "Assets/Prefabs/Wildlife/Seagull.prefab";
        const string Pack = "Assets/Coral Reef Decor Pack/Seagull/textures";
        const string DiffusePath = Pack + "/TEX_Seagull_D.png";
        const string NormalPath = Pack + "/TEX_Seagull_N_(convert.io).png";
        const string GameScene = "Assets/Scenes/IslandTest.unity";
        static readonly string[] Takes = { "Float", "FloatPreen", "FloatShake", "FloatFlap", "TakeOff", "Fly", "Glide", "Land" };
        static readonly string[] Looping = { "Float", "Fly", "Glide" };

        [MenuItem("FishGame/Build Seagull Assets")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Wildlife")) AssetDatabase.CreateFolder("Assets/Prefabs", "Wildlife");
            SetupModel();
            var material = BuildMaterial();
            var controller = BuildController();

            var root = new GameObject("Seagull");
            GameObject prefab;
            try
            {
                var gull = root.AddComponent<Seagull>();
                var old = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (old != null && old.TryGetComponent(out Seagull tuned)) EditorUtility.CopySerialized(tuned, gull);

                var model = AddModel(root, material);
                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                // It moves by script and keeps time with its takes (landing): offscreen the takes still run on,
                // only the bones stop being posed.
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

                var so = new SerializedObject(gull);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.ApplyModifiedPropertiesWithoutUndo();

                AssetDatabase.SaveAssets();   // the controller written before the prefab points at it
                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);   // (built in the open scene: gone before any scene's saved)
            }
            PlaceInScene(prefab);
            Debug.Log("[Seagulls] Built the seagull.");
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
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                // Takes come in as "SKM_Seagull.ao|Float" and so on.
                clip.name = clip.takeName.Contains("|") ? clip.takeName.Substring(clip.takeName.LastIndexOf('|') + 1) : clip.takeName;
                clip.loopTime = Looping.Contains(clip.name);
                clip.loopPose = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        static Material BuildMaterial()
        {
            // The pack's normal map came in as a colour texture: import it as a normal map.
            if (AssetImporter.GetAtPath(NormalPath) is TextureImporter normalImporter &&
                normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.sRGBTexture = false;
                normalImporter.SaveAndReimport();
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("HDRP/Lit")) { name = "Seagull" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
                mat.SetFloat("_Smoothness", 0.3f);
            }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(DiffusePath));
            mat.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
            mat.SetFloat("_NormalScale", 1f);
            mat.SetFloat("_Metallic", 0f);
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static AnimatorController BuildController()
        {
            // The same asset, emptied and filled again (deleting it would leave the prefab pointing at nothing).
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var st in machine.states) machine.RemoveState(st.state);
            while (controller.parameters.Length > 0) controller.RemoveParameter(0);
            for (int i = 0; i < Takes.Length; i++)
            {
                var clip = Clip(Takes[i]);
                if (clip == null) { Debug.LogWarning($"[Seagulls] No '{Takes[i]}' take in {ModelPath}."); continue; }
                var state = machine.AddState(Takes[i], new Vector3(300f, 60f * i, 0f));
                state.motion = clip;
                state.writeDefaultValues = false;
                if (Takes[i] == "Float") machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // The gull under root: beak along +z, its origin (the waterline) at the root.
        static GameObject AddModel(GameObject root, Material material)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
            skin.sharedMaterial = material;
            skin.updateWhenOffscreen = false;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            Clip("Glide")?.SampleAnimation(model, 0f);
            Vector3 facing = Find(model.transform, "Beak_end").position - Find(model.transform, "Tail2_end").position;
            facing.y = 0f;
            model.transform.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(facing, Vector3.forward, Vector3.up), 0f)
                                            * model.transform.localRotation;
            model.transform.localPosition = Vector3.zero;

            // Its wings reach well past the bind pose's box (spread, beating, raised): a roomier box, so it isn't
            // culled while a wingtip's still on screen.
            var b = skin.localBounds;
            b.extents *= 1.8f;
            skin.localBounds = b;
            Clip("Float")?.SampleAnimation(model, 0f);
            return model;
        }

        // The flock in the game scene - opened alongside what's open (that's left as it was, unsaved changes
        // and all), saved, and closed again if it wasn't open.
        static void PlaceInScene(GameObject prefab)
        {
            var scene = SceneManager.GetSceneByPath(GameScene);
            bool opened = false;
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Additive);
                opened = true;
            }
            Seagulls flock = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.TryGetComponent(out flock)) break;
            var so = flock != null ? new SerializedObject(flock) : null;
            // Already there and pointing at the prefab: the scene is left alone (unsaved changes of yours included).
            if (so == null || so.FindProperty("prefab").objectReferenceValue != prefab)
            {
                if (flock == null)
                {
                    var go = new GameObject("Seagulls");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    flock = go.AddComponent<Seagulls>();
                    so = new SerializedObject(flock);
                }
                so.FindProperty("prefab").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            if (opened) EditorSceneManager.CloseScene(scene, true);
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
