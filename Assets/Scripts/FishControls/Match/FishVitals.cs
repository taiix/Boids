using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// A fish's survival stats. Hunger drains constantly; once it hits zero the fish starves and
    /// loses health, and at zero health it dies. Eating restores hunger through <see cref="Feed"/>.
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

        [Header("Death")]
        [Tooltip("Components disabled the moment this fish dies (controller, abilities, etc.).")]
        [SerializeField] Behaviour[] disableOnDeath;

        [SyncVar] float _health;
        [SyncVar] float _hunger;
        [SyncVar(hook = nameof(OnDeadChanged))] bool _isDead;

        bool _deathApplied;

        public float Health => _health;
        public float Hunger => _hunger;
        public float HealthNorm => maxHealth > 0f ? _health / maxHealth : 0f;
        public float HungerNorm => maxHunger > 0f ? _hunger / maxHunger : 0f;
        public bool IsDead => _isDead;

        bool Offline => !NetworkServer.active && !NetworkClient.active;
        bool Authority => Offline || isServer;

        void Awake()
        {
            _health = maxHealth;
            _hunger = maxHunger;
        }

        void OnEnable() => MatchManager.Register(this);
        void OnDisable() => MatchManager.Unregister(this);

        void Update()
        {
            if (!Authority || _isDead) return;

            // Only drain while a round is actually underway. With no MatchManager at all (bare
            // sandbox) we still drain so the stats can be observed.
            var m = MatchManager.Instance;
            if (m != null && !m.IsPlaying) return;

            float dt = Time.deltaTime;
            if (_hunger > 0f)
                _hunger = Mathf.Max(0f, _hunger - hungerDrainPerSecond * dt);
            else
                _health = Mathf.Max(0f, _health - starveDamagePerSecond * dt);

            if (_health <= 0f) Die();
        }

        /// <summary>Restore hunger (called by the eating system). Authority-only.</summary>
        public void Feed(float amount = -1f)
        {
            if (!Authority || _isDead) return;
            float add = amount > 0f ? amount : feedRestore;
            _hunger = Mathf.Min(maxHunger, _hunger + add);
        }

        /// <summary>Direct health damage (e.g. a shark bite that doesn't instantly devour). Authority-only.</summary>
        public void Damage(float amount)
        {
            if (!Authority || _isDead || amount <= 0f) return;
            _health = Mathf.Max(0f, _health - amount);
            if (_health <= 0f) Die();
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
