using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// Goes on a player (fish or shark). Nearby <see cref="TaskStation"/>s register themselves here
    /// via their triggers; for the LOCAL player this shows a "Press E" prompt and, on E, starts the
    /// nearest available station's task. Its <see cref="Role"/> lets stations filter who may use them
    /// (fish tasks vs shark tasks).
    /// </summary>
    public class TaskInteractor : MonoBehaviour
    {
        [Tooltip("What kind of interactor this is. Stations filter by role (fish tasks vs shark tasks).")]
        [SerializeField] InteractorRole role = InteractorRole.Fish;

        public InteractorRole Role => role;

        readonly List<TaskStation> _nearby = new List<TaskStation>();
        InputAction _interact;

        // Offline objects (e.g. the shark in IslandTest) have no NetworkIdentity → treat as local.
        bool IsLocal
        {
            get
            {
                var ni = GetComponent<NetworkIdentity>();
                return ni == null || ni.isLocalPlayer;
            }
        }

        void Awake()
        {
            _interact = new InputAction("Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonNorth");
        }

        void OnEnable() => _interact.Enable();
        void OnDisable() => _interact.Disable();

        public void EnterStation(TaskStation s) { if (s != null && !_nearby.Contains(s)) _nearby.Add(s); }
        public void ExitStation(TaskStation s) { _nearby.Remove(s); }

        /// <summary>Nearest station in range that can be used right now (or null).</summary>
        TaskStation Nearest()
        {
            TaskStation best = null;
            float bestSqr = float.MaxValue;
            for (int i = _nearby.Count - 1; i >= 0; i--)
            {
                var s = _nearby[i];
                if (s == null) { _nearby.RemoveAt(i); continue; }
                if (!s.Available || !s.RoleAllowed(role)) continue;
                float d = (s.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = s; }
            }
            return best;
        }

        void Update()
        {
            if (!IsLocal) return;
            var s = Nearest();
            if (s != null && _interact.WasPressedThisFrame())
                s.Interact(this);
        }

        void OnGUI()
        {
            if (!IsLocal) return;
            var s = Nearest();
            if (s == null) return;

            var style = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            const float w = 340f, h = 42f;
            GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height - 96f, w, h),
                    $"Press  [E]  to  {s.PromptLabel}", style);
        }
    }
}
