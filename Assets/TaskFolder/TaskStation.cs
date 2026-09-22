using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Marks a spot in the world where a task can be started. Put it on a trigger collider (the
    /// interaction zone) near the task's props (e.g. the sea urchins). When a LOCAL player of an
    /// allowed role is inside the zone, its <see cref="TaskInteractor"/> shows a "Press E" prompt;
    /// pressing E calls <see cref="Interact"/>, which starts the task.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TaskStation : MonoBehaviour
    {
        [Tooltip("The task this station starts.")]
        [SerializeField] TaskBase task;
        [Tooltip("Which player roles may use this station. Fish tasks = Fish; add Shark to test with the shark.")]
        [SerializeField] InteractorRole allowedRoles = InteractorRole.Fish;
        [Tooltip("Shown in the interaction prompt, e.g. 'repair the reef'.")]
        [SerializeField] string promptLabel = "start task";

        public string PromptLabel => promptLabel;

        /// <summary>The station can be used right now (has a task that isn't running or already done).</summary>
        public bool Available => task != null && !task.IsActive && !task.Consumed;

        void Reset()
        {
            // Make the collider a trigger by default when the component is first added.
            if (TryGetComponent<Collider>(out var col)) col.isTrigger = true;
        }

        public bool RoleAllowed(InteractorRole role) => (allowedRoles & role) != 0;

        void OnTriggerEnter(Collider other)
        {
            var it = other.GetComponentInParent<TaskInteractor>();
            if (it != null && RoleAllowed(it.Role)) it.EnterStation(this);
        }

        void OnTriggerExit(Collider other)
        {
            var it = other.GetComponentInParent<TaskInteractor>();
            if (it != null) it.ExitStation(this);
        }

        /// <summary>Called by the interactor when the local player presses interact inside the zone.</summary>
        public void Interact(TaskInteractor who)
        {
            if (!Available || who == null || !RoleAllowed(who.Role)) return;
            Debug.Log($"[TaskStation] '{name}' started by {who.name} ({who.Role})");
            task.Begin(who);
        }
    }
}
