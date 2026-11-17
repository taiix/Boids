using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// Hiding in the sand - this machine's fish only. With the shark about and your fish down on the seabed,
    /// hold Descend (C) to bury yourself: the fish sinks into the sand and the shark can't see or lock it
    /// (<see cref="FishPlayer.IsBuried"/>). Stay calm: tap Space on the heartbeat. An off-beat tap is a
    /// twitch - a ripple the shark hears (<see cref="SharkTargeting.HearNoise"/>), and for a moment it can
    /// lock you. Missed beats and twitches cost breath; breath also runs out on its own, and at zero you
    /// burst out (noisily). E, W / S or the stick come out quietly.
    ///
    /// Spawns itself in any scene with a seabed.
    /// </summary>
    public class SandHide : MonoBehaviour
    {
        const float HoldToBury = 0.7f;        // seconds holding C on the seabed
        const float GroundReach = 1.4f;       // how close to the sand counts as "on the seabed" (m)
        const float SharkNear = 35f;          // the prompt only shows with the shark this close
        const float BeatInterval = 1.1f;
        const float BeatWindow = 0.17f;       // seconds either side of a beat that count as on it
        const float BreathSeconds = 28f;      // breath runs out on its own after this long
        const float MissedBeatCost = 0.05f, TwitchCost = 0.04f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded += (_, __) => Ensure();
            Ensure();
        }

        static void Ensure()
        {
            if (FindAnyObjectByType<SandHide>() != null || FindAnyObjectByType<TestTerrain>() == null) return;
            new GameObject("SandHide").AddComponent<SandHide>();
        }

        /// <summary>Testing: allow hiding with no shark about (the offline sandbox has none once you swap to a fish).</summary>
        public static bool RequireShark = true;

        /// <summary>This machine's fish is buried right now.</summary>
        public static bool LocalBuried { get; private set; }

        /// <summary>Testing (Tools > Reef Run > Minigames): dig this fish in right now, shark or no shark.
        /// It has to be on the seabed; returns false if it isn't.</summary>
        public static bool DebugBuryNow(FishPlayer fish)
        {
            var hide = FindAnyObjectByType<SandHide>();
            if (hide == null || fish == null || LocalBuried) return false;
            hide._fish = fish;
            if (!hide.OnSeabed(out _)) return false;
            hide.Bury();
            return LocalBuried;
        }

        FishPlayer _fish;
        MeshCollider _seabed;
        float _hold, _breath, _beatClock, _lastBeatHandled = -1f, _flash, _enteredAt;
        bool _flashGood, _tappedThisBeat, _descendReleased;
        Vector3 _restPos;
        Quaternion _restRot;
        Heartbeat _beat;
        BreathBubbles _bubbles;
        static InputAction s_tap;
        int _twitches, _missedBeats;      // this hide, for MatchLog

        void Update()
        {
            if (_fish == null || !_fish.isActiveAndEnabled) _fish = FindLocalFish();
            if (_fish == null) { if (LocalBuried) Unbury(false); return; }

            if (LocalBuried) { TickBuried(); return; }

            // Not buried: hold C on the seabed (with the shark about) to dig in.
            bool canHide = !MinigameHud.IsOpen && !TrashMinigameUI.IsOpen && !_fish.IsOut && !_fish.IsBeingEaten
                           && !PauseMenu.IsOpen && OnSeabed(out _) && (!RequireShark || SharkClose());
            if (!canHide) { _hold = 0f; return; }

            if (GameInput.Descend.IsPressed())
            {
                _hold += Time.deltaTime;
                MatchHud.ShowProgress("Burying", Mathf.Clamp01(_hold / HoldToBury));
                if (_hold >= HoldToBury) Bury();
            }
            else
            {
                _hold = 0f;
                MatchHud.ShowPrompt($"Hold {GameInput.Descend.Token} to hide in the sand");
            }
        }

        bool SharkClose()
        {
            foreach (var fp in FishPlayer.All)
                if (fp != null && fp != _fish && fp.IsSharkBody && fp.isActiveAndEnabled &&
                    (fp.transform.position - _fish.transform.position).sqrMagnitude < SharkNear * SharkNear) return true;
            return false;
        }

        bool OnSeabed(out Vector3 ground)
        {
            ground = default;
            if (_seabed == null)
            {
                var terrain = FindAnyObjectByType<TestTerrain>();
                if (terrain == null || !terrain.TryGetComponent(out _seabed)) return false;
            }
            Vector3 p = _fish.transform.position;
            if (!_seabed.Raycast(new Ray(p + Vector3.up * 2f, Vector3.down), out var hit, 2f + GroundReach)) return false;
            ground = hit.point;
            return true;
        }

        void Bury()
        {
            if (!OnSeabed(out var ground)) return;
            var ui = MinigameHud.Open("Hide");
            if (ui == null) return;
            LocalBuried = true;
            _hold = 0f;
            _breath = 1f;
            _beatClock = 0f;
            _lastBeatHandled = -1f;
            _tappedThisBeat = false;
            _descendReleased = false;
            _enteredAt = Time.unscaledTime;
            _twitches = _missedBeats = 0;
            MatchLog.Write("start", "Hide");
            _beat = ui.Q<Heartbeat>("beat");
            _bubbles = ui.Q<BreathBubbles>("breath");
            var key = ui.Q<Label>("tap-key");
            if (key != null) key.text = GameInput.UsingPad ? GameInput.PuzzleSubmit.PadLabel : "SPACE";
            var leave = ui.Q<Label>("leave-key");
            if (leave != null) leave.text = MinigameHud.KeyText(GameInput.Interact);
            MinigameHud.SetHint("The shark can't see you here. A tap off the beat, and it hears you");

            _fish.SetTaskLocked(true);
            _fish.SetBuried(true);
            Vector3 flatFwd = Vector3.ProjectOnPlane(_fish.transform.forward, Vector3.up);
            if (flatFwd.sqrMagnitude < 1e-3f) flatFwd = Vector3.forward;
            _restRot = Quaternion.LookRotation(flatFwd.normalized, Vector3.up);
            _restPos = ground + Vector3.up * 0.12f;   // half in the sand
            WaterFx.BubbleBurst(ground + Vector3.up * 0.3f, 18, 0.6f, 0.8f, new Vector2(0.02f, 0.05f));
            WaterFx.Shockwave(ground + Vector3.up * 0.1f, 1.6f, 0.4f);
        }

        void Unbury(bool loud)
        {
            if (!LocalBuried) return;
            MatchLog.Write(loud ? "gasped" : "left", "Hide", Time.unscaledTime - _enteredAt,
                $"twitches={_twitches} missed_beats={_missedBeats} breath={_breath:0.00}");
            LocalBuried = false;
            MinigameHud.Close();
            if (_fish != null)
            {
                _fish.SetBuried(false);
                _fish.SetTaskLocked(false);
                _fish.transform.position += Vector3.up * 0.5f;
                if (loud) _fish.MakeNoise(0.5f);
                WaterFx.BubbleBurst(_fish.transform.position, loud ? 24 : 10, 0.6f, 1f, new Vector2(0.02f, 0.05f));
            }
        }

        void TickBuried()
        {
            if (_fish.IsOut || _fish.IsBeingEaten) { Unbury(false); return; }
            // C dug us in: it only counts as "come out" once it's been let go.
            if (!GameInput.Descend.IsPressed()) _descendReleased = true;
            Vector2 keys = GameInput.Move.ReadPointer();
            var pad = Gamepad.current;
            bool moving = keys.sqrMagnitude > 0.2f || (pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.25f)
                          || (_descendReleased && GameInput.Descend.IsPressed());
            bool leave = Time.unscaledTime - _enteredAt > 0.4f && (GameInput.Interact.WasPressedThisFrame() || moving);
            if (leave) { Unbury(false); return; }

            GameInput.ReportPuzzle();
            float dt = PauseMenu.IsOpen ? 0f : Time.deltaTime;

            // settle into the sand
            _fish.transform.SetPositionAndRotation(
                Vector3.Lerp(_fish.transform.position, _restPos, 1f - Mathf.Exp(-6f * Time.deltaTime)),
                Quaternion.Slerp(_fish.transform.rotation, _restRot, 1f - Mathf.Exp(-6f * Time.deltaTime)));

            // the heartbeat
            _beatClock += dt;
            float phase = _beatClock / BeatInterval;          // beats at whole numbers
            int beat = Mathf.RoundToInt(phase);
            float offBy = Mathf.Abs(phase - beat) * BeatInterval;   // seconds from the nearest beat

            // a beat that went by untapped
            int passed = Mathf.FloorToInt(phase - BeatWindow / BeatInterval);
            if (passed > _lastBeatHandled && passed >= 1)
            {
                if (!_tappedThisBeat) { _breath -= MissedBeatCost; _missedBeats++; Flash(false); }
                _lastBeatHandled = passed;
                _tappedThisBeat = false;
            }

            if (s_tap == null)
            {
                s_tap = new InputAction("hide_tap", InputActionType.Button);
                s_tap.AddBinding("<Keyboard>/space");
                s_tap.AddBinding("<Gamepad>/buttonSouth");
                s_tap.Enable();
            }
            if (dt > 0f && (s_tap.WasPressedThisFrame() || GameInput.PuzzleSubmit.WasPressedThisFrame()))
            {
                if (offBy <= BeatWindow && beat > _lastBeatHandled && !_tappedThisBeat)
                {
                    _tappedThisBeat = true;
                    _breath = Mathf.Min(1f, _breath + 0.01f);
                    Flash(true);
                }
                else
                {
                    // a twitch: the shark hears it
                    _breath -= TwitchCost;
                    _twitches++;
                    Flash(false);
                    MinigameHud.Flash("TWITCH!", false);
                    _fish.MakeNoise(1.5f);
                    WaterFx.Shockwave(_fish.transform.position, 1.2f, 0.35f);
                }
            }

            _breath -= dt / BreathSeconds;
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 3f);
            if (_beat != null)
            {
                _beat.phase = phase - Mathf.Floor(phase);
                _beat.interval = BeatInterval;
                _beat.flash = _flash;
                _beat.flashGood = _flashGood;
                _beat.MarkDirtyRepaint();
            }
            if (_bubbles != null) { _bubbles.breath = _breath; _bubbles.MarkDirtyRepaint(); }

            if (_breath <= 0f)
            {
                MinigameHud.Flash("GASP!", false);
                Unbury(true);
            }
        }

        void Flash(bool good) { _flash = 1f; _flashGood = good; }

        static FishPlayer FindLocalFish()
        {
            foreach (var p in TrashPusher.All)
                if (p != null && p.IsLocalFish && p.TryGetComponent(out FishPlayer fish)) return fish;
            return null;
        }

        void OnDestroy() { if (LocalBuried) Unbury(false); }
    }
}
