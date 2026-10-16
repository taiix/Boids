using Steamworks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// The main menu's JOIN overlay: every public Reef Run lobby (worldwide) plus any lobby a friend is in,
    /// friends first. Built in code on top of the menu's root in the settings overlay's look, like
    /// <see cref="ReefRunCreditsPanel"/>; ✕ or a click outside the card closes it.
    ///
    /// Public lobbies come from a Steam lobby search filtered on <see cref="SteamLobby.GameTag"/> (AppID 480
    /// is shared with other test projects). Friends' lobbies come from their rich presence and need a
    /// RequestLobbyData round trip before their name/count are readable. Full lobbies and ones whose dive
    /// already started are hidden - there's no mid-match join.
    /// </summary>
    public class ReefRunLobbyBrowser : IDisposable
    {
        const float AutoRefreshSeconds = 8f;

        struct Entry { public CSteamID lobby; public bool friend; }

        readonly VisualElement _overlay;
        readonly ScrollView _list;
        readonly Label _status;
        readonly Button _refreshBtn;
        readonly IVisualElementScheduledItem _autoRefresh;

        readonly List<Entry> _entries = new();
        readonly Dictionary<CSteamID, VisualElement> _avatars = new();
        CallResult<LobbyMatchList_t> _listResult;
        Callback<LobbyDataUpdate_t> _dataUpdate;
        Callback<AvatarImageLoaded_t> _avatarLoaded;
        Callback<PersonaStateChange_t> _personaChanged;
        bool _searching, _joining;

        public bool IsOpen => _overlay.style.display == DisplayStyle.Flex;

        public ReefRunLobbyBrowser(VisualElement root)
        {
            _overlay = new VisualElement();
            _overlay.AddToClassList("mm-settings-overlay");
            _overlay.style.display = DisplayStyle.None;
            _overlay.RegisterCallback<ClickEvent>(e => { if (e.target == _overlay) Close(); });

            var card = new VisualElement();
            card.AddToClassList("mm-settings-card");
            card.AddToClassList("mm-glass");
            card.style.width = 820;
            card.style.height = new Length(70, LengthUnit.Percent);
            _overlay.Add(card);

            var head = new VisualElement();
            head.AddToClassList("mm-settings-head");
            var title = new Label("JOIN A DIVE");
            title.AddToClassList("mm-settings-title");
            head.Add(title);
            var headRight = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _refreshBtn = new Button(Refresh) { name = "browser-refresh", text = "REFRESH" };
            _refreshBtn.AddToClassList("mm-close-btn");
            _refreshBtn.style.marginRight = 8;
            var close = new Button(Close) { name = "browser-close", text = "✕" };
            close.AddToClassList("mm-close-btn");
            headRight.Add(_refreshBtn);
            headRight.Add(close);
            head.Add(headRight);
            card.Add(head);

            _status = new Label();
            _status.AddToClassList("mm-lb-status");
            card.Add(_status);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.flexGrow = 1;
            card.Add(_list);

            var foot = new Label("Public lobbies from every region, plus any lobby your friends are in.");
            foot.AddToClassList("mm-lb-foot");
            card.Add(foot);

            root.Add(_overlay);

            // Keep the list fresh while it's open (lobbies fill up and start).
            _autoRefresh = _overlay.schedule.Execute(() => { if (IsOpen && !_joining) Refresh(); })
                                   .Every((long)(AutoRefreshSeconds * 1000f));
            _autoRefresh.Pause();
            SteamLobby.JoinFailed += OnJoinFailed;
        }

        public void Open()
        {
            _joining = false;
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.BringToFront();
            _overlay.schedule.Execute(() => _overlay.AddToClassList("show")).StartingIn(16);
            Refresh();
            _autoRefresh.Resume();
        }

        public void Close()
        {
            _autoRefresh.Pause();
            _overlay.RemoveFromClassList("show");
            _overlay.schedule.Execute(() => _overlay.style.display = DisplayStyle.None).StartingIn(240);
        }

        public void Dispose()
        {
            SteamLobby.JoinFailed -= OnJoinFailed;
            _autoRefresh?.Pause();
            _listResult?.Dispose(); _listResult = null;
            _dataUpdate?.Dispose(); _dataUpdate = null;
            _avatarLoaded?.Dispose(); _avatarLoaded = null;
            _personaChanged?.Dispose(); _personaChanged = null;
        }

        // ===================================================================
        //  SEARCH
        // ===================================================================
        void Refresh()
        {
            if (!SteamManager.Initialized)
            {
                SetStatus("Steam is offline - start Steam to find lobbies.");
                _list.Clear();
                return;
            }

            _listResult ??= CallResult<LobbyMatchList_t>.Create(OnLobbyList);
            _dataUpdate ??= Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            _avatarLoaded ??= Callback<AvatarImageLoaded_t>.Create(OnAvatarLoaded);
            _personaChanged ??= Callback<PersonaStateChange_t>.Create(OnPersonaChanged);

            // Friends' lobbies first: their data arrives through OnLobbyDataUpdate.
            _entries.RemoveAll(e => e.friend);
            var app = SteamUtils.GetAppID();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (!SteamFriends.GetFriendGamePlayed(id, out FriendGameInfo_t game) || !game.m_gameID.IsValid()) continue;
                if (game.m_gameID.AppID() != app || !game.m_steamIDLobby.IsValid()) continue;
                if (_entries.Exists(e => e.lobby == game.m_steamIDLobby)) continue;
                _entries.Add(new Entry { lobby = game.m_steamIDLobby, friend = true });
                SteamMatchmaking.RequestLobbyData(game.m_steamIDLobby);
            }

            // Public lobbies. Filters must be added right before each RequestLobbyList.
            SteamMatchmaking.AddRequestLobbyListStringFilter(SteamLobby.GameKey, SteamLobby.GameTag, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(SteamLobby.PublicKey, "1", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
            _listResult.Set(SteamMatchmaking.RequestLobbyList());
            _searching = true;
            Render();
        }

        void OnLobbyList(LobbyMatchList_t result, bool ioFailure)
        {
            _searching = false;
            _entries.RemoveAll(e => !e.friend);
            if (!ioFailure)
                for (int i = 0; i < result.m_nLobbiesMatching; i++)
                {
                    var lobby = SteamMatchmaking.GetLobbyByIndex(i);
                    if (!_entries.Exists(e => e.lobby == lobby)) _entries.Add(new Entry { lobby = lobby });
                }
            Render();
            if (ioFailure && !_joining) SetStatus("Couldn't reach Steam's lobby search. Try REFRESH.");
        }

        void OnLobbyDataUpdate(LobbyDataUpdate_t e)
        {
            if (!IsOpen) return;
            var lobby = new CSteamID(e.m_ulSteamIDLobby);
            if (e.m_ulSteamIDMember == e.m_ulSteamIDLobby && _entries.Exists(x => x.lobby == lobby)) Render();
        }

        void OnJoinFailed(string reason)
        {
            _joining = false;
            if (!IsOpen) return;
            SetStatus(reason);
            Refresh();
        }

        // ===================================================================
        //  RENDER
        // ===================================================================
        void Render()
        {
            _list.Clear();
            _avatars.Clear();
            if (_joining) return;

            int shown = 0;
            foreach (var e in Sorted())
            {
                if (!IsJoinable(e.lobby)) continue;
                _list.Add(BuildRow(e));
                shown++;
            }

            if (shown > 0) SetStatus($"{shown} open lobb{(shown == 1 ? "y" : "ies")}");
            else if (_searching) SetStatus("Searching the reef…");
            else SetStatus("No open lobbies right now. HOST one and set it to PUBLIC.");
        }

        IEnumerable<Entry> Sorted()
        {
            var list = new List<Entry>(_entries);
            list.Sort((a, b) => a.friend != b.friend ? (a.friend ? -1 : 1)
                : string.Compare(LobbyName(a.lobby), LobbyName(b.lobby), StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // Ours (the tag also proves the data has arrived), not full, and not mid-dive.
        static bool IsJoinable(CSteamID lobby) =>
            SteamMatchmaking.GetLobbyData(lobby, SteamLobby.GameKey) == SteamLobby.GameTag
            && SteamMatchmaking.GetLobbyData(lobby, SteamLobby.StartingKey) != "1"
            && Members(lobby) < Limit(lobby);

        static int Members(CSteamID lobby)
        {
            if (int.TryParse(SteamMatchmaking.GetLobbyData(lobby, SteamLobby.MembersKey), out int n) && n > 0) return n;
            return Mathf.Max(1, SteamMatchmaking.GetNumLobbyMembers(lobby));
        }

        static int Limit(CSteamID lobby)
        {
            int limit = SteamMatchmaking.GetLobbyMemberLimit(lobby);
            return limit > 0 ? limit : ReefRunLobbyController.MaxSlots;
        }

        static string LobbyName(CSteamID lobby)
        {
            string name = SteamMatchmaking.GetLobbyData(lobby, SteamLobby.NameKey);
            return string.IsNullOrEmpty(name) ? "Reef Run lobby" : name;
        }

        static CSteamID Host(CSteamID lobby) =>
            ulong.TryParse(SteamMatchmaking.GetLobbyData(lobby, "HostAddress"), out ulong id) ? new CSteamID(id) : CSteamID.Nil;

        VisualElement BuildRow(Entry e)
        {
            var row = new VisualElement();
            row.AddToClassList("mm-lb-row");

            var av = new VisualElement();
            av.AddToClassList("mm-chip-av");
            var host = Host(e.lobby);
            if (host.IsValid())
            {
                // Strangers' avatars need their persona first; OnPersonaChanged/OnAvatarLoaded fill it in.
                if (!SteamFriends.RequestUserInformation(host, false))
                {
                    var tex = SteamManager.GetLocalSteamAvatar(host);
                    if (tex != null) av.style.backgroundImage = new StyleBackground(tex);
                }
                _avatars[host] = av;
            }
            row.Add(av);

            var text = new VisualElement { style = { flexGrow = 1 } };
            var nameRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var name = new Label(LobbyName(e.lobby));
            name.AddToClassList("mm-chip-name");
            nameRow.Add(name);
            bool friend = e.friend || (host.IsValid() && SteamFriends.HasFriend(host, EFriendFlags.k_EFriendFlagImmediate));
            if (friend)
            {
                var tag = new Label("FRIEND");
                tag.AddToClassList("mm-lb-tag");
                nameRow.Add(tag);
            }
            text.Add(nameRow);

            bool isPublic = SteamMatchmaking.GetLobbyData(e.lobby, SteamLobby.PublicKey) == "1";
            string clock = int.TryParse(SteamMatchmaking.GetLobbyData(e.lobby, ReefRunLobbyController.ClockKey), out int s) && s > 0
                ? $"{s / 60}:{s % 60:00}" : "10:00";
            var sub = new Label($"{(isPublic ? "Public" : "Friends only")} · {clock} clock");
            sub.AddToClassList("mm-chip-status");
            text.Add(sub);
            row.Add(text);

            var divers = new Label($"{Members(e.lobby)} / {Limit(e.lobby)}");
            divers.AddToClassList("mm-lb-count");
            row.Add(divers);

            var lobby = e.lobby;
            var join = new Button(() => Join(lobby)) { text = "JOIN" };
            join.AddToClassList("mm-chip-join");
            row.Add(join);
            return row;
        }

        void Join(CSteamID lobby)
        {
            if (SteamLobby.instance == null || _joining) return;
            _joining = true;
            _autoRefresh.Pause();
            Render();
            SetStatus($"Joining {LobbyName(lobby)}…");
            SteamLobby.instance.JoinLobby(lobby);
        }

        void OnPersonaChanged(PersonaStateChange_t e)
        {
            if (!_avatars.TryGetValue(new CSteamID(e.m_ulSteamID), out var av) || av == null) return;
            var tex = SteamManager.GetLocalSteamAvatar(new CSteamID(e.m_ulSteamID));
            if (tex != null) av.style.backgroundImage = new StyleBackground(tex);
        }

        void OnAvatarLoaded(AvatarImageLoaded_t e)
        {
            if (!_avatars.TryGetValue(e.m_steamID, out var av) || av == null) return;
            var tex = SteamManager.GetLocalSteamAvatar(e.m_steamID);
            if (tex != null) av.style.backgroundImage = new StyleBackground(tex);
        }

        void SetStatus(string text) => _status.text = text;
    }
}
