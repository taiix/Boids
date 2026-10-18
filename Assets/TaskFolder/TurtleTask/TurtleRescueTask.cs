using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// The reef's sea turtles: a few swim free around the map (<see cref="TurtleSwimmer"/>), and every round
    /// one is caught - the turtle rescue task (<see cref="TangledTurtle"/>): a loggerhead caught in a ghost net in
    /// a new random spot - out in the open sand, on fairly flat seabed clear of coral and rocks, away from the
    /// spawns, the reef puzzle, the kelp forests and the trash sinkholes. Everyone hears when one is freed, and
    /// then it swims off and roams with the rest.
    ///
    /// A plain scene object, not a network one (like <see cref="TrashTask"/>: Mirror switches unspawned scene
    /// network objects off, which would stop it working in the offline sandbox): the server - or this machine,
    /// offline - places the turtles and spawns them for everyone.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class TurtleRescueTask : MonoBehaviour
    {
        [SerializeField] GameObject turtlePrefab;
        [Tooltip("Turtles caught each round (each one is its own task, with its own reward).")]
        [Min(0)][SerializeField] int turtles = 1;

        [Header("Turtles swimming free")]
        [SerializeField] GameObject swimmingTurtlePrefab;
        [Tooltip("How many roam the reef from the start (a rescued one joins them).")]
        [Min(0)][SerializeField] int swimmingTurtles = 4;
        [Tooltip("They start this high above the seabed (metres, min - max).")]
        [SerializeField] Vector2 swimHeight = new Vector2(3f, 8f);

        [Header("Where (new spots every round)")]
        [Tooltip("Only over fairly flat ground (1 = level), so the ropes reach the seabed evenly.")]
        [Range(0f, 1f)][SerializeField] float minFlatness = 0.85f;
        [Tooltip("Stay this far inside the seabed's edges, in metres.")]
        [SerializeField] float edgeMargin = 30f;
        [Tooltip("Keep this far from spawn points and the reef puzzle.")]
        [SerializeField] float keepClearRadius = 20f;
        [Tooltip("Keep this far out of the kelp forests.")]
        [SerializeField] float forestMargin = 6f;
        [Tooltip("Keep this far from the trash task's sinkholes.")]
        [SerializeField] float awayFromSinkholes = 20f;
        [Tooltip("With more than one turtle: keep them at least this far apart.")]
        [SerializeField] float turtleSpacing = 60f;
        [Tooltip("No scenery (coral, rocks, sponges) within this many metres, so the ropes and their rocks are " +
                 "out in the open sand.")]
        [SerializeField] float clearOfScenery = 4f;

        [Header("HUD")]
        [SerializeField] float completeMessageSeconds = 5f;

        enum Mode { None, Offline, Server }
        Mode _builtFor;
        readonly List<GameObject> _spawned = new List<GameObject>();
        readonly HashSet<TangledTurtle> _announced = new HashSet<TangledTurtle>();
        float _seabedSeenAt = -1f;
        float _messageUntil;

        void Awake()
        {
            // Clients spawn what the server spawns, so they need to know the prefabs.
            Register(turtlePrefab);
            Register(swimmingTurtlePrefab);
        }

        static void Register(GameObject prefab)
        {
            // Registration outlives the scene; doing it again next round makes Mirror warn "Replacing existing prefab".
            if (prefab != null && prefab.TryGetComponent(out NetworkIdentity id)
                && !(NetworkClient.prefabs.TryGetValue(id.assetId, out var known) && known == prefab))
                NetworkClient.RegisterPrefab(prefab);
        }

        void Update()
        {
            Mode want = NetworkServer.active ? Mode.Server : NetworkClient.active ? Mode.None : Mode.Offline;
            if (_builtFor != Mode.None && _builtFor != want) Clear(); // e.g. F9 hosting replaces the offline set
            if (want != Mode.None && _builtFor == Mode.None && SeabedSettled(out var seabed)) Build(want, seabed);
            UpdateHud();
        }

        // Wait for the seabed - and a moment more, for the kelp forests and sinkholes placed on it.
        bool SeabedSettled(out MeshCollider seabed)
        {
            seabed = null;
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain == null || !terrain.TryGetComponent(out seabed) || seabed.sharedMesh == null) return false;
            if (_seabedSeenAt < 0f) _seabedSeenAt = Time.time;
            return Time.time - _seabedSeenAt > 2f;
        }

        void Build(Mode mode, MeshCollider seabed)
        {
            _builtFor = mode;
            if (turtlePrefab == null || !turtlePrefab.TryGetComponent(out TangledTurtle proto)) return;
            var rng = new System.Random(unchecked(System.Environment.TickCount * 17 + (int)System.DateTime.Now.Ticks));
            Bounds area = seabed.bounds;
            var keepClear = KelpForests.KeepClearPoints();
            var holes = new List<Vector3>();
            foreach (var h in Sinkhole.All)
                if (h != null) holes.Add(h.Centre);

            var scenery = new SceneryGrid(clearOfScenery);
            var placed = new List<Vector3>();
            const int tries = 600;
            for (int i = 0; i < tries && placed.Count < turtles; i++)
            {
                float x = Mathf.Lerp(area.min.x + edgeMargin, area.max.x - edgeMargin, (float)rng.NextDouble());
                float z = Mathf.Lerp(area.min.z + edgeMargin, area.max.z - edgeMargin, (float)rng.NextDouble());
                var ray = new Ray(new Vector3(x, area.max.y + 5f, z), Vector3.down);
                if (!seabed.Raycast(ray, out var hit, area.size.y + 10f) || hit.normal.y < minFlatness) continue;
                Vector3 p = hit.point;
                if (TooClose(keepClear, p, keepClearRadius) || KelpForests.NearAnyForest(p, forestMargin)
                    || CoralReefs.NearAnyReef(p, forestMargin)) continue;
                if (TooClose(holes, p, awayFromSinkholes)) continue;
                // (Over a seabed thick with scenery, give up on that for the last quarter of the tries.)
                if (i < tries * 3 / 4 && scenery.Near(p + Vector3.up * proto.HoverHeight * 0.5f)) continue;
                if (TooClose(placed, p, turtleSpacing * Mathf.Lerp(1f, 0.4f, (float)i / tries))) continue;
                // The ropes' rocks go about three metres out: the ground there has to be there too.
                if (!FlatAround(seabed, area, p)) continue;
                placed.Add(p);

                var go = Instantiate(turtlePrefab, p + Vector3.up * proto.HoverHeight,
                                     Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                go.GetComponent<TangledTurtle>().ServerSetup(rng);
                if (NetworkServer.active) NetworkServer.Spawn(go);
                _spawned.Add(go);
            }
            // The free ones: anywhere in open water over the reef, heading every which way.
            int free = 0;
            for (int i = 0; i < swimmingTurtles * 40 && free < swimmingTurtles && swimmingTurtlePrefab != null; i++)
            {
                float x = Mathf.Lerp(area.min.x + edgeMargin, area.max.x - edgeMargin, (float)rng.NextDouble());
                float z = Mathf.Lerp(area.min.z + edgeMargin, area.max.z - edgeMargin, (float)rng.NextDouble());
                var ray = new Ray(new Vector3(x, area.max.y + 5f, z), Vector3.down);
                if (!seabed.Raycast(ray, out var hit, area.size.y + 10f)) continue;
                Vector3 p = hit.point + Vector3.up * Mathf.Lerp(swimHeight.x, swimHeight.y, (float)rng.NextDouble());
                if (p.y > WaterFx.SurfaceY - 4f || TooClose(keepClear, p, keepClearRadius)) continue;
                var arena = ArenaWalls.Instance;
                if (arena != null && arena.Count > 0 && !arena.Inside(p, 15f)) continue;
                var go = Instantiate(swimmingTurtlePrefab, p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                if (NetworkServer.active) NetworkServer.Spawn(go);
                _spawned.Add(go);
                free++;
            }

            Debug.Log($"[TurtleTask] {placed.Count} tangled turtle(s) placed, {free} swimming free.");
            if (placed.Count < turtles)
                Debug.LogWarning($"[TurtleTask] Only found room for {placed.Count} of {turtles} turtles.");
        }

        // The scenery's meshes (coral, sponges, rocks - they've no colliders) bucketed by where they are, so a
        // spot can be checked against the few nearby.
        class SceneryGrid
        {
            const float Cell = 8f;
            readonly Dictionary<Vector2Int, List<Bounds>> _cells = new Dictionary<Vector2Int, List<Bounds>>();
            readonly float _clear;

            public SceneryGrid(float clear)
            {
                _clear = clear;
                foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
                {
                    if (!r.enabled) continue;
                    Bounds b = r.bounds;
                    if (b.size.magnitude > 25f) continue;   // the seabed itself, the arena's walls
                    var key = Key(b.center);
                    if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<Bounds>();
                    list.Add(b);
                }
            }

            static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));

            public bool Near(Vector3 p)
            {
                var key = Key(p);
                float reach = _clear * _clear;
                for (int x = -2; x <= 2; x++)
                    for (int z = -2; z <= 2; z++)
                        if (_cells.TryGetValue(new Vector2Int(key.x + x, key.y + z), out var list))
                            foreach (var b in list)
                                if (b.SqrDistance(p) < reach) return true;
                return false;
            }
        }

        // No cliff or hole where the ropes come down (within a metre of the middle's height, all round).
        static bool FlatAround(MeshCollider seabed, Bounds area, Vector3 p)
        {
            for (int k = 0; k < 8; k++)
            {
                Vector3 q = p + Quaternion.Euler(0f, 45f * k, 0f) * Vector3.forward * 3.2f;
                var ray = new Ray(new Vector3(q.x, area.max.y + 5f, q.z), Vector3.down);
                if (!seabed.Raycast(ray, out var hit, area.size.y + 10f) || Mathf.Abs(hit.point.y - p.y) > 1f) return false;
            }
            return true;
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

        void Clear()
        {
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                if (NetworkServer.active && go.TryGetComponent(out NetworkIdentity id) && id.netId != 0) NetworkServer.Destroy(go);
                else Destroy(go);
            }
            _spawned.Clear();
            _announced.Clear();
            _builtFor = Mode.None;
        }

        // Everyone hears when a turtle is freed.
        void UpdateHud()
        {
            foreach (var t in TangledTurtle.All)
                if (t != null && t.Freed && _announced.Add(t))
                    _messageUntil = Time.time + completeMessageSeconds;
            if (Time.time < _messageUntil)
                MatchHud.ShowStatus("<color=#7CFC9A>Task completed — the turtle is free!</color>");
        }
    }
}
