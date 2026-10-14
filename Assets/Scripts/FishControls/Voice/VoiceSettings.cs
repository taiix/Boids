using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// The player's voice-chat preferences, saved in PlayerPrefs and shared by the settings screen and
    /// <see cref="ProximityVoice"/>. Steam owns the microphone itself (which input device, its input
    /// volume) - that lives in Steam's own Voice settings, which the settings screen links to.
    /// </summary>
    public static class VoiceSettings
    {
        const string K_MIC_ON = "rr_mic_on";
        const string K_THRESHOLD = "rr_mic_threshold";
        const string K_VOLUME = "rr_voice_volume";

        /// <summary>Open-mic loudness (RMS) the mic must reach before it transmits. 0.015 was the
        /// original fixed value; quiet microphones never reached it, so it is now the player's call.</summary>
        public const float DefaultThreshold = 0.015f;

        /// <summary>Raised after any value changes (Apply in the settings screen).</summary>
        public static event System.Action Changed;

        /// <summary>Transmit your voice at all. Off = you still hear others.</summary>
        public static bool MicOn
        {
            get => PlayerPrefs.GetInt(K_MIC_ON, 1) == 1;
            set { PlayerPrefs.SetInt(K_MIC_ON, value ? 1 : 0); Changed?.Invoke(); }
        }

        /// <summary>Open-mic gate as RMS (0-1).</summary>
        public static float Threshold
        {
            get => PlayerPrefs.GetFloat(K_THRESHOLD, DefaultThreshold);
            set { PlayerPrefs.SetFloat(K_THRESHOLD, Mathf.Clamp(value, 0.0005f, 0.5f)); Changed?.Invoke(); }
        }

        /// <summary>Playback volume of other players' voices (0-1.5).</summary>
        public static float Volume
        {
            get => PlayerPrefs.GetFloat(K_VOLUME, 1f);
            set { PlayerPrefs.SetFloat(K_VOLUME, Mathf.Clamp(value, 0f, 1.5f)); Changed?.Invoke(); }
        }

        // The mic meter and the sensitivity slider share one scale, -60 dB .. 0 dB mapped to 0..1, so
        // the threshold marker sits exactly where the level bar has to reach.
        public static float RmsToMeter(float rms) => rms <= 0f ? 0f : Mathf.Clamp01((20f * Mathf.Log10(rms) + 60f) / 60f);
        public static float MeterToRms(float meter) => Mathf.Pow(10f, (Mathf.Clamp01(meter) * 60f - 60f) / 20f);
        public static float RmsToDb(float rms) => rms <= 0f ? -60f : Mathf.Max(-60f, 20f * Mathf.Log10(rms));
    }
}
