using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Minimal on-screen HUD for the survival round: the countdown plus the local fish's
    /// health/hunger. Auto-creates itself at runtime so it needs no scene wiring. This is a
    /// placeholder IMGUI overlay for development — replace with a proper UI Toolkit HUD later.
    /// </summary>
    public class MatchHud : MonoBehaviour
    {
        //[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var go = new GameObject("MatchHud (auto)");
            DontDestroyOnLoad(go);
            go.AddComponent<MatchHud>();
        }

        static Texture2D _px;
        static Texture2D Px
        {
            get
            {
                if (_px == null)
                {
                    _px = new Texture2D(1, 1);
                    _px.SetPixel(0, 0, Color.white);
                    _px.Apply();
                }
                return _px;
            }
        }

        static FishVitals FindLocalVitals()
        {
            if (NetworkClient.active && NetworkClient.localPlayer != null)
                return NetworkClient.localPlayer.GetComponent<FishVitals>();
            // Offline sandbox: just show the first fish that has vitals.
            return FindFirstObjectByType<FishVitals>();
        }

        void OnGUI()
        {
            GUI.skin.label.richText = true;

            GUILayout.BeginArea(new Rect(12, 12, 300, 220), GUI.skin.box);

            var m = MatchManager.Instance;
            if (m != null)
            {
                GUILayout.Label($"<b>Phase:</b> {m.Phase}");
                int s = Mathf.CeilToInt(m.RemainingSeconds);
                GUILayout.Label($"<b>Survive:</b> {s / 60:00}:{s % 60:00}");
            }
            else
            {
                GUILayout.Label("<b>No match manager in scene</b>");
            }

            var v = FindLocalVitals();
            if (v != null)
            {
                Bar("Health", v.HealthNorm, new Color(0.85f, 0.25f, 0.25f));
                Bar("Hunger", v.HungerNorm, new Color(0.9f, 0.7f, 0.2f));
                if (v.IsDead) GUILayout.Label("<b><color=#ff5555>DEAD</color></b>");
            }
            else
            {
                GUILayout.Label("<i>No fish vitals found</i>");
            }

            GUILayout.EndArea();
        }

        static void Bar(string label, float t, Color fill)
        {
            t = Mathf.Clamp01(t);
            GUILayout.Label($"{label}: {Mathf.RoundToInt(t * 100)}%");
            Rect r = GUILayoutUtility.GetRect(276, 16);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(r, Px);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * t, r.height), Px);
            GUI.color = prev;
        }
    }
}
