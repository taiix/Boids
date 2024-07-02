using UnityEngine;

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
    }

    private void Update()
    {
        if (Random.Range(0, 100) < 10)
        {
            goalPos = transform.position + new Vector3(Random.Range(-area.x, area.x),
                            Random.Range(-area.y, area.y),
                            Random.Range(-area.z, area.z));
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, area);
    }

}
