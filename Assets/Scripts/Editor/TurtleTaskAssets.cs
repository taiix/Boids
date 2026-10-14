using System.IO;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame.EditorTools
{
    /// <summary>
    /// Builds the turtle rescue task's assets (FishGame > Build Turtle Task Assets):
    ///   * Models/Turtle/LoggerheadTurtle.fbx + TurtleNet.fbx - exported from the loggerhead turtle .blend by
    ///     Blender/Turtle/export_turtle.py (the net is a cloth simulation draped over its shell): import settings.
    ///   * Its textures, copied in from the model's folder next to the project (loggerhead-turtle/textures).
    ///   * Materials: the turtle (HDRP/Lit, specular colour workflow like the source), the net and ropes, and
    ///     the knot floats (a faint glow, so they can be found in the murk).
    ///   * LoggerheadTurtle.controller - its swim cycle, looping.
    ///   * TaskFolder/TurtleTask/TangledTurtle.prefab - the networked task object: the turtle turned to face
    ///     forward (+z), its middle at the root, the net on it, and the corners the ropes tie to. Once freed it
    ///     swims about (its TurtleSwimmer, synced by a NetworkTransform).
    ///   * TaskFolder/TurtleTask/SwimmingTurtle.prefab - a turtle roaming the reef from the start.
    /// Safe to run again: it updates what's there.
    /// </summary>
    public static class TurtleTaskAssets
    {
        const string Root = "Assets/Models/Turtle";
        const string Textures = Root + "/Textures";
        const string Materials = Root + "/Materials";
        const string TurtleModel = Root + "/LoggerheadTurtle.fbx";
        const string NetModel = Root + "/TurtleNet.fbx";
        const string ControllerPath = Root + "/LoggerheadTurtle.controller";
        const string PrefabPath = "Assets/TaskFolder/TurtleTask/TangledTurtle.prefab";
        const string SwimmingPrefabPath = "Assets/TaskFolder/TurtleTask/SwimmingTurtle.prefab";
        const float ModelScale = 1.2f;   // a big adult, so it reads next to the fish
        static readonly string[] RockPaths =
        {
            "Assets/Underwater life deluxe/Prefabs/Landscape/Underwater_rock1_prefab.prefab",
            "Assets/Underwater life deluxe/Prefabs/Landscape/Underwater_rock2_prefab.prefab",
            "Assets/Underwater life deluxe/Prefabs/Landscape/Underwater_rock3_prefab.prefab",
            "Assets/Underwater life deluxe/Prefabs/Landscape/Underwater_rock4_prefab.prefab",
        };

        [MenuItem("FishGame/Build Turtle Task Assets")]
        public static void Build()
        {
            Folder(Root, "Textures");
            Folder(Root, "Materials");

            var color = CopyTexture("Loggerhead_color.png", normalMap: false);
            var normal = CopyTexture("Loggerhead_norm.png", normalMap: true);
            var spec = CopyTexture("Loggerhead_spec.png", normalMap: false);
            SetupModel(TurtleModel, animated: true);
            SetupModel(NetModel, animated: false);

            var turtle = TurtleMaterial(Materials + "/Loggerhead.mat", color, normal, spec);
            var net = Cord(Materials + "/TurtleNet.mat", new Color(0.17f, 0.36f, 0.31f), 0.3f);
            var rope = Cord(Materials + "/TurtleRope.mat", new Color(0.22f, 0.28f, 0.22f), 0.2f);
            var knot = Knot(Materials + "/TurtleKnot.mat");
            var controller = BuildController();
            BuildPrefab(turtle, net, rope, knot, controller);
            BuildSwimmingPrefab(turtle, controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[TurtleTask] Built the turtle task assets.");
        }

        // ------------------------------------------------------------------ imports

        static Texture2D CopyTexture(string file, bool normalMap)
        {
            string from = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "loggerhead-turtle", "textures", file));
            string to = Textures + "/" + file;
            if (File.Exists(from)) File.Copy(from, to, overwrite: true);
            if (!File.Exists(to)) { Debug.LogWarning($"[TurtleTask] Missing texture {file}."); return null; }
            AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(to);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normalMap;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(to);
        }

        static void SetupModel(string path, bool animated)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.addCollider = false;
            importer.bakeAxisConversion = true;
            if (animated)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    clip.name = "Swim";
                    clip.loopTime = true;
                    clip.loopPose = false;
                }
                importer.clipAnimations = clips;
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------ materials

        static Material TurtleMaterial(string path, Texture2D color, Texture2D normal, Texture2D spec)
        {
            var m = Upsert(path, Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseColorMap", color);
            m.SetTexture("_NormalMap", normal);
            m.SetFloat("_NormalScale", 1f);
            m.SetFloat("_MaterialID", 4f);                 // specular colour, like the source's spec map
            m.SetColor("_SpecularColor", Color.white);
            m.SetTexture("_SpecularColorMap", spec);
            m.SetFloat("_Smoothness", 0.55f);              // wet shell and skin
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Nylon cord: the net and the ropes.
        static Material Cord(string path, Color colour, float smoothness)
        {
            var m = Upsert(path, Shader.Find("HDRP/Lit"));
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        // A knot float: bright orange, glowing a little whatever the camera's exposure, to find in the murk.
        static Material Knot(string path)
        {
            var m = Upsert(path, Shader.Find("HDRP/Lit"));
            var orange = new Color(0.95f, 0.45f, 0.12f);
            m.SetColor("_BaseColor", orange);
            m.SetFloat("_Smoothness", 0.5f);
            m.SetColor("_EmissiveColor", orange * 0.6f);
            m.SetFloat("_EmissiveExposureWeight", 0f);
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Upsert(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            return m;
        }

        // ------------------------------------------------------------------ animation

        static AnimatorController BuildController()
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(TurtleModel).OfType<AnimationClip>()
                                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            var swim = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Swim") ?? machine.AddState("Swim");
            swim.motion = clip;
            machine.defaultState = swim;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ------------------------------------------------------------------ the prefab

        static void BuildPrefab(Material turtleMat, Material netMat, Material ropeMat, Material knotMat, AnimatorController controller)
        {
            var root = new GameObject("TangledTurtle");
            root.AddComponent<NetworkIdentity>();
            var task = root.AddComponent<TangledTurtle>();
            AddSwimming(root, swimFromStart: false);   // switched on once it's free

            // The net goes on before the turtle is turned and centred, so they move together.
            var model = AddModel(root, turtleMat, controller, out var animator, netGo =>
            {
                foreach (var r in netGo.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = netMat;
            });
            var netGo = model.transform.Find("Net").gameObject;

            var corners = new Transform[4];
            for (int i = 0; i < 4; i++) corners[i] = Find(netGo.transform, $"Corner_{i}");
            var rocks = RockPaths.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).Where(p => p != null).ToArray();

            var so = new SerializedObject(task);
            so.FindProperty("model").objectReferenceValue = model.transform;
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("net").objectReferenceValue = netGo.transform;
            var cornersProp = so.FindProperty("corners");
            cornersProp.arraySize = 4;
            for (int i = 0; i < 4; i++) cornersProp.GetArrayElementAtIndex(i).objectReferenceValue = corners[i];
            so.FindProperty("ropeMaterial").objectReferenceValue = ropeMat;
            so.FindProperty("knotMaterial").objectReferenceValue = knotMat;
            var rocksProp = so.FindProperty("rocks");
            rocksProp.arraySize = rocks.Length;
            for (int i = 0; i < rocks.Length; i++) rocksProp.GetArrayElementAtIndex(i).objectReferenceValue = rocks[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[TurtleTask] Prefab: corners {string.Join(", ", corners.Select(c => c != null ? c.name : "MISSING"))}, {rocks.Length} rocks.");
        }

        // A turtle roaming the reef.
        static void BuildSwimmingPrefab(Material turtleMat, AnimatorController controller)
        {
            var root = new GameObject("SwimmingTurtle");
            root.AddComponent<NetworkIdentity>();
            AddSwimming(root, swimFromStart: true);
            AddModel(root, turtleMat, controller, out _, null);
            PrefabUtility.SaveAsPrefabAsset(root, SwimmingPrefabPath);
            Object.DestroyImmediate(root);
        }

        // Swims on the server; everyone else follows its transform.
        static void AddSwimming(GameObject root, bool swimFromStart)
        {
            var swimmer = root.AddComponent<TurtleSwimmer>();
            var so = new SerializedObject(swimmer);
            so.FindProperty("swimFromStart").boolValue = swimFromStart;
            so.ApplyModifiedPropertiesWithoutUndo();

            var sync = root.AddComponent<NetworkTransformUnreliable>();
            var nt = new SerializedObject(sync);
            nt.FindProperty("target").objectReferenceValue = root.transform;
            nt.FindProperty("syncDirection").enumValueIndex = 0;   // server to clients
            nt.FindProperty("syncPosition").boolValue = true;
            nt.FindProperty("syncRotation").boolValue = true;
            nt.FindProperty("syncScale").boolValue = false;
            nt.FindProperty("onlySyncOnChange").boolValue = true;
            nt.FindProperty("interpolatePosition").boolValue = true;
            nt.FindProperty("interpolateRotation").boolValue = true;
            nt.ApplyModifiedPropertiesWithoutUndo();
        }

        // The turtle model under root: scaled, head along +z, the shell's middle at the root, swimming, solid.
        static GameObject AddModel(GameObject root, Material turtleMat, AnimatorController controller, out Animator animator,
                                   System.Action<GameObject> withNet)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TurtleModel));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * ModelScale;
            var body = model.GetComponentInChildren<SkinnedMeshRenderer>();
            body.sharedMaterial = turtleMat;
            body.updateWhenOffscreen = false;

            if (withNet != null)
            {
                var netGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NetModel));
                netGo.name = "Net";
                netGo.transform.SetParent(model.transform, false);
                withNet(netGo);
            }

            // Head along +z, the shell's middle at the root.
            var head = Find(model.transform, "HEAD");
            Bounds shell = body.bounds;
            if (head != null)
            {
                Vector3 toHead = head.position - shell.center;
                toHead.y = 0f;
                float yaw = Vector3.SignedAngle(toHead, Vector3.forward, Vector3.up);
                model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.transform.localRotation;
            }
            body.ResetBounds();
            shell = body.bounds;
            model.transform.position -= shell.center - root.transform.position;

            animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Something solid where the shell is (fish bump into it, rather than swimming through).
            var box = model.AddComponent<BoxCollider>();
            Vector3 size = model.transform.InverseTransformVector(shell.size);
            size = new Vector3(Mathf.Abs(size.x) * 0.55f, Mathf.Abs(size.y) * 0.8f, Mathf.Abs(size.z) * 0.75f);
            box.center = model.transform.InverseTransformPoint(shell.center);
            box.size = size;
            return model;
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
