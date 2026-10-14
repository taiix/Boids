using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A sea turtle swimming free around the map: it cruises from one spot to the next a few metres above the
    /// seabed, climbs over rising ground, turns away from walls and rocks, stays under the surface and inside
    /// the arena, and banks into its turns.
    ///
    /// The server (or this machine, offline) swims it; everyone else follows it through its NetworkTransform.
    /// The turtles roaming the reef swim from the start (<see cref="TurtleRescueTask"/> lets them go); a rescued
    /// one (<see cref="TangledTurtle"/>) is switched on once it's free, and joins them.
    /// </summary>
    public class TurtleSwimmer : MonoBehaviour
    {
        [SerializeField] bool swimFromStart = true;
        [Tooltip("Cruising speed (m/s).")]
        [SerializeField] float cruiseSpeed = 1.5f;
        [Tooltip("How fast it can turn (degrees a second).")]
        [SerializeField] float turnRate = 28f;
        [Tooltip("How high above the seabed it likes to swim (metres, min - max).")]
        [SerializeField] Vector2 heightAboveSeabed = new Vector2(2.5f, 9f);
        [Tooltip("Keep at least this far under the surface (metres).")]
        [SerializeField] float underSurface = 3f;
        [Tooltip("Keep this far in from the arena's walls (metres).")]
        [SerializeField] float wallMargin = 12f;

        NetworkIdentity _id;
        Bounds _seabed;
        bool _haveSeabed, _swimming;
        Vector3 _target;
        float _speed, _yaw, _pitch, _roll, _nextPick, _pace;

        public bool Swimming => _swimming;

        bool Authority => NetworkServer.active ? _id != null && _id.isServer : !NetworkClient.active;

        void Awake()
        {
            _id = GetComponent<NetworkIdentity>();
            _pace = Random.Range(0f, 100f);
        }

        void Start()
        {
            // Not all flapping in step.
            var animator = GetComponentInChildren<Animator>();
            if (animator != null && swimFromStart) animator.Play(0, 0, Random.value);
            if (swimFromStart) BeginSwimming(transform.position + transform.forward * 10f, cruiseSpeed);
        }

        /// <summary>Server / offline: start swimming, first toward <paramref name="firstTarget"/>.</summary>
        public void BeginSwimming(Vector3 firstTarget, float startSpeed)
        {
            _swimming = true;
            _target = firstTarget;
            _speed = startSpeed;
            _nextPick = Time.time + 20f;
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = Mathf.DeltaAngle(0f, e.x);
            _roll = 0f;
        }

        void Update()
        {
            if (!_swimming || !Authority) return;
            if (!_haveSeabed)
            {
                var terrain = FindAnyObjectByType<TestTerrain>();
                if (terrain == null || !terrain.TryGetComponent(out MeshCollider seabed) || seabed.sharedMesh == null) return;
                _seabed = seabed.bounds;
                _haveSeabed = true;
            }
            Swim(Time.deltaTime);
        }

        void Swim(float dt)
        {
            if (dt <= 0f) return;
            Vector3 pos = transform.position;
            Vector3 heading = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;
            if ((pos - _target).sqrMagnitude < 16f || Time.time > _nextPick || !InArea(_target)) PickTarget(pos, heading);

            Vector3 want = (_target - pos).normalized;

            // Something solid straight ahead (a wall, a big rock): turn off somewhere else.
            if (Physics.SphereCast(pos, 0.4f, heading, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<FishPlayer>() == null
                && hit.collider.gameObject.layer != 3)   // the seabed is handled below, by climbing
                PickTarget(pos, Vector3.Reflect(heading, hit.normal));

            // Ground rising ahead or close below: climb over it. The surface: stay under it.
            float clear = heightAboveSeabed.x;
            Vector3 ahead = pos + heading * 6f;
            if (WaterFx.SeabedHeight(ahead, out float groundAhead) && ahead.y < groundAhead + clear) want.y = Mathf.Max(want.y, 0.55f);
            if (WaterFx.SeabedHeight(pos, out float groundHere) && pos.y < groundHere + clear * 0.6f) want.y = Mathf.Max(want.y, 0.85f);
            if (pos.y > WaterFx.SurfaceY - underSurface) want.y = Mathf.Min(want.y, -0.4f);
            want.Normalize();

            // Turn toward it no faster than a turtle can; level-ish, banking into the turn.
            float wantYaw = Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg;
            float wantPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(want.y, -1f, 1f)) * Mathf.Rad2Deg, -35f, 35f);
            float turn = Mathf.Clamp(Mathf.DeltaAngle(_yaw, wantYaw), -turnRate * dt, turnRate * dt);
            _yaw += turn;
            _pitch = Mathf.MoveTowards(_pitch, wantPitch, turnRate * 0.8f * dt);
            _roll = Mathf.Lerp(_roll, Mathf.Clamp(-turn / dt * 0.6f, -20f, 20f), 1f - Mathf.Exp(-2f * dt));
            transform.rotation = Quaternion.Euler(_pitch, _yaw, _roll);

            // Cruise, easing a little faster and slower now and then (strokes and glides).
            float pace = cruiseSpeed * (1f + 0.15f * Mathf.Sin(Time.time * 0.3f + _pace));
            _speed = Mathf.MoveTowards(_speed, pace, 0.6f * dt);
            transform.position = pos + Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward * (_speed * dt);
        }

        // Somewhere new to swim to, roughly ahead (or along <paramref name="toward"/>), at a comfortable height.
        void PickTarget(Vector3 from, Vector3 toward)
        {
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-4f) toward = Random.insideUnitSphere;
            toward.y = 0f;
            toward.Normalize();
            for (int i = 0; i < 12; i++)
            {
                float spread = Mathf.Lerp(50f, 180f, i / 11f);
                Vector3 dir = Quaternion.Euler(0f, Random.Range(-spread, spread), 0f) * toward;
                Vector3 p = from + dir * Random.Range(15f, 40f);
                if (!InArea(p)) continue;
                float ground = WaterFx.SeabedHeight(p, out float g) ? g : from.y - heightAboveSeabed.y;
                p.y = Mathf.Min(ground + Random.Range(heightAboveSeabed.x, heightAboveSeabed.y), WaterFx.SurfaceY - underSurface - 1f);
                _target = p;
                _nextPick = Time.time + 30f;
                return;
            }
            // Boxed in: back toward the middle.
            _target = new Vector3(_seabed.center.x, from.y, _seabed.center.z);
            _nextPick = Time.time + 15f;
        }

        bool InArea(Vector3 p)
        {
            var walls = ArenaWalls.Instance;
            if (walls != null && walls.Count > 0) return walls.Inside(p, wallMargin);
            return p.x > _seabed.min.x + wallMargin && p.x < _seabed.max.x - wallMargin
                && p.z > _seabed.min.z + wallMargin && p.z < _seabed.max.z - wallMargin;
        }
    }
}
