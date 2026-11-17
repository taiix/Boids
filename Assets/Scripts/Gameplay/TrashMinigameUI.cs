using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// Timing minigame for pushing trash. The red needle sweeps from one end of the arc to the other and
    /// back. Each side of the yellow line has an orange box: press Space (Enter / pad A) while the needle
    /// is inside it. A hit fills the speed circle - more the closer to the box's middle - and makes the
    /// box on the OTHER side jump to a new spot on its own side. Pressing outside a box, or letting the
    /// needle leave a side without hitting its box, costs speed; speed also drains slowly on its own.
    /// Read <see cref="Speed"/> (0-1) to push the trash.
    ///
    /// Spawns itself (like MatchHud): nothing to place in a scene. Layout is
    /// Resources/Hud/TrashMinigame.uxml, styles TrashMinigame.uss - both editable in the UI Builder.
    ///
    ///     TrashMinigameUI.Open();              // start
    ///     float s = TrashMinigameUI.Speed;     // 0-1 every frame while open
    ///     TrashMinigameUI.Close();             // stop
    /// </summary>
    public class TrashMinigameUI : MonoBehaviour
    {
        // ------------------------------------------------------------------ static API

        static TrashMinigameUI s_instance;

        public static TrashMinigameUI Instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = FindAnyObjectByType<TrashMinigameUI>();
                    if (s_instance == null) s_instance = new GameObject("TrashMinigameUI").AddComponent<TrashMinigameUI>();
                }
                return s_instance;
            }
        }

        /// <summary>True while the minigame is showing and taking input.</summary>
        public static bool IsOpen => s_instance != null && s_instance._open;

        /// <summary>Push speed 0-1. 0 when closed.</summary>
        public static float Speed => IsOpen ? s_instance._speed : 0f;

        /// <summary>Fires on every Space press: true = hit, false = miss (also when a box is let pass).</summary>
        public static event System.Action<bool> Pressed;

        /// <summary>A hit, with its accuracy (1 = dead centre of the box).</summary>
        public static event System.Action<float> Hit;

        /// <summary>Pushing heavy trash together: show who's on it and the shared speed (instead of ours).</summary>
        public static void SetTogether(bool together, int playing, int needed, float sharedSpeed)
        {
            if (s_instance == null) return;
            s_instance._together = together;
            s_instance._sharedSpeed = sharedSpeed;
            var pips = s_instance._pips;
            if (pips != null)
            {
                int n = together ? needed : 0, on = Mathf.Min(playing, needed);
                if (pips.needed != n || pips.playing != on) { pips.needed = n; pips.playing = on; pips.MarkDirtyRepaint(); }
                pips.style.display = together ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public static void ShowFeedback(string text, bool good) { if (s_instance != null) s_instance.Feedback(text, good ? "hit" : "miss"); }

        public static void Open() => Instance.OpenInternal();
        public static void Close() { if (s_instance != null) s_instance.CloseInternal(); }

        /// <summary>The reading in the speed ring (the distance to the sinkhole: "12 m").</summary>
        public static void SetHint(string text) { if (s_instance != null && s_instance._hint != null) s_instance._hint.text = text; }

        // ------------------------------------------------------------------ tuning

        [Header("Needle")]
        [Tooltip("Seconds for the needle to cross the whole arc once, at zero speed.")]
        public float sweepSeconds = 1.0f;          // 0.85 before the 2026-11-17 balance pass (Balance/README.md)
        [Tooltip("At full speed the needle sweeps this much faster (0.3 = 30%).")]
        public float speedUpAtFull = 0.35f;

        [Header("Boxes")]
        [Tooltip("Box width, as a fraction of one side of the arc.")]
        [Range(0.06f, 0.5f)] public float boxWidth = 0.3f;   // was 0.2: a ~40 ms window, half the hits missed
        [Tooltip("Boxes never come closer to the yellow line than this (fraction of a side).")]
        [Range(0f, 0.6f)] public float minFromCentre = 0.2f;
        [Tooltip("How fast a box slides to its new spot (sides per second).")]
        public float boxSlideSpeed = 3f;

        [Header("Speed")]
        [Tooltip("Speed added by a hit dead in the middle of a box.")]
        public float perfectGain = 0.22f;
        [Tooltip("Speed added by a hit right on a box's edge.")]
        public float edgeGain = 0.08f;
        [Tooltip("Speed lost for pressing outside a box, or letting a box pass.")]
        public float missPenalty = 0.08f;          // was 0.15
        [Tooltip("Speed lost per second, always.")]
        public float drainPerSecond = 0.06f;

        // ------------------------------------------------------------------ state

        UIDocument _doc;
        VisualElement _panel;
        TrashGauge _gauge;
        SpeedRing _ring;
        Label _speedValue, _hint, _feedback, _hitKey, _quitKey;
        VisualElement _flash;
        int _glyphVersion = -1;
        bool _together;
        float _sharedSpeed;
        PusherPips _pips;
        bool _open, _built;

        float _needle, _dir = 1f, _speed, _shownSpeed;
        float _leftTarget, _rightTarget, _leftPos, _rightPos;
        int _visitSide;                 // which side the needle is on (-1 / 1, 0 before it leaves the middle)
        float _leftFlash, _rightFlash, _feedbackUntil;

        float Half => boxWidth * 0.5f;

        void Awake()
        {
            if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
            s_instance = this;
            DontDestroyOnLoad(gameObject);

            var settings = Resources.Load<PanelSettings>("Hud/HudPanelSettings");
            var layout = Resources.Load<VisualTreeAsset>("Hud/TrashMinigame");
            if (settings == null || layout == null)
            {
                Debug.LogWarning("[TrashMinigame] Resources/Hud/HudPanelSettings or Resources/Hud/TrashMinigame.uxml is missing.");
                enabled = false;
                return;
            }
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            _doc.sortingOrder = 5;          // above MatchHud (0), below the pause menu (10)
            _doc.visualTreeAsset = layout;
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        void Build()
        {
            var root = _doc != null ? _doc.rootVisualElement : null;
            if (root == null) return;
            _panel = root.Q("trash-minigame");
            _gauge = root.Q<TrashGauge>("gauge");
            _ring = root.Q<SpeedRing>("speed-ring");
            _speedValue = root.Q<Label>("speed-value");
            _hint = root.Q<Label>("hint");
            _feedback = root.Q<Label>("feedback");
            _flash = root.Q("tm-flash");
            _hitKey = root.Q<Label>("hit-key");
            _quitKey = root.Q<Label>("quit-key");
            _pips = root.Q<PusherPips>("pips");
            if (_pips != null) _pips.style.display = DisplayStyle.None;
            if (_panel == null || _gauge == null || _ring == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            _built = true;
            ApplyVisibility();
        }

        void OpenInternal()
        {
            _open = true;
            _needle = 0f; _dir = Random.value < 0.5f ? -1f : 1f;
            _speed = 0f; _shownSpeed = 0f; _visitSide = 0;
            _together = false; _sharedSpeed = 0f;
            _leftTarget = _leftPos = -RandomSpot();
            _rightTarget = _rightPos = RandomSpot();
            _leftFlash = _rightFlash = 0f;
            _feedbackUntil = 0f;
            if (_built)
            {
                _gauge.leftState = _gauge.rightState = TrashGauge.BoxState.Ready;
                if (_hint != null) _hint.text = "";
            }
            ApplyVisibility();
        }

        void CloseInternal()
        {
            _open = false;
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            if (!_built) return;
            _panel.style.display = _open ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // A box centre on the right side (negate for the left), clear of the middle and the end.
        float RandomSpot() => Random.Range(minFromCentre + Half, 1f - Half);

        // Space (and pad A without Steam Input). An InputAction, like the rest of GameInput: a raw
        // Keyboard.spaceKey.wasPressedThisFrame can miss presses the Input System handles in another update.
        static InputAction s_hit;

        static bool PressedThisFrame()
        {
            if (s_hit == null)
            {
                s_hit = new InputAction("trash_hit", InputActionType.Button);
                s_hit.AddBinding("<Keyboard>/space");
                s_hit.AddBinding("<Gamepad>/buttonSouth");
                s_hit.Enable();
            }
            return s_hit.WasPressedThisFrame() || GameInput.PuzzleSubmit.WasPressedThisFrame();   // + Enter / Steam pad A
        }

        void Update()
        {
            if (!_built) Build();
            if (!_built || !_open) return;

            GameInput.ReportPuzzle();       // the keys go to the minigame, not to swimming
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (PauseMenu.IsOpen) dt = 0f;

            // needle: end to end and back, a little faster the faster you're going
            float rate = 2f / Mathf.Max(0.2f, sweepSeconds) * (1f + speedUpAtFull * _speed);
            _needle += _dir * rate * dt;
            if (_needle > 1f) { _needle = 2f - _needle; _dir = -1f; }
            if (_needle < -1f) { _needle = -2f - _needle; _dir = 1f; }

            // Crossing the yellow line: the side we're leaving had its chance.
            int side = _needle > 0f ? 1 : -1;
            if (side != _visitSide)
            {
                if (_visitSide != 0 && BoxState(_visitSide) == TrashGauge.BoxState.Ready)
                    Miss(_visitSide, "TOO LATE");
                SetBoxState(side, TrashGauge.BoxState.Ready);
                _visitSide = side;
            }

            if (dt > 0f && PressedThisFrame()) Press(side);

            _speed = Mathf.Clamp01(_speed - drainPerSecond * dt);

            // boxes slide to their new spots
            _leftPos = Mathf.MoveTowards(_leftPos, _leftTarget, boxSlideSpeed * dt);
            _rightPos = Mathf.MoveTowards(_rightPos, _rightTarget, boxSlideSpeed * dt);
            _leftFlash = Mathf.MoveTowards(_leftFlash, 0f, dt * 2.5f);
            _rightFlash = Mathf.MoveTowards(_rightFlash, 0f, dt * 2.5f);
            _shownSpeed = Mathf.MoveTowards(_shownSpeed, _together ? _sharedSpeed : _speed, dt * 1.5f);

            // The box to hit next: this side's, until it's been hit or missed; then the other side's.
            int current = _needle > 0f ? 1 : -1;
            _gauge.activeSide = BoxState(current) == TrashGauge.BoxState.Ready ? current : -current;

            // draw
            _gauge.needle = _needle;
            _gauge.boxHalf = Half;
            _gauge.leftBox = _leftPos;
            _gauge.rightBox = _rightPos;
            _gauge.leftFlash = _leftFlash;
            _gauge.rightFlash = _rightFlash;
            _ring.value = _shownSpeed;
            if (_speedValue != null) _speedValue.text = Mathf.RoundToInt(_shownSpeed * 100f) + "%";
            if (_flash != null) _flash.style.opacity = Mathf.Clamp01((_feedbackUntil - Time.unscaledTime) * 3f);

            // key tiles: the keyboard's keys, or the controller's buttons once it's in use
            if (_glyphVersion != GameInput.GlyphVersion)
            {
                _glyphVersion = GameInput.GlyphVersion;
                if (_hitKey != null) _hitKey.text = GameInput.UsingPad ? GameInput.PuzzleSubmit.PadLabel : "SPACE";
                if (_quitKey != null) _quitKey.text = GameInput.UsingPad ? GameInput.Interact.PadLabel : GameInput.Interact.KeyLabel;
            }
        }

        void Press(int side)
        {
            float centre = side < 0 ? _leftPos : _rightPos;
            float off = Mathf.Abs(_needle - centre);
            if (BoxState(side) == TrashGauge.BoxState.Ready && off <= Half)
            {
                float accuracy = 1f - off / Half;                       // 1 = dead centre
                _speed = Mathf.Clamp01(_speed + Mathf.Lerp(edgeGain, perfectGain, accuracy));
                SetBoxState(side, TrashGauge.BoxState.Hit);
                Flash(side);

                // the other side's box jumps somewhere new on its own side
                float spot = RandomSpot();
                if (side < 0) _rightTarget = spot; else _leftTarget = -spot;

                Feedback(accuracy > 0.7f ? "BIG SHOVE" : accuracy > 0.35f ? "SHOVE" : "NUDGE", "hit");
                Pressed?.Invoke(true);
                Hit?.Invoke(accuracy);
            }
            else
            {
                Miss(side, "MISSED");
            }
        }

        void Miss(int side, string text)
        {
            _speed = Mathf.Clamp01(_speed - missPenalty);
            if (BoxState(side) == TrashGauge.BoxState.Ready) SetBoxState(side, TrashGauge.BoxState.Missed);
            Flash(side);
            Feedback(text, "miss");
            Pressed?.Invoke(false);
        }

        TrashGauge.BoxState BoxState(int side) => side < 0 ? _gauge.leftState : _gauge.rightState;

        void SetBoxState(int side, TrashGauge.BoxState state)
        {
            if (side < 0) _gauge.leftState = state; else _gauge.rightState = state;
        }

        void Flash(int side)
        {
            if (side < 0) _leftFlash = 1f; else _rightFlash = 1f;
        }

        void Feedback(string text, string kind)
        {
            if (_feedback == null) return;
            _feedback.text = text;
            _flash?.EnableInClassList("tm-flash--miss", kind == "miss");
            _feedbackUntil = Time.unscaledTime + 0.6f;
        }
    }
}
