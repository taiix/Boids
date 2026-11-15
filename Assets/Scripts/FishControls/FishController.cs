using UnityEngine;

namespace FishGame
{
    public class FishController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] FishMotor motor;
        [Tooltip("Camera that provides the aim direction. If empty, uses a child FishOrbitCamera, else Camera.main's.")]
        [SerializeField] FishOrbitCamera aimCamera;

        [Header("Tuning")]
        [Tooltip("Allow swimming backwards with S. Off = S only brakes (more fish-like).")]
        [SerializeField] bool allowReverse = false;

        void Awake()
        {
            if (motor == null) motor = GetComponent<FishMotor>();
            // Player prefabs carry their own orbit camera; the scene's main camera is only a fallback.
            if (aimCamera == null) aimCamera = GetComponentInChildren<FishOrbitCamera>(true);
            if (aimCamera == null && Camera.main != null)
                aimCamera = Camera.main.GetComponent<FishOrbitCamera>();
        }

        void Update()
        {
            if (motor == null) return;

            Vector2 move = GameInput.Move.ReadVector2();
            float throttle = allowReverse ? move.y : Mathf.Max(0f, move.y);
            // When the camera is in keyboard-steering mode, A/D turns the fish instead of strafing.
            bool keyboardSteering = aimCamera != null && aimCamera.KeyboardSteering;
            float strafe = keyboardSteering ? 0f : move.x;

            float ascend = (GameInput.Ascend.IsPressed() ? 1f : 0f) - (GameInput.Descend.IsPressed() ? 1f : 0f);
            bool boost = GameInput.Boost.IsPressed();

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
