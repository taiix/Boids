using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// The round's on-screen moments, drawn with UI Toolkit in the lobby's style:
    ///   * at the start, what you are and what to do ("Survive and escape" / "Hunt down all other players"),
    ///   * when the clock runs out, which wall has lifted, plus a marker pointing to the way out,
    ///   * caught in the shark's jaws: mash to break free (meter, and how long before it swallows),
    ///   * your own outcome (escaped / eaten / died) and, while you spectate, who you're watching,
    ///   * at the end, the results: who escaped, who was eaten, and which side won - with Play again /
    ///     Back to lobby for the host and Leave for everyone.
    ///
    /// Created by the local player's body in the game scene (alongside MatchHud), so it goes with the scene.
    /// Everything here is read from MatchManager and the local player; it sends nothing.
    /// Styles: Resources/RoundScreen.uss. Panel: Resources/RoundScreenPanel (drawn under the launch overlay).
    /// </summary>
    public class RoundScreen : MonoBehaviour
    {
        /// <summary>The results are up: the mouse belongs to their buttons, not the camera.</summary>
        public static bool ShowingResults { get; private set; }

        static RoundScreen s_instance;

        /// <summary>Show the round screen in the current (game) scene, once.</summary>
        public static void Show()
        {
            if (s_instance != null) return;
            s_instance = new GameObject("RoundScreen").AddComponent<RoundScreen>();
        }

        const string MainMenuScene = "01 - MainMenu";
        const string FallbackLobbyScene = "02 - ReefRunLobby";

        UIDocument _doc;
        VisualElement _root;
        VisualElement _banner, _marker, _spectate, _results, _columns, _buttons;
        VisualElement _struggle, _struggleFill, _struggleTimeFill;
        Label _struggleSub;
        float _struggleShown;
        bool _wasHeld;
        Label _bannerEyebrow, _bannerTitle, _bannerSub, _markerLabel, _spectateName, _spectateHint;
        Label _resEyebrow, _resTitle, _resYou, _resSub, _resShark, _resLeft, _resWait;
        Button _playAgain, _toLobby, _leave;

        float _bannerShownAt = -1f, _bannerHold;
        FishPlayer _introFor, _outcomeFor;
        PlayerOutcome _lastOutcome;
        MatchPhase _lastPhase = MatchPhase.Warmup;
        float _nextResultsRefresh;
        bool _leaving;

        void Awake()
        {
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = Resources.Load<PanelSettings>("RoundScreenPanel");
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            ShowingResults = false;
        }

        void Start() => Build();

        // ------------------------------------------------------------------ layout

        void Build()
        {
            _root = _doc.rootVisualElement;
            if (_root == null) return;
            var sheet = Resources.Load<StyleSheet>("RoundScreen");
            if (sheet != null) _root.styleSheets.Add(sheet);
            _root.AddToClassList("rs-root");
            _root.pickingMode = PickingMode.Ignore;

            _banner = Box(_root, "rs-banner");
            _bannerEyebrow = Text(_banner, "rs-eyebrow");
            _bannerTitle = Text(_banner, "rs-title");
            _bannerSub = Text(_banner, "rs-sub");

            _marker = Box(_root, "rs-marker");
            Box(_marker, "rs-diamond");
            _markerLabel = Text(_marker, "rs-marker-label");

            _spectate = Box(_root, "rs-spectate");
            var card = Box(_spectate, "rs-spectate-card");
            Text(card, "rs-spec-eyebrow", "SPECTATING");
            _spectateName = Text(card, "rs-spec-name");
            _spectateHint = Text(card, "rs-spec-hint", "Left click: next player   ·   Right click: previous");

            _struggle = Box(_root, "rs-struggle");
            Text(_struggle, "rs-struggle-title", "BREAK FREE!");
            Text(_struggle, "rs-struggle-key", "MASH  SPACE");
            var bar = Box(_struggle, "rs-struggle-bar");
            _struggleFill = Box(bar, "rs-struggle-fill");
            var time = Box(_struggle, "rs-struggle-time");
            _struggleTimeFill = Box(time, "rs-struggle-time-fill");
            _struggleSub = Text(_struggle, "rs-struggle-sub");

            _results = Box(_root, "rs-results");
            var res = Box(_results, "rs-card");
            _resEyebrow = Text(res, "rs-res-eyebrow", "ROUND OVER");
            _resTitle = Text(res, "rs-res-title");
            _resYou = Text(res, "rs-res-you");
            _resSub = Text(res, "rs-res-sub");
            _columns = Box(res, "rs-columns");
            _resShark = Text(res, "rs-shark-line");
            _resLeft = Text(res, "rs-left-line");
            _buttons = Box(res, "rs-buttons");
            _playAgain = Btn(_buttons, "PLAY AGAIN", PlayAgain, "primary");
            _toLobby = Btn(_buttons, "BACK TO LOBBY", BackToLobby);
            _leave = Btn(_buttons, "LEAVE GAME", Leave);
            _resWait = Text(res, "rs-wait", "Waiting for the host to start the next round...");

            foreach (var e in new[] { _banner, _marker, _spectate, _struggle })
                SetPickingIgnore(e);
            _struggle.style.display = DisplayStyle.None;
            _results.style.display = DisplayStyle.None;
            _marker.style.display = DisplayStyle.None;
            _spectate.style.display = DisplayStyle.None;
            _banner.style.opacity = 0f;
        }

        static VisualElement Box(VisualElement parent, string cls)
        {
            var v = new VisualElement();
            v.AddToClassList(cls);
            parent.Add(v);
            return v;
        }

        static Label Text(VisualElement parent, string cls, string text = "")
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        static Button Btn(VisualElement parent, string text, System.Action onClick, string extra = null)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("rs-btn");
            if (extra != null) b.AddToClassList(extra);
            parent.Add(b);
            return b;
        }

        static void SetPickingIgnore(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            foreach (var child in e.Children()) SetPickingIgnore(child);
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (_root == null) return;
            var m = MatchManager.Instance;
            var me = LocalPlayer();

            if (m != null)
            {
                WatchForMoments(m, me);
                _lastPhase = m.Phase;
            }

            UpdateStruggle(me);
            UpdateBanner();
            UpdateMarker(m, me);
            UpdateSpectating(me);
            UpdateResults(m, me);
        }

        void WatchForMoments(MatchManager m, FishPlayer me)
        {
            // Your role and goal, as the round starts (and again if F8 hands you the other body mid-round).
            if (me != null && me != _introFor && m.Phase == MatchPhase.Playing && me.RoundLive && !me.IsOut)
            {
                _introFor = me;
                if (me.IsSharkBody)
                    Banner("YOU ARE THE SHARK", "HUNT DOWN ALL OTHER PLAYERS",
                           "Catch the fish before the clock runs out and they escape", "shark", 6f);
                else
                    Banner("YOU ARE A FISH", "SURVIVE AND ESCAPE",
                           "When the clock runs out a wall lifts - swim through it to escape", "fish", 6f);
            }

            // The clock ran out: say which wall lifted.
            if (m.Phase == MatchPhase.Escape && _lastPhase != MatchPhase.Escape)
            {
                string wall = ArenaWalls.Instance != null ? ArenaWalls.Instance.NameOf(m.ExitWall) : "";
                string title = string.IsNullOrEmpty(wall) ? "THE WAY OUT IS OPEN" : $"THE {wall.ToUpperInvariant()} WALL HAS LIFTED";
                string sub = me != null && me.IsSharkBody
                    ? "Every fish is revealed to you - don't let them escape!"
                    : me != null && !me.IsOut
                        ? "Swim through the gap to escape!"
                        : "The fish are making a run for it";
                Banner("TIME'S UP", title, sub, "alert", 7f);
            }

            // We're out of the round.
            if (me != _outcomeFor) { _outcomeFor = me; _lastOutcome = me != null ? me.Outcome : PlayerOutcome.Alive; }
            if (me != null && me.Outcome != _lastOutcome)
            {
                _lastOutcome = me.Outcome;
                const string watch = "You can watch the others until the round ends";
                switch (me.Outcome)
                {
                    case PlayerOutcome.Escaped: Banner("YOU MADE IT", "YOU ESCAPED!", watch, "fish", 5f); break;
                    case PlayerOutcome.Eaten: Banner("CAUGHT", "YOU WERE EATEN", watch, "shark", 5f); break;
                    case PlayerOutcome.Died: Banner("OUT OF STRENGTH", "YOU DIED", watch, "shark", 5f); break;
                }
            }
        }

        // Caught: the break-free meter, how long until the shark swallows, and how fast to mash (faster when
        // hurt). Tearing free gets a banner.
        void UpdateStruggle(FishPlayer me)
        {
            bool held = me != null && me.IsHeld && !ShowingResults;
            if (_wasHeld && me != null && !me.IsHeld && !me.IsBeingEaten && !me.IsOut)
                Banner("TORE FREE", "YOU BROKE FREE!", "You're hurt and bleeding - swim for it!", "fish", 3f);
            if (held && !_wasHeld) _struggleShown = 0f;
            _wasHeld = me != null && me.IsHeld;

            _struggle.style.display = held ? DisplayStyle.Flex : DisplayStyle.None;
            if (!held) return;

            // The meter comes from the host a few times a second; ease toward it so it doesn't step.
            _struggleShown = Mathf.MoveTowards(_struggleShown, me.StruggleMeter, Time.unscaledDeltaTime * 2.5f);
            _struggleFill.style.width = Length.Percent(_struggleShown * 100f);
            float left = me.StruggleSeconds > 0f ? me.StruggleSecondsLeft / me.StruggleSeconds : 0f;
            _struggleTimeFill.style.width = Length.Percent(Mathf.Clamp01(left) * 100f);
            _struggle.EnableInClassList("pressed", Time.unscaledTime - me.LastStrugglePress < 0.07f);

            int health = me.TryGetComponent(out FishVitals v) ? Mathf.RoundToInt(v.HealthNorm * 100f) : 100;
            _struggleSub.text = $"Health {health}%  -  mash faster than {me.StruggleHoldRate:0.#} a second";
        }

        void Banner(string eyebrow, string title, string sub, string tone, float hold)
        {
            _bannerEyebrow.text = eyebrow;
            _bannerTitle.text = title;
            _bannerSub.text = sub;
            _banner.EnableInClassList("fish", tone == "fish");
            _banner.EnableInClassList("shark", tone == "shark");
            _banner.EnableInClassList("alert", tone == "alert");
            _bannerShownAt = Time.unscaledTime;
            _bannerHold = hold;
        }

        // Fade in, hold, fade out.
        void UpdateBanner()
        {
            float a = 0f;
            if (_bannerShownAt >= 0f && !ShowingResults)
            {
                float t = Time.unscaledTime - _bannerShownAt;
                a = t < 0.4f ? t / 0.4f : t < 0.4f + _bannerHold ? 1f : 1f - (t - 0.4f - _bannerHold) / 0.8f;
                if (a <= 0f) _bannerShownAt = -1f;
            }
            _banner.style.opacity = Mathf.Clamp01(a);
        }

        // A marker on the way out: on the open wall, level with you, the nearest stretch of the gap. Held at
        // the edge of the screen when it's off to the side or behind.
        void UpdateMarker(MatchManager m, FishPlayer me)
        {
            var walls = ArenaWalls.Instance;
            var cam = ViewCamera(me);
            bool show = m != null && walls != null && cam != null && m.ExitWall >= 0 && !m.IsOver
                        && me != null && !me.IsOut && !ShowingResults;
            _marker.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            Vector3 eye = cam.transform.position;
            Vector3 exit = walls.ExitPointNear(m.ExitWall, eye);
            Vector3 sp = cam.WorldToScreenPoint(exit);
            float w = Screen.width, h = Screen.height, inset = Mathf.Min(w, h) * 0.08f;
            var p = new Vector2(sp.x, sp.y);
            bool behind = sp.z < 0f;
            if (behind || p.x < inset || p.x > w - inset || p.y < inset || p.y > h - inset)
            {
                var c = new Vector2(w * 0.5f, h * 0.5f);
                Vector2 d = p - c;
                if (behind) d = -d;
                if (d.sqrMagnitude < 1f) d = Vector2.down;
                float k = Mathf.Min((c.x - inset) / Mathf.Max(Mathf.Abs(d.x), 1e-3f),
                                    (c.y - inset) / Mathf.Max(Mathf.Abs(d.y), 1e-3f));
                p = c + d * (behind ? k : Mathf.Min(k, 1f));
            }

            float scale = _root.resolvedStyle.width > 0f ? _root.resolvedStyle.width / w : 1f;
            _marker.style.left = p.x * scale;
            _marker.style.top = (h - p.y) * scale;
            _markerLabel.text = $"EXIT  {Vector3.Distance(eye, exit):0} m";
        }

        void UpdateSpectating(FishPlayer me)
        {
            var spec = Spectator.Active;
            bool show = spec != null && me != null && me.IsOut && !ShowingResults;
            _spectate.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            var who = spec.Watching;
            string side = who != null && who.IsSharkBody ? "<color=#ff5d6c>SHARK</color>" : "<color=#3fe0c5>FISH</color>";
            _spectateName.text = who == null ? "Nobody left in the water" : $"{who.PlayerName}   {side}";
            _spectateHint.style.display = who != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ results

        void UpdateResults(MatchManager m, FishPlayer me)
        {
            bool over = m != null && m.IsOver;
            if (over != ShowingResults)
            {
                ShowingResults = over;
                _results.style.display = over ? DisplayStyle.Flex : DisplayStyle.None;
                _results.EnableInClassList("shown", false);
                if (over) _results.schedule.Execute(() => _results.AddToClassList("shown")).StartingIn(30);
                _nextResultsRefresh = 0f;
            }
            if (!over) return;

            // The buttons need the mouse.
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            if (Time.unscaledTime < _nextResultsRefresh) return;
            _nextResultsRefresh = Time.unscaledTime + 0.5f;
            FillResults(m, me);
        }

        void FillResults(MatchManager m, FishPlayer me)
        {
            bool fishWin = m.Phase == MatchPhase.FishWin;
            _resTitle.text = fishWin ? "THE FISH WIN" : "THE SHARK WINS";
            _resTitle.EnableInClassList("fish", fishWin);
            _resTitle.EnableInClassList("shark", !fishWin);

            var escaped = new List<string>();
            var eaten = new List<string>();
            var died = new List<string>();
            var sharks = new List<string>();
            var left = new List<string>();
            string myName = me != null ? me.PlayerName : null;
            int fish = 0;
            foreach (var p in m.Roster)
            {
                string name = p.name == myName ? $"{p.name}  <color=#88b4bd>(you)</color>" : p.name;
                if (p.outcome == PlayerOutcome.Left) { left.Add(p.name); continue; }
                if (p.role == FishRole.Shark) { sharks.Add(name); continue; }
                fish++;
                if (p.outcome == PlayerOutcome.Escaped) escaped.Add(name);
                else if (p.outcome == PlayerOutcome.Eaten) eaten.Add(name);
                else died.Add(name);
            }

            if (fishWin) _resSub.text = $"{escaped.Count} of {fish} fish escaped";
            else if (escaped.Count == 0) _resSub.text = fish == 1 ? "The fish didn't escape" : "No fish escaped";
            else _resSub.text = $"Only {escaped.Count} of {fish} fish escaped - more than half had to";

            // Did *you* win? Fish win together, the shark wins alone.
            bool known = me != null;
            bool won = known && (me.IsSharkBody ? !fishWin : fishWin);
            _resYou.style.display = known ? DisplayStyle.Flex : DisplayStyle.None;
            _resYou.text = won ? "YOU WON" : "YOU LOST";
            _resYou.EnableInClassList("won", won);
            _resYou.EnableInClassList("lost", !won);

            _columns.Clear();
            Column("ESCAPED", "escaped", escaped, "Nobody escaped");
            Column("EATEN BY THE SHARK", "eaten", eaten, "Nobody was eaten");
            if (died.Count > 0) Column("DIDN'T SURVIVE", "died", died, null);

            _resShark.text = sharks.Count == 0 ? "" : $"THE SHARK  <color=#ff5d6c><b>{string.Join(", ", sharks)}</b></color>";
            _resShark.style.display = sharks.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _resLeft.text = left.Count == 0 ? "" : $"Left the game: {string.Join(", ", left)}";
            _resLeft.style.display = left.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // The host picks what happens next; everyone else can leave.
            bool host = NetworkServer.active || MatchManager.Offline;
            _playAgain.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            _toLobby.style.display = NetworkServer.active ? DisplayStyle.Flex : DisplayStyle.None;
            _resWait.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void Column(string heading, string cls, List<string> names, string empty)
        {
            var col = Box(_columns, "rs-col");
            col.AddToClassList(cls);
            Text(col, "rs-col-head", heading);
            if (names.Count == 0 && empty != null) Text(col, "rs-empty", empty);
            foreach (var n in names)
            {
                var row = Box(col, "rs-row");
                Box(row, "rs-dot");
                Text(row, "rs-row-name", n);
            }
        }

        // ------------------------------------------------------------------ buttons

        void PlayAgain()
        {
            if (_leaving) return;
            if (NetworkServer.active && NetworkManager.singleton is CustomNetworkManager nm)
            {
                _leaving = true;
                nm.ServerChangeScene(nm.GameSceneName); // everyone reloads; a new shark is drawn
            }
            else if (MatchManager.Offline)
            {
                _leaving = true;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
        }

        void BackToLobby()
        {
            if (_leaving || !NetworkServer.active) return;
            _leaving = true;
            string lobby = SteamLobby.instance != null && !string.IsNullOrEmpty(SteamLobby.instance.LobbySceneName)
                ? SteamLobby.instance.LobbySceneName
                : FallbackLobbyScene;
            NetworkManager.singleton.ServerChangeScene(lobby); // pulls every client along
        }

        void Leave()
        {
            if (_leaving) return;
            _leaving = true;
            SteamLobby.instance?.LeaveLobby();
            SceneManager.LoadScene(MainMenuScene);
        }

        // ------------------------------------------------------------------ helpers

        static FishPlayer LocalPlayer()
        {
            if (NetworkClient.active)
                return NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<FishPlayer>() : null;
            foreach (var fp in FishPlayer.All)
                if (fp != null && fp.IsOfflineSandboxBody) return fp;
            return null;
        }

        static Camera ViewCamera(FishPlayer me)
        {
            if (me != null && me.camera != null && me.camera.TryGetComponent(out Camera cam) && cam.isActiveAndEnabled)
                return cam;
            return Camera.main;
        }
    }
}
