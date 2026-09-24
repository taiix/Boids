using Steamworks;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// Drives the Reef Run main menu (ReefRunMainMenu.uxml).
    /// HOST loads LobbySceneName. SETTINGS opens the shared settings overlay
    /// (ReefRunSettings.uxml, see <see cref="ReefRunSettingsPanel"/>) - the same one the in-game
    /// pause menu opens.
    /// Override OnJoin / OnHost to add scene-transition logic.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ReefRunMainMenuController : MonoBehaviour
    {
        // ---- inspector ----
        public string FriendsText = "5 friends online";
        public string Version = "v0.4.1 — EARLY ACCESS BUILD";
        public string LobbySceneName = "02 - ReefRunLobby";

        UIDocument _doc;
        Texture2D _bgTex;
        ReefRunSettingsPanel _settings;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            StartCoroutine(BuildNextFrame());
        }

        IEnumerator BuildNextFrame()
        {
            yield return null;
            var root = _doc != null ? _doc.rootVisualElement : null;
            if (root == null) yield break;
            BuildUI(root);
        }

        void BuildUI(VisualElement root)
        {
            // background + ambient
            if (_bgTex == null) _bgTex = BuildBackground();
            root.Q<VisualElement>("stage").style.backgroundImage = new StyleBackground(_bgTex);
            var mount = root.Q<VisualElement>("ambient-mount");
            mount.Clear();
            mount.Add(new AmbientReef { style = { flexGrow = 1 } });

            // player card

            SetText(root, "player-name", SteamFriends.GetPersonaName());
            SetImage(root, "player-image", SteamManager.GetLocalSteamAvatar(SteamUser.GetSteamID()));
            SetText(root, "player-sub", FriendsText);
            SetText(root, "version-label", Version);

            // settings overlay (also restores the saved master volume)
            _settings = new ReefRunSettingsPanel(root);

            // nav buttons
            Wire(root, "play-btn", OnHost);
            Wire(root, "join-btn", OnJoin);
            Wire(root, "settings-btn", OpenSettings);
            Wire(root, "quit-btn", OnQuit);
        }

        // ===================================================================
        //  NAV ACTIONS
        // ===================================================================
        protected virtual void OnHost() {
            if (SteamLobby.instance != null)
            {
                SteamLobby.instance.CreateLobby();
            }
        }

        protected virtual void OnJoin() => Debug.Log("[ReefRun] JOIN");

        protected virtual void OnQuit()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        void OpenSettings() => _settings?.Open();

        // ===================================================================
        //  HELPERS
        // ===================================================================
        static void Wire(VisualElement root, string name, System.Action cb)
        {
            var btn = root.Q<Button>(name);
            if (btn != null) btn.clicked += cb;
        }

        static void SetText(VisualElement root, string name, string text)
        {
            var lbl = root.Q<Label>(name);
            if (lbl != null) lbl.text = text;
        }

        static void SetImage(VisualElement root, string name, Texture2D tex)
        {
            var img = root.Q<VisualElement>(name);
            if (img != null) img.style.backgroundImage = new StyleBackground(tex);
        }

        // Dark navy background, soft teal glow in upper-right corner.
        // Texture2D pixel (0,0) = bottom-left; (w-1,h-1) = top-right.
        static Texture2D BuildBackground(int w = 160, int h = 90)
        {
            var dark = Hex("050D12");
            var navy = Hex("081A22");
            var glow = Hex("1DA896");

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];

            float gx = w * 0.86f;
            float gy = h * 0.82f;
            float gr = h * 1.05f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color b = Color.Lerp(navy, dark, (float)x / w * 0.6f + 0.2f);
                    float d = Mathf.Sqrt((x - gx) * (x - gx) + (y - gy) * (y - gy));
                    float gi = Mathf.Clamp01(1f - d / gr);
                    gi = gi * gi * 0.52f;
                    Color c = b + glow * gi;
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
    }
}
