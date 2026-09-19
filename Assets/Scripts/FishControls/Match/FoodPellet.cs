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

        // Physics runs on the server; on remote clients the NetworkTransform drives the pose, so
        // make the body kinematic there to avoid it fighting the synced motion.
        public override void OnStartServer() => _rb.isKinematic = false;
        public override void OnStartClient()
        {
            if (!isServer) _rb.isKinematic = true;
        }

        void FixedUpdate()
        {
            if (!isServer || IsEaten) return;

            // Ease the downward velocity toward the sink speed (buoyant drift).
            Vector3 v = _rb.linearVelocity;
            if (v.y > -sinkSpeed)
                v.y = Mathf.MoveTowards(v.y, -sinkSpeed, sinkAccel * Time.fixedDeltaTime);
            _rb.linearVelocity = v;

            _age += Time.fixedDeltaTime;
            if (_age >= lifetime) { NetworkServer.Destroy(gameObject); return; }

            // Once it has effectively stopped descending (resting on the bottom), start a despawn timer.
            if (Mathf.Abs(_rb.linearVelocity.y) < 0.05f)
            {
                _restTimer += Time.fixedDeltaTime;
                if (_restTimer >= restDespawnDelay) NetworkServer.Destroy(gameObject);
            }
            else _restTimer = 0f;
        }

        /// <summary>Server-side consume: feed the eater and despawn. Returns false if already eaten.</summary>
        [Server]
        public bool Consume(FishVitals eater)
        {
            if (IsEaten) return false;
            IsEaten = true;
            if (eater != null) eater.Feed(nutrition);
            NetworkServer.Destroy(gameObject);
            return true;
        }
    }
}
