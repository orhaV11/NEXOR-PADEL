"""The Venuvoy sound kit: instruments, a stadium crowd choir and sound effects, all synthesized here so
nothing needs a licence. Every function returns a mono (n,) or stereo (n, 2) float array at SR.

    from kit import *                      # used by the spot scores (anthem.py, chaos.py, sports.py)
    m = Mix(30); m.add(stomp(), 0.0); m.add(choir([...]), 0.0); m.render("raw.wav")

The foley of the series "To Be Confirmed" is at the end of the file: dry, close, never a tune. Each returns a stereo
(n, 2) array at SR with its peak below 0.9 (the film engine, src/film/cut.py, resamples whatever the kit returns to
48 kHz and places it by name: { "synth": "thud", "at": "stamp", "gain_db": -8 }, arguments in "args"):

    thud()            a rubber stamp hitting paper on a desk: a low knock, a hard top, nothing rings (0.25 s)
    vote(i=0)         a soft tick for one vote in a poll; i = 0..6 lifts the pitch by a third of a semitone a step
    msg()             a quiet message pop, softer and lower than pop()
    sys_tick()        an even softer, duller tick for a system line ("Maya added Danny")
    clock_tick(tock=False)   one tick of a mantel clock; clock_tick(True) is the lower "tock" of the pair
    cup()             a china cup put down on its saucer: two small high clinks (0.3 s)
    two_note()        the series' closing figure: two low, soft, slow notes, a fourth apart, falling (1.6 s)

    python3 src/audio/kit.py                     # prints these seven sounds' length, peak, level, spectral centroid,
                                                 # share of energy under 300 Hz, level above 300 Hz and last 30 ms
    python3 src/audio/kit.py thud vote:6 pop     # the same for any sound of the kit ("vote:6" is vote(6))
    python3 src/audio/kit.py --wav DIR thud ...  # also writes DIR/<name>.wav to listen to
"""
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, fftconvolve, sosfilt

SR = 44100
_rng = np.random.default_rng(1234)


def R(seed=None):
    return _rng if seed is None else np.random.default_rng(seed)


def tt(d):
    return np.arange(max(1, int(SR * d))) / SR


def mtof(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def bp(x, lo, hi, order=2):
    lo, hi = max(20, lo), min(SR / 2 - 200, hi)
    return sosfilt(butter(order, [lo, hi], btype="band", fs=SR, output="sos"), x, axis=0)


def hp(x, f, order=2):
    return sosfilt(butter(order, f, btype="high", fs=SR, output="sos"), x, axis=0)


def lp(x, f, order=2):
    return sosfilt(butter(order, min(f, SR / 2 - 200), btype="low", fs=SR, output="sos"), x, axis=0)


def env(n, a=0.005, r=0.1, d=None):
    """Attack/release envelope of n samples (seconds)."""
    t = np.arange(n) / SR
    d = n / SR if d is None else d
    return np.clip(t / max(a, 1e-4), 0, 1) * np.clip((d - t) / max(r, 1e-4), 0, 1)


def stereo(x, pan=0.0):
    if x.ndim == 2:
        return x
    a = (pan + 1) * np.pi / 4
    return np.stack([x * np.cos(a), x * np.sin(a)], axis=1) * np.sqrt(2)


def reverb_ir(d=2.2, decay=0.6, damp=6000, pre=0.02, seed=5):
    r = R(seed)
    t = tt(d)
    ir = r.standard_normal((len(t), 2)) * np.exp(-t / decay)[:, None]
    ir = lp(ir, damp)
    ir[: int(pre * SR)] = 0
    return ir / np.sqrt((ir ** 2).sum(axis=0))


def convolve(x, ir):
    x = stereo(x)
    return np.stack([fftconvolve(x[:, c], ir[:, c])[: len(x)] for c in range(2)], axis=1)


class Mix:
    """A stereo timeline. add() places a signal at t seconds with gain and pan; sends feed the reverbs."""

    def __init__(self, dur):
        self.dur = dur
        self.n = int(SR * dur)
        self.dry = np.zeros((self.n + SR * 6, 2))
        self.room = np.zeros_like(self.dry)  # short room
        self.hall = np.zeros_like(self.dry)  # stadium-sized tail

    def _put(self, buf, sig, t, gain, pan):
        sig = stereo(np.asarray(sig, dtype=float), pan) * gain
        i = int(round(t * SR))
        if i < 0:
            sig, i = sig[-i:], 0
        n = min(len(sig), len(buf) - i)
        if n > 0:
            buf[i: i + n] += sig[:n]

    def add(self, sig, t, gain=1.0, pan=0.0, room=0.0, hall=0.0):
        self._put(self.dry, sig, t, gain, pan)
        if room:
            self._put(self.room, sig, t, gain * room, pan)
        if hall:
            self._put(self.hall, sig, t, gain * hall, pan)

    def mixdown(self, fade=0.6):
        y = self.dry + convolve(self.room, reverb_ir(0.9, 0.18, 7000, 0.008, 7)) * 0.9 + convolve(self.hall, reverb_ir(3.2, 0.9, 5200, 0.035, 9)) * 0.8
        y = hp(y, 30)[: self.n]
        f = np.clip((self.dur - np.arange(self.n) / SR) / fade, 0, 1) ** 1.5
        y *= f[:, None]
        y = np.tanh(1.3 * y / (np.max(np.abs(y)) + 1e-9)) / np.tanh(1.3)
        return y * 10 ** (-1 / 20) / np.max(np.abs(y))

    def render(self, path, fade=0.6):
        y = self.mixdown(fade)
        wavfile.write(path, SR, (y * 32767).astype(np.int16))
        print(f"wrote {path}: {len(y) / SR:.1f}s, rms {20 * np.log10(np.sqrt(np.mean(y ** 2))):.1f} dBFS")


# ------------------------------------------------------------------ drums
def kick(f1=150, f0=48, d=0.45, click=0.35):
    t = tt(d)
    f = f0 + (f1 - f0) * np.exp(-t / 0.03)
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.28)
    y += click * hp(R(1).standard_normal(len(t)), 2500) * np.exp(-t / 0.003)
    return hp(np.tanh(1.8 * y) * 0.9, 32)


def stomp(seed=2):
    """A stadium stomp: a big low body thump with wood/concrete knock."""
    t = tt(0.55)
    f = 46 + 90 * np.exp(-t / 0.045)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.22)
    knock = bp(R(seed).standard_normal(len(t)), 150, 900) * np.exp(-t / 0.03) * 0.9
    return np.tanh(1.6 * (body + knock)) * 0.95


def clap(seed=3, spread=0.012, layers=3):
    """A big crowd-sized clap: several claps a few ms apart."""
    t = tt(0.45)
    out = np.zeros(len(t))
    r = R(seed)
    for k in range(layers):
        n = r.standard_normal(len(t))
        e = np.zeros(len(t))
        for j, dd in enumerate([0.0, 0.009, 0.018]):
            i = int((dd + k * spread * r.random()) * SR)
            e[i:] += np.exp(-(t[: len(t) - i]) / (0.005 if j < 2 else 0.09))
        out += bp(n * e, 800 + 300 * k, 3800)
    return out / layers * 1.8


def snare(seed=4):
    t = tt(0.25)
    tone = np.sin(2 * np.pi * 185 * t) * np.exp(-t / 0.05)
    noise = bp(R(seed).standard_normal(len(t)), 1300, 8000) * np.exp(-t / 0.08)
    return tone * 0.6 + noise


def hat(open_=False, seed=5):
    t = tt(0.3 if open_ else 0.06)
    return hp(R(seed).standard_normal(len(t)), 7600, 4) * np.exp(-t / (0.12 if open_ else 0.017)) * 0.7


def shaker(seed=6):
    t = tt(0.08)
    return bp(R(seed).standard_normal(len(t)), 4500, 11000) * np.minimum(t / 0.012, 1) * np.exp(-t / 0.03) * 0.6


def crash(d=2.4, seed=7):
    t = tt(d)
    n = hp(R(seed).standard_normal((len(t), 2)), 3500)
    return n * np.exp(-t / 0.9)[:, None] * 0.6


def taiko(f0=62, d=1.1, seed=8):
    t = tt(d)
    f = f0 * (1 + 0.6 * np.exp(-t / 0.05))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.42)
    skin = bp(R(seed).standard_normal(len(t)), 120, 1100) * np.exp(-t / 0.06) * 0.7
    return np.tanh(1.5 * (body + skin))


def tom(f0=110, d=0.5, seed=9):
    t = tt(d)
    f = f0 * (1 + 0.35 * np.exp(-t / 0.04))
    return (np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.2) + 0.3 * bp(R(seed).standard_normal(len(t)), 200, 2000) * np.exp(-t / 0.03)) * 0.9


def timpani(m=40, d=1.6):
    t = tt(d)
    f = mtof(m)
    y = sum(a * np.sin(2 * np.pi * f * k * t) * np.exp(-t / (0.9 / k)) for k, a in ((1, 1), (1.5, 0.4), (1.98, 0.25), (2.44, 0.12)))
    return y * np.minimum(t / 0.004, 1) * 0.8


# ------------------------------------------------------------------ tonal
def saw(f, t, ph=0.0):
    return 2 * ((f * t + ph) % 1.0) - 1


def brass(notes, d, bright=1.0, seed=10):
    """A brass section chord: detuned saws through a filter that opens on the attack (the 'blat')."""
    t = tt(d)
    r = R(seed)
    y = np.zeros(len(t))
    for m in notes:
        f = mtof(m)
        vib = 1 + 0.004 * np.sin(2 * np.pi * 5.2 * t + r.random() * 6) * np.clip((t - 0.25) / 0.3, 0, 1)
        for dt in (-0.006, 0.0, 0.007):
            y += saw(f * (1 + dt), t * vib, r.random())
    y /= len(notes) * 3
    cut = 500 + (2600 * bright) * (1 - np.exp(-t / 0.05)) * np.exp(-t / 1.2) + 900 * bright
    # time-varying low-pass, applied in short blocks
    out = np.zeros_like(y)
    blk = 512
    zi = None
    for i in range(0, len(y), blk):
        sos = butter(2, min(cut[i], SR / 2 - 500), btype="low", fs=SR, output="sos")
        seg, zi = sosfilt(sos, y[i: i + blk], zi=zi if zi is not None else np.zeros((sos.shape[0], 2)))
        out[i: i + blk] = seg
    a = np.minimum(t / 0.035, 1) * np.clip((d - t) / 0.12, 0, 1) * (0.85 + 0.15 * np.exp(-t / 0.15))
    return np.tanh(2.0 * out * a) * 0.8


def strings(notes, d, seed=11):
    t = tt(d)
    r = R(seed)
    y = np.zeros(len(t))
    for m in notes:
        for dt in (-0.008, -0.003, 0.002, 0.006, 0.01):
            y += saw(mtof(m) * (1 + dt), t, r.random())
    y = lp(y / (len(notes) * 5), 2600)
    return y * np.minimum(t / 0.25, 1) * np.clip((d - t) / 0.3, 0, 1)


def pad(notes, d, cutoff=1500, seed=12):
    t = tt(d)
    r = R(seed)
    y = np.zeros(len(t))
    for m in notes:
        for dt in (-0.006, -0.002, 0.003, 0.007):
            y += saw(mtof(m) * (1 + dt), t, r.random())
    y = lp(y / (len(notes) * 4), cutoff)
    return y * np.minimum(t / 0.6, 1) * np.clip((d - t) / 0.4, 0, 1)


def sub(m, d):
    t = tt(d)
    return np.sin(2 * np.pi * mtof(m) * t) * np.minimum(t / 0.01, 1) * np.clip((d - t) / 0.05, 0, 1)


def bass_note(m, d=0.22):
    t = tt(d)
    f = mtof(m)
    y = lp(0.7 * saw(f, t) + 0.6 * np.sin(2 * np.pi * f * t), 900)
    return np.tanh(1.6 * y * np.minimum(t / 0.004, 1) * np.exp(-t / 0.13))


def keys(chord, d=0.45, bright=1.0, seed=13):
    t = tt(d)
    y = np.zeros(len(t))
    for m in chord:
        f = mtof(m)
        for h, a in ((1, 1.0), (2, 0.5 * bright), (3, 0.28 * bright), (4, 0.15 * bright)):
            y += a * np.sin(2 * np.pi * f * h * t) * np.exp(-t * (3 + 1.5 * h))
    y += 0.1 * bp(R(seed).standard_normal(len(t)), 1500, 5000) * np.exp(-t / 0.006)
    return y / len(chord) * 1.4 * np.minimum(t / 0.002, 1)


def bell(m, d=1.8):
    t = tt(d)
    f = mtof(m)
    y = sum(a * np.sin(2 * np.pi * f * k * t) * np.exp(-t / (0.8 / k ** 0.5)) for k, a in ((1, 1.0), (2.76, 0.32), (5.4, 0.12)))
    return y * np.minimum(t / 0.003, 1) * 0.5


def pluck(m, d=0.35):
    t = tt(d)
    f = mtof(m)
    y = np.zeros(len(t))
    k = 1
    while f * k < 9000:
        y += np.sin(2 * np.pi * f * k * t) / k
        k += 2
    return lp(y, 5200) * np.minimum(t / 0.003, 1) * np.exp(-t / 0.13) * 0.55


# ------------------------------------------------------------------ voices
VOWELS = {"o": (480, 860, 2600), "a": (720, 1150, 2500), "e": (520, 1760, 2500), "u": (330, 760, 2400), "i": (290, 2200, 2900), "ey": (560, 1700, 2450)}


def _formants(x, vowel):
    f1, f2, f3 = VOWELS[vowel]
    return bp(x, f1 * 0.8, f1 * 1.2) + 0.55 * bp(x, f2 * 0.88, f2 * 1.12) + 0.22 * bp(x, f3 * 0.9, f3 * 1.1) + 0.06 * hp(x, 3500)


def choir(notes, voices=26, crowd=0.0, octave_up=0.25, seed=21):
    """Many people singing. notes: [(t, dur, midi, vowel)]. crowd 0..1 widens tuning and timing toward a
    terrace singing together. Returns stereo, starting at 0 s."""
    r = R(seed)
    total = max(t + d for t, d, _, _ in notes) + 0.6
    n = int(total * SR)
    bufs = {}
    for v in range(voices):
        cents = r.normal(0, 9 + 22 * crowd)
        lag = r.normal(0, 0.008 + 0.03 * crowd)
        pan = r.uniform(-0.85, 0.85)
        up = 12 if r.random() < octave_up else 0
        g = (0.55 if up else 1.0) * r.uniform(0.7, 1.0)
        vr, vd, vp = r.uniform(4.6, 6.2), r.uniform(0.003, 0.009), r.random() * 6.28
        for t0, d, m, vowel in notes:
            dd = d + 0.12
            tv = tt(dd)
            f = mtof(m + up) * 2 ** (cents / 1200)
            vib = 1 + vd * np.sin(2 * np.pi * vr * tv + vp) * np.clip((tv - 0.12) / 0.25, 0, 1)
            src = saw(1.0, np.cumsum(f * vib) / SR) + 0.25 * r.standard_normal(len(tv)) * (0.3 + crowd)
            a = np.clip(tv / r.uniform(0.035, 0.09), 0, 1) * np.clip((dd - tv) / 0.12, 0, 1)
            sig = src * a * g
            i = max(0, int((t0 + lag) * SR))
            L, Rr = bufs.setdefault(vowel, (np.zeros(n), np.zeros(n)))
            k = min(len(sig), n - i)
            ang = (pan + 1) * np.pi / 4
            L[i: i + k] += sig[:k] * np.cos(ang)
            Rr[i: i + k] += sig[:k] * np.sin(ang)
    out = np.zeros((n, 2))
    for vowel, (L, Rr) in bufs.items():
        out[:, 0] += _formants(L, vowel)
        out[:, 1] += _formants(Rr, vowel)
    out = lp(out, 6500)
    return out / (np.max(np.abs(out)) + 1e-9) * 0.9


def hey(seed=31, voices=40):
    """A crowd shouting 'hey!' together."""
    return choir([(0.0, 0.16, 52, "ey")], voices=voices, crowd=1.0, octave_up=0.3, seed=seed)


# ------------------------------------------------------------------ crowd beds
def crowd(d, energy=0.6, seed=41):
    """Stadium crowd: many wobbling vocal-band voices plus a noise wash; energy 0..1."""
    r = R(seed)
    t = tt(d)
    out = np.zeros((len(t), 2))
    for _ in range(34):
        c = r.uniform(300, 1700)
        band = bp(r.standard_normal(len(t)), c * 0.78, c * 1.25)
        lfo = 0.5 + 0.5 * np.sin(2 * np.pi * r.uniform(0.3, 1.8) * t + r.uniform(0, 6.28))
        a = (r.uniform(-0.95, 0.95) + 1) * np.pi / 4
        out[:, 0] += band * lfo * np.cos(a)
        out[:, 1] += band * lfo * np.sin(a)
    wash = lp(r.standard_normal((len(t), 2)), 2400) * 0.35
    out = out / 7 + wash
    out *= 0.4 + 0.6 * energy
    return out * np.minimum(t / 0.25, 1)[:, None] * np.clip((d - t) / 0.5, 0, 1)[:, None]


def roar(d=3.0, seed=42):
    """A swell: the crowd rising to a roar and settling."""
    t = tt(d)
    c = crowd(d, 1.0, seed)
    shape = np.minimum(t / (0.35 * d), 1) ** 1.5 * np.clip((d - t) / (0.55 * d), 0, 1)
    return c * shape[:, None] * 1.4


def cheer(d=3.5, seed=43):
    t = tt(d)
    c = crowd(d, 1.0, seed) + hp(R(seed).standard_normal((len(t), 2)), 1800) * 0.06
    shape = np.minimum(t / 0.08, 1) * np.exp(-t / (0.5 * d))
    return c * shape[:, None] * 1.5


# ------------------------------------------------------------------ transitions and UI
def whoosh(d=0.45, up=True, seed=51):
    t = tt(d)
    n = R(seed).standard_normal(len(t))
    out = np.zeros(len(t))
    segs = 8
    for i in range(segs):
        c = 600 * (2 ** ((i if up else segs - 1 - i) * 0.55))
        out += bp(n, c * 0.6, c * 1.7) * np.exp(-((t / d - (i + 0.5) / segs) ** 2) / 0.02)
    return out * np.sin(np.pi * np.clip(t / d, 0, 1)) ** 1.5 * 0.8


def whip(d=0.28, seed=52):
    """A fast pan: a short, bright noise sweep."""
    t = tt(d)
    n = R(seed).standard_normal(len(t))
    out = sum(bp(n, c * 0.7, c * 1.4) * np.exp(-((t / d - k / 7) ** 2) / 0.01) for k, c in enumerate([700, 1100, 1700, 2600, 3900, 5600, 7800]))
    return stereo(out * 0.9, 0) * np.stack([np.linspace(1, 0.3, len(t)), np.linspace(0.3, 1, len(t))], axis=1)


def riser(d=2.0, seed=53):
    t = tt(d)
    n = R(seed).standard_normal(len(t))
    out = sum(hp(n, 400 * 2 ** (i * 0.5)) * np.exp(-((t / d - i / 9) ** 2) / 0.015) for i in range(10))
    sweep = np.sin(2 * np.pi * np.cumsum(220 * 2 ** (2.5 * t / d)) / SR) * 0.22
    return (out * 0.55 + sweep) * (t / d) ** 2


def downlifter(d=1.2, seed=54):
    t = tt(d)
    sweep = np.sin(2 * np.pi * np.cumsum(900 * 2 ** (-3 * t / d)) / SR) * 0.3
    return (sweep + 0.3 * lp(R(seed).standard_normal(len(t)), 3000)) * np.exp(-t / (0.4 * d))


def rev_cymbal(d=0.8, seed=55):
    t = tt(d)
    return hp(R(seed).standard_normal(len(t)), 5000) * (t / d) ** 3 * 0.8


def boom(d=1.2):
    t = tt(d)
    f = 36 + 64 * np.exp(-t / 0.06)
    return np.tanh(2 * np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.45))


def impact(d=1.6, seed=56):
    """A trailer hit: sub boom, noise burst and a metallic ring."""
    t = tt(d)
    burst = lp(R(seed).standard_normal(len(t)), 5000) * np.exp(-t / 0.05) * 0.8
    ring = sum(np.sin(2 * np.pi * f * t) * np.exp(-t / 0.35) * a for f, a in ((211, 0.2), (347, 0.14), (523, 0.08)))
    return np.tanh(1.4 * (boom(d) + burst + ring))


def stamp(seed=57):
    t = tt(0.32)
    y = np.sin(2 * np.pi * 72 * t) * np.exp(-t / 0.07) + 0.6 * bp(R(seed).standard_normal(len(t)), 300, 2400) * np.exp(-t / 0.02)
    return np.tanh(1.6 * y)


def click(d=0.014, lo=1800, hi=6500, seed=58):
    t = tt(d)
    return bp(R(seed).standard_normal(len(t)), lo, hi) * np.exp(-t / 0.0028)


def thump(seed=59):
    """A button press: soft low tap with a tick on top."""
    t = tt(0.18)
    y = np.sin(2 * np.pi * (90 + 120 * np.exp(-t / 0.01)) * t) * np.exp(-t / 0.05) * 0.8
    c = click(0.01, 2500, 7000, seed) * 0.5
    y[: len(c)] += c
    return y


def typing(d=1.2, rate=0.07, seed=60):
    r = R(seed)
    out = np.zeros(int(SR * (d + 0.1)))
    k = 0.0
    while k < d:
        c = lp(click(0.016, 1200, 5200, int(r.integers(1e6))), 4500) * (0.6 + 0.4 * r.random())
        i = int(k * SR)
        out[i: i + len(c)] += c[: len(out) - i]
        k += rate * (0.6 + 0.8 * r.random())
    return out


def pop(f0=950, f1=330, d=0.09):
    t = tt(d)
    f = f1 + (f0 - f1) * np.exp(-t / 0.018)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.035)


def ding(notes=(88, 95), d=1.0):
    t = tt(d)
    y = np.zeros(len(t))
    for k, m in enumerate(notes):
        i = int(k * 0.085 * SR)
        f = mtof(m)
        tone = (np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * f * 2.76 * t) * np.exp(-t / 0.08)) * np.exp(-t / 0.35)
        y[i:] += tone[: len(t) - i]
    return y * 0.5


def notif():
    """A messaging-app arrival: two quick soft tones."""
    return np.concatenate([pop(1300, 1050, 0.07) * 0.8, np.zeros(int(0.03 * SR)), pop(1700, 1450, 0.09)])


def buzz(d=0.38):
    """A phone vibrating on a table."""
    t = tt(d)
    y = np.sign(np.sin(2 * np.pi * 150 * t)) * (0.55 + 0.45 * np.sign(np.sin(2 * np.pi * 22 * t)))
    return lp(y, 1600) * np.clip((d - t) / 0.04, 0, 1) * 0.35


def error_bonk():
    t = tt(0.22)
    return np.sin(2 * np.pi * (330 - 120 * t / 0.22) * t) * np.exp(-t / 0.08) * 0.8


def airport_chime():
    """The classic terminal announcement chime: two soft bell tones falling a major third."""
    return np.concatenate([bell(84, 0.55)[: int(0.42 * SR)], bell(80, 1.4)])


def whistle(d=0.7, seed=61):
    """A referee's whistle: a pea-rattled tone with breath."""
    t = tt(d)
    f = 3150 + 60 * np.sin(2 * np.pi * 34 * t)
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR) * (0.65 + 0.35 * np.sin(2 * np.pi * 34 * t + 1))
    breath = bp(R(seed).standard_normal(len(t)), 2400, 6000) * 0.25
    a = np.minimum(t / 0.02, 1) * np.clip((d - t) / 0.06, 0, 1)
    return (tone + breath) * a * 0.55


def jet_flyby(d=1.8, seed=62):
    """A plane passing: filtered roar with a Doppler whine and a left-to-right pan."""
    t = tt(d)
    x = (t / d - 0.5) * 2
    dop = 1 + 0.18 * (-x) / np.sqrt(x ** 2 + 0.05)
    whine = np.sin(2 * np.pi * np.cumsum(1250 * dop) / SR) * 0.12
    roar_ = lp(R(seed).standard_normal(len(t)), 1800) * 0.9 + hp(R(seed + 1).standard_normal(len(t)), 3000) * 0.15
    amp = 1 / (x ** 2 * 6 + 0.35)
    y = (roar_ + whine) * amp / amp.max()
    p = np.clip(x, -1, 1)
    return np.stack([y * np.cos((p + 1) * np.pi / 4), y * np.sin((p + 1) * np.pi / 4)], axis=1) * 1.2


def rip(d=0.32, seed=63):
    """Paper tearing: a crackle of short band-passed bursts."""
    r = R(seed)
    out = np.zeros(int(SR * d))
    k = 0.0
    while k < d - 0.01:
        c = bp(r.standard_normal(int(0.012 * SR)), 1500, 7000) * np.exp(-np.arange(int(0.012 * SR)) / SR / 0.003)
        i = int(k * SR)
        out[i: i + len(c)] += c[: len(out) - i] * (0.4 + 0.6 * r.random())
        k += 0.004 + 0.01 * r.random()
    return out * np.linspace(1, 0.4, len(out))


def shutter():
    return np.concatenate([click(0.01, 1500, 6000, 70), np.zeros(int(0.05 * SR)), click(0.016, 900, 4000, 71) * 0.8])


def record_scratch(d=0.55):
    """A turntable scratch: noise through a fast back-and-forth pitch sweep."""
    t = tt(d)
    ph = np.cumsum(1 + 3.2 * np.sin(2 * np.pi * 3.6 * t) * np.exp(-t / 0.3)) / SR
    src = np.interp((ph * 3000) % 4000, np.arange(4000), R(72).standard_normal(4000))
    return bp(src, 500, 4500) * np.exp(-t / 0.25) * 1.2


def tape_stop(x, d=0.6):
    """Slows the end of a signal to a stop (for a dramatic cut)."""
    n = min(len(x), int(d * SR))
    head, tail = x[:-n], x[-n:]
    rate = np.linspace(1, 0, n) ** 1.3
    pos = np.cumsum(rate)
    pos = pos / pos[-1] * (n - 1) * 0.5
    tail2 = np.stack([np.interp(pos, np.arange(n), tail[:, c]) for c in range(tail.shape[1])], axis=1) if tail.ndim == 2 else np.interp(pos, np.arange(n), tail)
    return np.concatenate([head, tail2 * np.linspace(1, 0, n)[:, None] if tail.ndim == 2 else tail2 * np.linspace(1, 0, n)])


def glitch(d=0.25, seed=73):
    r = R(seed)
    t = tt(d)
    y = np.sign(np.sin(2 * np.pi * r.uniform(200, 800) * t)) * 0.3 + bp(r.standard_normal(len(t)), 1000, 6000) * 0.5
    gate = (np.sin(2 * np.pi * 34 * t) > 0).astype(float)
    return y * gate * np.exp(-t / 0.15)


def firework(seed=74):
    """A burst far away: a dull thump then a cascade of crackles."""
    r = R(seed)
    t = tt(2.0)
    th = np.sin(2 * np.pi * np.cumsum(60 + 80 * np.exp(-t / 0.05)) / SR) * np.exp(-t / 0.25) * 0.8
    cr = np.zeros(len(t))
    for _ in range(140):
        i = int((0.08 + r.random() ** 0.7 * 1.6) * SR)
        c = hp(r.standard_normal(int(0.006 * SR)), 2500) * r.uniform(0.2, 0.8)
        cr[i: i + len(c)] += c[: len(cr) - i]
    return lp(th, 400) + cr * np.exp(-t / 0.9) * 0.6


# ------------------------------------------------------------------ sport sounds
def ball_kick(seed=81):
    t = tt(0.3)
    slap = bp(R(seed).standard_normal(len(t)), 300, 3500) * np.exp(-t / 0.012)
    body = np.sin(2 * np.pi * (95 + 160 * np.exp(-t / 0.012)) * t) * np.exp(-t / 0.06)
    return np.tanh(1.5 * (slap * 0.8 + body))


def dribble(seed=82):
    t = tt(0.25)
    return np.sin(2 * np.pi * (120 + 90 * np.exp(-t / 0.01)) * t) * np.exp(-t / 0.05) + 0.4 * bp(R(seed).standard_normal(len(t)), 600, 3000) * np.exp(-t / 0.006)


def tennis_pock(seed=83):
    t = tt(0.16)
    return (np.sin(2 * np.pi * 980 * t) * 0.6 + np.sin(2 * np.pi * 1530 * t) * 0.3) * np.exp(-t / 0.018) + 0.5 * bp(R(seed).standard_normal(len(t)), 1500, 6000) * np.exp(-t / 0.004)


def golf_swing(seed=84):
    """Swing whoosh, the clean 'tink' of the strike, and the ball whistling away."""
    sw = whoosh(0.35, True, seed) * 0.6
    t = tt(0.9)
    tink = sum(np.sin(2 * np.pi * f * t) * np.exp(-t / dd) * a for f, dd, a in ((3400, 0.05, 0.7), (5100, 0.03, 0.35), (2200, 0.08, 0.3)))
    fly = np.sin(2 * np.pi * np.cumsum(2600 - 900 * t) / SR) * np.exp(-t / 0.35) * 0.06
    out = np.zeros(int(1.25 * SR))
    out[: len(sw)] += sw
    i = int(0.3 * SR)
    out[i: i + len(t)] += tink + fly
    return out


def engine_pass(d=1.3, seed=85):
    """A race car flying past: a buzzy engine harmonic stack with Doppler and pan."""
    t = tt(d)
    x = (t / d - 0.45) * 2
    dop = 1 + 0.22 * (-x) / np.sqrt(x ** 2 + 0.04)
    f = 380 * dop
    ph = np.cumsum(f) / SR
    y = sum(np.sin(2 * np.pi * k * ph) / k ** 0.8 for k in range(1, 14))
    y = np.tanh(1.8 * y) + 0.2 * bp(R(seed).standard_normal(len(t)), 800, 6000)
    amp = 1 / (x ** 2 * 7 + 0.25)
    y = lp(y, 6000) * amp / amp.max()
    p = np.clip(x, -1, 1)
    return np.stack([y * np.cos((p + 1) * np.pi / 4), y * np.sin((p + 1) * np.pi / 4)], axis=1) * 0.7


def rugby_thud(seed=86):
    t = tt(0.4)
    return np.tanh(2 * (np.sin(2 * np.pi * (70 + 60 * np.exp(-t / 0.02)) * t) * np.exp(-t / 0.09) + 0.5 * bp(R(seed).standard_normal(len(t)), 120, 900) * np.exp(-t / 0.03)))


def swish():
    """A basketball through the net."""
    t = tt(0.35)
    return bp(R(87).standard_normal(len(t)), 2500, 9000) * np.sin(np.pi * np.clip(t / 0.3, 0, 1)) ** 2 * 0.6


def buzzer(d=0.7):
    t = tt(d)
    y = np.sign(np.sin(2 * np.pi * 420 * t)) + 0.5 * np.sign(np.sin(2 * np.pi * 633 * t))
    return lp(y, 3500) * np.clip((d - t) / 0.05, 0, 1) * np.minimum(t / 0.01, 1) * 0.25


# ------------------------------------------------------------------ the sonic logo
def sonic_logo(seed=91):
    """Venuvoy's sound: a brass 'da-DAAA' up a fourth (G to C), the crowd's 'hey!', a bell sparkle on top
    and a low hit underneath. About 2 s; the same in every spot."""
    m = np.zeros((int(2.6 * SR), 2))

    def put(sig, t, g=1.0, pan=0.0):
        s = stereo(sig, pan) * g
        i = int(t * SR)
        k = min(len(s), len(m) - i)
        m[i: i + k] += s[:k]

    put(brass([55, 62, 67], 0.16, 1.2), 0.0, 0.8)
    put(brass([60, 64, 67, 72], 1.5, 1.3), 0.19, 1.0)
    put(sub(36, 1.2), 0.19, 0.55)
    put(boom(1.2), 0.19, 0.6)
    put(hey(seed), 0.2, 0.5)
    for k, n in enumerate((84, 88, 91, 96)):
        put(bell(n, 1.6), 0.24 + k * 0.05, 0.16, -0.3 + 0.2 * k)
    return m


# ------------------------------------------------------------------ comedy
def alarm(d=0.7):
    """A cheap website's urgency alarm: harsh two-tone beeping."""
    t = tt(d)
    f = np.where((t * 9).astype(int) % 2 == 0, 1250, 940)
    y = np.sign(np.sin(2 * np.pi * np.cumsum(f) / SR)) * 0.3
    return lp(y, 4200) * np.clip((d - t) / 0.03, 0, 1)


def scribble(d=0.5, seed=75):
    """A marker scribbling something out."""
    t = tt(d)
    n = bp(R(seed).standard_normal(len(t)), 1500, 6500)
    strokes = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 11 * t + 3 * np.sin(2 * np.pi * 3 * t)))
    return n * strokes * 0.5 * np.clip((d - t) / 0.05, 0, 1)


def beeps(n=6, gap=0.06):
    """Calculator keys."""
    out = []
    for k in range(n):
        tb = tt(0.045)
        out.append(np.sin(2 * np.pi * (1700 + 260 * (k % 3)) * tb) * np.exp(-tb / 0.02) * 0.5)
        out.append(np.zeros(int(gap * SR)))
    return np.concatenate(out)


def plop():
    """A golf ball dropping into the cup: a hollow knock and a little rattle."""
    t = tt(0.35)
    knock = np.sin(2 * np.pi * 620 * t) * np.exp(-t / 0.03) + 0.5 * np.sin(2 * np.pi * 940 * t) * np.exp(-t / 0.02)
    rattle = np.zeros(len(t))
    for k, d in enumerate((0.06, 0.1, 0.13, 0.15)):
        i = int(d * SR)
        c = np.sin(2 * np.pi * (700 + 60 * k) * t[: int(0.03 * SR)]) * np.exp(-t[: int(0.03 * SR)] / 0.008) * (0.5 - 0.1 * k)
        rattle[i: i + len(c)] += c
    return (knock + rattle) * 0.6


def ooh(d=1.2, seed=44):
    """The crowd going 'ooh': a quick vowel swell."""
    c = choir([(0.0, d * 0.8, 50, "u"), (0.0, d * 0.8, 55, "u")], voices=30, crowd=1.0, octave_up=0.3, seed=seed)
    n = len(c)
    sh = np.minimum(np.arange(n) / (0.15 * SR), 1) * np.exp(-np.arange(n) / (0.6 * SR))
    return c * sh[:, None]


# ------------------------------------------------------------------ "To Be Confirmed": dry, close foley
def _foley(y, peak):
    """Stereo, the last 4 ms faded so that nothing clicks, and the peak set (always below 0.9)."""
    y = stereo(np.array(y, dtype=float))
    n = min(len(y), int(0.004 * SR))
    y[-n:] *= np.linspace(1, 0, n)[:, None]
    return y / (np.max(np.abs(y)) + 1e-9) * min(peak, 0.89)


def _struck(t, at, partials, k=1.0):
    """Something small and hard being struck at second "at": decaying partials [(Hz, level, decay s, phase)]."""
    i = int(at * SR)
    u = t[: len(t) - i]
    out = np.zeros(len(t))
    out[i:] = sum(a * np.sin(2 * np.pi * f * k * u + ph) * np.exp(-u / d) for f, a, d, ph in partials)
    return out


def _burst(t, at, noise, decay):
    """Filtered noise let go at second "at" and gone in a few decays."""
    i = int(at * SR)
    out = np.zeros(len(t))
    out[i:] = noise[: len(t) - i] * np.exp(-t[: len(t) - i] / decay)
    return out


def thud(seed=92):
    """The series' stamp: a rubber stamp coming down on paper on a desk. A short low body (the desk), a hard top
    (rubber on paper), and nothing that rings: everything is gone in a quarter of a second."""
    t = tt(0.25)
    r = R(seed)
    f = 78 + 85 * np.exp(-t / 0.012)  # the desk: a knock that falls to 78 Hz
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.038)
    # the desk top's own short notes: the part of the knock that a phone's speaker can play
    wood = _struck(t, 0.0, ((212, 0.45, 0.016, 0.6), (345, 0.5, 0.02, 0.2), (540, 0.34, 0.011, 1.4), (810, 0.2, 0.007, 0.9)))
    slap = bp(r.standard_normal(len(t)), 700, 3800) * np.exp(-t / 0.006) * 1.5  # rubber on paper: the hard top
    paper = bp(r.standard_normal(len(t)), 350, 1600) * np.exp(-t / 0.014) * 0.5
    tick = hp(r.standard_normal(len(t)), 5000) * np.exp(-t / 0.0012) * 0.35
    y = np.tanh(1.6 * (body + wood) + 0.55 * (slap + paper + tick))  # one blow: the knock and the slap saturate together
    return _foley(y * np.clip((0.25 - t) / 0.06, 0, 1), 0.88)


def vote(i=0, seed=93):
    """A soft tick for one vote landing in a poll. i (0 to 6) lifts it a third of a semitone a step, so a run of
    votes climbs a little without turning into a scale."""
    t = tt(0.07)
    f = 1180 * 2 ** (0.36 * min(max(float(i), 0.0), 8.0) / 12)
    y = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.011) + 0.35 * np.sin(2 * np.pi * f * 2.4 * t) * np.exp(-t / 0.004)
    y += 0.25 * bp(R(seed).standard_normal(len(t)), 1500, 5000) * np.exp(-t / 0.002)
    return _foley(lp(y * np.minimum(t / 0.0015, 1), 6000), 0.5)


def msg():
    """A message arriving, quietly: softer and lower than pop() (which falls from 950 to 330 Hz; this falls from
    650 to 250 and starts without a click)."""
    t = tt(0.15)
    ph = 2 * np.pi * np.cumsum(250 + 400 * np.exp(-t / 0.02)) / SR
    y = (np.sin(ph) + 0.14 * np.sin(2 * ph)) * np.exp(-t / 0.03) * np.minimum(t / 0.003, 1)
    return _foley(y, 0.45)


def sys_tick(seed=94):
    """A system line appearing ("Maya added Danny"): hardly a sound, one small dull tick."""
    t = tt(0.05)
    y = bp(R(seed).standard_normal(len(t)), 700, 2600) * np.exp(-t / 0.004) + 0.5 * np.sin(2 * np.pi * 880 * t) * np.exp(-t / 0.007)
    return _foley(y * np.minimum(t / 0.001, 1), 0.28)


def clock_tick(tock=False, seed=95):
    """One tick of a mantel clock: the escapement's click, the lock a fiftieth of a second after it, and the wooden
    case answering. tock=True is the other half of the pair, a little lower. Dry: the clock is in the room."""
    t = tt(0.14)
    r = R(seed + (1 if tock else 0))
    k = 0.84 if tock else 1.0
    case = ((1450, 0.5, 0.012, 0.3), (2380, 0.32, 0.008, 1.1), (640, 0.3, 0.02, 0.0))
    y = np.zeros(len(t))
    for at, g in ((0.0, 1.0), (0.021, 0.35)):  # the pallet drops, then locks
        y += g * (_burst(t, at, hp(r.standard_normal(len(t)), 2500 * k), 0.0016) * 0.9 + _struck(t, at, case, k))
    return _foley(lp(y, 9000), 0.5)


def cup(seed=96):
    """A china cup put down on its saucer: two small high clinks, the second lighter as the cup settles. Short:
    china held in a hand does not ring."""
    t = tt(0.3)
    r = R(seed)
    china = ((2630, 1.0, 0.035, 0.0), (4170, 0.6, 0.024, 0.9), (6050, 0.4, 0.014, 2.0), (7900, 0.22, 0.009, 0.4), (1480, 0.25, 0.03, 1.3))
    y = np.zeros(len(t))
    for at, g, k in ((0.0, 1.0, 1.0), (0.085, 0.5, 1.07)):
        y += g * (_struck(t, at, china, k) + _burst(t, at, hp(r.standard_normal(len(t)), 3000), 0.0012) * 0.7)
    return _foley(y, 0.5)


def two_note():
    """The series' closing figure: two low, soft, slow notes, a fall of a fourth (B flat to the F below it), half a
    second apart. Warm: a round tone with a soft start and a little second and third harmonic (so that a phone can
    play it), no hammer and no bell partials; each ear a hair off the note. 1.6 s, ending in the second note's own
    decay."""
    d = 1.6
    t = tt(d)
    y = np.zeros((len(t), 2))
    for at, m, g, dec in ((0.0, 58, 1.0, 0.4), (0.52, 53, 0.94, 0.36)):
        i = int(at * SR)
        u = t[: len(t) - i]
        for ch, cents in ((0, -1.5), (1, 1.5)):
            f = mtof(m) * 2 ** (cents / 1200)
            tone = sum(a * np.sin(2 * np.pi * f * h * u) * np.exp(-u / (dec / h ** 0.9)) for h, a in ((1, 1.0), (2, 0.62), (3, 0.3), (4, 0.1), (5, 0.03)))
            y[i:, ch] += tone * (1 - np.exp(-u / 0.012)) * g
    y = lp(y, 1800) * (0.5 - 0.5 * np.cos(np.pi * np.clip((d - t) / 0.3, 0, 1)))[:, None]
    return _foley(y, 0.6)


if __name__ == "__main__":  # for people (and agents) who cannot hear: the numbers of any sound in the kit
    import sys
    from pathlib import Path

    names = [a for a in sys.argv[1:] if not a.startswith("--")]
    wav = None
    if "--wav" in sys.argv:
        wav = Path(names.pop(0))
        wav.mkdir(parents=True, exist_ok=True)
    print(f"{'sound':<16}{'length':>8}{'peak':>7}{'rms dB':>8}{'centroid':>10}{'<300 Hz':>9}{'phone dB':>10}{'tail dB':>9}   (SR {SR}; the film resamples to 48000;"
          f" phone = rms above 300 Hz; tail = the last 30 ms)")
    for name in names or ["thud", "vote", "msg", "sys_tick", "clock_tick", "cup", "two_note"]:
        fn, _, arg = name.partition(":")  # "vote:6" = vote(6)
        y = stereo(np.asarray(globals()[fn](*([float(arg)] if arg else [])), dtype=float))
        m = y.mean(axis=1)
        sp = np.abs(np.fft.rfft(m)) ** 2
        fr = np.fft.rfftfreq(len(m), 1 / SR)
        tail = y[-max(1, int(0.03 * SR)):]
        print(f"{name:<16}{len(y) / SR:7.3f}s{np.max(np.abs(y)):7.3f}{20 * np.log10(np.sqrt(np.mean(y ** 2)) + 1e-12):8.1f}{(sp * fr).sum() / sp.sum():8.0f} Hz"
              f"{100 * sp[fr < 300].sum() / sp.sum():8.0f}%{20 * np.log10(np.sqrt(np.mean(hp(y, 300) ** 2)) + 1e-12):10.1f}{20 * np.log10(np.sqrt(np.mean(tail ** 2)) + 1e-12):9.1f}")
        if wav:
            wavfile.write(wav / f"{name.replace(':', '_')}.wav", SR, (np.clip(y, -1, 1) * 32767).astype(np.int16))
