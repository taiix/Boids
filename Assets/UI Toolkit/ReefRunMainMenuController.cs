using Steamworks;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// Drives the Reef Run main menu (ReefRunMainMenu.uxml). The background is the live 3D menu scene
    /// (see <see cref="MenuSharkCruise"/>), so this only owns the overlay: logo, nav, the "friends on the
    /// reef" chips (with JOIN for friends sitting in a Reef Run lobby) and the player card.
    /// JOIN opens the lobby browser (<see cref="ReefRunLobbyBrowser"/>): public lobbies plus friends' ones.
    /// SETTINGS opens the shared settings overlay (ReefRunSettings.uxml, see <see cref="ReefRunSettingsPanel"/>)
    /// - the same one the in-game pause menu opens.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ReefRunMainMenuController : MonoBehaviour
    {
        // ---- inspector ----
        public string Version = "v0.4.1 · EARLY ACCESS";
        public string LobbySceneName = "02 - ReefRunLobby";
        [Tooltip("How many friends to show as chips in the bottom-left corner.")]
        public int MaxFriendChips = 3;
        [Tooltip("Seconds between refreshes of the friends chips.")]
        public float FriendsRefreshSeconds = 5f;

        static readonly Color Navy = new(0.02f, 0.09f, 0.16f);

        UIDocument _doc;
        VisualElement _root, _friends, _playerImage;
        Label _hint;
        Texture2D _shadeTop, _shadeBottom;
        ReefRunSettingsPanel _settings;
        ReefRunCreditsPanel _credits;
        ReefRunLobbyBrowser _browser;
        Callback<AvatarImageLoaded_t> _avatarLoaded;
        Callback<PersonaStateChange_t> _personaChanged;
        IVisualElementScheduledItem _refresh;
        VisualElement _veil;
        readonly Dictionary<CSteamID, VisualElement> _chipAvatars = new();

        const string HostHint = "Open a lobby for up to 4 divers";

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            // Nothing Steam here: the menu is the first scene and Bootstrap (which owns the real
            // SteamManager + SteamLobby) only loads a frame later. Touching Steam now would make a
            // stand-alone SteamManager ahead of it. BuildUI runs after that frame.
            StartCoroutine(BuildNextFrame());
        }

        void OnDisable()
        {
            _avatarLoaded?.Dispose(); _avatarLoaded = null;
            _personaChanged?.Dispose(); _personaChanged = null;
            _refresh?.Pause();
            _browser?.Dispose(); _browser = null;
            SteamLobby.JoinFailed -= OnJoinFailed;
        }

        IEnumerator BuildNextFrame()
        {
            yield return null;
            _root = _doc != null ? _doc.rootVisualElement : null;
            if (_root == null) yield break;
            BuildUI();
        }

        void BuildUI()
        {
            if (SteamManager.Initialized)
            {
                _avatarLoaded ??= Callback<AvatarImageLoaded_t>.Create(OnAvatarLoaded);
                _personaChanged ??= Callback<PersonaStateChange_t>.Create(_ => RefreshFriends());
            }

            // UI Toolkit has no gradients: bake the two shades that keep the logo and chips readable
            // over the live scene (dark at the top fading out, and a softer one along the bottom).
            if (_shadeTop == null) _shadeTop = VerticalShade(0.78f, 0f);
            if (_shadeBottom == null) _shadeBottom = VerticalShade(0f, 0.6f);
            _root.Q("shade-top").style.backgroundImage = new StyleBackground(_shadeTop);
            _root.Q("shade-bottom").style.backgroundImage = new StyleBackground(_shadeBottom);

            // Fade the live shark view in once the first frames are out.
            _veil = _root.Q("veil");
            _veil.schedule.Execute(RevealReef).StartingIn(150);

            _friends = _root.Q("friends");
            _hint = _root.Q<Label>("nav-hint");
            _playerImage = _root.Q("player-image");
            SetText("version-label", Version);

            if (SteamManager.Initialized)
            {
                var me = SteamUser.GetSteamID();
                SetText("player-name", SteamFriends.GetPersonaName());
                var tex = SteamManager.GetLocalSteamAvatar(me);
                if (tex != null) _playerImage.style.backgroundImage = new StyleBackground(tex);
                _chipAvatars[me] = _playerImage;
            }

            // settings overlay (also restores the saved master volume)
            _settings = new ReefRunSettingsPanel(_root);

            Wire("play-btn", OnHost, HostHint);
            _browser = new ReefRunLobbyBrowser(_root);
            SteamLobby.JoinFailed += OnJoinFailed;
            Wire("join-btn", OnJoin, "Browse public lobbies and your friends' lobbies");
            Wire("settings-btn", OpenSettings, "Audio and display");
            _credits = new ReefRunCreditsPanel(_root);
            Wire("credits-btn", () => _credits.Open(), "Who made the music and sounds");
            Wire("quit-btn", OnQuit, "Back to the surface");

            RefreshFriends();
            _refresh = _root.schedule.Execute(RefreshFriends).Every((long)(FriendsRefreshSeconds * 1000f));
        }

        // ===================================================================
        //  NAV ACTIONS
        // ===================================================================
        protected virtual void OnHost()
        {
            if (SteamLobby.instance != null) SteamLobby.instance.CreateLobby();
        }

        /// <summary>JOIN opens the lobby browser: every public lobby plus any lobby a friend is in.</summary>
        protected virtual void OnJoin() => _browser?.Open();

        // A friend-chip JOIN that bounced (the browser shows its own failures).
        void OnJoinFailed(string reason)
        {
            if (_browser == null || !_browser.IsOpen) SetHint(reason);
        }

        protected virtual void OnQuit()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        void OpenSettings() => _settings?.Open();

        void RevealReef() => _veil?.AddToClassList("mm-veil--hidden");

        static void JoinFriend(CSteamID lobby)
        {
            if (SteamLobby.instance != null) SteamLobby.instance.JoinLobby(lobby);
        }

        // ===================================================================
        //  FRIENDS ON THE REEF
        // ===================================================================
        struct Friend { public CSteamID id; public string name; public bool inReefRun; public CSteamID lobby; }

        // Online friends, best first: in a Reef Run lobby (joinable), then playing Reef Run, then the rest.
        static List<Friend> OnlineFriends()
        {
            var list = new List<Friend>();
            if (!SteamManager.Initialized) return list;
            var app = SteamUtils.GetAppID();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (SteamFriends.GetFriendPersonaState(id) == EPersonaState.k_EPersonaStateOffline) continue;
                var f = new Friend { id = id, name = SteamFriends.GetFriendPersonaName(id) };
                if (SteamFriends.GetFriendGamePlayed(id, out FriendGameInfo_t game) && game.m_gameID.IsValid()
                    && game.m_gameID.AppID() == app)
                {
                    f.inReefRun = true;
                    f.lobby = game.m_steamIDLobby;
                }
                list.Add(f);
            }
            list.Sort((a, b) =>
            {
                int ra = a.lobby.IsValid() ? 0 : a.inReefRun ? 1 : 2;
                int rb = b.lobby.IsValid() ? 0 : b.inReefRun ? 1 : 2;
                return ra != rb ? ra - rb : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        void RefreshFriends()
        {
            var friends = OnlineFriends();
            SetText("player-sub", SteamManager.Initialized
                ? $"● {friends.Count} friend{(friends.Count == 1 ? "" : "s")} online"
                : "Steam offline");

            _chipAvatars.Clear();
            if (SteamManager.Initialized) _chipAvatars[SteamUser.GetSteamID()] = _playerImage;
            if (_friends == null) return;   // the friend chips were removed from the layout
            _friends.Clear();
            for (int i = 0; i < friends.Count && i < MaxFriendChips; i++)
                _friends.Add(BuildChip(friends[i]));
        }

        VisualElement BuildChip(Friend f)
        {
            var chip = new VisualElement();
            chip.AddToClassList("mm-chip");

            var av = new VisualElement();
            av.AddToClassList("mm-chip-av");
            var tex = SteamManager.GetLocalSteamAvatar(f.id);
            if (tex != null) av.style.backgroundImage = new StyleBackground(tex);
            _chipAvatars[f.id] = av;
            chip.Add(av);

            var text = new VisualElement();
            var name = new Label(f.name); name.AddToClassList("mm-chip-name");
            var status = new Label(f.lobby.IsValid() ? "In a Reef Run lobby" : f.inReefRun ? "Playing Reef Run" : "Online");
            status.AddToClassList("mm-chip-status");
            if (f.inReefRun && !f.lobby.IsValid()) status.AddToClassList("mm-chip-status--game");
            text.Add(name);
            text.Add(status);
            chip.Add(text);

            if (f.lobby.IsValid())
            {
                var lobby = f.lobby;
                var join = new Button(() => JoinFriend(lobby)) { text = "JOIN" };
                join.AddToClassList("mm-chip-join");
                chip.Add(join);
            }
            return chip;
        }

        void OnAvatarLoaded(AvatarImageLoaded_t e)
        {
            if (!_chipAvatars.TryGetValue(e.m_steamID, out var av) || av == null) return;
            var tex = SteamManager.GetLocalSteamAvatar(e.m_steamID);
            if (tex != null) av.style.backgroundImage = new StyleBackground(tex);
        }

        // ===================================================================
        //  HELPERS
        // ===================================================================
        void Wire(string name, System.Action cb, string hint)
        {
            var btn = _root.Q<Button>(name);
            if (btn == null) return;
            btn.clicked += cb;
            btn.RegisterCallback<PointerEnterEvent>(_ => SetHint(hint));
            btn.RegisterCallback<FocusInEvent>(_ => SetHint(hint));
            btn.RegisterCallback<PointerLeaveEvent>(_ => SetHint(HostHint));
        }

        void SetHint(string text) { if (_hint != null) _hint.text = text; }

        void SetText(string name, string text)
        {
            var lbl = _root.Q<Label>(name);
            if (lbl != null) lbl.text = text;
        }

        // Navy alpha ramp: aTop at the top edge -> aBottom at the bottom edge (smoothstepped).
        static Texture2D VerticalShade(float aTop, float aBottom, int h = 64)
        {
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
            {
                float t = 1f - y / (float)(h - 1);   // texture row 0 is the bottom
                tex.SetPixel(0, y, new Color(Navy.r * 0.6f, Navy.g * 0.6f, Navy.b * 0.6f, Mathf.SmoothStep(aTop, aBottom, t)));
            }
            tex.Apply();
            return tex;
        }
    }
}
