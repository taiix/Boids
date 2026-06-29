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
        [Tooltip("Unparent from the fish on play so the camera follows purely by script (smooth). " +
                 "Parenting to the fish's interpolated Rigidbody causes jitter on fast turns. Keep ON.")]
        [SerializeField] bool detachFromParentOnStart = true;

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

        [Header("Steering mode")]
        [Tooltip("ON = turn with the keyboard (A/D yaw, Up/Down arrows pitch). OFF = steer with the mouse. " +
                 "Toggle at runtime with the toggle key.")]
        [SerializeField] bool keyboardSteering = false;
        [Tooltip("Key that flips between mouse and keyboard steering.")]
        [SerializeField] Key toggleSteeringKey = Key.T;
        [SerializeField] float keyboardYawSpeed = 120f;   // deg/sec
        [SerializeField] float keyboardPitchSpeed = 90f;  // deg/sec

        /// <summary>True while the fish is being turned by the keyboard instead of the mouse.</summary>
        public bool KeyboardSteering => keyboardSteering;

        [Header("Follow rig")]
        [Tooltip("Distance the camera sits behind the fish.")]
        [SerializeField] float distance = 5f;
        [Tooltip("How high above the fish the camera floats.")]
        [SerializeField] float height = 1.4f;
        [Tooltip("Position smoothing time. Lower = snappier, higher = floatier.")]
        [SerializeField] float followSmoothTime = 0.08f;
        [Tooltip("How far ahead of the fish the camera looks (keeps the nose lower on screen).")]
        [SerializeField] float lookAheadHeight = 0.5f;

        [Header("Collision (don't clip into ground/terrain)")]
        [Tooltip("Pull the camera in toward the fish when geometry is between them.")]
        [SerializeField] bool avoidClipping = true;
        [Tooltip("Layers that block the camera (seabed, terrain, reefs). Narrow this if it catches things it shouldn't.")]
        [SerializeField] LayerMask collisionMask = ~0;
        [Tooltip("Thickness of the camera probe so it pulls in before the lens actually touches a surface.")]
        [SerializeField] float collisionRadius = 0.3f;
        [Tooltip("Closest the camera will zoom toward the fish when fully blocked.")]
        [SerializeField] float minDistance = 0.8f;
        [Tooltip("Extra gap kept between the camera and whatever it hit.")]
        [SerializeField] float collisionBuffer = 0.2f;
        [Tooltip("How fast the camera zooms back OUT after the obstacle clears. Pull-in is always instant " +
                 "(so it never clips); this only smooths the recovery.")]
        [SerializeField] float zoomReturnSharpness = 8f;

        readonly RaycastHit[] _camHits = new RaycastHit[8];
        float _currentDistance;

        InputAction _lookAction;
        InputAction _freeLookAction;
        InputAction _turnAction; // keyboard steering: x = yaw (A/D), y = pitch (Up/Down arrows)
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
            _currentDistance = distance;

            if (target == null && transform.parent != null)
                target = transform.parent;

            // Self-contained bindings so the camera works with zero Inspector wiring.
            _lookAction = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
            _lookAction.AddBinding("<Mouse>/delta");
            _lookAction.AddBinding("<Gamepad>/rightStick");

            _freeLookAction = new InputAction("FreeLook", InputActionType.Button);
            _freeLookAction.AddBinding("<Mouse>/rightButton");
            _freeLookAction.AddBinding("<Gamepad>/leftShoulder");

            _turnAction = new InputAction("KeyboardTurn", InputActionType.Value, expectedControlType: "Vector2");
            _turnAction.AddCompositeBinding("2DVector")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow");

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
            _turnAction.Enable();
        }

        void OnDisable()
        {
            _lookAction.Disable();
            _freeLookAction.Disable();
            _turnAction.Disable();
        }

        void Start()
        {
            // Follow the fish by reference, not by parenting -> no parented-Rigidbody jitter.
            if (detachFromParentOnStart && transform.parent != null)
            {
                if (target == null) target = transform.parent;
                transform.SetParent(null, true);
            }

            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void Update()
        {
            // Flip steering mode on the toggle key.
            var kb = Keyboard.current;
            if (kb != null && kb[toggleSteeringKey].wasPressedThisFrame)
            {
                keyboardSteering = !keyboardSteering;
                Debug.Log($"[FishOrbitCamera] Steering = {(keyboardSteering ? "KEYBOARD (A/D yaw, Up/Down arrows pitch)" : "MOUSE")}");
            }

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
                if (keyboardSteering)
                {
                    // Keyboard steers the fish: A/D = yaw, Up/Down arrows = pitch. Mouse is ignored.
                    Vector2 turn = _turnAction.ReadValue<Vector2>();
                    _yaw += turn.x * keyboardYawSpeed * Time.deltaTime;
                    float kp = turn.y * keyboardPitchSpeed * Time.deltaTime;
                    _pitch = Mathf.Clamp(_pitch + (invertY ? -kp : kp), -pitchLimit, pitchLimit);
                }
                else
                {
                    // Mouse steers the fish.
                    _yaw += dx;
                    _pitch = Mathf.Clamp(_pitch + pitchDelta, -pitchLimit, pitchLimit);
                }

                // Ease the orbit offset back to zero -> camera swings home behind the fish.
                float k = 1f - Mathf.Exp(-freeLookReturnSpeed * Time.deltaTime);
                _freeYaw = Mathf.Lerp(_freeYaw, 0f, k);
                _freePitch = Mathf.Lerp(_freePitch, 0f, k);
            }
        }

        void LateUpdate()
        {
            if (target == null) return;
            float dt = Time.deltaTime;

            // Camera orientation uses steering + free-look offset; the fish only ever sees the
            // steering angles via AimDirection, so orbiting never turns the fish.
            Quaternion camRot = Quaternion.Euler(_pitch + _freePitch, _yaw + _freeYaw, 0f);
            Vector3 pivot = target.position + Vector3.up * height;   // top of the spring arm
            Vector3 backDir = -(camRot * Vector3.forward);            // fish -> camera

            // Spring arm: cast along the IDEAL arm direction from the stable pivot (NOT from the
            // jittery camera position) so the result doesn't feed back into itself and shake.
            float targetDist = distance;
            if (avoidClipping)
            {
                int n = Physics.SphereCastNonAlloc(pivot, collisionRadius, backDir, _camHits, distance,
                                                   collisionMask, QueryTriggerInteraction.Ignore);
                float nearest = distance;
                for (int i = 0; i < n; i++)
                {
                    if (_camHits[i].collider.transform.IsChildOf(target)) continue; // ignore the fish itself
                    if (_camHits[i].distance < nearest) nearest = _camHits[i].distance;
                }
                if (nearest < targetDist)
                    targetDist = Mathf.Max(minDistance, nearest - collisionBuffer);
            }

            // Pull IN instantly (never clip); ease back OUT smoothly. This asymmetry is what
            // kills the in/out chatter when you push the camera against the ground.
            if (targetDist < _currentDistance)
                _currentDistance = targetDist;
            else
                _currentDistance = Mathf.Lerp(_currentDistance, targetDist, 1f - Mathf.Exp(-zoomReturnSharpness * dt));

            // Smooth the follow (lag as the fish moves), then place the camera along the arm.
            Vector3 desiredPos = pivot + backDir * _currentDistance;
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref _posVelocity, followSmoothTime);

            // Look at the fish. Guard against a near-zero direction when zoomed in tight, which
            // would make the rotation jitter — fall back to the orbit direction instead.
            Vector3 lookTarget = target.position + Vector3.up * lookAheadHeight;
            Vector3 lookDir = lookTarget - transform.position;
            transform.rotation = lookDir.sqrMagnitude > 0.0004f
                ? Quaternion.LookRotation(lookDir, Vector3.up)
                : camRot;
        }
    }
}
