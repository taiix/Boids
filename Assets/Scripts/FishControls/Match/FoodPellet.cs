using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Server-authoritative food pellet. Spawns at the water surface and slowly sinks toward the
    /// bottom, staying a dynamic rigidbody so fish/sharks can bump and push it around. It is eaten
    /// via the player's eat action (see FishPlayer.CmdEat), which the server validates for range,
    /// then calls <see cref="Consume"/> to feed the eater and despawn the pellet.
    ///
    /// Physics runs on the server; a NetworkTransform replicates the motion to clients.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(NetworkIdentity))]
    public class FoodPellet : NetworkBehaviour
    {
        [Header("Nutrition")]
        [Tooltip("Hunger restored when this pellet is eaten.")]
        [SerializeField] float nutrition = 35f;

        [Header("Sinking")]
        [Tooltip("Target downward speed as it sinks (m/s). Gentle buoyancy, not full gravity.")]
        [SerializeField] float sinkSpeed = 0.6f;
        [Tooltip("How quickly it eases to the sink speed.")]
        [SerializeField] float sinkAccel = 1.5f;
        [Tooltip("Linear damping so a push bleeds off instead of flinging it away.")]
        [SerializeField] float linearDamping = 1.2f;

        [Header("Despawn")]
        [Tooltip("Hard cap: despawn this many seconds after spawning even if never eaten.")]
        [SerializeField] float lifetime = 60f;
        [Tooltip("Despawn after resting on the bottom this many seconds.")]
        [SerializeField] float restDespawnDelay = 15f;

        public float Nutrition => nutrition;
        public bool IsEaten { get; private set; }

        /// <summary>Every pellet currently in the world (for the eat check and its prompt).</summary>
        public static readonly List<FoodPellet> All = new List<FoodPellet>();

        Rigidbody _rb;
        float _age;
        float _restTimer;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;              // we drive a gentle sink instead of full gravity
            _rb.linearDamping = linearDamping;
            _rb.angularDamping = 0.5f;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // Fully offline (no server AND no client) -> simulate locally for single-player testing.
        // With a server running, only the server simulates; remote clients just follow the
        // NetworkTransform, so their body is made kinematic to avoid fighting the synced motion.
        bool Offline => !NetworkServer.active && !NetworkClient.active;
        bool Authority => Offline || isServer;

        public override void OnStartServer() => _rb.isKinematic = false;
        public override void OnStartClient()
        {
            if (!isServer) _rb.isKinematic = true;
        }

        void FixedUpdate()
        {
            if (!Authority || IsEaten) return;

            // Ease the downward velocity toward the sink speed (buoyant drift).
            Vector3 v = _rb.linearVelocity;
            if (v.y > -sinkSpeed)
                v.y = Mathf.MoveTowards(v.y, -sinkSpeed, sinkAccel * Time.fixedDeltaTime);
            _rb.linearVelocity = v;

            _age += Time.fixedDeltaTime;
            if (_age >= lifetime) { Despawn(); return; }

            // Once it has effectively stopped descending (resting on the bottom), start a despawn timer.
            if (Mathf.Abs(_rb.linearVelocity.y) < 0.05f)
            {
                _restTimer += Time.fixedDeltaTime;
                if (_restTimer >= restDespawnDelay) Despawn();
            }
            else _restTimer = 0f;
        }

        void Despawn()
        {
            if (NetworkServer.active) NetworkServer.Destroy(gameObject);
            else Destroy(gameObject); // offline / single-player test
        }

        /// <summary>
        /// Consume the pellet: feed the eater (if any) and despawn. Runs on the server in networked
        /// play, or locally when offline. Returns false if already eaten or not the authority.
        /// </summary>
        public bool Consume(FishVitals eater)
        {
            if (IsEaten || !Authority) return false;
            IsEaten = true;
            if (eater != null) eater.Feed(nutrition);
            Despawn();
            return true;
        }
    }
}
