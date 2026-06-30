using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// Prey ability: when near the flock, press the blend key to "join" the school for a few
    /// seconds. Instead of approximating flock motion through the FishMotor (which never quite
    /// matches), the player BECOMES a real boid for the duration:
    ///   * player control + the FishMotor physics are suspended,
    ///   * the player is registered into <see cref="BoidsManager"/> so the school reacts to it,
    ///   * the actual <see cref="Boid"/> script drives the fish, so it moves identically.
    /// After the timer, the motor + player control are restored, carrying the school's heading.
    /// </summary>
    [RequireComponent(typeof(FishMotor))]
    public class FishFlockBlend : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] FishMotor motor;
        [SerializeField] FishController controller;

        [Header("Activation")]
        [Tooltip("How close to a flock fish you must be to blend in.")]
        [SerializeField] float joinRange = 6f;
        [Tooltip("How long the blend lasts before control returns (seconds).")]
        [SerializeField] float blendDuration = 5f;

        [Header("Blend visuals")]
        [Tooltip("Shrink the player to the flock fish's size while blended (and grow back after) " +
                 "so it actually reads as one of the school.")]
        [SerializeField] bool matchFlockScale = true;
        [Tooltip("How fast the shrink/grow transition happens. Higher = snappier.")]
        [SerializeField] float scaleSmoothing = 10f;

        InputAction _blendAction;
        Rigidbody _rb;
        Boid _boid;
        bool _blending;
        float _timer;
        RigidbodyInterpolation _prevInterpolation;
        Collider[] _disabledColliders;
        Vector3 _originalScale;
        Vector3 _targetScale;
        bool _scaleActive;

        /// <summary>True while the fish is schooling as a boid (player control suspended).</summary>
        public bool IsBlending => _blending;

        void Awake()
        {
            if (motor == null) motor = GetComponent<FishMotor>();
            if (controller == null) controller = GetComponent<FishController>();
            _rb = GetComponent<Rigidbody>();

            _blendAction = new InputAction("FlockBlend", InputActionType.Button);
            _blendAction.AddBinding("<Keyboard>/f");
            _blendAction.AddBinding("<Gamepad>/buttonNorth");
        }

        void OnEnable() => _blendAction?.Enable();

        void OnDisable()
        {
            _blendAction?.Disable();
            if (_blending) EndBlend(); // never leave the fish stuck as a boid
        }

        void Update()
        {
            // Smoothly shrink toward the flock size (and grow back after) — runs even once the
            // blend has ended, until the scale is fully restored.
            if (_scaleActive)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, _targetScale,
                                                    1f - Mathf.Exp(-scaleSmoothing * Time.deltaTime));
                if (!_blending && (transform.localScale - _targetScale).sqrMagnitude < 1e-6f)
                {
                    transform.localScale = _targetScale;
                    _scaleActive = false;
                }
            }

            if (_blending)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f) EndBlend();
                return;
            }

            if (_blendAction.WasPressedThisFrame() && IsNearFlock())
                StartBlend();
        }

        void StartBlend()
        {
            var mgr = BoidsManager.instance;
            if (mgr == null) return;

            _blending = true;
            _timer = blendDuration;

            // Hand the body to the flock: stop player input and the motor's physics.
            if (controller != null) controller.enabled = false;
            if (motor != null)
            {
                // Seed the motor's speed so the tail-undulation looks right while schooling.
                motor.SetVelocity(transform.forward * mgr.maxSpeed);
                motor.enabled = false;
            }
            if (_rb != null)
            {
                // Boids move the transform directly; Rigidbody interpolation would lag/jitter it.
                _prevInterpolation = _rb.interpolation;
                _rb.interpolation = RigidbodyInterpolation.None;
                _rb.isKinematic = true;
            }

            // Disable our own colliders so Boid.AvoidObstacles doesn't raycast-hit ourselves and
            // twitch every frame (the flock fish have no colliders). We're hidden anyway.
            var cols = GetComponentsInChildren<Collider>();
            var off = new System.Collections.Generic.List<Collider>(cols.Length);
            foreach (var c in cols)
                if (c != null && c.enabled) { c.enabled = false; off.Add(c); }
            _disabledColliders = off.ToArray();

            // Shrink to the flock's size so we read as one of the school (grab a member's scale
            // BEFORE we join, so we don't measure ourselves).
            if (matchFlockScale)
            {
                if (!_scaleActive) _originalScale = transform.localScale; // keep true size if re-blending mid-restore
                _targetScale = FlockFishScale(mgr);
                _scaleActive = true;
            }

            // Become a real member so the boids align/cohere/avoid with us...
            mgr.Join(gameObject);

            // ...and run the SAME Boid logic the flock uses, so we move identically.
            if (_boid == null) _boid = GetComponent<Boid>();
            if (_boid == null) _boid = gameObject.AddComponent<Boid>();
            _boid.enabled = true;
        }

        void EndBlend()
        {
            _blending = false;

            if (_boid != null) _boid.enabled = false;

            var mgr = BoidsManager.instance;
            if (mgr != null) mgr.Leave(gameObject);

            // Grow back to our real size (Update finishes the lerp).
            if (_scaleActive) _targetScale = _originalScale;

            // Re-enable the colliders we turned off.
            if (_disabledColliders != null)
            {
                foreach (var c in _disabledColliders)
                    if (c != null) c.enabled = true;
                _disabledColliders = null;
            }

            // Restore physics + player control, carrying the school's heading so it's seamless.
            float exitSpeed = mgr != null ? mgr.maxSpeed : 3f;
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.interpolation = _prevInterpolation;
            }
            if (motor != null)
            {
                motor.enabled = true;
                motor.SetVelocity(transform.forward * exitSpeed);
            }
            if (controller != null) controller.enabled = true;
        }

        // Scale the player so its rendered size matches a flock fish, independent of model/mesh.
        Vector3 FlockFishScale(BoidsManager mgr)
        {
            GameObject flockFish = null;
            if (mgr != null && mgr.allFish != null)
                foreach (var fish in mgr.allFish)
                    if (fish != null && fish != gameObject) { flockFish = fish; break; }

            if (flockFish == null) return transform.localScale; // no reference -> leave size alone

            float playerSize = RenderedSize(gameObject);
            float flockSize = RenderedSize(flockFish);
            if (playerSize > 0.0001f && flockSize > 0.0001f)
                return transform.localScale * (flockSize / playerSize);

            return flockFish.transform.localScale; // fallback if no renderers
        }

        static float RenderedSize(GameObject go)
        {
            var r = go.GetComponentInChildren<Renderer>();
            return r != null ? r.bounds.size.magnitude : 0f;
        }

        bool IsNearFlock()
        {
            var mgr = BoidsManager.instance;
            if (mgr == null || mgr.allFish == null) return false;

            float r2 = joinRange * joinRange;
            Vector3 pos = transform.position;
            foreach (var fish in mgr.allFish)
            {
                if (fish == null) continue;
                if ((fish.transform.position - pos).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = _blending ? Color.cyan : new Color(0f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, joinRange);
        }
    }
}
