using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// One seagull, for ambience above the sea: it floats on the waves (riding them, paddling about, looking
    /// round, now and then preening, ruffling its feathers or rearing up to beat its wings), takes off - running on the water with hard wingbeats - flies and glides about over the sea for a
    /// while, banking into its turns, then comes down, flares and lands on the water to float again.
    ///
    /// The takes are Models/Seagull/Seagull.fbx (Blender/Seagull/export_seagull.py): Float (with FloatPreen,
    /// FloatShake and FloatFlap dropped in now and then), TakeOff, Fly, Glide, Land. The bird's origin is the waterline: floating, it sits on the water's height; in the air its body
    /// rides at that height above the origin. It moves itself (no root motion), timed to the takes: TakeOff runs
    /// along the water before it climbs; Land's feet meet the water <see cref="Touchdown"/> s in, which is when
    /// this brings it down to the surface.
    ///
    /// A shark near the surface close by scares it off the water (and the gulls round it, through
    /// <see cref="Seagulls"/>). Local to each machine, like the other wildlife: nothing about it matters to the game.
    /// </summary>
    public class Seagull : MonoBehaviour
    {
        // The takes' timing (24 fps): TakeOff 40 frames, Land 56 with its feet on the water at frame 30.
        public const float TakeOffLength = 40f / 24f, LandLength = 56f / 24f, Touchdown = 30f / 24f;
        // The floating extras' lengths; FloatFlap's wings slap the water, and it drops back in, at these times.
        const float PreenLength = 96f / 24f, ShakeLength = 40f / 24f, FlapLength = 64f / 24f;
        const float FlapSlap = 10f / 24f, FlapDrop = 46f / 24f;
        // Coming in to land: down a glide slope (metres down per metre on) to this height as it starts landing.
        const float GlideSlope = 0.2f, FinalHeight = 1.4f;

        public enum Mode { Float, TakeOff, Fly, Land }

        [SerializeField] Animator animator;

        [Header("Flying")]
        [Tooltip("Cruising speed, m/s.")]
        [SerializeField] float cruiseSpeed = 7.5f;
        [Tooltip("How high over the sea it flies about, in metres (min, max).")]
        [SerializeField] Vector2 cruiseHeight = new Vector2(6f, 18f);
        [Tooltip("Keep at least this high over land (the island).")]
        [SerializeField] float landClearance = 8f;
        [Tooltip("Fastest turn, degrees a second.")]
        [SerializeField] float turnRate = 45f;
        [Tooltip("How far it leans into its fastest turn, degrees.")]
        [SerializeField] float maxBank = 38f;
        [Tooltip("Seconds in the air before it looks for somewhere to land (min, max).")]
        [SerializeField] Vector2 flightTime = new Vector2(25f, 70f);

        [Header("Floating")]
        [Tooltip("Seconds on the water before it flies off again (min, max).")]
        [SerializeField] Vector2 floatTime = new Vector2(20f, 75f);
        [Tooltip("How fast it paddles about on the water, m/s.")]
        [SerializeField] float paddleSpeed = 0.15f;
        [Tooltip("Seconds between preening, ruffling its feathers or bathing, afloat (min, max).")]
        [SerializeField] Vector2 fidgetEvery = new Vector2(6f, 16f);

        [Header("Scared off the water")]
        [Tooltip("A shark this close (sideways, metres)...")]
        [SerializeField] float sharkScareRadius = 9f;
        [Tooltip("...and this near the surface (metres down) scares it off the water.")]
        [SerializeField] float sharkScareDepth = 4f;
        [Tooltip("Any other fish this close to it at the surface does too.")]
        [SerializeField] float fishScareRadius = 2.5f;

        /// <summary>The flock it belongs to (keeps to its area, spreads a scare).</summary>
        public Seagulls Flock { get; set; }
        public Mode State { get; private set; } = Mode.Float;

        static readonly int FloatState = Animator.StringToHash("Float"), TakeOffState = Animator.StringToHash("TakeOff"),
                            FlyState = Animator.StringToHash("Fly"), GlideState = Animator.StringToHash("Glide"),
                            LandState = Animator.StringToHash("Land"), PreenState = Animator.StringToHash("FloatPreen"),
                            ShakeState = Animator.StringToHash("FloatShake"), FlapState = Animator.StringToHash("FloatFlap");

        float _yaw, _yawRate, _bank, _pitch, _speed;
        float _t;                         // seconds into the current mode
        float _modeEnds;                  // floating: when it flies off; flying: when it looks for somewhere to land
        Vector3 _tilt = Vector3.up;       // afloat: the wave under it
        float _paddleYaw, _nextPaddleTurn;
        // Flying
        Vector3 _target;
        float _nextTarget, _cruiseY;
        bool _flapping, _landing;
        float _flapUntil, _glideUntil;
        // Taking off / landing
        Vector3 _from;
        float _fromSpeed;
        Vector3 _touchdown;
        bool _splashed, _leftWater;
        // Afloat: the extra it's doing (0 = none) and when it started / is next due.
        int _fidget;
        float _fidgetStart, _nextFidget;
        float _scareAt = -1f;
        Vector3 _scareFrom;

        void Reset() => animator = GetComponentInChildren<Animator>();

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        void Start()
        {
            _yaw = transform.eulerAngles.y;
            _paddleYaw = _yaw;
            _cruiseY = WaterFx.SurfaceY + Random.Range(cruiseHeight.x, cruiseHeight.y);
        }

        /// <summary>Begin afloat on the water here.</summary>
        public void StartFloating()
        {
            EnterFloat(0f);
            _modeEnds = Time.time + Random.Range(floatTime.x, floatTime.y) * Random.Range(0.2f, 1f);
            if (animator != null) animator.Play(FloatState, 0, Random.value);
        }

        /// <summary>Begin in the air here, going its way.</summary>
        public void StartFlying()
        {
            _yaw = transform.eulerAngles.y;
            _speed = cruiseSpeed;
            _cruiseY = transform.position.y;
            EnterFly(playAnim: false);
            _modeEnds = Time.time + Random.Range(flightTime.x, flightTime.y) * Random.Range(0.3f, 1f);
            if (animator != null) animator.Play(GlideState, 0, Random.value);
        }

        /// <summary>Something frightening at <paramref name="from"/>: off the water (after a startled moment).</summary>
        public void Scare(Vector3 from, float delay = 0f)
        {
            if (State != Mode.Float || _scareAt >= 0f) return;
            _scareAt = Time.time + delay;
            _scareFrom = from;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _t += dt;
            switch (State)
            {
                case Mode.Float: UpdateFloat(dt); break;
                case Mode.TakeOff: UpdateTakeOff(dt); break;
                case Mode.Fly: UpdateFly(dt); break;
                case Mode.Land: UpdateLand(dt); break;
            }
        }

        // ------------------------------------------------------------------ afloat

        void EnterFloat(float fade)
        {
            State = Mode.Float;
            _t = 0f;
            _scareAt = -1f;
            _bank = _pitch = 0f;
            _paddleYaw = _yaw;
            _nextPaddleTurn = Time.time + Random.Range(3f, 10f);
            _modeEnds = Time.time + Random.Range(floatTime.x, floatTime.y);
            _fidget = 0;
            _nextFidget = Time.time + Random.Range(fidgetEvery.x, fidgetEvery.y) * 0.6f;
            if (fade > 0f && animator != null) animator.CrossFadeInFixedTime(FloatState, fade, 0, 0f);
        }

        // Now and then, afloat: preens, ruffles its feathers, or rears up and beats its wings - then back to floating.
        void UpdateFidget(Vector3 p)
        {
            if (animator == null) return;
            float into = Time.time - _fidgetStart;
            if (_fidget == 0)
            {
                if (Time.time < _nextFidget) return;
                float roll = Random.value;
                _fidget = roll < 0.45f ? PreenState : roll < 0.8f ? ShakeState : FlapState;
                _fidgetStart = Time.time;
                _splashed = _leftWater = false;
                animator.CrossFadeInFixedTime(_fidget, 0.25f, 0, 0f);
                return;
            }
            if (_fidget == FlapState)
            {
                if (!_splashed && into >= FlapSlap) { _splashed = true; Splash(p, Vector3.up * 1.2f, 0.4f); }
                if (!_leftWater && into >= FlapDrop) { _leftWater = true; Splash(p, Vector3.down * 1.2f, 0.35f); }
            }
            float length = _fidget == PreenState ? PreenLength : _fidget == ShakeState ? ShakeLength : FlapLength;
            if (into >= length - 0.3f)
            {
                _fidget = 0;
                _nextFidget = Time.time + Random.Range(fidgetEvery.x, fidgetEvery.y);
                animator.CrossFadeInFixedTime(FloatState, 0.3f, 0, 0f);
            }
        }

        void UpdateFloat(float dt)
        {
            // Paddling about: now and then it turns to a new way, and keeps to its flock's patch of sea.
            if (Time.time >= _nextPaddleTurn)
            {
                _nextPaddleTurn = Time.time + Random.Range(4f, 12f);
                _paddleYaw = _yaw + Random.Range(-70f, 70f);
                if (Flock != null && !Flock.InArea(transform.position, 0.8f))
                    _paddleYaw = YawTo(Flock.Centre - transform.position);
            }
            _yaw = Mathf.MoveTowardsAngle(_yaw, _paddleYaw, 12f * dt);
            Vector3 p = transform.position + Forward(_yaw) * (paddleSpeed * dt);
            p.y = WaterFx.WaterHeightAt(p, out Vector3 normal);
            _tilt = Vector3.Slerp(_tilt, normal, 1f - Mathf.Exp(-3f * dt));
            transform.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.up, _tilt) * Quaternion.Euler(0f, _yaw, 0f));

            UpdateFidget(p);

            if (_scareAt < 0f && Threatened(p, out Vector3 threat))
            {
                Scare(threat);
                if (Flock != null) Flock.Startle(this, threat);
            }
            if (_scareAt >= 0f && Time.time >= _scareAt)
            {
                Vector3 away = p - _scareFrom;
                away.y = 0f;
                EnterTakeOff(away.sqrMagnitude > 0.01f ? YawTo(away) + Random.Range(-25f, 25f) : _yaw);
            }
            else if (_scareAt < 0f && Time.time >= _modeEnds && _fidget == 0)   // (finishes what it's doing first)
                EnterTakeOff(Flock != null && !Flock.InArea(p, 0.6f) ? YawTo(Flock.Centre - p) : _yaw + Random.Range(-60f, 60f));
        }

        bool Threatened(Vector3 p, out Vector3 threat)
        {
            threat = default;
            float surface = p.y;
            foreach (var fp in FishPlayer.All)
            {
                if (fp == null || !fp.isActiveAndEnabled || fp.IsOut) continue;
                Vector3 f = fp.transform.position;
                Vector3 d = f - p;
                d.y = 0f;
                bool shark = fp.IsSharkBody;
                float r = shark ? sharkScareRadius : fishScareRadius;
                float depth = shark ? sharkScareDepth : 1.5f;
                if (d.sqrMagnitude < r * r && f.y > surface - depth)
                {
                    threat = f;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ taking off

        void EnterTakeOff(float heading)
        {
            State = Mode.TakeOff;
            _t = 0f;
            _scareAt = -1f;
            _from = transform.position;
            _fromSpeed = 0f;
            _paddleYaw = heading;
            _splashed = _leftWater = false;
            if (animator != null) animator.CrossFadeInFixedTime(TakeOffState, 0.12f, 0, 0f);
        }

        void UpdateTakeOff(float dt)
        {
            // Turn into the way it's going while the wings open, then run along the water and climb away.
            _yaw = Mathf.MoveTowardsAngle(_yaw, _paddleYaw, 260f * dt);
            float run = Mathf.Clamp01(_t / (TakeOffLength * 0.95f));
            _speed = Mathf.Lerp(0.3f, cruiseSpeed * 0.85f, run * run * (3f - 2f * run));
            Vector3 p = transform.position + Forward(_yaw) * (_speed * dt);
            float water = WaterFx.WaterHeightAt(p);
            // Up off the water with the first beats, along it a moment (its feet pattering), then climbing.
            float rise = 0.1f * Smooth(_t / 0.35f) + Mathf.Pow(Mathf.Max(0f, _t - 0.5f), 2f) * 1.6f;
            p.y = water + rise;
            _pitch = Mathf.Lerp(_pitch, _t > 0.6f ? -10f : 0f, 1f - Mathf.Exp(-4f * dt));
            _tilt = Vector3.Slerp(_tilt, Vector3.up, 1f - Mathf.Exp(-6f * dt));
            transform.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.up, _tilt) * Quaternion.Euler(_pitch, _yaw, 0f));

            // The wings slap the water as it goes; a smaller splash as its feet leave it.
            if (!_splashed && _t >= 0.15f)
            {
                _splashed = true;
                Splash(p, new Vector3(0f, 1.5f, 0f), 0.45f);
            }
            if (!_leftWater && _t >= 0.75f)
            {
                _leftWater = true;
                Splash(p, Forward(_yaw) * _speed + Vector3.up, 0.3f);
            }

            if (_t >= TakeOffLength - 0.08f)
            {
                _cruiseY = WaterFx.SurfaceY + Random.Range(cruiseHeight.x, cruiseHeight.y);
                EnterFly(playAnim: true);
                _flapping = true;
                _flapUntil = Time.time + Random.Range(2.5f, 4.5f);   // climbs away flapping
                _modeEnds = Time.time + Random.Range(flightTime.x, flightTime.y);
            }
        }

        // ------------------------------------------------------------------ flying

        void EnterFly(bool playAnim)
        {
            State = Mode.Fly;
            _t = 0f;
            _landing = false;
            _nextTarget = 0f;
            _flapping = false;
            _glideUntil = Time.time + Random.Range(2f, 6f);
            if (playAnim && animator != null) animator.CrossFadeInFixedTime(FlyState, 0.15f, 0, 0f);
        }

        void UpdateFly(float dt)
        {
            Vector3 p = transform.position;
            float surface = WaterFx.SurfaceY;

            // Where to: wandering points over the sea; when its time's up, a stretch of open water to land on.
            if (!_landing && Time.time >= _modeEnds && FindLandingSpot(p, out _touchdown))
                _landing = true;
            if (!_landing && (Time.time >= _nextTarget || Flat(_target - p).sqrMagnitude < 10f * 10f))
            {
                _target = Flock != null ? Flock.RandomPoint() : p + Random.insideUnitSphere * 40f;
                _cruiseY = surface + Random.Range(cruiseHeight.x, cruiseHeight.y);
                _nextTarget = Time.time + Random.Range(10f, 22f);
            }
            Vector3 goal = _landing ? _touchdown : _target;
            float dist = Flat(goal - p).magnitude;

            // Steering: turn toward the goal (no faster than turnRate), leaning into the turn.
            float want = Mathf.DeltaAngle(_yaw, YawTo(goal - p));
            float rate = Mathf.Clamp(want * 1.2f, -turnRate, turnRate);
            _yawRate = Mathf.Lerp(_yawRate, rate, 1f - Mathf.Exp(-2.5f * dt));
            _yaw += _yawRate * dt;

            // Height: cruising (and well over the island); coming in to land, down toward the water.
            float ground = WaterFx.SeabedHeight(p, out float g) ? g : surface - 50f;
            float wantY = _cruiseY;
            float landRun = LandDistance(_speed);
            if (_landing)
                wantY = Mathf.Min(surface + FinalHeight + Mathf.Max(0f, dist - landRun) * GlideSlope, Mathf.Max(_cruiseY, surface + FinalHeight));
            wantY = Mathf.Max(wantY, ground + landClearance, surface + 0.8f);
            // On the glide slope it sinks with the slope as it goes (else it'd trail metres above it).
            bool onSlope = _landing && dist > landRun && wantY < _cruiseY - 0.5f;
            float climb = Mathf.Clamp((wantY - p.y) * 0.8f - (onSlope ? GlideSlope * _speed : 0f), -2.6f, 2.8f);

            // Flapping or gliding: flaps to climb and in bursts, glides most of the time and coming in to land.
            bool shouldFlap = climb > 0.9f || (!_landing && Time.time < _flapUntil);
            if (!_flapping && shouldFlap && (climb > 0.9f || Time.time >= _glideUntil))
            {
                _flapping = true;
                _flapUntil = Mathf.Max(_flapUntil, Time.time + Random.Range(1.5f, 3.5f));
                if (animator != null) animator.CrossFadeInFixedTime(FlyState, 0.35f, 0, 0f);
            }
            else if (_flapping && !shouldFlap)
            {
                _flapping = false;
                _glideUntil = Time.time + Random.Range(3f, 9f);
                if (animator != null) animator.CrossFadeInFixedTime(GlideState, 0.45f, 0, Random.value);
            }
            if (!_flapping && !_landing && Time.time >= _glideUntil && Random.value < dt * 0.15f)
                _flapUntil = Time.time + Random.Range(1.2f, 2.8f);   // a few beats now and then

            float wantSpeed = cruiseSpeed * (_landing ? 0.85f : 1f) - climb * 0.35f;
            _speed = Mathf.MoveTowards(_speed, wantSpeed, 1.5f * dt);
            p += Forward(_yaw) * (_speed * dt) + Vector3.up * (climb * dt);
            _bank = Mathf.Lerp(_bank, -_yawRate / turnRate * maxBank, 1f - Mathf.Exp(-3f * dt));
            _pitch = Mathf.Lerp(_pitch, -Mathf.Atan2(climb, Mathf.Max(1f, _speed)) * Mathf.Rad2Deg * 0.8f, 1f - Mathf.Exp(-3f * dt));
            transform.SetPositionAndRotation(p, Quaternion.Euler(_pitch, _yaw, _bank));

            if (_landing)
            {
                // Lined up, low and close enough: land. Too high or off line as it gets there: go round.
                float above = p.y - WaterFx.WaterHeightAt(p);
                if (dist <= landRun && above < 3f && Mathf.Abs(want) < 25f)
                    EnterLand();
                else if (dist < landRun * 0.5f && !FindLandingSpot(p, out _touchdown))
                    _landing = false;
            }
        }

        // Somewhere ahead-ish to land: open water (deep enough, not by the island), in the flock's area.
        bool FindLandingSpot(Vector3 from, out Vector3 spot)
        {
            for (int i = 0; i < 12; i++)
            {
                float yaw = _yaw + Random.Range(-70f, 70f);
                // Far enough to come down the glide slope from where it is.
                float need = Mathf.Max(30f, (from.y - WaterFx.SurfaceY - FinalHeight) / GlideSlope + 12f);
                Vector3 s = from + Forward(yaw) * Random.Range(need, need + 30f);
                if (Flock != null && !Flock.InArea(s, 0.85f)) continue;
                if (!Seagulls.OpenWater(s)) continue;
                s.y = WaterFx.SurfaceY;
                spot = s;
                return true;
            }
            spot = default;
            return false;
        }

        // How far it carries on from starting to land to its feet meeting the water.
        static float LandDistance(float speed) => (Mathf.Clamp(speed, 4f, 9f) + 1.2f) * 0.5f * Touchdown;

        // ------------------------------------------------------------------ landing

        void EnterLand()
        {
            State = Mode.Land;
            _t = 0f;
            _from = transform.position;
            _fromSpeed = Mathf.Clamp(_speed, 4f, 9f);
            _splashed = false;
            if (animator != null) animator.CrossFadeInFixedTime(LandState, 0.25f, 0, 0f);
        }

        void UpdateLand(float dt)
        {
            Vector3 p = transform.position;
            _bank = Mathf.Lerp(_bank, 0f, 1f - Mathf.Exp(-5f * dt));
            _pitch = Mathf.Lerp(_pitch, 0f, 1f - Mathf.Exp(-5f * dt));
            if (_t < Touchdown)
            {
                // Slowing from its glide to a walk as it comes down; the descent levels out as it flares.
                float s = _t / Touchdown;
                _speed = Mathf.Lerp(_fromSpeed, 1.2f, s);
                p += Forward(_yaw) * (_speed * dt);
                float water = WaterFx.WaterHeightAt(p);
                p.y = Mathf.Lerp(_from.y, water, 1f - (1f - s) * (1f - s));
                transform.SetPositionAndRotation(p, Quaternion.Euler(_pitch, _yaw, _bank));
                return;
            }
            if (!_splashed)
            {
                _splashed = true;
                Splash(p, Forward(_yaw) * 1.2f + Vector3.down * 1.5f, 0.5f);
            }
            // On the water: a short skid, riding the waves.
            _speed = Mathf.MoveTowards(_speed, 0f, 2.2f * dt);
            p += Forward(_yaw) * (_speed * dt);
            p.y = WaterFx.WaterHeightAt(p, out Vector3 normal);
            _tilt = Vector3.Slerp(_tilt, normal, 1f - Mathf.Exp(-3f * dt));
            transform.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.up, _tilt) * Quaternion.Euler(_pitch, _yaw, _bank));
            if (_t >= LandLength - 0.05f) EnterFloat(0.2f);
        }

        // ------------------------------------------------------------------ helpers

        // Splashes are the sea's shared 3D ones: only where someone could see them.
        static void Splash(Vector3 at, Vector3 velocity, float size)
        {
            var cam = WaterFx.ViewCamera;
            if (cam == null || (cam.transform.position - at).sqrMagnitude > 70f * 70f) return;
            WaterFx.Splash(at, velocity, size);
        }

        static Vector3 Forward(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        static float YawTo(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }
}
