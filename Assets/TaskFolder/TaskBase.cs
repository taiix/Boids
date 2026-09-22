using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Base class for every task in the game. A <see cref="TaskStation"/> starts it via
    /// <see cref="Begin"/>; the task calls <see cref="CompleteTask"/> when finished, which shaves
    /// <see cref="TimeReward"/> seconds off the survival clock (via <see cref="MatchManager"/>) and
    /// raises <see cref="Completed"/>.
    ///
    /// To add a new task: subclass this, implement <see cref="Begin"/>, call CompleteTask() on
    /// success, and drop a TaskStation in the scene pointing at it.
    /// </summary>
    public abstract class TaskBase : MonoBehaviour
    {
        [Tooltip("Seconds shaved off the survival clock when this task is completed (its difficulty).")]
        [SerializeField] protected float timeReward = 20f;

        /// <summary>How much time completing this task removes from the survival clock.</summary>
        public float TimeReward => timeReward;

        /// <summary>True while the task is being worked on (started, not yet finished/abandoned).</summary>
        public bool IsActive { get; protected set; }

        /// <summary>True once completed — a one-time task, so its station won't offer it again.</summary>
        public bool Consumed { get; protected set; }

        /// <summary>Raised when the task is completed successfully.</summary>
        public event System.Action<TaskBase> Completed;

        /// <summary>Called by a TaskStation when a player interacts to start the task.</summary>
        public abstract void Begin(TaskInteractor interactor);

        /// <summary>Subclasses call this on successful completion.</summary>
        protected void CompleteTask()
        {
            if (!IsActive) return;
            IsActive = false;
            Consumed = true;

            // Shave time off the survival clock (server-authoritative / offline-safe inside MatchManager).
            if (MatchManager.Instance != null)
                MatchManager.Instance.ReduceRemaining(timeReward);

            Debug.Log($"[Task] '{name}' completed → survival clock -{timeReward:0}s");
            Completed?.Invoke(this);
        }
    }
}
