using System.Collections.Generic;
using FishGame;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReefRun
{
    /// <summary>
    /// Drives the settings overlay (ReefRunSettings.uxml): audio sliders, display dropdowns,
    /// fullscreen/vsync toggles and the microphone section (voice chat on/off, a live mic check
    /// with the open-mic sensitivity, voice volume, "hear myself" and a link to Steam's own voice
    /// settings, where the input device is chosen). All values persist to PlayerPrefs on Apply.
    /// Shared by the main menu and the in-game pause menu, so both edit the same settings.
    /// </summary>
    public class ReefRunSettingsPanel
    {
        // PlayerPrefs keys
        const string K_MASTER = "rr_vol_master";
        const string K_MUSIC = "rr_vol_music";
        const string K_SFX = "rr_vol_sfx";
        const string K_QUALITY = "rr_quality";
        const string K_RES_IDX = "rr_res_idx";
        const string K_FULLSCREEN = "rr_fullscreen";
        const string K_VSYNC = "rr_vsync";

        readonly VisualElement _overlay;
        readonly Slider _masterSlider, _musicSlider, _sfxSlider;
        readonly Label _masterVal, _musicVal, _sfxVal;
        readonly DropdownField _resDropdown, _qualityDropdown;
        readonly Toggle _fullscreenToggle, _vsyncToggle;
        Resolution[] _resolutions;

        // microphone
        readonly Toggle _micToggle, _loopToggle;
        readonly Slider _thresholdSlider, _voiceVolSlider;
        readonly Label _thresholdVal, _voiceVolVal, _micDb, _micStatusText;
        readonly VisualElement _meterFill, _meterMark, _statusDot, _loopRow;
        readonly MicTester _mic = new();
        IVisualElementScheduledItem _micPoll;
        const string SteamVoiceSettingsUrl = "steam://settings/voice";

        /// <summary>True from Open until the close fade has finished.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Raised once the overlay has faded out (after ✕, Apply or Close).</summary>
        public event System.Action Closed;

        /// <summary>Bind to a tree containing the settings overlay (queried by element name).</summary>
        public ReefRunSettingsPanel(VisualElement root)
        {
            _overlay = root.Q<VisualElement>("settings-overlay");
            _masterSlider = root.Q<Slider>("master-slider");
            _musicSlider = root.Q<Slider>("music-slider");
            _sfxSlider = root.Q<Slider>("sfx-slider");
            _masterVal = root.Q<Label>("master-val");
            _musicVal = root.Q<Label>("music-val");
            _sfxVal = root.Q<Label>("sfx-val");
            _resDropdown = root.Q<DropdownField>("resolution-dropdown");
            _qualityDropdown = root.Q<DropdownField>("quality-dropdown");
            _fullscreenToggle = root.Q<Toggle>("fullscreen-toggle");
            _vsyncToggle = root.Q<Toggle>("vsync-toggle");

            _micToggle = root.Q<Toggle>("mic-toggle");
            _loopToggle = root.Q<Toggle>("mic-loopback");
            _loopRow = root.Q<VisualElement>("mic-loopback-row");
            _thresholdSlider = root.Q<Slider>("mic-threshold");
            _voiceVolSlider = root.Q<Slider>("voice-volume");
            _thresholdVal = root.Q<Label>("mic-threshold-val");
            _voiceVolVal = root.Q<Label>("voice-volume-val");
            _micDb = root.Q<Label>("mic-db");
            _micStatusText = root.Q<Label>("mic-status-text");
            _meterFill = root.Q<VisualElement>("mic-meter-fill");
            _meterMark = root.Q<VisualElement>("mic-meter-mark");
            _statusDot = root.Q<VisualElement>("mic-status-dot");

            _thresholdSlider?.RegisterValueChangedCallback(e => ShowThreshold(e.newValue));
            _voiceVolSlider?.RegisterValueChangedCallback(e => _voiceVolVal.text = Pct(e.newValue));
            _loopToggle?.RegisterValueChangedCallback(e =>
            {
                _mic.SetLoopback(e.newValue);
                if (e.newValue && !_mic.LoopbackOn) _loopToggle.SetValueWithoutNotify(false);
            });
            var steam = root.Q<Button>("mic-steam");
            if (steam != null) steam.clicked += () => Application.OpenURL(SteamVoiceSettingsUrl);

            var close = root.Q<Button>("settings-close");
            if (close != null) close.clicked += Close;
            var apply = root.Q<Button>("settings-apply");
            if (apply != null) apply.clicked += Apply;

            // live percentage labels while dragging (no audio change until Apply)
            _masterSlider?.RegisterValueChangedCallback(e =>
            {
                _masterVal.text = Pct(e.newValue);
                AudioListener.volume = e.newValue; // live audio preview
            });
            _musicSlider?.RegisterValueChangedCallback(e => _musicVal.text = Pct(e.newValue));
            _sfxSlider?.RegisterValueChangedCallback(e => _sfxVal.text = Pct(e.newValue));

            PopulateDropdowns();
            ApplySavedToControls(); // populate controls from PlayerPrefs
        }

        void PopulateDropdowns()
        {
            // populate resolution dropdown from Screen.resolutions
            _resolutions = Screen.resolutions;
            if (_resDropdown != null)
            {
                if (_resolutions != null && _resolutions.Length > 0)
                {
                    var choices = new List<string>(_resolutions.Length);
                    int fallback = 0;
                    for (int i = 0; i < _resolutions.Length; i++)
                    {
                        var r = _resolutions[i];
                        choices.Add($"{r.width} × {r.height}  {(int)r.refreshRateRatio.value}Hz");
                        if (r.width == Screen.width && r.height == Screen.height)
                            fallback = i;
                    }
                    _resDropdown.choices = choices;
                    _resDropdown.index = Mathf.Clamp(
                        PlayerPrefs.GetInt(K_RES_IDX, fallback), 0, _resolutions.Length - 1);
                }
                else
                {
                    _resDropdown.choices = new List<string> { $"{Screen.width} × {Screen.height}" };
                    _resDropdown.index = 0;
                }
            }

            // populate quality dropdown from Unity quality levels
            if (_qualityDropdown != null)
            {
                _qualityDropdown.choices = new List<string>(QualitySettings.names);
                _qualityDropdown.index = Mathf.Clamp(
                    PlayerPrefs.GetInt(K_QUALITY, QualitySettings.GetQualityLevel()),
                    0, QualitySettings.names.Length - 1);
            }
        }

        // Restore saved values into the slider/toggle controls.
        void ApplySavedToControls()
        {
            float master = PlayerPrefs.GetFloat(K_MASTER, 1f);
            float music = PlayerPrefs.GetFloat(K_MUSIC, 1f);
            float sfx = PlayerPrefs.GetFloat(K_SFX, 1f);

            if (_masterSlider != null) { _masterSlider.SetValueWithoutNotify(master); _masterVal.text = Pct(master); }
            if (_musicSlider != null) { _musicSlider.SetValueWithoutNotify(music); _musicVal.text = Pct(music); }
            if (_sfxSlider != null) { _sfxSlider.SetValueWithoutNotify(sfx); _sfxVal.text = Pct(sfx); }

            AudioListener.volume = master;

            bool fs = PlayerPrefs.GetInt(K_FULLSCREEN, Screen.fullScreen ? 1 : 0) == 1;
            bool vsync = PlayerPrefs.GetInt(K_VSYNC, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
            _fullscreenToggle?.SetValueWithoutNotify(fs);
            _vsyncToggle?.SetValueWithoutNotify(vsync);

            _micToggle?.SetValueWithoutNotify(VoiceSettings.MicOn);
            if (_thresholdSlider != null)
            {
                _thresholdSlider.SetValueWithoutNotify(VoiceSettings.RmsToMeter(VoiceSettings.Threshold));
                ShowThreshold(_thresholdSlider.value);
            }
            if (_voiceVolSlider != null)
            {
                _voiceVolSlider.SetValueWithoutNotify(VoiceSettings.Volume);
                _voiceVolVal.text = Pct(VoiceSettings.Volume);
            }
            _loopToggle?.SetValueWithoutNotify(false);
        }

        void ShowThreshold(float meter)
        {
            if (_thresholdVal != null) _thresholdVal.text = $"{Mathf.RoundToInt(meter * 60f - 60f)} dB";
            if (_meterMark != null) _meterMark.style.left = Length.Percent(meter * 100f);
        }

        // ~20x a second while open: run the mic check and paint the meter + verdict.
        void PollMic()
        {
            if (_thresholdSlider == null) return;
            float thresholdMeter = _thresholdSlider.value;
            _mic.Poll(VoiceSettings.MeterToRms(thresholdMeter), _micToggle == null || _micToggle.value);

            if (_meterFill != null)
            {
                _meterFill.style.width = Length.Percent(_mic.Meter * 100f);
                _meterFill.EnableInClassList("mm-meter-fill--loud", _mic.Meter >= thresholdMeter);
            }
            if (_micDb != null) _micDb.text = _mic.Meter > 0.01f ? $"{Mathf.RoundToInt(_mic.LevelDb)} dB" : "–";
            if (_micStatusText != null) _micStatusText.text = _mic.Message;
            if (_statusDot != null)
            {
                var v = _mic.State;
                _statusDot.EnableInClassList("mm-mic-dot--ok", v == MicTester.Verdict.Heard);
                _statusDot.EnableInClassList("mm-mic-dot--warn", v == MicTester.Verdict.TooQuiet || v == MicTester.Verdict.MicOff
                                                                || v == MicTester.Verdict.Silent || v == MicTester.Verdict.NoAudio);
                _statusDot.EnableInClassList("mm-mic-dot--bad", v == MicTester.Verdict.NoSteam || v == MicTester.Verdict.SteamError);
            }
        }

        public void Open()
        {
            if (_overlay == null) return;
            IsOpen = true;
            // re-sync sliders to saved values each time panel opens
            ApplySavedToControls();
            _overlay.style.display = DisplayStyle.Flex;
            _overlay.schedule.Execute(
                () => _overlay.AddToClassList("show")).StartingIn(16);

            // mic check runs only while the screen is open
            _mic.Start();
            if (_loopRow != null) _loopRow.style.display = _mic.UsingGameMic ? DisplayStyle.None : DisplayStyle.Flex;
            if (_micPoll == null) _micPoll = _overlay.schedule.Execute(PollMic).Every(50);
            else _micPoll.Resume();
        }

        public void Close()
        {
            if (_overlay == null) return;
            // revert live master volume preview to last saved value
            AudioListener.volume = PlayerPrefs.GetFloat(K_MASTER, 1f);
            _micPoll?.Pause();
            _mic.Stop();
            _loopToggle?.SetValueWithoutNotify(false);
            _overlay.RemoveFromClassList("show");
            _overlay.schedule.Execute(() =>
            {
                _overlay.style.display = DisplayStyle.None;
                IsOpen = false;
                Closed?.Invoke();
            }).StartingIn(260);
        }

        void Apply()
        {
            float master = _masterSlider.value;
            float music = _musicSlider.value;
            float sfx = _sfxSlider.value;
            bool fs = _fullscreenToggle.value;
            int vsync = _vsyncToggle.value ? 1 : 0;
            int qi = _qualityDropdown.index;
            int ri = _resDropdown.index;

            // apply audio
            AudioListener.volume = master;

            // apply display
            QualitySettings.SetQualityLevel(qi, true);
            QualitySettings.vSyncCount = vsync;
            Screen.fullScreen = fs;
            if (_resolutions != null && ri >= 0 && ri < _resolutions.Length)
            {
                var r = _resolutions[ri];
                Screen.SetResolution(r.width, r.height, fs);
            }

            // persist
            PlayerPrefs.SetFloat(K_MASTER, master);
            PlayerPrefs.SetFloat(K_MUSIC, music);
            PlayerPrefs.SetFloat(K_SFX, sfx);
            PlayerPrefs.SetInt(K_QUALITY, qi);
            PlayerPrefs.SetInt(K_RES_IDX, ri);
            PlayerPrefs.SetInt(K_FULLSCREEN, fs ? 1 : 0);
            PlayerPrefs.SetInt(K_VSYNC, vsync);
            if (_micToggle != null) VoiceSettings.MicOn = _micToggle.value;
            if (_thresholdSlider != null) VoiceSettings.Threshold = VoiceSettings.MeterToRms(_thresholdSlider.value);
            if (_voiceVolSlider != null) VoiceSettings.Volume = _voiceVolSlider.value;
            PlayerPrefs.Save();

            Close();
        }

        static string Pct(float v) => $"{Mathf.RoundToInt(v * 100)}%";
    }
}
