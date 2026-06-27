using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// Full-screen launch overlay that survives the lobby -> game scene change.
    ///
    /// The host triggers <see cref="Play"/> on every client (broadcast through
    /// Steam lobby data in SteamLobby/ReefRunLobbyController). Because this object
    /// is DontDestroyOnLoad, the animation keeps playing through Mirror's network
    /// scene load and only fades out once the game scene ("Island") is loaded.
    ///
    /// Put this on a GameObject (e.g. under Bootstrap) with a UIDocument whose
    /// Source Asset is LaunchOverlay.uxml.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LaunchOverlay : MonoBehaviour
    {
        public static LaunchOverlay Instance { get; private set; }

        [Tooltip("Scene name that ends the overlay (fades out once it loads).")]
        public string GameSceneName = "Island";

        UIDocument _doc;
        VisualElement _overlay;
        bool _playing;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(GrabNextFrame());
        }

        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        // Wait one frame so the UIDocument finishes building its tree before we
        // grab the overlay element (same pattern as ReefRunLobbyController).
        System.Collections.IEnumerator GrabNextFrame()
        {
            yield return null;
            var root = _doc != null ? _doc.rootVisualElement : null;
            if (root == null) yield break;
            _overlay = root.Q<VisualElement>("overlay") ?? root;
            _overlay.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Play the launch animation. Stays fully visible until the game scene
        /// loads, at which point it fades out automatically.
        /// </summary>
        public void Play()
        {
            if (_overlay == null || _playing) return;
            _playing = true;
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.schedule.Execute(() => _overlay.AddToClassList("show")).StartingIn(16);
            _overlay.schedule.Execute(() => _overlay.AddToClassList("phase1")).StartingIn(1700);
            _overlay.schedule.Execute(() => _overlay.AddToClassList("phase2")).StartingIn(3000);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_playing || _overlay == null || scene.name != GameSceneName) return;

            // .overlay has `transition: opacity 0.6s` — removing "show" fades it out.
            _overlay.RemoveFromClassList("show");
            _overlay.schedule.Execute(() =>
            {
                _overlay.style.display = DisplayStyle.None;
                _overlay.RemoveFromClassList("phase1");
                _overlay.RemoveFromClassList("phase2");
                _playing = false;
            }).StartingIn(700);
        }
    }
}
