using System.Collections.Generic;
using UnityEngine;
using CodenameLib.ProceduralTerrain;

public class ProceduralVegetation : MonoBehaviour
{
    [System.Serializable]
    public struct Vegetation
    {
        public int numberOfObjectsToSpawn;
        public GameObject prefab;
        [Range(0f, 1f)] public float minHeight; // normalized terrain height (0 = lowest, 1 = highest)
        [Range(0f, 1f)] public float maxHeight;
        public float radius;                    // minimum world-space spacing between objects
        public int spacing;                     // grid stride when sampling candidate vertices (>=1)
        public float objectHeight;              // Y localScale applied to the spawned prefab
    }

    [Header("References")]
    [Tooltip("Terrain to populate. Auto-found in the scene if left empty.")]
    [SerializeField] private TestTerrain terrain;

    [Header("Vegetation")]
    [SerializeField] private List<GameObject> customAreaObjects;
    [SerializeField] private List<Vegetation> vegetations = new();
    [SerializeField] private List<GameObject> spawnedObjects = new();

    [Header("Folder Scatter (whole terrain)")]
    [Tooltip("Scatter every prefab from the folder below across the entire terrain on generation.")]
    [SerializeField] private bool scatterFolderPrefabs = true;
    [Tooltip("Project-relative folder. Use the 'Load Folder Prefabs' context menu to bake the references in (works in builds).")]
    [SerializeField] private string prefabFolder = "Assets/Coral Reef Decor Pack/Coral Reef Decor Pack - HDRP/Assets/Prefabs";
    [SerializeField] private List<GameObject> folderPrefabs = new();
    [Tooltip("Maximum prefabs to scatter. 0 = as many as the spacing allows.")]
    [SerializeField] private int folderScatterCount = 1500;
    [Tooltip("Minimum world-space distance between scattered prefabs.")]
    [SerializeField] private float folderScatterRadius = 1.5f;
    [Tooltip("Grid stride when sampling candidate vertices (>=1). Higher = sparser/cheaper.")]
    [SerializeField] private int folderScatterSpacing = 1;
    [SerializeField] private Vector2 folderScaleRange = new(0.8f, 1.2f);
    [SerializeField] private bool folderRandomYaw = true;

    private void OnEnable()
    {
        TestTerrain.OnCreatingDone.AddListener(Init);
    }

    private void OnDisable()
    {
        TestTerrain.OnCreatingDone.RemoveListener(Init);
    }

    public void Init()
    {
        if (terrain == null)
            terrain = FindFirstObjectByType<TestTerrain>();

        if (terrain == null)
        {
            Debug.LogError("[Vegetation] No TestTerrain found in the scene.");
            return;
        }

        Mesh mesh = terrain.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
        if (mesh == null)
        {
            Debug.LogError("[Vegetation] Terrain has no generated mesh yet.");
            return;
        }

        ClearSpawned();

#if UNITY_EDITOR
        if (scatterFolderPrefabs && folderPrefabs.Count == 0)
            LoadPrefabsFromFolder();
#endif

        PopulateTreeObjects(mesh, terrain.transform);

        if (scatterFolderPrefabs)
            PopulateWholeTerrain(mesh, terrain.transform);
    }

    // Scatter every prefab from folderPrefabs uniformly across the whole terrain mesh,
    // picking a random prefab at each placement. No height band — covers land and seabed.
    public void PopulateWholeTerrain(Mesh mesh, Transform terrainTransform)
    {
        if (folderPrefabs == null || folderPrefabs.Count == 0)
        {
            Debug.LogWarning("[Vegetation] No folder prefabs to scatter. Run 'Load Folder Prefabs' first.");
            return;
        }

        Vector3[] verts = mesh.vertices;
        if (verts.Length == 0) return;

        int res = Mathf.RoundToInt(Mathf.Sqrt(verts.Length));
        int step = Mathf.Max(1, folderScatterSpacing);

        // Every (sub-sampled) vertex is a candidate ground point.
        List<Vector3> candidates = new();
        for (int z = 0; z < res; z += step)
            for (int x = 0; x < res; x += step)
                candidates.Add(terrainTransform.TransformPoint(verts[x + z * res]));

        Shuffle(candidates);

        List<Vector3> placed = new();
        int target = folderScatterCount > 0 ? folderScatterCount : candidates.Count;
        float radiusSqr = folderScatterRadius * folderScatterRadius;
        int spawned = 0;

        foreach (Vector3 worldPos in candidates)
        {
            if (spawned >= target) break;

            bool canPlace = true;
            foreach (Vector3 old in placed)
            {
                if ((old - worldPos).sqrMagnitude < radiusSqr)
                {
                    canPlace = false;
                    break;
                }
            }
            if (!canPlace) continue;

            GameObject prefab = folderPrefabs[Random.Range(0, folderPrefabs.Count)];
            if (prefab == null) continue;

            Quaternion rot = folderRandomYaw
                ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                : Quaternion.identity;

            GameObject go = Instantiate(prefab, worldPos, rot, transform);
            go.name = prefab.name;
            go.transform.localScale *= Random.Range(folderScaleRange.x, folderScaleRange.y);

            placed.Add(worldPos);
            spawnedObjects.Add(go);
            spawned++;
        }

        Debug.Log($"[Vegetation] Scattered {spawned} folder prefabs across the terrain.");
    }

#if UNITY_EDITOR
    // Bakes every prefab in prefabFolder into folderPrefabs so the references persist into play mode and builds.
    [ContextMenu("Load Folder Prefabs")]
    public void LoadPrefabsFromFolder()
    {
        folderPrefabs.Clear();

        if (!UnityEditor.AssetDatabase.IsValidFolder(prefabFolder))
        {
            Debug.LogError($"[Vegetation] Folder not found: {prefabFolder}");
            return;
        }

        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolder });
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            GameObject go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) folderPrefabs.Add(go);
        }

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[Vegetation] Loaded {folderPrefabs.Count} prefabs from {prefabFolder}");
    }
#endif

    // Build candidate ground points from the procedural mesh, then scatter prefabs over them.
    public void PopulateTreeObjects(Mesh mesh, Transform terrainTransform)
    {
        Vector3[] verts = mesh.vertices;
        if (verts.Length == 0) return;

        // Grid is square: res * res vertices laid out as index = x + z * res.
        int res = Mathf.RoundToInt(Mathf.Sqrt(verts.Length));

        // Normalize height against the actual mesh Y range so minHeight/maxHeight are 0..1.
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < verts.Length; i++)
        {
            if (verts[i].y < minY) minY = verts[i].y;
            if (verts[i].y > maxY) maxY = verts[i].y;
        }
        float range = maxY - minY;

        List<Vector3> placed = new();

        foreach (var vegetation in vegetations)
        {
            if (vegetation.prefab == null) continue;

            int step = Mathf.Max(1, vegetation.spacing);

            // Collect eligible candidate world positions within this type's height band.
            List<Vector3> candidates = new();
            for (int z = 0; z < res; z += step)
            {
                for (int x = 0; x < res; x += step)
                {
                    Vector3 local = verts[x + z * res];
                    float normalizedHeight = range > Mathf.Epsilon ? (local.y - minY) / range : 0f;

                    if (normalizedHeight < vegetation.minHeight || normalizedHeight > vegetation.maxHeight)
                        continue;

                    candidates.Add(terrainTransform.TransformPoint(local));
                }
            }

            Shuffle(candidates);

            int spawned = 0;
            foreach (Vector3 worldPos in candidates)
            {
                if (spawned >= vegetation.numberOfObjectsToSpawn) break;

                bool canPlace = true;
                foreach (Vector3 old in placed)
                {
                    if ((old - worldPos).sqrMagnitude < vegetation.radius * vegetation.radius)
                    {
                        canPlace = false;
                        break;
                    }
                }
                if (!canPlace) continue;

                GameObject go = Instantiate(vegetation.prefab, worldPos, Quaternion.identity, transform);
                go.name = vegetation.prefab.name;

                Vector3 scale = go.transform.localScale;
                go.transform.localScale = new Vector3(scale.x, vegetation.objectHeight, scale.z);

                placed.Add(worldPos);
                spawnedObjects.Add(go);
                spawned++;

                //if (go.TryGetComponent<Interactable>(out Interactable interactable))
                //{
                //    interactable.parentIsland = this;
                //    interactable.isSpawnedByIsland = true;
                //}
            }
        }
    }

    public void ClearSpawned()
    {
        for (int i = 0; i < spawnedObjects.Count; i++)
        {
            if (spawnedObjects[i] != null)
                Destroy(spawnedObjects[i]);
        }
        spawnedObjects.Clear();
    }

    public void RemoveObjects(GameObject go)
    {
        if (spawnedObjects.Contains(go))
        {
            Destroy(go);
            spawnedObjects.Remove(go);
            Debug.Log($"Removed object {go.name} from island {go.name}.");
        }
    }

    private static void Shuffle(List<Vector3> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public List<GameObject> GetSpawnedObjects()
    {
        return spawnedObjects;
    }

    public void AddToSpawnedObjects(GameObject go)
    {
        spawnedObjects.Add(go);
    }
}
