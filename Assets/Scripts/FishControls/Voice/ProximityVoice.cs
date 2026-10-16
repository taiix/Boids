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
        // The loudness gate itself is the player's setting (VoiceSettings.Threshold, Settings > Microphone):
        // a fixed value here left quiet microphones never transmitting at all.
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

        [Header("The shark hears fish")]
        [Tooltip("A fish talking within earshot of the shark shows it a ripple where the voice came from. " +
                 "Closer than this the ripple is exactly on the fish, and the fish can be locked onto.")]
        [SerializeField] float clearHearing = 12f;
        [Tooltip("How far off (metres) the ripple can be from a fish talking at the very edge of earshot. " +
                 "In between it grows with distance.")]
        [SerializeField] float farHearingError = 8f;
        [Tooltip("Seconds between ripples while a fish keeps talking.")]
        [SerializeField] float hearingInterval = 0.8f;

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

        /// <summary>The local player's voice, while it is recording - the settings screen reads the mic
        /// level from here in-game instead of pulling audio out of Steam itself (which would steal it).</summary>
        public static ProximityVoice LocalMic => s_micOwner != null && s_micOwner._recording ? s_micOwner : null;

        /// <summary>Loudness (RMS) of the latest mic frame, and when it arrived (unscaled time).</summary>
        public float MicLevel { get; private set; }
        public float MicLevelTime { get; private set; } = -1f;
        public EVoiceResult MicStatus => _lastMicResult;

        /// <summary>Shark's machine: a fish was heard. Where the ripple goes, how far off it may be (metres),
        /// and the fish itself when it was close enough to pin down (else null).</summary>
        public static event System.Action<Vector3, float, GameObject> SharkHeard;

        /// <summary>Local fish: until when (unscaled time) the HUD warns that the shark heard us.</summary>
        public static float HeardBySharkUntil { get; private set; } = -1f;

        float _nextHeardTime;   // server: when this fish's voice may next make a ripple

        void Awake()
        {
            _player = GetComponent<FishPlayer>();
            _vitals = GetComponent<FishVitals>();   // fish and shark both have one; null is treated as alive
        }

        void OnEnable() => VoiceSettings.Changed += ApplyVolume;
        void OnDisable() => VoiceSettings.Changed -= ApplyVolume;

        void ApplyVolume()
        {
            if (_source != null) _source.volume = VoiceSettings.Volume;
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

            // Measure even with the mic switched off, so the settings meter still shows the level.
            if (!PassesGate(_compressed, written) || !VoiceSettings.MicOn)
            {
                IsSpeaking = false;
                return;
            }

            IsSpeaking = true;

            if (written > MaxPayloadBytes) { _statTooLarge++; return; }   // see MaxPayloadBytes
            // Mid scene change (back to the lobby) the connection is not ready and the server drops
            // commands with a warning - the frame is lost either way, so don't send it.
            // On the host the server side goes not-ready a frame before the client side notices.
            if (!NetworkClient.ready || NetworkClient.isLoadingScene || NetworkServer.isLoadingScene) return;
            if (isServer && connectionToClient != null && !connectionToClient.isReady) return;

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
                    Debug.Log($"[Voice] Mic: loudest {_statPeakRms:0.000} (open-mic threshold {VoiceSettings.Threshold:0.000}), " +
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
            MicLevel = rms;
            MicLevelTime = Time.unscaledTime;
            _statFrames++;
            if (rms > _statPeakRms) _statPeakRms = rms;
            if (rms >= VoiceSettings.Threshold) _loudUntil = Time.unscaledTime + holdSeconds;

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

            // Round over: the dead and the living talk freely again over the results.
            var match = MatchManager.Instance;
            bool roundOver = match != null && match.IsOver;
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

                if (roundOver)
                {
                    // everyone hears everyone
                }
                else if (senderDead)
                {
                    // Ghosts talk among themselves, anywhere on the map. The living never hear them,
                    // otherwise being eaten would instantly leak who the shark is.
                    if (!listenerDead) continue;
                }
                else if (!listenerDead)
                {
                    if ((listener.transform.position - senderPos).sqrMagnitude > earshotSqr) continue;
                    ServerHeardBy(listener, conn);
                }
                // a dead listener spectates and hears the living regardless of distance

                TargetVoice(conn, data);
            }
        }

        // A ghost: out of the round any way at all - eaten, starved, escaped - not only dead by its vitals.
        // An eaten fish's vitals never die, so going by them alone let it keep talking to the living.
        // A fish still struggling in the jaws is alive - the others can hear it scream.
        bool IsDead => _vitals != null && _vitals.IsDead || _player != null && _player.IsOut;

        /// <summary>This player is out of the round, so only the other ghosts hear them (and they hear everyone).</summary>
        public bool IsGhost => IsDead;

        /// <summary>
        /// Server: a living listener within earshot just got a frame of this voice. If that listener is
        /// the shark and we're a fish still in the water, the shark sees a ripple where the voice came
        /// from - exact up close, off by up to <see cref="farHearingError"/> at the edge of earshot - and
        /// we're told we were heard. Decided here, like the audio itself, so no client can fake or hide it.
        /// Public so tests can stand in for a voice packet.
        /// </summary>
        [Server]
        public void ServerHeardBy(ProximityVoice shark, NetworkConnectionToClient sharkConn)
        {
            if (shark == null || sharkConn == null || shark == this) return;
            if (_player == null || _player.IsSharkBody || _player.IsOut || _player.IsBeingEaten) return;
            if (shark._player == null || !shark._player.IsSharkBody) return;
            if (Time.time < _nextHeardTime) return;
            _nextHeardTime = Time.time + hearingInterval;

            Vector3 pos = transform.position;
            float d = Vector3.Distance(shark.transform.position, pos);
            float error = farHearingError * Mathf.InverseLerp(clearHearing, earshot, d);
            Vector3 heardAt = pos + Random.insideUnitSphere * error;
            // Only a fish heard clearly is named, so a far one's real position never reaches the shark.
            shark.TargetHeardFish(sharkConn, heardAt, error, d <= clearHearing ? netId : 0u);
            if (connectionToClient != null) TargetHeardByShark(connectionToClient);
        }

        [TargetRpc]
        void TargetHeardFish(NetworkConnectionToClient target, Vector3 heardAt, float error, uint fishNetId)
        {
            GameObject fish = null;
            if (fishNetId != 0 && NetworkClient.spawned.TryGetValue(fishNetId, out var identity) && identity != null)
                fish = identity.gameObject;
            SharkHeard?.Invoke(heardAt, error, fish);
        }

        [TargetRpc]
        void TargetHeardByShark(NetworkConnectionToClient target)
        {
            // A warning as the shark first picks us up, not on every ripple while we keep talking.
            if (Time.unscaledTime >= HeardBySharkUntil && GameAudio.Lib != null) GameAudio.Play(GameAudio.Lib.uiWarning, 0.5f);
            HeardBySharkUntil = Time.unscaledTime + hearingInterval + 1.2f;
        }

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
            _source.volume = VoiceSettings.Volume;           // Settings > Microphone > Voice volume

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
            // A ghost's body is hidden somewhere on the reef: its voice comes from nowhere in particular, at
            // full volume wherever the other ghosts are. Same for everyone once the round is over.
            var match = MatchManager.Instance;
            _source.spatialBlend = IsDead || match != null && match.IsOver ? 0f : 1f;
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
