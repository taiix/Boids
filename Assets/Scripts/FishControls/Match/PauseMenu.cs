using Mirror;
using ReefRun;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Cursor = UnityEngine.Cursor;

namespace FishGame
{
    /// <summary>
    /// In-game menu on Escape: Resume, Settings, Back to Lobby, Main Menu, Quit.
    ///
    /// It's multiplayer, so the game keeps running: opening the menu only frees the cursor and takes
    /// your controls away (FishPlayer, the orbit camera and the reef task all check <see cref="IsOpen"/>).
    /// Settings opens the same settings screen as the main menu (ReefRunSettings.uxml on a UIDocument).
    /// Back to Lobby is the host's call - it takes everyone back, as the lobby is where the host starts
    /// the next round. Anyone can leave to the main menu or quit.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        /// <summary>True while the local player has the menu (or its settings screen) open.</summary>
        public static bool IsOpen { get; private set; }

        [Header("Menu")]
        [Tooltip("Everything shown while the menu is open (backdrop + panel). Starts hidden.")]
        [SerializeField] GameObject menuRoot;
        [SerializeField] Button resumeButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button lobbyButton;
        [SerializeField] Button mainMenuButton;
        [SerializeField] Button quitButton;
        [Tooltip("Shown under Back to Lobby when this player can't use it (not the host / offline).")]
        [SerializeField] Text lobbyHint;

        [Header("Settings")]
        [Tooltip("UIDocument showing ReefRunSettings.uxml. Kept inactive until Settings is pressed.")]
        [SerializeField] UIDocument settingsDocument;

        [Header("Scenes")]
        [SerializeField] string mainMenuScene = "01 - MainMenu";
        [Tooltip("Used when there's no SteamLobby to ask (it normally knows the lobby scene).")]
        [SerializeField] string fallbackLobbyScene = "02 - ReefRunLobby";

        InputAction _toggle;
        ReefRunSettingsPanel _settings;
        CursorLockMode _cursorBefore;
        bool _cursorVisibleBefore;

        void Awake()
        {
            IsOpen = false;
            _toggle = new InputAction("Pause", InputActionType.Button);
            _toggle.AddBinding("<Keyboard>/escape");
            _toggle.AddBinding("<Gamepad>/start");

            if (resumeButton != null) resumeButton.onClick.AddListener(() => SetOpen(false));
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (lobbyButton != null) lobbyButton.onClick.AddListener(BackToLobby);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ToMainMenu);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);

            if (menuRoot != null) menuRoot.SetActive(false);
            if (settingsDocument != null) settingsDocument.gameObject.SetActive(false);

            // The buttons need an EventSystem; add one if this scene doesn't have it.
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                               typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        }

        void OnEnable() => _toggle.Enable();
        void OnDisable() => _toggle.Disable();

        // The game scene is going away (back to the lobby / menu, or the host took everyone back):
        // the menus there need a free cursor, and the lobby needs this round's flags cleared.
        void OnDestroy()
        {
            IsOpen = false;
            _toggle?.Dispose();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ResetLobbyFlags();
        }

        void Update()
        {
            if (_toggle.WasPressedThisFrame())
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
                if (_settings != null && _settings.IsOpen) _settings.Close();
            }
            if (menuRoot != null) menuRoot.SetActive(open);
        }

        void RefreshLobbyButton()
        {
            bool host = NetworkServer.active;
            if (lobbyButton != null)
            {
                lobbyButton.interactable = host;
                var label = lobbyButton.GetComponentInChildren<Text>();
                if (label != null) label.color = new Color(1f, 1f, 1f, host ? 1f : 0.35f);
            }
            if (lobbyHint != null)
            {
                lobbyHint.gameObject.SetActive(!host);
                lobbyHint.text = NetworkClient.active
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
            _settings.Closed += OnSettingsClosed;
            if (menuRoot != null) menuRoot.SetActive(false);
            _settings.Open();
        }

        void OnSettingsClosed()
        {
            _settings = null;
            if (settingsDocument != null) settingsDocument.gameObject.SetActive(false);
            if (menuRoot != null) menuRoot.SetActive(IsOpen);
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
