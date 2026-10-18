using System.Collections.Generic;
using CodenameLib.ProceduralTerrain;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Grows coral reefs on the seabed - a few proper reefs instead of coral sprinkled evenly everywhere.
    ///
    /// Each reef is an irregular patch (a few overlapping lobes, with sand channels running through it from a
    /// noise field) built the way a real one is: mounds of reef rock in its heart, the big colonies - brain,
    /// table, cauliflower, plate corals, tube sponges - packed on and around them, branching corals and sea fans
    /// (all a reef's fans turned the same way, across the current) further out, and toward the fringe smaller
    /// stuff thinning into the sand: anemones, rubble, shells, starfish. Corals grow in colonies - a handful of
    /// one kind together - bigger toward the heart, standing on the rock where there is some.
    ///
    /// The seabed scatter (<see cref="ProceduralVegetation"/>) is thinned to match: inside a reef it's cleared
    /// (the reef replaces it), and out on the sand only <see cref="strayCoralKept"/> of its coral stays, as the
    /// odd colony between reefs; its rocks, shells and weed are left alone.
    ///
    /// Placed from the terrain's seed once the seabed is final (<see cref="SeededVegetation"/> calls
    /// <see cref="RebuildAll"/> after the scatter and the kelp forests), so every player gets the same reefs.
    /// Set up with FishGame > Set Up Coral Reefs.
    /// </summary>
    public class CoralReefs : MonoBehaviour
    {
        [System.Serializable]
        public class Kind
        {
            public string name;
            [Tooltip("Its look-alikes (variants of one coral): one is picked for each piece.")]
            public GameObject[] prefabs;
            [Tooltip("Scale, times the prefab's own (min, max). Bigger toward the reef's heart.")]
            public Vector2 scale = new Vector2(0.8f, 1.2f);
            [Tooltip("Pieces in a colony (min, max).")]
            public Vector2Int colony = new Vector2Int(2, 5);
            [Tooltip("Where on the reef it grows: 0 = the fringe, 1 = the heart (min, max).")]
            public Vector2 zone = new Vector2(0f, 1f);
            [Tooltip("How often it's picked against the others that grow there.")]
            public float weight = 1f;
            [Tooltip("Grows on the reef's rock where there is some (else only on the sand).")]
            public bool onRock = true;
            [Tooltip("Faces across the current, like a sea fan: every one on a reef turned about the same way.")]
            public bool facesCurrent;
            [Tooltip("How close together, as a share of their size (lower = packed tighter, overlapping).")]
            [Range(0.2f, 1.5f)] public float spacing = 0.7f;
        }

        [Header("What it's made of")]
        [Tooltip("The reef's rock: mounds in its heart that the coral grows on.")]
        [SerializeField] GameObject[] rockPrefabs;
        [Tooltip("Scale of a rock mound, times the prefab's own (min, max).")]
        [SerializeField] Vector2 rockScale = new Vector2(1.5f, 2.8f);
        [Tooltip("Rock mounds per 100 m² of reef.")]
        [SerializeField] float rockDensity = 3f;
        [SerializeField] Kind[] kinds;

        [Header("Reefs")]
        [SerializeField] int reefCount = 9;
        [Tooltip("How far a reef reaches from its middle, in metres (min, max). It's made of a few lobes, so " +
                 "its outline is ragged, not round.")]
        [SerializeField] Vector2 reefRadius = new Vector2(12f, 19f);
        [SerializeField] Vector2Int lobesPerReef = new Vector2Int(3, 5);
        [Tooltip("Keep reefs at least this far apart, middle to middle, in metres.")]
        [SerializeField] float reefSpacing = 55f;
        [Tooltip("Coral colonies per 100 m² of reef.")]
        [SerializeField] float colonyDensity = 20f;
        [Tooltip("Sand channels through a reef: how much the noise eats into it (0 = solid).")]
        [Range(0f, 1f)][SerializeField] float channels = 0.45f;

        [Header("Where")]
        [Tooltip("Only where the seabed is at least this flat at a reef's middle (1 = level).")]
        [Range(0f, 1f)][SerializeField] float minFlatness = 0.8f;
        [Tooltip("Stay this far inside the seabed's edges, in metres.")]
        [SerializeField] float edgeMargin = 22f;
        [Tooltip("Keep reefs this far from the tasks and the spawn points, so those stay in the open.")]
        [SerializeField] float keepClearRadius = 20f;
        [Tooltip("And this far from the kelp forests.")]
        [SerializeField] float forestMargin = 6f;

        [Header("The scattered scenery")]
        [Tooltip("Of the coral the seabed scatter puts out on the sand between reefs, the share kept.")]
        [Range(0f, 1f)][SerializeField] float strayCoralKept = 0.2f;

        // A reef's layout: its lobes (centre xz, radius) and the offset into the channel noise.
        struct Reef
        {
            public Vector3 centre;
            public Vector3[] lobes;   // x, z, radius
            public Vector2 noise;
            public float reach;
            public float currentYaw;
        }

        struct Info { public float bottom, height, footprint; }

        static readonly List<CoralReefs> s_all = new List<CoralReefs>();
        static readonly Dictionary<GameObject, Info> s_info = new Dictionary<GameObject, Info>();
        readonly List<GameObject> _spawned = new List<GameObject>();
        readonly List<Reef> _reefs = new List<Reef>();

        void OnEnable() => s_all.Add(this);
        void OnDisable() => s_all.Remove(this);

        /// <summary>Grow every reef again on the current seabed (after the scatter and the kelp).</summary>
        public static void RebuildAll()
        {
            foreach (var r in s_all)
                if (r != null) r.Rebuild();
        }

        /// <summary>Put every reef's pieces in <see cref="SceneryIndex"/>.</summary>
        public static void AddAllToIndex()
        {
            foreach (var r in s_all)
                if (r != null) SceneryIndex.Add(r._spawned);
        }

        /// <summary>Is <paramref name="p"/> on a reef, or within <paramref name="margin"/> metres of one
        /// (across the seabed)? For keeping other things off them.</summary>
        public static bool NearAnyReef(Vector3 p, float margin)
        {
            foreach (var r in s_all)
                if (r != null)
                    foreach (var reef in r._reefs)
                        if (Covers(reef, p, margin)) return true;
            return false;
        }

        static bool Covers(Reef reef, Vector3 p, float margin)
        {
            float dx = p.x - reef.centre.x, dz = p.z - reef.centre.z, reach = reef.reach + margin;
            if (dx * dx + dz * dz > reach * reach) return false;
            foreach (var l in reef.lobes)
            {
                float lx = p.x - l.x, lz = p.z - l.y, r = l.z + margin;
                if (lx * lx + lz * lz < r * r) return true;
            }
            return false;
        }

        // How much reef there is at p: 1 in a lobe's heart, falling to 0 at its edge, eaten into by the channels.
        float Density(Reef reef, Vector3 p)
        {
            float d = 0f;
            foreach (var l in reef.lobes)
            {
                float t = Mathf.Sqrt((p.x - l.x) * (p.x - l.x) + (p.z - l.y) * (p.z - l.y)) / l.z;
                d = Mathf.Max(d, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, t)));
            }
            if (d <= 0f) return 0f;
            float n = Mathf.PerlinNoise(reef.noise.x + p.x * 0.11f, reef.noise.y + p.z * 0.11f);
            return Mathf.Clamp01(d - channels * Mathf.Max(0f, 0.62f - n) * 2.2f);
        }

        public void Rebuild()
        {
            Clear();
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain == null || !terrain.TryGetComponent(out MeshCollider seabed) || seabed.sharedMesh == null) return;
            if (kinds == null || kinds.Length == 0) return;

            // Our own stream from the terrain's seed: the same reefs for everyone.
            var rng = new System.Random(unchecked(terrain.settings.seed * 19349663 + 83492791));
            Bounds area = seabed.bounds;
            var keepClear = KelpForests.KeepClearPoints();

            for (int tries = 0; _reefs.Count < reefCount && tries < reefCount * 60; tries++)
            {
                float x = Mathf.Lerp(area.min.x + edgeMargin, area.max.x - edgeMargin, (float)rng.NextDouble());
                float z = Mathf.Lerp(area.min.z + edgeMargin, area.max.z - edgeMargin, (float)rng.NextDouble());
                if (!Ground(seabed, x, z, out var hit) || hit.normal.y < minFlatness) continue;
                if (TooClose(hit.point, reefSpacing) || NearAny(keepClear, hit.point, keepClearRadius)
                    || KelpForests.NearAnyForest(hit.point, forestMargin + reefRadius.x)) continue;
                _reefs.Add(Layout(hit.point, rng));
            }

            int pieces = 0;
            for (int i = 0; i < _reefs.Count; i++)
                pieces += Grow(seabed, _reefs[i], i, rng);
            int thinned = ThinScatter();
            Debug.Log($"[CoralReefs] Grew {_reefs.Count} reefs ({pieces} pieces); cleared {thinned} scattered pieces from them and the sand.");
        }

        Reef Layout(Vector3 centre, System.Random rng)
        {
            float radius = Mathf.Lerp(reefRadius.x, reefRadius.y, (float)rng.NextDouble());
            int count = rng.Next(lobesPerReef.x, lobesPerReef.y + 1);
            var lobes = new Vector3[count];
            float reach = 0f;
            for (int i = 0; i < count; i++)
            {
                // The first lobe in the middle, the rest pushed out every which way: a ragged outline.
                float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float off = i == 0 ? 0f : radius * Mathf.Lerp(0.3f, 0.6f, (float)rng.NextDouble());
                float r = radius * Mathf.Lerp(0.45f, 0.7f, (float)rng.NextDouble());
                lobes[i] = new Vector3(centre.x + Mathf.Cos(a) * off, centre.z + Mathf.Sin(a) * off, r);
                reach = Mathf.Max(reach, off + r);
            }
            return new Reef
            {
                centre = centre, lobes = lobes, reach = reach,
                noise = new Vector2((float)rng.NextDouble() * 500f, (float)rng.NextDouble() * 500f),
                currentYaw = (float)rng.NextDouble() * 360f,
            };
        }

        int Grow(MeshCollider seabed, Reef reef, int index, System.Random rng)
        {
            var parent = new GameObject($"Reef {index + 1}").transform;
            parent.SetParent(transform, false);
            parent.position = reef.centre;
            _spawned.Add(parent.gameObject);

            float area = 0f;
            foreach (var l in reef.lobes) area += Mathf.PI * l.z * l.z;
            area *= 0.6f;   // the lobes overlap, and the channels take some

            // ---- the rock: mounds in the heart, overlapping into one mass
            var placed = new List<Vector4>();   // xz position, footprint radius, 1 = rock
            var rocks = new HashSet<Collider>();
            int wantRocks = Mathf.RoundToInt(area / 100f * rockDensity);
            if (rockPrefabs != null && rockPrefabs.Length > 0)
                for (int tries = 0, n = 0; n < wantRocks && tries < wantRocks * 30; tries++)
                {
                    Vector3 p = Somewhere(reef, rng);
                    float d = Density(reef, p);
                    if (d < 0.45f || rng.NextDouble() > d) continue;
                    if (!Ground(seabed, p.x, p.z, out var hit)) continue;
                    var prefab = rockPrefabs[rng.Next(rockPrefabs.Length)];
                    if (prefab == null) continue;
                    var info = InfoOf(prefab);
                    float s = Mathf.Lerp(rockScale.x, rockScale.y, (float)rng.NextDouble()) * Mathf.Lerp(0.75f, 1f, d);
                    float r = info.footprint * s;
                    if (Crowded(placed, hit.point, r, 0.55f, ignoreRock: false)) continue;
                    var rot = Quaternion.Euler((float)(rng.NextDouble() * 24 - 12), (float)rng.NextDouble() * 360f, (float)(rng.NextDouble() * 24 - 12));
                    // Bedded in: a third of it under the sand.
                    var go = Place(prefab, hit.point + Vector3.up * (-info.bottom * s - info.height * s * 0.33f), rot, s, parent);
                    foreach (var c in go.GetComponentsInChildren<Collider>()) rocks.Add(c);
                    placed.Add(new Vector4(hit.point.x, hit.point.z, r, 1f));
                    n++;
                }
            Physics.SyncTransforms();   // the coral stands on the rock: raycasts have to see it

            // ---- the coral, in colonies
            var options = new List<Kind>();
            int wantColonies = Mathf.RoundToInt(area / 100f * colonyDensity);
            for (int tries = 0, colonies = 0; colonies < wantColonies && tries < wantColonies * 12; tries++)
            {
                Vector3 c = Somewhere(reef, rng);
                float d = Density(reef, c);
                if (d < 0.06f) continue;
                var kind = Pick(d, rng, options);
                if (kind == null) continue;
                var info = InfoOf(kind.prefabs[0]);
                int count = rng.Next(kind.colony.x, kind.colony.y + 1);
                float spread = info.footprint * kind.scale.y * (0.9f + 0.55f * Mathf.Sqrt(count));
                int grew = 0;
                for (int k = 0; k < count * 3 && grew < count; k++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2f), dist = spread * (float)rng.NextDouble();
                    Vector3 p = new Vector3(c.x + Mathf.Cos(a) * dist, 0f, c.z + Mathf.Sin(a) * dist);
                    float here = Density(reef, p);
                    if (here < 0.04f) continue;
                    if (Grow(seabed, kind, p, here, reef.currentYaw, placed, rocks, parent, rng)) grew++;
                }
                if (grew > 0) colonies++;
            }
            return placed.Count;   // rock and coral
        }

        // One piece of a colony at p (xz): on the rock or the sand, leaning a little with the ground under it.
        bool Grow(MeshCollider seabed, Kind kind, Vector3 p, float d, float currentYaw, List<Vector4> placed,
                  HashSet<Collider> rocks, Transform parent, System.Random rng)
        {
            var prefab = kind.prefabs[rng.Next(kind.prefabs.Length)];
            if (prefab == null) return false;
            var info = InfoOf(prefab);
            float s = Mathf.Lerp(kind.scale.x, kind.scale.y, (float)rng.NextDouble()) * Mathf.Lerp(0.8f, 1.12f, d);
            float r = info.footprint * s;
            if (Crowded(placed, p, r, kind.spacing, ignoreRock: kind.onRock)) return false;
            if (!Footing(seabed, p, kind.onRock ? rocks : null, out var hit)) return false;
            if (hit.normal.y < 0.45f) return false;   // too steep a face of rock to hold on to

            var up = Vector3.Slerp(Vector3.up, hit.normal, 0.55f).normalized;
            float yaw = kind.facesCurrent
                ? currentYaw + (float)(rng.NextDouble() * 40 - 20)   // across the reef's current, give or take
                : (float)rng.NextDouble() * 360f;
            var face = Quaternion.Euler(0f, yaw, 0f);
            var rot = Quaternion.FromToRotation(Vector3.up, up) * face;
            // Its base on the ground, sunk in a touch so it looks rooted.
            Vector3 at = hit.point - up * (info.bottom * s + 0.05f + info.height * s * 0.04f);
            Place(prefab, at, rot, s, parent);
            placed.Add(new Vector4(p.x, p.z, r, 0f));
            return true;
        }

        Kind Pick(float d, System.Random rng, List<Kind> options)
        {
            options.Clear();
            float total = 0f;
            foreach (var k in kinds)
                if (k != null && k.prefabs != null && k.prefabs.Length > 0 && d >= k.zone.x && d <= k.zone.y)
                {
                    options.Add(k);
                    total += k.weight;
                }
            float roll = (float)rng.NextDouble() * total;
            foreach (var k in options)
                if ((roll -= k.weight) <= 0f) return k;
            return options.Count > 0 ? options[options.Count - 1] : null;
        }

        GameObject Place(GameObject prefab, Vector3 at, Quaternion rot, float scale, Transform parent)
        {
            var go = Instantiate(prefab, at, rot, parent);
            go.name = prefab.name;
            go.transform.localScale = prefab.transform.localScale * scale;
            _spawned.Add(go);
            return go;
        }

        static Vector3 Somewhere(Reef reef, System.Random rng)
        {
            var l = reef.lobes[rng.Next(reef.lobes.Length)];
            float a = (float)(rng.NextDouble() * Mathf.PI * 2f), r = l.z * Mathf.Sqrt((float)rng.NextDouble());
            return new Vector3(l.x + Mathf.Cos(a) * r, 0f, l.y + Mathf.Sin(a) * r);
        }

        // Too close to what's there already? Coral that grows on rock can stand on a mound's footprint.
        static bool Crowded(List<Vector4> placed, Vector3 p, float r, float spacing, bool ignoreRock)
        {
            foreach (var q in placed)
            {
                if (ignoreRock && q.w > 0.5f) continue;
                float dx = q.x - p.x, dz = q.y - p.z, min = (q.z + r) * spacing;
                if (dx * dx + dz * dz < min * min) return true;
            }
            return false;
        }

        // The highest ground at p: the seabed, or (for coral that grows on it) the reef's rock.
        static bool Footing(MeshCollider seabed, Vector3 p, HashSet<Collider> rocks, out RaycastHit best)
        {
            best = default;
            Bounds b = seabed.bounds;
            var ray = new Ray(new Vector3(p.x, b.max.y + 20f, p.z), Vector3.down);
            float length = b.size.y + 40f;
            bool found = seabed.Raycast(ray, out best, length);
            if (rocks == null || rocks.Count == 0) return found;
            foreach (var hit in Physics.RaycastAll(ray, length, ~0, QueryTriggerInteraction.Ignore))
                if (rocks.Contains(hit.collider) && (!found || hit.point.y > best.point.y))
                {
                    best = hit;
                    found = true;
                }
            return found;
        }

        // How a prefab sits: its lowest point under its pivot, its height and how far it spreads (at its own
        // scale, upright) - from its meshes, so it works on the asset in a build too.
        static Info InfoOf(GameObject prefab)
        {
            if (s_info.TryGetValue(prefab, out var info)) return info;
            var root = prefab.transform;
            Matrix4x4 toRoot = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                Bounds mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; }
                    else b.Encapsulate(w);
                }
            }
            info = any
                ? new Info { bottom = b.min.y, height = b.size.y, footprint = Mathf.Max(0.15f, Mathf.Max(b.extents.x, b.extents.z)) }
                : new Info { bottom = 0f, height = 1f, footprint = 0.5f };
            s_info[prefab] = info;
            return info;
        }

        // ------------------------------------------------------------------ the scatter

        // Clear the seabed scatter off the reefs, and most of its coral off the sand between them.
        int ThinScatter()
        {
            var coral = new HashSet<string>();
            foreach (var k in kinds)
                if (k?.prefabs != null)
                    foreach (var p in k.prefabs)
                        if (p != null) coral.Add(p.name);

            int removed = 0;
            foreach (var veg in FindObjectsByType<ProceduralVegetation>())
            {
                var list = veg.GetSpawnedObjects();
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var go = list[i];
                    if (go == null) { list.RemoveAt(i); continue; }
                    Vector3 p = go.transform.position;
                    bool onReef = NearAnyReef(p, 1.5f);
                    if (!onReef && (!coral.Contains(go.name) || Keep(p))) continue;
                    go.SetActive(false);   // gone from physics now, not at the end of the frame
                    Destroy(go);
                    list.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        // The same answer on every machine for the same spot (the scatter is the same everywhere).
        bool Keep(Vector3 p)
        {
            unchecked
            {
                uint h = (uint)Mathf.RoundToInt(p.x * 100f) * 73856093u ^ (uint)Mathf.RoundToInt(p.z * 100f) * 19349663u;
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffff) / 65535f < strayCoralKept;
            }
        }

        // ------------------------------------------------------------------ helpers

        bool TooClose(Vector3 p, float distance)
        {
            foreach (var r in _reefs)
            {
                float dx = r.centre.x - p.x, dz = r.centre.z - p.z;
                if (dx * dx + dz * dz < distance * distance) return true;
            }
            return false;
        }

        static bool NearAny(List<Vector3> points, Vector3 p, float distance)
        {
            foreach (var q in points)
            {
                float dx = q.x - p.x, dz = q.z - p.z;
                if (dx * dx + dz * dz < distance * distance) return true;
            }
            return false;
        }

        static bool Ground(MeshCollider seabed, float x, float z, out RaycastHit hit)
        {
            Bounds b = seabed.bounds;
            return seabed.Raycast(new Ray(new Vector3(x, b.max.y + 5f, z), Vector3.down), out hit, b.size.y + 10f);
        }

        void Clear()
        {
            foreach (var go in _spawned)
                if (go != null)
                {
                    go.SetActive(false);
                    Destroy(go);
                }
            _spawned.Clear();
            _reefs.Clear();
        }

#if UNITY_EDITOR
        /// <summary>Editor (FishGame > Set Up Coral Reefs): what the reefs are made of.</summary>
        public void SetPalette(GameObject[] rocks, Kind[] palette)
        {
            rockPrefabs = rocks;
            kinds = palette;
        }
#endif
    }
}
