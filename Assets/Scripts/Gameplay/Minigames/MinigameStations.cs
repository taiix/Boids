using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishGame
{
    /// <summary>
    /// Puts the station minigames on the reef each round: a leaking drum (<see cref="LeakStation"/>),
    /// bleached coral (<see cref="CoralScrubStation"/>) and a tangled fishing line (<see cref="UntangleStation"/>),
    /// on open, fairly flat sand clear of the sinkholes, kelp forests and reefs.
    ///
    /// Stations are built in code, not prefabs: the server (or this machine, offline) makes one with
    /// <see cref="Create"/> and spawns it under a fixed asset id; every client builds the same object from
    /// that id through a Mirror spawn handler registered here. Spawns itself in any scene with a seabed
    /// (<see cref="TestTerrain"/>) - nothing to place. Counts are below.
    /// </summary>
    public class MinigameStations : MonoBehaviour
    {
        public enum Kind : byte { Leak = 1, Coral = 2, Line = 3 }

        // How many of each, every round.
        static readonly (Kind kind, int count)[] Counts = { (Kind.Leak, 2), (Kind.Coral, 3), (Kind.Line, 2) };

        const uint AssetIdBase = 0x4D470000;   // "MG..": far from Mirror's prefab-hash ids
        static uint AssetId(Kind k) => AssetIdBase + (uint)k;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            RegisterHandlers();
            SceneManager.sceneLoaded += (_, __) => Ensure();
            Ensure();
        }

        static void Ensure()
        {
            if (FindAnyObjectByType<MinigameStations>() != null) return;
            if (FindAnyObjectByType<TestTerrain>() == null) return;
            new GameObject("MinigameStations").AddComponent<MinigameStations>();
        }

        static void RegisterHandlers()
        {
            foreach (var (kind, _) in Counts)
            {
                var k = kind;
                NetworkClient.UnregisterSpawnHandler(AssetId(k));
                NetworkClient.RegisterSpawnHandler(AssetId(k),
                    msg => Create(k, msg.position, msg.rotation),
                    go => Destroy(go));
            }
        }

        /// <summary>Builds a station (every machine builds its own the same way).</summary>
        public static GameObject Create(Kind kind, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(kind + "Station");
            go.SetActive(false);   // all components in before Awake (NetworkIdentity collects its behaviours there)
            go.transform.SetPositionAndRotation(position, rotation);
            go.AddComponent<SphereCollider>();
            switch (kind)
            {
                case Kind.Leak: go.AddComponent<LeakStation>(); break;
                case Kind.Coral: go.AddComponent<CoralScrubStation>(); break;
                case Kind.Line: go.AddComponent<UntangleStation>(); break;
            }
            if (!go.TryGetComponent(out NetworkIdentity _)) go.AddComponent<NetworkIdentity>();
            go.SetActive(true);
            return go;
        }

        // ------------------------------------------------------------------ placing them

        [Tooltip("Stay this far inside the seabed's edges, in metres.")]
        [SerializeField] float edgeMargin = 30f;
        [SerializeField] float awayFromSinkholes = 15f;
        [SerializeField] float stationSpacing = 35f;
        [Range(0f, 1f)][SerializeField] float minFlatness = 0.88f;

        enum Mode { None, Offline, Server }
        Mode _builtFor;
        readonly List<GameObject> _spawned = new List<GameObject>();
        float _seabedSeenAt = -1f;

        void Awake() => RegisterHandlers();   // again per scene: a NetworkClient shutdown may have dropped them

        void Update()
        {
            Mode want = NetworkServer.active ? Mode.Server : NetworkClient.active ? Mode.None : Mode.Offline;
            if (_builtFor != Mode.None && _builtFor != want) Clear();   // e.g. F9 hosting replaces the offline set
            if (want != Mode.None && _builtFor == Mode.None && SeabedSettled(out var seabed)) Build(want, seabed);
        }

        bool SeabedSettled(out MeshCollider seabed)
        {
            seabed = null;
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain == null || !terrain.TryGetComponent(out seabed) || seabed.sharedMesh == null) return false;
            if (_seabedSeenAt < 0f) _seabedSeenAt = Time.time;
            return Time.time - _seabedSeenAt > 2.5f;   // after the kelp, reefs and sinkholes are placed
        }

        void Build(Mode mode, MeshCollider seabed)
        {
            _builtFor = mode;
            var rng = new System.Random(unchecked(System.Environment.TickCount * 31 + 7));
            Bounds area = seabed.bounds;
            var keepClear = KelpForests.KeepClearPoints();
            var placed = new List<Vector3>();
            foreach (var (kind, count) in Counts)
            {
                for (int n = 0; n < count; n++)
                {
                    for (int tries = 0; tries < 400; tries++)
                    {
                        float x = Mathf.Lerp(area.min.x + edgeMargin, area.max.x - edgeMargin, (float)rng.NextDouble());
                        float z = Mathf.Lerp(area.min.z + edgeMargin, area.max.z - edgeMargin, (float)rng.NextDouble());
                        var ray = new Ray(new Vector3(x, area.max.y + 5f, z), Vector3.down);
                        if (!seabed.Raycast(ray, out var hit, area.size.y + 10f) || hit.normal.y < minFlatness) continue;
                        Vector3 p = hit.point;
                        if (Near(keepClear, p, 20f) || KelpForests.NearAnyForest(p, 6f) || CoralReefs.NearAnyReef(p, 4f)) continue;
                        bool nearHole = false;
                        foreach (var h in Sinkhole.All) if (h != null && (h.Centre - p).sqrMagnitude < awayFromSinkholes * awayFromSinkholes) nearHole = true;
                        if (nearHole) continue;
                        if (Near(placed, p, stationSpacing * Mathf.Lerp(1f, 0.4f, tries / 400f))) continue;
                        placed.Add(p);

                        var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                        var go = Create(kind, p, rot);
                        if (mode == Mode.Server) NetworkServer.Spawn(go, AssetId(kind));
                        _spawned.Add(go);
                        break;
                    }
                }
            }
            Debug.Log($"[Minigame] Placed {_spawned.Count} minigame stations ({mode}).");
        }

        static bool Near(List<Vector3> points, Vector3 p, float r)
        {
            foreach (var q in points)
            {
                float dx = q.x - p.x, dz = q.z - p.z;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        void Clear()
        {
            foreach (var go in _spawned)
                if (go != null)
                {
                    if (NetworkServer.active && go.TryGetComponent(out NetworkIdentity id) && id.netId != 0) NetworkServer.Destroy(go);
                    else Destroy(go);
                }
            _spawned.Clear();
            _builtFor = Mode.None;
        }

        void OnDestroy() => Clear();
    }
}
