"""Master a mix once: static gain to the social loudness target, then a true-peak limiter.

    python3 src/audio/master.py in.wav out.wav [--lufs -14] [--tp -1] [--headroom 1] [--wav-only]

Loudness is measured with ffmpeg's EBU R128 meter. The gain is one static number (no pumping);
only the few peaks that would cross the ceiling are pulled down, by a 4x-oversampled look-ahead
limiter with a smooth release, and the gain is nudged until the result reads the target.

Every file here ships as AAC 192k, and the encoder moves both numbers: it adds inter-sample
overshoot (a WAV that touches -1 dBTP comes out near 0) and drops the air above ~16 kHz (about
-0.4 LU on these synthesized hats). So the ceiling starts --headroom dB under --tp, and the loop
measures an AAC encode of each attempt, not the WAV: the file that ships reads -14 LUFS and at or
under -1 dBTP, the video-use skill's rule for anything delivered. --wav-only measures the WAV.
"""
import os
import re
import subprocess
import sys
import tempfile

import numpy as np
from scipy.io import wavfile
from scipy.ndimage import minimum_filter1d, uniform_filter1d
from scipy.signal import resample_poly

OS = 4  # oversampling for the true-peak estimate, as in ITU-R BS.1770


def measure(path):
    """Integrated loudness (LUFS) and true peak (dBTP) from ffmpeg's EBU R128 meter."""
    err = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", path, "-af", "ebur128=peak=true", "-f", "null", "-"],
                         capture_output=True, text=True).stderr
    summary = err[err.rfind("Summary:"):]
    i = float(re.search(r"I:\s+(-?[\d.]+|-inf) LUFS", summary).group(1))
    tp = float(re.search(r"Peak:\s+(-?[\d.]+|-inf) dBFS", summary).group(1))
    return i, tp


def measure_shipped(path):
    """The same meter, read off an AAC 192k encode, which is what every mp4 here carries."""
    m4a = path[:-4] + ".m4a"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", path, "-c:a", "aac", "-b:a", "192k", m4a], check=True)
    try:
        return measure(m4a)
    finally:
        os.remove(m4a)


def read(path):
    sr, x = wavfile.read(path)
    scale = {np.dtype(np.int16): 32768.0, np.dtype(np.int32): 2147483648.0}.get(x.dtype, 1.0)
    x = x.astype(np.float64) / scale
    return sr, x if x.ndim == 2 else x[:, None]


def true_peaks(x):
    """Per input sample, the largest absolute value among its oversampled neighbours, across channels."""
    up = np.abs(resample_poly(x, OS, 1, axis=0)).max(axis=1)
    n = x.shape[0]
    return up[: n * OS].reshape(n, OS).max(axis=1)


def limit(x, sr, ceiling_db, lookahead_ms=1.5, release_ms=120.0):
    ceiling = 10 ** (ceiling_db / 20)
    need = np.minimum(1.0, ceiling / np.maximum(true_peaks(x), 1e-12))
    if need.min() >= 1.0:
        return x
    la = max(1, int(sr * lookahead_ms / 1000))
    g = minimum_filter1d(need, size=2 * la + 1, mode="nearest")  # start ducking before the peak, hold past it
    rel = np.exp(-1.0 / (sr * release_ms / 1000))
    out = np.empty_like(g)
    prev = 1.0
    for i, v in enumerate(g):  # instant attack (already look-ahead), exponential release
        prev = v if v < prev else rel * prev + (1 - rel) * v
        out[i] = prev
    out = uniform_filter1d(out, size=la, mode="nearest")  # round the corners so the gain itself makes no click
    return x * out[:, None]


def main():
    args = sys.argv[1:]
    src, dst = args[0], args[1]
    opt = lambda name, dflt: float(args[args.index(name) + 1]) if name in args else dflt
    target, tp, headroom = opt("--lufs", -14.0), opt("--tp", -1.0), opt("--headroom", 1.0)
    meter = measure if "--wav-only" in args else measure_shipped
    ceiling = tp - headroom

    sr, x = read(src)
    i0, tp0 = measure(src)
    gain = target - i0
    fd, tmp = tempfile.mkstemp(suffix=".wav")
    os.close(fd)
    try:
        for _ in range(8):
            y = x * 10 ** (gain / 20)
            for _ in range(3):  # the gain envelope can leave a hair over; tighten until it doesn't
                y = limit(y, sr, ceiling)
                over = 20 * np.log10(true_peaks(y).max()) - ceiling
                if over <= 0.02:
                    break
                y *= 10 ** (-(over + 0.02) / 20)
            wavfile.write(tmp, sr, np.clip(np.round(y * 32767), -32768, 32767).astype(np.int16))
            i1, tp1 = meter(tmp)
            if abs(i1 - target) <= 0.1 and tp1 <= tp:
                break
            if tp1 > tp:
                ceiling -= tp1 - tp + 0.1
            gain += target - i1
        os.replace(tmp, dst)
        os.chmod(dst, 0o644)
    finally:
        if os.path.exists(tmp):
            os.remove(tmp)
    print(f"mastered {dst}: {i1:.1f} LUFS, true peak {tp1:.1f} dBTP{'' if meter is measure else ' as shipped (AAC 192k)'}"
          f" (was {i0:.1f} LUFS / {tp0:.1f} dBTP; gain {gain:+.1f} dB, ceiling {ceiling:.1f} dBTP)")


if __name__ == "__main__":
    main()
