"""Cut a reading of a whole sketch into its lines.

    python3 src/film/toon/lines.py <sketch.json> <take name> [<take name> ...]

The words heard back (with times) are matched in order to the words of the script; each line runs from just before
its first word to where the voice has died away after its last one (never into the next line). Lines are saved as
<work>/voice/lines/<take>_<nn>.wav and listed in <work>/voice/lines/<take>.json with their place in the take.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sk = json.load(open(sys.argv[1]))
V = ROOT / sk["work"] / "voice"
(V / "lines").mkdir(exist_ok=True)
norm = lambda w: re.sub(r"[^a-z]", "", w.lower())  # noqa: E731

for take in sys.argv[2:]:
    meta = json.loads((V / f"{take}.json").read_text())
    x, sr = sf.read(V / f"{take}.wav")
    words = [(norm(w), a, b) for w, a, b in meta["words"] if norm(w)]
    h = int(0.01 * sr)
    env = np.array([20 * np.log10(np.sqrt(np.mean(x[i:i + h] ** 2)) + 1e-9) for i in range(0, len(x) - h, h)])
    voiced = env > -46  # the readings have true silence between lines
    # first word of every line as the recogniser heard it; a line the recogniser missed (a groan) starts where the one before ended
    k, starts = 0, []
    for who, text in sk["lines"]:
        want = [norm(w) for w in re.sub(r"\[.*?\]", "", text).split() if norm(w)]
        got = []
        for w in want:
            j = next((i for i in range(k, min(k + 4, len(words))) if words[i][0][:3] == w[:3]), None)
            if j is not None:
                got.append(words[j])
                k = j + 1
        starts.append((got, len(want)))
    out, prev_end = [], 0.0
    for n, ((who, text), (got, nwant)) in enumerate(zip(sk["lines"], starts), 1):
        nxt = next((g[0][1] for g, _ in starts[n:] if g), len(x) / sr)
        a = got[0][1] if got else prev_end + 0.05
        lo, hi = int(max(prev_end, a - 0.25) * 100), int((min(nxt, len(x) / sr) if got else len(x) / sr) * 100)
        idx = np.where(voiced[lo:hi])[0]
        if not len(idx):
            out.append({"n": n, "who": who, "missing": True})
            continue
        # the line ends at the first silent gap of a quarter second after its last word has begun (the recogniser
        # stretches a last word through the silence, and the next speaker's first sound must not be taken along);
        # a line without recognised words (a groan) runs to the first gap of 0.6 s
        i0 = lo + idx[0]
        after = int((got[-1][1] + 0.1) * 100) if got else i0
        gap = 25 if got else 60
        i1, quiet = after, 0
        for i in range(after, len(voiced)):
            if voiced[i]:
                i1, quiet = i, 0
            else:
                quiet += 1
                if quiet >= gap:
                    break
        t0, t1 = max(prev_end, i0 / 100 - 0.03), min(i1 / 100 + 0.06, len(x) / sr)
        prev_end = t1
        seg = x[int(t0 * sr):int(t1 * sr)].copy()
        f = int(0.008 * sr)
        seg[:f] *= np.linspace(0, 1, f)
        seg[-f:] *= np.linspace(1, 0, f)
        sf.write(V / "lines" / f"{take}_{n:02d}.wav", seg, sr)
        out.append({"n": n, "who": who, "text": re.sub(r"\[.*?\]\s*", "", text), "t0": round(t0, 3), "t1": round(t1, 3), "dur": round(t1 - t0, 3),
                    "words": [[w, round(a_ - t0, 3), round(b_ - t0, 3)] for w, a_, b_ in got], "matched": f"{len(got)}/{nwant}",
                    "peak_db": round(float(20 * np.log10(np.abs(seg).max() + 1e-9)), 1)})
    (V / "lines" / f"{take}.json").write_text(json.dumps(out, indent=1))
    print(take)
    for o in out:
        print("  ", o["n"], o["who"], "MISSING" if o.get("missing") else f'{o["t0"]:6.2f}-{o["t1"]:6.2f} ({o["dur"]:.2f} s) {o["matched"]} peak {o["peak_db"]} dB  {o["text"]}')
