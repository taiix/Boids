#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReefRun.EditorTools
{
    /// <summary>
    /// After dropping the ReefRun folder into Assets/, run:
    ///   Tools > Reef Run > Build Lobby Scene
    /// Creates a PanelSettings + theme, makes a scene, and wires a GameObject
    /// with UIDocument + ReefRunLobbyController. Then press Play.
    /// </summary>
    public static class ReefRunSetup
    {
        [MenuItem("Tools/Reef Run/Build Main Menu Scene")]
        public static void BuildMainMenu()
        {
            string[] guids = AssetDatabase.FindAssets("ReefRunMainMenu t:VisualTreeAsset");
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Reef Run",
                    "Couldn't find ReefRunMainMenu.uxml. Make sure the whole ReefRun folder is inside Assets/.", "OK");
                return;
            }

            string uxmlPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            string folder   = Path.GetDirectoryName(uxmlPath).Replace("\\", "/");
            var    uxml     = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);

            // shared theme (same as lobby)
            string themePath = folder + "/ReefRunTheme.tss";
            if (!File.Exists(themePath))
            {
                File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);

            // shared panel settings
            string panelPath = folder + "/ReefRunPanelSettings.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, panelPath);
            }
            panel.themeStyleSheet     = theme;
            panel.scaleMode           = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            panel.screenMatchMode     = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match               = 0.5f;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();

            // scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go    = new GameObject("ReefRunMainMenu");
            var doc   = go.AddComponent<UIDocument>();
            doc.panelSettings  = panel;
            doc.visualTreeAsset = uxml;
            go.AddComponent<ReefRunMainMenuController>();
            Selection.activeGameObject = go;

            string scenePath = folder + "/ReefRunMainMenu.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log($"<color=#3FE0C5>Reef Run Main Menu ready.</color> Scene saved to {scenePath} — press Play.");
            EditorUtility.DisplayDialog("Reef Run",
                "Done! The scene 'ReefRunMainMenu' is open and wired up.\n\nPress Play to see the menu.", "Dive in");
        }

        [MenuItem("Tools/Reef Run/Build Lobby Scene")]
        public static void Build()
        {
            string[] guids = AssetDatabase.FindAssets("ReefRunLobby t:VisualTreeAsset");
            if (guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Reef Run",
                    "Couldn't find ReefRunLobby.uxml. Make sure the whole ReefRun folder is inside Assets/.", "OK");
                return;
            }

            string uxmlPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            string folder = Path.GetDirectoryName(uxmlPath).Replace("\\", "/");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);

            // theme (self-contained, imports Unity's default runtime theme)
            string themePath = folder + "/ReefRunTheme.tss";
            if (!File.Exists(themePath))
            {
                File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);

            // panel settings
            string panelPath = folder + "/ReefRunPanelSettings.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, panelPath);
            }
            panel.themeStyleSheet     = theme;
            panel.scaleMode           = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1280, 720);
            panel.screenMatchMode     = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match               = 0.5f;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();

            // scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("ReefRunLobby");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = uxml;
            go.AddComponent<ReefRunLobbyController>();
            Selection.activeGameObject = go;

            string scenePath = folder + "/ReefRunLobby.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            Debug.Log($"<color=#3FE0C5>Reef Run ready.</color> Scene saved to {scenePath} — press Play.");
            EditorUtility.DisplayDialog("Reef Run",
                "Done! The scene 'ReefRunLobby' is open and wired up.\n\nPress Play to see the lobby.", "Dive in");
        }
    }
}
#endif
