using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FishGame.EditorTools
{
    /// <summary>
    /// FishGame > Set Up Coral Reefs: puts a <see cref="CoralReefs"/> in the open game scene (or updates the one
    /// there) and fills in what its reefs are made of - the Coral Reef Decor Pack's corals, sorted into kinds
    /// by where on a reef they grow, its rocks as the reef's rock, and the deluxe pack's tube corals - then
    /// saves the scene. Its reef settings (how many, how big, how dense) are kept if it's there already.
    /// </summary>
    public static class CoralReefSetup
    {
        const string Decor = "Assets/Coral Reef Decor Pack/Coral Reef Decor Pack - HDRP/Assets/Prefabs/";
        const string Deluxe = "Assets/Underwater life deluxe/Prefabs/Vegetation/";

        [MenuItem("FishGame/Set Up Coral Reefs")]
        public static void SetUp()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (FindVegetation(scene) == null)
            {
                Debug.LogError($"[CoralReefs] '{scene.name}' has no seabed scatter (ProceduralVegetation) - open the game scene first.");
                return;
            }

            CoralReefs reefs = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out reefs)) break;
            if (reefs == null)
            {
                var go = new GameObject("CoralReefs");   // into the active scene
                reefs = go.AddComponent<CoralReefs>();
            }

            var rocks = Load(Decor, "SM_Seabed_019_a", "SM_Seabed_019_b", "SM_Seabed_020_a", "SM_Seabed_020_b",
                             "SM_Seabed_021_b", "SM_Seabed_022_a", "SM_Seabed_022_b", "SM_Seabed_028_a", "SM_Seabed_028_b");
            var kinds = new List<CoralReefs.Kind>
            {
                // The heart: the big colonies, on and around the rock.
                Kind("Brain coral", Load(Decor, "SM_Seabed_004_a", "SM_Seabed_004_b"), 0.9f, 1.7f, 1, 3, 0.35f, 1f, 1.2f, spacing: 0.75f),
                Kind("Table coral", Load(Decor, "SM_Seabed_005_a", "SM_Seabed_005_b"), 0.6f, 1.1f, 1, 2, 0.45f, 1f, 0.9f, spacing: 0.55f),
                Kind("Cauliflower coral (pink)", Load(Decor, "SM_Seabed_009_a"), 0.8f, 1.5f, 3, 7, 0.2f, 1f, 1.1f),
                Kind("Cauliflower coral (yellow)", Load(Decor, "SM_Seabed_009_b"), 0.8f, 1.5f, 3, 7, 0.2f, 1f, 1.1f),
                Kind("Plate coral", Load(Decor, "SM_Seabed_010_a", "SM_Seabed_010_b"), 0.8f, 1.3f, 2, 4, 0.3f, 0.95f, 0.8f, spacing: 0.6f),
                Kind("Tube sponge", Load(Decor, "SM_Seabed_017_a", "SM_Seabed_017_b"), 0.8f, 1.3f, 1, 3, 0.3f, 1f, 0.8f),
                Kind("Leaf coral", Load(Decor, "SM_Seabed_001"), 0.45f, 0.8f, 1, 2, 0.5f, 1f, 0.5f, spacing: 0.6f),
                // Further out: branching corals in thickets, and the sea fans.
                Kind("Finger coral", Load(Decor, "SM_Seabed_018_a", "SM_Seabed_018_b"), 0.9f, 1.5f, 2, 5, 0.2f, 0.9f, 1.2f, spacing: 0.6f),
                Kind("Purple branching coral", Load(Decor, "SM_Seabed_003"), 0.8f, 1.4f, 2, 5, 0.25f, 1f, 1f, spacing: 0.6f),
                Kind("Red branching coral", Load(Decor, "SM_Seabed_006", "SM_Seabed_024"), 0.8f, 1.4f, 2, 5, 0.25f, 1f, 1f, spacing: 0.6f),
                Kind("Pillar coral", Load(Decor, "SM_Seabed_008"), 0.8f, 1.3f, 2, 4, 0.3f, 1f, 0.7f, spacing: 0.6f),
                Kind("Sea whip", Load(Decor, "SM_Seabed_030"), 0.7f, 1.1f, 1, 3, 0.25f, 0.9f, 0.6f),
                Kind("Polyp coral", Load(Decor, "SM_Seabed_015", "SM_Seabed_016_a", "SM_Seabed_016_b"), 0.8f, 1.3f, 2, 4, 0.2f, 0.8f, 0.8f, spacing: 0.6f),
                Kind("Red sea fan", Load(Decor, "SM_Seabed_012"), 0.8f, 1.3f, 1, 3, 0.3f, 1f, 0.6f, faces: true, spacing: 0.5f),
                Kind("Orange sea fan", Load(Decor, "SM_Seabed_013"), 0.8f, 1.3f, 1, 3, 0.3f, 1f, 0.6f, faces: true, spacing: 0.5f),
                Kind("Yellow sea fan", Load(Decor, "SM_Seabed_014"), 0.8f, 1.3f, 1, 3, 0.3f, 1f, 0.6f, faces: true, spacing: 0.5f),
                Kind("Tube coral", Load(Deluxe, "Coral_v2_prefab", "Coral_v2_1_prefab", "Coral_v2_2_prefab", "Coral_v3_prefab"), 2.6f, 4f, 2, 5, 0.15f, 0.8f, 0.8f, spacing: 0.6f),
                // The fringe, thinning into the sand.
                Kind("Anemones", Load(Decor, "SM_Seabed_007"), 0.7f, 1.2f, 2, 6, 0f, 0.6f, 1f, spacing: 0.6f),
                Kind("Rubble", Load(Decor, "SM_Seabed_019_a", "SM_Seabed_021_a", "SM_Seabed_021_b", "SM_Seabed_022_a", "SM_Seabed_011"), 0.4f, 0.9f, 2, 5, 0f, 0.35f, 1f, onRock: false),
                Kind("Shells", Load(Decor, "SM_Seabed_025", "SM_Seabed_026"), 0.8f, 1.2f, 1, 2, 0f, 0.4f, 0.4f, onRock: false),
                Kind("Starfish", Load(Decor, "SM_Seabed_027"), 0.8f, 1.3f, 1, 2, 0f, 0.7f, 0.4f),
            };
            reefs.SetPalette(rocks, kinds.ToArray());
            EditorUtility.SetDirty(reefs);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[CoralReefs] Set up in '{scene.name}': {rocks.Length} rocks, {kinds.Count} kinds of coral.");
        }

        static CoralReefs.Kind Kind(string name, GameObject[] prefabs, float minScale, float maxScale, int minColony, int maxColony,
                                    float zoneFrom, float zoneTo, float weight, bool faces = false, bool onRock = true, float spacing = 0.7f) =>
            new CoralReefs.Kind
            {
                name = name, prefabs = prefabs, scale = new Vector2(minScale, maxScale), colony = new Vector2Int(minColony, maxColony),
                zone = new Vector2(zoneFrom, zoneTo), weight = weight, facesCurrent = faces, onRock = onRock, spacing = spacing,
            };

        static GameObject[] Load(string folder, params string[] names)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(folder + n + ".prefab");
                if (go != null) list.Add(go);
                else Debug.LogWarning($"[CoralReefs] No prefab {folder}{n}.prefab");
            }
            return list.ToArray();
        }

        static CodenameLib.ProceduralTerrain.ProceduralVegetation FindVegetation(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var veg = root.GetComponentInChildren<CodenameLib.ProceduralTerrain.ProceduralVegetation>(true);
                if (veg != null) return veg;
            }
            return null;
        }
    }
}
