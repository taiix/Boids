using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace FishGame
{
    /// <summary>Fired when a fish is eaten; carries the predator GameObject.</summary>
    [System.Serializable]
    public class DevouredEvent : UnityEvent<GameObject> { }

    /// <summary>
    /// Put this on anything a shark can eat. On a catch the fish is flipped to an "eaten"
    /// state: control/physics off, its in-mouth clip plays, and it's smoothly pulled into and
    /// parented to the shark's mouth anchor so it rides the jaw while the shark's devour clip
    /// plays — then it despawns.
    ///
    /// MULTIPLAYER (Mirror): drive <see cref="Devour"/> from the SERVER (after it validates the
    /// catch) and let it run on every client via a ClientRpc so the attach + both animations
    /// stay in sync; swap the local Destroy for NetworkServer.Destroy.
    /// </summary>
    public class Edible : MonoBehaviour
    {
        [Header("Eaten state")]
        [Tooltip("Components disabled the moment the fish is eaten (its controller, motor, boid, etc.).")]
        [SerializeField] Behaviour[] disableOnEaten;
        [Tooltip("Disable the fish's colliders while it's being eaten.")]
        [SerializeField] bool disableCollidersOnEaten = true;

        [Header("In-mouth animation")]
        [Tooltip("The prey's Animator (auto-found in children if empty).")]
        [SerializeField] Animator animator;
        [Tooltip("Trigger for the 'being eaten / in mouth' clip.")]
        [SerializeField] string eatenTrigger = "Eaten";
        [Tooltip("Seconds to smoothly slide into the shark's mouth anchor.")]
        [SerializeField] float pullInTime = 0.2f;

        [Tooltip("Despawn this many seconds after being eaten (match the shark's devour clip length). -1 = never.")]
        [SerializeField] float despawnDelay = 0.9f;

        [Tooltip("Raised when this fish is eaten. Wire up score, respawn, VFX, or a network event here.")]
        public DevouredEvent OnDevoured; // argument = the predator

        public bool IsEaten { get; private set; }

        int _eatenId;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _eatenId = Animator.StringToHash(eatenTrigger);
        }

        /// <summary>
        /// Called when the shark catches this fish. Pass the shark's mouth anchor to have the
        /// fish pulled into and parented to it (rides the jaw). Returns false if already eaten.
        /// In multiplayer, call this on the SERVER (fanned out via RPC).
        /// </summary>
        public bool Devour(GameObject predator, Transform mouth = null)
        {
            if (IsEaten) return false;
            IsEaten = true;

            if (disableOnEaten != null)
                foreach (var b in disableOnEaten)
                    if (b != null) b.enabled = false;

            if (TryGetComponent<Rigidbody>(out var rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true; // the mouth drives it now, not physics
            }

            if (disableCollidersOnEaten)
                foreach (var c in GetComponentsInChildren<Collider>())
                    c.enabled = false;

            // Play the prey's in-mouth clip. Kill root motion so the swim/eat clip can't drag
            // the fish out of the mouth while it's parented there.
            if (animator != null)
            {
                animator.applyRootMotion = false;
                if (!string.IsNullOrEmpty(eatenTrigger) && HasParameter(_eatenId))
                    animator.SetTrigger(_eatenId);
            }

            OnDevoured?.Invoke(predator);

            if (mouth != null)
                StartCoroutine(IntoMouth(mouth));
            else if (despawnDelay >= 0f)
                Destroy(gameObject, despawnDelay); // swap for NetworkServer.Destroy in MP

            return true;
        }

        bool HasParameter(int hash)
        {
            if (animator == null) return false;
            var ps = animator.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].nameHash == hash) return true;
            return false;
        }

        // Parent to the mouth immediately (so it tracks the moving jaw), then ease the local
        // pose to seated (origin of the anchor) for a smooth "sucked in" motion.
        IEnumerator IntoMouth(Transform mouth)
        {
            Vector3 worldScale = transform.lossyScale;
            transform.SetParent(mouth, worldPositionStays: true);

            // Keep the fish's real size even if the jaw bone is scaled.
            Vector3 pls = mouth.lossyScale;
            transform.localScale = new Vector3(
                pls.x != 0f ? worldScale.x / pls.x : worldScale.x,
                pls.y != 0f ? worldScale.y / pls.y : worldScale.y,
                pls.z != 0f ? worldScale.z / pls.z : worldScale.z);

            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;

            float t = 0f;
            while (t < pullInTime)
            {
                t += Time.deltaTime;
                float k = pullInTime > 0f ? Mathf.SmoothStep(0f, 1f, t / pullInTime) : 1f;
                transform.localPosition = Vector3.Lerp(startPos, Vector3.zero, k);
                transform.localRotation = Quaternion.Slerp(startRot, Quaternion.identity, k);
                yield return null;
            }
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            if (despawnDelay >= 0f)
                Destroy(gameObject, despawnDelay); // swap for NetworkServer.Destroy in MP
        }
    }
}
