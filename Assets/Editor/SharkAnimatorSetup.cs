#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ReefRun.EditorTools
{
    /// <summary>
    /// One-click rebuild of the shark's Animator Controller so it works with
    /// SwimAnimatorLink + SharkAbilities. Run:
    ///   Tools > Reef Run > Setup Shark Animator
    ///
    /// It targets the selected AnimatorController if you have one selected, otherwise it
    /// finds "Great_white_shark_anim_controller". It reuses the existing swim / fastswim /
    /// eat clips and rebuilds the graph:
    ///   * Parameters: Speed (float), Boosting (bool), Bite (trigger)
    ///   * "Locomotion" 1D blend tree on Speed: swim @0 -> fastswim @1   (default state)
    ///   * "eat" state, entered from AnyState when Bite fires, returns to Locomotion
    /// Safe to re-run; it clears and rebuilds the single layer.
    /// </summary>
    public static class SharkAnimatorSetup
    {
        const string SpeedParam = "Speed";
        const string BoostingParam = "Boosting";
        const string BiteParam = "Bite";

        [MenuItem("Tools/Reef Run/Setup Shark Animator")]
        public static void Setup()
        {
            var controller = ResolveController();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Shark Animator",
                    "Couldn't find the controller. Select the shark's AnimatorController in the Project " +
                    "window (or make sure 'Great_white_shark_anim_controller' exists), then run again.", "OK");
                return;
            }

            var sm = controller.layers[0].stateMachine;

            // 1) Capture the existing clips by state name (check 'fast' before 'swim').
            Motion swim = null, fast = null, eat = null;
            foreach (var cs in sm.states)
            {
                string n = cs.state.name.ToLowerInvariant();
                if (n.Contains("fast")) fast = cs.state.motion;
                else if (n.Contains("eat")) eat = cs.state.motion;
                else if (n.Contains("swim")) swim = cs.state.motion;
            }

            if (swim == null || fast == null || eat == null)
            {
                EditorUtility.DisplayDialog("Shark Animator",
                    $"Expected states named swim / fastswim / eat with clips assigned.\n" +
                    $"Found: swim={(swim ? "ok" : "missing")}, fastswim={(fast ? "ok" : "missing")}, eat={(eat ? "ok" : "missing")}.",
                    "OK");
                return;
            }

            // 2) Clear the layer's states (snapshot first; RemoveState mutates the list).
            var existing = sm.states;
            foreach (var cs in existing)
                sm.RemoveState(cs.state);
            sm.anyStateTransitions = new AnimatorStateTransition[0];

            // 3) Reset parameters.
            for (int i = controller.parameters.Length - 1; i >= 0; i--)
                controller.RemoveParameter(i);
            controller.AddParameter(SpeedParam, AnimatorControllerParameterType.Float);
            controller.AddParameter(BoostingParam, AnimatorControllerParameterType.Bool);
            controller.AddParameter(BiteParam, AnimatorControllerParameterType.Trigger);

            // 4) Locomotion blend tree (swim @0 -> fastswim @1).
            AnimatorState loco = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = SpeedParam;
            tree.useAutomaticThresholds = false;
            tree.AddChild(swim, 0f);
            tree.AddChild(fast, 1f);
            loco.writeDefaultValues = true;
            sm.defaultState = loco;

            // 5) Eat state.
            var eatState = sm.AddState("eat");
            eatState.motion = eat;
            eatState.writeDefaultValues = true;

            // AnyState -> eat when Bite fires (immediate, doesn't interrupt itself).
            var toEat = sm.AddAnyStateTransition(eatState);
            toEat.AddCondition(AnimatorConditionMode.If, 0f, BiteParam);
            toEat.hasExitTime = false;
            toEat.duration = 0.08f;
            toEat.hasFixedDuration = true;
            toEat.canTransitionToSelf = false;

            // eat -> Locomotion once the bite clip is (almost) done.
            var backToLoco = eatState.AddTransition(loco);
            backToLoco.hasExitTime = true;
            backToLoco.exitTime = 0.85f;
            backToLoco.duration = 0.15f;
            backToLoco.hasFixedDuration = true;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SharkAnimatorSetup] Rebuilt '{controller.name}': Speed blend (swim->fastswim) + Bite->eat.", controller);
            Selection.activeObject = controller;
        }

        static AnimatorController ResolveController()
        {
            if (Selection.activeObject is AnimatorController sel)
                return sel;

            foreach (var guid in AssetDatabase.FindAssets("Great_white_shark_anim_controller t:AnimatorController"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (ac != null) return ac;
            }
            return null;
        }
    }
}
#endif
