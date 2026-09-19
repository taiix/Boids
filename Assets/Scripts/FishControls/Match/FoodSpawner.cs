using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Spawns <see cref="FoodPellet"/>s at random points inside a flat surface area, on an interval,
    /// up to a max concurrent count. Server-only: it's a plain MonoBehaviour gated on
    /// <see cref="NetworkServer.active"/>, so it needs no NetworkIdentity and won't be disabled as an
    /// unspawned scene object. Place one in the game scene and size the area over open water.
    /// </summary>
    public class FoodSpawner : MonoBehaviour
    {
        [Header("What to spawn")]
        [SerializeField] GameObject foodPrefab;

        [Header("Area (place over open water, at the surface)")]
        [Tooltip("Center of the spawn area. Set y to the water surface (~0).")]
        [SerializeField] Vector3 areaCenter = new Vector3(0f, 0.5f, 0f);
        [Tooltip("Width (x) and depth (z) of the area. Pellets spawn at areaCenter.y.")]
        [SerializeField] Vector2 areaSize = new Vector2(60f, 60f);

        [Header("Rate")]
        [SerializeField] float spawnInterval = 4f;
        [SerializeField] int maxConcurrent = 12;

        float _timer;

        void Awake()
        {
            // Make sure remote clients know how to spawn the pellet (server spawns it below).
            if (foodPrefab != null && foodPrefab.TryGetComponent<NetworkIdentity>(out _))
                NetworkClient.RegisterPrefab(foodPrefab);
        }

        void OnEnable() => _timer = spawnInterval;

        void Update()
        {
            if (foodPrefab == null) return;

            // Spawn when we're the authority: the server (host) in networked play, or the local
            // instance when fully offline (single-player testing). A pure client never spawns —
            // the server's pellets are replicated to it.
            bool offline = !NetworkServer.active && !NetworkClient.active;
            if (!offline && !NetworkServer.active) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = spawnInterval;

            if (CountAlive() >= maxConcurrent) return;

            Vector3 pos = new Vector3(
                areaCenter.x + Random.Range(-areaSize.x * 0.5f, areaSize.x * 0.5f),
                areaCenter.y,
                areaCenter.z + Random.Range(-areaSize.y * 0.5f, areaSize.y * 0.5f));

            GameObject go = Instantiate(foodPrefab, pos, Random.rotation);
            if (NetworkServer.active) NetworkServer.Spawn(go); // networked: replicate to clients; offline: local only
        }

        static int CountAlive() =>
            FindObjectsByType<FoodPellet>(FindObjectsSortMode.None).Length;

        // Always-on gizmo so the spawn area is visible without selecting the object. Pellets spawn
        // at a random (x, z) inside this rectangle, at areaCenter.y (the water surface), then sink.
        void OnDrawGizmos()
        {
            Vector3 size = new Vector3(areaSize.x, 0.1f, areaSize.y);
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.12f);
            Gizmos.DrawCube(areaCenter, size);       // translucent fill
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.95f);
            Gizmos.DrawWireCube(areaCenter, size);   // bright outline
            // A small marker at the center.
            Gizmos.DrawWireSphere(areaCenter, 0.6f);
        }
    }
}
