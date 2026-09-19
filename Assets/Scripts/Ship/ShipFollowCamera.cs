using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// Third-person camera for a ship. Orbit with the mouse, zoom with the wheel.
    ///
    /// The important part is what it deliberately ignores. A ship heaves and rolls constantly,
    /// and a camera rigidly bolted to it inherits every bit of that — which reads as the world
    /// shaking rather than the ship moving. So the rig is built on the target's heading and
    /// horizontal position only: roll and pitch are dropped outright, and vertical motion is
    /// followed on a much longer time constant than horizontal. The ship visibly rides the
    /// swell; the horizon stays put.
    ///
    /// Never parent this to the ship. Following an interpolated Rigidbody by parenting samples
    /// it at the wrong point in the frame and jitters — the same reason
    /// <see cref="FishOrbitCamera"/> detaches itself.
    /// </summary>
    public class ShipFollowCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The ship to follow. If empty, uses this object's parent.")]
        [SerializeField] Transform target;
        [Tooltip("Unparent on play so the camera follows purely by script. Keep ON.")]
        [SerializeField] bool detachFromParentOnStart = true;
        [Tooltip("Point on the ship the camera looks at, in the ship's local space. Raise it to frame the superstructure.")]
        [SerializeField] Vector3 lookAtOffset = new Vector3(0f, 8f, 0f);

        [Header("Rig")]
        [Tooltip("Distance astern of the ship.")]
        [SerializeField] float distance = 90f;
        [SerializeField] float minDistance = 25f;
        [SerializeField] float maxDistance = 250f;

        [Header("Look")]
        [SerializeField] float mouseSensitivity = 0.15f;
        [SerializeField] float gamepadSensitivity = 120f;
        [SerializeField] float zoomSensitivity = 8f;
        [Tooltip("How far up/down you can look, in degrees.")]
        [SerializeField] float minPitch = -5f;
        [SerializeField] float maxPitch = 70f;
        [SerializeField] bool invertY = false;
        [Tooltip("Hold this to orbit. Off = the mouse always orbits.")]
        [SerializeField] bool requireHoldToOrbit = true;

        [Header("Smoothing")]
        [Tooltip("Horizontal follow smoothing. Lower = snappier.")]
        [SerializeField] float followSmoothTime = 0.45f;
        [Tooltip("Vertical follow smoothing. Deliberately much longer than horizontal: this is what stops the ship's heave from shaking the view.")]
        [SerializeField] float heightSmoothTime = 1.6f;
        [Tooltip("How closely the rig tracks the ship's heading. Lower = the camera swings round behind sooner.")]
        [SerializeField] float headingSmoothTime = 0.8f;

        float yaw;
        float pitch = 22f;
        float smoothedHeading;
        float smoothedHeight;
        float headingVelocity;
        float heightVelocity;
        Vector3 planarPosition;
        Vector3 planarVelocity;

        InputAction lookAction;
        InputAction zoomAction;
        InputAction orbitAction;

        void Awake()
        {
            lookAction = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
            lookAction.AddBinding("<Mouse>/delta");
            lookAction.AddBinding("<Gamepad>/rightStick");

            zoomAction = new InputAction("Zoom", InputActionType.Value, expectedControlType: "Vector2");
            zoomAction.AddBinding("<Mouse>/scroll");

            orbitAction = new InputAction("Orbit", InputActionType.Button);
            orbitAction.AddBinding("<Mouse>/rightButton");
            orbitAction.AddBinding("<Gamepad>/leftShoulder");
        }

        void OnEnable()
        {
            lookAction.Enable();
            zoomAction.Enable();
            orbitAction.Enable();
        }

        void OnDisable()
        {
            lookAction.Disable();
            zoomAction.Disable();
            orbitAction.Disable();
        }

        void Start()
        {
            if (target == null && transform.parent != null)
                target = transform.parent;

            if (detachFromParentOnStart)
                transform.SetParent(null, true);

            if (target != null)
            {
                smoothedHeading = HeadingOf(target);
                smoothedHeight = target.position.y;
                planarPosition = new Vector3(target.position.x, 0f, target.position.z);
                yaw = 0f;
            }
        }

        // LateUpdate so the ship has already been moved and interpolated this frame.
        void LateUpdate()
        {
            if (target == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // Smooth the timestep itself for the follow maths. A camera tracking a ship at 8 m/s
            // turns every millisecond of frame-time variance directly into positional judder:
            // with frame times swinging between 15ms and 32ms, the rig lurches by a different
            // amount every frame even though the ship is moving perfectly smoothly. Feeding the
            // damper an averaged dt costs a little responsiveness and removes most of that.
            float smoothDt = Time.smoothDeltaTime > 0f ? Time.smoothDeltaTime : dt;

            ReadLook(dt);

            // Split the target into the parts worth following and the parts worth ignoring.
            Vector3 targetPlanar = new Vector3(target.position.x, 0f, target.position.z);
            planarPosition = Vector3.SmoothDamp(planarPosition, targetPlanar, ref planarVelocity, followSmoothTime, Mathf.Infinity, smoothDt);

            smoothedHeight = Mathf.SmoothDamp(smoothedHeight, target.position.y, ref heightVelocity, heightSmoothTime, Mathf.Infinity, smoothDt);

            // Heading only — the ship's roll and pitch never reach the rig. Take it from the
            // flattened forward vector rather than eulerAngles.y: Euler angles are reconstructed
            // from the quaternion every time they are read, and on a hull that is rolling and
            // pitching that reconstruction jitters by a fraction of a degree each frame. At a
            // hundred metres of boom length, a fraction of a degree is centimetres of camera
            // shake — far more visible than the heave it was meant to be tracking.
            smoothedHeading = Mathf.SmoothDampAngle(smoothedHeading, HeadingOf(target), ref headingVelocity, headingSmoothTime, Mathf.Infinity, smoothDt);

            Vector3 pivot = planarPosition + Vector3.up * smoothedHeight;
            Vector3 focus = pivot + Quaternion.Euler(0f, smoothedHeading, 0f) * lookAtOffset;

            Quaternion orbit = Quaternion.Euler(pitch, smoothedHeading + yaw, 0f);
            transform.position = focus + orbit * new Vector3(0f, 0f, -distance);

            // Look at the ship with an explicit world up, so the camera can never inherit roll.
            Vector3 toFocus = focus - transform.position;
            if (toFocus.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(toFocus, Vector3.up);
        }

        static float HeadingOf(Transform t)
        {
            Vector3 flat = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
                flat = Vector3.ProjectOnPlane(t.up, Vector3.up);

            return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }

        void ReadLook(float dt)
        {
            bool orbiting = !requireHoldToOrbit || orbitAction.IsPressed();

            if (orbiting)
            {
                Vector2 look = lookAction.ReadValue<Vector2>();
                bool gamepad = Gamepad.current != null && Gamepad.current.rightStick.ReadValue().sqrMagnitude > 0.01f;
                float scale = gamepad ? gamepadSensitivity * dt : mouseSensitivity;

                yaw += look.x * scale;
                pitch += (invertY ? look.y : -look.y) * scale;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }

            float scroll = zoomAction.ReadValue<Vector2>().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomSensitivity, minDistance, maxDistance);
        }
    }
}
