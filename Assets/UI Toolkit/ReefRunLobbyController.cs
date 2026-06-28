using Mirror;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;
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
    /// Drives the Reef Run lobby. Put this on a GameObject with a UIDocument
    /// pointing at ReefRunLobby.uxml.
    ///
    /// DATA-DRIVEN: the roster is rebuilt from a List&lt;Player&gt;. To make the
    /// view change when a player joins/leaves/readies, call the public API:
    ///     AddPlayer(player);  SetReady(id, true);  RemovePlayer(id);
    /// Each call updates avatars, names, pings, chips, counts and buttons.
    /// Wire these to your Mirror room callbacks later — the demo below just
    /// calls the same methods on a timer so you can see it live.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ReefRunLobbyController : MonoBehaviour
    {
        // ---- player model (one row). Maps onto a Mirror room slot. ----

        const int MaxSlots = 8;

        readonly List<Player> _players = new();   // present players, in join order
        readonly List<Player> _demoQueue = new(); // not-yet-joined (demo only)
        float _start;

        VisualElement _root, _barFill;
        ScrollView _list, _feed;

        Label _lobbyCount, _readyCount;
        Button _readyBtn, _startBtn, _inviteBtn;

        Player Me => _players.Find(p => p.isYou);

        UIDocument _doc;
        Texture2D _bgTex;

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            StartCoroutine(BuildNextFrame());
        }

        // Wait one frame so UIDocument finishes building its tree before we
        // populate it — otherwise the background/ambient/rows get wiped and
        // you see them flash on, then vanish.
        System.Collections.IEnumerator BuildNextFrame()
        {
            yield return null;
            _root = _doc != null ? _doc.rootVisualElement : null;
            if (_root == null) yield break;
            BuildUI();
        }

        void BuildUI()
        {
            if (_bgTex == null) _bgTex = BuildBackground();
            _root.Q<VisualElement>("stage").style.backgroundImage = new StyleBackground(_bgTex);

            // ambient layer (clear first so re-enabling doesn't stack copies)
            var mount = _root.Q<VisualElement>("ambient-mount");
            mount.Clear();
            mount.Add(new AmbientReef { style = { flexGrow = 1 } });

            // grab elements
            _list = _root.Q<ScrollView>("roster-list");
            _feed = _root.Q<ScrollView>("feed");
            _barFill = _root.Q<VisualElement>("bar-fill");
            _lobbyCount = _root.Q<Label>("lobby-count");
            _readyCount = _root.Q<Label>("ready-count");
            _readyBtn = _root.Q<Button>("ready-btn");
            _startBtn = _root.Q<Button>("start-btn");
            _inviteBtn = _root.Q<Button>("invite-btn");

            _readyBtn.clicked += ToggleLocalReady;
            _startBtn.clicked += StartMatch;
            _inviteBtn.clicked += InviteNext;

            _start = Time.realtimeSinceStartup;

            _startBtn.style.display = NetworkServer.active ? DisplayStyle.Flex : DisplayStyle.None;

            PopulateSteamLobby();
            Rebuild();
        }

        void PopulateSteamLobby()
        {
            if (!SteamManager.Initialized || SteamLobby.instance == null) return;
            CSteamID lobbyId = SteamLobby.instance.CurrentLobbyId;

            if (!lobbyId.IsValid()) return;

            int count = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            for (int i = 0; i < count; i++)
            {
                CSteamID memberId = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                string name = SteamFriends.GetFriendPersonaName(memberId);
                _players.Add(new Player
                {
                    steamId = memberId,
                    name = name,
                    avatar = SteamManager.GetLocalSteamAvatar(memberId),
                    isYou = memberId == SteamUser.GetSteamID(),
                    isHost = memberId == SteamMatchmaking.GetLobbyOwner(lobbyId),
                });
            }
        }


        // ===================================================================
        //  PUBLIC API — call these from your networking layer
        // ===================================================================
        public void AddPlayer(Player p)
        {
            if (_players.Count >= MaxSlots || _players.Exists(x => x.steamId == p.steamId)) return;
            int index = _players.Count;
            _players.Add(p);

            Rebuild();
            FlashRow(index);
        }

        public void SetReady(CSteamID id, bool ready)
        {
            var p = _players.Find(x => x.steamId == id);
            if (p == null || p.ready == ready) return;
            p.ready = ready;
            Rebuild();
            string who = p.isYou ? "You" : p.name;
            string verb = p.isYou ? "are" : "is";
        }

        public void RemovePlayer(CSteamID id)
        {
            int i = _players.FindIndex(x => x.steamId == id);
            if (i < 0) return;
            var p = _players[i];
            _players.RemoveAt(i);
            Rebuild();
            Log($"<b>{p.name}</b> left the lobby", Hex("88B4BD"));
        }

        // ===================================================================
        //  RENDER
        // ===================================================================
        void Rebuild()
        {
            _list.Clear();
            for (int i = 0; i < MaxSlots; i++)
                _list.Add(i < _players.Count ? BuildRow(_players[i]) : BuildOpenRow());

          

            int ready = _players.FindAll(p => p.ready).Count;
            int n = _players.Count;
            _lobbyCount.text = n.ToString();
            _readyCount.text = $"{ready}/{n}";
            _barFill.style.width = Length.Percent(n == 0 ? 0 : 100f * ready / n);

            bool meReady = Me?.ready ?? false;
            _readyBtn.text = meReady ? "Cancel" : "Ready up";
            _readyBtn.EnableInClassList("btn-aqua", !meReady);
            _readyBtn.EnableInClassList("btn-ghost", meReady);

            bool full = _demoQueue.Count == 0 && n >= MaxSlots;
            _inviteBtn.SetEnabled(!full);
            _inviteBtn.text = full ? "Lobby full" : "Invite a friend";

            _startBtn.SetEnabled(n >= 1 && ready == n);
        }

        VisualElement BuildRow(Player p)
        {
            var row = new VisualElement();
            row.AddToClassList("row");

            var av = new VisualElement();
            av.AddToClassList("avatar");
            if (p.avatar != null) av.style.backgroundImage = new StyleBackground(p.avatar);
            row.Add(av);

            var who = new VisualElement(); who.AddToClassList("who");
            var line = new VisualElement(); line.AddToClassList("name-line");
            line.Add(Lbl(p.name, "name"));
            if (p.isYou) line.Add(Tag("YOU", "tag-you"));
            if (p.isHost) line.Add(Tag("HOST", null));
            who.Add(line);
            row.Add(who);

            var rs = new VisualElement(); rs.AddToClassList("ready-state");
            var box = new VisualElement(); box.AddToClassList("checkbox");
            if (p.ready) box.AddToClassList("on");
            rs.Add(box);
            var rl = Lbl(p.ready ? "Ready" : "Not ready", "rlabel");
            if (p.ready) rl.AddToClassList("ready");
            rs.Add(rl);
            row.Add(rs);

            return row;
        }

        VisualElement BuildOpenRow()
        {
            var row = new VisualElement();
            row.AddToClassList("row-open");
            var ghost = new VisualElement(); ghost.AddToClassList("ghost-av");
            row.Add(ghost);
            row.Add(Lbl("Open slot — invite a friend", null));
            row.RegisterCallback<ClickEvent>(_ => InviteNext());
            return row;
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
            SteamMatchmaking.SetLobbyMemberData(SteamLobby.instance.CurrentLobbyId, "ready", newReady ? "1" : "0");
            SetReady(me.steamId, newReady); // update locally immediately
        }

        void InviteNext()
        {
            SteamFriends.ActivateGameOverlayInviteDialog(SteamLobby.instance.CurrentLobbyId);
        }

        void StartMatch()
        {
            // Host only, and only once everyone is ready (button is otherwise disabled/hidden).
            if (!NetworkServer.active || !_startBtn.enabledSelf) return;

            // Tell every client to play the launch overlay (lobby-level data).
            SteamMatchmaking.SetLobbyData(SteamLobby.instance.CurrentLobbyId, "starting", "1");
            LaunchOverlay.Instance?.Play();

            // After the animation, pull everyone into the game scene over Mirror.
            // The overlay persists the load and fades out once Island is active.
            _root.schedule.Execute(() => CustomNetworkManager.singleton.ServerChangeScene("Island")).StartingIn(6200);
        }

        void FlashRow(int index)
        {
            if (index < 0 || index >= _list.childCount) return;
            var row = _list[index];
            row.AddToClassList("joined");
            row.schedule.Execute(() => row.RemoveFromClassList("joined")).StartingIn(200);
        }

        void Log(string text, Color color)
        {
            if (_feed == null) return;
            var line = new VisualElement(); line.AddToClassList("feed-line");
            var dot = new VisualElement(); dot.AddToClassList("feed-dot");
            dot.style.backgroundColor = color;
            line.Add(dot);
            var t = Lbl(text, "feed-text"); t.enableRichText = true; line.Add(t);
            line.Add(Lbl(Stamp(), "feed-time"));
            _feed.Add(line);
            _feed.schedule.Execute(() => _feed.ScrollTo(line)).StartingIn(50);
        }

        string Stamp()
        {
            int s = Mathf.FloorToInt(Time.realtimeSinceStartup - _start);
            return $"{s / 60:00}:{s % 60:00}";
        }

        static Label Lbl(string text, string cls)
        {
            var l = new Label(text);
            if (!string.IsNullOrEmpty(cls)) l.AddToClassList(cls);
            return l;
        }
        static Label Tag(string text, string variant)
        {
            var l = Lbl(text, "tag");
            if (!string.IsNullOrEmpty(variant)) l.AddToClassList(variant);
            return l;
        }
        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        // baked vertical gradient (navy->teal->seabed) with a bottom-centre glow
        static Texture2D BuildBackground(int w = 160, int h = 90)
        {
            var navy = Hex("06283F");
            var teal = Hex("0A4A56");
            var seabed = Hex("0E7E7A");
            var glow = Hex("2FC9BD");

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            float cx = w * 0.5f, cy = 0f;                 // glow at bottom centre (data y=0)
            float gr = h * 1.1f;

            for (int y = 0; y < h; y++)
            {
                float f = (h - 1 - y) / (float)(h - 1);   // 0 = top, 1 = bottom (displayed)
                Color baseCol = f < 0.56f
                    ? Color.Lerp(navy, teal, f / 0.56f)
                    : Color.Lerp(teal, seabed, (f - 0.56f) / 0.44f);

                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float gi = Mathf.Clamp01(1f - d / gr) * 0.45f;
                    Color c = baseCol + glow * gi;
                    c.a = 1f;
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}