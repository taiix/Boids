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
    /// scene load and only fades out once the round has actually started - i.e. once every player
    /// has loaded (see MatchManager), so nobody gets a head start on a faster machine.
    ///
    /// Put this on a GameObject (e.g. under Bootstrap) with a UIDocument whose
    /// Source Asset is LaunchOverlay.uxml.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class LaunchOverlay : MonoBehaviour
    {
        public static LaunchOverlay Instance { get; private set; }

        [Tooltip("Fallback only: the overlay ends when the NetworkManager's Game Scene Name loads (and the " +
                 "round starts). This is used just if no CustomNetworkManager is around.")]
        public string GameSceneName = "03 - Island";

        // The match scene is set once, on the NetworkManager - a second copy here got out of step once
        // (the overlay then never faded).
        string MatchScene => Mirror.NetworkManager.singleton is CustomNetworkManager nm ? nm.GameSceneName : GameSceneName;

        [Tooltip("Shown while this machine has loaded but others are still loading.")]
        public string WaitingText = "Waiting for everyone to reach the reef...";

        [Tooltip("Give up waiting for the round and reveal the scene after this many seconds.")]
        public float MaxWaitSeconds = 120f;

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
            _overlay.style.opacity = 1f;   // instant full-dark cover (no lobby flash)
            // text reveals still animate via USS .ov-line transitions
            _overlay.schedule.Execute(() => _overlay.AddToClassList("phase1")).StartingIn(1700);
            _overlay.schedule.Execute(() => _overlay.AddToClassList("phase2")).StartingIn(3000);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_playing || _overlay == null || scene.name != MatchScene) return;
            StartCoroutine(FadeOutWhenRoundStarts(0.6f));
        }

        // The server flips RoundLive on every player once the last one has loaded; we watch our own.
        System.Collections.IEnumerator FadeOutWhenRoundStarts(float dur)
        {
            var waitingLabel = _overlay.Q<Label>("ov-line-2");
            bool showedWaiting = false;
            float waited = 0f;
            while (waited < MaxWaitSeconds)
            {
                if (!Mirror.NetworkClient.active) break;   // offline: nothing to wait for
                var local = Mirror.NetworkClient.localPlayer;
                if (local != null && local.TryGetComponent(out FishPlayer me) && me.RoundLive) break;
                if (!showedWaiting && waitingLabel != null) { waitingLabel.text = WaitingText; showedWaiting = true; }
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return FadeOutWhenSettled(dur);
        }

        // Fade the cover out once the new scene is up. We DON'T use the USS opacity
        // transition here: sceneLoaded fires while Island is still doing its heavy
        // first frame, and that single long frame swallows the whole transition so
        // it looks instant. Instead we wait a few frames for the load to settle,
        // then interpolate opacity ourselves on unscaled time.
        System.Collections.IEnumerator FadeOutWhenSettled(float dur)
        {
            yield return null;
            yield return null;
            yield return null;

            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                _overlay.style.opacity = Mathf.Clamp01(1f - t / dur);
                yield return null;
            }

            _overlay.style.opacity = 0f;
            _overlay.style.display = DisplayStyle.None;
            _overlay.RemoveFromClassList("phase1");
            _overlay.RemoveFromClassList("phase2");
            _playing = false;
        }
    }
}
