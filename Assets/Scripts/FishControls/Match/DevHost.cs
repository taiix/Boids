#if UNITY_EDITOR
using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishGame
{
    /// <summary>
    /// EDITOR-ONLY quick test. Press Play with the game scene ("Island") active and this hosts a
    /// solo game over a local transport, skipping the Steam menu/lobby so you can iterate fast.
    ///
    /// It never interferes with real hosting: it only boots when the active scene at play start is
    /// the game scene (i.e. you pressed Play directly in it), and it bails immediately if the normal
    /// Steam flow already started networking. Wrapped in UNITY_EDITOR so it's stripped from builds.
    /// </summary>
    public class DevHost : MonoBehaviour
    {
        const string GameScene = "Island";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // Only when you pressed Play with the game scene active = developer test intent.
            if (SceneManager.GetActiveScene().name != GameScene) return;
            var go = new GameObject("DevHost");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<DevHost>();
        }

        IEnumerator Start()
        {
            // Give the real Steam flow a moment to take over; bail out if it does.
            float t = 0f;
            while (t < 1.5f)
            {
                if (NetworkServer.active || NetworkClient.active) { Destroy(gameObject); yield break; }
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            var nm = NetworkManager.singleton as CustomNetworkManager;
            if (nm == null || NetworkServer.active || NetworkClient.active) { Destroy(gameObject); yield break; }

            Transport transport = GetOrAddLocalTransport(nm.gameObject);
            Debug.Log($"[DevHost] Starting solo host over {transport.GetType().Name} (editor test, no Steam).");
            nm.DevStartHost(transport);
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
