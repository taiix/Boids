using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// The Steam Input action sets (SteamInput/steam_input_manifest.vdf). Only one is active at a time; a
    /// controller button means whatever the active set binds it to, so the same button can dive in the water
    /// and go back in a menu. <see cref="GameInput.CurrentSet"/> picks it every frame.
    /// </summary>
    public enum InputSet { Menu, Fish, Shark, Spectator, Puzzle }

    /// <summary>
    /// One thing the player can do, whatever they do it with. Keyboard and mouse come from the Input System.
    /// A controller comes through Steam Input (the action of the same <see cref="Name"/> in the manifest), so
    /// the player can rebind it in Steam's configurator and we can ask Steam which button it is on, to draw
    /// the right glyph. With no Steam Input (Steam not running, or the player switched it off for the game)
    /// the controller falls back to the Input System's gamepad on the same default layout.
    /// </summary>
    public sealed class GameAction
    {
        /// <summary>The action's name in the Steam Input manifest.</summary>
        public readonly string Name;
        public readonly bool Analog;
        /// <summary>What a keyboard / mouse prompt shows, e.g. "E" or "LMB".</summary>
        public readonly string KeyLabel;
        /// <summary>What a controller prompt shows when Steam can't give us a glyph.</summary>
        public readonly string PadLabel;
        /// <summary>The default button, in Xbox terms: used to ask Steam for a glyph when the controller is
        /// on a gamepad-emulation layout instead of our actions.</summary>
        public readonly EXboxOrigin Xbox;
        /// <summary>The set a prompt for this action is looked up in unless told otherwise.</summary>
        public readonly InputSet HomeSet;

        /// <summary>Stands for this action's button in a HUD prompt: "Press {interact} to eat" (see MatchHud.ShowPrompt).</summary>
        public string Token => "{" + Name + "}";

        internal readonly int Sets;          // bit per InputSet the action belongs to
        internal readonly InputAction Kbm;   // keyboard / mouse (may be null)
        internal readonly InputAction Pad;   // Input System gamepad, used only without Steam Input (may be null)

        // Filled in by SteamInputService each frame.
        internal bool SteamNow, SteamPrev;
        internal Vector2 SteamVec;
        internal InputDigitalActionHandle_t DigitalHandle;
        internal InputAnalogActionHandle_t AnalogHandle;

        internal GameAction(string name, bool analog, InputSet[] sets, string keyLabel, string padLabel,
                            EXboxOrigin xbox, InputAction kbm, InputAction pad)
        {
            Name = name;
            Analog = analog;
            KeyLabel = keyLabel;
            PadLabel = padLabel;
            Xbox = xbox;
            HomeSet = sets[0];
            foreach (var s in sets) Sets |= 1 << (int)s;
            Kbm = kbm;
            Pad = pad;
            Kbm?.Enable();
            Pad?.Enable();
        }

        // The Input System gamepad only speaks when Steam isn't delivering our actions, and - like Steam -
        // only for actions in the active set.
        bool PadLive => Pad != null && !SteamInputService.Native && GameInput.InCurrentSet(this);

        public bool IsPressed() =>
            (Kbm != null && Kbm.IsPressed()) || (PadLive && Pad.IsPressed()) || SteamNow;

        public bool WasPressedThisFrame() =>
            (Kbm != null && Kbm.WasPressedThisFrame()) || (PadLive && Pad.WasPressedThisFrame()) || (SteamNow && !SteamPrev);

        public bool WasReleasedThisFrame() =>
            (Kbm != null && Kbm.WasReleasedThisFrame()) || (PadLive && Pad.WasReleasedThisFrame()) || (!SteamNow && SteamPrev);

        /// <summary>Keyboard / mouse part of a 2D action (mouse delta is per frame, in pixels).</summary>
        public Vector2 ReadPointer() => Kbm != null && Analog ? Kbm.ReadValue<Vector2>() : Vector2.zero;

        /// <summary>Controller part of a 2D action: a stick, -1..1.</summary>
        public Vector2 ReadPad()
        {
            if (!Analog) return Vector2.zero;
            if (SteamInputService.Native) return SteamVec;
            return PadLive ? Pad.ReadValue<Vector2>() : Vector2.zero;
        }

        /// <summary>Both together, for actions where they mean the same (movement).</summary>
        public Vector2 ReadVector2() => Vector2.ClampMagnitude(ReadPointer() + ReadPad(), 1f);

        internal bool UnityPadActuated =>
            Pad != null && !SteamInputService.Native && (Analog ? Pad.ReadValue<Vector2>().sqrMagnitude > 0.16f : Pad.IsPressed());
    }

    /// <summary>
    /// Every input the game reads, in one place. Scripts ask these actions instead of building their own, so
    /// keyboard, mouse and any controller Steam knows about all arrive the same way.
    ///
    /// Also keeps track of what the player is holding right now (<see cref="UsingPad"/>) so prompts can show
    /// keys or controller glyphs (see <see cref="InputGlyphs"/>), and says when a controller they were
    /// playing on goes away (<see cref="ControllerDisconnected"/>) so the match can open its menu.
    /// </summary>
    public static class GameInput
    {
        static readonly List<GameAction> s_all = new List<GameAction>();
        public static IReadOnlyList<GameAction> All => s_all;

        static readonly InputSet[] Swim = { InputSet.Fish, InputSet.Shark };
        static readonly InputSet[] Everywhere = { InputSet.Fish, InputSet.Shark, InputSet.Spectator, InputSet.Puzzle, InputSet.Menu };

        // ---- swimming (fish and shark) ----
        public static readonly GameAction Move = Stick("move", Swim, "WASD", "Left Stick", EXboxOrigin.k_EXboxOrigin_LeftStick_Move,
            Wasd(), "<Gamepad>/leftStick");
        public static readonly GameAction Look = Stick("camera", new[] { InputSet.Fish, InputSet.Shark, InputSet.Puzzle }, "MOUSE", "Right Stick",
            EXboxOrigin.k_EXboxOrigin_RightStick_Move, Bound(new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2"), "<Mouse>/delta"),
            "<Gamepad>/rightStick");
        public static readonly GameAction Ascend = Button("ascend", Swim, "SPACE", "A", EXboxOrigin.k_EXboxOrigin_A,
            new[] { "<Keyboard>/space" }, "<Gamepad>/buttonSouth");
        public static readonly GameAction Descend = Button("descend", Swim, "C", "B", EXboxOrigin.k_EXboxOrigin_B,
            new[] { "<Keyboard>/c", "<Keyboard>/leftCtrl" }, "<Gamepad>/buttonEast");
        public static readonly GameAction Boost = Button("boost", Swim, "SHIFT", "LS", EXboxOrigin.k_EXboxOrigin_LeftStick_Click,
            new[] { "<Keyboard>/leftShift" }, "<Gamepad>/leftStickPress");
        public static readonly GameAction FreeLook = Button("free_look", Swim, "RMB", "LB", EXboxOrigin.k_EXboxOrigin_LeftBumper,
            new[] { "<Mouse>/rightButton" }, "<Gamepad>/leftShoulder");
        /// <summary>Controller only: the keyboard key is the camera's own (set in its Inspector).</summary>
        public static readonly GameAction FirstPerson = Button("first_person", Swim, "V", "RS", EXboxOrigin.k_EXboxOrigin_RightStick_Click,
            null, "<Gamepad>/rightStickPress");

        // ---- fish ----
        /// <summary>Eat, join or leave a reef puzzle, bite a rope, hold a task: the fish's "use".</summary>
        public static readonly GameAction Interact = Button("interact", new[] { InputSet.Fish, InputSet.Puzzle }, "E", "X", EXboxOrigin.k_EXboxOrigin_X,
            new[] { "<Keyboard>/e" }, "<Gamepad>/buttonWest");
        public static readonly GameAction FlockBlend = Button("flock_blend", new[] { InputSet.Fish }, "F", "Y", EXboxOrigin.k_EXboxOrigin_Y,
            new[] { "<Keyboard>/f" }, "<Gamepad>/buttonNorth");
        public static readonly GameAction TagTask = Button("tag_task", new[] { InputSet.Fish }, "G", "RB", EXboxOrigin.k_EXboxOrigin_RightBumper,
            new[] { "<Mouse>/middleButton", "<Keyboard>/g" }, "<Gamepad>/rightShoulder");

        // ---- shark ----
        public static readonly GameAction Attack = Button("attack", new[] { InputSet.Shark }, "LMB", "RT", EXboxOrigin.k_EXboxOrigin_RightTrigger_Pull,
            new[] { "<Mouse>/leftButton" }, "<Gamepad>/rightTrigger");
        public static readonly GameAction Lunge = Button("lunge", new[] { InputSet.Shark }, "Q", "LT", EXboxOrigin.k_EXboxOrigin_LeftTrigger_Pull,
            new[] { "<Keyboard>/q" }, "<Gamepad>/leftTrigger");
        public static readonly GameAction Sonar = Button("sonar", new[] { InputSet.Shark }, "R", "Y", EXboxOrigin.k_EXboxOrigin_Y,
            new[] { "<Keyboard>/r" }, "<Gamepad>/buttonNorth");
        public static readonly GameAction LockTarget = Button("lock_target", new[] { InputSet.Shark }, "F", "X", EXboxOrigin.k_EXboxOrigin_X,
            new[] { "<Keyboard>/f" }, "<Gamepad>/buttonWest");
        public static readonly GameAction CycleTarget = Button("cycle_target", new[] { InputSet.Shark }, "TAB", "RB", EXboxOrigin.k_EXboxOrigin_RightBumper,
            new[] { "<Keyboard>/tab" }, "<Gamepad>/rightShoulder");

        // ---- out of the round ----
        public static readonly GameAction SpectateNext = Button("spectate_next", new[] { InputSet.Spectator }, "LMB", "RB", EXboxOrigin.k_EXboxOrigin_RightBumper,
            new[] { "<Mouse>/leftButton" }, "<Gamepad>/rightShoulder");
        public static readonly GameAction SpectatePrevious = Button("spectate_previous", new[] { InputSet.Spectator }, "RMB", "LB", EXboxOrigin.k_EXboxOrigin_LeftBumper,
            new[] { "<Mouse>/rightButton" }, "<Gamepad>/leftShoulder");
        public static readonly GameAction GhostHint = Button("ghost_hint", new[] { InputSet.Spectator }, "G", "Y", EXboxOrigin.k_EXboxOrigin_Y,
            new[] { "<Keyboard>/g" }, "<Gamepad>/buttonNorth");

        // ---- reef puzzle (entering the arrows) ----
        public static readonly GameAction PuzzleUp = Button("puzzle_up", new[] { InputSet.Puzzle }, "W", "D-Pad Up", EXboxOrigin.k_EXboxOrigin_DPad_North,
            new[] { "<Keyboard>/w", "<Keyboard>/upArrow" }, "<Gamepad>/dpad/up", "<Gamepad>/leftStick/up");
        public static readonly GameAction PuzzleDown = Button("puzzle_down", new[] { InputSet.Puzzle }, "S", "D-Pad Down", EXboxOrigin.k_EXboxOrigin_DPad_South,
            new[] { "<Keyboard>/s", "<Keyboard>/downArrow" }, "<Gamepad>/dpad/down", "<Gamepad>/leftStick/down");
        public static readonly GameAction PuzzleLeft = Button("puzzle_left", new[] { InputSet.Puzzle }, "A", "D-Pad Left", EXboxOrigin.k_EXboxOrigin_DPad_West,
            new[] { "<Keyboard>/a", "<Keyboard>/leftArrow" }, "<Gamepad>/dpad/left", "<Gamepad>/leftStick/left");
        public static readonly GameAction PuzzleRight = Button("puzzle_right", new[] { InputSet.Puzzle }, "D", "D-Pad Right", EXboxOrigin.k_EXboxOrigin_DPad_East,
            new[] { "<Keyboard>/d", "<Keyboard>/rightArrow" }, "<Gamepad>/dpad/right", "<Gamepad>/leftStick/right");
        public static readonly GameAction PuzzleSubmit = Button("puzzle_submit", new[] { InputSet.Puzzle }, "ENTER", "A", EXboxOrigin.k_EXboxOrigin_A,
            new[] { "<Keyboard>/enter", "<Keyboard>/numpadEnter" }, "<Gamepad>/buttonSouth");
        public static readonly GameAction PuzzleDelete = Button("puzzle_delete", new[] { InputSet.Puzzle }, "BACKSPACE", "B", EXboxOrigin.k_EXboxOrigin_B,
            new[] { "<Keyboard>/backspace" }, "<Gamepad>/buttonEast");

        // ---- menus (the keyboard and mouse drive UI Toolkit themselves; these are the controller's) ----
        public static readonly GameAction MenuUp = Button("menu_up", new[] { InputSet.Menu }, "UP", "D-Pad Up", EXboxOrigin.k_EXboxOrigin_DPad_North,
            null, "<Gamepad>/dpad/up", "<Gamepad>/leftStick/up");
        public static readonly GameAction MenuDown = Button("menu_down", new[] { InputSet.Menu }, "DOWN", "D-Pad Down", EXboxOrigin.k_EXboxOrigin_DPad_South,
            null, "<Gamepad>/dpad/down", "<Gamepad>/leftStick/down");
        public static readonly GameAction MenuLeft = Button("menu_left", new[] { InputSet.Menu }, "LEFT", "D-Pad Left", EXboxOrigin.k_EXboxOrigin_DPad_West,
            null, "<Gamepad>/dpad/left", "<Gamepad>/leftStick/left");
        public static readonly GameAction MenuRight = Button("menu_right", new[] { InputSet.Menu }, "RIGHT", "D-Pad Right", EXboxOrigin.k_EXboxOrigin_DPad_East,
            null, "<Gamepad>/dpad/right", "<Gamepad>/leftStick/right");
        public static readonly GameAction MenuSelect = Button("menu_select", new[] { InputSet.Menu }, "ENTER", "A", EXboxOrigin.k_EXboxOrigin_A,
            null, "<Gamepad>/buttonSouth");
        public static readonly GameAction MenuCancel = Button("menu_cancel", new[] { InputSet.Menu }, "ESC", "B", EXboxOrigin.k_EXboxOrigin_B,
            null, "<Gamepad>/buttonEast");
        /// <summary>The lobby's ready toggle.</summary>
        public static readonly GameAction MenuReady = Button("menu_ready", new[] { InputSet.Menu }, "R", "X", EXboxOrigin.k_EXboxOrigin_X,
            new[] { "<Keyboard>/r" }, "<Gamepad>/buttonWest");

        // ---- everywhere ----
        public static readonly GameAction Pause = Button("pause_menu", Everywhere, "ESC", "Menu", EXboxOrigin.k_EXboxOrigin_Menu,
            new[] { "<Keyboard>/escape" }, "<Gamepad>/start");

        // ------------------------------------------------------------------ state

        /// <summary>The action set in force this frame.</summary>
        public static InputSet CurrentSet { get; private set; } = InputSet.Menu;

        /// <summary>True when the last thing the player touched was a controller: prompts show its glyphs.</summary>
        public static bool UsingPad { get; private set; }

        /// <summary>Goes up whenever prompts may need redrawing (device switched, layout rebound).</summary>
        public static int GlyphVersion { get; private set; }

        /// <summary>A controller the player was playing on has gone (unplugged, out of battery).</summary>
        public static event Action ControllerDisconnected;

        /// <summary>Unscaled time of the last <see cref="ControllerDisconnected"/>, for "controller disconnected" notes.</summary>
        public static float LastDisconnectTime { get; private set; } = -100f;

        static int s_bodyFrame = -10, s_puzzleFrame = -10;
        static bool s_bodyShark, s_bodyLocked;

        /// <summary>The action with that manifest name, or null.</summary>
        public static GameAction Find(string name)
        {
            foreach (var a in s_all)
                if (a.Name == name) return a;
            return null;
        }

        internal static bool InCurrentSet(GameAction a) => (a.Sets & (1 << (int)CurrentSet)) != 0;

        /// <summary>The local player's body says it's there, every frame (FishPlayer): which controls it has.</summary>
        public static void ReportLocalBody(bool shark, bool taskLocked)
        {
            s_bodyFrame = Time.frameCount;
            s_bodyShark = shark;
            s_bodyLocked = taskLocked;
        }

        /// <summary>A puzzle is taking the arrows this frame (the older single-scene task).</summary>
        public static void ReportPuzzle() => s_puzzleFrame = Time.frameCount;

        /// <summary>One of the four puzzle arrows, the frame it's pressed.</summary>
        public static bool PuzzleArrowPressed(out Vector2 direction)
        {
            direction = PuzzleUp.WasPressedThisFrame() ? Vector2.up
                      : PuzzleDown.WasPressedThisFrame() ? Vector2.down
                      : PuzzleLeft.WasPressedThisFrame() ? Vector2.left
                      : PuzzleRight.WasPressedThisFrame() ? Vector2.right
                      : Vector2.zero;
            return direction != Vector2.zero;
        }

        /// <summary>Shake the controller (0-1 each motor). Call with zeros to stop.</summary>
        public static void Rumble(float low, float high)
        {
            if (SteamInputService.Native) SteamInputService.Vibrate(low, high);
            else
            {
                var pad = Gamepad.current;
                if (pad == null) return;
                if (low <= 0f && high <= 0f) pad.ResetHaptics();
                else pad.SetMotorSpeeds(Mathf.Clamp01(low), Mathf.Clamp01(high));
            }
        }

        /// <summary>True if there's any controller to shake.</summary>
        public static bool HasPad => SteamInputService.ControllerCount > 0 || Gamepad.current != null;

        internal static void BumpGlyphs() => GlyphVersion++;

        // ------------------------------------------------------------------ per frame

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            CurrentSet = InputSet.Menu;
            UsingPad = false;
            LastDisconnectTime = -100f;
            s_bodyFrame = s_puzzleFrame = -10;
            ControllerDisconnected = null;
            foreach (var a in s_all) { a.SteamNow = a.SteamPrev = false; a.SteamVec = Vector2.zero; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("GameInput");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GameInputRunner>();
        }

        internal static void Tick()
        {
            CurrentSet = ResolveSet();
            SteamInputService.Tick(CurrentSet);

            // What are they holding? A controller once it does anything; keyboard / mouse once those do.
            bool pad = SteamInputService.ActivityThisFrame;
            if (!pad)
                foreach (var a in s_all)
                    if (a.UnityPadActuated) { pad = true; break; }
            if (pad) SetUsingPad(true);
            else if (UsingPad && KeyboardOrMouseUsed()) SetUsingPad(false);

            UiPad.Tick();
            InputGlyphs.Tick();
        }

        static InputSet ResolveSet()
        {
            if (UiPad.HasActiveScope || PauseMenu.IsOpen || RoundScreen.ShowingResults) return InputSet.Menu;
            if (Time.frameCount - s_bodyFrame > 1) return InputSet.Menu;   // no body of ours: a menu scene
            if (Spectator.Active != null && Spectator.Active.isActiveAndEnabled) return InputSet.Spectator;
            if (s_bodyLocked || Time.frameCount - s_puzzleFrame <= 1) return InputSet.Puzzle;
            return s_bodyShark ? InputSet.Shark : InputSet.Fish;
        }

        static bool KeyboardOrMouseUsed()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            var m = Mouse.current;
            if (m == null) return false;
            return m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame
                || m.delta.ReadValue().sqrMagnitude > 16f;
        }

        internal static void SetUsingPad(bool pad)
        {
            if (UsingPad == pad) return;
            UsingPad = pad;
            GlyphVersion++;
        }

        /// <summary>A controller went away (from Steam Input, or the Input System when Steam isn't driving).</summary>
        internal static void OnControllerLost()
        {
            if (Time.unscaledTime - LastDisconnectTime < 1f) return;   // Steam and the Input System both tell us
            if (!UsingPad) return;                                     // they weren't playing on it
            LastDisconnectTime = Time.unscaledTime;
            ControllerDisconnected?.Invoke();
        }

        // ------------------------------------------------------------------ building the actions

        static GameAction Button(string name, InputSet[] sets, string key, string padLabel, EXboxOrigin xbox, string[] kbm, params string[] pad)
        {
            InputAction k = null, p = null;
            if (kbm != null && kbm.Length > 0)
            {
                k = new InputAction(name, InputActionType.Button);
                foreach (var b in kbm) k.AddBinding(b);
            }
            if (pad != null && pad.Length > 0)
            {
                p = new InputAction(name + "_pad", InputActionType.Button);
                foreach (var b in pad) p.AddBinding(b);
            }
            var a = new GameAction(name, false, sets, key, padLabel, xbox, k, p);
            s_all.Add(a);
            return a;
        }

        static GameAction Stick(string name, InputSet[] sets, string key, string padLabel, EXboxOrigin xbox, InputAction kbm, string pad)
        {
            var p = new InputAction(name + "_pad", InputActionType.Value, expectedControlType: "Vector2");
            p.AddBinding(pad);
            var a = new GameAction(name, true, sets, key, padLabel, xbox, kbm, p);
            s_all.Add(a);
            return a;
        }

        static InputAction Wasd()
        {
            var a = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            a.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            return a;
        }

        static InputAction Bound(InputAction a, string binding)
        {
            a.AddBinding(binding);
            return a;
        }
    }

    /// <summary>Runs <see cref="GameInput"/> ahead of every other script each frame, so they all see this
    /// frame's input, and watches the Input System for a gamepad going away.</summary>
    [DefaultExecutionOrder(-10000)]
    sealed class GameInputRunner : MonoBehaviour
    {
        void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;
        void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        void Update() => GameInput.Tick();
        void LateUpdate() => UiPad.LateTick();

        static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Gamepad)) return;
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
                GameInput.OnControllerLost();
            GameInput.BumpGlyphs();
        }
    }
}
