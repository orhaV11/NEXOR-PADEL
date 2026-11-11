"""Gate a generated take before it enters the edit.

    python3 src/film/gate.py clip.mp4 "The exact line it should say." [--json]

Checks, without anyone listening: the words match the script (ASR, numbers normalised), the last word ends
with slack before the clip does, there is no steady musical tone under or between the words, and the loudness.
Prints PASS/FAIL per check and the word times (source seconds) for cutting.
"""
import difflib
import json
import re
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asr  # noqa: E402

NUM = {"zero": 0, "one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "seven": 7, "eight": 8, "nine": 9, "ten": 10, "eleven": 11,
       "twelve": 12, "thirteen": 13, "fourteen": 14, "fifteen": 15, "sixteen": 16, "seventeen": 17, "eighteen": 18, "nineteen": 19, "twenty": 20,
       "thirty": 30, "forty": 40, "fifty": 50, "sixty": 60, "seventy": 70, "eighty": 80, "ninety": 90}


def tokens(text):
    """Lower-case words with spelled-out numbers folded to digits, so 'forty-seven' == '47' and 'nine hundred' == '900'."""
    ws = re.findall(r"[a-z0-9']+", text.lower().replace("-", " ").replace("kickoff", "kick off").replace("hair dryer", "hairdryer"))
    out, i = [], 0
    while i < len(ws):
        if ws[i] in NUM:
            v, j, seen = 0, i, False
            cur = 0
            while j < len(ws) and (ws[j] in NUM or ws[j] in ("hundred", "thousand", "and")):
                if ws[j] == "and":
                    if not seen or j + 1 >= len(ws) or ws[j + 1] not in NUM:
                        break
                elif ws[j] == "hundred":
                    cur = max(cur, 1) * 100
                elif ws[j] == "thousand":
                    v += max(cur, 1) * 1000
                    cur = 0
                else:
                    cur += NUM[ws[j]]
                seen = True
                j += 1
            out.append(str(v + cur))
            i = j
        else:
            out.append(ws[i])
            i += 1
    return out


def check(path, line):
    ws, wt = asr.words(path)
    x, sr = asr.load(path)
    dur = len(x) / sr
    want = tokens(line)
    got_p, got_w = tokens(" ".join(w for w, _, _ in ws)), tokens(wt)
    rp = difflib.SequenceMatcher(None, want, got_p, autojunk=False).ratio()
    rw = difflib.SequenceMatcher(None, want, got_w, autojunk=False).ratio()
    extra = [w for w in got_w if w not in want]
    missing = [w for w in want if w not in got_w and w not in got_p]
    last = ws[-1][2] if ws else 0.0
    first = ws[0][1] if ws else None
    # steady tone between words = probably music: spectral flatness of the gaps (noise/ambience is flat, music is peaky)
    gap = np.ones(len(x), bool)
    for _, a, b in ws:
        gap[max(0, int((a - 0.1) * sr)): int((b + 0.15) * sr)] = False
    g = x[gap]
    tonal = None
    if len(g) > sr // 2:
        import scipy.signal as ss
        f, pxx = ss.welch(g, sr, nperseg=2048)
        band = (f > 150) & (f < 4000)
        p = pxx[band] + 1e-16
        tonal = float(10 * np.log10(p.max() / np.exp(np.mean(np.log(p)))))  # crest of the spectrum, dB
    rms = 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-9)
    res = {"file": str(path), "dur": round(dur, 2), "heard": wt, "words": ws, "match": round(max(rp, rw), 2), "missing": missing, "extra": extra,
           "first_word": first, "last_word_end": round(last, 2), "tail_slack": round(dur - last, 2), "gap_tone_db": None if tonal is None else round(tonal, 1),
           "rms_db": round(float(rms), 1)}
    res["ok_words"] = max(rp, rw) >= 0.9 and not missing
    res["ok_tail"] = dur - last >= 0.35
    res["ok_music"] = tonal is None or tonal < 30
    return res


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    r = check(args[0], args[1])
    if "--json" in sys.argv:
        print(json.dumps(r))
    else:
        flag = lambda b: "PASS" if b else "FAIL"  # noqa: E731
        print(f"== {r['file']} ({r['dur']} s, {r['rms_db']} dBFS)")
        print(f"   heard: {r['heard']}")
        print(f"   words {flag(r['ok_words'])} (match {r['match']}, missing {r['missing']}, extra {r['extra']})")
        print(f"   tail  {flag(r['ok_tail'])} (last word ends {r['last_word_end']} s, slack {r['tail_slack']} s; first word {r['first_word']} s)")
        print(f"   music {flag(r['ok_music'])} (tone in the gaps {r['gap_tone_db']} dB)")
        print("   times: " + " ".join(f"{w}@{a:.2f}-{b:.2f}" for w, a, b in r["words"]))
