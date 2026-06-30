using UnityEngine;
using UnityEngine.Events;

public class BoidsManager : MonoBehaviour
{
    public static BoidsManager instance { get; private set; }

    public Vector3 area;
    [SerializeField] private GameObject prefab;
    [SerializeField] private int fishCount;

    public GameObject[] allFish;

    [Header("Boid Settings")]
    [Range(0f, 5f)]
    public float minSpeed;
    [Range(0f, 5f)]
    public float maxSpeed;
    [Range(0f, 10f)]
    public float neighbourDist;
    [Range(1f, 5f)]
    public float rotationSpeed;

    public Vector3 goalPos;

    public UnityEvent<float> OnMinSpeedChanged = new UnityEvent<float>();
    public UnityEvent<float> OnMaxSpeedChanged = new UnityEvent<float>();
    public UnityEvent<float> OnNeighbourDistChanged = new UnityEvent<float>();
    public UnityEvent<float> OnRotationSpeedChanged = new UnityEvent<float>();

    private Vector3 previousMinSpeed;
    private Vector3 previousMaxSpeed;
    private Vector3 previousNeighbourDist;
    private Vector3 previousRotationSpeed;

    private void Start()
    {
        instance = this;

        allFish = new GameObject[fishCount];

        for (int i = 0; i < fishCount; i++)
        {
            allFish[i] = Instantiate(prefab,
                transform.position + new Vector3(Random.Range(-area.x, area.x),
                            Random.Range(-area.y, area.y),
                            Random.Range(-area.z, area.z)) / 2, Quaternion.identity);
        }

        previousMinSpeed = new Vector3(minSpeed, 0, 0);
        previousMaxSpeed = new Vector3(maxSpeed, 0, 0);
        previousNeighbourDist = new Vector3(neighbourDist, 0, 0);
        previousRotationSpeed = new Vector3(rotationSpeed, 0, 0);
    }

    private void Update()
    {
        if (minSpeed != previousMinSpeed.x)
        {
            OnMinSpeedChanged.Invoke(minSpeed);
            previousMinSpeed.x = minSpeed;
        }

        if (maxSpeed != previousMaxSpeed.x)
        {
            OnMaxSpeedChanged.Invoke(maxSpeed);
            previousMaxSpeed.x = maxSpeed;
        }

        if (neighbourDist != previousNeighbourDist.x)
        {
            OnNeighbourDistChanged.Invoke(neighbourDist);
            previousNeighbourDist.x = neighbourDist;
        }

        if (rotationSpeed != previousRotationSpeed.x)
        {
            OnRotationSpeedChanged.Invoke(rotationSpeed);
            previousRotationSpeed.x = rotationSpeed;
        }

        if (Random.Range(0, 100) < 10)
        {
            goalPos = transform.position + new Vector3(Random.Range(-area.x, area.x),
                            Random.Range(-area.y, area.y),
                            Random.Range(-area.z, area.z));
        }
    }

    public void AddFish()
    {
        GameObject newFish = Instantiate(prefab,
            transform.position + new Vector3(Random.Range(-area.x, area.x),
                        Random.Range(-area.y, area.y),
                        Random.Range(-area.z, area.z)) / 2, Quaternion.identity);

        System.Array.Resize(ref allFish, allFish.Length + 1);
        allFish[allFish.Length - 1] = newFish;
    }

    public void RemoveFish()
    {
        if (allFish.Length > 0)
        {
            Destroy(allFish[allFish.Length - 1]);
            System.Array.Resize(ref allFish, allFish.Length - 1);
        }
    }

    // Register an EXISTING object (e.g. a blending player) into the flock so the boids
    // align/cohere/avoid with it too. It is not instantiated or destroyed by the manager.
    public void Join(GameObject fish)
    {
        if (fish == null || allFish == null) return;
        for (int i = 0; i < allFish.Length; i++)
            if (allFish[i] == fish) return; // already a member

        System.Array.Resize(ref allFish, allFish.Length + 1);
        allFish[allFish.Length - 1] = fish;
    }

    // Remove a member previously added with Join (without destroying it).
    public void Leave(GameObject fish)
    {
        if (fish == null || allFish == null) return;
        int idx = -1;
        for (int i = 0; i < allFish.Length; i++)
            if (allFish[i] == fish) { idx = i; break; }
        if (idx < 0) return;

        for (int i = idx; i < allFish.Length - 1; i++)
            allFish[i] = allFish[i + 1];
        System.Array.Resize(ref allFish, allFish.Length - 1);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, area);
    }

}
