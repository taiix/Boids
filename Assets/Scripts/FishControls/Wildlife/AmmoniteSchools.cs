using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Little schools of ammonites wandering the reef, for ambience (<see cref="AmmoniteSwimmer"/>). Each
    /// school follows a point that roams the arena in mid-water - over the reefs as often as not - now and then
    /// slowing to a drift; scare one and the school makes off the other way.
    ///
    /// Local to each machine, like the crabs (they start in the same places from the terrain's seed, then go
    /// their own ways): nothing about them matters to the game.
    /// </summary>
    public class AmmoniteSchools : MonoBehaviour
    {
        /// <summary>One school: where it's heading, and who's in it.</summary>
        public class School
        {
            public Vector3 Anchor;
            public bool Drifting;
            public readonly List<AmmoniteSwimmer> Members = new List<AmmoniteSwimmer>();
            internal Vector3 goal;
            internal float nextGoal, driftUntil, nextDrift, fleeUntil;

            /// <summary>Something frightening at <paramref name="threat"/>: the school makes off the other way.</summary>
            public void Scare(Vector3 threat)
            {
                if (Time.time < fleeUntil) return;
                Vector3 away = Anchor - threat;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-3f) away = Random.insideUnitSphere;
                goal = Anchor + away.normalized * 25f;
                fleeUntil = Time.time + 4f;
                nextGoal = fleeUntil + 2f;
                Drifting = false;
                foreach (var m in Members)
                    if (m != null) m.Scare(m.transform.position - threat);
            }
        }

        [SerializeField] GameObject prefab;
        [Tooltip("How many schools.")]
        [SerializeField] int schools = 10;
        [Tooltip("Ammonites in a school (min, max).")]
        [SerializeField] Vector2Int perSchool = new Vector2Int(2, 5);
        [Tooltip("Their size, times the prefab's (1 = about 0.4 m nose to tail).")]
        [SerializeField] Vector2 scale = new Vector2(2.4f, 3.3f);
        [Tooltip("How high over the seabed a school roams, in metres (min, max).")]
        [SerializeField] Vector2 height = new Vector2(3f, 14f);
        [Tooltip("How fast a school's wandering point moves (m/s): the ammonites keep up with it.")]
        [SerializeField] float roamSpeed = 0.9f;
        [Tooltip("Stay this far inside the arena's walls / the seabed's edges, in metres.")]
        [SerializeField] float edgeMargin = 12f;

        readonly List<School> _schools = new List<School>();
        readonly List<GameObject> _spawned = new List<GameObject>();
        Bounds _area;
        bool _built;

        void Update()
        {
            if (!_built)
            {
                var terrain = FindAnyObjectByType<TestTerrain>();
                if (terrain == null || !terrain.TryGetComponent(out MeshCollider seabed) || seabed.sharedMesh == null) return;
                Build(terrain, seabed);
            }
            float dt = Time.deltaTime;
            foreach (var s in _schools) Roam(s, dt);
        }

        void Build(TestTerrain terrain, MeshCollider seabed)
        {
            _built = true;
            if (prefab == null) return;
            _area = seabed.bounds;
            var rng = new System.Random(unchecked(terrain.settings.seed * 2654435 + 40503));
            for (int i = 0; i < schools; i++)
            {
                Vector3 at = default;
                bool found = false;
                for (int tries = 0; tries < 40 && !found; tries++)
                    found = Spot(rng, out at);
                if (!found) continue;
                var school = new School { Anchor = at, goal = at };
                int count = rng.Next(perSchool.x, perSchool.y + 1);
                float size = Mathf.Lerp(scale.x, scale.y, (float)rng.NextDouble());
                float yaw = (float)rng.NextDouble() * 360f;
                for (int n = 0; n < count; n++)
                {
                    Vector3 p = at + new Vector3((float)rng.NextDouble() * 4f - 2f, (float)rng.NextDouble() * 1.5f - 0.75f, (float)rng.NextDouble() * 4f - 2f);
                    var go = Instantiate(prefab, p, Quaternion.Euler(0f, yaw + (float)rng.NextDouble() * 30f - 15f, 0f), transform);
                    go.name = prefab.name;
                    go.transform.localScale = prefab.transform.localScale * size * Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());
                    _spawned.Add(go);
                    if (go.TryGetComponent(out AmmoniteSwimmer swimmer))
                    {
                        swimmer.School = school;
                        school.Members.Add(swimmer);
                    }
                }
                _schools.Add(school);
            }
            Debug.Log($"[Ammonites] {_spawned.Count} ammonites in {_schools.Count} schools.");
        }

        // A spot in open water: inside the arena, at a school's height over the seabed and the reef.
        bool Spot(System.Random rng, out Vector3 p)
        {
            float x = Mathf.Lerp(_area.min.x + edgeMargin, _area.max.x - edgeMargin, (float)rng.NextDouble());
            float z = Mathf.Lerp(_area.min.z + edgeMargin, _area.max.z - edgeMargin, (float)rng.NextDouble());
            p = new Vector3(x, 0f, z);
            if (!SceneryIndex.FloorAt(p, 1f, 0f, 0f, out float floor)) return false;
            p.y = floor + Mathf.Lerp(height.x, height.y, (float)rng.NextDouble());
            p.y = Mathf.Min(p.y, WaterFx.SurfaceY - 4f);
            var walls = ArenaWalls.Instance;
            return walls == null || walls.Inside(p, edgeMargin);
        }

        void Roam(School s, float dt)
        {
            s.Members.RemoveAll(m => m == null);
            if (s.Members.Count == 0) return;

            // Now and then the school slows to a drift for a while.
            if (Time.time >= s.nextDrift)
            {
                s.Drifting = Random.value < 0.35f;
                s.driftUntil = Time.time + Random.Range(5f, 12f);
                s.nextDrift = s.driftUntil;
            }

            bool fleeing = Time.time < s.fleeUntil;
            if (!fleeing && (Time.time >= s.nextGoal || (s.goal - s.Anchor).sqrMagnitude < 4f))
            {
                // Somewhere else not too far off: wandering, not crossing the map in one go.
                var rng = new System.Random(Random.Range(int.MinValue, int.MaxValue));
                for (int tries = 0; tries < 10; tries++)
                    if (Spot(rng, out var p) && (p - s.Anchor).sqrMagnitude < 60f * 60f) { s.goal = p; break; }
                s.nextGoal = Time.time + Random.Range(20f, 40f);
            }

            // The point moves on ahead of the school; it waits for them if they've fallen behind.
            Vector3 middle = Vector3.zero;
            foreach (var m in s.Members) middle += m.transform.position;
            middle /= s.Members.Count;
            float lead = (s.Anchor - middle).magnitude;
            float speed = (fleeing ? roamSpeed * 2.5f : s.Drifting ? roamSpeed * 0.25f : roamSpeed) * (lead > 6f ? 0.2f : 1f);
            s.Anchor = Vector3.MoveTowards(s.Anchor, s.goal, speed * dt);
            // Keep it in the water: over the reef, under the surface, inside the walls.
            if (SceneryIndex.FloorAt(s.Anchor, 1f, height.x, height.x * 0.7f, out float floor) && s.Anchor.y < floor) s.Anchor.y = floor;
            s.Anchor.y = Mathf.Min(s.Anchor.y, WaterFx.SurfaceY - 4f);
            var walls = ArenaWalls.Instance;
            if (walls != null && !walls.Inside(s.Anchor, edgeMargin * 0.5f))
            {
                s.goal = Vector3.Lerp(s.Anchor, new Vector3(_area.center.x, s.Anchor.y, _area.center.z), 0.3f);
                s.fleeUntil = 0f;
            }
        }

        void OnDestroy()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
        }
    }
}
