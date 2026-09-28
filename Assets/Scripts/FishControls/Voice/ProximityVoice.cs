using Mirror;
using Steamworks;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Proximity voice chat: you hear whoever is near you, and they fade out with distance.
    ///
    /// Three layers, only one of which is Steam:
    ///   * Steam captures the mic and does the Opus encode/decode.
    ///   * Mirror moves the compressed bytes, and the SERVER decides who is allowed to receive
    ///     them (see <see cref="CmdVoice"/>).
    ///   * Unity's 3D AudioSource does the actual proximity - distance falloff and panning.
    ///
    /// The server-side filtering is the important part and is deliberately not a client concern.
    /// If every client received every voice packet and merely turned distant ones down, a modified
    /// client could ignore the attenuation and listen to the whole map - fatal in a game whose
    /// tension is who knows what. Audio that was never sent cannot be recovered.
    ///
    /// Lives on the player prefab next to <see cref="FishPlayer"/>. The AudioSource is created at
    /// runtime, so no prefab setup is required.
    /// </summary>
    [RequireComponent(typeof(FishPlayer))]
    [DisallowMultipleComponent]
    public class ProximityVoice : NetworkBehaviour
    {
        [Header("Range")]
        [Tooltip("How far a voice carries, in metres. Also drives the AudioSource's max distance so " +
                 "the volume reaches zero exactly where the server stops sending - no audible pop.")]
        [SerializeField] float earshot = 30f;
        [Tooltip("Distance at which a voice is still at full volume.")]
        [SerializeField] float fullVolumeDistance = 3f;

        [Header("Open mic")]
        [Tooltip("Loudness (RMS, 0-1) the mic must exceed before anything is transmitted.")]
        [Range(0f, 0.2f)][SerializeField] float voiceThreshold = 0.015f;
        [Tooltip("Keep transmitting this long after you drop below the threshold, so the ends of " +
                 "words are not clipped off.")]
        [SerializeField] float holdSeconds = 0.35f;
        [Tooltip("How often the mic is drained and sent, in seconds. Smaller = lower latency but " +
                 "more packets.")]
        [SerializeField] float sendInterval = 0.05f;

        [Header("Shark voice")]
        [Tooltip("The shark is audible, but wrong. Low-pass cutoff in Hz applied to its voice.")]
        [SerializeField] float sharkLowPassHz = 900f;
        [Tooltip("Distortion applied to the shark's voice, 0-1.")]
        [Range(0f, 1f)][SerializeField] float sharkDistortion = 0.45f;

        [Header("Playback")]
        [Tooltip("Milliseconds of audio to buffer before playback starts. Higher rides out worse " +
                 "network jitter at the cost of latency.")]
        [SerializeField] int jitterBufferMs = 120;

        // Mirror's unreliable channel caps out around the MTU. Steam's frames at a 50ms interval sit
        // far below this; anything larger is dropped rather than split, because slicing a compressed
        // stream mid-frame would just hand the decoder garbage.
        const int MaxPayloadBytes = 900;

        FishPlayer _player;
        FishVitals _vitals;
        AudioSource _source;
        AudioLowPassFilter _lowPass;
        AudioDistortionFilter _distortion;

        VoiceRingBuffer _buffer;
        int _sampleRate;
        bool _priming = true;
        int _jitterSamples;

        // Capture scratch, reused every poll so open-mic does not allocate per frame.
        byte[] _compressed;
        byte[] _decompressScratch;
        float[] _floatScratch;

        float _nextSendTime;
        float _loudUntil;
        bool _recording;
        bool _voiceUnavailable;
        FishRole _treatedRole = FishRole.Unassigned;

        // Steam has ONE microphone recorder for the whole game. When a player's body is replaced (F8
        // role swap, respawn after being eaten) the new body starts it before the old one is destroyed
        // on a client, so only the body that currently owns the mic may stop it.
        static ProximityVoice s_micOwner;

        public bool IsSpeaking { get; private set; }

        void Awake()
        {
            _player = GetComponent<FishPlayer>();
            _vitals = GetComponent<FishVitals>();   // fish and shark both have one; null is treated as alive
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            _sampleRate = SteamAvailable ? (int)SteamUser.GetVoiceOptimalSampleRate() : 24000;
            if (_sampleRate <= 0) _sampleRate = 24000;

            _jitterSamples = Mathf.Max(1, _sampleRate * jitterBufferMs / 1000);
            _buffer = new VoiceRingBuffer(_sampleRate * 2);          // two seconds of headroom
            _decompressScratch = new byte[_sampleRate * 2];          // one second of 16-bit mono
            _floatScratch = new float[_sampleRate];

            // Playback is built lazily on the first packet rather than here: isLocalPlayer is not
            // reliably set yet during OnStartClient, and a player who never speaks never needs an
            // AudioSource at all.
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            if (!SteamAvailable)
            {
                Debug.LogWarning("[Voice] Steam is not initialised, so the microphone is " +
                                 "unavailable. Voice chat is disabled for this session.", this);
                _voiceUnavailable = true;
                return;
            }

            _compressed = new byte[MaxPayloadBytes * 2];
            SteamUser.StartVoiceRecording();
            _recording = true;
            s_micOwner = this;
        }

        static bool SteamAvailable => SteamManager.Initialized;

        void OnDestroy()
        {
            if (!_recording) return;
            _recording = false;
            // A body replaced by a new one of ours leaves the mic running for it.
            if (s_micOwner != this) return;
            s_micOwner = null;
            // On quit Steam can already be shut down; there's nothing left to stop then.
            try { SteamUser.StopVoiceRecording(); }
            catch (System.InvalidOperationException) { }
        }

        // ---------------------------------------------------------------- capture (local player)

        void Update()
        {
            ReportVoiceStats();
            if (!isLocalPlayer || _voiceUnavailable || !_recording) return;
            if (Time.unscaledTime < _nextSendTime) return;
            _nextSendTime = Time.unscaledTime + sendInterval;

            var available = SteamUser.GetAvailableVoice(out uint pending);
            if (available != _lastMicResult)
            {
                // OK / NoData just mean "talking" / "quiet"; anything else is Steam refusing the mic.
                if (available != EVoiceResult.k_EVoiceResultOK && available != EVoiceResult.k_EVoiceResultNoData)
                    Debug.LogWarning($"[Voice] Steam microphone reports {available}.");
                _lastMicResult = available;
            }
            if (available == EVoiceResult.k_EVoiceResultNotRecording)
            {
                // Something stopped the mic under us (e.g. an old body's clean-up) - start it again.
                SteamUser.StartVoiceRecording();
                s_micOwner = this;
                return;
            }
            if (available != EVoiceResult.k_EVoiceResultOK || pending == 0)
                return;

            var got = SteamUser.GetVoice(true, _compressed, (uint)_compressed.Length, out uint written);
            if (got != EVoiceResult.k_EVoiceResultOK || written == 0) return;

            if (!PassesGate(_compressed, written))
            {
                IsSpeaking = false;
                return;
            }

            IsSpeaking = true;

            if (written > MaxPayloadBytes) { _statTooLarge++; return; }   // see MaxPayloadBytes

            var payload = new byte[written];
            System.Buffer.BlockCopy(_compressed, 0, payload, 0, (int)written);
            CmdVoice(payload);
            _statSent++;
        }

        // ---------------------------------------------------------------- diagnostics
        // Voice goes one way only on some setups; these log lines say which end is at fault. Your own
        // body reports the mic (Steam status, loudness vs the open-mic threshold, packets sent); every
        // other player's body reports how much of their voice actually arrived here.

        const float StatsInterval = 10f;
        EVoiceResult _lastMicResult = EVoiceResult.k_EVoiceResultOK;
        float _statPeakRms;
        int _statFrames, _statSent, _statTooLarge, _statReceived;
        float _nextStatsTime = -1f;
        bool _warnedDecode;

        void ReportVoiceStats()
        {
            if (_nextStatsTime < 0f) { _nextStatsTime = Time.unscaledTime + StatsInterval; return; }
            if (Time.unscaledTime < _nextStatsTime) return;
            _nextStatsTime = Time.unscaledTime + StatsInterval;

            if (isLocalPlayer)
            {
                if (_voiceUnavailable) return; // already warned once
                if (_statFrames == 0)
                    Debug.Log($"[Voice] Mic: no audio from Steam in the last {StatsInterval:0}s " +
                              $"(Steam status {_lastMicResult}) - check Steam > Settings > Voice and Windows microphone access.");
                else
                    Debug.Log($"[Voice] Mic: loudest {_statPeakRms:0.000} (open-mic threshold {voiceThreshold:0.000}), " +
                              $"sent {_statSent} packets" + (_statTooLarge > 0 ? $", {_statTooLarge} too large to send" : "") + ".");
            }
            else if (_statReceived > 0)
            {
                Debug.Log($"[Voice] Heard {name} (netId {netId}): {_statReceived} packets in the last {StatsInterval:0}s.");
            }
            _statPeakRms = 0f;
            _statFrames = _statSent = _statTooLarge = _statReceived = 0;
        }

        /// <summary>
        /// Open-mic gate. Steam hands back compressed audio whether or not anyone is talking, so the
        /// frame is decoded locally just to measure its loudness. One extra decode of ~50ms of mono
        /// audio per interval is cheap, and it is far more reliable than guessing from packet size.
        /// </summary>
        bool PassesGate(byte[] data, uint length)
        {
            var result = SteamUser.DecompressVoice(data, length, _decompressScratch,
                (uint)_decompressScratch.Length, out uint pcmBytes, (uint)_sampleRate);

            if (result != EVoiceResult.k_EVoiceResultOK || pcmBytes == 0)
                return Time.unscaledTime < _loudUntil;

            int samples = (int)pcmBytes / 2;
            double sum = 0;
            for (int i = 0; i < samples; i++)
            {
                short s = (short)(_decompressScratch[i * 2] | (_decompressScratch[i * 2 + 1] << 8));
                float f = s / 32768f;
                sum += f * f;
            }

            float rms = samples > 0 ? Mathf.Sqrt((float)(sum / samples)) : 0f;
            _statFrames++;
            if (rms > _statPeakRms) _statPeakRms = rms;
            if (rms >= voiceThreshold) _loudUntil = Time.unscaledTime + holdSeconds;

            return Time.unscaledTime < _loudUntil;
        }

        // ---------------------------------------------------------------- routing (server)

        /// <summary>
        /// Decide who is allowed to hear this. Living voices carry only to the living within
        /// earshot; the dead form their own channel and cannot be heard by the living at all.
        /// </summary>
        [Command(channel = Channels.Unreliable, requiresAuthority = true)]
        void CmdVoice(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > MaxPayloadBytes) return;

            bool senderDead = IsDead;
            Vector3 senderPos = transform.position;
            float earshotSqr = earshot * earshot;

            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn?.identity == null) continue;
                if (conn == connectionToClient) continue;       // never echo a player to themselves

                var listener = conn.identity.GetComponent<ProximityVoice>();
                if (listener == null) continue;

                bool listenerDead = listener.IsDead;

                if (senderDead)
                {
                    // Ghosts talk among themselves, anywhere on the map. The living never hear them,
                    // otherwise being eaten would instantly leak who the shark is.
                    if (!listenerDead) continue;
                }
                else if (!listenerDead)
                {
                    if ((listener.transform.position - senderPos).sqrMagnitude > earshotSqr) continue;
                }
                // a dead listener spectates and hears the living regardless of distance

                TargetVoice(conn, data);
            }
        }

        bool IsDead => _vitals != null && _vitals.IsDead;

        /// <summary>
        /// Delivered on the speaker's own object on the listener's machine, so "this" is already the
        /// right player - the audio plays from their transform and gets correct 3D placement for free.
        /// </summary>
        [TargetRpc(channel = Channels.Unreliable)]
        void TargetVoice(NetworkConnectionToClient target, byte[] data) => Receive(data);

        // ---------------------------------------------------------------- playback (listeners)

        void BuildPlayback()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.spatialBlend = 1f;                       // fully 3D - this is what makes it proximity
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = fullVolumeDistance;
            _source.maxDistance = earshot;                   // silent exactly where the server stops sending
            _source.loop = true;
            _source.playOnAwake = false;
            _source.dopplerLevel = 0f;                       // voices should not warble as fish dart about

            _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            _lowPass.enabled = false;
            _distortion = gameObject.AddComponent<AudioDistortionFilter>();
            _distortion.enabled = false;

            _source.clip = AudioClip.Create($"Voice_{netId}", _sampleRate, 1, _sampleRate, true, OnPcmRead);
            _source.Play();
        }

        void Receive(byte[] data)
        {
            if (_buffer == null || data == null || data.Length == 0) return;
            if (!SteamAvailable) return;

            // Our own voice is never played back to us, and the server should not have sent it.
            if (isLocalPlayer) return;
            _statReceived++;
            if (_source == null) BuildPlayback();

            var result = SteamUser.DecompressVoice(data, (uint)data.Length, _decompressScratch,
                (uint)_decompressScratch.Length, out uint pcmBytes, (uint)_sampleRate);

            if (result != EVoiceResult.k_EVoiceResultOK || pcmBytes == 0)
            {
                if (!_warnedDecode) Debug.LogWarning($"[Voice] Could not decode {name}'s voice: {result}.");
                _warnedDecode = true;
                return;
            }

            int samples = Mathf.Min((int)pcmBytes / 2, _floatScratch.Length);
            for (int i = 0; i < samples; i++)
            {
                short s = (short)(_decompressScratch[i * 2] | (_decompressScratch[i * 2 + 1] << 8));
                _floatScratch[i] = s / 32768f;
            }

            _buffer.Write(_floatScratch, samples);
            ApplyRoleTreatment();
        }

        /// <summary>Runs on the audio thread: drain the buffer, or emit silence while re-priming.</summary>
        void OnPcmRead(float[] data)
        {
            var buffer = _buffer;
            if (buffer == null) { System.Array.Clear(data, 0, data.Length); return; }

            // Wait for a cushion before starting, so ordinary jitter does not produce clicks.
            if (_priming)
            {
                if (buffer.Count < _jitterSamples) { System.Array.Clear(data, 0, data.Length); return; }
                _priming = false;
            }

            int supplied = buffer.Read(data, data.Length);
            if (supplied == 0) _priming = true;   // underran - rebuild the cushion before resuming
        }

        /// <summary>The shark is audible but wrong: muffled and distorted, so it never sounds human.</summary>
        void ApplyRoleTreatment()
        {
            if (_source == null || _player == null) return;

            FishRole role = _player.Role;
            if (role == _treatedRole) return;
            _treatedRole = role;

            bool isShark = role == FishRole.Shark;

            if (_lowPass != null)
            {
                _lowPass.enabled = isShark;
                _lowPass.cutoffFrequency = sharkLowPassHz;
            }
            if (_distortion != null)
            {
                _distortion.enabled = isShark;
                _distortion.distortionLevel = sharkDistortion;
            }
        }
    }
}
