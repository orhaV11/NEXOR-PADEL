"""Read a music track the way an editor needs it: tempo, where the beats are, where the energy is.

    python3 src/film/s2/beats.py track.mp3 [more.mp3 ...]

For each track: length, tempo (to 0.1 BPM), the time of the first beat, and a bar-by-bar table of loudness in three
bands (bass under 150 Hz = kick and bassline, mids = the body, highs above 5 kHz = hats and claps). From it one can
see, without hearing: where the full groove (the "drop") begins, where the music thins out, and whether the track
starts on the beat. Used to cut films of "Plan, No Plan" so that every cut lands on a beat.

    import beats
    b = beats.analyse("track.mp3")     # {"bpm", "first_beat", "beat", "bars": [{"t", "bass", "mid", "high", "all"}], "dur"}
"""
import subprocess
import sys

import numpy as np
from scipy.signal import butter, sosfilt

SR = 22050


def pcm(path):
    return np.frombuffer(subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                                        capture_output=True, check=True).stdout, dtype=np.float32)


def onset_env(x, hop=256, win=1024):
    n = (len(x) - win) // hop
    w = np.hanning(win)
    S = np.abs(np.fft.rfft(np.stack([x[i * hop:i * hop + win] * w for i in range(n)]), axis=1))
    flux = np.maximum(0, np.diff(np.log1p(60 * S), axis=0)).sum(1)
    return flux - flux.mean(), hop / SR


def analyse(path, lo=95, hi=150):
    x = pcm(path)
    dur = len(x) / SR
    env, dt = onset_env(x)
    # tempo: the lag of the onset envelope that repeats best, refined by fitting many beats ahead
    best = (0, 120.0)
    for bpm in np.arange(lo, hi + 0.05, 0.1):
        lag = 60.0 / bpm / dt
        idx = (np.arange(1, 9)[:, None] * lag + np.arange(len(env) - int(8 * lag) - 2)[None, :]).astype(int)
        score = float((env[None, : idx.shape[1]] * env[idx]).mean())
        if score > best[0]:
            best = (score, float(bpm))
    bpm = round(best[1], 1)
    beat = 60.0 / bpm
    # phase: the offset at which a comb of beats collects the most onset energy
    lag = beat / dt
    offs = np.arange(0, lag, 0.5)
    k = np.arange(int(len(env) / lag) - 1)
    sc = [float(env[(o + k * lag).astype(int)].sum()) for o in offs]
    first = float(offs[int(np.argmax(sc))] * dt)
    bands = {"bass": butter(4, 150, btype="low", fs=SR, output="sos"), "mid": butter(4, [150, 5000], btype="band", fs=SR, output="sos"),
             "high": butter(4, 5000, btype="high", fs=SR, output="sos")}
    y = {k_: sosfilt(s, x) for k_, s in bands.items()}
    bars, t = [], first
    db = lambda v: round(float(20 * np.log10(np.sqrt(np.mean(v ** 2)) + 1e-7)), 1)  # noqa: E731
    while t + 4 * beat <= dur:
        a, b = int(t * SR), int((t + 4 * beat) * SR)
        bars.append({"t": round(t, 3), "bass": db(y["bass"][a:b]), "mid": db(y["mid"][a:b]), "high": db(y["high"][a:b]), "all": db(x[a:b])})
        t += 4 * beat
    return {"bpm": bpm, "beat": beat, "first_beat": round(first, 3), "dur": round(dur, 2), "bars": bars}


if __name__ == "__main__":
    for p in sys.argv[1:]:
        a = analyse(p)
        print(f"\n{p}: {a['dur']} s, {a['bpm']} BPM, first beat at {a['first_beat']} s, {len(a['bars'])} bars")
        print("  bar  time   bass   mid   high   all")
        for i, b in enumerate(a["bars"]):
            bar = "#" * max(0, int((b["bass"] + 50) / 2))
            print(f"  {i + 1:>3} {b['t']:6.2f} {b['bass']:6.1f} {b['mid']:6.1f} {b['high']:6.1f} {b['all']:6.1f}  {bar}")
