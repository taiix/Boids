using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// Simple example task and template for solo tasks: once started, hold E for a few seconds to
    /// complete it. Demonstrates the whole framework — TaskStation starts it, it reports progress,
    /// and CompleteTask() shaves TimeReward off the survival clock.
    /// </summary>
    public class HoldTask : TaskBase
    {
        [Tooltip("Seconds of continuous holding to complete.")]
        [SerializeField] float holdSeconds = 3f;

        InputAction _hold;
        float _progress;

        void Awake()
        {
            _hold = new InputAction("HoldTask", InputActionType.Button);
            _hold.AddBinding("<Keyboard>/e");
            _hold.AddBinding("<Gamepad>/buttonNorth");
        }

        void OnEnable() => _hold.Enable();
        void OnDisable() => _hold.Disable();

        public override void Begin(TaskInteractor interactor)
        {
            if (IsActive) return;
            IsActive = true;
            _progress = 0f;
            Debug.Log($"[HoldTask] '{name}' started — hold E for {holdSeconds:0}s to complete.");
        }

        void Update()
        {
            if (!IsActive) return;
            if (_hold.IsPressed())
            {
                _progress += Time.deltaTime;
                if (_progress >= holdSeconds) CompleteTask();
            }
            else
            {
                _progress = 0f; // must hold continuously
            }
        }

        public float Progress01 => IsActive ? Mathf.Clamp01(_progress / holdSeconds) : 0f;

        // The HUD draws the bar (MatchHud.ShowProgress), in the game's style.
        void LateUpdate()
        {
            if (IsActive) MatchHud.ShowProgress($"Hold E  {Mathf.RoundToInt(Progress01 * 100)}%", Progress01);
        }
    }
}
