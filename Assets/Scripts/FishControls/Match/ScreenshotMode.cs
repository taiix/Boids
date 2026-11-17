using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// For screenshots during a match: <see cref="ToggleKey"/> (F1) hides the in-game UI - the HUD, the round's
    /// banners, markers and name tags, task panels, the teammate glow and the shark's target brackets - and F1
    /// again brings it back. Only in a match (not the menus or the lobby), in any build. Opening the pause menu,
    /// or leaving the match, brings the UI back.
    ///
    /// UI Toolkit screens are hidden through their root's visibility (which none of them set themselves, so their
    /// own show / hide logic carries on underneath), uGUI canvases by switching them off; anything drawn with
    /// OnGUI and the teammate glow check <see cref="Hidden"/> themselves.
    /// </summary>
    public class ScreenshotMode : MonoBehaviour
    {
        public const Key ToggleKey = Key.F1;

        /// <summary>The UI is hidden for a screenshot.</summary>
        public static bool Hidden { get; private set; }

        static ScreenshotMode s_instance;
        readonly List<Canvas> _offCanvases = new List<Canvas>();   // the ones we switched off, to switch back on

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("ScreenshotMode");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<ScreenshotMode>();
        }

        // In a match: the game scene, with its MatchManager (the menus and the lobby have none).
        static bool InMatch => MatchManager.Instance != null;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[ToggleKey].wasPressedThisFrame && InMatch) SetHidden(!Hidden);
        }

        // Every frame while hidden, so a screen that opens meanwhile (a banner, a task panel) stays hidden too.
        void LateUpdate()
        {
            if (Hidden && (PauseMenu.IsOpen || !InMatch)) SetHidden(false);
            if (!Hidden) return;
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
            {
                var root = doc.rootVisualElement;
                if (root != null && root.style.visibility != Visibility.Hidden) root.style.visibility = Visibility.Hidden;
            }
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
                if (canvas.enabled && canvas.isRootCanvas)
                {
                    canvas.enabled = false;
                    _offCanvases.Add(canvas);
                }
        }

        void SetHidden(bool hidden)
        {
            if (hidden == Hidden) return;
            Hidden = hidden;
            if (hidden) return;   // LateUpdate hides it all

            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
                if (doc.rootVisualElement != null) doc.rootVisualElement.style.visibility = StyleKeyword.Null;
            foreach (var canvas in _offCanvases)
                if (canvas != null) canvas.enabled = true;
            _offCanvases.Clear();
        }
    }
}
