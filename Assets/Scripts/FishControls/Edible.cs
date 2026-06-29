using UnityEngine;
using UnityEngine.Events;

namespace FishGame
{
    /// <summary>Fired when a fish is eaten; carries the predator GameObject.</summary>
    [System.Serializable]
    public class DevouredEvent : UnityEvent<GameObject> { }

    /// <summary>
    /// Put this on anything a shark can eat (the prey fish). It is just a MARKER + a
    /// state change: the shark calls <see cref="Devour"/> when it connects, and this flips
    /// the fish into an "eaten" state and raises an event.
    ///
    /// MULTIPLAYER (Mirror): eating is a SERVER-AUTHORITATIVE state change, not a physics
    /// trick. The flow is: owner client detects a bite -> [Command] to server -> server
    /// validates range and calls Devour -> a [ClientRpc] runs the same reaction (disable
    /// control, play shark bite + prey death anim) on every client. Keep <see cref="Devour"/>
    /// being driven from the SERVER; <see cref="OnDevoured"/> is where the RPC / despawn hooks in.
    /// </summary>
    public class Edible : MonoBehaviour
    {
        [Tooltip("Optional: a transform on the prey the shark's mouth should grab (defaults to this object).")]
        [SerializeField] Transform grabPoint;

        [Tooltip("Components disabled the moment the fish is eaten (its controller, motor, etc.).")]
        [SerializeField] Behaviour[] disableOnEaten;

        [Tooltip("Hide/despawn the prey this many seconds after being eaten (lets a bite anim play). -1 = never.")]
        [SerializeField] float despawnDelay = 0.6f;

        [Tooltip("Raised when this fish is eaten. Wire up score, respawn, ragdoll, VFX, or a network event here.")]
        public DevouredEvent OnDevoured; // argument = the predator

        public bool IsEaten { get; private set; }
        public Transform GrabPoint => grabPoint != null ? grabPoint : transform;

        /// <summary>
        /// Called when the shark catches this fish. Returns false if already eaten.
        /// In multiplayer, call this on the SERVER (then let OnDevoured fan out via RPC).
        /// </summary>
        public bool Devour(GameObject predator)
        {
            if (IsEaten) return false;
            IsEaten = true;

            if (disableOnEaten != null)
                foreach (var b in disableOnEaten)
                    if (b != null) b.enabled = false;

            // Stop it dead so it doesn't keep drifting while being eaten.
            if (TryGetComponent<Rigidbody>(out var rb))
                rb.linearVelocity = Vector3.zero;

            OnDevoured?.Invoke(predator);

            if (despawnDelay >= 0f)
                Destroy(gameObject, despawnDelay); // swap for Mirror NetworkServer.Destroy / pooling later

            return true;
        }
    }
}
