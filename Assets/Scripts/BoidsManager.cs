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

    [Header("Water ceiling")]
    [Tooltip("Keep the school below the water surface.")]
    public bool keepBelowWater = true;
    [Tooltip("World Y of the water surface. Auto-filled from the Water Surface below if assigned.")]
    public float waterLevel = 0f;
    [Tooltip("Optional HDRP Water Surface; if set, Water Level follows its height each frame.")]
    [SerializeField] WaterSurface waterSurface;
    [Tooltip("How far below the surface the fish should stay.")]
    public float waterMargin = 0.5f;

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
    NativeParallelMultiHashMap<int, int> _grid;
    TransformAccessArray _tArray;
    NativeArray<float3> _influencers;                  // player positions the flock reacts to
    int _count;
    bool _dirty = true;

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

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    private void Start()
    {
        instance = this;

        allFish = new GameObject[fishCount];
        for (int i = 0; i < fishCount; i++)
        {
            allFish[i] = Instantiate(prefab,
                transform.position + new Vector3(Random.Range(-area.x, area.x),
                            Random.Range(-area.y, area.y),
                            Random.Range(-area.z, area.z)) / 2, Random.rotation);

            // Manager drives flock fish via the job, so the per-fish Boid Update is off here.
            if (allFish[i].TryGetComponent(out Boid boid)) { boid.SetManager(this); boid.enabled = false; }
        }
        _dirty = true;

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

        if (Random.Range(0, 100) < 10)
            goalPos = transform.position + new Vector3(Random.Range(-area.x, area.x),
                            Random.Range(-area.y, area.y), Random.Range(-area.z, area.z));

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

        float cellSize = Mathf.Max(0.01f, neighbourDist);

        // Build the spatial grid (main thread, cheap).
        _grid.Clear();
        for (int i = 0; i < _count; i++)
            _grid.Add(Hash(CellOf(_posA[i], cellSize)), i);

        // Gather player influencer positions so the flock reacts to them.
        int infl = 0;
        for (int i = 0; i < _influencerTransforms.Count && infl < MaxInfluencers; i++)
            if (_influencerTransforms[i] != null) _influencers[infl++] = (float3)_influencerTransforms[i].position;

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

        // Steering + integration (Burst, parallel, writes transforms).
        var job = new SteerJob
        {
            positions = _posA, headings = _hdgA,
            grid = _grid, hits = _hits, influencers = _influencers, influencerCount = infl,
            dt = Time.deltaTime, cellSize = cellSize,
            minSpeed = minSpeed, maxSpeed = maxSpeed, speedMatch = speedMatch,
            neighbourDist = neighbourDist, separationDist = separationDist, rotationSpeed = rotationSpeed,
            alignmentWeight = alignmentWeight, cohesionWeight = cohesionWeight, separationWeight = separationWeight,
            goalWeight = goalWeight, boundaryWeight = boundaryWeight,
            avoidWeight = avoidWeight, avoidDistance = avoidDistance,
            goalPos = goalPos, areaCenter = transform.position, areaHalf = (float3)area * 0.5f,
            keepBelow = keepBelowWater, ceiling = _ceiling,
            outPositions = _posB, outHeadings = _hdgB,
        };
        job.Schedule(_tArray, castDep).Complete();

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

        _posA = new NativeArray<float3>(n, Allocator.Persistent);
        _posB = new NativeArray<float3>(n, Allocator.Persistent);
        _hdgA = new NativeArray<float3>(n, Allocator.Persistent);
        _hdgB = new NativeArray<float3>(n, Allocator.Persistent);
        _commands = new NativeArray<RaycastCommand>(math.max(1, n), Allocator.Persistent);
        _hits = new NativeArray<RaycastHit>(math.max(1, n), Allocator.Persistent);
        _grid = new NativeParallelMultiHashMap<int, int>(math.max(1, n), Allocator.Persistent);
        _influencers = new NativeArray<float3>(MaxInfluencers, Allocator.Persistent);
        _tArray = new TransformAccessArray(n);

        int k = 0;
        for (int i = 0; i < allFish.Length; i++)
        {
            var f = allFish[i];
            if (f == null) continue;
            _tArray.Add(f.transform);
            _posA[k] = (float3)f.transform.position;
            _hdgA[k] = (float3)f.transform.forward;
            k++;
        }
        _dirty = false;
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
        if (_grid.IsCreated) _grid.Dispose();
        if (_influencers.IsCreated) _influencers.Dispose();
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

        public float dt, cellSize, neighbourDist, separationDist, rotationSpeed;
        public float alignmentWeight, cohesionWeight, separationWeight, goalWeight, boundaryWeight, avoidWeight, avoidDistance;
        public float minSpeed, maxSpeed, speedMatch;
        public float3 goalPos, areaCenter, areaHalf;
        public bool keepBelow;
        public float ceiling;

        [WriteOnly] public NativeArray<float3> outPositions;
        [WriteOnly] public NativeArray<float3> outHeadings;

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
            float3 newPos = pos + newFwd * (effSpeed * dt);

            // Hard clamp so a fish can never cross the surface even if steering lags.
            if (keepBelow && newPos.y > ceiling) newPos.y = ceiling;

            outPositions[index] = newPos;
            outHeadings[index] = newFwd;
            t.position = newPos;
            t.rotation = quaternion.LookRotationSafe(newFwd, math.up());
        }
    }
}
