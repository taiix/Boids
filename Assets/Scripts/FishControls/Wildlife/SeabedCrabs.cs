using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Crabs on the seabed (<see cref="CrabWalker"/>): little groups of them dotted about the arena, on ground
    /// gentle enough to walk on. Scenery only - every machine places its own, from the same seed, so everyone
    /// starts with them in the same spots (they don't stay in step after that; nothing about them is networked).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SeabedCrabs : MonoBehaviour
    {
        [SerializeField] GameObject crabPrefab;
        [Tooltip("How many groups of crabs.")]
        [Min(0)][SerializeField] int groups = 8;
        [Tooltip("Crabs in each group (min - max).")]
        [SerializeField] Vector2Int perGroup = new Vector2Int(2, 4);
        [Tooltip("A group's crabs start within this many metres of each other.")]
        [SerializeField] float groupSpread = 3f;
        [Tooltip("How big they are (1 = the model's own size, about half a metre across; min - max).")]
        [SerializeField] Vector2 size = new Vector2(0.9f, 1.5f);
        [Tooltip("Only where the ground is this gentle (degrees).")]
        [SerializeField] float maxSlope = 25f;
        [Tooltip("Keep this far in from the arena's walls (metres).")]
        [SerializeField] float wallMargin = 8f;
        [SerializeField] int seed = 20261016;

        readonly List<GameObject> _crabs = new List<GameObject>();
        float _seabedSeenAt = -1f;
        bool _placed;

        void Update()
        {
            if (_placed || crabPrefab == null) return;
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain == null || !terrain.TryGetComponent(out MeshCollider seabed) || seabed.sharedMesh == null) return;
            // Wait for the seabed to settle (it's shaped when the round loads).
            if (_seabedSeenAt < 0f) _seabedSeenAt = Time.time;
            if (Time.time - _seabedSeenAt < 2f) return;
            _placed = true;
            Place(seabed.bounds);
        }

        void Place(Bounds area)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var walls = ArenaWalls.Instance;
            int made = 0;
            for (int g = 0, tries = 0; g < groups && tries < groups * 40; tries++)
            {
                var centre = new Vector3(Mathf.Lerp(area.min.x, area.max.x, R()), area.max.y, Mathf.Lerp(area.min.z, area.max.z, R()));
                if (!Gentle(centre, out _) || (walls != null && walls.Count > 0 && !walls.Inside(centre, wallMargin))) continue;
                g++;
                int count = Mathf.RoundToInt(Mathf.Lerp(perGroup.x, perGroup.y, R()));
                for (int i = 0, t = 0; i < count && t < count * 10; t++)
                {
                    float a = R() * Mathf.PI * 2f, d = Mathf.Sqrt(R()) * groupSpread;
                    var at = centre + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    if (!Gentle(at, out Vector3 ground)) continue;
                    var crab = Instantiate(crabPrefab, ground, Quaternion.Euler(0f, R() * 360f, 0f), transform);
                    if (crab.TryGetComponent(out CrabWalker walker)) walker.Init(Mathf.Lerp(size.x, size.y, R()));
                    _crabs.Add(crab);
                    made++;
                    i++;
                }
            }
            Debug.Log($"[Crabs] {made} crabs in {groups} groups.");
        }

        bool Gentle(Vector3 at, out Vector3 ground)
        {
            bool found = WaterFx.SeabedAt(at, out ground, out Vector3 normal);
            return found && Vector3.Angle(normal, Vector3.up) <= maxSlope;
        }
    }
}
