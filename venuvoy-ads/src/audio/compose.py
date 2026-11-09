"""Original soundtrack for the English reel: an upbeat 120 BPM pop-house cue plus sound effects
placed on the video's own cue times. Everything is synthesized here, so there is nothing to license.

    python3 src/audio/compose.py out.wav

Structure (one bar = 2 s): intro 0-4 s, drop 4 s, groove to 16 s, breakdown 16-20 s, drop 20 s,
final chorus 26-31 s, last hit at 31 s with its tail to 32 s.
"""
import sys

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, fftconvolve, sosfilt

# CLI: compose.py OUT.wav                       full 32 s reel with its sound effects
#      compose.py OUT.wav --cut START DUR [--cues cues.json]
#                                                a slice of the music bed, with effects placed on the
#                                                given animation cues ({t, type, dur}) instead
ARGS = sys.argv[1:]
OUT = ARGS[0] if ARGS and not ARGS[0].startswith("--") else "music.wav"
CUT = None
CUES = []
if "--cut" in ARGS:
    i = ARGS.index("--cut")
    CUT = (float(ARGS[i + 1]), float(ARGS[i + 2]))
if "--cues" in ARGS:
    import json

    CUES = json.load(open(ARGS[ARGS.index("--cues") + 1]))

SR = 44100
BPM = 120
BEAT = 60 / BPM
BAR = 4 * BEAT
DUR = 32.0
N = int(SR * DUR)
rng = np.random.default_rng(7)


def mtof(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def tt(d):
    return np.arange(int(SR * d)) / SR


def bp(x, lo, hi, order=2):
    return sosfilt(butter(order, [lo, hi], btype="band", fs=SR, output="sos"), x)


def hp(x, f, order=2):
    return sosfilt(butter(order, f, btype="high", fs=SR, output="sos"), x)


def lp(x, f, order=2):
    return sosfilt(butter(order, f, btype="low", fs=SR, output="sos"), x)


class Bus:
    def __init__(self):
        self.y = np.zeros((N + SR * 4, 2))

    def add(self, sig, t, gain=1.0, pan=0.0):
        """Mono or stereo signal at time t (s); pan -1..1 (equal-power)."""
        i = int(round(t * SR))
        if i >= N:
            return
        if sig.ndim == 1:
            a = (pan + 1) * np.pi / 4
            sig = np.stack([sig * np.cos(a), sig * np.sin(a)], axis=1) * np.sqrt(2)
        n = min(len(sig), len(self.y) - i)
        self.y[i : i + n] += sig[:n] * gain


# ---------------------------------------------------------------- instruments
def kick():
    t = tt(0.42)
    f = 46 + (160 - 46) * np.exp(-t / 0.032)
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = np.sin(ph) * np.exp(-t / 0.26)
    y += 0.35 * hp(rng.standard_normal(len(t)), 2500) * np.exp(-t / 0.003)
    return hp(np.tanh(1.8 * y) * 0.9, 38)


def clap():
    t = tt(0.32)
    n = rng.standard_normal(len(t))
    env = np.zeros(len(t))
    for k, d in enumerate([0.0, 0.011, 0.022]):
        i = int(d * SR)
        env[i:] += np.exp(-(t[: len(t) - i]) / (0.006 if k < 2 else 0.11))
    return bp(n * env, 900, 3200) * 1.4


def snare():
    t = tt(0.2)
    tone = np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.05)
    noise = bp(rng.standard_normal(len(t)), 1200, 7000) * np.exp(-t / 0.07)
    return tone * 0.5 + noise * 0.9


def hat(open_=False):
    t = tt(0.25 if open_ else 0.06)
    return hp(rng.standard_normal(len(t)), 7500, 4) * np.exp(-t / (0.11 if open_ else 0.018)) * 0.7


def shaker():
    t = tt(0.07)
    return bp(rng.standard_normal(len(t)), 4500, 10000) * (np.minimum(t / 0.01, 1) * np.exp(-t / 0.03)) * 0.6


def crash(d=2.2):
    t = tt(d)
    n = hp(rng.standard_normal((len(t), 2)).T, 4000).T
    return n * np.exp(-t / 0.9)[:, None] * 0.55


def saw(f, t, detune=0.0):
    ph = (f * (1 + detune) * t) % 1.0
    return 2 * ph - 1


def bass_note(m, d=0.21):
    t = tt(d)
    f = mtof(m)
    y = 0.65 * saw(f, t) + 0.6 * np.sin(2 * np.pi * f * t) + 0.35 * np.sin(2 * np.pi * f / 2 * t)
    y = lp(y, 900, 2)
    env = np.minimum(t / 0.004, 1) * np.exp(-t / 0.12)
    return np.tanh(1.6 * y * env)


def bass_long(m, d):
    t = tt(d)
    f = mtof(m)
    y = lp(0.6 * saw(f, t) + 0.7 * np.sin(2 * np.pi * f * t), 500)
    env = np.minimum(t / 0.05, 1) * np.minimum((d - t) / 0.06, 1)
    return y * env * 0.7


def keys_note(m, d=0.42, bright=1.0):
    """House piano-ish stab: decaying harmonics with a soft hammer and slight detune chorus."""
    t = tt(d)
    f = mtof(m)
    y = np.zeros(len(t))
    for h, a in [(1, 1.0), (2, 0.55 * bright), (3, 0.3 * bright), (4, 0.18 * bright), (5, 0.08 * bright)]:
        y += a * np.sin(2 * np.pi * f * h * t) * np.exp(-t * (3.2 + 1.6 * h))
    y += 0.4 * np.sin(2 * np.pi * f * 1.004 * t) * np.exp(-t * 4)
    hammer = bp(rng.standard_normal(len(t)), 1500, 5000) * np.exp(-t / 0.006) * 0.12
    return (y + hammer) * np.minimum(t / 0.002, 1)


def stab(chord, d=0.42, bright=1.0):
    y = sum(keys_note(m, d, bright) for m in chord)
    return y / len(chord) * 1.6


def pad(chord, d):
    t = tt(d)
    y = np.zeros(len(t))
    for m in chord:
        f = mtof(m)
        for dt in (-0.006, -0.002, 0.003, 0.007):
            y += saw(f, t + rng.random() * 0.01, dt)
    y = lp(y / (len(chord) * 4), 1600)
    env = np.minimum(t / 0.6, 1) * np.minimum((d - t) / 0.12, 1)
    return y * env


def pluck(m, d=0.3):
    """Lead: bright pulse pluck, band-limited by summing odd harmonics."""
    t = tt(d)
    f = mtof(m)
    y = np.zeros(len(t))
    k = 1
    while f * k < 9000:
        y += np.sin(2 * np.pi * f * k * t) / k * (0.9 if k == 1 else 1)
        k += 2
    y += 0.5 * np.sin(2 * np.pi * f * 2 * t)
    env = np.minimum(t / 0.003, 1) * np.exp(-t / 0.13)
    return lp(y, 5200) * env * 0.55


# ---------------------------------------------------------------- sound effects
def pop(f0=900, f1=320, d=0.09):
    t = tt(d)
    f = f1 + (f0 - f1) * np.exp(-t / 0.018)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.035)


def click(d=0.012, lo=2000, hi=6000):
    t = tt(d)
    return bp(rng.standard_normal(len(t)), lo, hi) * np.exp(-t / 0.0025)


def whoosh(d=0.45, up=True):
    t = tt(d)
    n = rng.standard_normal(len(t))
    segs = 8
    out = np.zeros(len(t))
    for i in range(segs):
        c = 600 * (2 ** (i * 0.55)) if up else 600 * (2 ** ((segs - 1 - i) * 0.55))
        band = bp(n, max(80, c * 0.6), min(SR / 2 - 100, c * 1.7))
        w = np.exp(-((t / d - (i + 0.5) / segs) ** 2) / 0.02)
        out += band * w
    env = np.sin(np.pi * np.clip(t / d, 0, 1)) ** 1.5
    return out * env * 0.8


def riser(d):
    t = tt(d)
    n = rng.standard_normal(len(t))
    out = np.zeros(len(t))
    for i in range(10):
        c = 400 * (2 ** (i * 0.5))
        w = np.exp(-((t / d - i / 9) ** 2) / 0.015)
        out += hp(n, c) * w
    sweep = np.sin(2 * np.pi * np.cumsum(220 * 2 ** (2.5 * t / d)) / SR) * 0.25
    env = (t / d) ** 2
    return (out * 0.6 + sweep) * env


def rev_cymbal(d=0.8):
    t = tt(d)
    return hp(rng.standard_normal(len(t)), 5000) * (t / d) ** 3 * 0.8


def boom(d=1.2):
    t = tt(d)
    f = 38 + 60 * np.exp(-t / 0.06)
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.45)
    return np.tanh(2 * y)


def ding(notes=(88, 95), d=0.9):
    t = tt(d)
    y = np.zeros(len(t))
    for k, m in enumerate(notes):
        i = int(k * 0.09 * SR)
        f = mtof(m)
        tone = (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t / 0.08)) * np.exp(-t / 0.35)
        y[i:] += tone[: len(t) - i]
    return y * 0.5


def buzz(d=0.32):
    t = tt(d)
    y = np.sign(np.sin(2 * np.pi * 116 * t)) + np.sign(np.sin(2 * np.pi * 123 * t))
    y = lp(y, 1800) * (0.6 + 0.4 * np.sign(np.sin(2 * np.pi * 18 * t))) * np.minimum((d - t) / 0.05, 1)
    return y * 0.28


def stamp():
    t = tt(0.3)
    y = np.sin(2 * np.pi * 75 * t) * np.exp(-t / 0.07) + 0.5 * bp(rng.standard_normal(len(t)), 300, 2000) * np.exp(-t / 0.02)
    return np.tanh(1.5 * y)


def tick():
    t = tt(0.03)
    return np.sin(2 * np.pi * 2400 * t) * np.exp(-t / 0.006) * 0.6


def flap_clatter(d=0.32):
    out = np.zeros(int(SR * d))
    for k in range(int(d / 0.05)):
        c = click(0.02, 900, 4500) * (0.7 + 0.3 * rng.random())
        i = int(k * 0.05 * SR + rng.random() * 0.006 * SR)
        n = min(len(c), len(out) - i)
        out[i : i + n] += c[:n]
    return out


def sparkle(d=1.6, n=60):
    out = np.zeros(int(SR * d))
    for _ in range(n):
        f = mtof(rng.integers(88, 103))
        t = tt(0.12)
        s = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.03) * (0.15 + 0.25 * rng.random())
        i = int(rng.random() * (d - 0.15) * SR * (rng.random() ** 0.6))
        out[i : i + len(s)] += s
    return out


# ---------------------------------------------------------------- arrangement
CH = {
    "C": [60, 64, 67, 72], "G": [59, 62, 67, 71], "Am": [57, 60, 64, 69], "F": [57, 60, 65, 69],
}
ROOT = {"C": 36, "G": 31, "Am": 33, "F": 29}
PROG = ["F", "G", "C", "G", "Am", "F", "C", "G", "Am", "F", "C", "G", "Am", "F", "C", "G"]
HOOK = {
    "C": [76, None, 79, None, 81, 79, 76, None],
    "G": [74, None, 79, None, 83, 81, 79, None],
    "Am": [72, None, 76, None, 81, 79, 76, None],
    "F": [77, None, 81, None, 84, 81, 79, None],
}

drums, music, lead, fx, verb = Bus(), Bus(), Bus(), Bus(), Bus()
K, CL, SN, HC, HO, SH = kick(), clap(), snare(), hat(), hat(True), shaker()
kick_times = []


def bar_time(b):
    return b * BAR


for b in range(16):
    t0 = bar_time(b)
    ch = PROG[b]
    intro = b < 2
    breakdown = b in (8, 9)
    groove_b = b in (5, 6, 7, 11, 12)
    final = b >= 13
    # ---- drums
    for beat in range(4):
        tb = t0 + beat * BEAT
        if b == 15 and tb >= 31.0:
            continue
        if intro:
            if b == 0 and beat in (0, 2):
                drums.add(lp(K, 600), tb, 0.6)
                kick_times.append(tb)
        elif not breakdown:
            drums.add(K, tb, 0.78)
            kick_times.append(tb)
            if beat in (1, 3):
                drums.add(CL, tb, 0.42, pan=0.05)
            drums.add(HO, tb + BEAT / 2, 0.22, pan=0.25)
        for s16 in range(4):
            if intro and b == 0:
                continue
            ts = tb + s16 * BEAT / 4
            vel = 0.13 if s16 % 2 else 0.2
            drums.add(HC, ts, vel * (0.6 if breakdown else 1), pan=-0.3)
            if groove_b or final:
                drums.add(SH, ts + 0.01, 0.12, pan=0.45)
    # ---- music
    if breakdown:
        music.add(pad(CH[ch], BAR), t0, 0.32)
        music.add(bass_long(ROOT[ch] + 12, BAR - 0.05), t0, 0.35)
        verb.add(pad(CH[ch], BAR), t0, 0.12)
    else:
        bright = 0.45 if intro else 1.0
        for slot in (0, 3, 6):
            ts = t0 + slot * BEAT / 2
            if b == 15 and ts >= 31.0:
                continue
            g = 0.22 if intro else 0.3
            s = stab(CH[ch], 0.4, bright)
            music.add(lp(s, 900) if intro else s, ts, g, pan=-0.15)
            verb.add(s, ts, 0.06)
        if not intro:
            for slot in (1, 3, 5, 7):
                ts = t0 + slot * BEAT / 2
                if b == 15 and ts >= 31.0:
                    continue
                music.add(bass_note(ROOT[ch] + (12 if slot == 5 else 0)), ts, 0.42)
    # ---- hook lead
    if b in (3, 4, 5, 6, 7) or b >= 10:
        pat = HOOK[ch]
        for slot, m in enumerate(pat):
            if m is None:
                continue
            ts = t0 + slot * BEAT / 2
            if b == 15 and ts >= 31.0:
                continue
            p = pluck(m)
            lead.add(p, ts, 0.3, pan=0.1)
            lead.add(p, ts + 0.375, 0.12, pan=-0.6)  # dotted-eighth echo
            lead.add(p, ts + 0.75, 0.05, pan=0.6)
            verb.add(p, ts, 0.08)

# intro build and drops
for k in range(8):
    drums.add(SN, 2.0 + k * BEAT / 2, 0.08 + 0.02 * k)
for k in range(12):
    drums.add(SN, 3.0 + k * BEAT / 4, 0.18 + 0.02 * k)
fx.add(riser(1.95), 2.0, 0.5)
fx.add(rev_cymbal(0.75), 3.25, 0.5)
for td in (4.0, 20.0, 26.0, 28.0):
    drums.add(crash(), td, 0.55)
fx.add(boom(), 4.0, 0.7)
# breakdown build back into 20 s
fx.add(riser(1.45), 18.5, 0.45)
for k in range(16):
    drums.add(SN, 19.0 + k * BEAT / 8 * 0.96, 0.1 + 0.015 * k)
fx.add(rev_cymbal(0.6), 19.4, 0.45)

# final hit at 31 s: stab + boom + crash, with a long tail
music.add(stab(CH["C"], 1.4), 31.0, 0.45)
verb.add(stab(CH["C"], 1.4), 31.0, 0.25)
drums.add(K, 31.0, 0.95)
drums.add(crash(1.0), 31.0, 0.5)
fx.add(boom(1.0), 31.0, 0.5)
kick_times.append(31.0)

def crowd(d):
    """A stadium roar: many amplitude-wobbling vocal-range bands, spread across the stereo field."""
    t = tt(d)
    out = np.zeros((len(t), 2))
    for _ in range(28):
        c = rng.uniform(320, 1500)
        band = bp(rng.standard_normal(len(t)), c * 0.75, c * 1.3)
        lfo = 0.55 + 0.45 * np.sin(2 * np.pi * rng.uniform(0.4, 1.6) * t + rng.uniform(0, 6.28))
        a = (rng.uniform(-0.9, 0.9) + 1) * np.pi / 4
        out[:, 0] += band * lfo * np.cos(a)
        out[:, 1] += band * lfo * np.sin(a)
    env = np.minimum(t / (0.35 * d), 1) * np.minimum((d - t) / (0.45 * d), 1)
    return out * env[:, None] / 6


fx.add(crowd(2.6), 3.6, 0.55)
fx.add(crowd(3.2), 27.9, 0.5)
if CUT is None:
    fx.add(whoosh(0.4), 5.7, 0.35)

    # ---------------------------------------------------------------- SFX on the video's cues
    B = lambda n: n * 0.5
    for i, tp in enumerate([B(2), B(2.5), B(3), B(3.5), B(4), B(4.5), B(5), B(5.5)]):
        fx.add(pop(800 + 60 * i, 300 + 20 * i), tp, 0.33, pan=(-0.5 if i % 2 else 0.5))
    fx.add(stamp(), B(6), 0.5)
    fx.add(whoosh(0.5, up=False), 3.5, 0.4)
    # C: typing, chips, button, plan
    sentence_len = len("A week in London, end of November, Arsenal twice")
    for k in range(0, sentence_len, 2):
        fx.add(click(), 6.3 + 1.5 * k / sentence_len, 0.22 + 0.08 * rng.random(), pan=0.2)
    for i, tp in enumerate([7.9, 8.025, 8.15, 8.275]):
        fx.add(pop(1000 + 80 * i, 420), tp, 0.28, pan=0.3)
    fx.add(click(0.02, 800, 3000), 8.5, 0.6)
    fx.add(whoosh(0.4), 8.55, 0.3)
    for tp in (8.95, 9.2):
        fx.add(tick(), tp, 0.35)
    # D: tickets
    fx.add(whoosh(0.35), 9.85, 0.35)
    for i in range(5):
        fx.add(tick(), 10.75 + i * 0.25, 0.4)
    fx.add(stamp(), 11.75, 0.55)
    link_len = len("tickets-arsenal-•••.shop")
    for k in range(0, link_len, 2):
        fx.add(click(), 12.65 + 0.65 * k / link_len, 0.22, pan=-0.2)
    fx.add(buzz(), 13.4, 0.55)
    fx.add(ding((84, 91)), 14.1, 0.45)
    fx.add(whoosh(0.35), 14.35, 0.3)
    for i, tp in enumerate([14.7, 14.85, 15.0]):
        fx.add(pop(700 + 100 * i, 300), tp, 0.25)
    # E: alerts
    fx.add(whoosh(0.4, up=False), 15.85, 0.35)
    fx.add(ding(), 17.0, 0.55)
    fx.add(ding((86, 93)), 18.0, 0.5)
    fx.add(click(0.02, 800, 3000), 18.75, 0.5)
    # F: on the day
    fx.add(whoosh(0.35), 19.85, 0.3)
    for k in range(10):
        fx.add(tick(), 20.3 + k * 0.12, 0.18)
    for i, tp in enumerate([20.5, 20.75, 21.0, 21.25]):
        fx.add(pop(900 + 70 * i, 350), tp, 0.3, pan=(-0.4 + 0.27 * i))
    # G1: counters
    fx.add(whoosh(0.35), 21.85, 0.3)
    for i, tp in enumerate([22.0, 22.5, 23.0, 23.5]):
        fx.add(boom(0.5), tp, 0.35)
        fx.add(pop(600 + 80 * i, 200), tp, 0.25)
    # G2: split-flap rows
    fx.add(whoosh(0.35), 23.85, 0.3)
    for i in range(6):
        fx.add(flap_clatter(0.33), 24.15 + i * 0.25, 0.45, pan=(-0.3 + 0.12 * i))
    # H1: price
    fx.add(whoosh(0.35), 25.85, 0.3)
    for i, tp in enumerate([27.0, 27.25, 27.5]):
        fx.add(pop(1100 + 120 * i, 500), tp, 0.32)
    fx.add(stamp(), 27.75, 0.45)
    # H2: end card + confetti
    fx.add(boom(), 28.0, 0.55)
    fx.add(sparkle(), 28.05, 0.6, pan=0.0)
    fx.add(ding((91, 96)), 29.1, 0.25)

# ---------------------------------------------------------------- mix
def duck_env():
    env = np.ones(len(drums.y))
    t = np.arange(int(SR * 0.3)) / SR
    shape = 1 - 0.55 * np.exp(-t / 0.11)
    for tk in kick_times:
        i = int(tk * SR)
        n = min(len(shape), len(env) - i)
        env[i : i + n] = np.minimum(env[i : i + n], shape[:n])
    return env[:, None]


def reverb_ir(d=1.8):
    t = tt(d)
    ir = rng.standard_normal((len(t), 2)) * np.exp(-t / 0.45)[:, None]
    ir = lp(ir.T, 7000).T
    ir[: int(0.012 * SR)] = 0
    return ir / np.sqrt((ir ** 2).sum(axis=0))


duck = duck_env()
music_y = music.y * duck
lead_y = lead.y * (0.55 + 0.45 * duck)
ir = reverb_ir()
wet = np.stack([fftconvolve(verb.y[:, c], ir[:, c])[: len(verb.y)] for c in range(2)], axis=1)
fx_wet = np.stack([fftconvolve(fx.y[:, c], ir[:, c])[: len(fx.y)] for c in range(2)], axis=1)

bed = drums.y * 1.0 + music_y * 1.0 + lead_y * 1.0 + wet * 0.55
# the bed stays a little darker until the drop; effects stay crisp
lpf = np.stack([lp(bed[:, c], 2200) for c in range(2)], axis=1)
xf = np.clip((np.arange(len(bed)) / SR - 3.0) / 1.0, 0, 1)[:, None]
bed = lpf * (1 - xf) + bed * xf
mix = bed + fx.y * 0.9 + fx_wet * 0.12
mix = hp(mix.T, 35).T

if CUT is None:
    mix = mix[:N]
    # fade the last half second after the final hit
    fade = np.ones(N)
    fade[int(31.4 * SR) :] = np.linspace(1, 0, N - int(31.4 * SR)) ** 1.5
    mix *= fade[:, None]
else:
    s0, d = CUT
    n = int(d * SR)
    mix = mix[int(s0 * SR) : int(s0 * SR) + n].copy()
    sfx = Bus()
    for c in CUES:
        t0, kind, dur = c["t"], c["type"], c.get("dur", 0.4)
        if kind == "open":
            sfx.add(whoosh(0.7), max(0.0, t0 - 0.05), 0.25)
        elif kind == "slam":
            sfx.add(whoosh(0.3, up=False), max(0.0, t0 - 0.12), 0.28)
            sfx.add(boom(0.45), t0 + 0.02, 0.22)
        elif kind == "pop":
            sfx.add(pop(950 + 200 * rng.random(), 340), t0, 0.26)
        elif kind == "tick":
            sfx.add(tick(), t0, 0.28)
        elif kind == "stamp":
            sfx.add(stamp(), t0 + 0.04, 0.5)
        elif kind == "whoosh":
            sfx.add(whoosh(0.6), t0, 0.3)
        elif kind == "typing":
            k = 0.0
            while k < dur:
                sfx.add(click(), t0 + k, 0.2 + 0.08 * rng.random(), pan=0.2)
                k += 0.055
    mix += sfx.y[:n] * 0.9
    fin = np.minimum(np.arange(n) / (0.015 * SR), 1)
    fout = np.clip((d - np.arange(n) / SR) / 0.7, 0, 1) ** 1.3
    mix *= (fin * fout)[:, None]
    N = n
# glue + limiter-ish
mix = np.tanh(1.25 * mix / np.max(np.abs(mix)) * 1.6) / np.tanh(1.25 * 1.6)
mix *= 10 ** (-1.0 / 20) / np.max(np.abs(mix))

wavfile.write(OUT, SR, (mix * 32767).astype(np.int16))
rms = np.sqrt(np.mean(mix ** 2))
print(f"wrote {OUT}: {len(mix) / SR:.1f}s, peak -1 dBFS, rms {20 * np.log10(rms):.1f} dBFS")
