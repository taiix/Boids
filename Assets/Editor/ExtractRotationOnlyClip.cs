using UnityEngine;
using UnityEditor;

public static class ExtractRotationOnlyClip
{
    [MenuItem("Assets/Extract Rotation-Only Clip", true)]
    static bool Validate() => Selection.activeObject is AnimationClip;

    [MenuItem("Assets/Extract Rotation-Only Clip")]
    static void Extract()
    {
        var src = (AnimationClip)Selection.activeObject;
        var dst = new AnimationClip();
        dst.frameRate = src.frameRate;

        int kept = 0, dropped = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            // curves on the root object node itself (path has no '/') are Blender's
            // coordinate-conversion baggage - always drop them
            bool isRootNode = !b.path.Contains("/");
            bool isRotation = b.propertyName.StartsWith("m_LocalRotation")
                           || b.propertyName.StartsWith("localEulerAngles");

            if (!isRotation || isRootNode) { dropped++; continue; }

            var binding = b;
            // strip Blender's "Armature/" wrapper so paths match the original model
            if (binding.path.StartsWith("Armature/"))
                binding.path = binding.path.Substring("Armature/".Length);

            AnimationUtility.SetEditorCurve(dst, binding,
                AnimationUtility.GetEditorCurve(src, b));
            kept++;
        }

        var settings = AnimationUtility.GetAnimationClipSettings(dst);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(dst, settings);

        string path = AssetDatabase.GenerateUniqueAssetPath(
            "Assets/" + src.name + "_Clean.anim");
        AssetDatabase.CreateAsset(dst, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created {path}  (kept {kept} rotation curves, dropped {dropped})");
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
    }
}
