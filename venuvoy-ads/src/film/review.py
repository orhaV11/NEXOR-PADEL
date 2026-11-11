"""A reviewer's packet for a finished cut, for people (and agents) who cannot play video.

    python3 src/film/review.py film.mp4 [--out dir] [--fps 2]

Writes contact sheets with timecodes, the transcript of the final mix with word times, and loudness per second
(full band and through a phone-speaker high-pass), and prints the numbers a critic would ask for.
"""
import io
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asr  # noqa: E402

args = [a for a in sys.argv[1:] if not a.startswith("--")]
opt = lambda k, d: sys.argv[sys.argv.index(k) + 1] if k in sys.argv else d  # noqa: E731
film = Path(args[0])
out = Path(opt("--out", film.parent / "review"))
fps = float(opt("--fps", 2))
out.mkdir(parents=True, exist_ok=True)
dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(film)], capture_output=True, text=True).stdout)
n = int(dur * fps)
frames = []
for k in range(n):
    t = (k + 0.5) / fps
    raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{t:.3f}", "-i", str(film), "-frames:v", "1", "-vf", "scale=270:-2", "-f", "image2pipe", "-c:v", "png", "-"], capture_output=True).stdout
    frames.append((t, Image.open(io.BytesIO(raw)).convert("RGB")))
w, h = frames[0][1].size
cols, per = 10, 30
sheets = []
for p in range(0, len(frames), per):
    chunk = frames[p:p + per]
    rows = (len(chunk) + cols - 1) // cols
    s = Image.new("RGB", (cols * w, rows * (h + 16)), "white")
    d = ImageDraw.Draw(s)
    for i, (t, im) in enumerate(chunk):
        x, y = (i % cols) * w, (i // cols) * (h + 16)
        s.paste(im, (x, y + 16))
        d.text((x + 3, y + 2), f"{t:.2f}s", fill="black")
    sp = out / f"sheet_{p // per + 1}.jpg"
    s.save(sp, quality=84)
    sheets.append(str(sp))
x, sr = asr.load(film)
ws = []
for a in range(0, int(len(x) / sr) + 1, 25):  # the recogniser takes under 30 s at a time
    seg = x[a * sr:(a + 25) * sr]
    if len(seg) < sr // 2:
        continue
    st = asr.parakeet().create_stream()
    st.accept_waveform(sr, seg)
    asr.parakeet().decode_stream(st)
    cur, t0 = "", 0.0
    for tok, t in zip(st.result.tokens, st.result.timestamps):
        if tok.startswith(" ") or not cur:
            if cur:
                ws.append((cur, round(a + t0, 2)))
            cur, t0 = tok.strip(), t
        else:
            cur += tok
    if cur:
        ws.append((cur, round(a + t0, 2)))
import scipy.signal as ss
b, aa = ss.butter(4, 400 / (sr / 2), btype="high")
hp = ss.filtfilt(b, aa, x)
lvl = [round(float(20 * np.log10(np.sqrt(np.mean(x[i * sr:(i + 1) * sr] ** 2)) + 1e-9)), 1) for i in range(int(dur))]
lvl_hp = [round(float(20 * np.log10(np.sqrt(np.mean(hp[i * sr:(i + 1) * sr] ** 2)) + 1e-9)), 1) for i in range(int(dur))]
hop = int(0.05 * sr)
env = np.array([20 * np.log10(np.sqrt(np.mean(x[i:i + hop] ** 2)) + 1e-9) for i in range(0, len(x) - hop, hop)])
quiet = env < -36
runs, k = [], 0
while k < len(quiet):
    if quiet[k]:
        j = k
        while j < len(quiet) and quiet[j]:
            j += 1
        if (j - k) * 0.05 >= 0.3:
            runs.append((round(k * 0.05, 2), round(j * 0.05, 2)))
        k = j
    else:
        k += 1
gaps = [(round(a, 2), round(b, 2)) for (_, a), (_, b) in zip(ws, ws[1:]) if b - a > 1.6]
rep = {"film": str(film), "dur": round(dur, 2), "sheets": sheets, "words": ws, "word_count": len(ws), "words_per_s": round(len(ws) / dur, 2),
       "first_word_at": ws[0][1] if ws else None, "gaps_over_1.6s_between_word_starts": gaps, "level_per_s_db": lvl, "phone_level_per_s_db": lvl_hp,
       "near_silent_runs_over_0.3s": runs}
(out / "review.json").write_text(json.dumps(rep, indent=1))
print(f"{film.name}: {dur:.2f} s, {len(ws)} words ({rep['words_per_s']}/s), first word at {rep['first_word_at']} s")
print("transcript:", " ".join(f"{w}@{t:.1f}" for w, t in ws))
print("gaps between word starts over 1.6 s:", gaps)
print("level per second (dBFS):", " ".join(f"{v:.0f}" for v in lvl))
print("phone-speaker level/s  :", " ".join(f"{v:.0f}" for v in lvl_hp))
print("near-silent runs:", runs)
print("sheets:", *sheets)
