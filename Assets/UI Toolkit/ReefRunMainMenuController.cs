using Steamworks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// Drives the Reef Run main menu (ReefRunMainMenu.uxml).
    /// HOST loads LobbySceneName. SETTINGS opens a modal overlay with
    /// audio sliders, display dropdowns, and fullscreen/vsync toggles.
    /// All values persist to PlayerPrefs and are applied on Apply.
    /// Override OnJoin / OnHost to add scene-transition logic.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ReefRunMainMenuController : MonoBehaviour
    {
        // ---- inspector ----
        public string FriendsText = "5 friends online";
        public string Version = "v0.4.1 — EARLY ACCESS BUILD";
        public string LobbySceneName = "ReefRunLobby";

        // PlayerPrefs keys
        const string K_MASTER = "rr_vol_master";
        const string K_MUSIC = "rr_vol_music";
        const string K_SFX = "rr_vol_sfx";
        const string K_QUALITY = "rr_quality";
        const string K_RES_IDX = "rr_res_idx";
        const string K_FULLSCREEN = "rr_fullscreen";
        const string K_VSYNC = "rr_vsync";

        UIDocument _doc;
        Texture2D _bgTex;

        // settings controls (queried once in BuildUI)
        VisualElement _settingsOverlay;
        Slider _masterSlider, _musicSlider, _sfxSlider;
        Label _masterVal, _musicVal, _sfxVal;
        DropdownField _resDropdown, _qualityDropdown;
        Toggle _fullscreenToggle, _vsyncToggle;
        Resolution[] _resolutions;

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
            ApplySavedToControls();   // populate controls from PlayerPrefs
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

            // nav buttons
            Wire(root, "play-btn", OnHost);
            Wire(root, "join-btn", OnJoin);
            Wire(root, "settings-btn", OpenSettings);
            Wire(root, "quit-btn", OnQuit);

            // ---- settings overlay ----
            _settingsOverlay = root.Q<VisualElement>("settings-overlay");
            _masterSlider = root.Q<Slider>("master-slider");
            _musicSlider = root.Q<Slider>("music-slider");
            _sfxSlider = root.Q<Slider>("sfx-slider");
            _masterVal = root.Q<Label>("master-val");
            _musicVal = root.Q<Label>("music-val");
            _sfxVal = root.Q<Label>("sfx-val");
            _resDropdown = root.Q<DropdownField>("resolution-dropdown");
            _qualityDropdown = root.Q<DropdownField>("quality-dropdown");
            _fullscreenToggle = root.Q<Toggle>("fullscreen-toggle");
            _vsyncToggle = root.Q<Toggle>("vsync-toggle");

            Wire(root, "settings-close", CloseSettings);
            Wire(root, "settings-apply", ApplySettings);

            // live percentage labels while dragging (no audio change until Apply)
            _masterSlider?.RegisterValueChangedCallback(e =>
            {
                _masterVal.text = Pct(e.newValue);
                AudioListener.volume = e.newValue; // live audio preview
            });
            _musicSlider?.RegisterValueChangedCallback(e => _musicVal.text = Pct(e.newValue));
            _sfxSlider?.RegisterValueChangedCallback(e => _sfxVal.text = Pct(e.newValue));

            // populate resolution dropdown from Screen.resolutions
            _resolutions = Screen.resolutions;
            if (_resolutions != null && _resolutions.Length > 0)
            {
                var choices = new List<string>(_resolutions.Length);
                int fallback = 0;
                for (int i = 0; i < _resolutions.Length; i++)
                {
                    var r = _resolutions[i];
                    choices.Add($"{r.width} × {r.height}  {(int)r.refreshRateRatio.value}Hz");
                    if (r.width == Screen.width && r.height == Screen.height)
                        fallback = i;
                }
                _resDropdown.choices = choices;
                _resDropdown.index = Mathf.Clamp(
                    PlayerPrefs.GetInt(K_RES_IDX, fallback), 0, _resolutions.Length - 1);
            }
            else
            {
                _resDropdown.choices = new List<string> { $"{Screen.width} × {Screen.height}" };
                _resDropdown.index = 0;
            }

            // populate quality dropdown from Unity quality levels
            _qualityDropdown.choices = new List<string>(QualitySettings.names);
            _qualityDropdown.index = Mathf.Clamp(
                PlayerPrefs.GetInt(K_QUALITY, QualitySettings.GetQualityLevel()),
                0, QualitySettings.names.Length - 1);
        }

        // Restore saved values into the slider/toggle controls.
        void ApplySavedToControls()
        {
            float master = PlayerPrefs.GetFloat(K_MASTER, 1f);
            float music = PlayerPrefs.GetFloat(K_MUSIC, 1f);
            float sfx = PlayerPrefs.GetFloat(K_SFX, 1f);

            if (_masterSlider != null) { _masterSlider.SetValueWithoutNotify(master); _masterVal.text = Pct(master); }
            if (_musicSlider != null) { _musicSlider.SetValueWithoutNotify(music); _musicVal.text = Pct(music); }
            if (_sfxSlider != null) { _sfxSlider.SetValueWithoutNotify(sfx); _sfxVal.text = Pct(sfx); }

            AudioListener.volume = master;

            bool fs = PlayerPrefs.GetInt(K_FULLSCREEN, Screen.fullScreen ? 1 : 0) == 1;
            bool vsync = PlayerPrefs.GetInt(K_VSYNC, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
            _fullscreenToggle?.SetValueWithoutNotify(fs);
            _vsyncToggle?.SetValueWithoutNotify(vsync);
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

        // ===================================================================
        //  SETTINGS
        // ===================================================================
        void OpenSettings()
        {
            if (_settingsOverlay == null) return;
            // re-sync sliders to saved values each time panel opens
            ApplySavedToControls();
            _settingsOverlay.style.display = DisplayStyle.Flex;
            _settingsOverlay.schedule.Execute(
                () => _settingsOverlay.AddToClassList("show")).StartingIn(16);
        }

        void CloseSettings()
        {
            if (_settingsOverlay == null) return;
            // revert live master volume preview to last saved value
            AudioListener.volume = PlayerPrefs.GetFloat(K_MASTER, 1f);
            _settingsOverlay.RemoveFromClassList("show");
            _settingsOverlay.schedule.Execute(
                () => _settingsOverlay.style.display = DisplayStyle.None).StartingIn(260);
        }

        void ApplySettings()
        {
            float master = _masterSlider.value;
            float music = _musicSlider.value;
            float sfx = _sfxSlider.value;
            bool fs = _fullscreenToggle.value;
            int vsync = _vsyncToggle.value ? 1 : 0;
            int qi = _qualityDropdown.index;
            int ri = _resDropdown.index;

            // apply audio
            AudioListener.volume = master;

            // apply display
            QualitySettings.SetQualityLevel(qi, true);
            QualitySettings.vSyncCount = vsync;
            Screen.fullScreen = fs;
            if (_resolutions != null && ri >= 0 && ri < _resolutions.Length)
            {
                var r = _resolutions[ri];
                Screen.SetResolution(r.width, r.height, fs);
            }

            // persist
            PlayerPrefs.SetFloat(K_MASTER, master);
            PlayerPrefs.SetFloat(K_MUSIC, music);
            PlayerPrefs.SetFloat(K_SFX, sfx);
            PlayerPrefs.SetInt(K_QUALITY, qi);
            PlayerPrefs.SetInt(K_RES_IDX, ri);
            PlayerPrefs.SetInt(K_FULLSCREEN, fs ? 1 : 0);
            PlayerPrefs.SetInt(K_VSYNC, vsync);
            PlayerPrefs.Save();

            CloseSettings();
        }

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

        static string Pct(float v) => $"{Mathf.RoundToInt(v * 100)}%";

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
