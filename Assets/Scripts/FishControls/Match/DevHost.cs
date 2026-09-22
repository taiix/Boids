#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using Mirror;
using Unity.Multiplayer.PlayMode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FishGame
{
    /// <summary>
    /// EDITOR-ONLY test helper for pressing Play directly in a game scene. Three modes, picked
    /// automatically:
    ///
    ///   * <b>Local multiplayer test</b> (Multiplayer Play Mode virtual players are active; set them up in
    ///     Window > Multiplayer > Multiplayer Play Mode): the main Editor hosts and every virtual player
    ///     joins it over localhost. Steam is bypassed entirely - every instance on this PC is the same
    ///     Steam account, and Steam cannot connect an account to itself. The host holds spawns until
    ///     all expected players have connected so the shark draw is the same as in a real match.
    ///   * <b>Offline sandbox</b> (no virtual players): no networking at all, so mechanics can be iterated
    ///     on with whatever is in the scene. Offline-capable systems self-simulate.
    ///   * <b>F9</b> in the offline sandbox: start a solo networked host to test the multiplayer path.
    ///
    /// It never interferes with real hosting: it only boots when Play starts in a game scene, and if
    /// the normal Steam flow starts networking it stands down. Stripped from builds.
    /// </summary>
    public class DevHost : MonoBehaviour
    {
        // Scenes we allow dev-hosting from when you press Play directly in them (Island + test copy).
        static readonly string[] GameScenes = { "03 - Island", "IslandTest" };
        const Key HostKey = Key.F9;
        const string LocalAddress = "localhost";
        const float JoinRetrySeconds = 1.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // Main Editor: only when you pressed Play with a game scene active = developer test intent.
            // Virtual players boot whatever scene they have open - their editor does not reliably follow
            // the main Editor's scene - and the host's SceneMessage moves them into the game scene.
            bool gameScene = System.Array.IndexOf(GameScenes, SceneManager.GetActiveScene().name) >= 0;
            if (!gameScene && CurrentPlayer.IsMainEditor) return;
            var go = new GameObject("DevHost");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<DevHost>();
        }

        enum Mode { Offline, AutoHost, AutoJoin }

        /// <summary>
        /// True when this Play session is a Multiplayer Play Mode test (we are a virtual player, or the
        /// main Editor with virtual players active). Offline-only helpers such as Debug_Clients check
        /// this so they don't add a local, non-networked player on top of the networked ones.
        /// </summary>
        public static bool IsLocalMultiplayerTest =>
            !CurrentPlayer.IsMainEditor || CountActiveVirtualPlayers() > 0;

        Mode _mode;
        bool _done;          // networking is up (started by us or by the Steam flow); stop acting
        float _nextJoinTry;

        void Start()
        {
            if (!CurrentPlayer.IsMainEditor)
            {
                _mode = Mode.AutoJoin;
                Debug.Log($"[DevHost] Virtual player: joining the main Editor's host at {LocalAddress}.");
                return;
            }

            int virtualPlayers = CountActiveVirtualPlayers();
            if (virtualPlayers > 0)
            {
                _mode = Mode.AutoHost;
                StartDevHost(1 + virtualPlayers);
            }
            else
            {
                _mode = Mode.Offline;
                Debug.Log("[DevHost] Offline sandbox. Press F9 to host solo, or activate virtual players in " +
                          "Window > Multiplayer > Multiplayer Play Mode to test with several players.");
            }
        }

        void Update()
        {
            if (_done) return;

            if (_mode == Mode.AutoJoin)
            {
                if (NetworkClient.isConnected) { _done = true; return; }
                // The host may still be loading; keep retrying until it accepts us.
                if (!NetworkClient.active && Time.unscaledTime >= _nextJoinTry)
                {
                    _nextJoinTry = Time.unscaledTime + JoinRetrySeconds;
                    StartDevClient();
                }
                return;
            }

            // If anything already started networking (e.g. the real Steam flow), stand down.
            if (NetworkServer.active || NetworkClient.active) { _done = true; return; }

            var kb = Keyboard.current;
            if (kb != null && kb[HostKey].wasPressedThisFrame)
                StartDevHost(1);
        }

        void StartDevHost(int expectedPlayers)
        {
            var nm = NetworkManager.singleton as CustomNetworkManager;
            if (nm == null || NetworkServer.active || NetworkClient.active) return;

            Transport transport = GetOrAddLocalTransport(nm.gameObject);
            Debug.Log(expectedPlayers > 1
                ? $"[DevHost] Hosting a local test for {expectedPlayers} players over {transport.GetType().Name} (no Steam)."
                : $"[DevHost] F9 pressed - starting solo host over {transport.GetType().Name} (editor test, no Steam).");
            nm.DevStartHost(transport, expectedPlayers);
            _done = true;
        }

        void StartDevClient()
        {
            var nm = NetworkManager.singleton as CustomNetworkManager;
            if (nm == null) return;
            nm.DevStartClient(GetOrAddLocalTransport(nm.gameObject), LocalAddress);
        }

        static Transport GetOrAddLocalTransport(GameObject nmGo)
        {
            var tp = nmGo.GetComponent<TelepathyTransport>();
            if (tp == null) tp = nmGo.AddComponent<TelepathyTransport>();
            return tp;
        }

        /// <summary>
        /// Active virtual players, read from Multiplayer Play Mode's own state file. There is no public
        /// API for this, so the file is read defensively: if its format ever changes this returns 0 and
        /// Play falls back to the offline sandbox (F9 still works) instead of breaking.
        /// </summary>
        static int CountActiveVirtualPlayers()
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "VP", "SystemData.json"));
                if (!File.Exists(path)) return 0;
                if (!Regex.IsMatch(File.ReadAllText(path), "\"IsMppmActive\"\\s*:\\s*true")) return 0;

                // Each player entry lists "Active" before "Type" (0 = main Editor, 1 = virtual player),
                // with no nested objects between them.
                int count = 0;
                foreach (Match m in Regex.Matches(File.ReadAllText(path), "\"Active\"\\s*:\\s*(true|false)[^{}]*?\"Type\"\\s*:\\s*(\\d+)"))
                    if (m.Groups[1].Value == "true" && m.Groups[2].Value == "1") count++;
                return count;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DevHost] Could not read Multiplayer Play Mode state; assuming no virtual players. " + e.Message);
                return 0;
            }
        }
    }
}
#endif
