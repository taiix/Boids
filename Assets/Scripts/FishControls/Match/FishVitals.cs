using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A fish's survival stats. Hunger drains constantly; once it hits zero the fish starves and
    /// loses health, and at zero health it dies. Eating restores hunger through <see cref="Feed"/>.
    ///
    /// The shark never dies: at zero health - from anything (orcas, the trawler net, starving) - it's stunned
    /// for <see cref="sharkStunSeconds"/> (it can't move, SharkCondition), then its health refills over
    /// <see cref="sharkRecoverSeconds"/>. While stunned nothing hurts it, and it doesn't starve while it recovers.
    ///
    /// Server-authoritative when hosting (SyncVars replicate to every client). When fully offline
    /// (the FishControls sandbox with no NetworkManager) it simulates locally so the stats are
    /// visible and testable without hosting.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class FishVitals : NetworkBehaviour
    {
        [Header("Health")]
        [SerializeField] float maxHealth = 100f;
        [Tooltip("HP lost per second while starving (hunger at 0).")]
        [SerializeField] float starveDamagePerSecond = 4f;

        [Header("Hunger")]
        [SerializeField] float maxHunger = 100f;
        [Tooltip("Hunger lost per second. 0.6 empties a full bar in ~2.8 min.")]
        [SerializeField] float hungerDrainPerSecond = 0.6f;
        [Tooltip("Hunger restored per bite of food when Feed() is called with no explicit amount.")]
        [SerializeField] float feedRestore = 30f;

        [Header("Bleeding")]
        [Tooltip("Seconds a fish bleeds after taking damage (a cut, a bite it survived). While it bleeds it " +
                 "leaves a lingering blood trail the shark can see from far away.")]
        [SerializeField] float bleedSecondsOnDamage = 20f;

        [Header("Death")]
        [Tooltip("Components disabled the moment this fish dies (controller, abilities, etc.).")]
        [SerializeField] Behaviour[] disableOnDeath;

        [Header("The shark (never dies)")]
        [Tooltip("Seconds the shark is stunned, unable to move, when its health hits zero.")]
        [SerializeField] float sharkStunSeconds = 6f;
        [Tooltip("Seconds its health then takes to refill.")]
        [SerializeField] float sharkRecoverSeconds = 8f;

        [SyncVar] float _health;
        [SyncVar] float _hunger;
        [SyncVar(hook = nameof(OnDeadChanged))] bool _isDead;
        // When the bleeding stops, on the shared clock (NetworkTime, or local time offline).
        [SyncVar] double _bleedUntil;
        // The shark at zero health: stunned until then, on the shared clock.
        [SyncVar] double _stunnedUntil;

        double _recoverUntil;   // authority: the shark's health refills until then
        bool _deathApplied;
        FishPlayer _player;

        // Escaped / eaten: out of the round, so no more hunger, starving or bleeding.
        bool OutOfRound => _player != null && _player.IsOut;
        // In the shark's jaws: its fate is the swallow or the break-free, so it can't starve meanwhile.
        bool Held => _player != null && _player.IsBeingEaten;

        public float Health => _health;
        public float Hunger => _hunger;
        public float HealthNorm => maxHealth > 0f ? _health / maxHealth : 0f;
        public float HungerNorm => maxHunger > 0f ? _hunger / maxHunger : 0f;
        public bool IsDead => _isDead;
        public bool IsBleeding => !_isDead && !OutOfRound && Now < _bleedUntil;
        /// <summary>The shark at zero health: it can't move until this runs out.</summary>
        public bool IsStunned => Now < _stunnedUntil;
        /// <summary>Seconds of the shark's stun left.</summary>
        public float StunLeft => Mathf.Max(0f, (float)(_stunnedUntil - Now));
        /// <summary>Authority: the shark come round from a stun, its health still refilling.</summary>
        public bool IsRecovering => !IsStunned && Now < _recoverUntil;
        /// <summary>Whether this is the shark's body (it never dies).</summary>
        bool IsShark => _player != null && _player.IsSharkBody;

        bool Offline => !NetworkServer.active && !NetworkClient.active;
        bool Authority => Offline || isServer;
        static double Now => !NetworkServer.active && !NetworkClient.active ? Time.timeAsDouble : NetworkTime.time;

        /// <summary>Every loaded instance, enabled or not (what FindAnyObjectByType would search, without
        /// searching the scene).</summary>
        internal static readonly System.Collections.Generic.List<FishVitals> Loaded = new System.Collections.Generic.List<FishVitals>();

        void Awake()
        {
            _health = maxHealth;
            _hunger = maxHunger;
            _player = GetComponent<FishPlayer>();
            Loaded.Add(this);
        }

        void OnDestroy() => Loaded.Remove(this);

        void OnEnable() => MatchManager.Register(this);
        void OnDisable() => MatchManager.Unregister(this);

        void Update()
        {
            // Every machine draws the blood trail of every bleeding fish (purely visual).
            if (IsBleeding) BloodInWater.KeepBleeding(transform);

            if (!Authority || _isDead || OutOfRound || Held) return;

            // Only drain while a round is actually underway. With no MatchManager at all (bare
            // sandbox) we still drain so the stats can be observed.
            var m = MatchManager.Instance;
            if (m != null && !m.RoundRunning) return;

            float dt = Time.deltaTime;
            // The shark, stunned or recovering: no starving meanwhile, and its health comes back.
            if (IsStunned) return;
            if (Now < _recoverUntil)
            {
                _health = Mathf.Min(maxHealth, _health + maxHealth / Mathf.Max(0.1f, sharkRecoverSeconds) * dt);
                return;
            }

            if (_hunger > 0f)
                _hunger = Mathf.Max(0f, _hunger - hungerDrainPerSecond * dt);
            else
                _health = Mathf.Max(0f, _health - starveDamagePerSecond * dt);

            if (_health <= 0f) OutOfHealth();
        }

        /// <summary>Restore hunger (called by the eating system). Authority-only.</summary>
        public void Feed(float amount = -1f)
        {
            if (!Authority || _isDead) return;
            float add = amount > 0f ? amount : feedRestore;
            _hunger = Mathf.Min(maxHunger, _hunger + add);
        }

        /// <summary>Direct health damage (e.g. a cut, a shark bite that doesn't instantly devour), which
        /// also starts the fish bleeding - <paramref name="bleedSeconds"/>, or the default when negative.
        /// Authority-only.</summary>
        public void Damage(float amount, float bleedSeconds = -1f)
        {
            if (!Authority || _isDead || IsStunned || amount <= 0f) return;
            _health = Mathf.Max(0f, _health - amount);
            StartBleeding(bleedSeconds >= 0f ? bleedSeconds : bleedSecondsOnDamage);
            if (_health <= 0f) OutOfHealth();
        }

        /// <summary>Health lost to the surroundings (toxic water) - no wound, so no bleeding. Authority-only.</summary>
        public void Drain(float amount)
        {
            if (!Authority || _isDead || OutOfRound || IsStunned || amount <= 0f) return;
            _health = Mathf.Max(0f, _health - amount);
            if (_health <= 0f) OutOfHealth();
        }

        /// <summary>Bleed for at least <paramref name="seconds"/> from now (a fresh wound never shortens
        /// one already bleeding). Authority-only; the end time syncs to everyone.</summary>
        public void StartBleeding(float seconds)
        {
            if (!Authority || _isDead || seconds <= 0f) return;
            double until = Now + seconds;
            if (until > _bleedUntil) _bleedUntil = until;
        }

        // Zero health: a fish dies; the shark is stunned, then recovers.
        void OutOfHealth()
        {
            if (!IsShark) { Die(); return; }
            if (IsStunned) return;
            _health = 0f;
            _stunnedUntil = Now + sharkStunSeconds;
            _recoverUntil = _stunnedUntil + sharkRecoverSeconds;
            if (NetworkServer.active) _player.ServerReleasePrey();   // limp jaws let go of whatever it held
        }

        void Die()
        {
            if (_isDead) return;
            _isDead = true;        // SyncVar -> hook fires on remote clients
            ApplyDeath();          // apply on the authority (host / offline) too
            if (MatchManager.Instance != null) MatchManager.Instance.NotifyFishDied(this);
        }

        void OnDeadChanged(bool _, bool dead)
        {
            if (dead) ApplyDeath();
        }

        void ApplyDeath()
        {
            if (_deathApplied) return;
            _deathApplied = true;
            if (disableOnDeath != null)
                foreach (var b in disableOnDeath)
                    if (b != null) b.enabled = false;
        }
    }
}
