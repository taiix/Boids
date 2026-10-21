using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

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
    ///
    /// The first-person key (V / right stick click) switches to looking out of the creature's own
    /// eyes - fish and shark alike - and back. Steering is the same either way; in first person the
    /// free-look button looks around without turning. The body you're in is hidden from your own view
    /// (it still casts its shadow, and everyone else sees it as normal). The choice is remembered
    /// across role swaps, respawns and sessions; spectating (being eaten) is always third person.
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

        [Header("First person (toggle)")]
        [Tooltip("Key that switches between first and third person (gamepad: right stick click).")]
        [SerializeField] Key firstPersonKey = Key.V;
        [Tooltip("Where the eyes are, in the body's own space (before its scale). Zero = worked out from the " +
                 "model: up at the front, a little above the middle.")]
        [SerializeField] Vector3 eyeOffset;
        [Tooltip("Seconds the view takes to swing between third and first person.")]
        [SerializeField] float viewBlendSeconds = 0.25f;
        [Tooltip("How far the first-person eye keeps from the seabed and rocks (metres). The eyes sit up at the " +
                 "nose, ahead of the body the collider keeps out of the ground - nosing into the sand would " +
                 "otherwise put the view under it - so it's pulled back toward the body's middle when needed.")]
        [SerializeField] float firstPersonClearance = 0.2f;
        [Tooltip("Near clip plane in first person: close, so a surface right in front of your face isn't " +
                 "sliced open (third person keeps the camera's own).")]
        [SerializeField] float firstPersonNearClip = 0.05f;

        const string FirstPersonPref = "FishGame.FirstPerson";
        static bool s_firstPerson;
        static bool s_prefLoaded;

        /// <summary>True while the player has chosen to look out of the creature's eyes.</summary>
        public static bool FirstPerson => s_firstPerson;

        InputAction _viewAction;
        float _fpBlend;            // 0 = third person, 1 = first person (eased between)
        Vector3 _tpPos;            // where the third-person rig is, kept up to date even in first person
        bool _tpPosValid;
        Vector3 _eyeLocal;         // eye point in the target's own space
        Transform _eyeFor;         // the target _eyeLocal was worked out for
        readonly List<(Renderer renderer, ShadowCastingMode mode)> _hidden = new List<(Renderer, ShadowCastingMode)>();
        Transform _hiddenBody;     // the body we've hidden from our own view, if any
        Camera _camera;
        float _thirdPersonNearClip = -1f; // the camera's own near plane, put back in third person

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
        Transform _spectating; // watching another creature through its view (input ignored)

        /// <summary>World-space direction the player is aiming (camera forward). Steer the fish toward this.</summary>
        public Vector3 AimDirection => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;
        /// <summary>Full aim rotation (yaw + pitch).</summary>
        public Quaternion AimRotation => Quaternion.Euler(_pitch, _yaw, 0f);

        /// <summary>The creature being watched while spectating (null when following our own).</summary>
        public Transform SpectateTarget => _spectating;
        /// <summary>The creature the camera follows right now (our own, or the one we spectate).</summary>
        public Transform Target => target;

        // A menu or the results screen has the mouse: it mustn't also swing the camera or flip views.
        static bool MouseTaken => PauseMenu.IsOpen || RoundScreen.ShowingResults;

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

            _viewAction = new InputAction("FirstPerson", InputActionType.Button);
            _viewAction.AddBinding("<Gamepad>/rightStickPress");

            if (!s_prefLoaded)
            {
                s_prefLoaded = true;
                s_firstPerson = PlayerPrefs.GetInt(FirstPersonPref, 0) == 1;
            }
            _fpBlend = s_firstPerson ? 1f : 0f; // a new body starts straight in the view you left

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
            _spectating = null;
            target = t;
            if (t != null) _yaw = t.eulerAngles.y;
        }

        /// <summary>
        /// Watch another creature through its own view: sit behind it and turn with its heading (e.g.
        /// the shark eating us sees roughly what its own camera does). Input is ignored until
        /// <see cref="SetTarget"/> is called again.
        /// </summary>
        public void Spectate(Transform other)
        {
            _spectating = other;
            target = other;
            _freeYaw = _freePitch = 0f;
        }
        void OnEnable()
        {
            _lookAction.Enable();
            _freeLookAction.Enable();
            _turnAction.Enable();
            _viewAction.Enable();
        }

        void OnDisable()
        {
            _lookAction.Disable();
            _freeLookAction.Disable();
            _turnAction.Disable();
            _viewAction.Disable();
            HideBody(null); // never leave a body invisible
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
            // Spectating: follow the other creature's heading instead of our own input.
            if (_spectating != null)
            {
                Vector3 f = _spectating.forward;
                float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg, -pitchLimit, pitchLimit);
                float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
                _yaw = Mathf.LerpAngle(_yaw, yaw, k);
                _pitch = Mathf.Lerp(_pitch, pitch, k);
                return;
            }

            // Flip steering mode on the toggle key.
            var kb = Keyboard.current;
            if (kb != null && kb[toggleSteeringKey].wasPressedThisFrame && !MouseTaken)
            {
                keyboardSteering = !keyboardSteering;
                Debug.Log($"[FishOrbitCamera] Steering = {(keyboardSteering ? "KEYBOARD (A/D yaw, Up/Down arrows pitch)" : "MOUSE")}");
            }

            // First / third person.
            if (!MouseTaken &&
                ((kb != null && kb[firstPersonKey].wasPressedThisFrame) || _viewAction.WasPressedThisFrame()))
            {
                s_firstPerson = !s_firstPerson;
                PlayerPrefs.SetInt(FirstPersonPref, s_firstPerson ? 1 : 0);
            }

            // Mouse delta is already per-frame movement, so we DON'T multiply by deltaTime.
            // Gamepad is a sustained axis, so that one is time-scaled.
            // The Escape menu frees the mouse for its buttons; it mustn't also swing the camera.
            Vector2 look = MouseTaken ? Vector2.zero : _lookAction.ReadValue<Vector2>();
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

            // Smooth the follow (lag as the fish moves), then place the camera along the arm. Tracked even
            // in first person, so switching back swings out to where the rig would be by now.
            Vector3 desiredPos = pivot + backDir * _currentDistance;
            if (!_tpPosValid) { _tpPos = transform.position; _tpPosValid = true; }
            _tpPos = Vector3.SmoothDamp(_tpPos, desiredPos, ref _posVelocity, followSmoothTime);

            // Look at the fish. Guard against a near-zero direction when zoomed in tight, which
            // would make the rotation jitter — fall back to the orbit direction instead.
            Vector3 lookTarget = target.position + Vector3.up * lookAheadHeight;
            Vector3 lookDir = lookTarget - _tpPos;
            Quaternion tpRot = lookDir.sqrMagnitude > 0.0004f
                ? Quaternion.LookRotation(lookDir, Vector3.up)
                : camRot;

            // First person: out of the creature's eyes, looking where it aims (free-look looks around).
            // Kept level - the body banks and rolls, the view doesn't. Spectating is always third person.
            bool firstPerson = s_firstPerson && _spectating == null;
            _fpBlend = Mathf.MoveTowards(_fpBlend, firstPerson ? 1f : 0f, dt / Mathf.Max(viewBlendSeconds, 0.01f));
            if (_fpBlend <= 0f)
            {
                transform.SetPositionAndRotation(_tpPos, tpRot);
            }
            else
            {
                float e = _fpBlend * _fpBlend * (3f - 2f * _fpBlend);
                transform.SetPositionAndRotation(Vector3.Lerp(_tpPos, SafeEyePoint(), e), Quaternion.Slerp(tpRot, camRot, e));
            }

            // Our own body would fill the view from inside it: hide it (shadow kept) once we're mostly in.
            bool inside = firstPerson && _fpBlend > 0.5f;
            HideBody(inside ? target : null);
            SetNearClip(inside);
        }

        // ---------------------------------------------------------------- first person

        Vector3 EyePoint()
        {
            if (_eyeFor != target)
            {
                _eyeFor = target;
                _eyeLocal = eyeOffset != Vector3.zero ? eyeOffset : AutoEye(target);
            }
            return target.TransformPoint(_eyeLocal);
        }

        // The eye, kept out of the seabed and rocks: a sphere is swept from the body's middle (which its
        // collider keeps clear of the ground) out to the eye, and stops short of anything in the way.
        Vector3 SafeEyePoint()
        {
            Vector3 eye = EyePoint();
            if (!avoidClipping) return eye;
            Vector3 from = target.position;
            Vector3 d = eye - from;
            float len = d.magnitude;
            if (len < 1e-4f) return eye;
            d /= len;
            int n = Physics.SphereCastNonAlloc(from, firstPersonClearance, d, _camHits, len, collisionMask,
                                               QueryTriggerInteraction.Ignore);
            float nearest = len;
            for (int i = 0; i < n; i++)
            {
                var hit = _camHits[i];
                if (hit.collider.transform.IsChildOf(target)) continue; // our own body
                // (Something already overlapping the middle reports distance 0: the eye stays at the middle.)
                if (hit.distance < nearest) nearest = hit.distance;
            }
            return from + d * nearest;
        }

        void SetNearClip(bool firstPerson)
        {
            if (_camera == null && !TryGetComponent(out _camera)) return;
            if (_thirdPersonNearClip < 0f) _thirdPersonNearClip = _camera.nearClipPlane;
            float want = firstPerson ? Mathf.Min(firstPersonNearClip, _thirdPersonNearClip) : _thirdPersonNearClip;
            if (!Mathf.Approximately(_camera.nearClipPlane, want)) _camera.nearClipPlane = want;
        }

        // The body's meshes measured in its own space (nose along +Z): eyes near the front, a little above
        // the middle - right for a small fish and a five-metre shark alike.
        static Vector3 AutoEye(Transform body)
        {
            bool any = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                Transform space = r is SkinnedMeshRenderer smr && smr.rootBone != null ? smr.rootBone : r.transform;
                Bounds lb = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = body.InverseTransformPoint(space.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    any = true;
                }
            }
            if (!any) return new Vector3(0f, 0.2f, 0.5f);
            Vector3 c = (min + max) * 0.5f, ext = (max - min) * 0.5f;
            return new Vector3(c.x, c.y + ext.y * 0.35f, max.z - ext.z * 0.25f);
        }

        // Hide the body we're looking out of (only on this machine - shadows stay), or put the last one back.
        void HideBody(Transform body)
        {
            if (_hiddenBody == body) return;
            foreach (var (r, mode) in _hidden)
                if (r != null) r.shadowCastingMode = mode;
            _hidden.Clear();
            _hiddenBody = body;
            if (body == null) return;
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (r.transform.IsChildOf(transform)) continue;
                _hidden.Add((r, r.shadowCastingMode));
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
        }

        void OnDestroy() => HideBody(null);
    }
}
