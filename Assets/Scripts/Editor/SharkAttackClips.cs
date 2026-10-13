using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// Builds the shark's violent eating clips and wires them into its Animator:
    ///   * <b>Shark_DevourViolent</b> - one shot (~1.5 s): gape, snap shut, then shake the catch side to side
    ///     with the whole body (head one way, tail the other, rolling), two more chomps, settle.
    ///     Played for every fish eaten (trigger "Devour"), and as the final swallow of a player.
    ///   * <b>Shark_ThrashLoop</b> - loops while the shark holds a struggling player fish (bool "Holding"):
    ///     jaws clamped with chomps, head and body thrashing, rolling.
    ///
    /// The clips are keyframed on the shark's own bones, on top of its fast-swim cycle so the body keeps
    /// swimming underneath. (Generated here rather than in Blender: the rig is a 3ds Max export with
    /// stretched head/jaw bones, which Blender's FBX importer can't round-trip.) Tweak the numbers below and
    /// run FishGame > Build Shark Attack Clips again; the clips can also be edited in the Animation window.
    /// </summary>
    public static class SharkAttackClips
    {
        const string PrefabPath = "Assets/Prefabs/PlayerShark.prefab";
        const string ModelPath = "Assets/Underwater life deluxe/Mesh/Animals/great_white_shark_unity_exp.FBX";
        const string ControllerPath = "Assets/Underwater life deluxe/Animation Controllers/Animals/Great_white_shark_anim_controller.controller";
        const string OutFolder = "Assets/Animations/Shark";
        const float Fps = 30f;

        static readonly string[] Spine = { "Bone015", "Bone016", "Bone017", "Bone018", "Bone019", "Bone020", "Bone021" };
        static readonly string[] Jaws = { "Bone011", "Bone013" };

        [MenuItem("FishGame/Build Shark Attack Clips")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var model = root.transform.Find("SharkModel");
                var swim = LoadClip("Great white shark fastswim");
                var eat = LoadClip("Great white shark eat");
                if (model == null || swim == null || eat == null)
                {
                    Debug.LogError("[SharkClips] Missing the shark model or its swim/eat clips.");
                    return;
                }

                var rig = new Rig(model, eat);
                if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
                if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets/Animations", "Shark");

                var devour = Bake(rig, swim, "Shark_DevourViolent", 1.5f, loop: false, DevourPose);
                var thrash = Bake(rig, swim, "Shark_ThrashLoop", swim.length, loop: true, ThrashPose);
                Wire(devour, thrash);
                Debug.Log($"[SharkClips] Built {devour.name} ({devour.length:0.00}s) and {thrash.name} ({thrash.length:0.00}s, loop).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------ the motion

        /// <summary>What the body does at one moment, on top of the swim cycle. Angles in degrees.</summary>
        struct Pose
        {
            public float jaw;          // 0 = shut, 1 = the eat clip's widest gape, >1 wider still
            public float headYaw, headPitch;
            public float bodyYaw, bodyRoll, bodyPitch;
            public float spineYawAmp;  // tail whip strength (degrees at the tail tip)
            public float spinePhase;   // whip phase (radians)
            public Vector3 jolt;       // whole body shoved (metres)
        }

        // Gape, snap shut, shake it hard with the whole body, two more chomps, settle.
        static Pose DevourPose(float t)
        {
            const float snap = 0.18f, shakeHz = 3.2f;
            float shake = Mathf.Max(0f, t - snap);
            float env = Ramp(t, 0.12f, 0.26f) * (1f - Ramp(t, 1.0f, 1.45f));
            float w = 2f * Mathf.PI * shakeHz * shake;

            var p = new Pose
            {
                jaw = Keys(t, (0f, 0.2f), (0.10f, 1.35f), (0.17f, 0f), (0.42f, 0.15f), (0.52f, 0.9f), (0.58f, 0f),
                              (0.82f, 0.12f), (0.92f, 0.8f), (0.98f, 0f), (1.5f, 0f)),
                headPitch = Keys(t, (0f, 0f), (0.10f, -14f), (0.18f, 12f), (0.34f, 0f), (0.58f, 7f), (0.70f, 0f),
                                    (0.98f, 7f), (1.1f, 0f)),
                headYaw = env * 32f * Mathf.Sin(w),
                bodyYaw = env * 12f * Mathf.Sin(w + Mathf.PI * 0.5f),
                bodyRoll = env * 24f * Mathf.Sin(w * 0.5f) + 14f * Pulse(t, snap, 0.25f),
                bodyPitch = -6f * Pulse(t, 0.08f, 0.2f),
                spineYawAmp = env * 30f,
                spinePhase = w,
            };
            p.jolt = new Vector3(env * 0.16f * Mathf.Sin(w), 0f, 0.3f * Pulse(t, snap, 0.22f));
            return p;
        }

        // Holding a struggling fish: jaws clamped with two chomps a loop, head and body thrashing, rolling.
        // Every rhythm fits a whole number of times into the loop, so it repeats seamlessly.
        static Pose ThrashPose(float t, float loop)
        {
            float u = t / loop;                       // 0..1 around the loop
            float w = 2f * Mathf.PI * 4f * u;         // 4 head shakes per loop
            float chomp = Mathf.Max(Chomp(u, 0.20f), Chomp(u, 0.70f));
            var p = new Pose
            {
                jaw = 0.12f + chomp,
                headYaw = 34f * Mathf.Sin(w),
                headPitch = 4f * Mathf.Sin(w * 2f) + 9f * Mathf.Max(SnapKick(u, 0.20f), SnapKick(u, 0.70f)),
                bodyYaw = 13f * Mathf.Sin(w + Mathf.PI * 0.5f),
                bodyRoll = 26f * Mathf.Sin(2f * Mathf.PI * 2f * u),
                bodyPitch = 5f * Mathf.Sin(2f * Mathf.PI * 1f * u),
                spineYawAmp = 34f,
                spinePhase = w,
            };
            p.jolt = new Vector3(0.18f * Mathf.Sin(w), 0.06f * Mathf.Sin(w * 2f), 0.08f * Mathf.Sin(2f * Mathf.PI * 2f * u));
            return p;
        }

        // Jaw pops open then slams shut around `at` (u, a fraction of the loop).
        static float Chomp(float u, float at)
        {
            float d = u - at;
            if (d < -0.08f || d > 0.04f) return 0f;
            return d < 0f ? 0.65f * Mathf.SmoothStep(0f, 1f, (d + 0.08f) / 0.08f) : 0.65f * (1f - d / 0.04f);
        }

        // Head jerks down as the jaws slam.
        static float SnapKick(float u, float at)
        {
            float d = u - at;
            return d < 0f || d > 0.12f ? 0f : Mathf.Sin(Mathf.PI * d / 0.12f);
        }

        static float Ramp(float t, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

        static float Pulse(float t, float at, float width)
        {
            float d = (t - at) / width;
            return d < 0f || d > 1f ? 0f : Mathf.Sin(Mathf.PI * d);
        }

        // Piecewise, eased between keys - snaps stay sharp, holds stay held.
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

        // ------------------------------------------------------------------ baking

        delegate Pose PoseAt(float t);
        delegate Pose LoopPoseAt(float t, float loop);

        static AnimationClip Bake(Rig rig, AnimationClip swim, string name, float length, bool loop, PoseAt pose) =>
            BakeFrames(rig, swim, name, length, loop, t => pose(t));

        static AnimationClip Bake(Rig rig, AnimationClip swim, string name, float length, bool loop, LoopPoseAt pose) =>
            BakeFrames(rig, swim, name, length, loop, t => pose(t, length));

        static AnimationClip BakeFrames(Rig rig, AnimationClip swim, string name, float length, bool loop, System.Func<float, Pose> pose)
        {
            int frames = Mathf.RoundToInt(length * Fps);
            var rot = new Dictionary<Transform, AnimationCurve[]>();
            var pos = new Dictionary<Transform, AnimationCurve[]>();
            foreach (var b in rig.Bones)
            {
                rot[b] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                pos[b] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            }

            for (int f = 0; f <= frames; f++)
            {
                float t = f / Fps;
                swim.SampleAnimation(rig.Model.gameObject, t % swim.length);
                rig.Apply(pose(loop && f == frames ? 0f : t)); // a loop's last frame is its first
                foreach (var b in rig.Bones)
                {
                    Quaternion q = b.localRotation;
                    Vector3 p = b.localPosition;
                    var rc = rot[b];
                    rc[0].AddKey(t, q.x); rc[1].AddKey(t, q.y); rc[2].AddKey(t, q.z); rc[3].AddKey(t, q.w);
                    var pc = pos[b];
                    pc[0].AddKey(t, p.x); pc[1].AddKey(t, p.y); pc[2].AddKey(t, p.z);
                }
            }

            string path = $"{OutFolder}/{name}.anim";
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
                if (b == rig.Root)
                {
                    var pc = pos[b];
                    clip.SetCurve(bonePath, typeof(Transform), "localPosition.x", pc[0]);
                    clip.SetCurve(bonePath, typeof(Transform), "localPosition.y", pc[1]);
                    clip.SetCurve(bonePath, typeof(Transform), "localPosition.z", pc[2]);
                }
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return clip;
        }

        /// <summary>The shark's bones and how to bend them. Bends are turns about the shark's own axes
        /// (up = yaw, forward = roll, right = pitch), applied root first so each bone carries its children.</summary>
        class Rig
        {
            public readonly Transform Model, Root, Head;
            public readonly List<Transform> Bones = new List<Transform>();
            readonly Transform[] _spine;
            readonly Transform[] _jaws;
            readonly Quaternion[] _jawOpen;   // the eat clip's widest gape, per jaw bone (local)
            readonly Vector3 _rootHome;

            public Rig(Transform model, AnimationClip eat)
            {
                Model = model;
                var byName = new Dictionary<string, Transform>();
                foreach (var t in model.GetComponentsInChildren<Transform>())
                    byName[t.name] = t;
                Root = byName["Bone001"];
                Head = byName["Bone022"];
                _spine = System.Array.ConvertAll(Spine, n => byName[n]);
                _jaws = System.Array.ConvertAll(Jaws, n => byName[n]);

                // Every bone the stock clips animate (everything under the root bone, not the mouth anchor).
                foreach (var t in Root.GetComponentsInChildren<Transform>())
                    if (t.name.StartsWith("Bone")) Bones.Add(t);

                eat.SampleAnimation(model.gameObject, eat.length * 0.75f); // widest gape
                _jawOpen = System.Array.ConvertAll(_jaws, j => j.localRotation);
                eat.SampleAnimation(model.gameObject, 0f);
                _rootHome = Root.localPosition;
            }

            public void Apply(Pose p)
            {
                // Jaws: from wherever the swim has them toward the gape (past it for > 1).
                for (int i = 0; i < _jaws.Length; i++)
                    _jaws[i].localRotation = Quaternion.SlerpUnclamped(_jaws[i].localRotation, _jawOpen[i], p.jaw);

                Vector3 up = Model.up, forward = Model.forward, right = Model.right;

                // Whole body: yaw, roll, pitch about its middle, and shoved.
                Root.localPosition += p.jolt;
                Turn(Root, up, p.bodyYaw);
                Turn(Root, forward, p.bodyRoll);
                Turn(Root, right, p.bodyPitch);

                // Head whips one way...
                Turn(Head, up, p.headYaw);
                Turn(Head, right, p.headPitch);

                // ...and the tail lashes the other, a wave running down to the tip (bigger toward the tail).
                for (int i = 0; i < _spine.Length; i++)
                {
                    float share = (i + 1f) / _spine.Length;
                    float yaw = -p.spineYawAmp * (0.25f + 0.75f * share) / _spine.Length * 2.2f
                                * Mathf.Sin(p.spinePhase - 0.55f * (i + 1));
                    Turn(_spine[i], up, yaw);
                }
            }

            static void Turn(Transform bone, Vector3 axis, float degrees)
            {
                if (Mathf.Abs(degrees) < 1e-4f) return;
                bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
            }
        }

        // ------------------------------------------------------------------ animator

        // Any State -> DevourViolent on "Devour"; Any State -> Thrash while "Holding"; both back to Locomotion.
        static void Wire(AnimationClip devour, AnimationClip thrash)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) { Debug.LogError("[SharkClips] Shark animator controller not found."); return; }

            if (!System.Array.Exists(controller.parameters, p => p.name == "Holding"))
                controller.AddParameter("Holding", AnimatorControllerParameterType.Bool);

            var sm = controller.layers[0].stateMachine;
            AnimatorState locomotion = null;
            foreach (var s in sm.states) if (s.state.name == "Locomotion") locomotion = s.state;

            var devourState = StateFor(sm, "DevourViolent", devour, new Vector3(450f, 110f));
            var thrashState = StateFor(sm, "Thrash", thrash, new Vector3(450f, 220f));

            // Drop transitions this tool made before, so running it again doesn't stack them.
            foreach (var t in sm.anyStateTransitions)
                if (t.destinationState == devourState || t.destinationState == thrashState)
                    sm.RemoveAnyStateTransition(t);
            foreach (var t in devourState.transitions) devourState.RemoveTransition(t);
            foreach (var t in thrashState.transitions) thrashState.RemoveTransition(t);

            var toDevour = sm.AddAnyStateTransition(devourState);
            toDevour.AddCondition(AnimatorConditionMode.If, 0f, "Devour");
            toDevour.hasExitTime = false;
            toDevour.duration = 0.06f;
            toDevour.canTransitionToSelf = true;

            var toThrash = sm.AddAnyStateTransition(thrashState);
            toThrash.AddCondition(AnimatorConditionMode.If, 0f, "Holding");
            toThrash.hasExitTime = false;
            toThrash.duration = 0.12f;
            toThrash.canTransitionToSelf = false;

            // Devour and the hold win over a plain bite swing.
            var any = new List<AnimatorStateTransition>(sm.anyStateTransitions);
            any.Remove(toDevour);
            any.Remove(toThrash);
            any.Insert(0, toThrash);
            any.Insert(0, toDevour);
            sm.anyStateTransitions = any.ToArray();

            if (locomotion != null)
            {
                var done = devourState.AddTransition(locomotion);
                done.hasExitTime = true;
                done.exitTime = 0.9f;
                done.duration = 0.2f;

                var letGo = thrashState.AddTransition(locomotion);
                letGo.AddCondition(AnimatorConditionMode.IfNot, 0f, "Holding");
                letGo.hasExitTime = false;
                letGo.duration = 0.25f;
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        static AnimatorState StateFor(AnimatorStateMachine sm, string name, Motion motion, Vector3 at)
        {
            foreach (var s in sm.states)
                if (s.state.name == name) { s.state.motion = motion; return s.state; }
            var state = sm.AddState(name, at);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        static AnimationClip LoadClip(string name)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (o is AnimationClip c && c.name == name) return c;
            return null;
        }
    }
}
