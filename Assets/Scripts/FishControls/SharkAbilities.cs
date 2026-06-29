using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// The hunter's offense, layered on top of the shared movement (FishController + FishMotor).
    ///
    ///   * ATTACK  (Left Click): a short forward dash + bite animation, on a small cooldown.
    ///     If a fish is in the mouth during the bite, it gets eaten.
    ///   * LUNGE   (Q / Left Trigger): a big, fast, long-distance dash on a longer cooldown.
    ///     If the shark catches up to a fish and it enters eat range, the lunge is cut short
    ///     and the shark bites/eats instead of overshooting.
    ///
    /// NETCODE: this stays Mirror-agnostic (like FishController). When it detects a catch it
    /// does the instant local feedback (stop + bite anim) and raises <see cref="CaughtPrey"/>.
    /// In single-player there's no subscriber, so it falls back to eating locally. In Mirror,
    /// have your FishPlayer/shark NetworkBehaviour subscribe to <see cref="CaughtPrey"/> and send
    /// a [Command] so the SERVER validates and performs the actual Devour. Enable this component
    /// only for the local player (same pattern FishPlayer uses for FishController).
    /// </summary>
    [RequireComponent(typeof(FishMotor))]
    public class SharkAbilities : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] FishMotor motor;
        [Tooltip("Animator with the bite/eat clip. Optional - leave empty to skip animation.")]
        [SerializeField] Animator animator;

        [Header("Attack (Left Click)")]
        [SerializeField] float attackDashSpeed = 12f;
        [SerializeField] float attackDashDuration = 0.18f;
        [Range(0f, 1f)][SerializeField] float attackSteerControl = 0.5f;
        [SerializeField] float attackCooldown = 0.6f;

        [Header("Lunge (Q / Left Trigger)")]
        [SerializeField] float lungeSpeed = 26f;
        [SerializeField] float lungeDuration = 0.5f;
        [Range(0f, 1f)][SerializeField] float lungeSteerControl = 0.15f;
        [SerializeField] float lungeCooldown = 4f;

        [Header("Eating")]
        [Tooltip("Distance from the shark's pivot forward to its 'mouth'.")]
        [SerializeField] float mouthOffset = 1.0f;
        [Tooltip("How close a fish must be to the mouth to be eaten.")]
        [SerializeField] float eatRadius = 0.9f;
        [Tooltip("Which layers can contain prey. Leave as Everything and filter by the Edible component.")]
        [SerializeField] LayerMask preyMask = ~0;

        [Header("Animator")]
        [SerializeField] string biteTrigger = "Bite";

        /// <summary>
        /// Raised on the owner client the instant a bite connects with an <see cref="Edible"/>.
        /// Subscribe from your Mirror layer to route the kill through a server [Command].
        /// If nobody subscribes, the shark eats locally (single-player / testing).
        /// </summary>
        public event Action<Edible> CaughtPrey;

        InputAction _attackAction;
        InputAction _lungeAction;

        float _attackCd;
        float _lungeCd;
        float _eatWindow; // > 0 while a dash can connect into a bite
        int _biteId;
        readonly Collider[] _hits = new Collider[8];

        public bool AttackReady => _attackCd <= 0f;
        public bool LungeReady => _lungeCd <= 0f;

        void Awake()
        {
            if (motor == null) motor = GetComponent<FishMotor>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _biteId = Animator.StringToHash(biteTrigger);

            _attackAction = new InputAction("Attack", InputActionType.Button);
            _attackAction.AddBinding("<Mouse>/leftButton");
            _attackAction.AddBinding("<Gamepad>/rightTrigger");

            _lungeAction = new InputAction("Lunge", InputActionType.Button);
            _lungeAction.AddBinding("<Keyboard>/q");
            _lungeAction.AddBinding("<Gamepad>/leftTrigger");
        }

        void OnEnable()
        {
            _attackAction.Enable();
            _lungeAction.Enable();
        }

        void OnDisable()
        {
            _attackAction.Disable();
            _lungeAction.Disable();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_attackCd > 0f) _attackCd -= dt;
            if (_lungeCd > 0f) _lungeCd -= dt;
            if (_eatWindow > 0f) _eatWindow -= dt;

            // Don't let a new ability interrupt an in-progress dash.
            if (!motor.IsDashing)
            {
                if (AttackReady && _attackAction.WasPressedThisFrame())
                    DoAttack();
                else if (LungeReady && _lungeAction.WasPressedThisFrame())
                    DoLunge();
            }

            if (_eatWindow > 0f)
                TryEat();
        }

        void DoAttack()
        {
            motor.BeginDash(attackDashSpeed, attackDashDuration, attackSteerControl);
            TriggerBite(); // bite swing happens whether or not it connects
            _attackCd = attackCooldown;
            _eatWindow = attackDashDuration + 0.05f;
        }

        void DoLunge()
        {
            motor.BeginDash(lungeSpeed, lungeDuration, lungeSteerControl);
            _lungeCd = lungeCooldown;
            _eatWindow = lungeDuration; // contact anywhere along the lunge = a catch
        }

        void TryEat()
        {
            Vector3 mouth = transform.position + transform.forward * mouthOffset;
            int count = Physics.OverlapSphereNonAlloc(mouth, eatRadius, _hits, preyMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var edible = _hits[i].GetComponentInParent<Edible>();
                if (edible == null || edible.IsEaten || edible.gameObject == gameObject) continue;

                // Instant local feedback so the bite feels responsive even before the server confirms.
                motor.CancelDash(0f); // stop so we don't overshoot
                TriggerBite();
                _eatWindow = 0f;

                if (CaughtPrey != null)
                    CaughtPrey.Invoke(edible);   // networked: server validates + devours
                else
                    edible.Devour(gameObject);   // single-player / test fallback
                return;
            }
        }

        void TriggerBite()
        {
            if (animator != null && !string.IsNullOrEmpty(biteTrigger))
                animator.SetTrigger(_biteId);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + transform.forward * mouthOffset, eatRadius);
        }
    }
}
