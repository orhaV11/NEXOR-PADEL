"""Make the version without music of a film that cut.py has rendered.

    python3 src/film/s2/nomusic.py <slug> <gain_db> <film_9x16.mp4> <out.mp4>

<slug> names work/<slug>/cut-en/ (stems and raw mix); <gain_db> is the master gain cut.py printed ("gain +5.8 dB").
The voices and effects keep exactly the level they have in the main version: the engine's raw mix is a fixed multiple
of the sum of its stems, so the same multiple and the same master gain are applied to dialogue + effects, then a
limiter at -2 dBFS. The picture is copied, not re-encoded.
"""
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

slug, gain_db, film, out = sys.argv[1], float(sys.argv[2]), sys.argv[3], sys.argv[4]
D = Path(__file__).resolve().parent.parent.parent.parent / "work" / slug / "cut-en"
d, sr = sf.read(D / "stem_dialog.wav")
m, _ = sf.read(D / "stem_music.wav")
x, _ = sf.read(D / "stem_fx.wav")
raw, _ = sf.read(D / "raw.wav")
n = min(len(d), len(m), len(x), len(raw))
s = d[:n] + m[:n] + x[:n]
a = float((raw[:n] * s).sum() / (s ** 2).sum())  # raw = a * (dialogue + music + effects)
sf.write(D / "pre_nomusic.wav", a * 10 ** (gain_db / 20) * (d[:n] + x[:n]), sr, subtype="FLOAT")
subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(D / "pre_nomusic.wav"), "-af", "alimiter=limit=0.794:attack=2:release=60:level=disabled", "-ar", "48000",
                "-c:a", "pcm_f32le", str(D / "lim_nomusic.wav")], check=True)
subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", film, "-i", str(D / "lim_nomusic.wav"), "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
                "-movflags", "+faststart", out], check=True)
print(f"{out}: voices and effects only (stems x {a:.4f}, master gain {gain_db:+.1f} dB)")
