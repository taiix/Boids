using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Co-op clean-up task: fish push trash into sinkholes in the seabed. Light pieces move for one fish;
    /// big ones need two pushing together, the same way. Every point sunk takes a little off the survival
    /// clock, and reaching the goal takes off a lot more.
    ///
    /// Every round the sinkholes go in new random spots - a few of them, well apart, so the shark can't
    /// just wait at one - away from spawns, the reef puzzle and the kelp forests; the trash is scattered
    /// around them.
    ///
    /// A plain scene object, not a network one (Mirror switches unspawned scene network objects off, which
    /// would stop it working in the offline sandbox): the server - or this machine, offline - places
    /// everything and spawns it for everyone. Holes and trash carry the shared state. Every machine shows
    /// its own fish the task's status (<see cref="MatchHud.ShowStatus"/>).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class TrashTask : MonoBehaviour
    {
        [System.Serializable]
        public struct TrashKind
        {
            public GameObject prefab;
            [Min(0)] public int count;
        }

        [Header("What spawns")]
        [SerializeField] GameObject sinkholePrefab;
        [SerializeField] TrashKind[] trash;

        [Header("Sinkholes (new spots every round)")]
        [SerializeField] int sinkholeCount = 3;
        [Tooltip("Keep sinkholes at least this far apart, so the shark can't guard them all from one spot.")]
        [SerializeField] float sinkholeSpacing = 45f;
        [Tooltip("Only on fairly flat ground (1 = level).")]
        [Range(0f, 1f)][SerializeField] float minFlatness = 0.9f;
        [Tooltip("Stay this far inside the seabed's edges, in metres.")]
        [SerializeField] float edgeMargin = 25f;
        [Tooltip("Keep this far from spawn points and the reef puzzle.")]
        [SerializeField] float keepClearRadius = 18f;
        [Tooltip("Keep this far out of the kelp forests.")]
        [SerializeField] float forestMargin = 4f;

        [Header("Trash")]
        [Tooltip("Each piece lands this far from a sinkhole (min, max metres), in a random direction.")]
        [SerializeField] Vector2 distanceFromSinkhole = new Vector2(14f, 45f);
        [Tooltip("Keep pieces at least this far apart, in metres.")]
        [SerializeField] float trashSpacing = 5f;
        [Tooltip("Only on fairly flat ground (1 = level), so pieces start out settled instead of rolling " +
                 "off downhill (0.93 is about 22°; trash rolls on slopes over 25°).")]
        [Range(0f, 1f)][SerializeField] float trashMinFlatness = 0.93f;

        [Header("Reward")]
        [Tooltip("Points needed to complete the task (a light piece is worth 1, a big one 2).")]
        [SerializeField] int goal = 9;
        [Tooltip("Seconds off the survival clock for every point of trash sunk.")]
        [SerializeField] float secondsPerPoint = 4f;
        [Tooltip("Extra seconds off the clock when the task is completed.")]
        [SerializeField] float completionSeconds = 30f;

        [Header("HUD")]
        [Tooltip("A fish within this many metres of a sinkhole sees how the clean-up is going.")]
        [SerializeField] float progressRange = 7f;
        [SerializeField] float completeMessageSeconds = 5f;

        enum Mode { None, Offline, Server }
        Mode _builtFor;
        readonly List<GameObject> _spawned = new List<GameObject>();
        readonly List<Sinkhole> _holes = new List<Sinkhole>();
        int _collected, _goal;
        bool _complete;
        float _seabedSeenAt = -1f;
        bool _sawComplete;
        float _messageUntil;

        void Awake()
        {
            // Clients spawn what the server spawns, so they need to know the prefabs.
            Register(sinkholePrefab);
            if (trash != null)
                foreach (var kind in trash) Register(kind.prefab);
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
            // The server places everything (offline, this machine does). A client gets the server's.
            Mode want = NetworkServer.active ? Mode.Server : NetworkClient.active ? Mode.None : Mode.Offline;
            if (_builtFor != Mode.None && _builtFor != want) Clear(); // e.g. F9 hosting replaces the offline set
            if (want != Mode.None && _builtFor == Mode.None && SeabedSettled(out var seabed)) Build(want, seabed);

            UpdateHud();
        }

        // Wait for the seabed - and a moment more, for the kelp forests that grow on it.
        bool SeabedSettled(out MeshCollider seabed)
        {
            seabed = null;
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain == null || !terrain.TryGetComponent(out seabed) || seabed.sharedMesh == null) return false;
            if (_seabedSeenAt < 0f) _seabedSeenAt = Time.time;
            return Time.time - _seabedSeenAt > 1.5f;
        }

        // ---------------------------------------------------------------- server / offline

        void Build(Mode mode, MeshCollider seabed)
        {
            _builtFor = mode;
            // New spots every round: not the terrain's seed, which would put them in the same places.
            var rng = new System.Random(unchecked(System.Environment.TickCount * 31 + (int)System.DateTime.Now.Ticks));
            Bounds area = seabed.bounds;
            var keepClear = KelpForests.KeepClearPoints();

            // Sinkholes: well apart. If the seabed won't fit them all at full spacing, ease off gradually.
            var holes = new List<Vector3>();
            const int tries = 800;
            for (int i = 0; i < tries && holes.Count < sinkholeCount; i++)
            {
                if (!RandomSpot(seabed, area, rng, minFlatness, out var p)) continue;
                if (TooClose(keepClear, p, keepClearRadius) || KelpForests.NearAnyForest(p, forestMargin)) continue;
                float spacing = sinkholeSpacing * Mathf.Lerp(1f, 0.5f, (float)i / tries);
                if (TooClose(holes, p, spacing)) continue;
                holes.Add(p);
            }

            _holes.Clear();
            foreach (var p in holes)
            {
                var go = Spawn(sinkholePrefab, p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                if (go != null && go.TryGetComponent(out Sinkhole hole))
                {
                    hole.Task = this;
                    _holes.Add(hole);
                }
            }

            // Trash: scattered around the holes, so every hole has some within reach.
            int available = 0, pieces = 0;
            var placed = new List<Vector3>();
            if (holes.Count > 0 && trash != null)
            {
                foreach (var kind in trash)
                {
                    if (kind.prefab == null || !kind.prefab.TryGetComponent(out PushableTrash proto)) continue;
                    for (int n = 0; n < kind.count; n++)
                    {
                        for (int attempt = 0; attempt < 60; attempt++)
                        {
                            Vector3 home = holes[rng.Next(holes.Count)];
                            float a = (float)(rng.NextDouble() * Mathf.PI * 2f);
                            float d = Mathf.Lerp(distanceFromSinkhole.x, distanceFromSinkhole.y, (float)rng.NextDouble());
                            float x = home.x + Mathf.Cos(a) * d, z = home.z + Mathf.Sin(a) * d;
                            if (x < area.min.x + edgeMargin || x > area.max.x - edgeMargin ||
                                z < area.min.z + edgeMargin || z > area.max.z - edgeMargin) continue;
                            if (!Ground(seabed, area, x, z, trashMinFlatness, out var p)) continue;
                            if (TooClose(placed, p, trashSpacing) || TooClose(holes, p, 8f)) continue;
                            // A piece with a sharp side starts with its blades toward its sinkhole (give or
                            // take), so pushing it in from behind - the smooth side - is always possible.
                            float yaw = (float)rng.NextDouble() * 360f;
                            Vector3 sharp = proto.SharpSideLocal;
                            if (proto.IsSharp && sharp.sqrMagnitude > 1e-6f)
                            {
                                Vector3 toHole = home - p;
                                toHole.y = 0f;
                                yaw = Vector3.SignedAngle(sharp, toHole, Vector3.up) + Mathf.Lerp(-35f, 35f, (float)rng.NextDouble());
                            }
                            if (Spawn(kind.prefab, p + Vector3.up * 1f, Quaternion.Euler(0f, yaw, 0f)) == null) break;
                            placed.Add(p);
                            available += proto.Points;
                            pieces++;
                            break;
                        }
                    }
                }
            }

            _collected = 0;
            _goal = Mathf.Max(1, Mathf.Min(goal, available));
            _complete = false;
            PushProgress();
            Debug.Log($"[TrashTask] {holes.Count} sinkholes and {pieces} pieces of trash ({available} points, goal {_goal}).");
            if (holes.Count < sinkholeCount)
                Debug.LogWarning($"[TrashTask] Only found room for {holes.Count} of {sinkholeCount} sinkholes.");
        }

        GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, position, rotation);
            if (NetworkServer.active) NetworkServer.Spawn(go);
            _spawned.Add(go);
            return go;
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
            _holes.Clear();
            _builtFor = Mode.None;
        }

        /// <summary>Server / offline: trash worth <paramref name="points"/> went down a sinkhole.</summary>
        public void ServerCollected(int points)
        {
            _collected += points;
            var match = MatchManager.Instance;
            if (match != null) match.ReduceRemaining(secondsPerPoint * points);
            if (!_complete && _collected >= _goal)
            {
                _complete = true;
                if (match != null) match.ReduceRemaining(completionSeconds);
                Debug.Log($"[TrashTask] Reef cleared! ({_collected}/{_goal})");
            }
            PushProgress();
        }

        void PushProgress()
        {
            foreach (var hole in _holes)
                if (hole != null) hole.ServerSetProgress(_collected, _goal, _complete);
        }

        bool RandomSpot(MeshCollider seabed, Bounds area, System.Random rng, float flatness, out Vector3 p)
        {
            float x = Mathf.Lerp(area.min.x + edgeMargin, area.max.x - edgeMargin, (float)rng.NextDouble());
            float z = Mathf.Lerp(area.min.z + edgeMargin, area.max.z - edgeMargin, (float)rng.NextDouble());
            return Ground(seabed, area, x, z, flatness, out p);
        }

        static bool Ground(MeshCollider seabed, Bounds area, float x, float z, float flatness, out Vector3 p)
        {
            p = default;
            var ray = new Ray(new Vector3(x, area.max.y + 5f, z), Vector3.down);
            if (!seabed.Raycast(ray, out var hit, area.size.y + 10f) || hit.normal.y < flatness) return false;
            p = hit.point;
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

        // ---------------------------------------------------------------- every machine: HUD

        // This machine's own fish, if it is one (the shark has no TrashPusher).
        static TrashPusher LocalPusher()
        {
            foreach (var p in TrashPusher.All)
                if (p != null && p.IsLocalFish) return p;
            return null;
        }

        void UpdateHud()
        {
            if (Sinkhole.All.Count == 0) return;
            var state = Sinkhole.All[0];

            // Everyone hears when it's done.
            if (state.Complete && !_sawComplete) _messageUntil = Time.time + completeMessageSeconds;
            _sawComplete = state.Complete;
            if (Time.time < _messageUntil)
            {
                MatchHud.ShowStatus("<color=#7CFC9A>Task completed — reef cleared!</color>");
                return;
            }

            var me = LocalPusher();
            if (me == null) return;

            // Pushing something that needs help, or pushing on the blades of something sharp.
            var pushing = me.LocalPushing;
            bool onBlades = pushing != null && pushing.IsSharpTowards(me.transform.position);
            if (pushing != null && (pushing.FishNeeded > 1 || onBlades))
            {
                string line = pushing.DisplayName;
                if (pushing.FishNeeded > 1) line += $" — {pushing.Pushers}/{pushing.FishNeeded} fish pushing";
                if (onBlades) line += " — <color=#ff7a66>you're on the sharp side, it'll cut you!</color>";
                MatchHud.ShowStatus(line);
                return;
            }

            // Only right at a sinkhole: how much is left to sink.
            if (state.Complete) return;
            Vector3 at = me.transform.position;
            float r2 = progressRange * progressRange;
            foreach (var h in Sinkhole.All)
                if (h != null && (h.Centre - at).sqrMagnitude < r2)
                {
                    MatchHud.ShowStatus($"Reef clean-up {state.Collected}/{state.Goal} — push trash into a sinkhole");
                    return;
                }
        }
    }
}
