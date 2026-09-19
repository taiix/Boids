#if UNITY_EDITOR
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FishGame
{
    /// <summary>
    /// EDITOR-ONLY test helper. Press Play in a game scene and the game runs <b>fully offline</b> —
    /// no networking, no host, no spawned network player — so you can iterate on mechanics locally
    /// with whatever is already in the scene (e.g. the placed shark). Offline-capable systems
    /// (FoodSpawner, FoodPellet, MatchManager, FishVitals) self-simulate; the same code stays
    /// server-authoritative in real multiplayer.
    ///
    /// Press <b>F9</b> at any time to spin up a solo networked host over a local transport
    /// (Telepathy, no Steam) to test the multiplayer path — that spawns the networked PlayerFish.
    ///
    /// It never interferes with real hosting: it only boots in a game scene, and if the normal Steam
    /// flow starts networking it stands down. Wrapped in UNITY_EDITOR so it's stripped from builds.
    /// </summary>
    public class DevHost : MonoBehaviour
    {
        // Scenes we allow dev-hosting from when you press Play directly in them (Island + test copy).
        static readonly string[] GameScenes = { "Island", "IslandTest" };
        const Key HostKey = Key.F9;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // Only when you pressed Play with a game scene active = developer test intent.
            if (System.Array.IndexOf(GameScenes, SceneManager.GetActiveScene().name) < 0) return;
            var go = new GameObject("DevHost");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<DevHost>();
        }

        bool _done; // once hosting has started (by us or the Steam flow), stop listening

        void Update()
        {
            if (_done) return;

            // If anything already started networking (e.g. the real Steam flow), stand down.
            if (NetworkServer.active || NetworkClient.active) { _done = true; return; }

            var kb = Keyboard.current;
            if (kb != null && kb[HostKey].wasPressedThisFrame)
                StartDevHost();
        }

        void StartDevHost()
        {
            var nm = NetworkManager.singleton as CustomNetworkManager;
            if (nm == null || NetworkServer.active || NetworkClient.active) return;

            Transport transport = GetOrAddLocalTransport(nm.gameObject);
            Debug.Log($"[DevHost] F9 pressed — starting solo host over {transport.GetType().Name} (editor test, no Steam).");
            nm.DevStartHost(transport);
            _done = true;
        }

        static Transport GetOrAddLocalTransport(GameObject nmGo)
        {
            var tp = nmGo.GetComponent<TelepathyTransport>();
            if (tp == null) tp = nmGo.AddComponent<TelepathyTransport>();
            return tp;
        }
    }
}
#endif
