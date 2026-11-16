using Mirror;
using ReefRun;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace FishGame
{
    /// <summary>
    /// In-game menu on Escape: Resume, Settings, Back to Lobby, Main Menu, Quit.
    ///
    /// It's multiplayer, so the game keeps running: opening the menu only frees the cursor and takes
    /// your controls away (FishPlayer, the orbit camera and the reef task all check <see cref="IsOpen"/>).
    /// Settings opens the same settings screen as the main menu (ReefRunSettings.uxml on a UIDocument).
    /// The menu itself is UI Toolkit, built in code in the main menu's style (Resources/Hud/PauseMenu.uss);
    /// the old uGUI canvas in the prefab stays hidden.
    /// Back to Lobby is the host's call - it takes everyone back, as the lobby is where the host starts
    /// the next round. Anyone can leave to the main menu or quit.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        /// <summary>True while the local player has the menu (or its settings screen) open.</summary>
        public static bool IsOpen { get; private set; }

        [Header("Menu")]
        [Tooltip("The old uGUI menu. Kept hidden; the menu is now built in UI Toolkit.")]
        [SerializeField] GameObject menuRoot;

        [Header("Settings")]
        [Tooltip("UIDocument showing ReefRunSettings.uxml. Kept inactive until Settings is pressed.")]
        [SerializeField] UIDocument settingsDocument;

        [Header("Scenes")]
        [SerializeField] string mainMenuScene = "01 - MainMenu";
        [Tooltip("Used when there's no SteamLobby to ask (it normally knows the lobby scene).")]
        [SerializeField] string fallbackLobbyScene = "02 - ReefRunLobby";

        ReefRunSettingsPanel _settings;
        UIDocument _doc;
        VisualElement _pause;
        UnityEngine.UIElements.Button _lobbyButton;
        Label _lobbyHint, _note;
        const string PausedNote = "The game keeps going while you're in here.";
        CursorLockMode _cursorBefore;
        bool _cursorVisibleBefore;

        void Awake()
        {
            IsOpen = false;
            if (menuRoot != null) menuRoot.SetActive(false);   // legacy uGUI menu: never shown

            // Same 1920x1080 panel as the HUD, so the settings screen lays out like the main menu's.
            var panel = Resources.Load<PanelSettings>("Hud/HudPanelSettings");
            if (settingsDocument != null)
            {
                if (panel != null) settingsDocument.panelSettings = panel;
                settingsDocument.sortingOrder = 20;
                settingsDocument.gameObject.SetActive(false);
            }

            if (panel != null)
            {
                var go = new GameObject("PauseMenuUI");
                go.transform.SetParent(transform, false);
                _doc = go.AddComponent<UIDocument>();
                _doc.panelSettings = panel;
                _doc.sortingOrder = 10;   // above the HUD
            }
        }

        void OnEnable() => GameInput.ControllerDisconnected += OnControllerDisconnected;

        void Start() => BuildMenu();

        void BuildMenu()
        {
            if (_pause != null || _doc == null || _doc.rootVisualElement == null) return;
            var root = _doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            var sheet = Resources.Load<StyleSheet>("Hud/PauseMenu");
            if (sheet != null) root.styleSheets.Add(sheet);

            _pause = new VisualElement();
            _pause.AddToClassList("pause");
            _pause.style.display = DisplayStyle.None;
            root.Add(_pause);

            // Title, top left.
            var title = new VisualElement { pickingMode = PickingMode.Ignore };
            title.AddToClassList("pause-title");
            title.Add(Lbl("PAUSED", "pause-word"));
            _note = Lbl(PausedNote, "pause-note");
            title.Add(_note);
            _pause.Add(title);

            // Nav, bottom left: RESUME as the one orange block (ReefSlab), the rest as words on a navy strip.
            var nav = new VisualElement { pickingMode = PickingMode.Ignore };
            nav.AddToClassList("pause-nav");
            _pause.Add(nav);

            var resume = new UnityEngine.UIElements.Button(() => SetOpen(false));
            resume.AddToClassList("pause-primary");
            var resumeSlab = new ReefSlab { pickingMode = PickingMode.Ignore };
            resumeSlab.AddToClassList("slab");
            resumeSlab.AddToClassList("pause-primary-slab");
            var resumeLabel = Lbl("RESUME", "pause-primary-label");
            var resumeKey = InputGlyphs.Tile(GameInput.Pause, InputSet.Menu);
            resumeLabel.pickingMode = PickingMode.Ignore;
            resumeKey.pickingMode = PickingMode.Ignore;
            resumeSlab.Add(resumeLabel);
            resumeSlab.Add(resumeKey);
            resume.Add(resumeSlab);
            nav.Add(resume);

            var more = new ReefSlab();
            more.AddToClassList("slab");
            more.AddToClassList("pause-more");
            nav.Add(more);
            Btn(more, "SETTINGS", OpenSettings);
            _lobbyButton = Btn(more, "BACK TO LOBBY", BackToLobby);
            Btn(more, "MAIN MENU", ToMainMenu);
            Btn(more, "QUIT GAME", Quit);

            _lobbyHint = Lbl("", "pause-hint");
            nav.Add(_lobbyHint);

            // Controller: d-pad / stick between the buttons, A presses, B resumes.
            UiPad.Register(_pause, () => SetOpen(false), () => resume, priority: 20);
        }

        static UnityEngine.UIElements.Button Btn(VisualElement parent, string text, System.Action onClick)
        {
            var b = new UnityEngine.UIElements.Button(onClick) { text = text };
            b.AddToClassList("pause-btn");
            parent.Add(b);
            return b;
        }

        static Label Lbl(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) l.AddToClassList(c);
            return l;
        }

        void ShowMenu(bool show)
        {
            BuildMenu();
            if (_pause == null) return;
            if (show)
            {
                _pause.style.display = DisplayStyle.Flex;
                _pause.schedule.Execute(() => _pause.AddToClassList("pause--show")).StartingIn(16);
            }
            else
            {
                _pause.RemoveFromClassList("pause--show");
                _pause.style.display = DisplayStyle.None;
            }
        }
        void OnDisable() => GameInput.ControllerDisconnected -= OnControllerDisconnected;

        // The controller they were playing on went away (unplugged, flat battery): open the menu, so they
        // aren't left swimming blind, and say why. It's multiplayer, so the round itself carries on.
        void OnControllerDisconnected()
        {
            if (RoundScreen.ShowingResults) return;   // already on a menu
            SetOpen(true);
            if (_note != null) _note.text = "Controller disconnected. Reconnect it, or carry on with keyboard and mouse.";
        }

        bool _quitting;
        void OnApplicationQuit() => _quitting = true;

        // The game scene is going away (back to the lobby / menu, or the host took everyone back):
        // the menus there need a free cursor, and the lobby needs this round's flags cleared.
        void OnDestroy()
        {
            IsOpen = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // Not when the game is closing: SteamManager is already gone, and asking it re-creates it
            // ("Tried to Initialize the SteamAPI twice"). Leaving Steam drops our lobby data anyway.
            if (!_quitting) ResetLobbyFlags();
        }

        void Update()
        {
            if (GameInput.Pause.WasPressedThisFrame())
            {
                if (_settings != null && _settings.IsOpen) _settings.Close(); // Esc backs out of settings first
                else SetOpen(!IsOpen);
            }

            if (!IsOpen) return;
            // A respawn (eaten, F8) re-locks the cursor from the new camera - keep it free while open.
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshLobbyButton();
        }

        void SetOpen(bool open)
        {
            if (open == IsOpen) return;
            IsOpen = open;
            if (open)
            {
                _cursorBefore = Cursor.lockState;
                _cursorVisibleBefore = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                RefreshLobbyButton();
            }
            else
            {
                Cursor.lockState = _cursorBefore;
                Cursor.visible = _cursorVisibleBefore;
                if (_note != null) _note.text = PausedNote;
                if (_settings != null && _settings.IsOpen) _settings.Close();
            }
            ShowMenu(open);
        }

        void RefreshLobbyButton()
        {
            bool host = NetworkServer.active;
            _lobbyButton?.SetEnabled(host);
            if (_lobbyHint != null)
            {
                _lobbyHint.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
                _lobbyHint.text = NetworkClient.active
                    ? "Only the host can take everyone back to the lobby"
                    : "Not in an online game";
            }
        }

        // ------------------------------------------------------------------ settings
        void OpenSettings()
        {
            if (settingsDocument == null) return;
            // The document is rebuilt each time it's enabled, so bind a fresh panel to it.
            settingsDocument.gameObject.SetActive(true);
            var root = settingsDocument.rootVisualElement;
            if (root == null) return;
            _settings = new ReefRunSettingsPanel(root);
            UiPad.Register(root, () => _settings?.Close(), priority: 30);
            _settings.Closed += OnSettingsClosed;
            ShowMenu(false);
            _settings.Open();
        }

        void OnSettingsClosed()
        {
            _settings = null;
            if (settingsDocument != null && settingsDocument.rootVisualElement != null) UiPad.Unregister(settingsDocument.rootVisualElement);
            if (settingsDocument != null) settingsDocument.gameObject.SetActive(false);
            ShowMenu(IsOpen);
        }

        // ------------------------------------------------------------------ leaving
        void BackToLobby()
        {
            if (!NetworkServer.active) return;
            string lobby = SteamLobby.instance != null && !string.IsNullOrEmpty(SteamLobby.instance.LobbySceneName)
                ? SteamLobby.instance.LobbySceneName
                : fallbackLobbyScene;
            NetworkManager.singleton.ServerChangeScene(lobby); // pulls every client along
        }

        void ToMainMenu()
        {
            // Same as the lobby's Back button: leave the Steam lobby, stop host/client, load the menu.
            SteamLobby.instance?.LeaveLobby();
            SceneManager.LoadScene(mainMenuScene);
        }

        void Quit()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // Back in the lobby everyone starts un-readied, and the host's next Start must be a change of
        // "starting" or clients never hear it. Steam lobby data outlives the round, so clear both here.
        static void ResetLobbyFlags()
        {
            if (!SteamManager.Initialized || SteamLobby.instance == null) return;
            CSteamID lobby = SteamLobby.instance.CurrentLobbyId;
            if (!lobby.IsValid()) return;
            SteamMatchmaking.SetLobbyMemberData(lobby, "ready", "0");
            if (SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID())
                SteamMatchmaking.SetLobbyData(lobby, "starting", "0");
        }
    }
}
