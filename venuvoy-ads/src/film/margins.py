"""Voice against everything else, line by line, in the band a phone plays (300 Hz - 3 kHz).

    python3 src/film/margins.py work/<slug>/cut-<lang>[-draft]

Lines are found from the dialogue stem itself (voice-active stretches), plus the voice effects named in the edit list
are not separated: read the numbers with marks.json beside them. Prints the margin of each stretch and the level of
the music in the gaps."""
import json
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal as ss

d = Path(sys.argv[1])
dia, sr = sf.read(d / "stem_dialog.wav")
mus, _ = sf.read(d / "stem_music.wav")
fx, _ = sf.read(d / "stem_fx.wav")
mono = lambda x: x.mean(1) if x.ndim > 1 else x  # noqa: E731
dia, mus, fx = map(mono, (dia, mus, fx))
n = min(len(dia), len(mus), len(fx))
sos = ss.butter(4, [300, 3000], "bandpass", fs=sr, output="sos")
bd, bo = ss.sosfilt(sos, dia[:n]), ss.sosfilt(sos, (mus + fx)[:n])
db = lambda x: 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-9)  # noqa: E731
h = int(0.01 * sr)
lvl = np.array([db(dia[i:i + 2 * h]) for i in range(0, n - 2 * h, h)])
act = lvl > -38
runs, k = [], 0
while k < len(act):
    if act[k]:
        j = k
        while j < len(act) and act[j]:
            j += 1
        if j - k >= 8:
            if runs and k - runs[-1][1] < 30:
                runs[-1][1] = j
            else:
                runs.append([k, j])
        k = j
    else:
        k += 1
worst = 99
for a, b in runs:
    i, j = a * h, b * h
    m = db(bd[i:j]) - db(bo[i:j])
    worst = min(worst, m)
    print(f"{a / 100:6.2f}-{b / 100:6.2f}  voice {db(bd[i:j]):6.1f}  other {db(bo[i:j]):6.1f}  margin {m:5.1f}{'  <-- low' if m < 8.5 else ''}")
print(f"worst margin {worst:.1f} dB")
m = json.load(open(d / "marks.json"))
full = mus + fx
print("everything but the voice, full band, per second:", " ".join(f"{db(full[i:i + sr]):.0f}" for i in range(0, n - sr, sr)))
print("everything but the voice, 300 Hz-3 kHz, per second:", " ".join(f"{db(bo[i:i + sr]):.0f}" for i in range(0, n - sr, sr)))
fin, sr2 = sf.read(d / "final.wav")
fb = ss.sosfilt(ss.butter(4, [300, 3000], "bandpass", fs=sr2, output="sos"), mono(fin))
half = [db(fb[i:i + sr2 // 2]) for i in range(0, len(fb) - sr2 // 2, sr2 // 2)]
print("mastered mix, 300 Hz-3 kHz, per half second:", " ".join(f"{v:.0f}" for v in half))
print(f"share of the film under -30 dBFS in that band: {100 * sum(v < -30 for v in half) / len(half):.1f}%")
