using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Feeds a model's Animator from the <see cref="FishMotor"/> so its built-in swim
    /// animations play at the right intensity. Use this on any fish/shark that has real
    /// authored clips (instead of, or alongside, the procedural FishBodyAnimator).
    ///
    /// Set up an Animator Controller with:
    ///   * a float  "Speed"   (0..1)  -> blend slow-swim -> fast-swim
    ///   * a bool   "Boosting"        -> optional, for a dedicated fast-swim state
    /// Parameter names are configurable below in case your controller uses different ones.
    /// </summary>
    public class SwimAnimatorLink : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] FishMotor motor;

        [Header("Animator parameters")]
        [SerializeField] string speedParam = "Speed";
        [SerializeField] string boostingParam = "Boosting";
        [Tooltip("Above this speed fraction (0..1) the fish counts as 'boosting' for the fast-swim state.")]
        [Range(0f, 1f)][SerializeField] float boostThreshold = 0.7f;
        [Tooltip("How quickly the Speed parameter follows the real speed (smooths anim blending).")]
        [SerializeField] float speedDamp = 8f;

        float _speed;
        int _speedId, _boostingId;
        bool _hasBoosting;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (motor == null) motor = GetComponentInParent<FishMotor>();

            _speedId = Animator.StringToHash(speedParam);
            _boostingId = Animator.StringToHash(boostingParam);
            _hasBoosting = !string.IsNullOrEmpty(boostingParam);
        }

        void Update()
        {
            if (animator == null || motor == null) return;

            float target = motor.SpeedFraction;
            _speed = Mathf.Lerp(_speed, target, 1f - Mathf.Exp(-speedDamp * Time.deltaTime));

            animator.SetFloat(_speedId, _speed);
            if (_hasBoosting)
                animator.SetBool(_boostingId, _speed >= boostThreshold);
        }
    }
}
