using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame.EditorTools
{
    /// <summary>
    /// Builds the orca event's killer whale (FishGame > Build Orca Assets):
    ///   * Models/Orca/KillerWhale.fbx (from orca-killer-whale next to the project): import settings, its
    ///     Swim / Idle / SwimOnSurface takes looping, BiteAttack / AlternativeAttack / Death once.
    ///   * Orca_TailSlapLeft / Orca_TailSlapRight - made here on its bones: it bends into a C away from the side
    ///     it slaps, then snaps its tail across, rolling into it, the swim stilled meanwhile, and swims on.
    ///   * KillerWhale.mat (HDRP/Lit), KillerWhale.controller: Swim (its pace from the SwimSpeed parameter),
    ///     the attacks (played from OrcaHunter, each back to Swim when it's done) and Idle.
    ///   * Prefabs/Wildlife/Orca.prefab - networked (NetworkIdentity, a server-to-clients NetworkTransform),
    ///     its OrcaHunter, and the whale <see cref="Length"/> m long, nose along +z, its middle at the root.
    ///     Set as WaterFxLibrary.orcaPrefab.
    /// Safe to run again: it updates what's there and keeps the OrcaHunter's tuning.
    /// </summary>
    public static class OrcaAssets
    {
        const string Root = "Assets/Models/Orca";
        const string ModelPath = Root + "/KillerWhale.fbx";
        const string TexturePath = Root + "/Killer_Whale.png";
        const string MaterialPath = Root + "/KillerWhale.mat";
        const string ControllerPath = Root + "/KillerWhale.controller";
        const string PrefabPath = "Assets/Prefabs/Wildlife/Orca.prefab";
        const string LibraryPath = "Assets/Resources/WaterFxLibrary.asset";
        const float Length = 7.5f;   // nose to tail (m): half again the shark's ~5
        const float Fps = 30f;
        const float SlapSeconds = 1.3f;

        static readonly string[] Looping = { "Swim", "Idle", "SwimOnSurface" };
        static readonly string[] Attacks = { "BiteAttack", "AlternativeAttack", "TailSlapLeft", "TailSlapRight" };
        // Bent into turns by OrcaHunter, and whipped round in the slaps.
        static readonly string[] Tail = { "Bip001 Tail", "Bip001 Tail1", "Bip001 Tail2", "Bip001 Tail3", "Bip001 Tail4" };
        static readonly string[] Front = { "Bip001 Spine1", "Bip001 Neck", "Bip001 Head" };

        [MenuItem("FishGame/Build Orca Assets")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Wildlife")) AssetDatabase.CreateFolder("Assets/Prefabs", "Wildlife");
            SetupModel();
            var mat = BuildMaterial();

            var root = new GameObject("Orca");
            try
            {
                var model = AddModel(root, mat, out float length);
                var rig = new Rig(root.transform, model.transform, Clip("Swim"));
                var slapLeft = BakeSlap(rig, "Orca_TailSlapLeft", -1f);
                var slapRight = BakeSlap(rig, "Orca_TailSlapRight", 1f);
                rig.Reset();
                var controller = BuildController(slapLeft, slapRight);

                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // OrcaHunter bends bones it animates

                root.AddComponent<NetworkIdentity>();
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

                var hunter = root.AddComponent<OrcaHunter>();
                // Keep how it's been tuned: only what comes from the model is set below.
                var old = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (old != null && old.TryGetComponent(out OrcaHunter tuned)) EditorUtility.CopySerialized(tuned, hunter);
                var so = new SerializedObject(hunter);
                so.FindProperty("animator").objectReferenceValue = animator;
                SetBones(so.FindProperty("tail"), model.transform, Tail);
                SetBones(so.FindProperty("front"), model.transform, Front);
                so.FindProperty("bodyLength").floatValue = length;
                so.ApplyModifiedPropertiesWithoutUndo();

                AssetDatabase.SaveAssets();                   // the controller written before the prefab points at it
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

                var lib = AssetDatabase.LoadAssetAtPath<WaterFxLibrary>(LibraryPath);
                if (lib != null)
                {
                    lib.orcaPrefab = prefab;
                    EditorUtility.SetDirty(lib);
                }
                else Debug.LogWarning("[Orca] No WaterFxLibrary to put the orca in (FishGame > Build Water FX Assets).");
                AssetDatabase.SaveAssets();
                Debug.Log($"[Orca] Built the orca: {length:0.0} m, {slapLeft.name} / {slapRight.name} ({SlapSeconds:0.0}s).");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void SetupModel()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer == null) { Debug.LogError($"[Orca] No model at {ModelPath}."); return; }
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Calculate;   // it comes without any
            importer.normalSmoothingAngle = 80f;
            importer.addCollider = false;
            importer.bakeAxisConversion = true;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            var clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.name = clip.takeName.Contains("|") ? clip.takeName.Substring(clip.takeName.LastIndexOf('|') + 1) : clip.takeName;
                clip.loopTime = Looping.Contains(clip.name);
                clip.loopPose = false;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            var tex = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            if (tex != null)
            {
                tex.textureType = TextureImporterType.Default;
                tex.sRGBTexture = true;
                tex.mipmapEnabled = true;
                tex.maxTextureSize = 2048;
                tex.textureCompression = TextureImporterCompression.CompressedHQ;
                tex.SaveAndReimport();
            }
        }

        static Material BuildMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("HDRP/Lit")) { name = "KillerWhale" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.62f);   // wet skin
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // The whale under root: Length m nose to tail, nose along +z, its middle at the root.
        static GameObject AddModel(GameObject root, Material mat, out float length)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            var body = model.GetComponentInChildren<SkinnedMeshRenderer>();
            body.sharedMaterials = Enumerable.Repeat(mat, body.sharedMaterials.Length).ToArray();
            body.updateWhenOffscreen = true;               // the skinned bounds don't follow it otherwise

            var swim = Clip("Swim");
            if (swim != null) swim.SampleAnimation(model, 0f);
            var nose = Find(model.transform, "Bip001 HeadNub");
            var tail = Find(model.transform, "Bip001 TailNub");
            float raw = Vector3.Distance(nose.position, tail.position);
            model.transform.localScale *= Length / Mathf.Max(1e-5f, raw);

            Vector3 along = nose.position - tail.position;
            along.y = 0f;
            model.transform.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(along, Vector3.forward, Vector3.up), 0f)
                                            * model.transform.localRotation;
            Vector3 middle = (nose.position + tail.position) * 0.5f;
            middle.y = MeshMiddleY(body);
            model.transform.position -= middle - root.transform.position;
            length = Vector3.Distance(nose.position, tail.position);
            return model;
        }

        // The skinned mesh's middle height as it's posed now (its stored bounds don't follow the pose).
        static float MeshMiddleY(SkinnedMeshRenderer body)
        {
            var mesh = new Mesh();
            body.BakeMesh(mesh, true);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in mesh.vertices)
            {
                float y = body.transform.TransformPoint(v).y;
                lo = Mathf.Min(lo, y);
                hi = Mathf.Max(hi, y);
            }
            Object.DestroyImmediate(mesh);
            return (lo + hi) * 0.5f;
        }

        // ------------------------------------------------------------------ the tail slaps

        // side +1 slaps to its right, -1 to its left. Coils into a C away from that side (head and tail both
        // round that way), then snaps the tail across, rolling into it, the head swinging back against it; the
        // swim stilled the while, and back into it as it recovers. The blow lands as the tail whips through, at
        // about 0.47 s (OrcaHunter.ImpactAt).
        static AnimationClip BakeSlap(Rig rig, string name, float side)
        {
            int frames = Mathf.RoundToInt(SlapSeconds * Fps);
            var rot = rig.Bones.ToDictionary(b => b, _ => new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() });
            var pos = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            float[] tailDegrees = { 9f, 12f, 15f, 18f, 20f };
            float[] frontDegrees = { 6f, 7f, 8f };

            for (int f = 0; f <= frames; f++)
            {
                float t = f / Fps;
                float env = Ramp(t, 0f, 0.15f) * (1f - Ramp(t, 0.9f, SlapSeconds));
                // + coiled away from the side it slaps, - snapped across to it.
                float c = Keys(t, (0f, 0f), (0.3f, 1f), (0.38f, 1f), (0.5f, -1.15f), (0.68f, -0.55f), (0.95f, 0.15f), (SlapSeconds, 0f));
                float whip = Pulse(t, 0.36f, 0.3f);

                rig.Swim(t, 1f - 0.85f * env);
                // Positive yaw about up swings the tail (behind, along -z) toward -x: for a slap to the right
                // (+x) it coils that way first, then crosses.
                for (int i = 0; i < rig.Tail.Length; i++) rig.Turn(rig.Tail[i], rig.Up, side * c * tailDegrees[i]);
                float head = Mathf.Max(c, 0f) + 0.5f * Mathf.Max(-c, 0f);   // round with the coil, then against the snap
                for (int i = 0; i < rig.Front.Length; i++) rig.Turn(rig.Front[i], rig.Up, -side * head * frontDegrees[i]);
                rig.Turn(rig.Hips, rig.Forward, -side * 25f * whip);
                rig.Hips.position += rig.Right * (-side * 0.15f * whip);

                foreach (var b in rig.Bones)
                {
                    Quaternion q = b.localRotation;
                    var rc = rot[b];
                    rc[0].AddKey(t, q.x); rc[1].AddKey(t, q.y); rc[2].AddKey(t, q.z); rc[3].AddKey(t, q.w);
                }
                Vector3 p = rig.Hips.localPosition;
                pos[0].AddKey(t, p.x); pos[1].AddKey(t, p.y); pos[2].AddKey(t, p.z);
            }

            string path = $"{Root}/{name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.frameRate = Fps;
            foreach (var b in rig.Bones)
            {
                string bonePath = AnimationUtility.CalculateTransformPath(b, rig.Model);
                var rc = rot[b];
                clip.SetCurve(bonePath, typeof(Transform), "localRotation.x", rc[0]);
                clip.SetCurve(bonePath, typeof(Transform), "localRotation.y", rc[1]);
                clip.SetCurve(bonePath, typeof(Transform), "localRotation.z", rc[2]);
                clip.SetCurve(bonePath, typeof(Transform), "localRotation.w", rc[3]);
            }
            string hipsPath = AnimationUtility.CalculateTransformPath(rig.Hips, rig.Model);
            clip.SetCurve(hipsPath, typeof(Transform), "localPosition.x", pos[0]);
            clip.SetCurve(hipsPath, typeof(Transform), "localPosition.y", pos[1]);
            clip.SetCurve(hipsPath, typeof(Transform), "localPosition.z", pos[2]);
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>The whale's bones, and its own axes (the prefab root's) to bend them about.</summary>
        class Rig
        {
            public readonly Transform Model, Hips;
            public readonly Transform[] Tail, Front;
            public readonly List<Transform> Bones = new List<Transform>();
            public readonly Vector3 Up, Forward, Right;
            readonly AnimationClip _swim;
            readonly Dictionary<Transform, Quaternion> _neutral = new Dictionary<Transform, Quaternion>();
            Vector3 _neutralHips;

            public Rig(Transform root, Transform model, AnimationClip swim)
            {
                Model = model;
                _swim = swim;
                Up = root.up; Forward = root.forward; Right = root.right;
                Hips = Find(model, "Bip001");
                Tail = System.Array.ConvertAll(OrcaAssets.Tail, n => Find(model, n));
                Front = System.Array.ConvertAll(OrcaAssets.Front, n => Find(model, n));
                Bones.AddRange(Hips.GetComponentsInChildren<Transform>());

                // The swim cycle's middle: each bone averaged round it - the body held straight.
                const int samples = 24;
                var sum = new Dictionary<Transform, Vector4>();
                for (int i = 0; i < samples; i++)
                {
                    swim.SampleAnimation(model.gameObject, swim.length * i / samples);
                    foreach (var b in Bones)
                    {
                        Quaternion q = b.localRotation;
                        var v = new Vector4(q.x, q.y, q.z, q.w);
                        sum.TryGetValue(b, out var acc);
                        if (Vector4.Dot(acc, v) < 0f) v = -v;
                        sum[b] = acc + v;
                    }
                    _neutralHips += Hips.localPosition / samples;
                }
                foreach (var kv in sum)
                {
                    Vector4 v = kv.Value.normalized;
                    _neutral[kv.Key] = new Quaternion(v.x, v.y, v.z, v.w);
                }
            }

            /// <summary>The swim cycle at <paramref name="t"/>, <paramref name="weight"/> of it (0 = held straight).</summary>
            public void Swim(float t, float weight)
            {
                _swim.SampleAnimation(Model.gameObject, t % _swim.length);
                foreach (var b in Bones)
                    b.localRotation = Quaternion.Slerp(_neutral[b], b.localRotation, weight);
                Hips.localPosition = Vector3.Lerp(_neutralHips, Hips.localPosition, weight);
            }

            public void Reset() => _swim.SampleAnimation(Model.gameObject, 0f);

            public void Turn(Transform bone, Vector3 axis, float degrees)
            {
                if (Mathf.Abs(degrees) < 1e-4f) return;
                bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
            }
        }

        static float Ramp(float t, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        static float Pulse(float t, float at, float width)
        {
            float d = (t - at) / width;
            return d < 0f || d > 1f ? 0f : Mathf.Sin(Mathf.PI * d);
        }

        // Piecewise, eased between keys.
        static float Keys(float t, params (float time, float value)[] keys)
        {
            if (t <= keys[0].time) return keys[0].value;
            for (int i = 1; i < keys.Length; i++)
                if (t <= keys[i].time)
                {
                    float k = Mathf.InverseLerp(keys[i - 1].time, keys[i].time, t);
                    return Mathf.Lerp(keys[i - 1].value, keys[i].value, Mathf.SmoothStep(0f, 1f, k));
                }
            return keys[keys.Length - 1].value;
        }

        // ------------------------------------------------------------------ animator

        static AnimatorController BuildController(AnimationClip slapLeft, AnimationClip slapRight)
        {
            // The same asset, emptied and filled again (deleting it would leave the prefab pointing at nothing).
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var st in machine.states) machine.RemoveState(st.state);
            foreach (var t in machine.anyStateTransitions) machine.RemoveAnyStateTransition(t);
            while (controller.parameters.Length > 0) controller.RemoveParameter(0);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "SwimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f,
            });

            var swim = machine.AddState("Swim", new Vector3(300f, 0f));
            swim.motion = Clip("Swim");
            swim.speedParameterActive = true;
            swim.speedParameter = "SwimSpeed";
            machine.defaultState = swim;
            machine.AddState("Idle", new Vector3(300f, -80f)).motion = Clip("Idle");

            var motions = new Dictionary<string, Motion>
            {
                { "BiteAttack", Clip("BiteAttack") }, { "AlternativeAttack", Clip("AlternativeAttack") },
                { "TailSlapLeft", slapLeft }, { "TailSlapRight", slapRight },
            };
            for (int i = 0; i < Attacks.Length; i++)
            {
                var state = machine.AddState(Attacks[i], new Vector3(600f, -60f + i * 70f));
                state.motion = motions[Attacks[i]];
                var back = state.AddTransition(swim);
                back.hasExitTime = true;
                back.exitTime = 0.92f;
                back.duration = 0.25f;
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // ------------------------------------------------------------------ helpers

        static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

        static void SetBones(SerializedProperty list, Transform model, string[] names)
        {
            list.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = Find(model, names[i]);
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
    }
}
