"""Where each spoken phrase of a clip really starts and ends, for cutting tight.

    python3 src/film/phrases.py clip.mp4 [more.mp4 ...]

ASR gives the words and when each starts; the voice-band envelope gives where the sound of a phrase begins and
dies away. Prints one line per phrase: in/out with a little air, ready to paste into an edit list.
"""
import sys
from pathlib import Path

import numpy as np
import scipy.signal as ss

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asr  # noqa: E402


def phrases(path, gap=0.45):
    ws, _ = asr.words(path)
    x, sr = asr.load(path)
    if not ws:
        return [], len(x) / sr
    b, a = ss.butter(4, [200 / (sr / 2), 4000 / (sr / 2)], btype="band")
    v = ss.filtfilt(b, a, x)
    hop = int(0.01 * sr)
    env = np.array([np.sqrt(np.mean(v[i:i + hop] ** 2)) for i in range(0, len(v) - hop, hop)]) + 1e-9
    edb = 20 * np.log10(env)
    floor = np.percentile(edb, 10)
    peak = np.percentile(edb, 95)
    thr = floor + 0.25 * (peak - floor)
    groups, cur = [], [ws[0]]
    for prev, w in zip(ws, ws[1:]):
        # a new phrase after sentence punctuation or a long gap between word starts
        if prev[0][-1:] in ".?!" or w[1] - prev[1] > gap + 0.6:
            groups.append(cur)
            cur = []
        cur.append(w)
    groups.append(cur)
    out = []
    for k, g in enumerate(groups):
        t0, t_last = g[0][1], g[-1][1]
        lim = groups[k + 1][0][1] if k + 1 < len(groups) else len(x) / sr
        i = int(t0 * 100)
        while i > 0 and i > int((t0 - 0.35) * 100) and edb[min(i, len(edb) - 1)] > thr:
            i -= 1
        start = i / 100
        j = int((t_last + 0.12) * 100)
        quiet = 0
        while j < min(len(edb), int(lim * 100)):
            quiet = quiet + 1 if edb[j] < thr else 0
            if quiet >= 10:
                break
            j += 1
        end = (j - quiet + 1) / 100
        out.append({"text": " ".join(w for w, _, _ in g), "start": round(start, 2), "end": round(min(end, lim), 2)})
    return out, len(x) / sr


if __name__ == "__main__":
    for p in sys.argv[1:]:
        ph, dur = phrases(p)
        print(f"== {p} ({dur:.2f} s)")
        for q in ph:
            print(f"   {q['start']:5.2f} - {q['end']:5.2f}  ({q['end'] - q['start']:.2f} s)  cut {max(0, q['start'] - 0.08):.2f}-{min(dur, q['end'] + 0.14):.2f}   {q['text']}")
