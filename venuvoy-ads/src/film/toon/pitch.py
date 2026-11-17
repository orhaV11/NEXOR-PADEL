"""Whose voice is this? The speech model now and then reads a line of his in a woman's voice (or hers in a man's),
and the words are still right, so the word check does not catch it. This measures the pitch of the voiced frames:
Timo sits at about 100-140 Hz, Luna at about 220-260 Hz.

    python3 src/film/toon/pitch.py work/uphill/voice/lines/ldn_g25p_1_01.wav ...     median pitch and the share of low frames
    from pitch import right_voice; right_voice(path, "T") -> (True/False, median Hz, share of frames under 165 Hz)
"""
import sys

import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

SPLIT = 165.0          # Hz: below is his range, above hers


def f0(x, sr, hop=0.01, win=0.04, fmin=60, fmax=400):
    """The pitch of every voiced frame (YIN), in Hz."""
    n, h = int(win * sr), int(hop * sr)
    tmax, tmin = int(sr / fmin), int(sr / fmax)
    out = []
    for i in range(0, len(x) - n, h):
        f = x[i:i + n] - x[i:i + n].mean()
        if np.sqrt((f ** 2).mean()) < 0.01:
            continue
        m = n - tmax
        d = np.array([np.sum((f[:m] - f[t:t + m]) ** 2) for t in range(tmax + 1)])
        cm = np.ones_like(d)
        cm[1:] = d[1:] * np.arange(1, len(d)) / np.maximum(np.cumsum(d[1:]), 1e-12)
        cand = np.where(cm[tmin:] < 0.15)[0]
        if len(cand) == 0:
            continue
        t = cand[0] + tmin
        while t + 1 < len(cm) and cm[t + 1] < cm[t]:
            t += 1
        out.append(sr / t)
    return np.array(out)


def measure(path):
    x, sr = sf.read(str(path))
    x = x.mean(axis=1) if x.ndim > 1 else x
    if sr != 16000:
        x, sr = resample_poly(x, 16000, sr), 16000
    v = f0(x, sr)
    if len(v) < 5:
        return 0.0, 0.5
    return float(np.median(v)), float(np.mean(v < SPLIT))


def cepstrum(path):
    """A second, independent measure (the peak of the cepstrum): YIN can jump an octave up on a creaky low voice."""
    x, sr = sf.read(str(path))
    x = x.mean(axis=1) if x.ndim > 1 else x
    if sr != 16000:
        x, sr = resample_poly(x, 16000, sr), 16000
    n, h, out = 1024, 160, []
    lo, hi = int(sr / 400), int(sr / 60)
    for i in range(0, len(x) - n, h):
        f = x[i:i + n] * np.hanning(n)
        if np.sqrt((f ** 2).mean()) < 0.01:
            continue
        c = np.fft.irfft(np.log(np.abs(np.fft.rfft(f)) + 1e-9))
        out.append(sr / (lo + np.argmax(c[lo:hi])))
    return float(np.median(out)) if out else 0.0


def right_voice(path, who):
    """Only a clear swap counts: his voice above 185 Hz, hers below 140 Hz. (Luna speaking low or whispering goes
    down to about 150 Hz and Timo asking a question up to about 170; the listening model heard all of those right.)"""
    med, low = measure(path)
    if med == 0.0:                     # too few voiced frames to tell (a whisper)
        return True, med, low
    wrong = (lambda f: f > 185) if who == "T" else (lambda f: f < 140)
    if not wrong(med):
        return True, med, low
    c = cepstrum(path)                 # both measures must agree before a take is thrown out
    return not wrong(c), med, low


if __name__ == "__main__":
    for p in sys.argv[1:]:
        med, low = measure(p)
        print(f"{p}: median {med:.0f} Hz, {low * 100:.0f}% of frames under {SPLIT:.0f} Hz")
