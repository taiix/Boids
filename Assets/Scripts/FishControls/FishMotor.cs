using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame
{
    /// <summary>
    /// The physics "engine" of a fish. It is dumb on purpose: something else (a player
    /// controller or an AI brain) fills in <see cref="Input"/> each frame, and the motor
    /// turns that into smooth, momentum-driven swimming.
    ///
    /// Real-fish principles baked in here:
    ///   * Thrust is always along the fish's own forward axis (the tail pushes it where the nose points).
    ///   * Orientation EASES toward the aim direction instead of snapping (the head leads, the body follows).
    ///   * Velocity has inertia + water drag, so you accelerate with "tail beats" and coast when you stop (burst-and-coast).
    ///   * The fish banks (rolls) into turns; we measure the turn rate here and expose it for the visuals.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FishMotor : MonoBehaviour
    {
        /// <summary>Per-frame command from whatever is driving this fish.</summary>
        public struct ControlInput
        {
            /// <summary>World-space direction the fish should aim toward (need not be normalized).</summary>
            public Vector3 AimDirection;
            /// <summary>Forward throttle, -1 (reverse) .. 1 (full ahead).</summary>
            public float Throttle;
            /// <summary>Sideways nudge, -1 .. 1 (gentle strafe, for fine positioning).</summary>
            public float Strafe;
            /// <summary>Vertical nudge, -1 (dive) .. 1 (rise), on top of aiming up/down.</summary>
            public float Ascend;
            /// <summary>Hold to swim faster (sprint). The hunter's burst dash is a separate ability.</summary>
            public bool Boost;
        }

        [Header("Cruise")]
        [Tooltip("Top speed at full throttle (m/s).")]
        [SerializeField] float maxSpeed = 6f;
        [Tooltip("How hard the tail pushes. Higher = reaches top speed faster.")]
        [SerializeField] float acceleration = 14f;
        [Tooltip("Water resistance. Higher = stops sooner / glides less. This is what makes it 'coast'.")]
        [SerializeField] float drag = 1.6f;

        [Header("Boost (hold Sprint)")]
        [SerializeField] float boostSpeed = 11f;
        [SerializeField] float boostAcceleration = 26f;

        [Header("Turning")]
        [Tooltip("How quickly the fish swings to face your aim. Higher = sharper, snappier turns; lower = lazier, wider arcs (big/fast fish).")]
        [SerializeField] float turnResponsiveness = 6f;
        [Tooltip("Turn responsiveness while boosting. Fast fish should turn WIDER, so keep this <= the cruise value.")]
        [SerializeField] float boostTurnResponsiveness = 4f;

        [Header("Fine control")]
        [SerializeField] float strafeAcceleration = 6f;
        [SerializeField] float verticalAcceleration = 8f;

        [Header("Dash / Lunge")]
        [Tooltip("How hard a dash drives the fish up to its dash speed. Keep high for an instant, snappy burst.")]
        [SerializeField] float dashAcceleration = 90f;
        [Tooltip("How fast the fish bleeds off above-max speed after a dash (m/s per second). " +
                 "Lower = longer, smoother coast-out; higher = stops sooner.")]
        [SerializeField] float overspeedDecel = 22f;

        [Header("Water / breaching")]
        [Tooltip("HDRP Water Surface used to know where the surface is. Leave empty to use the scene's " +
                 "(found automatically - a prefab can't reference a scene object, so spawned players " +
                 "always start without one).")]
        [SerializeField] WaterSurface waterSurface;
        [Tooltip("Downward acceleration once the fish leaves the water (m/s^2). Higher = snappier, " +
                 "more arcade arcs; ~9.81 is real gravity.")]
        [SerializeField] float airGravity = 16f;
        [Tooltip("Air resistance while airborne. Keep small — air barely slows a leaping fish.")]
        [SerializeField] float airDrag = 0.05f;
        [Tooltip("The fish counts as airborne once its pivot rises this far above the surface. " +
                 "Raise it a little if the pivot sits low in the body so it fully clears before falling.")]
        [SerializeField] float exitOffset = 0f;
        [Tooltip("How quickly the fish rotates to point along its arc while airborne.")]
        [SerializeField] float airOrientResponsiveness = 4f;

        bool _isSubmerged = true;
        bool _warnedNoWater;
        int _waterFailStreak;
        float _nextWaterLookup;

        Rigidbody _rb;
        Vector3 _velocity;
        ControlInput _input;
        float _yawRateSmoothed;

        // Dash state (used by abilities like the shark's attack + lunge).
        float _dashTimer;
        float _dashSpeed;
        float _dashSteer;

        // Temporary cruise-speed cap (e.g. while blended into the flock). 0 = no override.
        float _speedLimit;


        // Motion as seen from outside. Another player's copy is moved by the network with this motor
        // switched off (so is a body blended into a flock), which would leave the values below at zero
        // and its swim animation frozen. While the motor isn't driving, they're measured from how the
        // body actually moves instead - sampled once per frame by whoever asks.
        Vector3 _obsPos, _obsFwd, _obsVelocity;
        float _obsYawRate, _obsTime = -1f;
        int _obsFrame = -1;

        public Vector3 Velocity
        {
            get
            {
                if (enabled) return _velocity;
                SampleObservedMotion();
                return _obsVelocity;
            }
        }

        public float Speed => Velocity.magnitude;
        //0..1 fraction of top (boost) speed. Handy for driving tail-beat frequency / FOV / audio.
        public float SpeedFraction => Mathf.Clamp01(Velocity.magnitude / Mathf.Max(0.01f, boostSpeed));
        //Smoothed horizontal turn rate in deg/sec. Positive = turning right. Drives banking.
        public float YawRate
        {
            get
            {
                if (enabled) return _yawRateSmoothed;
                SampleObservedMotion();
                return _obsYawRate;
            }
        }

        void SampleObservedMotion()
        {
            if (_obsFrame == Time.frameCount) return;
            _obsFrame = Time.frameCount;

            Vector3 pos = transform.position;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            float dt = Time.time - _obsTime;
            if (_obsTime >= 0f && dt > 0f && dt < 0.5f)
            {
                // Same smoothing as the motor's own yaw rate; also evens out network interpolation steps.
                float k = 1f - Mathf.Exp(-10f * dt);
                _obsVelocity = Vector3.Lerp(_obsVelocity, (pos - _obsPos) / dt, k);
                float yawRate = fwd.sqrMagnitude > 0.0001f && _obsFwd.sqrMagnitude > 0.0001f
                    ? Vector3.SignedAngle(_obsFwd, fwd, Vector3.up) / dt
                    : 0f;
                _obsYawRate = Mathf.Lerp(_obsYawRate, yawRate, k);
            }
            else
            {
                // First look, or nobody asked for a while (e.g. just handed over from the motor).
                _obsVelocity = enabled ? _velocity : Vector3.zero;
                _obsYawRate = 0f;
            }
            _obsPos = pos;
            _obsFwd = fwd;
            _obsTime = Time.time;
        }

        /// <summary>True while the fish is in the water; false while airborne (breaching/jumping).</summary>
        public bool IsSubmerged => _isSubmerged;

        //Called by the driver (player/AI) every frame to set intent.
        public void SetInput(in ControlInput input) => _input = input;

        /// <summary>Forward throttle the driver is asking for right now (0 while the motor is off), e.g. to
        /// tell a fish swimming into something from one just resting against it.</summary>
        public float Throttle => enabled ? _input.Throttle : 0f;

        /// <summary>Force the internal velocity, e.g. when resuming control after an external
        /// takeover (flock blend) so the motor continues smoothly instead of snapping.</summary>
        public void SetVelocity(Vector3 v) => _velocity = v;

        /// <summary>True while a dash/lunge is in progress.</summary>
        public bool IsDashing => _dashTimer > 0f;

        /// <summary>A strong current (<see cref="WorldEvents"/>): added to every body's own velocity underwater.</summary>
        public static Vector3 Flow;
        /// <summary>Water moving differently from place to place (the storm churning near the surface), added on
        /// top of <see cref="Flow"/>. Null when nothing is happening.</summary>
        public static System.Func<Vector3, Vector3> FlowAt;
        /// <summary>How fast anyone can swim at a spot (the cold layer), 1 = normal. Null when nothing is happening.</summary>
        public static System.Func<Vector3, float> SpeedScaleAt;

        float _slowedFor, _slowFactor = 1f;

        /// <summary>Stung or numbed: top speed and acceleration times <paramref name="factor"/> for <paramref name="seconds"/>.</summary>
        /// <summary>A lasting slow on this body (a hurt shark, SharkCondition): multiplies its speeds; 1 = none.
        /// Separate from <see cref="SlowFor"/>, so the two stack.</summary>
        public float SpeedScale { get; set; } = 1f;

        public void SlowFor(float seconds, float factor)
        {
            _slowedFor = Mathf.Max(_slowedFor, seconds);
            _slowFactor = Mathf.Clamp01(factor);
        }

        float _entangledFor;
        Vector3 _entangledCarry;

        /// <summary>Caught in a trawl net: no control for <paramref name="seconds"/>, carried along at
        /// <paramref name="carry"/> (the net's velocity).</summary>
        public void Entangle(float seconds, Vector3 carry)
        {
            _entangledFor = Mathf.Max(_entangledFor, seconds);
            _entangledCarry = carry;
            _dashTimer = 0f;
        }

        public bool IsEntangled => _entangledFor > 0f;

        /// <summary>Cruise top speed (m/s) at full throttle.</summary>
        public float MaxSpeed => maxSpeed;
        /// <summary>Top speed (m/s) while sprinting.</summary>
        public float BoostSpeed => boostSpeed;

        float _frozenFor;

        /// <summary>Hit stop: hold the body dead still for <paramref name="seconds"/>, then carry on with the
        /// velocity it had (<see cref="GameFeel.HitStop"/>).</summary>
        public void Freeze(float seconds) => _frozenFor = Mathf.Max(_frozenFor, seconds);

        /// <summary>Temporarily cap cruise speed (e.g. to match the flock's pace). 0 clears it.</summary>
        public void SetSpeedLimit(float limit) => _speedLimit = Mathf.Max(0f, limit);
        public void ClearSpeedLimit() => _speedLimit = 0f;

        /// <summary>
        /// Burst forward along the current heading. Used for the shark's attack dash and big lunge.
        /// During the dash, normal throttle is ignored and steering is reduced to <paramref name="steerControl"/>
        /// (0 = locked straight, 1 = full control) so a lunge feels committed.
        /// </summary>
        public void BeginDash(float speed, float duration, float steerControl = 0.35f)
        {
            _dashSpeed = speed;
            _dashTimer = duration;
            _dashSteer = Mathf.Clamp01(steerControl);
            // Instant burst so the lunge reads as "really quick".
            _velocity = (_rb.rotation * Vector3.forward) * speed;
        }

        /// <summary>
        /// End a dash early (e.g. the shark caught a fish). <paramref name="keepSpeedFactor"/> 0 = full
        /// stop so the shark settles in to bite; 1 = keep current speed.
        /// </summary>
        public void CancelDash(float keepSpeedFactor = 0f)
        {
            _dashTimer = 0f;
            _velocity *= Mathf.Clamp01(keepSpeedFactor);
        }

        void Reset()
        {
            //Rigidbody defaults 
            var rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearDamping = 0f;   //model drag ourselves for a tunable, predictable feel
            rb.angularDamping = 0f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            UseWaterSurface(waterSurface);
        }

        // Without a surface the fish would swim straight up into the sky, so find the scene's when none
        // is assigned (retried once a second, in case the water loads after us).
        void EnsureWaterSurface()
        {
            if (waterSurface != null || Time.time < _nextWaterLookup) return;
            _nextWaterLookup = Time.time + 1f;
            UseWaterSurface(FindAnyObjectByType<WaterSurface>());
        }

        void UseWaterSurface(WaterSurface surface)
        {
            waterSurface = surface;
            if (waterSurface != null && !waterSurface.scriptInteractions)
                waterSurface.scriptInteractions = true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // Hit stop: frozen in place for a moment; the stored velocity carries on afterwards.
            if (_frozenFor > 0f)
            {
                _frozenFor -= dt;
                _rb.linearVelocity = Vector3.zero;
                return;
            }

            // --- Water check: below the surface we swim, above it we go ballistic ---------
            EnsureWaterSurface();
            _isSubmerged = true;
            if (waterSurface != null && SampleWaterHeight(_rb.position, out float waterY))
                _isSubmerged = _rb.position.y < waterY + exitOffset;

            if (!_isSubmerged)
            {
                AirborneStep(dt);
                return;
            }

            // Tangled in a net: it carries us, and nothing we do matters until it lets go.
            if (_entangledFor > 0f)
            {
                _entangledFor -= dt;
                _velocity = _entangledCarry;
                _rb.linearVelocity = _velocity;
                return;
            }

            bool dashing = _dashTimer > 0f;
            if (dashing) _dashTimer -= dt;
            bool boosting = !dashing && _input.Boost && _input.Throttle > 0.01f;

            // --- Orientation: ease toward where the player is aiming ----------------------
            Quaternion currentRot = _rb.rotation;
            Quaternion targetRot = currentRot;
            if (_input.AimDirection.sqrMagnitude > 0.0001f)
                targetRot = Quaternion.LookRotation(_input.AimDirection.normalized, Vector3.up);

            // Exponential smoothing -> framerate independent and feels "weighted" like a body in water.
            // A lunge reduces steering so it stays committed.
            float turn = dashing ? turnResponsiveness * _dashSteer
                       : boosting ? boostTurnResponsiveness : turnResponsiveness;
            float t = 1f - Mathf.Exp(-turn * dt);
            Quaternion newRot = Quaternion.Slerp(currentRot, targetRot, t);

            // Measure how fast we're yawing (for banking). Compare flattened forward vectors.
            Vector3 oldFwd = currentRot * Vector3.forward; oldFwd.y = 0f;
            Vector3 newFwd = newRot * Vector3.forward; newFwd.y = 0f;
            if (oldFwd.sqrMagnitude > 0.0001f && newFwd.sqrMagnitude > 0.0001f)
            {
                float yawRate = Vector3.SignedAngle(oldFwd, newFwd, Vector3.up) / dt;
                _yawRateSmoothed = Mathf.Lerp(_yawRateSmoothed, yawRate, 1f - Mathf.Exp(-10f * dt));
            }

            _rb.MoveRotation(newRot);
            _rb.angularVelocity = Vector3.zero; // we own rotation; kill any spin from collisions

            // --- Velocity: thrust along forward, plus drag + momentum --------------------
            float cruiseMax = _speedLimit > 0f ? _speedLimit : maxSpeed;
            float maxSpd = dashing ? _dashSpeed : (boosting ? boostSpeed : cruiseMax);
            float accel = dashing ? dashAcceleration : (boosting ? boostAcceleration : acceleration);
            // Cold water, a sting: everything we do is slower.
            float slow = SpeedScaleAt != null ? Mathf.Clamp01(SpeedScaleAt(_rb.position)) : 1f;
            if (_slowedFor > 0f) { _slowedFor -= dt; slow *= _slowFactor; }
            slow *= Mathf.Clamp01(SpeedScale);
            maxSpd *= slow;
            accel *= slow;
            float throttle = dashing ? 1f : _input.Throttle; // dash forces full forward

            Vector3 forward = newRot * Vector3.forward;
            Vector3 right = newRot * Vector3.right;

            Vector3 thrust = forward * (throttle * accel);
            if (!dashing) // no strafing / vertical nudging mid-lunge
            {
                thrust += right * (_input.Strafe * strafeAcceleration);
                thrust += Vector3.up * (_input.Ascend * verticalAcceleration);
            }

            _velocity += thrust * dt;
            _velocity *= Mathf.Exp(-drag * dt); // water resistance -> smooth coast-down

            float speed = _velocity.magnitude;
            if (speed > maxSpd)
            {
                // Smoothly bleed off above-max speed so a dash COASTS out instead of snapping
                // to the cap. (Normal driving barely overshoots, so this stays crisp there.)
                float reduced = Mathf.MoveTowards(speed, maxSpd, overspeedDecel * dt);
                _velocity *= reduced / speed;
            }

            Vector3 flow = Flow + (FlowAt != null ? FlowAt(_rb.position) : Vector3.zero);
            _rb.linearVelocity = _velocity + flow; // the water carries us on top of our own swimming
        }

        // Ballistic flight while out of the water: gravity + light air drag, nose follows the arc.
        // The velocity carried over from the water IS the launch momentum, so a fast upward exit
        // makes a high jump (real projectile motion: apex height ~ v_up^2 / 2g).
        void AirborneStep(float dt)
        {
            _dashTimer = 0f; // can't dash through the air

            _velocity += Vector3.down * (airGravity * dt);
            _velocity *= Mathf.Exp(-airDrag * dt);

            // Point along the direction of travel so the fish arcs head-first.
            if (_velocity.sqrMagnitude > 0.5f)
            {
                Quaternion target = Quaternion.LookRotation(_velocity.normalized, Vector3.up);
                Quaternion newRot = Quaternion.Slerp(_rb.rotation, target, 1f - Mathf.Exp(-airOrientResponsiveness * dt));
                _rb.MoveRotation(newRot);
                _rb.angularVelocity = Vector3.zero;
            }

            _rb.linearVelocity = _velocity;
        }

        // Wave-accurate water height at a world position via the HDRP Water Surface.
        bool SampleWaterHeight(Vector3 pos, out float height)
        {
            var sp = new WaterSearchParameters
            {
                startPositionWS = pos,
                targetPositionWS = pos,
                error = 0.01f,
                maxIterations = 8,
                includeDeformation = true,
                outputNormal = false,
            };

            if (waterSurface.ProjectPointOnWaterSurface(sp, out WaterSearchResult sr))
            {
                _waterFailStreak = 0;
                height = sr.projectedPositionWS.y;
                return true;
            }

            // Tolerate a brief warmup (CPU/GPU buffers aren't ready for the first frames);
            // only warn if sampling stays broken (~2s), which means a settings issue.
            if (++_waterFailStreak == 120 && !_warnedNoWater)
            {
                _warnedNoWater = true;
                Debug.LogWarning($"[FishMotor] Still can't sample '{waterSurface.name}' after warmup. " +
                                 "Enable 'Script Interactions' on the Water Surface, AND CPU simulation in the " +
                                 "HDRP Asset's Water settings. Using its flat height (no waves) meanwhile.", this);
            }
            // Never "no surface": that let fish swim off into the sky. The flat level ignores waves,
            // which is fine for keeping everyone in the water.
            height = waterSurface.transform.position.y;
            return true;
        }
    }
}
