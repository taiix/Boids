using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    public class FishController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] FishMotor motor;
        [Tooltip("Camera that provides the aim direction. If empty, uses Camera.main's FishOrbitCamera.")]
        [SerializeField] FishOrbitCamera aimCamera;

        [Header("Tuning")]
        [Tooltip("Allow swimming backwards with S. Off = S only brakes (more fish-like).")]
        [SerializeField] bool allowReverse = false;

        InputAction _moveAction;   // Vector2 (throttle / strafe)
        InputAction _ascendAction; // rise
        InputAction _descendAction;// dive
        InputAction _boostAction;  // sprint

        void Awake()
        {
            if (motor == null) motor = GetComponent<FishMotor>();
            if (aimCamera == null && Camera.main != null)
                aimCamera = Camera.main.GetComponent<FishOrbitCamera>();

            _moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _moveAction.AddBinding("<Gamepad>/leftStick");

            _ascendAction = new InputAction("Ascend", InputActionType.Button);
            _ascendAction.AddBinding("<Keyboard>/space");
            _ascendAction.AddBinding("<Gamepad>/buttonSouth");

            _descendAction = new InputAction("Descend", InputActionType.Button);
            _descendAction.AddBinding("<Keyboard>/c");
            _descendAction.AddBinding("<Keyboard>/leftCtrl");
            _descendAction.AddBinding("<Gamepad>/buttonEast");

            _boostAction = new InputAction("Boost", InputActionType.Button);
            _boostAction.AddBinding("<Keyboard>/leftShift");
            _boostAction.AddBinding("<Gamepad>/leftStickPress");
        }

        void OnEnable()
        {
            _moveAction.Enable();
            _ascendAction.Enable();
            _descendAction.Enable();
            _boostAction.Enable();
        }

        void OnDisable()
        {
            _moveAction.Disable();
            _ascendAction.Disable();
            _descendAction.Disable();
            _boostAction.Disable();
        }

        void Update()
        {
            if (motor == null) return;

            Vector2 move = _moveAction.ReadValue<Vector2>();
            float throttle = allowReverse ? move.y : Mathf.Max(0f, move.y);
            // When the camera is in keyboard-steering mode, A/D turns the fish instead of strafing.
            bool keyboardSteering = aimCamera != null && aimCamera.KeyboardSteering;
            float strafe = keyboardSteering ? 0f : move.x;

            float ascend = (_ascendAction.IsPressed() ? 1f : 0f) - (_descendAction.IsPressed() ? 1f : 0f);
            bool boost = _boostAction.IsPressed();

            Vector3 aim = aimCamera != null ? aimCamera.AimDirection : transform.forward;

            var input = new FishMotor.ControlInput
            {
                AimDirection = aim,
                Throttle = throttle,
                Strafe = strafe,
                Ascend = ascend,
                Boost = boost,
            };
            motor.SetInput(input);
        }
    }
}
