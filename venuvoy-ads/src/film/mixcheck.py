"""Is every line clear of the music? Reads the stems a cut leaves behind.

    python3 src/film/mixcheck.py work/<film>/cut-<lang>[-draft]

For every caption line: voice level and how far it sits above music+effects while it is spoken; then the share of
the film where music plays, and the quietest stretches of the full mix.
"""
import json
import sys
from pathlib import Path

import numpy as np
from scipy.io import wavfile

cut = Path(sys.argv[1])
sr, d = wavfile.read(cut / "stem_dialog.wav")
_, m = wavfile.read(cut / "stem_music.wav")
_, f = wavfile.read(cut / "stem_fx.wav")
d, m, f = (x.astype(float).mean(axis=1) / 32768 for x in (d, m, f))
tl = json.load(open(cut / "timeline.json"))
rms = lambda x: 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-9)  # noqa: E731
print(f"{'line':<34} {'voice':>7} {'music+fx':>9} {'margin':>7}")
lines, cur = [], None
for c in tl["caps"]:
    txt = " ".join(w["w"] for w in c["words"])
    if cur and c["t0"] - cur[1] < 0.2:
        cur[1], cur[2] = c["words"][-1]["t1"], cur[2] + " " + txt
    else:
        cur = [c["words"][0]["t0"], c["words"][-1]["t1"], txt]
        lines.append(cur)
worst = 99
for t0, t1, txt in lines:
    a, b = int(t0 * sr), int(t1 * sr)
    v, bed = rms(d[a:b]), rms(m[a:b] + f[a:b])
    worst = min(worst, v - bed)
    print(f"{txt[:34]:<34} {v:7.1f} {bed:9.1f} {v - bed:7.1f}{'  <-- low' if v - bed < 9 else ''}")
hop = int(0.25 * sr)
mus = np.array([rms(m[i:i + hop]) for i in range(0, len(m) - hop, hop)])
mix = np.array([rms((d + m + f)[i:i + hop]) for i in range(0, len(m) - hop, hop)])
print(f"music audible (> -45 dBFS) in {np.mean(mus > -45) * 100:.0f}% of the film; quietest quarter-seconds of the mix: "
      + ", ".join(f"{i * 0.25:.2f}s {mix[i]:.0f} dB" for i in np.argsort(mix)[:5]))
print(f"worst margin {worst:.1f} dB")
