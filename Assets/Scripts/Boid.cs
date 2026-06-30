using UnityEngine;

// NOTE: The many flock fish are now simulated by BoidsManager's Burst job (this component is
// disabled on them). This managed Boid is used ONLY by the player while blended into a flock:
// it reads the flock's snapshot to school with it, using the same weights so it matches.
public class Boid : MonoBehaviour
{
    private BoidsManager manager;
    private float speed;
    private Transform _t;

    public void SetManager(BoidsManager m) => manager = m;

    void Awake() => _t = transform;

    void Update()
    {
        if (manager == null) return;
        if (speed <= 0f) speed = Random.Range(manager.minSpeed, manager.maxSpeed); // lazy init (manager set after AddComponent)
        float dt = Time.deltaTime;
        Vector3 pos = _t.position;

        Vector3[] sp = manager.SnapshotPositions;
        Vector3[] sf = manager.SnapshotForwards;
        int count = manager.SnapshotCount;

        float nd2 = manager.neighbourDist * manager.neighbourDist;
        float sepDist = manager.separationDist;
        float sep2 = sepDist * sepDist;

        Vector3 align = Vector3.zero, center = Vector3.zero, separation = Vector3.zero;
        int neighbours = 0, sepCount = 0;

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = sp[i] - pos;
            float d2 = offset.sqrMagnitude;
            if (d2 > nd2 || d2 < 1e-6f) continue;

            align += sf[i];
            center += sp[i];
            neighbours++;
            if (d2 < sep2)
            {
                float d = Mathf.Sqrt(d2);
                separation -= (offset / d) * (1f - d / sepDist);
                sepCount++;
            }
        }

        Vector3 steer = Vector3.zero;
        if (neighbours > 0)
        {
            align /= neighbours;
            if (align.sqrMagnitude > 1e-6f) steer += align.normalized * manager.alignmentWeight;
            Vector3 toCenter = (center / neighbours) - pos;
            if (toCenter.sqrMagnitude > 1e-6f) steer += toCenter.normalized * manager.cohesionWeight;
        }
        if (sepCount > 0 && separation.sqrMagnitude > 1e-6f) steer += separation.normalized * manager.separationWeight;

        Vector3 toGoal = manager.goalPos - pos;
        if (toGoal.sqrMagnitude > 1e-4f) steer += toGoal.normalized * manager.goalWeight;

        // Flee predators with the school.
        bool fleeing = false;
        float fr2 = manager.fleeRadius * manager.fleeRadius;
        var preds = BoidsManager.Predators;
        for (int i = 0; i < preds.Count; i++)
        {
            if (preds[i] == null) continue;
            Vector3 away = pos - preds[i].position;
            float d2 = away.sqrMagnitude;
            if (d2 < fr2 && d2 > 1e-6f)
            {
                float d = Mathf.Sqrt(d2);
                steer += away.normalized * (manager.fleeWeight * (1f - d / manager.fleeRadius));
                fleeing = true;
            }
        }

        Bounds bounds = new Bounds(manager.transform.position, manager.area);
        if (!bounds.Contains(pos))
        {
            Vector3 toC = manager.transform.position - pos;
            if (toC.sqrMagnitude > 1e-6f) steer += toC.normalized * manager.boundaryWeight;
        }

        steer += AvoidObstacles();

        if (steer.sqrMagnitude > 1e-6f)
        {
            Quaternion target = Quaternion.LookRotation(steer.normalized, Vector3.up);
            _t.rotation = Quaternion.Slerp(_t.rotation, target, manager.rotationSpeed * dt);
        }

        float moveSpeed = fleeing ? manager.fleeSpeed : speed;
        _t.position += _t.forward * (moveSpeed * dt);

        // Stay below the water surface, same as the flock.
        float ceiling = manager.WaterCeiling;
        if (_t.position.y > ceiling)
        {
            var p = _t.position; p.y = ceiling; _t.position = p;
        }
    }

    Vector3 AvoidObstacles()
    {
        if (manager.avoidWeight <= 0f) return Vector3.zero;
        float lookAhead = manager.avoidDistance + speed * 0.3f;

        if (Physics.SphereCast(_t.position, manager.avoidRadius, _t.forward,
                out RaycastHit hit, lookAhead, manager.obstacleMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 along = Vector3.ProjectOnPlane(_t.forward, hit.normal);
            if (along.sqrMagnitude < 1e-4f) along = hit.normal;
            float strength = 1f - hit.distance / lookAhead;
            return along.normalized * (manager.avoidWeight * (1f + 3f * strength));
        }
        return Vector3.zero;
    }
}
