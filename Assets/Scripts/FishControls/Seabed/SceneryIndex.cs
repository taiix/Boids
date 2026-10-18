using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Where the seabed's scenery stands - the coral, sponges and rocks the reef is made of - bucketed by
    /// position, so things that swim (the orcas, the ammonites) can ask how high it reaches under a point and
    /// keep over it. Most of it has no colliders, so physics can't tell them.
    ///
    /// Rebuilt by <see cref="SeededVegetation"/> each time the reef is placed (the same reef on every machine).
    /// Each piece counts as a box (its renderers' bounds); kelp isn't in it (it's soft: you swim through).
    /// </summary>
    public static class SceneryIndex
    {
        const float Cell = 6f;
        const float MaxSize = 25f;   // bigger than this isn't scenery (the seabed, the arena's walls)

        static readonly Dictionary<Vector2Int, List<Bounds>> s_cells = new Dictionary<Vector2Int, List<Bounds>>();
        static readonly List<Renderer> s_renderers = new List<Renderer>();

        /// <summary>Pieces in the index.</summary>
        public static int Count { get; private set; }

        /// <summary>Forget everything (before the reef is placed again).</summary>
        public static void Clear()
        {
            s_cells.Clear();
            Count = 0;
        }

        /// <summary>Add each of <paramref name="pieces"/> (skipping any gone or switched off).</summary>
        public static void Add(IEnumerable<GameObject> pieces)
        {
            foreach (var go in pieces)
            {
                if (go == null || !go.activeInHierarchy) continue;
                go.GetComponentsInChildren(s_renderers);
                bool any = false;
                var b = new Bounds();
                foreach (var r in s_renderers)
                {
                    if (!r.enabled || r is ParticleSystemRenderer) continue;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (!any || b.size.x > MaxSize || b.size.z > MaxSize || b.size.y < 0.15f) continue;
                Vector2Int lo = Key(b.min), hi = Key(b.max);
                for (int x = lo.x; x <= hi.x; x++)
                    for (int z = lo.y; z <= hi.y; z++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!s_cells.TryGetValue(key, out var list)) s_cells[key] = list = new List<Bounds>();
                        list.Add(b);
                    }
                Count++;
            }
        }

        static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

        /// <summary>The highest scenery top within <paramref name="radius"/> metres of (x, z) across the seabed,
        /// or false if there's none there.</summary>
        public static bool TopAt(Vector3 p, float radius, out float top)
        {
            top = float.MinValue;
            Vector2Int lo = Key(p - new Vector3(radius, 0f, radius)), hi = Key(p + new Vector3(radius, 0f, radius));
            for (int x = lo.x; x <= hi.x; x++)
                for (int z = lo.y; z <= hi.y; z++)
                {
                    if (!s_cells.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                    foreach (var b in list)
                    {
                        if (b.max.y <= top) continue;
                        float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x);
                        float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z);
                        if (dx * dx + dz * dz <= radius * radius) top = b.max.y;
                    }
                }
            return top > float.MinValue;
        }

        /// <summary>What a swimmer <paramref name="radius"/> metres across must stay above at (x, z): the seabed
        /// plus <paramref name="seabedClearance"/>, or the scenery's top plus <paramref name="sceneryClearance"/>,
        /// whichever is higher. False where there's no seabed under it.</summary>
        public static bool FloorAt(Vector3 p, float radius, float seabedClearance, float sceneryClearance, out float floor)
        {
            floor = float.MinValue;
            bool ground = WaterFx.SeabedHeight(p, out float seabed);
            if (ground) floor = seabed + seabedClearance;
            if (TopAt(p, radius, out float top)) floor = Mathf.Max(floor, top + sceneryClearance);
            return floor > float.MinValue;
        }
    }
}
