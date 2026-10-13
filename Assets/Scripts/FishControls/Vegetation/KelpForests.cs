using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace FishGame
{
    /// <summary>
    /// Grows small forests on the seabed: clumps of plants, densest and tallest in the middle - cover for
    /// prey to hide in. Set up with the rigged seaweed (SeaweedA/B/C), which bends out of a swimmer's way
    /// through its bones (<see cref="SeaweedBoneSpring"/>).
    ///
    /// Placed from the terrain's seed once the seabed is final (<see cref="SeededVegetation"/> calls
    /// <see cref="RebuildAll"/> right after the rest of the reef), so every player gets the same forests in
    /// the same places. No colliders: you swim through, pushing the plants aside.
    /// </summary>
    public class KelpForests : MonoBehaviour
    {
        [Header("What grows")]
        [Tooltip("The forest's plants. One is picked at random for each spot. Rigged seaweed costs more " +
                 "than a static plant (skinned meshes + an Animator each), so size the forests with that in mind.")]
        [FormerlySerializedAs("kelpPrefabs")]
        [SerializeField] GameObject[] plantPrefabs;
        [Tooltip("Optional low plants scattered around each forest's feet.")]
        [SerializeField] GameObject[] undergrowthPrefabs;
        [Tooltip("Undergrowth plants per forest.")]
        [SerializeField] int undergrowthPerForest = 0;

        [Header("Forests")]
        [SerializeField] int forestCount = 10;
        [Tooltip("Plants per forest (min, max).")]
        [FormerlySerializedAs("kelpPerForest")]
        [SerializeField] Vector2Int plantsPerForest = new Vector2Int(8, 14);
        [Tooltip("Radius of a forest in metres (min, max).")]
        [SerializeField] Vector2 forestRadius = new Vector2(4f, 7f);
        [Tooltip("Keep forests at least this far apart, centre to centre, in metres.")]
        [SerializeField] float forestSpacing = 30f;
        [Tooltip("Keep plants at least this far apart, in metres.")]
        [FormerlySerializedAs("kelpSpacing")]
        [SerializeField] float plantSpacing = 1.6f;
        [Tooltip("Scale of a plant at the edge of a forest / at its middle (times the prefab's own scale).")]
        [FormerlySerializedAs("kelpScale")]
        [SerializeField] Vector2 plantScale = new Vector2(0.8f, 1.1f);

        [Header("Where")]
        [Tooltip("Only on ground at least this flat (1 = level, 0.7 is about a 45° slope).")]
        [Range(0f, 1f)][SerializeField] float minFlatness = 0.75f;
        [Tooltip("Stay this far inside the seabed's edges, in metres.")]
        [SerializeField] float edgeMargin = 15f;
        [Tooltip("Keep forests this far from the reef task and the spawn points, so those stay in the open.")]
        [SerializeField] float keepClearRadius = 14f;

        static readonly List<KelpForests> s_all = new List<KelpForests>();
        readonly List<GameObject> _spawned = new List<GameObject>();
        readonly List<Vector4> _forests = new List<Vector4>(); // centre (xyz) + radius (w) of each forest

        void OnEnable() => s_all.Add(this);
        void OnDisable() => s_all.Remove(this);

        /// <summary>Re-grow every forest on the current seabed (the reef does the same).</summary>
        public static void RebuildAll()
        {
            foreach (var f in s_all)
                if (f != null) f.Rebuild();
        }

        /// <summary>Is <paramref name="p"/> inside a forest, or within <paramref name="margin"/> metres of
        /// one (measured across the seabed)? For keeping other things out of them.</summary>
        public static bool NearAnyForest(Vector3 p, float margin)
        {
            foreach (var f in s_all)
            {
                if (f == null) continue;
                foreach (var c in f._forests)
                {
                    float dx = c.x - p.x, dz = c.z - p.z, r = c.w + margin;
                    if (dx * dx + dz * dz < r * r) return true;
                }
            }
            return false;
        }

        public void Rebuild()
        {
            Clear();
            if (plantPrefabs == null || plantPrefabs.Length == 0) return;
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain == null || !terrain.TryGetComponent(out MeshCollider seabed) || seabed.sharedMesh == null) return;

            // Our own stream from the terrain's seed: the same forests for everyone, and untouched by
            // whatever else uses UnityEngine.Random.
            var rng = new System.Random(unchecked(terrain.settings.seed * 73856093 + 19349663));
            Bounds area = seabed.bounds;
            var keepClear = KeepClearPoints();

            var centres = new List<Vector3>();
            for (int tries = 0; centres.Count < forestCount && tries < forestCount * 40; tries++)
            {
                float x = Mathf.Lerp(area.min.x + edgeMargin, area.max.x - edgeMargin, (float)rng.NextDouble());
                float z = Mathf.Lerp(area.min.z + edgeMargin, area.max.z - edgeMargin, (float)rng.NextDouble());
                if (!Ground(seabed, x, z, out var hit)) continue;
                if (TooClose(centres, hit.point, forestSpacing) || TooClose(keepClear, hit.point, keepClearRadius)) continue;
                centres.Add(hit.point);
            }

            int plants = 0;
            for (int i = 0; i < centres.Count; i++)
                plants += Grow(seabed, centres[i], i, rng);
            Debug.Log($"[KelpForests] Grew {centres.Count} forests ({plants} plants).");
        }

        int Grow(MeshCollider seabed, Vector3 centre, int index, System.Random rng)
        {
            float radius = Mathf.Lerp(forestRadius.x, forestRadius.y, (float)rng.NextDouble());
            int want = rng.Next(plantsPerForest.x, plantsPerForest.y + 1);
            var placed = new List<Vector3>();
            var parent = new GameObject($"Forest {index + 1}").transform;
            parent.SetParent(transform, false);
            parent.position = centre;
            _spawned.Add(parent.gameObject);
            _forests.Add(new Vector4(centre.x, centre.y, centre.z, radius));

            for (int tries = 0; placed.Count < want && tries < want * 10; tries++)
            {
                // Picking the distance uniformly (not by area) crowds plants toward the middle.
                float r01 = (float)rng.NextDouble();
                float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float x = centre.x + Mathf.Cos(a) * radius * r01;
                float z = centre.z + Mathf.Sin(a) * radius * r01;
                if (!Ground(seabed, x, z, out var hit) || TooClose(placed, hit.point, plantSpacing)) continue;

                var prefab = plantPrefabs[rng.Next(plantPrefabs.Length)];
                // Tallest in the middle, tapering toward the edge, with a little variety.
                float scale = Mathf.Lerp(plantScale.y, plantScale.x, r01) * Mathf.Lerp(0.9f, 1.1f, (float)rng.NextDouble());
                Spawn(prefab, hit.point, scale, parent, rng);
                placed.Add(hit.point);
            }

            if (undergrowthPrefabs != null && undergrowthPrefabs.Length > 0)
                for (int i = 0; i < undergrowthPerForest; i++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
                    float r = radius * Mathf.Lerp(0.3f, 1.15f, (float)rng.NextDouble());
                    if (!Ground(seabed, centre.x + Mathf.Cos(a) * r, centre.z + Mathf.Sin(a) * r, out var hit)) continue;
                    var prefab = undergrowthPrefabs[rng.Next(undergrowthPrefabs.Length)];
                    Spawn(prefab, hit.point, Mathf.Lerp(0.8f, 1.2f, (float)rng.NextDouble()), parent, rng);
                }

            return placed.Count;
        }

        void Spawn(GameObject prefab, Vector3 ground, float scale, Transform parent, System.Random rng)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, ground, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), parent);
            go.name = prefab.name;
            go.transform.localScale = prefab.transform.localScale * scale;
            SeatOnGround(go, ground.y);

            // Each plant sways at its own pace and from its own point in the clip, so a forest doesn't
            // move in lockstep.
            foreach (var animator in go.GetComponentsInChildren<Animator>())
            {
                animator.speed = Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());
                animator.Update((float)rng.NextDouble() * 4f);
            }
        }

        // Stand a plant on the ground by its lowest point (pivots aren't always at the base), sunk a
        // touch so it looks rooted.
        static void SeatOnGround(GameObject go, float groundY)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            float bottom = float.MaxValue;
            foreach (var r in renderers) bottom = Mathf.Min(bottom, r.bounds.min.y);
            go.transform.position += Vector3.up * (groundY - 0.1f - bottom);
        }

        // The seabed under (x, z), if it's flat enough to grow on.
        bool Ground(MeshCollider seabed, float x, float z, out RaycastHit hit)
        {
            Bounds b = seabed.bounds;
            var ray = new Ray(new Vector3(x, b.max.y + 5f, z), Vector3.down);
            return seabed.Raycast(ray, out hit, b.size.y + 10f) && hit.normal.y >= minFlatness;
        }

        static bool TooClose(List<Vector3> points, Vector3 p, float distance)
        {
            float d2 = distance * distance;
            foreach (var q in points)
            {
                float dx = q.x - p.x, dz = q.z - p.z;
                if (dx * dx + dz * dz < d2) return true;
            }
            return false;
        }

        /// <summary>Places that should stay open: the reef task(s) and where players spawn. Scene network
        /// objects are inactive until the server spawns them, so this includes inactive ones.</summary>
        public static List<Vector3> KeepClearPoints()
        {
            var points = new List<Vector3>();
            foreach (var t in FindObjectsByType<NetworkedSequenceTask>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                points.Add(t.transform.position);
            foreach (var s in FindObjectsByType<Mirror.NetworkStartPosition>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                points.Add(s.transform.position);
            points.AddRange(RoleSpawnZone.Centres());
            return points;
        }

        void Clear()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
            _forests.Clear();
        }
    }
}
