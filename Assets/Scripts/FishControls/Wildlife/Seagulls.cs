using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A flock of seagulls over the sea, for ambience (<see cref="Seagull"/>): some start afloat in little rafts,
    /// the rest in the air; each takes off, flies about and lands again on its own time. They keep to an area:
    /// the seabed's (in from its edges) when the scene has the game's terrain, else a circle round this object.
    /// A gull scared off the water startles the ones afloat near it, which follow it up a moment later.
    ///
    /// Local to each machine, like the crabs and ammonites (they start in the same places from the terrain's
    /// seed, then go their own ways): nothing about them matters to the game.
    /// </summary>
    public class Seagulls : MonoBehaviour
    {
        [SerializeField] GameObject prefab;
        [Tooltip("How many gulls.")]
        [SerializeField] int count = 12;
        [Tooltip("How many start afloat (the rest start in the air), 0-1.")]
        [Range(0f, 1f)]
        [SerializeField] float startAfloat = 0.6f;
        [Tooltip("Gulls afloat start in rafts of this many (min, max).")]
        [SerializeField] Vector2Int raft = new Vector2Int(2, 4);
        [Tooltip("Their size, times the prefab's (1 = about 0.9 m beak to tail, 1.5 m across the wings).")]
        [SerializeField] Vector2 scale = new Vector2(0.95f, 1.15f);
        [Tooltip("With no game terrain in the scene: how far from this object they roam, in metres.")]
        [SerializeField] float radius = 60f;
        [Tooltip("With the game terrain: keep this far in from the seabed's edges, in metres.")]
        [SerializeField] float edgeMargin = 15f;
        [Tooltip("In the arena: keep this far inside its walls, in metres (a gull's turns carry it on a bit).")]
        [SerializeField] float wallMargin = 18f;
        [Tooltip("Gulls afloat within this many metres of a scared one take off after it.")]
        [SerializeField] float startleRadius = 14f;

        readonly List<Seagull> _gulls = new List<Seagull>();
        Vector3 _centre;
        Vector2 _halfSize;   // the area: a box round _centre (the seabed's) or, if _round, a circle of radius
        bool _round, _built;

        public Vector3 Centre => _centre;
        public IReadOnlyList<Seagull> Gulls => _gulls;

        void Update()
        {
            if (_built) return;
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain != null)
            {
                // The game's seabed is built at runtime: wait for it.
                if (!terrain.TryGetComponent(out MeshCollider seabed) || seabed.sharedMesh == null) return;
                var b = seabed.bounds;
                _centre = new Vector3(b.center.x, 0f, b.center.z);
                _halfSize = new Vector2(Mathf.Max(5f, b.extents.x - edgeMargin), Mathf.Max(5f, b.extents.z - edgeMargin));
                _round = false;
                Build(unchecked(terrain.settings.seed * 7919 + 2027));
            }
            else
            {
                _centre = new Vector3(transform.position.x, 0f, transform.position.z);
                _halfSize = new Vector2(radius, radius);
                _round = true;
                Build(12345);
            }
        }

        void Build(int seed)
        {
            _built = true;
            if (prefab == null) return;
            var rng = new System.Random(seed);
            float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            float surface = WaterFx.SurfaceY;

            int afloat = Mathf.RoundToInt(count * startAfloat);
            int made = 0;
            // Rafts on the water.
            while (made < afloat)
            {
                if (!Spot(rng, 0.8f, out Vector3 at)) break;
                int n = Mathf.Min(afloat - made, rng.Next(raft.x, raft.y + 1));
                float yaw = R(0f, 360f);
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = at + new Vector3(R(-2.5f, 2.5f), 0f, R(-2.5f, 2.5f));
                    var gull = Spawn(p, yaw + R(-50f, 50f), R(scale.x, scale.y));
                    gull?.StartFloating();
                    made++;
                }
            }
            // The rest in the air.
            for (; made < count; made++)
            {
                if (!Spot(rng, 0.9f, out Vector3 at)) break;
                at.y = surface + R(6f, 16f);
                var gull = Spawn(at, R(0f, 360f), R(scale.x, scale.y));
                gull?.StartFlying();
            }
            Debug.Log($"[Seagulls] {_gulls.Count} gulls.");
        }

        Seagull Spawn(Vector3 at, float yaw, float size)
        {
            var go = Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), transform);
            go.name = prefab.name;
            go.transform.localScale = prefab.transform.localScale * size;
            if (!go.TryGetComponent(out Seagull gull)) return null;
            gull.Flock = this;
            _gulls.Add(gull);
            return gull;
        }

        // Open water somewhere in the area, on the surface.
        bool Spot(System.Random rng, float inset, out Vector3 p)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                p = _centre + new Vector3(((float)rng.NextDouble() * 2f - 1f) * _halfSize.x * inset, 0f,
                                          ((float)rng.NextDouble() * 2f - 1f) * _halfSize.y * inset);
                p.y = WaterFx.SurfaceY;
                if (InArea(p, inset) && OpenWater(p)) return true;
            }
            p = default;
            return false;
        }

        /// <summary>Is <paramref name="p"/> inside the area (shrunk to <paramref name="inset"/> of its size) - and,
        /// in the arena, well inside its walls (they stand above the sea too)?</summary>
        public bool InArea(Vector3 p, float inset = 1f)
        {
            float dx = (p.x - _centre.x) / Mathf.Max(1f, _halfSize.x * inset);
            float dz = (p.z - _centre.z) / Mathf.Max(1f, _halfSize.y * inset);
            bool inside = _round ? dx * dx + dz * dz <= 1f : Mathf.Abs(dx) <= 1f && Mathf.Abs(dz) <= 1f;
            var walls = ArenaWalls.Instance;
            return inside && (walls == null || walls.Inside(new Vector3(p.x, WaterFx.SurfaceY, p.z), wallMargin));
        }

        /// <summary>A point in the area, somewhere to fly to (at sea level; the gull picks its height).</summary>
        public Vector3 RandomPoint()
        {
            for (int tries = 0; tries < 10; tries++)
            {
                var p = _centre + new Vector3(Random.Range(-_halfSize.x, _halfSize.x), WaterFx.SurfaceY, Random.Range(-_halfSize.y, _halfSize.y));
                if (InArea(p)) return p;
            }
            return _centre + Vector3.up * WaterFx.SurfaceY;
        }

        /// <summary>Is the sea at <paramref name="p"/> open water to float on: a few metres deep all round (not on
        /// or beside the island)?</summary>
        public static bool OpenWater(Vector3 p)
        {
            float surface = WaterFx.SurfaceY;
            for (int i = 0; i < 5; i++)
            {
                Vector3 q = p + (i == 0 ? Vector3.zero : Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 4f);
                if (WaterFx.SeabedHeight(q, out float floor) && floor > surface - 2.5f) return false;
            }
            return true;
        }

        /// <summary><paramref name="who"/> was scared off the water by something at <paramref name="threat"/>: the
        /// gulls afloat near it go too, one after another.</summary>
        public void Startle(Seagull who, Vector3 threat)
        {
            foreach (var g in _gulls)
                if (g != null && g != who && g.State == Seagull.Mode.Float &&
                    (g.transform.position - who.transform.position).sqrMagnitude < startleRadius * startleRadius)
                    g.Scare(threat, Random.Range(0.15f, 0.7f));
        }

        void OnDestroy()
        {
            foreach (var g in _gulls)
                if (g != null) Destroy(g.gameObject);
        }
    }
}
