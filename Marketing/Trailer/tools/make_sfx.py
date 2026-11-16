"""Synthesises the trailer-only SFX (riser, booms, whooshes) into Marketing/Trailer/sfx/.
Pure numpy, 48 kHz stereo 16-bit WAV. Run: python Marketing/Trailer/tools/make_sfx.py"""
import os, wave
import numpy as np

SR = 48000
OUT = os.path.join(os.path.dirname(__file__), "..", "sfx")
rng = np.random.default_rng(7)


def onepole(x, cutoff):
    """One-pole low-pass; cutoff may be an array (Hz per sample)."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=np.float64), x.shape)
    a = 1.0 - np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def save(name, left, right=None):
    right = left if right is None else right
    st = np.stack([left, right], 1)
    st = st / max(1e-9, np.abs(st).max()) * 0.89  # -1 dBFS peak
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((st * 32767).astype(np.int16).tobytes())
    print(name, f"{len(left)/SR:.2f}s")


def boom(dur=3.0, f0=70, f1=32, punch=0.06):
    t = np.arange(int(dur * SR)) / SR
    f = f1 + (f0 - f1) * np.exp(-t * 6)
    ph = 2 * np.pi * np.cumsum(f) / SR
    body = np.sin(ph) * np.exp(-t * 1.6)
    sub = np.sin(ph * 0.5) * np.exp(-t * 1.1) * 0.6
    n = rng.standard_normal(len(t))
    crack = onepole(n, 2500) * np.exp(-t / punch) * 2.5
    tail = onepole(n, 300) * np.exp(-t * 2.2) * 1.2
    x = np.tanh((body + sub + crack + tail) * 1.6)
    x[: int(0.002 * SR)] *= np.linspace(0, 1, int(0.002 * SR))
    return x


def riser(dur=3.2):
    t = np.arange(int(dur * SR)) / SR
    p = t / dur
    nL, nR = rng.standard_normal(len(t)), rng.standard_normal(len(t))
    cut = 200 + 9000 * p ** 2.2
    nzL, nzR = onepole(nL, cut), onepole(nR, cut)
    f = 110 * 2 ** (p * 3.2)  # 110 Hz -> ~1 kHz
    ph = 2 * np.pi * np.cumsum(f) / SR
    tone = (np.sin(ph) + 0.5 * np.sin(ph * 1.5 + 0.3) + 0.3 * np.sin(ph * 2.01)) * 0.35
    trem = 0.7 + 0.3 * np.sin(2 * np.pi * np.cumsum(2 + 14 * p ** 2) / SR)
    env = p ** 2.4
    return (nzL * 2.2 + tone) * env * trem, (nzR * 2.2 + tone) * env * trem


def whoosh(dur=0.9, peak=0.45, lo=300, hi=3500):
    t = np.arange(int(dur * SR)) / SR
    env = np.where(t < peak, (t / peak) ** 2, np.exp(-(t - peak) * 7))
    sweep = lo + (hi - lo) * np.sin(np.clip(t / dur, 0, 1) * np.pi)
    nL, nR = rng.standard_normal(len(t)), rng.standard_normal(len(t))
    bL = onepole(nL, sweep) - onepole(nL, sweep * 0.25)
    bR = onepole(nR, sweep) - onepole(nR, sweep * 0.25)
    pan = np.clip(t / dur, 0, 1)
    return bL * env * (1.2 - 0.4 * pan), bR * env * (0.8 + 0.4 * pan)


def hit(dur=1.4):
    """Short sub thump for title cards."""
    t = np.arange(int(dur * SR)) / SR
    f = 45 + 60 * np.exp(-t * 18)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 3.0)
    x += onepole(rng.standard_normal(len(t)), 1800) * np.exp(-t / 0.025) * 1.5
    return np.tanh(x * 1.4)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    save("boom_big.wav", boom(3.5, 75, 30, 0.07))
    save("boom_soft.wav", boom(3.0, 55, 28, 0.03) * 0.8)
    save("riser.wav", *riser(3.2))
    save("whoosh.wav", *whoosh(0.9, 0.45))
    save("whoosh_fast.wav", *whoosh(0.5, 0.22, 500, 5000))
    save("hit.wav", hit())
