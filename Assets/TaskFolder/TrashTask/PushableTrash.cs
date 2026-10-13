using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A piece of trash on the seabed that fish push into a <see cref="Sinkhole"/>. Small pieces move for
    /// one fish; big ones only move when enough fish push at once, roughly the same way - pushes from
    /// opposite sides cancel out.
    ///
    /// The server moves it (kinematic) from the pushes the fish report (<see cref="TrashPusher"/>); a
    /// NetworkTransform shows the motion to everyone. Offline it runs the same way locally. It moves like
    /// something underwater: pushes go the way the fish swim (up too, so it can be lifted), it drifts on
    /// after a push and slowly slows, and it's heavier than water - it sinks back and settles on the
    /// seabed, where it drags on the sand and rolls along. Down near a sinkhole it's drawn in, and once
    /// over one at the bottom it sinks out of sight and counts toward the task (<see cref="TrashTask"/>).
    ///
    /// Feedback: a faint glow so it can be found; amber while fish push it but too few (or too
    /// scattered) to move it, with a strained wobble; green while it's moving. Sharp pieces have a bladed
    /// side (glinting red) that cuts a fish pushing on it now and then, which makes it bleed
    /// (<see cref="FishVitals.Damage"/>); pushed from the smooth side they're safe. They scrape along
    /// instead of rolling, so the blades stay where you saw them.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Rigidbody))]
    public class PushableTrash : NetworkBehaviour
    {
        [Header("What it is")]
        [Tooltip("Name shown to a fish pushing it.")]
        [SerializeField] string displayName = "Trash bag";
        [Tooltip("Progress it's worth when it goes down a sinkhole.")]
        [Min(1)][SerializeField] int points = 1;

        [Header("Pushing")]
        [Tooltip("Fish needed, pushing together, to move it.")]
        [Range(1, 4)][SerializeField] int fishNeeded = 1;
        [Tooltip("How far apart the pushers may be aiming and still count as pushing together, in degrees.")]
        [SerializeField] float maxSpread = 70f;
        [Tooltip("Speed with just enough fish pushing (m/s). Extra fish push it faster, up to Max Speed.")]
        [SerializeField] float pushSpeed = 1.8f;
        [SerializeField] float maxSpeed = 3f;
        [Tooltip("How hard the pushers get it going (m/s per second). Pushes go the way the fish swim - up " +
                 "included, so swimming into it at an upward angle lifts it off the seabed.")]
        [SerializeField] float acceleration = 3f;

        [Header("In the water")]
        [Tooltip("How quickly the water slows it (per second). Low = it keeps drifting a while after a push.")]
        [SerializeField] float waterDrag = 0.8f;
        [Tooltip("How quickly it sinks (m/s per second): it's heavier than water, so once lifted it drifts " +
                 "back down and settles on the seabed.")]
        [SerializeField] float sinkAcceleration = 0.4f;
        [Tooltip("Extra slowing while it rests on the seabed (per second) - it drags on the sand, so it stops " +
                 "sooner there than in open water.")]
        [SerializeField] float groundFriction = 1f;
        [Tooltip("How much it bounces when it lands on the seabed, 0..1.")]
        [Range(0f, 1f)][SerializeField] float bounce = 0.15f;
        [Tooltip("Resting on a slope steeper than Slide Above Degrees, it rolls downhill this much (0 = stays put).")]
        [SerializeField] float slopeSlide = 1f;
        [Tooltip("Gentler slopes than this hold it still once it's stopped, instead of it creeping away.")]
        [SerializeField] float slideAboveDegrees = 25f;
        [Tooltip("How quickly a tumble in open water dies away (per second).")]
        [SerializeField] float spinDrag = 0.8f;

        [Header("Body")]
        [Tooltip("Radius in metres (0 = from its SphereCollider).")]
        [SerializeField] float radius;
        [Tooltip("How far it sits sunk into the sand, as a fraction of its radius.")]
        [Range(0f, 0.5f)][SerializeField] float sinkIntoSand = 0.15f;
        [Tooltip("Rolls along as it's pushed. Off = it scrapes along the seabed facing the same way (so a " +
                 "sharp side stays where you saw it).")]
        [SerializeField] bool rolls = true;

        [Header("Sharp edges")]
        [Tooltip("Pushing it from its sharp side cuts the fish every this many seconds of pushing (0 = not " +
                 "sharp). A cut fish bleeds, and the shark can follow its trail.")]
        [SerializeField] float cutEverySeconds;
        [SerializeField] float cutDamage = 6f;
        [SerializeField] float cutBleedSeconds = 20f;
        [Tooltip("Which way the sharp side faces, in this object's own space (across the seabed). Zero = sharp " +
                 "all the way round.")]
        [SerializeField] Vector3 sharpSide;
        [Tooltip("How far round from the sharp side's middle it's still sharp, in degrees. Pushing from " +
                 "anywhere else is safe.")]
        [Range(0f, 180f)][SerializeField] float sharpSpread = 70f;
        [Tooltip("The blades, glinting red so it's obvious which side not to push from. They don't take the " +
                 "push glow.")]
        [SerializeField] Renderer sharpRenderer;
        [ColorUsage(false, true)][SerializeField] Color sharpGlow = new Color(1f, 0.12f, 0.08f);
        [SerializeField] float sharpGlowIntensity = 2.5f;

        [Header("Look")]
        [Tooltip("The model (a child of this object): it wobbles when pushed too weakly to move and shrinks " +
                 "away down a sinkhole. Empty = the first child.")]
        [SerializeField] Transform model;
        [ColorUsage(false, true)][SerializeField] Color idleGlow = new Color(0.45f, 0.75f, 1f);
        [ColorUsage(false, true)][SerializeField] Color strainGlow = new Color(1f, 0.6f, 0.15f);
        [ColorUsage(false, true)][SerializeField] Color movingGlow = new Color(0.35f, 1f, 0.45f);
        [Tooltip("Glow while nobody's pushing - just enough to spot it on the dark seabed.")]
        [SerializeField] float glowIntensity = 0.6f;
        [Tooltip("Glow while being pushed (amber = not enough fish, green = moving).")]
        [SerializeField] float pushedGlowIntensity = 1.2f;
        [SerializeField] float glowPulseRate = 0.5f;
        [Tooltip("Seconds it takes to disappear down a sinkhole.")]
        [SerializeField] float sinkSeconds = 2f;

        /// <summary>Every piece of trash in the world.</summary>
        public static readonly List<PushableTrash> All = new List<PushableTrash>();

        [SyncVar] byte _pushers;  // fish the server counts as pushing it right now
        [SyncVar] bool _moving;
        [SyncVar(hook = nameof(OnSunkChanged))] bool _sunk;
        [SyncVar] Vector3 _holeCentre;

        public string DisplayName => displayName;
        public int Points => points;
        public int FishNeeded => fishNeeded;
        public int Pushers => _pushers;
        public float MaxSpeed => maxSpeed;
        public bool IsSharp => cutEverySeconds > 0f;
        /// <summary>The sharp side's direction in the trash's own space, flat (zero = sharp all round).</summary>
        public Vector3 SharpSideLocal => new Vector3(sharpSide.x, 0f, sharpSide.z);

        /// <summary>Would something at <paramref name="position"/> be pushing on the sharp side?</summary>
        public bool IsSharpTowards(Vector3 position)
        {
            if (!IsSharp) return false;
            Vector3 side = transform.TransformDirection(SharpSideLocal);
            side.y = 0f;
            if (side.sqrMagnitude < 1e-6f) return true; // sharp all round
            Vector3 to = position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-6f) return true;
            return Vector3.Dot(to.normalized, side.normalized) >= Mathf.Cos(sharpSpread * Mathf.Deg2Rad);
        }
        public bool CanBePushed => !_sunk;
        public float Radius { get; private set; }
        public Vector3 Centre => transform.position;

        Rigidbody _rb;
        Vector3 _velocity;
        Vector3 _spin;           // tumble in open water (radians/s, world axis * rate)
        float _restY = float.NegativeInfinity; // where its middle sits when resting on the seabed below it
        float _surfaceY = float.PositiveInfinity;
        bool _surfaceKnown;
        float _sinkTime = -1f;   // Time.time the sinking started on this machine
        Vector3 _sinkFrom;
        Vector3 _modelPos, _modelScale;
        Renderer[] _renderers;
        MaterialPropertyBlock _glow;
        float _phase;
        MeshCollider _seabed;
        static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
        static readonly int EmissiveWeightId = Shader.PropertyToID("_EmissiveExposureWeight");

        bool Offline => !NetworkServer.active && !NetworkClient.active;
        bool Authority => Offline || isServer;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true; // moved by this script on the server, by the NetworkTransform elsewhere
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            Radius = radius;
            if (Radius <= 0f && TryGetComponent(out SphereCollider sphere))
            {
                Vector3 s = transform.lossyScale;
                Radius = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
            }
            if (Radius <= 0f) Radius = 0.5f;

            if (model == null && transform.childCount > 0) model = transform.GetChild(0);
            if (model != null) { _modelPos = model.localPosition; _modelScale = model.localScale; }
            var glowing = new List<Renderer>(GetComponentsInChildren<Renderer>(true));
            glowing.Remove(sharpRenderer); // the blades keep their own warning glint
            _renderers = glowing.ToArray();
            _glow = new MaterialPropertyBlock();
            _phase = Random.value * 10f;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void FixedUpdate()
        {
            if (!Authority) return;
            if (_sunk) SinkStep();
            else Simulate(Time.fixedDeltaTime);
        }

        void Simulate(float dt)
        {
            // Who's pushing it, and which way. Each fish's machine says what it's pushing; here we check
            // it's really up against us and add the pushes up.
            int count = 0;
            Vector3 sum = Vector3.zero;
            foreach (var p in TrashPusher.All)
            {
                if (p == null || p.Pushing != this) continue;
                if (p.Vitals != null && p.Vitals.IsDead) continue;
                if (!p.ServerTouching(this)) continue;
                count++;
                sum += p.PushDirection(this);

                // Sharp edges: pushing on the blades cuts every so often - and a cut fish bleeds. From the
                // smooth side it's safe.
                if (p.Vitals != null && IsSharpTowards(p.transform.position))
                {
                    p.SharpTime += dt;
                    if (p.SharpTime >= cutEverySeconds)
                    {
                        p.SharpTime = 0f;
                        p.Vitals.Damage(cutDamage, cutBleedSeconds);
                    }
                }
            }

            // Enough fish, and pushing the same way? Pushers aiming apart add up to less than their
            // number, so beyond Max Spread it won't budge. Pushes speed it up toward the pushers' pace;
            // they never hold it back, so it keeps whatever momentum it already has.
            float together = fishNeeded * Mathf.Cos(maxSpread * 0.5f * Mathf.Deg2Rad);
            bool moves = count >= fishNeeded && sum.magnitude >= together - 1e-3f;
            if (moves)
            {
                Vector3 dir = sum.normalized;
                float target = Mathf.Min(maxSpeed, pushSpeed * count / fishNeeded);
                float along = Vector3.Dot(_velocity, dir);
                if (along < target) _velocity += dir * Mathf.Min(acceleration * dt, target - along);
            }

            // The water: it drifts on after a push, slowing gradually, and - heavier than water - sinks.
            _velocity *= Mathf.Exp(-waterDrag * dt);
            _velocity.y -= sinkAcceleration * dt;

            // Down near the bottom by a sinkhole, it's drawn in - harder the closer it gets.
            Vector3 pos = _rb.position;
            var hole = Sinkhole.Nearest(pos, out float holeDist);
            if (hole != null && holeDist < hole.PullRadius && holeDist > 1e-3f && pos.y - _restY < 1.5f)
            {
                Vector3 toHole = hole.Centre - pos;
                toHole.y = 0f;
                _velocity += toHole / holeDist * (hole.Pull * (1f - holeDist / hole.PullRadius) * dt);
            }

            pos += _velocity * dt;
            KeepApart(ref pos);
            bool resting = StayInWater(ref pos, dt, moves);
            _rb.MovePosition(pos);

            // Rolls along the seabed; in open water a tumble carries on and slowly dies away. (A piece that
            // doesn't roll just scrapes and drifts, keeping its sharp side where it was.)
            if (rolls)
            {
                Vector3 flat = new Vector3(_velocity.x, 0f, _velocity.z);
                if (resting) _spin = Vector3.Cross(Vector3.up, flat) / Radius;
                else _spin *= Mathf.Exp(-spinDrag * dt);
                float rate = _spin.magnitude;
                if (rate > 1e-3f)
                    _rb.MoveRotation(Quaternion.AngleAxis(rate * dt * Mathf.Rad2Deg, _spin / rate) * _rb.rotation);
            }

            byte c = (byte)Mathf.Min(count, 255);
            if (_pushers != c) _pushers = c;
            bool m = moves && _velocity.sqrMagnitude > 0.01f; // being pushed along (not just drifting)
            if (_moving != m) _moving = m;

            // In once it's over a hole and down at the bottom (floating above one, it drops in as it sinks).
            hole = Sinkhole.Nearest(pos, out holeDist);
            if (hole != null && holeDist <= hole.CaptureRadius && pos.y - _restY <= Radius * 0.5f + 0.3f) Sink(hole);
        }

        // Pieces don't sit inside each other: each one steps out of any it overlaps (so a pushed piece
        // nudges a resting one along, or one lands on top of another and rolls off).
        void KeepApart(ref Vector3 pos)
        {
            foreach (var other in All)
            {
                if (other == this || other == null || other._sunk) continue;
                Vector3 d = pos - other.transform.position;
                float min = Radius + other.Radius;
                float dist = d.magnitude;
                if (dist >= min) continue;
                Vector3 away = dist > 1e-4f ? d / dist : Vector3.right;
                pos += away * ((min - dist) * 0.5f);
                float into = Vector3.Dot(_velocity, -away);
                if (into > 0f) _velocity += away * into; // don't keep driving into it
            }
        }

        // Keep it on the map, out of the seabed and under the surface. Returns whether it's resting on
        // the seabed - landing (with a little bounce), dragging on the sand, and rolling down steep slopes.
        // <paramref name="pushed"/>: fish are pushing it along right now, so it mustn't be held still.
        bool StayInWater(ref Vector3 pos, float dt, bool pushed)
        {
            if (!_surfaceKnown)
            {
                _surfaceKnown = true;
                var water = FindAnyObjectByType<UnityEngine.Rendering.HighDefinition.WaterSurface>();
                if (water != null) _surfaceY = water.transform.position.y;
            }
            if (pos.y > _surfaceY - Radius)
            {
                pos.y = _surfaceY - Radius;
                if (_velocity.y > 0f) _velocity.y = 0f;
            }

            if (_seabed == null)
            {
                var terrain = FindAnyObjectByType<TestTerrain>();
                if (terrain == null || !terrain.TryGetComponent(out _seabed)) return false;
            }
            if (_seabed.sharedMesh == null) return false;
            Bounds b = _seabed.bounds;
            float edge = Radius + 2f;
            pos.x = Mathf.Clamp(pos.x, b.min.x + edge, b.max.x - edge);
            pos.z = Mathf.Clamp(pos.z, b.min.z + edge, b.max.z - edge);
            var ray = new Ray(new Vector3(pos.x, b.max.y + 5f, pos.z), Vector3.down);
            if (!_seabed.Raycast(ray, out var hit, b.size.y + 10f)) return false;

            _restY = hit.point.y + Radius * (1f - sinkIntoSand);
            if (pos.y > _restY + 0.02f) return false; // still in open water

            pos.y = _restY;
            if (_velocity.y < 0f) _velocity.y = _velocity.y < -0.4f ? -_velocity.y * bounce : 0f;
            float friction = Mathf.Exp(-groundFriction * dt);
            _velocity.x *= friction;
            _velocity.z *= friction;
            // On a steep slope, sinking pulls it downhill; on a gentle one, once it's stopped and nobody's
            // pushing, it settles and stays put. (Held still while pushed, a heavy piece would never get going:
            // one step of a push is less than the "stopped" speed.)
            float steepness = Mathf.Sqrt(hit.normal.x * hit.normal.x + hit.normal.z * hit.normal.z); // sin of the slope
            if (steepness > Mathf.Sin(slideAboveDegrees * Mathf.Deg2Rad))
            {
                _velocity.x += hit.normal.x * sinkAcceleration * slopeSlide * dt;
                _velocity.z += hit.normal.z * sinkAcceleration * slopeSlide * dt;
            }
            else if (!pushed && _velocity.x * _velocity.x + _velocity.z * _velocity.z < 0.05f * 0.05f)
            {
                _velocity.x = 0f;
                _velocity.z = 0f;
            }
            return true;
        }

        // ---------------------------------------------------------------- down the hole

        void Sink(Sinkhole hole)
        {
            _sunk = true;
            _holeCentre = hole.Centre;
            _pushers = 0;
            _moving = false;
            BeginSinking();
            hole.ServerSwallow(this);
        }

        void OnSunkChanged(bool _, bool sunk)
        {
            if (sunk) BeginSinking();
        }

        void BeginSinking()
        {
            if (_sinkTime >= 0f) return;
            _sinkTime = Time.time;
            _sinkFrom = transform.position;
        }

        // Server: slide to the middle of the hole and down out of sight, then go.
        void SinkStep()
        {
            if (_sinkTime < 0f) BeginSinking();
            float t = Mathf.Clamp01((Time.time - _sinkTime) / Mathf.Max(sinkSeconds, 0.1f));
            Vector3 bottom = _holeCentre + Vector3.down * (Radius * 2f + 1.5f);
            Vector3 over = new Vector3(_holeCentre.x, _sinkFrom.y, _holeCentre.z);
            // First a third of the time sliding over the middle, then down out of sight.
            Vector3 pos = t < 0.35f
                ? Vector3.Lerp(_sinkFrom, over, Smooth(t / 0.35f))
                : Vector3.Lerp(over, bottom, Smooth((t - 0.35f) / 0.65f));
            _rb.MovePosition(pos);
            _rb.MoveRotation(Quaternion.AngleAxis(140f * Time.fixedDeltaTime, Vector3.up) * _rb.rotation);

            if (Time.time - _sinkTime >= sinkSeconds + 0.3f)
            {
                if (NetworkServer.active) NetworkServer.Destroy(gameObject);
                else Destroy(gameObject);
            }
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // ---------------------------------------------------------------- looks (every machine)

        void Update()
        {
            UpdateGlow();
            if (model == null) return;

            if (_sinkTime >= 0f)
            {
                float t = Mathf.Clamp01((Time.time - _sinkTime) / Mathf.Max(sinkSeconds, 0.1f));
                model.localScale = _modelScale * Mathf.Lerp(1f, 0.2f, t * t);
                model.localPosition = _modelPos;
                return;
            }

            // Straining: pushed, but not enough to move it - a shudder says "more fish, same way".
            if (_pushers > 0 && !_moving)
            {
                float k = 0.06f * Radius / Mathf.Max(transform.lossyScale.x, 1e-3f);
                float s = Time.time * 22f;
                model.localPosition = _modelPos + new Vector3(
                    (Mathf.PerlinNoise(s, _phase) - 0.5f) * k,
                    (Mathf.PerlinNoise(_phase, s) - 0.5f) * k * 0.5f,
                    (Mathf.PerlinNoise(s + 3.1f, _phase + 7.7f) - 0.5f) * k);
            }
            else model.localPosition = _modelPos;
        }

        // Exposure weight 0 keeps HDRP's camera exposure from dimming the glow to nothing underwater.
        void UpdateGlow()
        {
            // The blades: a red glint, flickering like light catching an edge.
            if (sharpRenderer != null)
            {
                float glint = _sunk ? 0f : 0.7f + 0.3f * Mathf.PerlinNoise(Time.time * 1.7f, _phase);
                sharpRenderer.GetPropertyBlock(_glow);
                _glow.SetColor(EmissiveColorId, sharpGlow * (sharpGlowIntensity * glint));
                _glow.SetFloat(EmissiveWeightId, 0f);
                sharpRenderer.SetPropertyBlock(_glow);
            }
            if (_renderers == null || _renderers.Length == 0) return;
            Color c = _sunk ? Color.black
                    : _moving ? movingGlow
                    : _pushers > 0 ? strainGlow
                    : idleGlow;
            bool active = _pushers > 0 || _moving;
            float pulse = active ? 1f : 0.55f + 0.45f * Mathf.Sin((Time.time + _phase) * glowPulseRate * 2f * Mathf.PI);
            Color emissive = c * ((active ? pushedGlowIntensity : glowIntensity) * pulse);
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_glow);
                _glow.SetColor(EmissiveColorId, emissive);
                _glow.SetFloat(EmissiveWeightId, 0f);
                r.SetPropertyBlock(_glow);
            }
        }
    }
}
