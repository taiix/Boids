using UnityEngine;

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

        Rigidbody _rb;
        Vector3 _velocity;
        ControlInput _input;
        float _yawRateSmoothed;


        public Vector3 Velocity => _velocity;

        public float Speed => _velocity.magnitude;
        //0..1 fraction of top (boost) speed. Handy for driving tail-beat frequency / FOV / audio.
        public float SpeedFraction => Mathf.Clamp01(_velocity.magnitude / Mathf.Max(0.01f, boostSpeed));
        //Smoothed horizontal turn rate in deg/sec. Positive = turning right. Drives banking.
        public float YawRate => _yawRateSmoothed;

        //Called by the driver (player/AI) every frame to set intent.
        public void SetInput(in ControlInput input) => _input = input;

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
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool boosting = _input.Boost && _input.Throttle > 0.01f;

            // --- Orientation: ease toward where the player is aiming ----------------------
            Quaternion currentRot = _rb.rotation;
            Quaternion targetRot = currentRot;
            if (_input.AimDirection.sqrMagnitude > 0.0001f)
                targetRot = Quaternion.LookRotation(_input.AimDirection.normalized, Vector3.up);

            // Exponential smoothing -> framerate independent and feels "weighted" like a body in water.
            float turn = boosting ? boostTurnResponsiveness : turnResponsiveness;
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
            float maxSpd = boosting ? boostSpeed : maxSpeed;
            float accel = boosting ? boostAcceleration : acceleration;

            Vector3 forward = newRot * Vector3.forward;
            Vector3 right = newRot * Vector3.right;

            Vector3 thrust = forward * (_input.Throttle * accel);
            thrust += right * (_input.Strafe * strafeAcceleration);
            thrust += Vector3.up * (_input.Ascend * verticalAcceleration);

            _velocity += thrust * dt;
            _velocity *= Mathf.Exp(-drag * dt); // water resistance -> smooth coast-down

            float speed = _velocity.magnitude;
            if (speed > maxSpd)
                _velocity = _velocity * (maxSpd / speed);

            _rb.linearVelocity = _velocity;
        }
    }
}
