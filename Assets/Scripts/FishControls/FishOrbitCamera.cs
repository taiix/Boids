using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// Third-person follow camera that doubles as the AIM source for the fish.
    /// The mouse moves yaw/pitch; the fish steers toward <see cref="AimDirection"/>.
    /// Pitch is clamped just short of straight up/down so the fish never gimbal-flips
    /// while still allowing free diving/rising (full 3D swimming).
    ///
    /// Hold the free-look button (right mouse by default) to orbit the camera around the
    /// fish WITHOUT steering it: the aim freezes, the camera swings freely, and on release
    /// the camera eases back behind the fish.
    /// </summary>
    public class FishOrbitCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The fish to follow. If empty, uses this object's parent.")]
        [SerializeField] Transform target;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.12f;
        [SerializeField] float gamepadSensitivity = 180f;
        [Tooltip("How far up/down you can look, in degrees (kept < 90 to avoid flipping).")]
        [SerializeField] float pitchLimit = 85f;
        [SerializeField] bool invertY = false;
        [SerializeField] bool lockCursor = true;

        [Header("Free look (hold to orbit without steering)")]
        [Tooltip("How fast the camera eases back behind the fish after you release free-look.")]
        [SerializeField] float freeLookReturnSpeed = 8f;

        [Header("Follow rig")]
        [Tooltip("Distance the camera sits behind the fish.")]
        [SerializeField] float distance = 5f;
        [Tooltip("How high above the fish the camera floats.")]
        [SerializeField] float height = 1.4f;
        [Tooltip("Position smoothing time. Lower = snappier, higher = floatier.")]
        [SerializeField] float followSmoothTime = 0.08f;
        [Tooltip("How far ahead of the fish the camera looks (keeps the nose lower on screen).")]
        [SerializeField] float lookAheadHeight = 0.5f;

        InputAction _lookAction;
        InputAction _freeLookAction;
        float _yaw;        // steering yaw the fish follows
        float _pitch;      // steering pitch the fish follows
        float _freeYaw;    // camera-only orbit offset while free-looking
        float _freePitch;  // camera-only orbit offset while free-looking
        Vector3 _posVelocity; // SmoothDamp scratch

        /// <summary>World-space direction the player is aiming (camera forward). Steer the fish toward this.</summary>
        public Vector3 AimDirection => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;
        /// <summary>Full aim rotation (yaw + pitch).</summary>
        public Quaternion AimRotation => Quaternion.Euler(_pitch, _yaw, 0f);

        void Awake()
        {
            if (target == null && transform.parent != null)
                target = transform.parent;

            // Self-contained bindings so the camera works with zero Inspector wiring.
            _lookAction = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
            _lookAction.AddBinding("<Mouse>/delta");
            _lookAction.AddBinding("<Gamepad>/rightStick");

            _freeLookAction = new InputAction("FreeLook", InputActionType.Button);
            _freeLookAction.AddBinding("<Mouse>/rightButton");
            _freeLookAction.AddBinding("<Gamepad>/leftShoulder");

            // Start aligned with the target's current heading.
            if (target != null)
            {
                Vector3 e = target.eulerAngles;
                _yaw = e.y;
                _pitch = 0f;
            }
        }
        public void SetTarget(Transform t)
        {
            target = t;
            if (t != null) _yaw = t.eulerAngles.y;
        }
        void OnEnable()
        {
            _lookAction.Enable();
            _freeLookAction.Enable();
        }

        void OnDisable()
        {
            _lookAction.Disable();
            _freeLookAction.Disable();
        }

        void Start()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void Update()
        {
            // Mouse delta is already per-frame movement, so we DON'T multiply by deltaTime.
            // Gamepad is a sustained axis, so that one is time-scaled.
            Vector2 look = _lookAction.ReadValue<Vector2>();
            bool fromGamepad = Gamepad.current != null && _lookAction.activeControl?.device == Gamepad.current;

            float dx, dy;
            if (fromGamepad)
            {
                dx = look.x * gamepadSensitivity * Time.deltaTime;
                dy = look.y * gamepadSensitivity * Time.deltaTime;
            }
            else
            {
                dx = look.x * mouseSensitivity;
                dy = look.y * mouseSensitivity;
            }

            float pitchDelta = invertY ? dy : -dy;

            if (_freeLookAction.IsPressed())
            {
                // Free-look: orbit the camera only. The steering angles (and thus the fish's
                // aim) stay frozen, so the fish keeps swimming its current heading.
                _freeYaw += dx;
                _freePitch += pitchDelta;
                // Keep the COMBINED pitch within the limit so we never flip over the fish.
                _freePitch = Mathf.Clamp(_freePitch, -pitchLimit - _pitch, pitchLimit - _pitch);
            }
            else
            {
                // Normal: mouse steers the fish.
                _yaw += dx;
                _pitch = Mathf.Clamp(_pitch + pitchDelta, -pitchLimit, pitchLimit);

                // Ease the orbit offset back to zero -> camera swings home behind the fish.
                float k = 1f - Mathf.Exp(-freeLookReturnSpeed * Time.deltaTime);
                _freeYaw = Mathf.Lerp(_freeYaw, 0f, k);
                _freePitch = Mathf.Lerp(_freePitch, 0f, k);
            }
        }

        void LateUpdate()
        {
            if (target == null) return;

            // Camera position uses steering + free-look offset; the fish only ever sees the
            // steering angles via AimDirection, so orbiting never turns the fish.
            Quaternion camRot = Quaternion.Euler(_pitch + _freePitch, _yaw + _freeYaw, 0f);
            Vector3 desiredPos = target.position
                                 - (camRot * Vector3.forward) * distance
                                 + Vector3.up * height;

            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVelocity, followSmoothTime);

            Vector3 lookTarget = target.position + Vector3.up * lookAheadHeight;
            transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
        }
    }
}
