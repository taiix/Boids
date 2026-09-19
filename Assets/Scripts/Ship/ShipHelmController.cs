using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// Player helm for a <see cref="ShipMotor"/>. Reads the wheel and the engine telegraph and
    /// hands them to the motor; all the weight and lag live down in the motor.
    ///
    /// A ship's controls are held, not pressed. The engine telegraph stays where you put it —
    /// W and S move the order up and down through the notches and it stays there, exactly like
    /// a real bridge. The wheel is the opposite: A and D hold it over and it self-centres when
    /// you let go, so you steady up by releasing rather than by counter-steering.
    /// </summary>
    [RequireComponent(typeof(ShipMotor))]
    public class ShipHelmController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ShipMotor motor;

        [Header("Engine Telegraph")]
        [Tooltip("How fast W/S move the engine order through its range, in units per second (1 = stop to full ahead in one second).")]
        [SerializeField] float telegraphRate = 0.6f;
        [Tooltip("Snap the order to notches (Full/Half/Slow/Stop) instead of a continuous dial, the way a real telegraph works.")]
        [SerializeField] bool useNotches = true;
        [Tooltip("Engine order notches, ahead and astern. Mirrored automatically for astern.")]
        [SerializeField] float[] notches = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        [Header("Wheel")]
        [Tooltip("How fast A/D put the wheel over, in units per second.")]
        [SerializeField] float wheelRate = 1.5f;
        [Tooltip("How fast the wheel returns amidships when you release it. 0 holds the rudder where you left it.")]
        [SerializeField] float wheelReturnRate = 2f;

        [Header("Keys")]
        [SerializeField] Key allStopKey = Key.Space;

        InputAction throttleAction;
        InputAction rudderAction;
        InputAction allStopAction;

        // The lever's continuous travel, kept separate from the notched value it reports.
        // Quantising the accumulator itself would round each frame's small increment straight
        // back to the notch it started on, and the order could never leave zero.
        float throttleTravel;
        float throttleOrder;
        float wheel;

        /// <summary>Current engine order, -1..1, before the motor's lag. For a telegraph readout.</summary>
        public float ThrottleOrder => throttleOrder;

        /// <summary>Current wheel position, -1..1, before the rudder's swing time.</summary>
        public float Wheel => wheel;

        void Awake()
        {
            if (motor == null) motor = GetComponent<ShipMotor>();

            throttleAction = new InputAction("Throttle", InputActionType.Value, expectedControlType: "Axis");
            throttleAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/s")
                .With("Positive", "<Keyboard>/w");
            throttleAction.AddBinding("<Gamepad>/leftStick/y");

            rudderAction = new InputAction("Rudder", InputActionType.Value, expectedControlType: "Axis");
            rudderAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            rudderAction.AddBinding("<Gamepad>/rightStick/x");

            allStopAction = new InputAction("AllStop", InputActionType.Button);
            allStopAction.AddBinding("<Keyboard>/" + allStopKey.ToString().ToLowerInvariant());
            allStopAction.AddBinding("<Gamepad>/buttonEast");
        }

        void OnEnable()
        {
            throttleAction.Enable();
            rudderAction.Enable();
            allStopAction.Enable();
        }

        void OnDisable()
        {
            throttleAction.Disable();
            rudderAction.Disable();
            allStopAction.Disable();
        }

        void Update()
        {
            if (motor == null) return;

            float dt = Time.deltaTime;

            if (allStopAction.WasPressedThisFrame())
            {
                throttleTravel = 0f;
                throttleOrder = 0f;
                wheel = 0f;
            }

            UpdateTelegraph(throttleAction.ReadValue<float>(), dt);
            UpdateWheel(rudderAction.ReadValue<float>(), dt);

            motor.Input = new ShipMotor.ControlInput
            {
                Throttle = throttleOrder,
                Rudder = wheel,
            };
        }

        // The order holds its position when the key is released — that is the whole point of a
        // telegraph. Notching quantises it so you land on recognisable engine orders.
        void UpdateTelegraph(float input, float dt)
        {
            if (Mathf.Abs(input) < 0.01f)
                return;

            throttleTravel = Mathf.Clamp(throttleTravel + input * telegraphRate * dt, -1f, 1f);

            throttleOrder = useNotches && notches != null && notches.Length > 0
                ? NearestNotch(throttleTravel)
                : throttleTravel;
        }

        float NearestNotch(float value)
        {
            float magnitude = Mathf.Abs(value);
            float sign = Mathf.Sign(value);

            float best = notches[0];
            float bestDistance = Mathf.Abs(magnitude - notches[0]);

            for (int i = 1; i < notches.Length; i++)
            {
                float distance = Mathf.Abs(magnitude - notches[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = notches[i];
                }
            }

            return best * sign;
        }

        // The wheel is sprung: hold it over, release to steady up.
        void UpdateWheel(float input, float dt)
        {
            if (Mathf.Abs(input) > 0.01f)
                wheel = Mathf.Clamp(wheel + input * wheelRate * dt, -1f, 1f);
            else if (wheelReturnRate > 0f)
                wheel = Mathf.MoveTowards(wheel, 0f, wheelReturnRate * dt);
        }
    }
}
