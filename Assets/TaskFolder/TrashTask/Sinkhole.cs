using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A sinkhole in the seabed that swallows trash for the <see cref="TrashTask"/>. The task's server
    /// places a few of them in new spots every round and spawns them for everyone.
    ///
    /// Trash that gets close is drawn in (<see cref="PullRadius"/>), and trash over the hole
    /// (<see cref="CaptureRadius"/>) sinks into it and counts. Every hole also carries the task's progress
    /// - the server writes it to all of them - so any machine can show how the clean-up is going.
    ///
    /// Each machine sits the hole on its own seabed (they all match once the host's has been shared),
    /// and its rim glows softly so it can be found.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class Sinkhole : NetworkBehaviour
    {
        [Tooltip("Trash whose middle comes this close to the hole's middle (across the seabed) falls in, in metres.")]
        [SerializeField] float captureRadius = 1.8f;
        [Tooltip("Trash within this distance is drawn toward the hole...")]
        [SerializeField] float pullRadius = 5f;
        [Tooltip("...this hard at the rim (m/s per second), fading out toward the edge of the pull.")]
        [SerializeField] float pull = 2.5f;
        [Tooltip("Reef coral and plants reaching within this distance of the middle are cleared away, so nothing " +
                 "grows over the hole. Every machine scatters the same reef, so they all clear the same pieces.")]
        [SerializeField] float clearReefRadius = 3.5f;

        [Header("Look")]
        [Tooltip("Renderers that glow softly so the hole can be found (the rim rocks). Empty = every renderer " +
                 "on the hole except decals.")]
        [SerializeField] Renderer[] glowRenderers;
        [ColorUsage(false, true)][SerializeField] Color glowColor = new Color(0.35f, 0.85f, 1f);
        [SerializeField] float glowIntensity = 0.8f;
        [SerializeField] float glowPulseRate = 0.35f;

        /// <summary>Every sinkhole in the world.</summary>
        public static readonly List<Sinkhole> All = new List<Sinkhole>();

        // The task's progress, the same on every hole.
        [SyncVar] int _collected;
        [SyncVar] int _goal;
        [SyncVar] bool _complete;

        /// <summary>Server / offline: the task that placed this hole (told when trash falls in).</summary>
        public TrashTask Task { get; set; }

        public float CaptureRadius => captureRadius;
        public float PullRadius => pullRadius;
        public float Pull => pull;
        public Vector3 Centre => transform.position;
        public int Collected => _collected;
        public int Goal => _goal;
        public bool Complete => _complete;

        bool _seatPending = true;
        MaterialPropertyBlock _glow;
        static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
        static readonly int EmissiveWeightId = Shader.PropertyToID("_EmissiveExposureWeight");

        void Awake()
        {
            if (glowRenderers == null || glowRenderers.Length == 0)
                glowRenderers = GetComponentsInChildren<MeshRenderer>(true);
            _glow = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            All.Add(this);
            TerrainNetSync.SeabedChanged += QueueSeat;
            _seatPending = true;
        }

        void OnDisable()
        {
            All.Remove(this);
            TerrainNetSync.SeabedChanged -= QueueSeat;
        }

        void QueueSeat() => _seatPending = true;

        /// <summary>The hole nearest <paramref name="p"/> and how far it is across the seabed, or null.</summary>
        public static Sinkhole Nearest(Vector3 p, out float flatDistance)
        {
            Sinkhole best = null;
            flatDistance = float.MaxValue;
            foreach (var hole in All)
            {
                if (hole == null) continue;
                float dx = hole.transform.position.x - p.x, dz = hole.transform.position.z - p.z;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < flatDistance) { flatDistance = d; best = hole; }
            }
            return best;
        }

        /// <summary>Server / offline: a piece of trash just fell in.</summary>
        public void ServerSwallow(PushableTrash trash)
        {
            if (Task != null && trash != null) Task.ServerCollected(trash.Points, transform.position);
        }

        /// <summary>Server / offline: the task's progress, for every player to see.</summary>
        public void ServerSetProgress(int collected, int goal, bool complete)
        {
            _collected = collected;
            _goal = goal;
            _complete = complete;
        }

        void Update()
        {
            if (_seatPending) { _seatPending = false; Seat(); }

            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * glowPulseRate * 2f * Mathf.PI);
            Color emissive = glowColor * (glowIntensity * pulse);
            foreach (var r in glowRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_glow);
                _glow.SetColor(EmissiveColorId, emissive);
                _glow.SetFloat(EmissiveWeightId, 0f);
                r.SetPropertyBlock(_glow);
            }
        }

        // Sit on this machine's seabed, leaning with the ground so the rim hugs it.
        void Seat()
        {
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain == null || !terrain.TryGetComponent(out MeshCollider seabed) || seabed.sharedMesh == null) return;
            Bounds b = seabed.bounds;
            Vector3 p = transform.position;
            if (!seabed.Raycast(new Ray(new Vector3(p.x, b.max.y + 5f, p.z), Vector3.down), out var hit, b.size.y + 10f)) return;
            float yaw = transform.eulerAngles.y;
            transform.SetPositionAndRotation(hit.point,
                Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0f, yaw, 0f));
            ClearReef();
        }

        // Hide reef pieces whose footprint reaches over the hole (the reef can be re-scattered when the
        // host's seabed arrives, which re-seats us and so runs this again).
        void ClearReef()
        {
            if (clearReefRadius <= 0f) return;
            Vector3 c = transform.position;
            float reject = (clearReefRadius + 8f) * (clearReefRadius + 8f);
            foreach (var veg in FindObjectsByType<CodenameLib.ProceduralTerrain.ProceduralVegetation>(FindObjectsInactive.Exclude))
                foreach (Transform piece in veg.transform)
                {
                    if (!piece.gameObject.activeSelf) continue;
                    Vector3 p = piece.position;
                    if ((p.x - c.x) * (p.x - c.x) + (p.z - c.z) * (p.z - c.z) > reject) continue;
                    var r = piece.GetComponentInChildren<Renderer>();
                    Vector3 near = r != null ? r.bounds.ClosestPoint(new Vector3(c.x, r.bounds.center.y, c.z)) : p;
                    float dx = near.x - c.x, dz = near.z - c.z;
                    if (dx * dx + dz * dz < clearReefRadius * clearReefRadius) piece.gameObject.SetActive(false);
                }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, captureRadius);
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, pullRadius);
        }
    }
}
