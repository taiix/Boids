using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// The physics "engine room" of a ship. Like <see cref="FishMotor"/> it is deliberately
    /// dumb: something else (a helm, an autopilot, an AI) fills in <see cref="Input"/> each
    /// frame and this turns it into weight-driven motion.
    ///
    /// Buoyancy is NOT this script's job — <see cref="Floater"/> owns heave, roll and the
    /// waterline. This only ever pushes in the horizontal plane, so the two never fight.
    ///
    /// What makes a ship feel like a ship rather than a car:
    ///   * Thrust is constant for a given throttle; drag sets the top speed. You get a long,
    ///     asymptotic acceleration curve for free instead of easing toward a target speed.
    ///   * Sideways drag is far higher than forward drag. A hull slips forward and resists
    ///     sliding, which is what makes it carve a turn instead of drifting through it.
    ///   * The rudder only bites when water is flowing past it. Dead in the water, the wheel
    ///     does nothing — and going astern, it works in reverse.
    ///   * Astern power is a fraction of ahead power, because propellers are shaped one way.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ShipMotor : MonoBehaviour
    {
        /// <summary>Per-frame command from whatever is driving this ship.</summary>
        public struct ControlInput
        {
            /// <summary>Engine order, -1 (full astern) .. 1 (full ahead).</summary>
            public float Throttle;
            /// <summary>Wheel position, -1 (hard to port) .. 1 (hard to starboard).</summary>
            public float Rudder;
        }

        [Header("Engine")]
        [Tooltip("Speed reached at full ahead once drag balances thrust (m/s). ~11 m/s is about 22 knots.")]
        [SerializeField] float maxSpeed = 11f;
        [Tooltip("Fraction of ahead power available astern. Real screws are far less efficient in reverse.")]
        [Range(0.1f, 1f)] [SerializeField] float asternPowerFraction = 0.35f;
        [Tooltip("Seconds to reach roughly 95% of top speed from a standstill. This is the main 'weight' dial.")]
        [Min(1f)] [SerializeField] float timeToFullSpeed = 28f;

        [Header("Throttle Lag")]
        [Tooltip("Seconds for the engines to answer a change of order. Stops the hull from twitching when you tap a key.")]
        [Min(0f)] [SerializeField] float engineResponseTime = 2.5f;

        [Header("Steering")]
        [Tooltip("Fastest rate of turn, in degrees per second, at full rudder and full speed. A real liner is under 1; this is raised so the demo is drivable.")]
        [SerializeField] float maxTurnRate = 5f;
        [Tooltip("Speed at which the rudder reaches full authority (m/s). Below this it bites proportionally less; at a dead stop it does nothing.")]
        [Min(0.1f)] [SerializeField] float rudderEffectiveSpeed = 4f;
        [Tooltip("Seconds for the rudder to swing from centre to hard over. Keeps steering smooth.")]
        [Min(0f)] [SerializeField] float rudderResponseTime = 1.5f;

        [Header("Heel (leaning out of turns)")]
        [Tooltip("How hard a turn throws the ship over. The moment scales with speed AND rate of turn together, so hard-over at full ahead is punishing while the same wheel at dead slow barely leans. 0 disables heel.")]
        [Min(0f)] [SerializeField] float heelStrength = 600f;
        [Tooltip("The ship is never allowed to heel past this. The moment is clamped against the righting moment available at this angle, so a turn can frighten you but can never capsize you.")]
        [Range(0f, 45f)] [SerializeField] float maxHeelAngle = 22f;
        [Tooltip("Angle past which the helm is considered to have made a mistake. Purely informational — read HeelWarning for a UI cue.")]
        [Range(0f, 45f)] [SerializeField] float heelWarningAngle = 14f;
        [Tooltip("Speed scrubbed off while heeled over. A ship leaning hard drags its bilge through the water and slows down, which is what punishes over-steering.")]
        [Min(0f)] [SerializeField] float heelDrag = 0.12f;

        [Header("Hull Resistance")]
        [Tooltip("How strongly the hull resists sliding sideways, relative to forward. High values make it track and carve; low values let it skid.")]
        [Min(1f)] [SerializeField] float lateralDragRatio = 12f;
        [Tooltip("Damping on yaw, so the ship settles on a heading instead of weathervaning forever.")]
        [Min(0f)] [SerializeField] float yawDamping = 3f;

        Rigidbody rb;
        float engineOrder;   // throttle after lag
        float rudderAngle;   // rudder after swing time

        /// <summary>Filled in each frame by a helm or an AI; consumed in FixedUpdate.</summary>
        [System.NonSerialized] public ControlInput Input;

        /// <summary>Speed along the hull's own forward axis, in m/s. Negative when making sternway.</summary>
        public float ForwardSpeed { get; private set; }

        /// <summary>Speed over ground in the horizontal plane, in m/s.</summary>
        public float SpeedOverGround { get; private set; }

        /// <summary>Engine order actually being answered, -1..1, after lag. For gauges.</summary>
        public float EngineOrder => engineOrder;

        /// <summary>Rudder position actually reached, -1..1, after swing time. For gauges.</summary>
        public float RudderAngle => rudderAngle;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // Work in the horizontal plane only. Taking the hull's forward directly would let
            // pitch tilt the thrust and drive the bow under, and would fight the Floater.
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            Vector3 velocity = rb.linearVelocity;
            Vector3 planarVelocity = new Vector3(velocity.x, 0f, velocity.z);

            ForwardSpeed = Vector3.Dot(planarVelocity, forward);
            SpeedOverGround = planarVelocity.magnitude;

            // Telegraph the order down to the engine room rather than applying it instantly.
            engineOrder = Smooth(engineOrder, Mathf.Clamp(Input.Throttle, -1f, 1f), engineResponseTime, dt);
            rudderAngle = Smooth(rudderAngle, Mathf.Clamp(Input.Rudder, -1f, 1f), rudderResponseTime, dt);

            ApplyEngine(forward);
            ApplyHullResistance(right);
            ApplyRudder();
            ApplyHeel(forward);
        }

        // Constant thrust per unit throttle, with linear drag sized so the two balance exactly
        // at maxSpeed. Deriving both from timeToFullSpeed means changing the "weight" dial
        // never changes where the ship tops out.
        void ApplyEngine(Vector3 forward)
        {
            // An exponential approach covers ~95% of the gap in three time constants.
            float dragCoefficient = 3f / Mathf.Max(0.01f, timeToFullSpeed);
            float fullThrust = maxSpeed * dragCoefficient;

            float power = engineOrder >= 0f ? 1f : asternPowerFraction;
            float thrust = engineOrder * fullThrust * power;

            rb.AddForce(forward * thrust, ForceMode.Acceleration);
            rb.AddForce(forward * (-ForwardSpeed * dragCoefficient), ForceMode.Acceleration);
        }

        // The hull's grip on the water. Without this the ship would slide through turns like
        // a puck; with it, the bow leads and the stern follows.
        //
        // This force is also where heel comes from, which is why it is applied low on the hull
        // rather than at the centre of mass. The water pushes the ship round at the waterline
        // while the mass above carries straight on, and the couple between the two rolls the
        // ship OUT of the turn. Producing heel this way rather than as a bolted-on torque means
        // it scales correctly on its own: the sideways force already grows with speed and rate
        // of turn, so a hard turn at full ahead heels hard and the same wheel at dead slow
        // barely leans at all.
        void ApplyHullResistance(Vector3 right)
        {
            float dragCoefficient = 3f / Mathf.Max(0.01f, timeToFullSpeed);
            float lateralSpeed = Vector3.Dot(rb.linearVelocity, right);

            rb.AddForce(right * (-lateralSpeed * dragCoefficient * lateralDragRatio), ForceMode.Acceleration);
        }

        // Rudder authority scales with the water flowing past it, and reverses when making
        // sternway — back a ship down with the wheel over and the stern walks the other way.
        void ApplyRudder()
        {
            float flow = Mathf.Clamp(ForwardSpeed / rudderEffectiveSpeed, -1f, 1f);
            float targetTurnRate = rudderAngle * flow * maxTurnRate * Mathf.Deg2Rad;

            float yawRate = Vector3.Dot(rb.angularVelocity, Vector3.up);

            // Drive yaw toward the commanded rate rather than adding raw torque, so the turn
            // rate is what the tuning value says it is regardless of the hull's inertia.
            rb.AddTorque(Vector3.up * ((targetTurnRate - yawRate) * yawDamping), ForceMode.Acceleration);
        }

        // A ship leans OUT of a turn, not into it: the water pushes the hull round at the
        // waterline while the mass above carries straight on. Applying that couple through a
        // physical lever arm turned out to be far too weak to feel — a 14m beam on a 2.5m draft
        // scale is enormously stiff in roll, and 6m of lever bought about two degrees. So the
        // moment is applied directly and tuned by feel instead, while keeping the property that
        // matters: it scales with speed AND rate of turn together, so slowing down before the
        // turn is always the answer.
        void ApplyHeel(Vector3 forward)
        {
            if (heelStrength <= 0f) return;

            float yawRate = Vector3.Dot(rb.angularVelocity, Vector3.up);

            // Fade the moment out as the limit approaches. The righting moment keeps growing
            // while this one goes to zero, so heel converges on maxHeelAngle and the ship is
            // guaranteed to come back up however badly the turn was judged.
            // Cubic rather than linear: a linear fade starts bleeding the moment away from the
            // very first degree, so the interesting angles become asymptotically unreachable and
            // need absurd gains. Cubed, the moment stays near full through the usable range and
            // only collapses right at the limit.
            float t = Mathf.Clamp01(Mathf.Abs(HeelAngle) / Mathf.Max(1f, maxHeelAngle));
            float fade = 1f - t * t * t;
            float moment = -yawRate * ForwardSpeed * heelStrength * fade;

            rb.AddTorque(forward * moment, ForceMode.Acceleration);

            // Leaning over drags the bilge and costs speed, so a sloppy turn is slow as well as
            // ugly. This is what makes over-steering actually cost something.
            float heel = Mathf.Abs(HeelAngle) * Mathf.Deg2Rad;
            if (heelDrag > 0f && heel > 0.01f)
                rb.AddForce(forward * (-ForwardSpeed * heelDrag * Mathf.Sin(heel)), ForceMode.Acceleration);
        }

        /// <summary>Current roll, in degrees. Negative is to port.</summary>
        public float HeelAngle
        {
            // Signed angle between world up and the hull's up, measured about its own
            // fore-aft axis. Reading eulerAngles.z instead would fold pitch into the answer.
            get => Vector3.SignedAngle(Vector3.up, transform.up, transform.forward);
        }

        /// <summary>True once the ship is heeled far enough that the helm has overcooked it.</summary>
        public bool HeelWarning => Mathf.Abs(HeelAngle) >= heelWarningAngle;

        /// <summary>Heel as a fraction of the maximum, 0..1. Handy for driving a UI gauge.</summary>
        public float HeelFraction => Mathf.Clamp01(Mathf.Abs(HeelAngle) / Mathf.Max(0.01f, maxHeelAngle));

        static float Smooth(float current, float target, float responseTime, float dt)
        {
            if (responseTime <= 0f)
                return target;

            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / responseTime));
        }

        /// <summary>Cuts the engines and centres the wheel. Used by "all stop".</summary>
        public void AllStop()
        {
            Input.Throttle = 0f;
            Input.Rudder = 0f;
        }
    }
}
