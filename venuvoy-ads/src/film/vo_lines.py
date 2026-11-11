"""Cut a continuous narration take into its lines.

    python3 src/film/vo_lines.py plan.json

plan.json: {"out": "src/film/assets/apa/vo/en", "target_db": -22.5, "max_gain_db": 3.5, "pause": 0.30,
            "lines": {"n1": [["take.mp3", t0, t1], ["take.mp3", t0, t1]], ...}}
Each line is one or more phrases (start and end of the voice inside a take, in seconds). Every phrase is brought
towards the same level (a quiet last phrase is a common fault of read speech), and the pause between the phrases of
a line is capped at "pause" seconds, or at the line's own "pause" given as a 4th value on its second phrase. Silence
is removed; the speech itself is never stretched or sped up."""
import json
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np

SR = 48000
plan = json.load(open(sys.argv[1]))
_cache = {}


def take(path):
    if path not in _cache:
        raw = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"], capture_output=True, check=True).stdout
        _cache[path] = np.frombuffer(raw, np.float32).astype(np.float64)
    return _cache[path]


def fade(x, n=int(0.012 * SR)):
    x = x.copy()
    n = min(n, len(x) // 2)
    x[:n] *= np.linspace(0, 1, n)
    x[-n:] *= np.linspace(1, 0, n)
    return x


out = Path(plan["out"])
out.mkdir(parents=True, exist_ok=True)
for name, phrases in plan["lines"].items():
    parts = []
    for k, ph in enumerate(phrases):
        a = take(ph[0])
        t0, t1 = ph[1], ph[2]
        body = a[int(t0 * SR):int(t1 * SR)]
        rms = 20 * np.log10(np.sqrt((body ** 2).mean()) + 1e-9)
        g = float(np.clip(plan.get("target_db", -22.5) - rms, -plan.get("max_gain_db", 3.5), plan.get("max_gain_db", 3.5)))
        x = fade(a[int((t0 - 0.05) * SR):int((t1 + 0.09) * SR)]) * 10 ** (g / 20)
        if k:
            gap = ph[3] if len(ph) > 3 else min(plan.get("pause", 0.30), ph[1] - phrases[k - 1][2]) if phrases[k - 1][0] == ph[0] else plan.get("pause", 0.30)
            parts.append(np.zeros(max(0, int((gap - 0.14) * SR))))
        parts.append(x)
        print(f"  {name} phrase {k}: {t1 - t0:.2f} s, {rms:.1f} dB, gain {g:+.1f}")
    y = np.concatenate(parts)
    with wave.open(str(out / f"{name}.wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((np.clip(y, -1, 1) * 32767).astype(np.int16).tobytes())
    print(f"{name}: {len(y) / SR:.2f} s")
