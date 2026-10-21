"""Synthesises Reef Run's tension layers (drone, shark-near cluster, stinger) as WAVs.
Loops use whole-cycle frequencies so they repeat seamlessly; each loop is rendered twice so the
ffmpeg reverb/filter pass can be cut from the second, already-settled, pass."""
import sys, wave, numpy as np

SR = 44100
out = sys.argv[1]
rng = np.random.default_rng(7)

def whole(f, length):  # nearest frequency with a whole number of cycles in the loop
    return max(1, round(f * length)) / length

def noise_lp(n, k):  # brown-ish noise: one-pole low-pass of white noise, k = smoothing (0..1)
    w = rng.standard_normal(n)
    y = np.empty(n); acc = 0.0
    for i in range(n):
        acc += k * (w[i] - acc); y[i] = acc
    return y / (np.abs(y).max() + 1e-9)

def save(name, l, r):
    st = np.stack([l, r], 1)
    st = st / (np.abs(st).max() + 1e-9) * 0.89
    with wave.open(f"{out}/{name}.wav", "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((st * 32767).astype(np.int16).tobytes())

def loop_noise(n, k):  # noise that wraps: cross-fade the end into the start
    a = noise_lp(n + SR, k)
    fade = np.linspace(0, 1, SR)
    a[:SR] = a[:SR] * fade + a[n:] * (1 - fade)
    return a[:n]

# ---- 1. Drone: a low, beating A/Bb cluster with a tritone above, breathing slowly (32 s loop)
L = 32.0; n = int(SR * L); t = np.arange(n) / SR
def tone(f, ph=0.0, harm=(1, .5, .25, .12)):
    f = whole(f, L)
    return sum(a * np.sin(2 * np.pi * f * h * t + ph * h) for h, a in zip(range(1, 9), harm))
breath = 0.65 + 0.35 * np.sin(2 * np.pi * whole(1 / 8, L) * t)
drift = 0.5 + 0.5 * np.sin(2 * np.pi * whole(1 / 16, L) * t + 1.3)
dl = tone(55) + 0.8 * tone(58.27, 1) + 0.35 * drift * tone(77.78, 2, (1, .3, .1)) + 0.25 * tone(110.3, .5, (1, .2))
dr = tone(55.0, .7) + 0.8 * tone(58.0, 2) + 0.35 * (1 - drift) * tone(77.78, 3, (1, .3, .1)) + 0.25 * tone(109.7, 1, (1, .2))
rumble = loop_noise(n, 0.004)
save("drone", np.tile(dl * breath + 0.6 * rumble, 2), np.tile(dr * breath + 0.6 * np.roll(rumble, SR // 3), 2))

# ---- 2. Near: a tight, tremolo-ing high cluster (minor seconds) with a slow rise inside (16 s loop)
L = 16.0; n = int(SR * L); t = np.arange(n) / SR
def saw(f, ph):
    f = whole(f, L)
    return sum(np.sin(2 * np.pi * f * h * t + ph) / h for h in range(1, 7))
trem = 0.6 + 0.4 * np.sin(2 * np.pi * whole(5.5, L) * t)
swell = 0.55 + 0.45 * np.sin(2 * np.pi * whole(1 / 4, L) * t - np.pi / 2)
nl = saw(207.65, 0) + saw(220, 1) + 0.7 * saw(233.08, 2) + 0.35 * saw(311.13, 3) + 0.2 * np.sin(2 * np.pi * whole(880, L) * t) * swell
nr = saw(208.2, 2) + saw(219.4, 0) + 0.7 * saw(233.6, 1) + 0.35 * saw(310.6, 2) + 0.2 * np.sin(2 * np.pi * whole(932.3, L) * t) * (1 - swell)
hiss = loop_noise(n, 0.08)
save("near", np.tile(nl * trem * swell + 0.15 * hiss, 2), np.tile(nr * trem[::-1] * swell + 0.15 * np.roll(hiss, SR), 2))

# ---- 3. Sting: a short rising suck-in, then a low hit with a dissonant cluster ringing out (5 s)
L = 5.0; n = int(SR * L); t = np.arange(n) / SR
pre = 0.45; hit = t >= pre
rise = np.where(t < pre, (t / pre) ** 3, 0) * noise_lp(n, 0.25)
env = np.where(hit, np.exp(-(t - pre) * 1.1), 0)
sub_f = 70 * np.exp(-(t - pre).clip(0) * 3) + 32
sub = np.where(hit, np.sin(2 * np.pi * np.cumsum(sub_f) / SR) * np.exp(-(t - pre).clip(0) * 2.2), 0)
thud = np.where(hit, noise_lp(n, 0.05) * np.exp(-(t - pre).clip(0) * 9), 0)
clus = sum(np.sin(2 * np.pi * f * t + i) / (1 + 0.4 * i) for i, f in enumerate((110, 116.5, 155.6, 164.8, 233.1, 246.9)))
s = 0.5 * rise + 1.2 * sub + 0.6 * thud + 0.45 * clus * env
save("sting", s, np.roll(s, 90))
