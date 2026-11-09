"""Original music and sound effects for the Venuvoy videos: a 120 BPM pop-house cue, synthesized here
(nothing to license), arranged bar by bar from a form so the music is written to each cut's length
instead of being sliced out of a longer track.

    python3 src/audio/compose.py out.wav --form "hook,lift,groove*3,..." --dur 35 --outro 31 --cues cues.json
    python3 src/audio/compose.py out.wav --form "pulse,lift,groove*2" --dur 8 --outro 6 --cues cues.json --sparse

One bar is 2 s; NAME*3 repeats a section, and the last section repeats to fill --dur.
  intro    soft kicks and darker stabs; the bed is low-passed until the first full bar
  hush     pads, a long bass note, soft bells and hats, no kick: room for a quiet picture
  pulse    hush plus a muted heartbeat kick on every beat: anticipation
  hook     drums, bass and stabs at full energy, no lead: something to hear on the first frame
  lift     hook plus the lead, with a crash and a boom on the downbeat
  groove   drums, bass, stabs and the lead
  groove2  groove plus a shaker
  break    drums out: pad, long bass, quiet hats; the next bar gets a riser and a snare build
  drop     groove2 with a crash and a boom on the downbeat
  final    groove2, with a crash on the first final bar
--outro T  drums, bass and lead stop at T on a stinger (stab, kick, crash, boom) and a quiet pad holds
           under the call to action; without it the last 0.6 s fade out.
--cues     one effect per visible event from the page's own list (see place_cues); --sparse thins an
           8 s ad's list to the first slam, stamps, typing and the call to action.
"""
import json
import math
import sys

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, fftconvolve, sosfilt

ARGS = sys.argv[1:]
OUT = ARGS[0] if ARGS and not ARGS[0].startswith("--") else "music.wav"
opt = lambda name, dflt=None: ARGS[ARGS.index(name) + 1] if name in ARGS else dflt
OUTRO = float(opt("--outro")) if "--outro" in ARGS else None
SPARSE = "--sparse" in ARGS
CUES = json.load(open(opt("--cues"))) if "--cues" in ARGS else []

SR = 44100
BPM = 120
BEAT = 60 / BPM
BAR = 4 * BEAT


def parse_form(text):
    out = []
    for part in text.split(","):
        name, _, n = part.strip().partition("*")
        out += [name.strip()] * (int(n) if n else 1)
    return out


# the original 32 s reel: two intro bars, a drop at 4 s, a breakdown at 16-20 s and a final chorus
FORM = parse_form(opt("--form", "intro*2,drop,groove*2,groove2*3,break*2,drop,groove2*2,final*3"))
DUR = float(opt("--dur", len(FORM) * BAR))
FORM += [FORM[-1]] * max(0, math.ceil(DUR / BAR) - len(FORM))
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
        if i < 0:  # starts before 0 s: keep only the part that is audible
            sig, i = sig[-i:], 0
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


def bell(m, d=1.6):
    """Soft glass bell for quiet bars: a few inharmonic partials with a long decay."""
    t = tt(d)
    f = mtof(m)
    y = sum(a * np.sin(2 * np.pi * f * r * t) * np.exp(-t / (0.7 / r ** 0.5)) for r, a in ((1, 1.0), (2.76, 0.32), (5.4, 0.12)))
    return y * np.minimum(t / 0.004, 1) * 0.5


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
HOOK = {
    "C": [76, None, 79, None, 81, 79, 76, None],
    "G": [74, None, 79, None, 83, 81, 79, None],
    "Am": [72, None, 76, None, 81, 79, 76, None],
    "F": [77, None, 81, None, 84, 81, 79, None],
}
FULL = {"hook", "lift", "groove", "groove2", "drop", "final"}
LEAD = {"lift", "groove", "groove2", "drop", "final"}
SHAKE = {"groove2", "drop", "final"}
QUIET = {"hush", "pulse", "break"}
# C G Am F, turned so the first arrival (lift or drop) lands on C: the bar before it is F, a plagal lean in
LOOP = ["C", "G", "Am", "F"]
ARRIVE = next((i for i, f in enumerate(FORM) if f in ("lift", "drop")), 0)
PROG = [LOOP[(i - ARRIVE) % 4] for i in range(len(FORM))]

drums, music, lead, fx, verb = Bus(), Bus(), Bus(), Bus(), Bus()
K, CL, SN, HC, HO, SH = kick(), clap(), snare(), hat(), hat(True), shaker()
kick_times = []


def live(ts):
    return ts < DUR - 1e-6 and (OUTRO is None or ts < OUTRO - 1e-6)


for b, sec in enumerate(FORM):
    t0 = b * BAR
    if t0 >= DUR:
        break
    ch = PROG[b]
    # ---- drums
    for beat in range(4):
        tb = t0 + beat * BEAT
        if not live(tb):
            continue
        if sec == "intro":
            if beat in (0, 2):
                drums.add(lp(K, 600), tb, 0.6)
                kick_times.append(tb)
        elif sec == "pulse":
            drums.add(lp(K, 420), tb, 0.55)
            kick_times.append(tb)
        elif sec in FULL:
            drums.add(K, tb, 0.78)
            kick_times.append(tb)
            if beat in (1, 3):
                drums.add(CL, tb, 0.42, pan=0.05)
            drums.add(HO, tb + BEAT / 2, 0.22, pan=0.25)
        for s16 in range(4):
            if sec == "intro" and b == 0:
                continue
            if sec in ("hush", "pulse") and s16 % 2:
                continue
            ts = tb + s16 * BEAT / 4
            if not live(ts):
                continue
            vel = 0.13 if s16 % 2 else 0.2
            drums.add(HC, ts, vel * {"break": 0.6, "hush": 0.4, "pulse": 0.5}.get(sec, 1.0), pan=-0.3)
            if sec in SHAKE:
                drums.add(SH, ts + 0.01, 0.12, pan=0.45)
    # ---- harmony
    if sec in QUIET:
        d = min(BAR, (OUTRO if OUTRO is not None else DUR) - t0)
        if d > 0.2:
            music.add(pad(CH[ch], d), t0, {"hush": 0.3, "pulse": 0.3, "break": 0.22}[sec])
            music.add(bass_long(ROOT[ch] + 12, d - 0.05), t0, 0.28 if sec == "break" else 0.35)
            verb.add(pad(CH[ch], d), t0, 0.12)
        if sec in ("hush", "pulse"):  # slow bells keep a quiet bar moving
            for slot, m in ((0, CH[ch][-1] + 12), (4, CH[ch][1] + 12)):
                ts = t0 + slot * BEAT / 2
                if live(ts):
                    p = bell(m)
                    music.add(p, ts, 0.2, pan=-0.2 if slot else 0.2)
                    verb.add(p, ts, 0.16)
    else:
        bright = 0.45 if sec == "intro" else 1.0
        for slot in (0, 3, 6):
            ts = t0 + slot * BEAT / 2
            if not live(ts):
                continue
            st = stab(CH[ch], 0.4, bright)
            music.add(lp(st, 900) if sec == "intro" else st, ts, 0.22 if sec == "intro" else 0.3, pan=-0.15)
            verb.add(st, ts, 0.06)
        if sec != "intro":
            for slot in (1, 3, 5, 7):
                ts = t0 + slot * BEAT / 2
                if live(ts):
                    music.add(bass_note(ROOT[ch] + (12 if slot == 5 else 0)), ts, 0.42)
    # ---- hook lead
    if sec in LEAD:
        for slot, m in enumerate(HOOK[ch]):
            ts = t0 + slot * BEAT / 2
            if m is None or not live(ts):
                continue
            p = pluck(m)
            lead.add(p, ts, 0.3, pan=0.1)
            lead.add(p, ts + 0.375, 0.12, pan=-0.6)  # dotted-eighth echo
            lead.add(p, ts + 0.75, 0.05, pan=0.6)
            verb.add(p, ts, 0.08)
    # ---- arrivals: a crash and a boom on the downbeat, and a build in the bar before
    prev = FORM[b - 1] if b else None
    if sec in ("lift", "drop") and live(t0):
        drums.add(crash(), t0, 0.55)
        fx.add(boom(), t0, 0.6)
        if prev in ("intro", "hush", "pulse", "break"):
            fx.add(riser(1.45), t0 - 1.5, 0.32)
            for k in range(16):
                drums.add(SN, t0 - 1.0 + k * BEAT / 8 * 0.96, 0.06 + 0.012 * k)
            fx.add(rev_cymbal(0.6), t0 - 0.6, 0.4)
        elif prev is not None:
            for k in range(4):
                drums.add(SN, t0 - BEAT + k * BEAT / 4, 0.12 + 0.05 * k)
            fx.add(rev_cymbal(0.5), t0 - 0.5, 0.35)
    if sec == "final" and prev != "final" and live(t0):
        drums.add(crash(), t0, 0.5)

if OUTRO is not None and FORM[min(int(OUTRO // BAR), len(FORM) - 1)] in QUIET:
    # a quiet piece ends quietly: a bell chord over the pad, no kick, crash or boom
    for k, m in enumerate([72, 76, 79, 84]):
        music.add(bell(m, 2.4), OUTRO + k * 0.06, 0.22)
        verb.add(bell(m, 2.4), OUTRO + k * 0.06, 0.2)
elif OUTRO is not None:
    # the music lands on the end card: one stinger, then a quiet pad so the call to action sits in clear air
    music.add(stab(CH["C"], 1.8), OUTRO, 0.5)
    verb.add(stab(CH["C"], 1.8), OUTRO, 0.3)
    drums.add(K, OUTRO, 0.95)
    drums.add(crash(2.6), OUTRO, 0.55)
    fx.add(boom(1.2), OUTRO, 0.55)
    kick_times.append(OUTRO)
if OUTRO is not None:
    tail = DUR - OUTRO
    if tail > 0.3:
        # about 6 LU under the body: present enough to carry the end card, quiet enough to read over
        music.add(pad(CH["C"] + [48], tail), OUTRO + 0.05, 0.5)
        verb.add(pad(CH["C"], tail), OUTRO + 0.05, 0.2)
        for k, m in enumerate([79, 76, 84, 79, 72, 76]):
            ts = OUTRO + 1.0 + k * 0.5
            if ts < DUR - 0.5:
                music.add(bell(m), ts, 0.14, pan=(-0.3, 0.3)[k % 2])
                verb.add(bell(m), ts, 0.12)

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


def place_cues(bus, cues, sparse=False):
    """Effects from a page's cue list ({t, type, dur}); each one is tied to something on screen.
    sparse (8 s ads) keeps only the first slam, the stamps, typing and the call to action."""
    slams = [c for c in cues if c["type"] == "slam"]
    pops = [c for c in cues if c["type"] == "pop"]
    for c in cues:
        t0, kind, dur = c["t"], c["type"], c.get("dur", 0.4)
        if kind == "open":
            bus.add(whoosh(0.7), max(0.0, t0 - 0.05), 0.16)
        elif kind == "slam":
            if sparse and c is not slams[0]:
                continue
            bus.add(whoosh(0.3, up=False), max(0.0, t0 - 0.12), 0.28)
            bus.add(boom(0.45), t0 + 0.02, 0.22)
        elif kind == "pop":
            if sparse:
                if c is pops[-1]:
                    bus.add(ding((84, 91), 0.7), t0, 0.32)
                    bus.add(boom(0.5), t0, 0.18)
                continue
            bus.add(pop(950 + 200 * rng.random(), 340), t0, 0.26)
        elif kind == "tick":
            if not sparse:
                bus.add(tick(), t0, 0.28)
        elif kind == "stamp":
            bus.add(stamp(), t0 + 0.04, 0.5)
        elif kind == "whoosh":
            bus.add(whoosh(0.6), t0, 0.3)
        elif kind == "typing":
            # one soft keyboard texture, not a click per letter
            k = 0.0
            while k < dur:
                bus.add(lp(click(0.016, 1200, 5000), 4200), t0 + k, 0.1 + 0.05 * rng.random(), pan=0.2)
                k += 0.06 + 0.03 * rng.random()
        elif kind == "click":
            bus.add(click(0.02, 800, 3000), t0, 0.6)
        elif kind == "boom":
            bus.add(boom(0.8), t0, 0.45)
        elif kind == "buzz":
            bus.add(buzz(), t0, 0.5)
        elif kind == "chime":
            bus.add(ding((84, 91)), t0, 0.42)
        elif kind == "ding":
            bus.add(ding(), t0, 0.5)
        elif kind == "cta":
            bus.add(ding((84, 91), 0.7), t0, 0.34)
            bus.add(boom(0.5), t0, 0.2)
        elif kind == "flaps":
            # the board settling: one short clatter that thins out, not a burst per row
            k = 0.0
            while k < dur:
                bus.add(click(0.02, 900, 4500), t0 + k, 0.3 * (1 - 0.6 * k / max(dur, 0.1)), pan=-0.3 + 0.6 * k / max(dur, 0.1))
                k += 0.045 + 0.02 * rng.random()
        elif kind == "sparkle":
            bus.add(sparkle(), t0, 0.3)
        elif kind == "crowd":
            bus.add(crowd(dur), t0, 0.3)
        elif kind == "crash":
            bus.add(crash(1.6), t0, 0.4)
        elif kind == "swell":
            # untuned reversed cymbal whose peak lands exactly on the cut, well under the music
            bus.add(rev_cymbal(0.8), max(0.0, t0 - 0.8), 0.28)


place_cues(fx, CUES, sparse=SPARSE)

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
# an intro stays darker until the first full bar; effects stay crisp
n_intro = next((i for i, f in enumerate(FORM) if f != "intro"), len(FORM))
if n_intro:
    lpf = np.stack([lp(bed[:, c], 2200) for c in range(2)], axis=1)
    edge = n_intro * BAR
    xf = np.clip((np.arange(len(bed)) / SR - (edge - 1.0)) / 1.0, 0, 1)[:, None]
    bed = lpf * (1 - xf) + bed * xf
mix = bed + fx.y * 0.9 + fx_wet * 0.12
mix = hp(mix.T, 35).T[:N]
fade = np.clip((DUR - np.arange(N) / SR) / 0.6, 0, 1) ** 1.5
mix *= fade[:, None]
# gentle glue only: loudness and peaks are master.py's job, so keep the transients that make it move
mix = np.tanh(1.4 * mix / np.max(np.abs(mix))) / np.tanh(1.4)
mix *= 10 ** (-1.0 / 20) / np.max(np.abs(mix))

wavfile.write(OUT, SR, (mix * 32767).astype(np.int16))
rms = np.sqrt(np.mean(mix ** 2))
print(f"wrote {OUT}: {len(mix) / SR:.1f}s, peak -1 dBFS, rms {20 * np.log10(rms):.1f} dBFS")
