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
        [Tooltip("HDRP Water Surface used to know where the surface is. Leave empty to disable gravity " +
                 "(free-swim everywhere, like before).")]
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


        public Vector3 Velocity => _velocity;

        public float Speed => _velocity.magnitude;
        //0..1 fraction of top (boost) speed. Handy for driving tail-beat frequency / FOV / audio.
        public float SpeedFraction => Mathf.Clamp01(_velocity.magnitude / Mathf.Max(0.01f, boostSpeed));
        //Smoothed horizontal turn rate in deg/sec. Positive = turning right. Drives banking.
        public float YawRate => _yawRateSmoothed;

        /// <summary>True while the fish is in the water; false while airborne (breaching/jumping).</summary>
        public bool IsSubmerged => _isSubmerged;

        //Called by the driver (player/AI) every frame to set intent.
        public void SetInput(in ControlInput input) => _input = input;

        /// <summary>Force the internal velocity, e.g. when resuming control after an external
        /// takeover (flock blend) so the motor continues smoothly instead of snapping.</summary>
        public void SetVelocity(Vector3 v) => _velocity = v;

        /// <summary>True while a dash/lunge is in progress.</summary>
        public bool IsDashing => _dashTimer > 0f;

        /// <summary>Cruise top speed (m/s) at full throttle.</summary>
        public float MaxSpeed => maxSpeed;

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

            if (waterSurface != null && !waterSurface.scriptInteractions)
                waterSurface.scriptInteractions = true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // --- Water check: below the surface we swim, above it we go ballistic ---------
            _isSubmerged = true;
            if (waterSurface != null && SampleWaterHeight(_rb.position, out float waterY))
                _isSubmerged = _rb.position.y < waterY + exitOffset;

            if (!_isSubmerged)
            {
                AirborneStep(dt);
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

            _rb.linearVelocity = _velocity;
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
                                 "HDRP Asset's Water settings. Treating fish as submerged.", this);
            }
            height = 0f;
            return false;
        }
    }
}
