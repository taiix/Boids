using UnityEngine;

public class Boid : MonoBehaviour
{
    private BoidsManager manager;

    private float speed;
    private bool turning;

    void Start()
    {
        manager = BoidsManager.instance;
        speed = Random.Range(manager.minSpeed, manager.maxSpeed);
    }


    void Update()
    {
        Bounds b = new Bounds(manager.transform.position, manager.area);

        if (!b.Contains(transform.position))
        {
            turning = true;
        }
        else turning = false;

        if (turning)
        {
            Vector3 dir = manager.transform.position - transform.position;
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir),
                manager.rotationSpeed * Time.deltaTime);
        }
        else
        {
            if (Random.Range(0, 100) < 10)
            {
                speed = Random.Range(manager.minSpeed, manager.maxSpeed);
            }
            if (Random.Range(0, 100) < 40)
            {
                //ApplyRules();
                Vector3 result = ApplySettings() + manager.goalPos;

                transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(result), manager.rotationSpeed * Time.deltaTime);
            }
        }
        AvoidObstacles();
        this.transform.Translate(0, 0, speed * Time.deltaTime);
    }

    private Vector3 ApplySettings()
    {
        GameObject[] _allFishes = manager.allFish;

        Vector3 align = Vector3.zero;
        Vector3 cohesion = Vector3.zero;
        Vector3 avoidance = Vector3.zero;

        float nDist = manager.neighbourDist;
        int neighbourCount = 0;

        foreach (var fish in _allFishes)
        {
            if (fish == this.gameObject) continue;
            float distance = (fish.transform.position - this.transform.position).magnitude;

            if (distance <= nDist)
            {
                align += fish.transform.forward;
                cohesion += fish.transform.position;

                if (distance <= 1f)
                {
                    Vector3 diff = (this.transform.position - fish.transform.position).normalized;
                    avoidance += diff;
                }

                neighbourCount++;
            }
        }
        if (neighbourCount > 0)
        {
            align /= neighbourCount;
            cohesion /= neighbourCount;
            avoidance /= neighbourCount;
        }

        Vector3 alignResult = align - this.transform.forward;
        Vector3 cohesionResult = cohesion - this.transform.position;
        Vector3 avoidResult = avoidance - this.transform.position;

        return alignResult + cohesionResult + avoidResult;
    }

    private void AvoidObstacles()
    {
        int startAngle = -110;
        int endAngle = 110;

        float raycastDist = 1f;
        for (int i = startAngle; i < endAngle; i += 10)
        {
            var currentPointPosition = Quaternion.AngleAxis(i, transform.up) * transform.forward;

            Ray ray = new Ray(this.transform.position, currentPointPosition);

            if (Physics.Raycast(ray, out RaycastHit hit, raycastDist))
            {
                Vector3 avoidDir = (this.transform.position - hit.point).normalized;

                float distanceFactor = 1.0f - (hit.distance / raycastDist);
                float rotationSpeed = manager.rotationSpeed * distanceFactor * Time.deltaTime;

                // Smoothly rotate the fish based on the distance to the obstacle
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(avoidDir), rotationSpeed);
            }
        }
    }

}
