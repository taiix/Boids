using System;
using System.Collections.Generic;
using Mirror;
using Steamworks;
using UnityEngine;

namespace FishGame
{
    /// <summary>A compressed Steam voice frame on its way through the lobby (client -> host -> everyone else).</summary>
    public struct LobbyVoiceMsg : NetworkMessage
    {
        public ulong speaker;            // Steam id of whoever is talking
        public ArraySegment<byte> data;  // Steam-compressed voice; only valid inside the handler
    }

    /// <summary>
    /// Voice chat in the lobby, where nobody has a body yet (so <see cref="ProximityVoice"/> isn't around).
    /// Everyone in the lobby hears everyone, flat 2D, at the player's voice volume.
    ///
    /// Same pipeline as the match, minus the proximity: Steam captures and compresses the mic, the same
    /// open-mic gate and on/off switch apply (<see cref="VoiceSettings"/>), Mirror carries the bytes as plain
    /// network messages (no NetworkIdentity needed) - the host relays each frame to every other connection.
    ///
    /// The lobby controller adds this at runtime; it lives and dies with the lobby scene.
    /// </summary>
    public class LobbyVoice : MonoBehaviour
    {
        const int MaxPayloadBytes = 900;      // same cap as ProximityVoice (unreliable channel MTU)
        const float SendInterval = 0.05f;
        const float HoldSeconds = 0.35f;
        const int JitterMs = 120;

        /// <summary>Who's talking right now (local player included), for the lobby's speaking indicators.</summary>
        public static bool IsTalking(CSteamID id) =>
            s_instance != null && s_instance._lastHeard.TryGetValue(id.m_SteamID, out float t) && Time.unscaledTime - t < 0.3f;

        static LobbyVoice s_instance;

        sealed class Speaker
        {
            public AudioSource source;
            public VoiceRingBuffer buffer;
            public bool priming = true;
        }

        readonly Dictionary<ulong, Speaker> _speakers = new();
        readonly Dictionary<ulong, float> _lastHeard = new();
        int _rate;
        int _jitterSamples;
        byte[] _compressed, _pcmBytes;
        float[] _pcm;
        bool _recording;
        float _nextSend, _loudUntil;
        ulong _me;

        void OnEnable()
        {
            s_instance = this;
            VoiceSettings.Changed += ApplyVolume;
        }

        void Start()
        {
            if (!SteamManager.Initialized) { Debug.LogWarning("[LobbyVoice] Steam isn't running - no lobby voice."); enabled = false; return; }

            _me = SteamUser.GetSteamID().m_SteamID;
            _rate = (int)SteamUser.GetVoiceOptimalSampleRate();
            if (_rate <= 0) _rate = 24000;
            _jitterSamples = _rate * JitterMs / 1000;
            _compressed = new byte[MaxPayloadBytes * 2];
            _pcmBytes = new byte[_rate * 2];
            _pcm = new float[_rate];

            // Registered for the whole network session and never taken away while it runs: a frame
            // still in flight when the lobby closes (the dive starting) must find a handler, or Mirror
            // disconnects whoever sent it - the host's own client included. Without a lobby, frames are dropped.
            if (NetworkServer.active) NetworkServer.ReplaceHandler<LobbyVoiceMsg>(OnServerVoice);
            if (NetworkClient.active) NetworkClient.ReplaceHandler<LobbyVoiceMsg>(OnClientVoice);

            SteamUser.StartVoiceRecording();
            _recording = true;
        }

        void OnDisable()
        {
            VoiceSettings.Changed -= ApplyVolume;
            if (s_instance == this) s_instance = null;

            // The match's voice takes the mic over when the game scene starts; never stop it under that one.
            if (_recording && ProximityVoice.LocalMic == null && SteamManager.Initialized)
            {
                try { SteamUser.StopVoiceRecording(); } catch (InvalidOperationException) { }
            }
            _recording = false;

            foreach (var s in _speakers.Values) if (s.source != null) Destroy(s.source.gameObject);
            _speakers.Clear();
        }

        // ---------------------------------------------------------------- capture + send
        void Update()
        {
            if (!_recording || !NetworkClient.isConnected || NetworkClient.isLoadingScene) return;
            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + SendInterval;

            var available = SteamUser.GetAvailableVoice(out uint pending);
            if (available == EVoiceResult.k_EVoiceResultNotRecording) { SteamUser.StartVoiceRecording(); return; }
            if (available != EVoiceResult.k_EVoiceResultOK || pending == 0) return;

            var got = SteamUser.GetVoice(true, _compressed, (uint)_compressed.Length, out uint written);
            if (got != EVoiceResult.k_EVoiceResultOK || written == 0 || written > MaxPayloadBytes) return;

            // Same open-mic gate as the match: decode locally, measure, send only above the sensitivity.
            float rms = Decode(_compressed, (int)written, out _);
            if (rms >= VoiceSettings.Threshold) _loudUntil = Time.unscaledTime + HoldSeconds;
            if (Time.unscaledTime >= _loudUntil || !VoiceSettings.MicOn) return;

            _lastHeard[_me] = Time.unscaledTime;   // our own speaking indicator
            NetworkClient.Send(new LobbyVoiceMsg { speaker = _me, data = new ArraySegment<byte>(_compressed, 0, (int)written) },
                               Channels.Unreliable);
        }

        // ---------------------------------------------------------------- relay (host)
        static void OnServerVoice(NetworkConnectionToClient from, LobbyVoiceMsg msg)
        {
            if (s_instance == null) return;   // the lobby has closed: a late frame, nobody to hear it
            if (msg.data.Count == 0 || msg.data.Count > MaxPayloadBytes) return;
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn == null || conn == from) continue;   // never echo a speaker to themselves
                conn.Send(msg, Channels.Unreliable);
            }
        }

        // ---------------------------------------------------------------- playback (everyone)
        static void OnClientVoice(LobbyVoiceMsg msg)
        {
            if (s_instance != null) s_instance.Play(msg);
        }

        void Play(LobbyVoiceMsg msg)
        {
            if (msg.speaker == _me || msg.data.Count == 0 || _pcm == null) return;
            // ArraySegment is only valid during this call: decode straight out of it.
            var copy = new byte[msg.data.Count];
            Buffer.BlockCopy(msg.data.Array, msg.data.Offset, copy, 0, msg.data.Count);
            Decode(copy, copy.Length, out int samples);
            if (samples <= 0) return;

            var spk = GetSpeaker(msg.speaker);
            spk.buffer.Write(_pcm, samples);
            _lastHeard[msg.speaker] = Time.unscaledTime;
        }

        /// <summary>Decode a Steam voice frame into _pcm. Returns its loudness (RMS).</summary>
        float Decode(byte[] data, int length, out int samples)
        {
            samples = 0;
            var r = SteamUser.DecompressVoice(data, (uint)length, _pcmBytes, (uint)_pcmBytes.Length, out uint pcm, (uint)_rate);
            if (r != EVoiceResult.k_EVoiceResultOK || pcm == 0) return 0f;
            samples = Mathf.Min((int)pcm / 2, _pcm.Length);
            double sum = 0;
            for (int i = 0; i < samples; i++)
            {
                float f = (short)(_pcmBytes[i * 2] | (_pcmBytes[i * 2 + 1] << 8)) / 32768f;
                _pcm[i] = f;
                sum += f * f;
            }
            return samples > 0 ? Mathf.Sqrt((float)(sum / samples)) : 0f;
        }

        Speaker GetSpeaker(ulong id)
        {
            if (_speakers.TryGetValue(id, out var s)) return s;
            s = new Speaker { buffer = new VoiceRingBuffer(_rate * 2) };
            var go = new GameObject($"LobbyVoice {id}");
            go.transform.SetParent(transform, false);
            s.source = go.AddComponent<AudioSource>();
            s.source.spatialBlend = 0f;                 // flat: everyone in the lobby is "in the room"
            s.source.loop = true;
            s.source.volume = VoiceSettings.Volume;
            var speaker = s;
            s.source.clip = AudioClip.Create($"LobbyVoice_{id}", _rate, 1, _rate, true, data => Read(speaker, data));
            s.source.Play();
            _speakers[id] = s;
            return s;
        }

        // Audio thread: drain the speaker's buffer, with a small cushion against network jitter.
        void Read(Speaker s, float[] data)
        {
            if (s.priming)
            {
                if (s.buffer.Count < _jitterSamples) { Array.Clear(data, 0, data.Length); return; }
                s.priming = false;
            }
            if (s.buffer.Read(data, data.Length) == 0) s.priming = true;
        }

        void ApplyVolume()
        {
            foreach (var s in _speakers.Values) if (s.source != null) s.source.volume = VoiceSettings.Volume;
        }
    }
}
