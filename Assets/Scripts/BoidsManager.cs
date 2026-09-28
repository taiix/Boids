using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Jobs;
using UnityEngine.Rendering.HighDefinition;
using Random = UnityEngine.Random;   // disambiguate from Unity.Mathematics.Random

// Burst/Jobs flock: the spawned fish are simulated in parallel (spatial-grid steering +
// batched obstacle casts) and their transforms are written by an IJobParallelForTransform.
// The player blends in on a lightweight managed path (see Boid + FishFlockBlend) reading the
// snapshot below, and registers itself as an "influencer" so the school reacts to it.
[DefaultExecutionOrder(-50)]
public class BoidsManager : MonoBehaviour
{
    public static BoidsManager instance { get; private set; }

    /// <summary>Every active flock, so things like the player's blend can find the nearest one.</summary>
    public static readonly List<BoidsManager> All = new List<BoidsManager>();

    /// <summary>Predators (sharks) the schools flee from. A Predator component registers here.</summary>
    public static readonly List<Transform> Predators = new List<Transform>();

    public Vector3 area;
    [SerializeField] private GameObject prefab;
    [SerializeField] private int fishCount;

    public GameObject[] allFish;

    [Header("Boid Settings")]
    [Range(0f, 5f)] public float minSpeed;
    [Range(0f, 5f)] public float maxSpeed;
    [Tooltip("Perception radius: fish only school with others within this distance.")]
    [Range(0f, 10f)] public float neighbourDist;
    [Range(1f, 8f)] public float rotationSpeed;

    [Header("Schooling weights")]
    public float separationDist = 1.2f;
    public float separationWeight = 1.6f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 0.9f;
    public float goalWeight = 0.35f;
    public float boundaryWeight = 2.0f;
    [Tooltip("How strongly fish speed up to catch the group / slow down when ahead, so the school " +
             "doesn't split into fast/slow clumps. 0 = fixed speeds (old behaviour).")]
    public float speedMatch = 1.5f;

    [Header("Obstacle avoidance")]
    public LayerMask obstacleMask = ~0;
    public float avoidDistance = 2.5f;
    public float avoidRadius = 0.4f;
    public float avoidWeight = 2.0f;

    [Header("Predator avoidance")]
    [Tooltip("Fish flee from a predator (shark) within this distance.")]
    public float fleeRadius = 5f;
    [Tooltip("How hard fish flee. High so panic overrides schooling and the group parts around the shark.")]
    public float fleeWeight = 6f;
    [Tooltip("Top speed while fleeing (panic burst) - usually higher than Max Speed.")]
    public float fleeSpeed = 9f;

    [Header("Water ceiling")]
    [Tooltip("Keep the school below the water surface.")]
    public bool keepBelowWater = true;
    [Tooltip("World Y of the water surface. Auto-filled from the Water Surface below if assigned.")]
    public float waterLevel = 0f;
    [Tooltip("Optional HDRP Water Surface; if set, Water Level follows its height each frame.")]
    [SerializeField] WaterSurface waterSurface;
    [Tooltip("How far below the surface the fish should stay.")]
    public float waterMargin = 0.5f;

    [Header("Seabed floor")]
    [Tooltip("Keep the school above the seabed by probing straight down and steering up off it.")]
    public bool keepAboveFloor = true;
    [Tooltip("Layers treated as the seabed for the downward probe (set to Terrain).")]
    public LayerMask floorMask = 1 << 3; // Terrain
    [Tooltip("World Y to cast the seabed probe from. Must be above the seabed (e.g. the water level ~0).")]
    public float floorCastHeight = 2f;
    [Tooltip("Max downward probe length from the cast height.")]
    public float floorProbe = 160f;
    [Tooltip("Start steering up when within this distance above the seabed.")]
    public float floorMargin = 4f;
    [Tooltip("Hard minimum clearance kept above the seabed - fish never get closer than this.")]
    public float floorClearance = 1.5f;
    [Tooltip("How hard fish steer up off the seabed.")]
    public float floorWeight = 3.5f;

    [Header("Network sync")]
    [Tooltip("Seed for the starting layout and the roaming goal (mixed with this school's position, so " +
             "several schools in a scene differ). Every player gets the identical school from it.")]
    public uint spawnSeed = 12345;
    [Tooltip("Seconds between changes of the school's roaming goal. Picked from the shared network clock, " +
             "so every player's school heads for the same spot.")]
    public float goalChangeInterval = 0.25f;
    [Tooltip("Seconds a correction from the host takes to blend in (clients only).")]
    public float correctionTime = 0.3f;
    [Tooltip("Corrections larger than this snap instead of blending (metres).")]
    public float snapDistance = 6f;

    /// <summary>Clock every player agrees on (set to Mirror's NetworkTime by FlockNetSync while
    /// networked). Offline it's local time.</summary>
    public static System.Func<double> SharedClock = () => Time.timeAsDouble;

    /// <summary>This school's id on the network: its rank among the loaded schools, ordered by position.
    /// Every player loads the same scene, so ranks agree without anyone numbering the schools.</summary>
    public byte NetId
    {
        get
        {
            int rank = 0;
            for (int m = 0; m < All.Count; m++)
                if (All[m] != null && All[m] != this && SortsBefore(All[m], this)) rank++;
            return (byte)rank;
        }
    }

    static bool SortsBefore(BoidsManager a, BoidsManager b)
    {
        Vector3 pa = a.transform.position, pb = b.transform.position;
        if (pa.x != pb.x) return pa.x < pb.x;
        if (pa.y != pb.y) return pa.y < pb.y;
        if (pa.z != pb.z) return pa.z < pb.z;
        return string.CompareOrdinal(a.name, b.name) < 0;
    }

    // Seed for this school's layout and roaming: the shared seed mixed with where it sits.
    uint FlockSeed
    {
        get
        {
            uint h = spawnSeed ^ math.hash((float3)transform.position);
            return h == 0 ? 1u : h;
        }
    }

    float _ceiling;

    /// <summary>World Y the fish must stay below (or +inf if the ceiling is off).</summary>
    public float WaterCeiling => keepBelowWater ? (waterLevel - waterMargin) : float.MaxValue;

    public Vector3 goalPos;

    public UnityEvent<float> OnMinSpeedChanged = new UnityEvent<float>();
    public UnityEvent<float> OnMaxSpeedChanged = new UnityEvent<float>();
    public UnityEvent<float> OnNeighbourDistChanged = new UnityEvent<float>();
    public UnityEvent<float> OnRotationSpeedChanged = new UnityEvent<float>();

    private Vector3 previousMinSpeed, previousMaxSpeed, previousNeighbourDist, previousRotationSpeed;

    // ---- Job/Burst simulation state -------------------------------------------------
    NativeArray<float3> _posA, _posB, _hdgA, _hdgB;   // double-buffered position + heading
    NativeArray<RaycastCommand> _commands;            // raycasts hit concave mesh colliders (sphere-casts don't)
    NativeArray<RaycastHit> _hits;
    NativeArray<RaycastCommand> _floorCommands;       // downward casts to find the seabed under each fish
    NativeArray<RaycastHit> _floorHits;
    NativeParallelMultiHashMap<int, int> _grid;
    TransformAccessArray _tArray;
    NativeArray<float3> _influencers;                  // player positions the flock reacts to
    NativeArray<float3> _predators;                    // shark positions the flock flees from
    NativeArray<float> _speed;                         // current speed per sim slot (written by the job)
    int _count;
    bool _dirty = true;

    // Fish ids are stable: a fish's id is its index in allFish for the whole match, and a dead fish
    // just leaves a null there. The sim packs live fish into slots, so map both ways.
    int[] _ids = System.Array.Empty<int>();   // sim slot -> fish id
    int[] _slot = System.Array.Empty<int>();  // fish id -> sim slot (-1 = dead)

    // Host corrections (clients only), per fish id.
    Vector3[] _corr = System.Array.Empty<Vector3>();     // position error still to blend in
    Vector3[] _netPos = System.Array.Empty<Vector3>();   // latest host state, extrapolated to arrival
    Vector3[] _netHdg = System.Array.Empty<Vector3>();
    bool[] _netPending = System.Array.Empty<bool>();
    bool _netDirty, _hasNetData;

    // Managed snapshot the player's (managed) Boid reads to school with this flock.
    Vector3[] _snapPos = System.Array.Empty<Vector3>();
    Vector3[] _snapFwd = System.Array.Empty<Vector3>();
    int _snapCount;
    public Vector3[] SnapshotPositions => _snapPos;
    public Vector3[] SnapshotForwards => _snapFwd;
    public int SnapshotCount => _snapCount;

    // Players currently blended into this flock (usually 0-1 locally).
    readonly List<Transform> _influencerTransforms = new List<Transform>();
    public void RegisterInfluencer(Transform t) { if (t != null && !_influencerTransforms.Contains(t)) _influencerTransforms.Add(t); }
    public void UnregisterInfluencer(Transform t) { _influencerTransforms.Remove(t); }

    const int MaxInfluencers = 16;
    const int MaxPredators = 16;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    // Spawns in Awake (not Start) so the school exists before a joining client reports ready: the
    // host's full-state message would otherwise find no fish to apply to.
    private void Awake()
    {
        instance = this;

        // Seeded, so every player spawns the identical school (fish id i starts at the same spot).
        var rng = new Unity.Mathematics.Random(FlockSeed);
        allFish = new GameObject[fishCount];
        for (int i = 0; i < fishCount; i++)
        {
            float3 offset = rng.NextFloat3(-(float3)area, (float3)area) * 0.5f;
            allFish[i] = Instantiate(prefab, transform.position + (Vector3)offset, rng.NextQuaternionRotation());

            // Manager drives flock fish via the job, so the per-fish Boid Update is off here.
            if (allFish[i].TryGetComponent(out Boid boid)) { boid.SetManager(this); boid.enabled = false; }
        }
        _dirty = true;
    }

    private void Start()
    {
        previousMinSpeed = new Vector3(minSpeed, 0, 0);
        previousMaxSpeed = new Vector3(maxSpeed, 0, 0);
        previousNeighbourDist = new Vector3(neighbourDist, 0, 0);
        previousRotationSpeed = new Vector3(rotationSpeed, 0, 0);
    }

    private void Update()
    {
        // --- UI change events (unchanged) ---------------------------------------
        if (minSpeed != previousMinSpeed.x) { OnMinSpeedChanged.Invoke(minSpeed); previousMinSpeed.x = minSpeed; }
        if (maxSpeed != previousMaxSpeed.x) { OnMaxSpeedChanged.Invoke(maxSpeed); previousMaxSpeed.x = maxSpeed; }
        if (neighbourDist != previousNeighbourDist.x) { OnNeighbourDistChanged.Invoke(neighbourDist); previousNeighbourDist.x = neighbourDist; }
        if (rotationSpeed != previousRotationSpeed.x) { OnRotationSpeedChanged.Invoke(rotationSpeed); previousRotationSpeed.x = rotationSpeed; }

        // Water ceiling (flat surface level is enough for a school).
        if (waterSurface != null) waterLevel = waterSurface.transform.position.y;
        _ceiling = waterLevel - waterMargin;

        goalPos = GoalAt(SharedClock());

        // Don't let the roaming goal sit above water, or the fish chase it upward.
        if (keepBelowWater) goalPos.y = Mathf.Min(goalPos.y, _ceiling);
    }

    // Sim runs in LateUpdate (after the animation phase) so the transform-writing job never
    // races the Animator/render. It still parallelizes across all cores via Burst.
    private void LateUpdate()
    {
        if (allFish == null || allFish.Length == 0) return;
        EnsureArrays();
        if (_count == 0) return;

        ApplyHostCorrections(Time.deltaTime);

        float cellSize = Mathf.Max(0.01f, neighbourDist);

        // Build the spatial grid (main thread, cheap).
        _grid.Clear();
        for (int i = 0; i < _count; i++)
            _grid.Add(Hash(CellOf(_posA[i], cellSize)), i);

        // Gather player influencer positions so the flock reacts to them.
        int infl = 0;
        for (int i = 0; i < _influencerTransforms.Count && infl < MaxInfluencers; i++)
            if (_influencerTransforms[i] != null) _influencers[infl++] = (float3)_influencerTransforms[i].position;

        // Gather predator (shark) positions the flock flees from.
        int pred = 0;
        for (int i = 0; i < Predators.Count && pred < MaxPredators; i++)
            if (Predators[i] != null) _predators[pred++] = (float3)Predators[i].position;

        // Batched obstacle sphere-casts (job).
        JobHandle castDep = default;
        if (avoidWeight > 0f)
        {
            var qp = new QueryParameters(obstacleMask, false, QueryTriggerInteraction.Ignore, false);
            float lookAhead = avoidDistance + maxSpeed * 0.5f;
            for (int i = 0; i < _count; i++)
                _commands[i] = new RaycastCommand(_posA[i], _hdgA[i], qp, lookAhead);
            castDep = RaycastCommand.ScheduleBatch(_commands, _hits, 32, default);
        }

        // Downward seabed casts (from a fixed height above the terrain): find the ground under
        // each fish so it can steer up off it and follow the contours instead of sinking through.
        JobHandle floorDep = default;
        if (keepAboveFloor && floorWeight > 0f)
        {
            var fqp = new QueryParameters(floorMask, false, QueryTriggerInteraction.Ignore, false);
            for (int i = 0; i < _count; i++)
                _floorCommands[i] = new RaycastCommand(new float3(_posA[i].x, floorCastHeight, _posA[i].z), new float3(0f, -1f, 0f), fqp, floorProbe);
            floorDep = RaycastCommand.ScheduleBatch(_floorCommands, _floorHits, 32, default);
        }

        // Steering + integration (Burst, parallel, writes transforms).
        var job = new SteerJob
        {
            positions = _posA, headings = _hdgA,
            grid = _grid, hits = _hits, influencers = _influencers, influencerCount = infl,
            predators = _predators, predatorCount = pred, fleeRadius = fleeRadius, fleeWeight = fleeWeight, fleeSpeed = fleeSpeed,
            dt = Time.deltaTime, cellSize = cellSize,
            minSpeed = minSpeed, maxSpeed = maxSpeed, speedMatch = speedMatch,
            neighbourDist = neighbourDist, separationDist = separationDist, rotationSpeed = rotationSpeed,
            alignmentWeight = alignmentWeight, cohesionWeight = cohesionWeight, separationWeight = separationWeight,
            goalWeight = goalWeight, boundaryWeight = boundaryWeight,
            avoidWeight = avoidWeight, avoidDistance = avoidDistance,
            goalPos = goalPos, areaCenter = transform.position, areaHalf = (float3)area * 0.5f,
            keepBelow = keepBelowWater, ceiling = _ceiling,
            floorHits = _floorHits, keepAboveFloor = keepAboveFloor, floorCastHeight = floorCastHeight,
            floorMargin = floorMargin, floorClearance = floorClearance, floorWeight = floorWeight,
            outPositions = _posB, outHeadings = _hdgB, outSpeeds = _speed,
        };
        job.Schedule(_tArray, JobHandle.CombineDependencies(castDep, floorDep)).Complete();

        // Swap read/write buffers (this frame's output becomes next frame's input).
        (_posA, _posB) = (_posB, _posA);
        (_hdgA, _hdgB) = (_hdgB, _hdgA);

        // Snapshot for the player's managed steering (only when someone is blending here).
        if (_influencerTransforms.Count > 0)
        {
            _snapCount = _count;
            if (_snapPos.Length < _count) { _snapPos = new Vector3[_count]; _snapFwd = new Vector3[_count]; }
            for (int i = 0; i < _count; i++) { _snapPos[i] = _posA[i]; _snapFwd[i] = _hdgA[i]; }
        }
        else _snapCount = 0;
    }

    void EnsureArrays()
    {
        // Count non-null spawned fish.
        int n = 0;
        for (int i = 0; i < allFish.Length; i++) if (allFish[i] != null) n++;
        if (!_dirty && n == _count && _tArray.isCreated) return;

        DisposeNative();
        _count = n;
        EnsurePerFish();

        _posA = new NativeArray<float3>(n, Allocator.Persistent);
        _posB = new NativeArray<float3>(n, Allocator.Persistent);
        _hdgA = new NativeArray<float3>(n, Allocator.Persistent);
        _hdgB = new NativeArray<float3>(n, Allocator.Persistent);
        _commands = new NativeArray<RaycastCommand>(math.max(1, n), Allocator.Persistent);
        _hits = new NativeArray<RaycastHit>(math.max(1, n), Allocator.Persistent);
        _floorCommands = new NativeArray<RaycastCommand>(math.max(1, n), Allocator.Persistent);
        _floorHits = new NativeArray<RaycastHit>(math.max(1, n), Allocator.Persistent);
        _grid = new NativeParallelMultiHashMap<int, int>(math.max(1, n), Allocator.Persistent);
        _influencers = new NativeArray<float3>(MaxInfluencers, Allocator.Persistent);
        _predators = new NativeArray<float3>(MaxPredators, Allocator.Persistent);
        _speed = new NativeArray<float>(math.max(1, n), Allocator.Persistent);
        _tArray = new TransformAccessArray(n);

        _ids = new int[n];
        for (int i = 0; i < _slot.Length; i++) _slot[i] = -1;

        int k = 0;
        for (int i = 0; i < allFish.Length; i++)
        {
            var f = allFish[i];
            if (f == null) continue;
            _tArray.Add(f.transform);
            _posA[k] = (float3)f.transform.position;
            _hdgA[k] = (float3)f.transform.forward;
            _ids[k] = i;
            _slot[i] = k;
            k++;
        }
        _dirty = false;
    }

    // Per-fish-id arrays follow allFish's length (it only changes via the debug Add/Remove buttons).
    void EnsurePerFish()
    {
        int len = allFish.Length;
        if (_slot.Length == len) return;
        System.Array.Resize(ref _slot, len);
        System.Array.Resize(ref _corr, len);
        System.Array.Resize(ref _netPos, len);
        System.Array.Resize(ref _netHdg, len);
        System.Array.Resize(ref _netPending, len);
    }

    void DisposeNative()
    {
        // Sim completes synchronously each LateUpdate, so no job is ever in flight here.
        if (_posA.IsCreated) _posA.Dispose();
        if (_posB.IsCreated) _posB.Dispose();
        if (_hdgA.IsCreated) _hdgA.Dispose();
        if (_hdgB.IsCreated) _hdgB.Dispose();
        if (_commands.IsCreated) _commands.Dispose();
        if (_hits.IsCreated) _hits.Dispose();
        if (_floorCommands.IsCreated) _floorCommands.Dispose();
        if (_floorHits.IsCreated) _floorHits.Dispose();
        if (_grid.IsCreated) _grid.Dispose();
        if (_influencers.IsCreated) _influencers.Dispose();
        if (_predators.IsCreated) _predators.Dispose();
        if (_speed.IsCreated) _speed.Dispose();
        if (_tArray.isCreated) _tArray.Dispose();
    }

    private void OnDestroy() => DisposeNative();

    static int3 CellOf(float3 p, float cellSize) => (int3)math.floor(p / cellSize);
    static int Hash(int3 c) { unchecked { return (c.x * 73856093) ^ (c.y * 19349663) ^ (c.z * 83492791); } }

    // ---- Public API kept for compatibility -----------------------------------------
    public void AddFish()
    {
        GameObject newFish = Instantiate(prefab,
            transform.position + new Vector3(Random.Range(-area.x, area.x),
                        Random.Range(-area.y, area.y), Random.Range(-area.z, area.z)) / 2, Random.rotation);
        if (newFish.TryGetComponent(out Boid boid)) { boid.SetManager(this); boid.enabled = false; }
        System.Array.Resize(ref allFish, allFish.Length + 1);
        allFish[allFish.Length - 1] = newFish;
        _dirty = true;
    }

    public void RemoveFish()
    {
        if (allFish.Length > 0)
        {
            Destroy(allFish[allFish.Length - 1]);
            System.Array.Resize(ref allFish, allFish.Length - 1);
            _dirty = true;
        }
    }

    /// <summary>Pull fish <paramref name="index"/> out of the flock (so the sim stops driving it) and
    /// return it, or null if it's already gone. The slot is left empty rather than closed up, so every
    /// other fish keeps its id - ids must match on all players for network sync.</summary>
    public GameObject RemoveAt(int index)
    {
        if (allFish == null || index < 0 || index >= allFish.Length) return null;
        var fish = allFish[index];
        if (fish == null) return null;
        allFish[index] = null;
        _dirty = true; // rebuild native arrays / TransformAccessArray next sim step
        return fish;
    }

    /// <summary>Find the nearest flock fish (across ALL flocks) within <paramref name="radius"/>
    /// of <paramref name="pos"/>, remove it from its flock, and return it — or null if none.
    /// Used by the shark to eat NPC fish, which have no colliders.</summary>
    public static GameObject EatNearestFish(Vector3 pos, float radius) =>
        EatNearestFish(pos, radius, out _, out _);

    /// <inheritdoc cref="EatNearestFish(Vector3, float)"/>
    /// <param name="flock">The school it came from.</param>
    /// <param name="id">Its fish id in that school (for telling the network which fish).</param>
    public static GameObject EatNearestFish(Vector3 pos, float radius, out BoidsManager flock, out int id)
    {
        BoidsManager bestMgr = null;
        int bestIdx = -1;
        float best = radius * radius;

        for (int m = 0; m < All.Count; m++)
        {
            var mgr = All[m];
            if (mgr == null || mgr.allFish == null) continue;
            var a = mgr.allFish;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null) continue;
                float d2 = (a[i].transform.position - pos).sqrMagnitude;
                if (d2 < best) { best = d2; bestMgr = mgr; bestIdx = i; }
            }
        }

        flock = bestMgr;
        id = bestIdx;
        return bestMgr != null ? bestMgr.RemoveAt(bestIdx) : null;
    }

    /// <summary>Pull one specific fish out of whichever flock holds it (so the sim stops driving it).
    /// Returns true if it was found. Used by the shark's focused bite to eat a *locked* flock fish
    /// rather than just the nearest one.</summary>
    public static bool DetachFish(GameObject fish) => DetachFish(fish, out _, out _);

    /// <inheritdoc cref="DetachFish(GameObject)"/>
    public static bool DetachFish(GameObject fish, out BoidsManager flock, out int id)
    {
        if (!TryFind(fish, out flock, out id)) return false;
        flock.RemoveAt(id);
        return true;
    }

    /// <summary>Which school a live flock fish belongs to, and its fish id there.</summary>
    public static bool TryFind(GameObject fish, out BoidsManager flock, out int id)
    {
        flock = null;
        id = -1;
        if (fish == null) return false;
        for (int m = 0; m < All.Count; m++)
        {
            var mgr = All[m];
            if (mgr == null || mgr.allFish == null) continue;
            int i = System.Array.IndexOf(mgr.allFish, fish);
            if (i >= 0) { flock = mgr; id = i; return true; }
        }
        return false;
    }

    /// <summary>The school with this network id (<see cref="NetId"/>), or null.</summary>
    public static BoidsManager ById(byte netId)
    {
        for (int m = 0; m < All.Count; m++)
            if (All[m] != null && All[m].NetId == netId) return All[m];
        return null;
    }

    /// <summary>The school whose area centre is closest to <paramref name="pos"/>, or null.</summary>
    public static BoidsManager NearestTo(Vector3 pos)
    {
        BoidsManager best = null;
        float bestSqr = float.MaxValue;
        for (int m = 0; m < All.Count; m++)
        {
            var mgr = All[m];
            if (mgr == null) continue;
            float d = (mgr.transform.position - pos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = mgr; }
        }
        return best;
    }

    // ---- Network sync (see FlockNetSync) ------------------------------------------------

    /// <summary>Number of fish ids (alive or not).</summary>
    public int FishCapacity => allFish != null ? allFish.Length : 0;

    public bool IsAlive(int id) => allFish != null && id >= 0 && id < allFish.Length && allFish[id] != null;

    /// <summary>Host: the current simulated state of a live fish.</summary>
    public bool TryGetState(int id, out Vector3 pos, out Vector3 heading, out float speed)
    {
        int k = IsAlive(id) && id < _slot.Length ? _slot[id] : -1;
        if (k < 0 || k >= _count || !_posA.IsCreated)
        {
            pos = heading = default;
            speed = 0f;
            return false;
        }
        pos = _posA[k];
        heading = _hdgA[k];
        speed = _speed[k];
        return true;
    }

    /// <summary>Client: the host's state for a fish, already extrapolated to arrival time. Eased in over
    /// <see cref="correctionTime"/> on the next sim step (or snapped if it's way off).</summary>
    public void ReceiveHostState(int id, Vector3 pos, Vector3 heading)
    {
        if (!IsAlive(id)) return;
        EnsurePerFish();
        _netPos[id] = pos;
        _netHdg[id] = heading;
        _netPending[id] = true;
        _netDirty = _hasNetData = true;
    }

    /// <summary>Remove a fish without any eating animation (it was already gone on the host).</summary>
    public void DespawnFish(int id)
    {
        var fish = RemoveAt(id);
        if (fish != null) Destroy(fish);
    }

    // Where the school roams. Derived from the shared clock + seed rather than rolled at random each
    // frame, so every player's school chases the same goal at the same moment without sending it.
    Vector3 GoalAt(double time)
    {
        long step = (long)System.Math.Floor(time / Mathf.Max(0.01f, goalChangeInterval));
        uint h = math.hash(new uint3((uint)step, (uint)(step >> 32), FlockSeed));
        var rng = new Unity.Mathematics.Random(h == 0 ? 1u : h);
        return transform.position + (Vector3)rng.NextFloat3(-(float3)area, (float3)area);
    }

    // Clients: fold the host's latest states into the sim, then ease outstanding errors in.
    void ApplyHostCorrections(float dt)
    {
        if (!_hasNetData) return;

        if (_netDirty)
        {
            _netDirty = false;
            float snap2 = snapDistance * snapDistance;
            for (int id = 0; id < _netPending.Length; id++)
            {
                if (!_netPending[id]) continue;
                _netPending[id] = false;
                int k = _slot[id];
                if (k < 0) continue;

                float3 target = _netPos[id];
                float3 err = target - _posA[k];
                if (math.lengthsq(err) > snap2)
                {
                    _posA[k] = target;                     // way off (e.g. just joined): snap
                    _hdgA[k] = _netHdg[id];
                    _corr[id] = Vector3.zero;
                }
                else
                {
                    _corr[id] = err;                       // blend the rest in over correctionTime
                    _hdgA[k] = math.normalizesafe(math.lerp(_hdgA[k], (float3)_netHdg[id], 0.5f), _hdgA[k]);
                }
            }
        }

        float a = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, correctionTime));
        for (int k = 0; k < _count; k++)
        {
            int id = _ids[k];
            Vector3 c = _corr[id];
            if (c.sqrMagnitude < 1e-8f) continue;
            Vector3 step = c * a;
            _posA[k] += (float3)step;
            _corr[id] = c - step;
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, area);
    }

    // =================================================================================
    [BurstCompile]
    struct SteerJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float3> headings;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> grid;
        [ReadOnly] public NativeArray<RaycastHit> hits;
        [ReadOnly] public NativeArray<float3> influencers;
        public int influencerCount;
        [ReadOnly] public NativeArray<float3> predators;
        public int predatorCount;
        public float fleeRadius, fleeWeight, fleeSpeed;


        public float dt, cellSize, neighbourDist, separationDist, rotationSpeed;
        public float alignmentWeight, cohesionWeight, separationWeight, goalWeight, boundaryWeight, avoidWeight, avoidDistance;
        public float minSpeed, maxSpeed, speedMatch;
        public float3 goalPos, areaCenter, areaHalf;
        public bool keepBelow;
        public float ceiling;

        [ReadOnly] public NativeArray<RaycastHit> floorHits;
        public bool keepAboveFloor;
        public float floorCastHeight, floorMargin, floorClearance, floorWeight;

        [WriteOnly] public NativeArray<float3> outPositions;
        [WriteOnly] public NativeArray<float3> outHeadings;
        [WriteOnly] public NativeArray<float> outSpeeds;

        public void Execute(int index, TransformAccess t)
        {
            float3 pos = positions[index];
            float3 fwd = headings[index];
            float nd2 = neighbourDist * neighbourDist;
            float sep2 = separationDist * separationDist;

            float3 align = 0, center = 0, separation = 0;
            int n = 0, sc = 0;

            int3 cell = (int3)math.floor(pos / cellSize);
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        int h = (((cell.x + x) * 73856093) ^ ((cell.y + y) * 19349663) ^ ((cell.z + z) * 83492791));
                        if (grid.TryGetFirstValue(h, out int j, out var it))
                        {
                            do
                            {
                                if (j == index) continue;
                                float3 off = positions[j] - pos;
                                float d2 = math.lengthsq(off);
                                if (d2 > nd2 || d2 < 1e-6f) continue;
                                align += headings[j];
                                center += positions[j];
                                n++;
                                if (d2 < sep2) { float d = math.sqrt(d2); separation -= (off / d) * (1f - d / separationDist); sc++; }
                            } while (grid.TryGetNextValue(out j, ref it));
                        }
                    }

            for (int p = 0; p < influencerCount; p++)
            {
                float3 off = influencers[p] - pos;
                float d2 = math.lengthsq(off);
                if (d2 <= nd2 && d2 > 1e-6f)
                {
                    center += influencers[p]; n++;
                    if (d2 < sep2) { float d = math.sqrt(d2); separation -= (off / d) * (1f - d / separationDist); sc++; }
                }
            }

            float3 steer = 0;
            float3 toLocalCenter = 0;
            if (n > 0)
            {
                float3 a = align / n;
                if (math.lengthsq(a) > 1e-6f) steer += math.normalize(a) * alignmentWeight;
                toLocalCenter = center / n - pos;
                if (math.lengthsq(toLocalCenter) > 1e-6f) steer += math.normalize(toLocalCenter) * cohesionWeight;
            }
            if (sc > 0 && math.lengthsq(separation) > 1e-6f) steer += math.normalize(separation) * separationWeight;

            float3 toGoal = goalPos - pos;
            if (math.lengthsq(toGoal) > 1e-4f) steer += math.normalize(toGoal) * goalWeight;

            // Flee predators (sharks): a strong radial escape that overrides schooling, so the
            // group parts/splits around the shark and rejoins (via cohesion) once it passes.
            bool fleeing = false;
            float fr2 = fleeRadius * fleeRadius;
            for (int p = 0; p < predatorCount; p++)
            {
                float3 away = pos - predators[p];
                float d2 = math.lengthsq(away);
                if (d2 < fr2 && d2 > 1e-6f)
                {
                    float d = math.sqrt(d2);
                    steer += math.normalize(away) * (fleeWeight * (1f - d / fleeRadius)); // closer = flee harder
                    fleeing = true;
                }
            }

            float3 d2c = pos - areaCenter;
            if (math.abs(d2c.x) > areaHalf.x || math.abs(d2c.y) > areaHalf.y || math.abs(d2c.z) > areaHalf.z)
            {
                float3 toCenter = areaCenter - pos;
                if (math.lengthsq(toCenter) > 1e-6f) steer += math.normalize(toCenter) * boundaryWeight;
            }

            if (avoidWeight > 0f)
            {
                RaycastHit hit = hits[index];
                float3 nrm = hit.normal;
                if (math.lengthsq(nrm) > 1e-4f)
                {
                    float3 along = fwd - math.dot(fwd, nrm) * nrm; // slide along the surface
                    if (math.lengthsq(along) < 1e-4f) along = nrm;
                    along = math.normalize(along) + nrm * 0.5f;    // and bias away from it
                    float strength = 1f - hit.distance / (avoidDistance + maxSpeed * 0.5f);
                    steer += math.normalize(along) * (avoidWeight * (1f + 6f * strength * strength));
                }
            }

            // Water ceiling: steer down as we approach the surface so the school dives back.
            if (keepBelow)
            {
                float over = pos.y - (ceiling - 1.5f);
                if (over > 0f)
                    steer += new float3(0f, -1f, 0f) * (boundaryWeight * 1.5f * math.saturate(over / 1.5f));
            }

            // Seabed floor: a probe cast straight down from floorCastHeight finds the ground under
            // this fish; steer up as we approach it so the school follows the terrain instead of
            // sinking through it.
            float groundY = float.NegativeInfinity;
            bool hasFloor = false;
            if (keepAboveFloor)
            {
                float3 fnrm = floorHits[index].normal;
                if (math.lengthsq(fnrm) > 1e-4f)   // a valid downward hit found the seabed
                {
                    hasFloor = true;
                    groundY = floorCastHeight - floorHits[index].distance;
                    float clearance = pos.y - groundY;
                    if (clearance < floorMargin)
                    {
                        float t01 = math.saturate(1f - clearance / floorMargin);
                        steer += new float3(0f, 1f, 0f) * (floorWeight * (1f + 4f * t01 * t01));
                    }
                }
            }

            float3 newFwd = fwd;
            if (math.lengthsq(steer) > 1e-6f)
            {
                float tt = math.saturate(rotationSpeed * dt);
                newFwd = math.normalize(math.lerp(fwd, math.normalize(steer), tt));
            }

            // Speed matching: lag behind the group's center -> speed up; ahead of it -> slow down.
            // Keeps the school from sorting into fast/slow clumps.
            float baseSpeed = (minSpeed + maxSpeed) * 0.5f;
            float effSpeed = baseSpeed;
            if (n > 0)
            {
                float ahead = math.dot(toLocalCenter, newFwd); // >0 = center is in front (we're behind)
                effSpeed = math.clamp(baseSpeed + ahead * speedMatch, minSpeed, maxSpeed);
            }
            if (fleeing) effSpeed = fleeSpeed; // panic burst (faster than cruise)
            float3 newPos = pos + newFwd * (effSpeed * dt);

            // Hard clamp so a fish can never cross the surface even if steering lags.
            if (keepBelow && newPos.y > ceiling) newPos.y = ceiling;
            // Hard floor: never let a fish sink into the seabed.
            if (hasFloor && newPos.y < groundY + floorClearance) newPos.y = groundY + floorClearance;

            outPositions[index] = newPos;
            outHeadings[index] = newFwd;
            outSpeeds[index] = effSpeed;
            t.position = newPos;
            t.rotation = quaternion.LookRotationSafe(newFwd, math.up());
        }
    }
}
