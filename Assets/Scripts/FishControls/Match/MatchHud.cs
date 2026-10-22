using System.Text.RegularExpressions;
using Mirror;
using ReefRun;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// The in-game HUD, in the same look as the menu and lobby (UI Toolkit). Its layout - every panel, bar and
    /// label, and their text - is Resources/Hud/MatchHud.uxml (styles in MatchHud.uss): open it in the UI
    /// Builder to change it. This fills in the live values and shows / hides the panels, finding them by name:
    ///   * survival clock (top centre), red in the last minute
    ///   * health / hunger (bottom left), "STARVING" once hunger runs out
    ///   * voice (bottom right): talking / quiet / mic off
    ///   * prompts with key tiles ("Press E to eat"), a task status line, a hold-progress bar, short flash messages
    ///   * screen flashes: red from the edges when you're hurt (<see cref="HurtFlash"/>, and on its own whenever your
    ///     health drops), a white pop when you tear free (<see cref="WhiteFlash"/>), and a slow red pulse while your
    ///     health is low; the bars react too - health shakes and flashes on a hit, hunger pops when you eat, and an
    ///     ability row pulses as it comes back
    /// (Your outcome and the round's result are RoundScreen's.)
    ///
    /// Created by the local player's body when it spawns, in the game scene, so it goes away with that
    /// scene. Layout, style and panel settings load from Resources/Hud, so nothing needs placing in a scene.
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
            WorldEvents.Show();
            ReefConditions.Show();
            if (s_instance != null) return;
            s_instance = new GameObject("MatchHud").AddComponent<MatchHud>();
        }

        // ---- per-frame requests (call every frame they apply; they vanish on their own) ----
        static string s_prompt;
        static int s_promptFrame = -10;
        static string s_status;
        static int s_statusFrame = -10;
        static string s_progressLabel;
        static float s_progress;
        static int s_progressFrame = -10;
        static string s_flash;
        static float s_flashUntil;
        static string s_event;
        static int s_eventFrame = -10;

        /// <summary>Show an interaction prompt (e.g. "Press E to eat") this frame. "press X" / "[X]"
        /// is drawn as a key tile.</summary>
        public static void ShowPrompt(string text)
        {
            s_prompt = text;
            s_promptFrame = Time.frameCount;
        }

        /// <summary>Show a task status line (e.g. "1/2 fish pushing") this frame, just under the prompt so the
        /// two never fight over one spot. Rich text colour tags work.</summary>
        public static void ShowStatus(string text)
        {
            s_status = text;
            s_statusFrame = Time.frameCount;
        }

        /// <summary>Show a progress bar (0-1) under the prompt this frame, e.g. a hold-to-complete task.</summary>
        public static void ShowProgress(string label, float progress01)
        {
            s_progressLabel = label;
            s_progress = Mathf.Clamp01(progress01);
            s_progressFrame = Time.frameCount;
        }

        /// <summary>Show what the reef is doing (a world event) under the clock, this frame.</summary>
        public static void ShowEvent(string text)
        {
            s_event = text;
            s_eventFrame = Time.frameCount;
        }

        static float s_hurt, s_pop;

        /// <summary>Red flash in from the screen's edges (0-1), fading on its own.</summary>
        public static void HurtFlash(float strength) => s_hurt = Mathf.Max(s_hurt, Mathf.Clamp01(strength));

        /// <summary>A white pop over the whole screen (0-1), fading fast.</summary>
        public static void WhiteFlash(float strength) => s_pop = Mathf.Max(s_pop, Mathf.Clamp01(strength));

        /// <summary>A short message in the middle of the screen (fades after a moment).</summary>
        public static void Flash(string text, float seconds = 1.2f)
        {
            s_flash = text;
            s_flashUntil = Time.unscaledTime + seconds;
        }

        // ---- UI ----
        UIDocument _doc;
        VisualElement _root, _clockFill, _healthFill, _hungerFill, _voice, _prompt, _promptPill, _status,
                      _progress, _progressFill, _flash, _abilities, _lungeFill, _sonarFill, _lungeRow, _sonarRow, _event;
        Label _clockTime, _healthVal, _hungerVal, _flashText, _statusText, _eventText;
        HudClockLabel _clockLabel;
        HudHungerLabel _hungerLabel;
        HudVoiceLabel _voiceLabel;
        string _clockPlaying, _hungerNormal, _voiceQuiet;   // their usual wordings, as laid out
        ReefGlyph _micIcon;
        VisualElement _vignette, _whitePop, _vitals, _hungerRow;
        FishVitals _feelVitals;
        float _lastHealth = -1f, _lastHunger = -1f;
        float _healthHitAt = -10f, _hungerPopAt = -10f, _lungeReadyAt = -10f, _sonarReadyAt = -10f;
        bool _lungeWasReady = true, _sonarWasReady = true, _barsMoved;
        static Texture2D s_vignetteTexture;
        string _shownPrompt;
        int _shownSeconds = int.MinValue, _shownHealth = int.MinValue, _shownHunger = int.MinValue;
        bool _built;

        static readonly Regex KeyPattern = new(@"(?i)(?:press\s+)?\[\s*(\w+)\s*\]|press\s+(\w+)");
        static readonly Color Orange = new(1f, 0.72f, 0.3f);

        void Awake()
        {
            var settings = Resources.Load<PanelSettings>("Hud/HudPanelSettings");
            if (settings == null) { Debug.LogWarning("[HUD] Resources/Hud/HudPanelSettings is missing - HUD disabled."); enabled = false; return; }
            var layout = Resources.Load<VisualTreeAsset>("Hud/MatchHud");
            if (layout == null) { Debug.LogWarning("[HUD] Resources/Hud/MatchHud.uxml is missing - HUD disabled."); enabled = false; return; }
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            _doc.visualTreeAsset = layout;
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        // The layout's pieces, found by name (MatchHud.uxml).
        void Build()
        {
            _root = _doc.rootVisualElement;
            if (_root == null || _root.Q("hud") == null) return;   // not laid out yet
            // A HUD never takes the mouse - nothing in it, whatever gets added in the UI Builder.
            _root.pickingMode = PickingMode.Ignore;
            _root.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);

            // full-screen flashes
            _vignette = Find<VisualElement>("hurt-vignette");
            _vignette.style.backgroundImage = VignetteTexture();
            _vignette.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
            _whitePop = Find<VisualElement>("white-pop");

            _clockLabel = Find<HudClockLabel>("clock-label");
            _clockTime = Find<Label>("clock-time");
            _clockFill = Find<VisualElement>("clock-fill");
            _event = Find<VisualElement>("event");
            _eventText = Find<Label>("event-text");

            _vitals = Find<VisualElement>("vitals");
            _healthFill = Find<VisualElement>("health-fill");
            _healthVal = Find<Label>("health-value");
            _hungerRow = Find<VisualElement>("hunger-row");
            _hungerLabel = Find<HudHungerLabel>("hunger-label");
            _hungerFill = Find<VisualElement>("hunger-fill");
            _hungerVal = Find<Label>("hunger-value");

            _voice = Find<VisualElement>("voice");
            _micIcon = Find<ReefGlyph>("mic-icon");
            _voiceLabel = Find<HudVoiceLabel>("voice-label");

            _prompt = Find<VisualElement>("prompt");
            _promptPill = Find<VisualElement>("prompt-pill");
            _promptPill.Clear();   // the layout's example prompt; the game writes its own
            _status = Find<VisualElement>("status");
            _statusText = Find<Label>("status-text");
            _progress = Find<VisualElement>("progress");
            _progressFill = Find<VisualElement>("progress-fill");

            _abilities = Find<VisualElement>("abilities");
            _lungeRow = Find<VisualElement>("lunge-row");
            _lungeFill = Find<VisualElement>("lunge-fill");
            _sonarRow = Find<VisualElement>("sonar-row");
            _sonarFill = Find<VisualElement>("sonar-fill");

            _flash = Find<VisualElement>("flash");
            _flashText = Find<Label>("flash-text");

            _clockPlaying = _clockLabel.text;
            _hungerNormal = _hungerLabel.text;
            _voiceQuiet = _voiceLabel.text;
            _built = true;
        }

        // A piece of the layout by name. One that's been removed (or renamed) in the UI Builder just doesn't show:
        // a stand-in off screen takes its place, with a warning saying which.
        T Find<T>(string name) where T : VisualElement, new()
        {
            var e = _root.Q<T>(name);
            if (e != null) return e;
            Debug.LogWarning($"[HUD] MatchHud.uxml has no {typeof(T).Name} named '{name}' - that part of the HUD won't show.");
            return new T();
        }

        void LateUpdate()
        {
            if (!_built) { Build(); if (!_built) return; }

            // the pause menu takes the screen; the HUD steps aside
            _root.style.display = PauseMenu.IsOpen ? DisplayStyle.None : DisplayStyle.Flex;

            UpdateClock();
            UpdateEvent();
            var vitals = FindLocalVitals();
            UpdateVitals(vitals);
            UpdateFeel(vitals);
            UpdateVoice();
            UpdateAbilities();
            UpdatePrompt();
            UpdateFlash();
        }

        // ---------------------------------------------------------------- pieces
        void UpdateClock()
        {
            var m = MatchManager.Instance;
            if (m == null) { _clockLabel.text = _clockLabel.noMatchText; return; }
            float left = m.RemainingSeconds;
            int s = Mathf.CeilToInt(left);
            if (s != _shownSeconds) { _shownSeconds = s; _clockTime.text = $"{s / 60:00}:{s % 60:00}"; } // no new string every frame
            _clockLabel.text = m.Phase == MatchPhase.Warmup ? _clockLabel.warmupText : m.IsPlaying ? _clockPlaying : _clockLabel.overText;
            bool low = m.IsPlaying && left <= 60f;
            _clockTime.EnableInClassList("clock-time--low", low);
            _clockFill.EnableInClassList("clock-fill--low", low);
            _clockFill.style.width = Length.Percent(m.RoundDuration > 0f ? 100f * left / m.RoundDuration : 0f);
        }

        void UpdateEvent()
        {
            bool show = !string.IsNullOrEmpty(s_event) && Time.frameCount - s_eventFrame <= 1;
            _event.EnableInClassList("hidden", !show);
            if (show && _eventText.text != s_event) _eventText.text = s_event;
        }

        void UpdateVitals(FishVitals v)
        {
            float h = v != null ? v.HealthNorm : 0f, g = v != null ? v.HungerNorm : 0f;
            _healthFill.style.width = Length.Percent(h * 100f);
            _hungerFill.style.width = Length.Percent(g * 100f);
            int hp = Mathf.RoundToInt(h * 100f), gp = Mathf.RoundToInt(g * 100f);
            if (hp != _shownHealth) { _shownHealth = hp; _healthVal.text = $"{hp}%"; }
            if (gp != _shownHunger) { _shownHunger = gp; _hungerVal.text = $"{gp}%"; }
            bool starving = v != null && !v.IsDead && g <= 0f;
            _hungerLabel.text = starving ? _hungerLabel.starvingText : _hungerNormal;
            // blink while starving
            _hungerLabel.EnableInClassList("vlabel--alert", starving && Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f);
        }

        void UpdateVoice()
        {
            var mic = ProximityVoice.LocalMic;
            bool off = !VoiceSettings.MicOn;
            bool talking = !off && mic != null && mic.IsSpeaking;
            // The shark is close enough to have heard us: the panel turns red and blinks while it lasts.
            bool heard = Time.unscaledTime < ProximityVoice.HeardBySharkUntil;
            // Out of the round: only the other ghosts hear us now.
            bool ghost = mic != null && mic.IsGhost;
            var v = _voiceLabel;
            v.text = heard ? v.sharkHeardText : mic == null ? v.noMicText : off ? v.micOffText
                   : ghost ? (talking ? v.ghostTalkingText : v.ghostText) : talking ? v.talkingText : _voiceQuiet;
            _voice.EnableInClassList("voice--talking", talking && !heard);
            _voice.EnableInClassList("voice--off", (off || mic == null) && !heard);
            _voice.EnableInClassList("voice--heard", heard);
            _voice.EnableInClassList("voice--heard-blink", heard && Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.25f);
            var kind = off || mic == null ? ReefGlyph.Kind.MicOff : ReefGlyph.Kind.Mic;
            if (_micIcon.kind != kind) _micIcon.kind = kind;
            _micIcon.color = heard ? new Color(1f, 0.42f, 0.42f) : talking ? new Color(0.17f, 0.76f, 0.77f) : off || mic == null ? new Color(0.9f, 0.7f, 0.2f) : new Color(0.56f, 0.69f, 0.73f);
        }

        // The shark's cooldowns: each bar fills back up, and the row lights when it's ready.
        void UpdateAbilities()
        {
            var shark = FindLocalShark();
            bool show = shark != null && !(shark.TryGetComponent(out FishPlayer fp) && fp.IsOut);
            _abilities.EnableInClassList("hidden", !show);
            if (!show) return;
            _lungeFill.style.width = Length.Percent(shark.LungeCharge * 100f);
            _sonarFill.style.width = Length.Percent(shark.SonarCharge * 100f);
            _lungeRow.EnableInClassList("arow--ready", shark.LungeReady);
            _sonarRow.EnableInClassList("arow--ready", shark.SonarReady);

            // A row that's just come back pulses.
            float now = Time.unscaledTime;
            if (shark.LungeReady && !_lungeWasReady) _lungeReadyAt = now;
            if (shark.SonarReady && !_sonarWasReady) _sonarReadyAt = now;
            _lungeWasReady = shark.LungeReady;
            _sonarWasReady = shark.SonarReady;
            Pop(_lungeRow, now - _lungeReadyAt, 0.4f, 0.14f);
            Pop(_sonarRow, now - _sonarReadyAt, 0.4f, 0.14f);
        }

        // ---------------------------------------------------------------- feel

        void UpdateFeel(FishVitals v)
        {
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;

            // Health dropping (whatever hurt us) flashes red and shakes the bar; hunger rising (we ate) pops it.
            if (v != _feelVitals) { _feelVitals = v; _lastHealth = _lastHunger = -1f; }
            if (v != null)
            {
                float h = v.HealthNorm, g = v.HungerNorm;
                float lost = _lastHealth - h;
                if (_lastHealth >= 0f && lost >= 0.03f)
                {
                    HurtFlash(Mathf.Clamp(lost * 5f, 0.35f, 1f));
                    _healthHitAt = now;
                }
                if (_lastHunger >= 0f && g - _lastHunger >= 0.03f) _hungerPopAt = now;
                _lastHealth = h;
                _lastHunger = g;
            }

            s_hurt = Mathf.Max(0f, s_hurt - 2.2f * dt);
            s_pop = Mathf.Max(0f, s_pop - 4f * dt);

            // Low health: a slow red pulse, like a heartbeat, until we're out of danger (or out of the round).
            var me = v != null ? v.GetComponent<FishPlayer>() : null;
            bool low = v != null && !v.IsDead && v.HealthNorm < 0.25f && (me == null || !me.IsOut);
            float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(now * 2.6f)), 6f);
            float pulse = low ? 0.14f + 0.2f * beat : 0f;
            _vignette.style.opacity = Mathf.Clamp01(Mathf.Max(s_hurt * 0.9f, pulse));
            _whitePop.style.opacity = s_pop * 0.45f;

            // The health bar jolts and flashes white for a moment after a hit.
            float since = now - _healthHitAt;
            if (since < 0.35f)
            {
                float a = 1f - since / 0.35f;
                _vitals.style.translate = new Translate(Random.Range(-7f, 7f) * a, Random.Range(-3f, 3f) * a);
                _healthFill.style.backgroundColor = since < 0.1f ? new StyleColor(Color.white) : new StyleColor(StyleKeyword.Null);
                _barsMoved = true;
            }
            else if (_barsMoved)
            {
                _barsMoved = false;
                _vitals.style.translate = StyleKeyword.Null;
                _healthFill.style.backgroundColor = StyleKeyword.Null;
            }
            Pop(_hungerRow, now - _hungerPopAt, 0.35f, 0.15f);
        }

        // Scale an element up by `amount` and let it settle back over `seconds`.
        static void Pop(VisualElement e, float since, float seconds, float amount)
        {
            if (e == null) return;
            if (since >= 0f && since < seconds)
            {
                float u = since / seconds;
                float s = 1f + amount * Mathf.Sin(u * Mathf.PI) * (1f - u * 0.5f);
                e.style.scale = new Scale(new Vector3(s, s, 1f));
            }
            else if (e.style.scale.keyword != StyleKeyword.Null) e.style.scale = StyleKeyword.Null;
        }

        // White, clear in the middle and solid at the edges (tinted red by MatchHud.uss's .hurt-vignette).
        static Texture2D VignetteTexture()
        {
            if (s_vignetteTexture != null) return s_vignetteTexture;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HurtVignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 1f, r));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return s_vignetteTexture = tex;
        }

        static SharkAbilities FindLocalShark()
        {
            if (NetworkClient.active)
                return NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<SharkAbilities>() : null;
            var s = AnyActive(SharkAbilities.Loaded);
            return s != null && s.enabled ? s : null;
        }

        // FindAnyObjectByType without searching the whole scene every frame: any instance on an active object.
        static T AnyActive<T>(System.Collections.Generic.List<T> loaded) where T : Component
        {
            foreach (var c in loaded)
                if (c != null && c.gameObject.activeInHierarchy) return c;
            return null;
        }

        void UpdatePrompt()
        {
            bool prompt = !string.IsNullOrEmpty(s_prompt) && Time.frameCount - s_promptFrame <= 1;
            bool progress = Time.frameCount - s_progressFrame <= 1;
            bool status = !string.IsNullOrEmpty(s_status) && Time.frameCount - s_statusFrame <= 1;
            _prompt.EnableInClassList("hidden", !prompt && !progress && !status);

            _status.EnableInClassList("hidden", !status);
            if (status && _statusText.text != s_status) _statusText.text = s_status;

            string text = prompt ? s_prompt : progress ? s_progressLabel : null;
            if (text != _shownPrompt) { _shownPrompt = text; FillPrompt(text); }
            _promptPill.EnableInClassList("hidden", string.IsNullOrEmpty(text));

            _progress.EnableInClassList("hidden", !progress);
            if (progress) _progressFill.style.width = Length.Percent(s_progress * 100f);
        }

        // "Press E to eat" -> [text "Press"] [key E] [text "to eat"]
        void FillPrompt(string text)
        {
            _promptPill.Clear();
            if (string.IsNullOrEmpty(text)) return;
            int at = 0;
            foreach (Match m in KeyPattern.Matches(text))
            {
                if (m.Index > at) Text(_promptPill, text.Substring(at, m.Index - at).Trim(), "prompt-text");
                string key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (m.Value.IndexOf("press", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    Text(_promptPill, "Press", "prompt-text");
                Text(_promptPill, key.ToUpperInvariant(), "key");
                at = m.Index + m.Length;
            }
            if (at < text.Length) Text(_promptPill, text.Substring(at).Trim(), "prompt-text");
        }

        void UpdateFlash()
        {
            bool show = s_flash != null && Time.unscaledTime < s_flashUntil;
            if (show && _flashText.text != s_flash) _flashText.text = s_flash;
            _flash.EnableInClassList("flash--show", show);
        }

        static FishVitals FindLocalVitals()
        {
            if (NetworkClient.active && NetworkClient.localPlayer != null)
                return NetworkClient.localPlayer.GetComponent<FishVitals>();
            // Offline sandbox: just show the (single) body that has vitals.
            return AnyActive(FishVitals.Loaded);
        }

        // ---------------------------------------------------------------- helpers
        static Label Text(VisualElement parent, string text, params string[] classes)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) l.AddToClassList(c);
            parent.Add(l);
            return l;
        }
    }
}
