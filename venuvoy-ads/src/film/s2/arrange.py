"""Cut one music track into the bed of a film, block by block, on the beat.

    python3 src/film/s2/arrange.py <arrangement.json>

A generated track cannot be asked to stop at 12.0 seconds and drop at 12.5. So the film's music is built from the
track the way a DJ would: blocks lifted on bar lines and laid where the film needs them, with filters standing in
for the stems we do not have ("bass" = everything under 170 Hz, "tops" = everything above 2.5 kHz, "full" = as is,
"lean" = as is with 6 dB less under 150 Hz, which leaves room to play the rest louder: a phone plays nothing down there).

arrangement.json
  { "track": "src/film/assets/s2/music/A1_0.mp3", "out": "src/film/assets/s2/music/bed_one_night.wav", "length": 28.5,
    "blocks": [ { "at": 0.0, "len": 4.0, "from_bar": 1, "layer": "full", "gain_db": -6 },
                { "at": 4.0, "len": 4.0, "from_bar": 9, "layer": "bass", "gain_db": -3 }, ... ] }

"from_bar" counts the track's bars from 1 (see beats.py for the bar table); "beat" (0-3, fractions allowed) starts
inside the bar. "from_s" instead of "from_bar" names a second of the track directly (for its last hit and tail).
Blocks may overlap (a bass block under a tops block). Every block gets a 12 ms fade at both ends unless "fade_in" /
"fade_out" say otherwise. Where no block lies the bed is silent: that is how the dead stop before the drop is made.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy.signal import butter, sosfiltfilt

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE))
import beats  # noqa: E402

SR = 48000


def load(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-ac", "2", "-ar", str(SR), "-f", "f32le", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).astype(np.float64)


def main(path):
    arr = json.load(open(path))
    track = ROOT / arr["track"]
    x = load(track)
    a = beats.analyse(track)
    beat, first = a["beat"], a["first_beat"]
    low150 = sosfiltfilt(butter(4, 150, btype="low", fs=SR, output="sos"), x, axis=0)
    layers = {"full": x, "lean": x - 0.5 * low150,
              "bass": sosfiltfilt(butter(4, 170, btype="low", fs=SR, output="sos"), x, axis=0),
              "tops": sosfiltfilt(butter(4, 2500, btype="high", fs=SR, output="sos"), x, axis=0),
              "nobass": sosfiltfilt(butter(4, 170, btype="high", fs=SR, output="sos"), x, axis=0)}
    out = np.zeros((int(arr["length"] * SR), 2))
    for b in arr["blocks"]:
        src_t = b["from_s"] if "from_s" in b else first + ((b["from_bar"] - 1) * 4 + b.get("beat", 0)) * beat
        n = int(b["len"] * SR)
        s0 = int(src_t * SR)
        seg = layers[b.get("layer", "full")][s0:s0 + n].copy()
        if len(seg) < n:
            seg = np.vstack([seg, np.zeros((n - len(seg), 2))])
        seg *= 10 ** (b.get("gain_db", 0) / 20)
        fi, fo = int(b.get("fade_in", 0.012) * SR), int(b.get("fade_out", 0.012) * SR)
        if fi:
            seg[:fi] *= np.linspace(0, 1, fi)[:, None]
        if fo:
            seg[-fo:] *= np.linspace(1, 0, fo)[:, None]
        d0 = int(b["at"] * SR)
        out[d0:d0 + n] += seg[: max(0, len(out) - d0)]
    peak = float(np.abs(out).max())
    if peak > 0.98:
        out *= 0.98 / peak
    dst = ROOT / arr["out"]
    sf.write(dst, out.astype(np.float32), SR)
    print(f"{dst}: {arr['length']} s from {track.name} ({a['bpm']} BPM, first beat {first} s), peak {20 * np.log10(max(peak, 1e-9)):.1f} dBFS")


if __name__ == "__main__":
    main(sys.argv[1])
