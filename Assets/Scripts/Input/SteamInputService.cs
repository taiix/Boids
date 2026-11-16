using System;
using System.Collections.Generic;
using System.IO;
using Steamworks;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// The game's side of the Steam Input API. Steam turns whatever controller is plugged in (Xbox,
    /// PlayStation, Switch, Steam Deck, Steam Controller, anything it adds later) into the actions named in
    /// the manifest (StreamingAssets/SteamInput/steam_input_manifest.vdf), on the layout the player chose in
    /// Steam's configurator. This:
    ///   * starts Steam Input once SteamManager is up and hands it the manifest,
    ///   * activates the action set <see cref="GameInput"/> asks for and reads every action each frame,
    ///   * notices controllers arriving and leaving,
    ///   * answers "which button is this action on?" with Steam's own glyph image or button name,
    ///   * opens Steam's binding screen and its on-screen keyboard, and shakes the controller.
    ///
    /// A controller on a layout without our actions (the player picked a plain gamepad template) isn't
    /// <see cref="Native"/>: the Input System reads it as a gamepad instead, and glyphs come from the Xbox
    /// button each action defaults to, translated by Steam to the real controller.
    /// </summary>
    public static class SteamInputService
    {
        public const string ManifestFolder = "SteamInput";
        public const string ManifestFile = "steam_input_manifest.vdf";

        static readonly string[] SetNames = { "MenuControls", "FishControls", "SharkControls", "SpectatorControls", "PuzzleControls" };

        /// <summary>Steam Input is running.</summary>
        public static bool Ready { get; private set; }

        /// <summary>Controllers Steam Input sees.</summary>
        public static int ControllerCount { get; private set; }

        /// <summary>At least one of them is on a layout that binds our actions: Steam delivers the input.</summary>
        public static bool Native { get; private set; }

        /// <summary>Some Steam action was pressed or moved this frame.</summary>
        public static bool ActivityThisFrame { get; private set; }

        static readonly InputHandle_t[] s_handles = new InputHandle_t[Constants.STEAM_INPUT_MAX_COUNT];
        static readonly List<ulong> s_known = new List<ulong>();
        static readonly List<ulong> s_now = new List<ulong>();
        static readonly InputActionSetHandle_t[] s_sets = new InputActionSetHandle_t[SetNames.Length];
        static readonly EInputActionOrigin[] s_origins = new EInputActionOrigin[Constants.STEAM_INPUT_MAX_ORIGINS];
        static readonly Dictionary<string, Texture2D> s_textures = new Dictionary<string, Texture2D>();

        static InputHandle_t s_active;          // the controller last used
        static bool s_handlesResolved, s_initFailed;
        static float s_nextResolve, s_nextNativeCheck;
        static InputSet s_lastSet;
        static int s_quietUntilFrame;
        static bool s_vibrating;

        static Callback<SteamInputConfigurationLoaded_t> s_configLoaded;
        static Callback<GamepadTextInputDismissed_t> s_textDismissed;
        static Action<string> s_textDone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Ready = Native = ActivityThisFrame = false;
            ControllerCount = 0;
            s_known.Clear();
            s_handlesResolved = s_initFailed = s_vibrating = false;
            s_nextResolve = s_nextNativeCheck = 0f;
            s_active = default;
            s_configLoaded = null;
            s_textDismissed = null;
            s_textDone = null;
        }

        // ------------------------------------------------------------------ start / stop

        static void TryInit()
        {
            if (s_initFailed || !SteamManager.IsReady) return;   // never wake Steam ourselves: Bootstrap owns it
            try
            {
                // true: we call RunFrame ourselves, first thing each frame, so input is never a frame stale.
                if (!SteamInput.Init(true))
                {
                    s_initFailed = true;
                    Debug.LogWarning("[SteamInput] Init failed - controllers fall back to the Input System.");
                    return;
                }

                // The actions and the default layouts ship with the game.
                string manifest = Path.Combine(Application.streamingAssetsPath, ManifestFolder, ManifestFile);
                if (File.Exists(manifest))
                {
                    if (!SteamInput.SetInputActionManifestFilePath(manifest))
                        Debug.LogWarning($"[SteamInput] Steam didn't take the action manifest at {manifest}.");
                }
                else Debug.LogWarning($"[SteamInput] No action manifest at {manifest}.");

                s_configLoaded = Callback<SteamInputConfigurationLoaded_t>.Create(_ =>
                {
                    // A layout was loaded or edited: ask for the handles and the glyphs again.
                    s_handlesResolved = false;
                    s_nextResolve = s_nextNativeCheck = 0f;
                    GameInput.BumpGlyphs();
                });
                s_textDismissed = Callback<GamepadTextInputDismissed_t>.Create(OnTextDismissed);
                Ready = true;
            }
            catch (Exception e)
            {
                s_initFailed = true;
                Debug.LogWarning("[SteamInput] Not available: " + e.Message);
            }
        }

        /// <summary>Called by SteamManager just before it shuts the Steam API down.</summary>
        public static void Shutdown()
        {
            if (!Ready) return;
            Ready = Native = false;
            ControllerCount = 0;
            s_configLoaded?.Dispose();
            s_textDismissed?.Dispose();
            s_configLoaded = null;
            s_textDismissed = null;
            try { SteamInput.Shutdown(); } catch (Exception) { /* Steam already gone */ }
        }

        // ------------------------------------------------------------------ per frame

        internal static void Tick(InputSet set)
        {
            ActivityThisFrame = false;
            if (!Ready) { TryInit(); if (!Ready) return; }
            if (!SteamManager.IsReady) { ClearActions(); return; }

            SteamInput.RunFrame();
            ControllerCount = SteamInput.GetConnectedControllers(s_handles);
            TrackControllers();
            if (ControllerCount == 0) { Native = false; ClearActions(); return; }

            float now = Time.unscaledTime;
            if (!s_handlesResolved && now >= s_nextResolve) { ResolveHandles(); s_nextResolve = now + 1f; }
            if (now >= s_nextNativeCheck) { CheckNative(); s_nextNativeCheck = now + 1f; }

            // Cheap, and safe to repeat every frame (Valve's own advice): keeps a controller that just
            // arrived on the right set too.
            var setHandle = s_sets[(int)set];
            if (setHandle.m_InputActionSetHandle != 0)
                for (int i = 0; i < ControllerCount; i++) SteamInput.ActivateActionSet(s_handles[i], setHandle);

            // A button held across a change of set would otherwise count as a fresh press in the new one
            // (B backs out of a menu and the fish dives).
            if (set != s_lastSet) { s_lastSet = set; s_quietUntilFrame = Time.frameCount + 2; }
            bool quiet = Time.frameCount <= s_quietUntilFrame;

            foreach (var a in GameInput.All)
            {
                bool pressed = false;
                Vector2 vec = Vector2.zero;
                for (int i = 0; i < ControllerCount; i++)
                {
                    bool used = false;
                    if (a.Analog)
                    {
                        if (a.AnalogHandle.m_InputAnalogActionHandle == 0) break;
                        var d = SteamInput.GetAnalogActionData(s_handles[i], a.AnalogHandle);
                        if (d.bActive == 0) continue;
                        var v = new Vector2(d.x, d.y);
                        if (v.sqrMagnitude > vec.sqrMagnitude) vec = v;
                        used = v.sqrMagnitude > 0.16f;
                    }
                    else
                    {
                        if (a.DigitalHandle.m_InputDigitalActionHandle == 0) break;
                        var d = SteamInput.GetDigitalActionData(s_handles[i], a.DigitalHandle);
                        if (d.bActive == 0 || d.bState == 0) continue;
                        pressed = used = true;
                    }
                    if (used) { s_active = s_handles[i]; ActivityThisFrame = true; }
                }
                a.SteamPrev = quiet ? pressed : a.SteamNow;
                a.SteamNow = pressed;
                a.SteamVec = vec;
            }
        }

        static void ClearActions()
        {
            foreach (var a in GameInput.All) { a.SteamPrev = a.SteamNow; a.SteamNow = false; a.SteamVec = Vector2.zero; }
        }

        // Which controllers came and went since last frame.
        static void TrackControllers()
        {
            s_now.Clear();
            for (int i = 0; i < ControllerCount; i++) s_now.Add(s_handles[i].m_InputHandle);

            bool changed = false, lost = false;
            foreach (ulong h in s_known)
                if (!s_now.Contains(h)) { changed = lost = true; if (s_active.m_InputHandle == h) s_active = default; }
            foreach (ulong h in s_now)
                if (!s_known.Contains(h)) changed = true;
            if (!changed) return;

            s_known.Clear();
            s_known.AddRange(s_now);
            s_handlesResolved = false;
            s_nextResolve = s_nextNativeCheck = 0f;
            if (s_now.Count == 0) s_vibrating = false;
            GameInput.BumpGlyphs();
            if (lost) GameInput.OnControllerLost();
        }

        // Handles come back as 0 until Steam has read the manifest, so this is retried until they're all there.
        static void ResolveHandles()
        {
            bool all = true;
            for (int i = 0; i < SetNames.Length; i++)
            {
                s_sets[i] = SteamInput.GetActionSetHandle(SetNames[i]);
                all &= s_sets[i].m_InputActionSetHandle != 0;
            }
            foreach (var a in GameInput.All)
            {
                if (a.Analog)
                {
                    a.AnalogHandle = SteamInput.GetAnalogActionHandle(a.Name);
                    all &= a.AnalogHandle.m_InputAnalogActionHandle != 0;
                }
                else
                {
                    a.DigitalHandle = SteamInput.GetDigitalActionHandle(a.Name);
                    all &= a.DigitalHandle.m_InputDigitalActionHandle != 0;
                }
            }
            s_handlesResolved = all;
        }

        // A layout is "ours" when it puts the menu's select button somewhere. A gamepad-emulation layout
        // doesn't, and then the Input System sees an ordinary gamepad.
        static void CheckNative()
        {
            bool native = false;
            var menu = s_sets[(int)InputSet.Menu];
            var select = GameInput.MenuSelect.DigitalHandle;
            if (menu.m_InputActionSetHandle != 0 && select.m_InputDigitalActionHandle != 0)
                for (int i = 0; i < ControllerCount && !native; i++)
                    native = SteamInput.GetDigitalActionOrigins(s_handles[i], menu, select, s_origins) > 0;
            if (native == Native) return;
            Native = native;
            GameInput.BumpGlyphs();
        }

        static bool TryController(out InputHandle_t handle)
        {
            handle = default;
            if (!Ready || ControllerCount == 0) return false;
            handle = s_handles[0];
            for (int i = 0; i < ControllerCount; i++)
                if (s_handles[i].m_InputHandle == s_active.m_InputHandle) { handle = s_handles[i]; break; }
            return true;
        }

        // ------------------------------------------------------------------ glyphs

        // The button an action is on, on the controller in use: from the player's layout when it binds our
        // actions, otherwise the action's default Xbox button translated to that controller.
        static EInputActionOrigin OriginOf(GameAction action, InputSet set)
        {
            if (!TryController(out var pad)) return EInputActionOrigin.k_EInputActionOrigin_None;
            var setHandle = s_sets[(int)set];
            if (Native && setHandle.m_InputActionSetHandle != 0)
            {
                int n = action.Analog
                    ? (action.AnalogHandle.m_InputAnalogActionHandle != 0 ? SteamInput.GetAnalogActionOrigins(pad, setHandle, action.AnalogHandle, s_origins) : 0)
                    : (action.DigitalHandle.m_InputDigitalActionHandle != 0 ? SteamInput.GetDigitalActionOrigins(pad, setHandle, action.DigitalHandle, s_origins) : 0);
                if (n > 0 && s_origins[0] != EInputActionOrigin.k_EInputActionOrigin_None) return s_origins[0];
            }
            return SteamInput.GetActionOriginFromXboxOrigin(pad, action.Xbox);
        }

        /// <summary>Steam's picture of the button this action is on, or null (no Steam, no controller).</summary>
        public static Texture2D Glyph(GameAction action, InputSet set)
        {
            var origin = OriginOf(action, set);
            if (origin == EInputActionOrigin.k_EInputActionOrigin_None) return null;
            string path = SteamInput.GetGlyphPNGForActionOrigin(origin, ESteamInputGlyphSize.k_ESteamInputGlyphSize_Medium, 0);
            if (string.IsNullOrEmpty(path)) return null;
            if (s_textures.TryGetValue(path, out var cached) && cached != null) return cached;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(path), wrapMode = TextureWrapMode.Clamp };
                if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
                return s_textures[path] = tex;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Steam's name for the button this action is on ("A Button", "Right Trigger"), or null.</summary>
        public static string ButtonName(GameAction action, InputSet set)
        {
            var origin = OriginOf(action, set);
            if (origin == EInputActionOrigin.k_EInputActionOrigin_None) return null;
            string name = SteamInput.GetStringForActionOrigin(origin);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        // ------------------------------------------------------------------ Steam's own screens

        /// <summary>Open Steam's controller layout screen for the controller in use, where every action can
        /// be rebound. False if Steam Input or its overlay isn't there.</summary>
        public static bool ShowBindingPanel() => TryController(out var pad) && SteamInput.ShowBindingPanel(pad);

        /// <summary>
        /// Bring up Steam's on-screen keyboard for a text box. <paramref name="done"/> gets the text when the
        /// full-screen keyboard is accepted; the floating one (used when that isn't available) types straight
        /// into the focused box instead. False if neither can show.
        /// </summary>
        public static bool ShowKeyboard(string description, string existing, int maxChars, RectInt fieldOnScreen, Action<string> done)
        {
            if (!SteamManager.IsReady) return false;
            if (SteamUtils.ShowGamepadTextInput(EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                    EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine, description ?? "", (uint)maxChars, existing ?? ""))
            {
                s_textDone = done;
                return true;
            }
            s_textDone = null;
            return SteamUtils.ShowFloatingGamepadTextInput(EFloatingGamepadTextInputMode.k_EFloatingGamepadTextInputModeModeSingleLine,
                fieldOnScreen.x, fieldOnScreen.y, fieldOnScreen.width, fieldOnScreen.height);
        }

        static void OnTextDismissed(GamepadTextInputDismissed_t e)
        {
            var done = s_textDone;
            s_textDone = null;
            if (done == null || !e.m_bSubmitted) return;
            if (SteamUtils.GetEnteredGamepadTextInput(out string text, e.m_unSubmittedText + 1))
                done(text ?? "");
        }

        // ------------------------------------------------------------------ rumble

        internal static void Vibrate(float low, float high)
        {
            if (!TryController(out var pad)) return;
            bool on = low > 0f || high > 0f;
            if (!on && !s_vibrating) return;
            s_vibrating = on;
            SteamInput.TriggerVibration(pad, (ushort)(Mathf.Clamp01(low) * ushort.MaxValue), (ushort)(Mathf.Clamp01(high) * ushort.MaxValue));
        }
    }
}
