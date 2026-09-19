using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// Multi-point buoyancy for large vessels.
//
// Probe points along the keel sample the HDRP water surface, but they do not push up
// independently: a single plane is least-squares fitted through them and low-passed, and the
// buoyancy is read off that. Ten independent probes make the hull chase every ripple, which
// reads as stutter on anything longer than the waves. Fitting first means a wave shorter than
// the ship averages out, the way it does for a real hull.
//
// The plane's two slope components are scaled separately, so roll can follow the swell while
// pitch is held level (see rollResponse / pitchResponse).
//
// Requires "Script Interactions" to be enabled on the target Water Surface,
// otherwise ProjectPointOnWaterSurface always fails and the ship just sinks.
// Do not put Unity's sample Buoyancy component on the same object: it assigns linearVelocity
// directly and disables useGravity, which fights this and produces exactly the stutter above.
[RequireComponent(typeof(Rigidbody))]
public class Floater : MonoBehaviour
{
    [Header("Water")]
    [SerializeField] private WaterSurface targetSurface;
    [Tooltip("Include water deformers (wakes, ripples, shore waves) in the height query.")]
    [SerializeField] private bool includeDeformation = true;
    [Tooltip("Raises or lowers the resting waterline without moving the hull pivot. Positive sits the ship deeper.")]
    [SerializeField] private float waterlineOffset = 0f;
    [Tooltip("Enable when the Water Surface geometry type is set to Custom Mesh.")]
    [SerializeField] private bool isWaterSurfaceACustomMesh = false;

    [Header("Wave Response")]
    [Tooltip("Seconds for the fitted water plane to catch up with the real surface. This is the main smoothness control: higher reads as a heavier ship. 0 disables filtering.")]
    [Min(0f)] [SerializeField] private float waveSmoothTime = 0.6f;
    [Tooltip("How much of the sideways water slope rolls the hull. 1 follows the swell, 0 stays level side to side.")]
    [Range(0f, 1f)] [SerializeField] private float rollResponse = 1f;
    [Tooltip("How much of the fore-aft water slope pitches the hull. At 0 waves never pitch it, and buoyancy still springs it back to level if something else does.")]
    [Range(0f, 1f)] [SerializeField] private float pitchResponse = 0f;
    [Tooltip("Sanity limits on the fitted plane: the steepest slope and the largest height deviation from the surface's base level that will be believed. Guards against a height query that converges on nonsense.")]
    [Min(0.01f)] [SerializeField] private float maxWaveSlope = 0.4f;
    [Min(0.1f)] [SerializeField] private float maxWaveHeight = 20f;

    [Header("Buoyancy")]
    [Tooltip("Upward acceleration at full submersion, as a multiple of gravity. The hull settles where submersion equals 1 / this value, so 2 rests at half the draft below.")]
    [Min(1.01f)] [SerializeField] private float buoyancyStrength = 2f;
    [Tooltip("How far a probe must sink below the surface to generate full buoyancy. Roughly the ship's design draft.")]
    [Min(0.01f)] [SerializeField] private float submersionDepth = 4f;
    [Tooltip("Local-space offset for the centre of mass. Lowering it (negative Y) keeps a tall superstructure from rolling over.")]
    [SerializeField] private Vector3 centerOfMassOffset = Vector3.zero;

    [Header("Probes")]
    [Tooltip("Optional explicit probe points. Leave empty to auto-generate a grid along the keel from the hull bounds.")]
    [SerializeField] private List<Transform> probeTransforms = new();
    [Tooltip("Auto-generated probe grid: X along the hull length, Y across the beam.")]
    [SerializeField] private Vector2Int autoProbeGrid = new Vector2Int(5, 2);
    [Tooltip("Pulls auto-generated probes in from the bounds edges, as a fraction of the hull size.")]
    [Range(0f, 0.45f)] [SerializeField] private float autoProbeInset = 0.12f;

    [Header("Damping")]
    [SerializeField] private float waterLinearDamping = 1.5f;
    [SerializeField] private float airLinearDamping = 0.05f;
    [SerializeField] private float waterAngularDamping = 3f;
    [SerializeField] private float airAngularDamping = 0.1f;
    [Tooltip("Per-probe vertical damping, as a fraction of critical damping. 0 bobs forever, 1 settles without overshooting. Scales itself with draft and buoyancy strength, so it stays correct when those change.")]
    [Range(0f, 1.5f)] [SerializeField] private float verticalDampingRatio = 0.45f;
    [Tooltip("Hard ceiling on what one probe may apply, in multiples of gravity. A rotating hull has its submerged probes damped while its airborne ones are not, which turns the damper into net thrust; this bounds that.")]
    [Min(1f)] [SerializeField] private float maxAccelerationG = 4f;

    [Tooltip("Self-righting torque. Probes all sit in the keel plane, which leaves no restoring moment once the hull is past its beam ends — a real hull's shape provides one. This stands in for that. 0 disables.")]
    [Min(0f)] [SerializeField] private float uprightStrength = 2f;

    [Header("Station Keeping")]
    [Tooltip("Holds the ship at its starting spot and heading so it never drifts away.")]
    [SerializeField] private bool holdPosition = true;
    [SerializeField] private float positionSpring = 1.5f;
    [SerializeField] private float headingSpring = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = true;
    [Tooltip("Logs a detailed per-probe trace for this many physics steps after startup, then stops. 0 disables.")]
    [SerializeField] private int traceFrames = 0;

    private Rigidbody rb;
    private Vector3[] probesLocal;
    private WaterSearchResult[] searchResults;
    private bool[] probeHasResult;
    private float[] lastGoodHeight;
    private float[] probeSubmersion;
    private Vector3[] probeWorld;
    private float[] probeWaterRaw;
    private float planeHeight;
    private Vector2 planeGradient;
    private Vector2 effectiveGradient;
    private bool planeInitialized;
    private Vector3 anchorPosition;
    private float anchorYaw;
    private float averageSubmersion;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = true;

        if (centerOfMassOffset != Vector3.zero)
            rb.centerOfMass = centerOfMassOffset;

        BuildProbes();

        anchorPosition = transform.position;
        anchorYaw = transform.eulerAngles.y;

        if (targetSurface == null)
            Debug.LogWarning($"{nameof(Floater)} on '{name}' has no Water Surface assigned; the ship will not float.", this);
    }

    private void FixedUpdate()
    {
        if (targetSurface == null || rb == null || probesLocal == null || probesLocal.Length == 0)
            return;

        Vector3 buoyantAccel = -Physics.gravity * buoyancyStrength;
        float perProbe = 1f / probesLocal.Length;
        float submersionSum = 0f;

        // Buoyancy behaves as a spring of stiffness (buoyancyStrength * g / draft). Deriving the
        // damper from that spring's natural frequency keeps the response identical whether this
        // is a 0.3m raft or a 30m hull.
        float naturalFrequency = Mathf.Sqrt(buoyancyStrength * Mathf.Abs(Physics.gravity.y) / submersionDepth);
        float verticalDamping = 2f * naturalFrequency * verticalDampingRatio;
        float maxProbeAccel = maxAccelerationG * Mathf.Abs(Physics.gravity.y) * perProbe;

        // Pass 1: sample every probe.
        int validProbes = 0;
        for (int i = 0; i < probesLocal.Length; i++)
        {
            probeWorld[i] = transform.TransformPoint(probesLocal[i]);

            if (SampleWaterHeight(i, probeWorld[i], out float waterY))
            {
                probeWaterRaw[i] = waterY;
                validProbes++;
            }
        }

        // The HDRP water CPU simulation needs a few frames after load before it answers height
        // queries. Applying buoyancy to only the probes that already resolve is not a partial
        // result — it is a force couple across the hull, which flips the ship. Wait it out.
        if (validProbes < probesLocal.Length)
        {
            // Hold station against gravity meanwhile. Letting it free-fall for the handful of
            // frames the simulation takes to wake up means buoyancy engages on a hull that is
            // already moving and already below the surface, which is what starts it tumbling.
            rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
            return;
        }

        UpdateWaterPlane();

        // Pass 2: re-derive submersion from the filtered plane rather than the raw samples, so
        // every probe reads off one smooth surface instead of chasing its own patch of water.
        for (int i = 0; i < probesLocal.Length; i++)
        {
            float planeY = SampleWaterPlane(probeWorld[i]);
            float depth = (planeY + waterlineOffset) - probeWorld[i].y;
            probeSubmersion[i] = Mathf.Clamp01(depth / submersionDepth);
            submersionSum += probeSubmersion[i];
        }

        averageSubmersion = submersionSum * perProbe;

        if (traceFrames > 0)
        {
            traceFrames--;
            var e = transform.eulerAngles;
            Debug.Log($"T{traceFrames} y={transform.position.y:F3} vy={rb.linearVelocity.y:F3} " +
                      $"pitch={Mathf.DeltaAngle(0f, e.x):F2} roll={Mathf.DeltaAngle(0f, e.z):F2} " +
                      $"planeY={planeHeight:F3} grad={planeGradient:F4} avgSub={averageSubmersion:F3}");
        }

        // Pass 3: apply.
        for (int i = 0; i < probesLocal.Length; i++)
        {
            float submersion = probeSubmersion[i];
            if (submersion <= 0f)
                continue;

            // Archimedes, spread evenly across the probes, with a ceiling so no single probe
            // can dominate when the hull is briefly deep under a wave.
            Vector3 buoyancy = Vector3.ClampMagnitude(buoyantAccel * (submersion * perProbe), maxProbeAccel);

            rb.AddForceAtPosition(buoyancy, probeWorld[i], ForceMode.Acceleration);
        }

        // Heave damping, applied at the centre of mass rather than per probe. A damping force
        // on a lever arm is a rectifier: as the hull rotates, only its submerged probes are
        // damped while the airborne ones are not, and the imbalance drives the ship upward
        // instead of slowing it. At the centre of mass there is no lever arm to exploit.
        rb.AddForce(Vector3.up * (-rb.linearVelocity.y * verticalDamping * averageSubmersion),
            ForceMode.Acceleration);

        rb.linearDamping = Mathf.Lerp(airLinearDamping, waterLinearDamping, averageSubmersion);
        rb.angularDamping = Mathf.Lerp(airAngularDamping, waterAngularDamping, averageSubmersion);

        ApplyUprightingTorque();

        if (holdPosition)
            ApplyStationKeeping();
    }

    // Least-squares fits one plane through the probe samples, low-passes it, then scales its
    // two slope components separately. Fitting first is what removes the chatter: the hull
    // reads the average water under its whole length instead of ten independent heights, so
    // any wave shorter than the ship averages away rather than shaking it.
    private void UpdateWaterPlane()
    {
        Vector3 centre = transform.position;

        float sumY = 0f, sumXX = 0f, sumZZ = 0f, sumXY = 0f, sumZY = 0f;

        for (int i = 0; i < probesLocal.Length; i++)
        {
            float dx = probeWorld[i].x - centre.x;
            float dz = probeWorld[i].z - centre.z;
            float y = probeWaterRaw[i];

            sumY += y;
            sumXX += dx * dx;
            sumZZ += dz * dz;
            sumXY += dx * y;
            sumZY += dz * y;
        }

        // The probe grid is symmetric about the hull centre, so the cross terms vanish and the
        // normal equations reduce to these. Guard the degenerate single-row/column layouts.
        float targetHeight = sumY / probesLocal.Length;
        Vector2 targetGradient = new Vector2(
            sumXX > 1e-4f ? sumXY / sumXX : 0f,
            sumZZ > 1e-4f ? sumZY / sumZZ : 0f);

        // Water cannot actually be this steep, so a fit this extreme means the height solver
        // returned garbage for at least one probe. Rejecting it outright is safer than letting
        // it through: a bad plane removes buoyancy, which drops the hull, which puts the probes
        // deeper and makes the next fit worse still.
        targetGradient = Vector2.ClampMagnitude(targetGradient, maxWaveSlope);
        targetHeight = Mathf.Clamp(
            targetHeight,
            targetSurface.transform.position.y - maxWaveHeight,
            targetSurface.transform.position.y + maxWaveHeight);

        if (!planeInitialized)
        {
            planeHeight = targetHeight;
            planeGradient = targetGradient;
            planeInitialized = true;
        }
        else
        {
            // Frame-rate independent exponential smoothing.
            float t = waveSmoothTime > 0f
                ? 1f - Mathf.Exp(-Time.fixedDeltaTime / waveSmoothTime)
                : 1f;
            planeHeight = Mathf.Lerp(planeHeight, targetHeight, t);
            planeGradient = Vector2.Lerp(planeGradient, targetGradient, t);
        }

        // Split the slope into the hull's own axes so roll and pitch can be dialled separately.
        Vector2 right = HorizontalDirection(transform.right);
        Vector2 forward = HorizontalDirection(transform.forward);

        float lateralSlope = Vector2.Dot(planeGradient, right) * rollResponse;
        float longitudinalSlope = Vector2.Dot(planeGradient, forward) * pitchResponse;

        effectiveGradient = lateralSlope * right + longitudinalSlope * forward;
    }

    // Height of the filtered plane at a world position.
    private float SampleWaterPlane(Vector3 pointWS)
    {
        return planeHeight
             + effectiveGradient.x * (pointWS.x - transform.position.x)
             + effectiveGradient.y * (pointWS.z - transform.position.z);
    }

    private static Vector2 HorizontalDirection(Vector3 axis)
    {
        Vector2 flat = new Vector2(axis.x, axis.z);
        return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector2.right;
    }

    // Rolls the hull back level. Cross(up, worldUp) points along the axis that undoes the
    // current tilt and scales with its sine, so it fades out as the ship comes upright and
    // never fights the yaw the heading spring owns.
    private void ApplyUprightingTorque()
    {
        if (uprightStrength <= 0f || averageSubmersion <= 0f)
            return;

        // Target the filtered water plane's normal, not world up. Aiming at world up would
        // fight the very roll the swell is supposed to produce; aiming at the plane lets roll
        // through while still springing pitch back to level, because pitchResponse has already
        // flattened the fore-aft component out of that normal.
        Vector3 targetUp = new Vector3(-effectiveGradient.x, 1f, -effectiveGradient.y).normalized;
        Vector3 tiltAxis = Vector3.Cross(transform.up, targetUp);

        // Damp pitch and roll only; angular velocity about world up is yaw.
        Vector3 tiltRate = rb.angularVelocity - Vector3.up * rb.angularVelocity.y;

        rb.AddTorque(
            (tiltAxis * uprightStrength - tiltRate * (2f * Mathf.Sqrt(uprightStrength))) * averageSubmersion,
            ForceMode.Acceleration);
    }

    // Keeps a moored ship on its mark: a critically damped spring on the horizontal
    // position, and another on the heading. Vertical motion is left entirely to buoyancy.
    private void ApplyStationKeeping()
    {
        Vector3 planarError = transform.position - anchorPosition;
        planarError.y = 0f;

        Vector3 planarVelocity = rb.linearVelocity;
        planarVelocity.y = 0f;

        rb.AddForce(-planarError * positionSpring - planarVelocity * (2f * Mathf.Sqrt(positionSpring)),
            ForceMode.Acceleration);

        float yawError = Mathf.DeltaAngle(transform.eulerAngles.y, anchorYaw) * Mathf.Deg2Rad;
        rb.AddTorque(Vector3.up * (yawError * headingSpring - rb.angularVelocity.y * (2f * Mathf.Sqrt(headingSpring))),
            ForceMode.Acceleration);
    }

    private bool SampleWaterHeight(int probeIndex, Vector3 pointWS, out float heightWS)
    {
        WaterSearchParameters searchParameters = new WaterSearchParameters
        {
            // Seed directly above/below the probe at the surface's own base height. Unity's
            // Buoyancy sample seeds from the previous result instead, which has two failure
            // modes: the first query defaults to (0,0,0) and only converges because the demo
            // scenes sit near the world origin, and once the hull is moving fast the carried
            // candidate drifts far enough that the solver converges on nonsense. A fixed anchor
            // at the water plane is always a good guess and cannot drift.
            startPositionWS = new Vector3(pointWS.x, targetSurface.transform.position.y, pointWS.z),
            targetPositionWS = pointWS,
            error = 0.01f,
            maxIterations = 8,
            includeDeformation = includeDeformation,
            excludeSimulation = false,
        };

        bool converged = targetSurface.ProjectPointOnWaterSurface(searchParameters, out WaterSearchResult result);

        // A search that runs out of iterations can still report success while handing back a
        // non-finite height. Feeding that into AddForce poisons the entire rigidbody.
        if (converged && float.IsFinite(result.projectedPositionWS.y))
        {
            searchResults[probeIndex] = result;
            heightWS = result.projectedPositionWS.y;

            // A custom-mesh surface reports heights relative to its own transform.
            if (isWaterSurfaceACustomMesh)
                heightWS += targetSurface.transform.position.y;

            lastGoodHeight[probeIndex] = heightWS;
            probeHasResult[probeIndex] = true;
            return true;
        }

        // Reuse this probe's last good height rather than dropping its buoyancy. A probe that
        // contributes nothing while its neighbours still push is an asymmetric force couple,
        // which spins the hull instead of merely bobbing it.
        if (probeHasResult[probeIndex])
        {
            heightWS = lastGoodHeight[probeIndex];
            return true;
        }

        heightWS = 0f;
        return false;
    }

    private void BuildProbes()
    {
        List<Vector3> local = new();

        foreach (Transform probe in probeTransforms)
        {
            if (probe != null)
                local.Add(transform.InverseTransformPoint(probe.position));
        }

        if (local.Count == 0)
            local.AddRange(GenerateKeelGrid());

        probesLocal = local.ToArray();
        searchResults = new WaterSearchResult[probesLocal.Length];
        probeHasResult = new bool[probesLocal.Length];
        lastGoodHeight = new float[probesLocal.Length];
        probeSubmersion = new float[probesLocal.Length];
        probeWorld = new Vector3[probesLocal.Length];
        probeWaterRaw = new float[probesLocal.Length];
        planeInitialized = false;
    }

    // Lays probes out across the hull footprint at keel height, so submersion is
    // measured from the lowest part of the ship upward.
    private IEnumerable<Vector3> GenerateKeelGrid()
    {
        Bounds local = GetLocalBounds();

        int countX = Mathf.Max(1, autoProbeGrid.x);
        int countZ = Mathf.Max(1, autoProbeGrid.y);

        float spanX = local.size.x * (1f - 2f * autoProbeInset);
        float spanZ = local.size.z * (1f - 2f * autoProbeInset);

        for (int x = 0; x < countX; x++)
        {
            float tx = countX == 1 ? 0.5f : x / (float)(countX - 1);
            for (int z = 0; z < countZ; z++)
            {
                float tz = countZ == 1 ? 0.5f : z / (float)(countZ - 1);
                yield return new Vector3(
                    local.center.x + (tx - 0.5f) * spanX,
                    local.min.y,
                    local.center.z + (tz - 0.5f) * spanZ);
            }
        }
    }

    // Colliders describe the physical hull; renderers are the fallback when the ship
    // has visual geometry but no collider yet.
    private Bounds GetLocalBounds()
    {
        Bounds local = new Bounds(Vector3.zero, Vector3.one);
        bool initialized = false;

        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider collider in colliders)
            EncapsulateWorldBounds(collider.bounds, ref local, ref initialized);

        if (!initialized)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
                EncapsulateWorldBounds(renderer.bounds, ref local, ref initialized);
        }

        if (!initialized)
            Debug.LogWarning($"{nameof(Floater)} on '{name}' found no collider or renderer to size the probe grid from; using a 1m cube.", this);

        return local;
    }

    private void EncapsulateWorldBounds(Bounds worldBounds, ref Bounds local, ref bool initialized)
    {
        Vector3 center = worldBounds.center;
        Vector3 extents = worldBounds.extents;

        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = new Vector3(
                (corner & 1) == 0 ? -extents.x : extents.x,
                (corner & 2) == 0 ? -extents.y : extents.y,
                (corner & 4) == 0 ? -extents.z : extents.z);

            Vector3 pointLS = transform.InverseTransformPoint(center + offset);

            if (!initialized)
            {
                local = new Bounds(pointLS, Vector3.zero);
                initialized = true;
            }
            else
            {
                local.Encapsulate(pointLS);
            }
        }
    }

    [ContextMenu("Fit Draft To Hull")]
    private void FitDraftToHull()
    {
        Bounds local = GetLocalBounds();
        submersionDepth = Mathf.Max(0.01f, local.size.y * 0.25f);
        Debug.Log($"{nameof(Floater)} on '{name}': submersion depth fitted to {submersionDepth:0.##}m from a hull height of {local.size.y:0.##}m.", this);
    }

    public float GetAverageSubmersion() => averageSubmersion;

    /// <summary>
    /// The self-righting torque this hull can muster, in the same acceleration units a motor
    /// would use. Exposed so something applying a heeling moment can cap itself against it and
    /// guarantee the ship comes back up.
    /// </summary>
    public float UprightStrength => uprightStrength;

    private void OnDrawGizmosSelected()
    {
        if (!drawDebug)
            return;

        // Outside play mode the probe cache is empty, so preview the generated layout.
        Vector3[] probes = probesLocal;
        if (probes == null || probes.Length == 0)
        {
            List<Vector3> preview = new();
            foreach (Transform probe in probeTransforms)
            {
                if (probe != null)
                    preview.Add(transform.InverseTransformPoint(probe.position));
            }
            if (preview.Count == 0)
                preview.AddRange(GenerateKeelGrid());
            probes = preview.ToArray();
        }

        float radius = Mathf.Max(0.1f, submersionDepth * 0.1f);

        for (int i = 0; i < probes.Length; i++)
        {
            Vector3 probeWS = transform.TransformPoint(probes[i]);
            float submersion = probeSubmersion != null && i < probeSubmersion.Length ? probeSubmersion[i] : 0f;

            Gizmos.color = Color.Lerp(Color.yellow, Color.cyan, submersion);
            Gizmos.DrawSphere(probeWS, radius);

            // Vertical line spanning the draft this probe measures against.
            Gizmos.color = new Color(0f, 0.6f, 1f, 0.5f);
            Gizmos.DrawLine(probeWS, probeWS + Vector3.up * submersionDepth);
        }

        if (Application.isPlaying && holdPosition)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, anchorPosition);
        }
    }
}
