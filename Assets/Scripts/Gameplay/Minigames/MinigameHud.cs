using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace FishGame
{
    /// <summary>
    /// The overlay every station minigame draws into (net, leak, coral, line, hiding). One UIDocument on
    /// the HUD's panel settings, above MatchHud; <see cref="Open"/> clones a layout from
    /// Resources/Hud/Minigames/&lt;name&gt;.uxml into it. Layouts share Minigame.uss (and the HUD's own
    /// MatchHud.uss) so they look like the rest of the game - edit them in the UI Builder.
    ///
    /// Shared pieces a layout may contain, found by name: "mg-flash" / "mg-flash-text" (PERFECT / MISS slab,
    /// see <see cref="Flash"/>), "mg-onboard" (<see cref="SetHint"/>, first time only), "mg-progress-fill" (<see cref="SetProgress"/>, if a layout has one).
    /// Spawns itself; nothing to place in a scene.
    /// </summary>
    public class MinigameHud : MonoBehaviour
    {
        static MinigameHud s_instance;

        /// <summary>A minigame is showing.</summary>
        public static bool IsOpen => s_instance != null && s_instance._open;

        /// <summary>The open minigame reads the mouse itself: the camera mustn't turn with it.</summary>
        public static bool HasMouse => IsOpen && s_instance._mouse;

        /// <summary>Show a layout; returns its root to query, or null if it's missing.
        /// <paramref name="freeCursor"/>: the game needs a pointer (drag puzzles); the cursor is shown and freed.
        /// <paramref name="takesMouse"/>: the game reads mouse movement; the camera stops following it.</summary>
        public static VisualElement Open(string layout, bool freeCursor = false, bool takesMouse = false)
            => Instance.OpenInternal(layout, freeCursor, takesMouse || freeCursor);

        public static void Close() { if (s_instance != null) s_instance.CloseInternal(); }

        /// <summary>A short shout on the HUD's orange slab (red when <paramref name="good"/> is false).</summary>
        public static void Flash(string text, bool good)
        {
            if (s_instance == null || s_instance._flash == null) return;
            s_instance._flashText.text = text;
            s_instance._flash.EnableInClassList("mg-flash--miss", !good);
            s_instance._flashUntil = Time.unscaledTime + 0.7f;
        }

        /// <summary>How it works, in one line - shown only the first time this game is opened this session,
        /// fading out after a few seconds. After that the controls line says enough.</summary>
        public static void SetHint(string text)
        {
            if (s_instance == null || s_instance._hint == null || string.IsNullOrEmpty(text)) return;
            if (!s_taught.Add(s_instance._layout)) return;
            s_instance._hint.text = text;
            s_instance._hintUntil = Time.unscaledTime + 5f;
        }
        static readonly System.Collections.Generic.HashSet<string> s_taught = new();

        /// <summary>The layout's progress bar, 0-1.</summary>
        public static void SetProgress(float value)
        {
            if (s_instance != null && s_instance._progress != null)
                s_instance._progress.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
        }

        /// <summary>Key tile text: the keyboard key, or the pad's button once a controller is in use.</summary>
        public static string KeyText(GameAction action, string keyboardOverride = null) =>
            GameInput.UsingPad ? action.PadLabel : keyboardOverride ?? action.KeyLabel;

        static MinigameHud Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindAnyObjectByType<MinigameHud>();
                    if (s_instance == null) s_instance = new GameObject("MinigameHud").AddComponent<MinigameHud>();
                }
                return s_instance;
            }
        }

        UIDocument _doc;
        bool _open, _mouse, _cursorFreed;
        CursorLockMode _cursorBefore;
        bool _cursorVisibleBefore;
        VisualElement _flash, _progress;
        Label _flashText, _hint;
        float _flashUntil, _hintUntil;
        string _layout;

        void Awake()
        {
            if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
            s_instance = this;
            DontDestroyOnLoad(gameObject);
            var settings = Resources.Load<PanelSettings>("Hud/HudPanelSettings");
            if (settings == null) { Debug.LogWarning("[Minigame] Resources/Hud/HudPanelSettings is missing."); enabled = false; return; }
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            _doc.sortingOrder = 6;   // above MatchHud (0) and the trash minigame (5), below the pause menu (10)
        }

        void OnDestroy()
        {
            if (s_instance == this) { RestoreCursor(); s_instance = null; }
        }

        VisualElement OpenInternal(string layout, bool freeCursor, bool takesMouse)
        {
            if (_doc == null) return null;
            var tree = Resources.Load<VisualTreeAsset>("Hud/Minigames/" + layout);
            if (tree == null) { Debug.LogWarning($"[Minigame] Resources/Hud/Minigames/{layout}.uxml is missing."); return null; }

            var root = _doc.rootVisualElement;
            root.Clear();
            tree.CloneTree(root);
            root.pickingMode = PickingMode.Ignore;
            // Nothing takes the mouse except what the layout marks "mg-pick" (a drag board).
            root.Query<VisualElement>().ForEach(e =>
                e.pickingMode = e.ClassListContains("mg-pick") ? PickingMode.Position : PickingMode.Ignore);

            _flash = root.Q("mg-flash");
            _flashText = root.Q<Label>("mg-flash-text");
            _hint = root.Q<Label>("mg-onboard");
            _layout = layout;
            _hintUntil = 0f;
            _progress = root.Q("mg-progress-fill");
            if (_flash != null) _flash.style.opacity = 0f;
            _flashUntil = 0f;

            _open = true;
            _mouse = takesMouse;
            if (freeCursor && !_cursorFreed)
            {
                _cursorFreed = true;
                _cursorBefore = Cursor.lockState;
                _cursorVisibleBefore = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return root;
        }

        void CloseInternal()
        {
            _open = false;
            _mouse = false;
            if (_doc != null) _doc.rootVisualElement.Clear();
            _flash = _progress = null;
            _flashText = _hint = null;
            RestoreCursor();
        }

        void RestoreCursor()
        {
            if (!_cursorFreed) return;
            _cursorFreed = false;
            Cursor.lockState = _cursorBefore;
            Cursor.visible = _cursorVisibleBefore;
        }

        void Update()
        {
            if (!_open) return;
            if (_flash != null) _flash.style.opacity = Mathf.Clamp01((_flashUntil - Time.unscaledTime) * 3f);
            if (_hint != null) _hint.style.opacity = Mathf.Clamp01((_hintUntil - Time.unscaledTime) * 1.5f);
            // The pause menu frees the cursor too; ours stays free under it and comes back on close.
            if (_cursorFreed && !PauseMenu.IsOpen && Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}
