using FishGame;
using Steamworks;
using UnityEngine;

namespace ReefRun
{
    /// <summary>
    /// The microphone check behind Settings > Microphone. Voice chat goes through Steam: Steam picks the
    /// input device and captures it, then <see cref="ProximityVoice"/> only transmits frames louder than
    /// the player's sensitivity. So this measures exactly what the game measures, and says in plain words
    /// which link is broken: no Steam, Steam refusing the mic, no audio at all (wrong device / Windows
    /// privacy), or audio that's too quiet to pass the gate.
    ///
    /// Outside a match it records from Steam itself (and can play you back to yourself). In a match the
    /// player's own <see cref="ProximityVoice"/> is already recording, so it just reads that one's level
    /// rather than pulling audio out of Steam, which would stop it reaching the other players.
    /// </summary>
    public sealed class MicTester
    {
        public enum Verdict { NoSteam, SteamError, Waiting, NoAudio, Silent, MicOff, TooQuiet, Heard }

        public Verdict State { get; private set; } = Verdict.Waiting;
        public string Message { get; private set; } = "";
        /// <summary>Smoothed level on the shared meter scale (0..1, see VoiceSettings.RmsToMeter).</summary>
        public float Meter { get; private set; }
        public float LevelDb { get; private set; } = -60f;
        /// <summary>True in a match: the level comes from the game's own voice capture.</summary>
        public bool UsingGameMic => ProximityVoice.LocalMic != null;

        const float NoAudioAfter = 8f;   // seconds of nothing at all before suggesting it's the device

        bool _running, _ownRecording;
        float _startedAt, _lastAudioAt = -1f, _lastLoudAt = -1f, _lastAudibleAt = -1f;
        EVoiceResult _status = EVoiceResult.k_EVoiceResultOK;
        int _rate;
        byte[] _compressed, _pcmBytes;
        float[] _pcm;

        // "Hear myself": a 2D source fed from what Steam captured.
        GameObject _loopGo;
        AudioSource _loopSource;
        VoiceRingBuffer _loopBuffer;
        bool _loopPriming = true;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _startedAt = Time.unscaledTime;
            _lastAudioAt = _lastLoudAt = _lastAudibleAt = -1f;
            Meter = 0f;
            if (SteamManager.Initialized && !UsingGameMic) BeginOwnRecording();
        }

        void BeginOwnRecording()
        {
            _rate = (int)SteamUser.GetVoiceOptimalSampleRate();
            if (_rate <= 0) _rate = 24000;
            _compressed ??= new byte[8192];
            _pcmBytes = new byte[_rate * 2];
            _pcm = new float[_rate];
            SteamUser.StartVoiceRecording();
            _ownRecording = true;
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            SetLoopback(false);
            // Only stop what we started, and never under a match's own voice capture.
            if (_ownRecording && ProximityVoice.LocalMic == null && SteamManager.Initialized)
                SteamUser.StopVoiceRecording();
            _ownRecording = false;
        }

        /// <summary>Call ~20 times a second while the settings screen is open.</summary>
        public void Poll(float thresholdRms, bool micOn)
        {
            if (!_running) return;
            float now = Time.unscaledTime;
            float rms = -1f;   // -1 = no new frame this poll

            if (!SteamManager.Initialized)
            {
                Set(Verdict.NoSteam, "Steam isn't running, so voice chat is off. Start Steam and restart the game.");
                Meter = 0f;
                return;
            }

            var game = ProximityVoice.LocalMic;
            if (game != null)
            {
                _status = game.MicStatus;
                if (game.MicLevelTime > 0f && now - game.MicLevelTime < 0.25f) rms = game.MicLevel;
                if (game.MicLevelTime > 0f) _lastAudioAt = Mathf.Max(_lastAudioAt, game.MicLevelTime);
            }
            else
            {
                if (!_ownRecording) BeginOwnRecording();   // e.g. the match's capture just ended
                _status = SteamUser.GetAvailableVoice(out uint pending);
                if (_status == EVoiceResult.k_EVoiceResultNotRecording) SteamUser.StartVoiceRecording();
                if (_status == EVoiceResult.k_EVoiceResultOK && pending > 0)
                    rms = ReadOwnFrame();
            }

            if (rms >= 0f)
            {
                _lastAudioAt = now;
                if (rms > 0.0003f) _lastAudibleAt = now;              // not just digital silence
                if (rms >= thresholdRms) _lastLoudAt = now;
                float target = VoiceSettings.RmsToMeter(rms);
                Meter = target > Meter ? Mathf.Lerp(Meter, target, 0.7f) : Mathf.Lerp(Meter, target, 0.25f);
                LevelDb = VoiceSettings.RmsToDb(rms);
            }
            else if (_lastAudioAt < 0f || now - _lastAudioAt > 0.3f)
            {
                Meter = Mathf.Lerp(Meter, 0f, 0.25f);
            }

            // ---- verdict, most fundamental problem first ----
            if (_status != EVoiceResult.k_EVoiceResultOK && _status != EVoiceResult.k_EVoiceResultNoData
                && _status != EVoiceResult.k_EVoiceResultNotRecording)
                Set(Verdict.SteamError, _status == EVoiceResult.k_EVoiceResultRestricted
                    ? "Steam is blocking voice chat on this account (restricted). Check the account's Steam limitations."
                    : $"Steam can't use the microphone ({Pretty(_status)}). Check Steam's Voice settings.");
            // Steam only hands over audio while it hears something, so a gap is normal silence, not a fault.
            else if (_lastAudioAt < 0f)
                Set(now - _startedAt < NoAudioAfter ? Verdict.Waiting : Verdict.NoAudio,
                    now - _startedAt < NoAudioAfter ? "Listening… say something."
                    : "Nothing from your microphone yet. Talk normally; if the bar stays empty, choose your mic in Steam's Voice settings (below) and check Windows lets Steam use the microphone.");
            else if (now - _lastAudioAt > 3f)
                Set(Verdict.Waiting, "Listening… say something.");
            else if (_lastAudibleAt < 0f || now - _lastAudibleAt > 3f)
                Set(Verdict.Silent, "Your microphone is sending pure silence: it's probably muted, or Steam is using the wrong device.");
            else if (!micOn)
                Set(Verdict.MicOff, "Voice chat is switched off: you hear others, they don't hear you.");
            else if (_lastLoudAt > 0f && now - _lastLoudAt < 1.5f)
                Set(Verdict.Heard, "Others can hear you.");
            else
                Set(Verdict.TooQuiet, "We hear you, but too quietly to send. Drag Sensitivity left (or raise the input volume in Steam).");
        }

        float ReadOwnFrame()
        {
            var got = SteamUser.GetVoice(true, _compressed, (uint)_compressed.Length, out uint written);
            if (got != EVoiceResult.k_EVoiceResultOK || written == 0) return -1f;
            var dec = SteamUser.DecompressVoice(_compressed, written, _pcmBytes, (uint)_pcmBytes.Length, out uint pcm, (uint)_rate);
            if (dec != EVoiceResult.k_EVoiceResultOK || pcm == 0) return -1f;

            int n = Mathf.Min((int)pcm / 2, _pcm.Length);
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                float f = (short)(_pcmBytes[i * 2] | (_pcmBytes[i * 2 + 1] << 8)) / 32768f;
                _pcm[i] = f;
                sum += f * f;
            }
            if (_loopBuffer != null) _loopBuffer.Write(_pcm, n);
            return n > 0 ? Mathf.Sqrt((float)(sum / n)) : 0f;
        }

        /// <summary>Play your own captured voice back to you (outside a match only).</summary>
        public void SetLoopback(bool on)
        {
            if (on && (UsingGameMic || !SteamManager.Initialized || _rate <= 0)) on = false;
            if (on == (_loopSource != null)) return;
            if (on)
            {
                _loopBuffer = new VoiceRingBuffer(_rate * 2);
                _loopPriming = true;
                _loopGo = new GameObject("Mic loopback") { hideFlags = HideFlags.HideAndDontSave };
                _loopSource = _loopGo.AddComponent<AudioSource>();
                _loopSource.spatialBlend = 0f;
                _loopSource.loop = true;
                _loopSource.clip = AudioClip.Create("MicLoopback", _rate, 1, _rate, true, OnLoopRead);
                _loopSource.Play();
            }
            else
            {
                if (_loopGo != null) Object.Destroy(_loopGo);
                _loopGo = null; _loopSource = null; _loopBuffer = null;
            }
        }

        public bool LoopbackOn => _loopSource != null;

        // Audio thread. A short cushion first, like the in-game voice playback.
        void OnLoopRead(float[] data)
        {
            var buf = _loopBuffer;
            if (buf == null) { System.Array.Clear(data, 0, data.Length); return; }
            if (_loopPriming)
            {
                if (buf.Count < _rate / 10) { System.Array.Clear(data, 0, data.Length); return; }
                _loopPriming = false;
            }
            if (buf.Read(data, data.Length) == 0) _loopPriming = true;
        }

        void Set(Verdict v, string msg) { State = v; Message = msg; }

        static string Pretty(EVoiceResult r) => r.ToString().Replace("k_EVoiceResult", "");
    }
}
