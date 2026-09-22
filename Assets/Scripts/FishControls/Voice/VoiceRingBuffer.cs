using System;

namespace FishGame
{
    /// <summary>
    /// A fixed-size circular buffer of mono float samples.
    ///
    /// Voice arrives on Unity's main thread (a network RPC) but is consumed on the audio thread
    /// (the streaming AudioClip's reader callback), so every access is behind a lock. The lock is
    /// only ever held for a memcpy, which is short enough not to risk stalling the audio thread.
    ///
    /// When it overflows, the OLDEST samples are dropped rather than the newest: if the network
    /// burst-delivers more than we can play, the listener should end up hearing the most recent
    /// speech rather than a growing delay.
    /// </summary>
    public sealed class VoiceRingBuffer
    {
        readonly float[] _buffer;
        readonly object _gate = new object();
        int _read;
        int _write;
        int _count;

        public VoiceRingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new float[capacity];
        }

        public int Capacity => _buffer.Length;

        public int Count
        {
            get { lock (_gate) return _count; }
        }

        public void Clear()
        {
            lock (_gate) { _read = 0; _write = 0; _count = 0; }
        }

        /// <summary>Append <paramref name="length"/> samples, discarding the oldest on overflow.</summary>
        public void Write(float[] source, int length)
        {
            if (source == null || length <= 0) return;

            // A chunk bigger than the whole buffer can only be partly kept. Keep its TAIL, so the
            // policy stays "newest audio wins" rather than silently preserving the stalest samples.
            int offset = 0;
            if (length > _buffer.Length)
            {
                offset = length - _buffer.Length;
                length = _buffer.Length;
            }

            lock (_gate)
            {
                for (int i = 0; i < length; i++)
                {
                    _buffer[_write] = source[offset + i];
                    _write = (_write + 1) % _buffer.Length;

                    if (_count < _buffer.Length) _count++;
                    else _read = (_read + 1) % _buffer.Length; // full: overwrite the oldest sample
                }
            }
        }

        /// <summary>
        /// Fill <paramref name="destination"/> with buffered samples, padding any shortfall with
        /// silence. Returns how many real samples were supplied, so the caller can detect underrun.
        /// </summary>
        public int Read(float[] destination, int length)
        {
            if (destination == null || length <= 0) return 0;

            int supplied;
            lock (_gate)
            {
                supplied = Math.Min(length, _count);
                for (int i = 0; i < supplied; i++)
                {
                    destination[i] = _buffer[_read];
                    _read = (_read + 1) % _buffer.Length;
                }
                _count -= supplied;
            }

            if (supplied < length)
                Array.Clear(destination, supplied, length - supplied);

            return supplied;
        }
    }
}
