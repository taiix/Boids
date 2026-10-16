using FishGame;
using Mirror;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ReefRun
{
    public class Player
    {
        public CSteamID steamId;
        public string name;
        public int ping;
        public bool ready;
        public bool isYou;
        public bool isHost;
        public Texture2D avatar;
    }

    /// <summary>
    /// Drives the Reef Run lobby ("pre-dive briefing"). Put this on a GameObject with a UIDocument
    /// pointing at ReefRunLobby.uxml.
    ///
    /// DATA-DRIVEN: the roster is rebuilt from a List&lt;Player&gt;. SteamLobby calls the public API
    /// (AddPlayer / SetReady / RemovePlayer / RefreshLobbyData) as Steam lobby events arrive.
    ///
    /// The host's survival-clock choice is written to the Steam lobby data (<see cref="ClockKey"/>) so
    /// everyone sees it, and handed to <see cref="MatchManager.RoundSecondsOverride"/> for the round.
    /// The host's FRIENDS ONLY / PUBLIC choice goes through <see cref="SteamLobby.SetPublic"/>; a public
    /// lobby shows up in the main menu's lobby browser.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ReefRunLobbyController : MonoBehaviour
    {
        public const int MaxSlots = 4;
        /// <summary>Steam lobby data key holding the survival clock length in seconds.</summary>
        public const string ClockKey = "clock";
        static readonly int[] ClockOptions = { 300, 600, 900 };

        static readonly Color Orange = new(1f, 0.72f, 0.3f);
        static readonly Color Navy = new(0.02f, 0.09f, 0.16f);

        readonly List<Player> _players = new();   // present players, in join order

        // Avatar elements per Steam user (player cards and friend rows are rebuilt separately), so the
        // AvatarImageLoaded callback can fill in pictures that weren't cached when they were built.
        readonly Dictionary<CSteamID, List<VisualElement>> _cardAvatars = new();
        readonly Dictionary<CSteamID, List<VisualElement>> _friendAvatars = new();
        readonly HashSet<CSteamID> _invited = new();
        Callback<AvatarImageLoaded_t> _avatarLoaded;

        VisualElement _root, _roster;
        ScrollView _friends;
        TextField _search;
        Label _eyebrow, _fishCount, _friendsCount, _readyLabel, _startLabel, _startSub, _settingsNote, _searchHint, _typeNote;
        Button _readyBtn, _startBtn, _backBtn, _visFriendsBtn, _visPublicBtn;
        readonly Button[] _clockBtns = new Button[ClockOptions.Length];
        bool _searchFocused;
        int _clockSeconds = 600;
        bool _public;

        UIDocument _doc;
        Texture2D _cardFade, _roleFade;

        // speaking indicators: card + mic glyph per player, refreshed ~10x a second from LobbyVoice
        readonly Dictionary<CSteamID, (VisualElement card, ReefGlyph mic)> _talk = new();
        static readonly Color MicIdle = new(0.56f, 0.69f, 0.73f, 0.55f);
        static readonly Color MicTalking = new(0.17f, 0.76f, 0.77f);

        Player Me => _players.Find(p => p.isYou);
        static CSteamID LobbyId => SteamLobby.instance != null ? SteamLobby.instance.CurrentLobbyId : CSteamID.Nil;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            // Steam is touched only after a frame (BuildUI), so pressing Play in this scene directly still
            // lets Bootstrap's SteamManager come up first.
            StartCoroutine(BuildNextFrame());
        }

        void OnDisable()
        {
            _avatarLoaded?.Dispose();
            _avatarLoaded = null;
        }

        void Update()
        {
            // R toggles ready, like the in-game key tiles suggest - but not while typing a search.
            if (_root != null && !_searchFocused && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                ToggleLocalReady();
        }

        // Steam loads avatars asynchronously: GetLargeFriendAvatar returns -1 the first time and fires
        // this callback once the image is ready.
        void OnAvatarLoaded(AvatarImageLoaded_t e)
        {
            var tex = SteamManager.GetLocalSteamAvatar(e.m_steamID);
            if (tex == null) return;
            foreach (var map in new[] { _cardAvatars, _friendAvatars })
                if (map.TryGetValue(e.m_steamID, out var slots))
                    foreach (var av in slots) av.style.backgroundImage = new StyleBackground(tex);
        }

        // Wait one frame so UIDocument finishes building its tree before we populate it.
        System.Collections.IEnumerator BuildNextFrame()
        {
            yield return null;
            _root = _doc != null ? _doc.rootVisualElement : null;
            if (_root == null) yield break;
            BuildUI();
        }

        void BuildUI()
        {
            if (SteamManager.Initialized)
                _avatarLoaded ??= Callback<AvatarImageLoaded_t>.Create(OnAvatarLoaded);

            // Voice chat while everyone waits (no bodies yet, so the match's proximity voice isn't here).
            if (GetComponent<LobbyVoice>() == null) gameObject.AddComponent<LobbyVoice>();
            _root.schedule.Execute(UpdateTalking).Every(100);

            if (_cardFade == null) _cardFade = Gradient(1, 64, false, 0f, 1f, 0f, 1f);       // transparent -> navy, downwards
            if (_roleFade == null) _roleFade = Gradient(64, 1, true, 0.92f, 0.35f, 0.08f, 0.6f); // navy left -> clear right

            _roster = _root.Q<VisualElement>("roster");
            _friends = _root.Q<ScrollView>("friends-list");
            _search = _root.Q<TextField>("friend-search");
            _searchHint = _root.Q<Label>("search-hint");
            _eyebrow = _root.Q<Label>("lobby-eyebrow");
            _fishCount = _root.Q<Label>("fish-count");
            _friendsCount = _root.Q<Label>("friends-count");
            _readyLabel = _root.Q<Label>("ready-label");
            _startLabel = _root.Q<Label>("start-label");
            _startSub = _root.Q<Label>("start-sub");
            _settingsNote = _root.Q<Label>("settings-note");
            _readyBtn = _root.Q<Button>("ready-btn");
            _startBtn = _root.Q<Button>("start-btn");
            _backBtn = _root.Q<Button>("back-btn");
            _typeNote = _root.Q<Label>("lobby-type-note");
            _visFriendsBtn = _root.Q<Button>("vis-friends");
            _visPublicBtn = _root.Q<Button>("vis-public");
            if (_visFriendsBtn != null) _visFriendsBtn.clicked += () => SetPublic(false);
            if (_visPublicBtn != null) _visPublicBtn.clicked += () => SetPublic(true);

            _root.Q("fish-fade").style.backgroundImage = new StyleBackground(_roleFade);
            _root.Q("shark-fade").style.backgroundImage = new StyleBackground(_roleFade);

            _readyBtn.clicked += ToggleLocalReady;
            _startBtn.clicked += StartMatch;
            _backBtn.clicked += LeaveLobby;
            _root.Q<Label>("overlay-link").RegisterCallback<ClickEvent>(_ => OpenInviteOverlay());

            for (int i = 0; i < ClockOptions.Length; i++)
            {
                int seconds = ClockOptions[i];
                _clockBtns[i] = _root.Q<Button>($"clock-{seconds}");
                _clockBtns[i].clicked += () => SetClock(seconds);
            }

            _search.RegisterValueChangedCallback(_ => { UpdateSearchHint(); PopulateFriends(); });
            _search.RegisterCallback<FocusInEvent>(_ => { _searchFocused = true; UpdateSearchHint(); });
            _search.RegisterCallback<FocusOutEvent>(_ => { _searchFocused = false; UpdateSearchHint(); });

            PopulateSteamLobby();
            RefreshLobbyData();
            Rebuild();
            PopulateFriends();
        }

        void PopulateSteamLobby()
        {
            if (!SteamManager.Initialized || !LobbyId.IsValid()) return;

            int count = SteamMatchmaking.GetNumLobbyMembers(LobbyId);
            for (int i = 0; i < count; i++)
            {
                CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(LobbyId, i);
                _players.Add(new Player
                {
                    steamId = memberId,
                    name = SteamFriends.GetFriendPersonaName(memberId),
                    avatar = SteamManager.GetLocalSteamAvatar(memberId),
                    isYou = memberId == SteamUser.GetSteamID(),
                    isHost = memberId == SteamMatchmaking.GetLobbyOwner(LobbyId),
                    ready = SteamMatchmaking.GetLobbyMemberData(LobbyId, memberId, "ready") == "1",
                });
            }
        }

        // ===================================================================
        //  PUBLIC API - called by SteamLobby
        // ===================================================================
        public void AddPlayer(Player p)
        {
            if (_players.Count >= MaxSlots || _players.Exists(x => x.steamId == p.steamId)) return;
            int index = _players.Count;
            _players.Add(p);

            Rebuild();
            PopulateFriends();   // refresh "In lobby" labels
            FlashCard(index);
        }

        public void SetReady(CSteamID id, bool ready)
        {
            var p = _players.Find(x => x.steamId == id);
            if (p == null || p.ready == ready) return;
            p.ready = ready;
            Rebuild();
        }

        public void RemovePlayer(CSteamID id)
        {
            int i = _players.FindIndex(x => x.steamId == id);
            if (i < 0) return;
            _players.RemoveAt(i);
            Rebuild();
            PopulateFriends();   // they can be invited again
        }

        /// <summary>Lobby-level Steam data changed (e.g. the host picked a different clock).</summary>
        public void RefreshLobbyData()
        {
            if (SteamManager.Initialized && LobbyId.IsValid())
            {
                if (int.TryParse(SteamMatchmaking.GetLobbyData(LobbyId, ClockKey), out int s) && s > 0)
                    _clockSeconds = s;
                _public = SteamLobby.instance != null && SteamLobby.instance.IsPublic;
            }
            if (_root != null) { UpdateClockButtons(); UpdateVisibility(); }
        }

        // ===================================================================
        //  RENDER
        // ===================================================================
        void Rebuild()
        {
            if (_roster == null) return;
            _roster.Clear();
            _cardAvatars.Clear();
            _talk.Clear();
            for (int i = 0; i < MaxSlots; i++)
                _roster.Add(i < _players.Count ? BuildCard(_players[i], i) : BuildOpenSlot());

            int n = _players.Count;
            int ready = _players.FindAll(p => p.ready).Count;
            bool host = NetworkServer.active;

            _fishCount.text = n >= 2 ? $"{n - 1} of you" : "Everyone, until a second diver joins";

            bool meReady = Me?.ready ?? false;
            _readyLabel.text = meReady ? "CANCEL READY" : "READY UP";
            _readyBtn.EnableInClassList("btn-teal", !meReady);
            _readyBtn.EnableInClassList("btn-ghost", meReady);

            bool canStart = host && n >= 1 && ready == n;
            _startBtn.SetEnabled(canStart);
            _startBtn.EnableInClassList("btn-orange", canStart);
            _startBtn.EnableInClassList("btn-off", !canStart);
            _startLabel.text = host ? "START DIVE" : "WAITING FOR HOST";
            _startSub.text = $"{ready} of {n} ready";

            _settingsNote.text = host ? "You're the host" : "Host only";
            UpdateClockButtons();
            UpdateVisibility();
        }

        VisualElement BuildCard(Player p, int slot)
        {
            var card = Box("pc");
            if (p.isYou) card.AddToClassList("pc--you");

            var pic = Box("pc-pic");
            if (slot % 2 == 1) pic.AddToClassList("pc-pic--flip");   // face the fish inwards
            var fade = Box("pc-fade");
            fade.style.backgroundImage = new StyleBackground(_cardFade);
            pic.Add(fade);
            card.Add(pic);
            card.Add(Lbl((slot + 1).ToString(), "key", "key-sm", "pc-slot"));

            var info = Box("pc-info");
            var av = Box("pc-avatar");
            SetAvatar(_cardAvatars, av, p.steamId, p.avatar);
            info.Add(av);

            var who = Box("pc-who");
            who.Add(Lbl(p.name, "pc-name", "f-xb"));
            var tags = Box("tags");
            if (p.isYou) tags.Add(Lbl("YOU", "tag", "tag--you"));
            if (p.isHost) tags.Add(Lbl("HOST", "tag", "tag--host"));
            if (!p.isYou && !p.isHost)
                tags.Add(Lbl(IsFriend(p.steamId) ? "FRIEND" : "DIVER", "tag"));   // public lobbies bring strangers
            who.Add(tags);
            info.Add(who);

            var mic = new ReefGlyph(ReefGlyph.Kind.Mic, MicIdle, 22);
            mic.AddToClassList("pc-mic");
            mic.tooltip = "Lights up while they talk";
            info.Add(mic);
            _talk[p.steamId] = (card, mic);

            var chip = Box("chip", p.ready ? "chip--on" : "chip--off");
            if (p.ready) chip.Add(new ReefGlyph(ReefGlyph.Kind.Check, Orange, 16));
            chip.Add(Lbl(p.ready ? "READY" : "NOT READY", "chip-label"));
            info.Add(chip);

            card.Add(info);
            return card;
        }

        VisualElement BuildOpenSlot()
        {
            var open = Box("open");
            var plus = Box("key", "key-lg");
            plus.Add(new ReefGlyph(ReefGlyph.Kind.Plus, Orange, 24));
            open.Add(plus);
            open.Add(Lbl("Open slot", "open-t1"));
            open.Add(Lbl("Invite a friend →", "open-t2"));
            open.AddManipulator(new Clickable(OpenInviteOverlay));
            return open;
        }

        void UpdateTalking()
        {
            foreach (var kv in _talk)
            {
                bool talking = LobbyVoice.IsTalking(kv.Key);
                kv.Value.card.EnableInClassList("pc--talking", talking);
                kv.Value.mic.color = talking ? MicTalking : MicIdle;
            }
        }

        void UpdateClockButtons()
        {
            bool host = NetworkServer.active;
            for (int i = 0; i < ClockOptions.Length; i++)
            {
                if (_clockBtns[i] == null) continue;
                _clockBtns[i].EnableInClassList("seg-btn--on", ClockOptions[i] == _clockSeconds);
                _clockBtns[i].SetEnabled(host);
            }
            if (host) MatchManager.RoundSecondsOverride = _clockSeconds;
        }

        void UpdateVisibility()
        {
            bool host = NetworkServer.active;
            if (_visFriendsBtn != null) { _visFriendsBtn.EnableInClassList("seg-btn--on", !_public); _visFriendsBtn.SetEnabled(host); }
            if (_visPublicBtn != null) { _visPublicBtn.EnableInClassList("seg-btn--on", _public); _visPublicBtn.SetEnabled(host); }
            if (_typeNote != null) _typeNote.text = _public ? "Public lobby · listed under JOIN" : "Friends-only lobby";
            if (_eyebrow != null)
                _eyebrow.text = $"{(_public ? "PUBLIC" : "FRIENDS")} LOBBY · {_players.Count} / {MaxSlots} DIVERS";
        }

        static bool IsFriend(CSteamID id) =>
            SteamManager.Initialized && SteamFriends.HasFriend(id, EFriendFlags.k_EFriendFlagImmediate);

        void UpdateSearchHint() =>
            _searchHint.style.display = _searchFocused || !string.IsNullOrEmpty(_search.value) ? DisplayStyle.None : DisplayStyle.Flex;

        // ===================================================================
        //  FRIENDS
        // ===================================================================
        enum FriendState { InLobby, InReefRun, InGame, Online, Away, Offline }

        // Every Steam friend, filtered by the search box: online first, then by name. Each row has an
        // invite tile that sends a direct lobby invite (InviteUserToLobby) - no overlay needed.
        void PopulateFriends()
        {
            if (_friends == null) return;
            _friends.Clear();
            _friendAvatars.Clear();
            if (!SteamManager.Initialized) { _friendsCount.text = "Steam offline"; return; }

            string filter = _search?.value?.Trim() ?? "";
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            var friends = new List<(CSteamID id, string name, FriendState state)>(count);
            int online = 0;
            for (int i = 0; i < count; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                var state = StateOf(id);
                if (state != FriendState.Offline) online++;
                string name = SteamFriends.GetFriendPersonaName(id);
                if (filter.Length > 0 && name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                friends.Add((id, name, state));
            }
            _friendsCount.text = $"● {online} online";

            friends.Sort((a, b) =>
            {
                int oa = a.state == FriendState.Offline ? 1 : 0, ob = b.state == FriendState.Offline ? 1 : 0;
                return oa != ob ? oa - ob : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });

            int onlineShown = friends.FindAll(f => f.state != FriendState.Offline).Count;
            if (onlineShown > 0) _friends.Add(Lbl($"ONLINE — {onlineShown}", "sec"));
            bool offlineHeader = false;
            foreach (var f in friends)
            {
                if (f.state == FriendState.Offline && !offlineHeader)
                {
                    _friends.Add(Lbl($"OFFLINE — {friends.Count - onlineShown}", "sec"));
                    offlineHeader = true;
                }
                _friends.Add(BuildFriendRow(f.id, f.name, f.state));
            }

            if (friends.Count == 0)
                _friends.Add(Lbl(filter.Length > 0 ? "No friends match that search." : "No Steam friends found.", "friends-empty"));
        }

        FriendState StateOf(CSteamID id)
        {
            if (_players.Exists(p => p.steamId == id)) return FriendState.InLobby;
            var persona = SteamFriends.GetFriendPersonaState(id);
            if (persona == EPersonaState.k_EPersonaStateOffline) return FriendState.Offline;
            if (SteamFriends.GetFriendGamePlayed(id, out FriendGameInfo_t game) && game.m_gameID.IsValid())
                return game.m_gameID.AppID() == SteamUtils.GetAppID() ? FriendState.InReefRun : FriendState.InGame;
            if (persona == EPersonaState.k_EPersonaStateAway || persona == EPersonaState.k_EPersonaStateSnooze)
                return FriendState.Away;
            return FriendState.Online;
        }

        VisualElement BuildFriendRow(CSteamID id, string name, FriendState state)
        {
            var row = Box("f");
            if (state == FriendState.Offline) row.AddToClassList("f--off");

            var av = Box("fav");
            SetAvatar(_friendAvatars, av, id, SteamManager.GetLocalSteamAvatar(id));
            if (state != FriendState.Offline)
            {
                var dot = Box("fav-dot");
                dot.style.backgroundColor = state switch
                {
                    FriendState.InReefRun or FriendState.InGame => new Color(0.5f, 0.7f, 1f),
                    FriendState.Away => new Color(0.9f, 0.7f, 0.2f),
                    _ => new Color(0.17f, 0.76f, 0.77f),
                };
                av.Add(dot);
            }
            row.Add(av);

            var who = Box("fw");
            who.Add(Lbl(name, "fn"));
            var (status, cls) = state switch
            {
                FriendState.InLobby => ("In your lobby", "fs--on"),
                FriendState.InReefRun => ("In Reef Run", "fs--game"),
                FriendState.InGame => ("In another game", "fs--game"),
                FriendState.Away => ("Away", "fs--away"),
                FriendState.Online => ("Online", "fs--on"),
                _ => ("Offline", null),
            };
            who.Add(Lbl(status, "fs", cls));
            row.Add(who);

            if (state == FriendState.InLobby)
                row.Add(Lbl("IN LOBBY", "in-lobby"));
            else if (state != FriendState.Offline)
                row.Add(BuildInviteTile(id));

            return row;
        }

        VisualElement BuildInviteTile(CSteamID id)
        {
            var tile = new Button();
            tile.AddToClassList("inv");
            tile.tooltip = "Invite to lobby";
            bool sent = _invited.Contains(id);
            tile.Add(new ReefGlyph(sent ? ReefGlyph.Kind.Check : ReefGlyph.Kind.Plus, Orange, sent ? 18 : 20));
            if (sent)
            {
                tile.AddToClassList("inv--done");
                tile.SetEnabled(false);
                return tile;
            }
            tile.clicked += () =>
            {
                if (!LobbyId.IsValid()) { Debug.LogWarning("[ReefRun] Invite ignored: no valid lobby yet."); return; }
                if (!SteamMatchmaking.InviteUserToLobby(LobbyId, id)) { Debug.LogWarning($"[ReefRun] InviteUserToLobby failed for {id}."); return; }
                _invited.Add(id);
                PopulateFriends();
            };
            return tile;
        }

        static void SetAvatar(Dictionary<CSteamID, List<VisualElement>> map, VisualElement el, CSteamID id, Texture2D tex)
        {
            if (tex != null) el.style.backgroundImage = new StyleBackground(tex);
            if (!map.TryGetValue(id, out var list)) map[id] = list = new List<VisualElement>();
            list.Add(el);
        }

        // ===================================================================
        //  ACTIONS
        // ===================================================================
        void ToggleLocalReady()
        {
            var me = Me;
            if (me == null || SteamLobby.instance == null) return;
            bool newReady = !me.ready;
            // broadcast to all lobby members via Steam member data
            SteamMatchmaking.SetLobbyMemberData(LobbyId, "ready", newReady ? "1" : "0");
            SetReady(me.steamId, newReady); // update locally immediately
        }

        void SetClock(int seconds)
        {
            if (!NetworkServer.active) return;   // host only
            _clockSeconds = seconds;
            if (SteamManager.Initialized && LobbyId.IsValid())
                SteamMatchmaking.SetLobbyData(LobbyId, ClockKey, seconds.ToString());
            UpdateClockButtons();
        }

        void SetPublic(bool isPublic)
        {
            if (!NetworkServer.active || SteamLobby.instance == null) return;   // host only
            _public = isPublic;
            SteamLobby.instance.SetPublic(isPublic);
            UpdateVisibility();
        }

        void OpenInviteOverlay()
        {
            if (SteamManager.Initialized && LobbyId.IsValid())
                SteamFriends.ActivateGameOverlayInviteDialog(LobbyId);
        }

        // Back button: tear down the lobby (leave Steam lobby + stop host/client) and return to the menu.
        void LeaveLobby()
        {
            SteamLobby.instance?.LeaveLobby();
            UnityEngine.SceneManagement.SceneManager.LoadScene("01 - MainMenu");
        }

        void StartMatch()
        {
            // Host only, and only once everyone is ready (button is otherwise disabled).
            if (!NetworkServer.active || !_startBtn.enabledSelf) return;

            MatchManager.RoundSecondsOverride = _clockSeconds;

            // Tell every client to play the launch overlay (lobby-level data).
            SteamMatchmaking.SetLobbyData(LobbyId, SteamLobby.StartingKey, "1");
            LaunchOverlay.Instance?.Play();

            // After the animation, pull everyone into the game scene over Mirror. The overlay persists
            // the load and fades out once Island is active. The scene comes from the NetworkManager's
            // Game Scene Name, so it's set in one place.
            _root.schedule.Execute(() =>
            {
                var nm = CustomNetworkManager.singleton as CustomNetworkManager;
                CustomNetworkManager.singleton.ServerChangeScene(nm != null ? nm.GameSceneName : "03 - Island");
            }).StartingIn(6200);
        }

        void FlashCard(int index)
        {
            if (index < 0 || index >= _roster.childCount) return;
            var card = _roster[index];
            card.AddToClassList("joined");
            card.schedule.Execute(() => card.RemoveFromClassList("joined")).StartingIn(700);
        }

        // ===================================================================
        //  HELPERS
        // ===================================================================
        static VisualElement Box(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        static Label Lbl(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            return l;
        }

        // Navy alpha ramp baked into a tiny texture (UI Toolkit has no CSS gradients).
        // horizontal: alpha a0 at the left -> a1 at the middle -> a2 at the right, with 'mid' at that x.
        // vertical:   alpha a0 at the top -> a1 at the bottom ('a2'/'mid' unused).
        static Texture2D Gradient(int w, int h, bool horizontal, float a0, float a1, float a2, float mid)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a;
                    if (horizontal)
                    {
                        float t = x / (float)(w - 1);
                        a = t < mid ? Mathf.Lerp(a0, a1, t / mid) : Mathf.Lerp(a1, a2, (t - mid) / (1f - mid));
                    }
                    else
                    {
                        float t = 1f - y / (float)(h - 1);   // texture row 0 is the bottom
                        a = Mathf.SmoothStep(a0, a1, t);
                    }
                    px[y * w + x] = new Color(Navy.r, Navy.g, Navy.b, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
