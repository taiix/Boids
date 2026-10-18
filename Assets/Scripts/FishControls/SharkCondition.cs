
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// How the shark is holding up. Hurt, it's slower - down to <see cref="slowestAtNoHealth"/> of its speed at
    /// zero health. At zero it's stunned (FishVitals): it can't move or bite and sinks a little, limp, until the
    /// stun wears off and its health refills. On every machine it plays the shark's limp "Stunned" pose and the
    /// flinch when it's hit (<see cref="PlayHit"/>); the speed and the stun bite only on the machine driving it.
    /// FishPlayer adds it to the shark's body.
    /// </summary>
    public class SharkCondition : MonoBehaviour
    {
        [Tooltip("Speed left at zero health (1 = unhurt). Hurt sharks are slower in between.")]
        [Range(0.1f, 1f)][SerializeField] float slowestAtNoHealth = 0.5f;
        [Tooltip("How fast a stunned shark sinks (m/s).")]
        [SerializeField] float stunnedSink = 0.35f;

        static readonly int StunnedId = Animator.StringToHash("Stunned");
        static readonly int HitId = Animator.StringToHash("Hit");

        FishPlayer _player;
        FishVitals _vitals;
        FishMotor _motor;
        SharkAbilities _jaws;
        Animator _animator;
        bool _wasStunned, _paramsChecked, _hasStunned, _hasHit;

        void Awake()
        {
            _player = GetComponent<FishPlayer>();
            _vitals = GetComponent<FishVitals>();
            _motor = GetComponent<FishMotor>();
            _jaws = GetComponent<SharkAbilities>();
            _animator = GetComponentInChildren<Animator>();
        }

        bool Driving => _player != null && (_player.isLocalPlayer || _player.IsOfflineSandboxBody);

        /// <summary>The flinch of a hit (an orca), on every machine.</summary>
        public void PlayHit()
        {
            CheckParameters();
            if (_hasHit) _animator.SetTrigger(HitId);
        }

        void Update()
        {
            if (_vitals == null) return;
            bool stunned = _vitals.IsStunned;
            CheckParameters();
            if (_hasStunned) _animator.SetBool(StunnedId, stunned);

            if (!Driving || _motor == null) { _wasStunned = stunned; return; }
            // Hurt: everything it does is slower, the more so the worse it's hurt.
            _motor.SpeedScale = Mathf.Lerp(slowestAtNoHealth, 1f, _vitals.HealthNorm);

            if (stunned)
            {
                // Out cold: held where it is, drifting down, no bites.
                _motor.Entangle(0.15f, Vector3.down * stunnedSink);
                if (_jaws != null) _jaws.Stun(0.2f);
                if (!_wasStunned) MatchHud.Flash("STUNNED!", 1.5f);
                MatchHud.ShowStatus($"<color=#ff7a66>STUNNED</color>  -  you can move again in {Mathf.CeilToInt(_vitals.StunLeft)}s");
            }
            _wasStunned = stunned;
        }

        // Whether the shark's animator has the Stunned / Hit parameters (FishGame > Build Shark Attack Clips).
        void CheckParameters()
        {
            if (_paramsChecked || _animator == null || !_animator.isInitialized) return;
            _paramsChecked = true;
            foreach (var p in _animator.parameters)
            {
                if (p.nameHash == StunnedId) _hasStunned = true;
                if (p.nameHash == HitId) _hasHit = true;
            }
        }
    }
}
