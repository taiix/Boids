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

        void OnGUI()
        {
            if (!IsActive) return;
            const float w = 300f, h = 24f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height - 140f;
            var prev = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f); GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 0.85f, 1f, 0.9f); GUI.DrawTexture(new Rect(x, y, w * Progress01, h), Texture2D.whiteTexture);
            GUI.color = prev;
            var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(x, y, w, h), $"Hold E… {Mathf.RoundToInt(Progress01 * 100)}%", st);
        }
    }
}
