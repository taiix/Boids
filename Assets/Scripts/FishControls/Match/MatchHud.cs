using Mirror;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Minimal on-screen HUD for the survival round: the countdown plus the local player's
    /// health/hunger (fish and shark alike). Created by the local player's body when it spawns, in
    /// the game scene, so it goes away with that scene - it used to auto-create at startup and sat on
    /// the menu and lobby too. This is a placeholder IMGUI overlay for development — replace with a
    /// proper UI Toolkit HUD later.
    /// </summary>
    public class MatchHud : MonoBehaviour
    {
        static MatchHud s_instance;

        /// <summary>Show the HUD (and the round's banners / results, RoundScreen) in the current (game)
        /// scene, once.</summary>
        public static void Show()
        {
            RoundScreen.Show();
            FishAllies.Show();
            if (s_instance != null) return;
            s_instance = new GameObject("MatchHud").AddComponent<MatchHud>();
        }

        static string s_prompt, s_status;
        static int s_promptFrame = -10, s_statusFrame = -10;
        static GUIStyle s_promptStyle, s_shadowStyle;

        /// <summary>Show an interaction prompt (e.g. "Press E to eat") this frame. Call it every frame
        /// it applies; it disappears on its own once nobody asks for it.</summary>
        public static void ShowPrompt(string text)
        {
            s_prompt = text;
            s_promptFrame = Time.frameCount;
        }

        /// <summary>Show a task status line (e.g. "1/2 fish pushing") this frame, just under the prompt so
        /// the two never fight over one spot. Same rules as <see cref="ShowPrompt"/>.</summary>
        public static void ShowStatus(string text)
        {
            s_status = text;
            s_statusFrame = Time.frameCount;
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
            // Offline sandbox: just show the (single) body that has vitals.
            return FindAnyObjectByType<FishVitals>();
        }

        void OnGUI()
        {
            if (RoundScreen.ShowingResults) return; // the results cover the round
            DrawPrompt();
            GUI.skin.label.richText = true;

            GUILayout.BeginArea(new Rect(12, 12, 300, 220), GUI.skin.box);

            var m = MatchManager.Instance;
            if (m != null)
            {
                switch (m.Phase)
                {
                    case MatchPhase.Warmup:
                        GUILayout.Label("<b>Waiting for everyone...</b>");
                        break;
                    case MatchPhase.Playing:
                        int s = Mathf.CeilToInt(m.RemainingSeconds);
                        GUILayout.Label($"<b>Exit opens in:</b> {s / 60:00}:{s % 60:00}");
                        break;
                    case MatchPhase.Escape:
                        string wall = ArenaWalls.Instance != null ? ArenaWalls.Instance.NameOf(m.ExitWall) : "";
                        GUILayout.Label(string.IsNullOrEmpty(wall)
                            ? "<b><color=#ffc857>EXIT OPEN</color></b>"
                            : $"<b><color=#ffc857>EXIT OPEN:</color></b> {wall} wall");
                        break;
                    default:
                        GUILayout.Label("<b>Round over</b>");
                        break;
                }
            }
            else
            {
                GUILayout.Label("<b>No match manager in scene</b>");
            }

            var v = FindLocalVitals();
            if (v != null)
            {
                bool shark = v.TryGetComponent(out SharkAbilities _);
                GUILayout.Label($"<b>You are:</b> {(shark ? "Shark" : "Fish")}   <size=11>(F8 to swap)</size>");
                Bar("Health", v.HealthNorm, new Color(0.85f, 0.25f, 0.25f));
                Bar("Hunger", v.HungerNorm, new Color(0.9f, 0.7f, 0.2f));
                if (v.IsDead) GUILayout.Label("<b><color=#ff5555>DEAD</color></b>");
            }
            else
            {
                GUILayout.Label("<i>No vitals found</i>");
            }

            GUILayout.EndArea();
        }

        // Centred near the top, the same spot and look as the reef puzzle's prompt; the status line
        // sits just below it, a little smaller.
        static void DrawPrompt()
        {
            bool prompt = !string.IsNullOrEmpty(s_prompt) && Time.frameCount - s_promptFrame <= 1;
            bool status = !string.IsNullOrEmpty(s_status) && Time.frameCount - s_statusFrame <= 1;
            if (!prompt && !status) return;
            if (s_promptStyle == null)
            {
                s_promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true };
                s_shadowStyle = new GUIStyle(s_promptStyle);
            }
            int size = Mathf.Max(16, Screen.height / 26);
            s_promptStyle.normal.textColor = Color.white;
            s_shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);

            float y = Screen.height * 0.29f;
            if (prompt) DrawCentred(s_prompt, y, size);
            if (status) DrawCentred(s_status, y + size * 1.6f, Mathf.RoundToInt(size * 0.8f));
        }

        static void DrawCentred(string text, float y, int size)
        {
            s_promptStyle.fontSize = s_shadowStyle.fontSize = size;
            var r = new Rect(0f, y, Screen.width, size * 1.6f);
            // The shadow drops any colour tags so it stays a shadow.
            string plain = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), plain, s_shadowStyle);
            GUI.Label(r, text, s_promptStyle);
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
