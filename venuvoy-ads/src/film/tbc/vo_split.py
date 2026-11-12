"""Find each narrator line inside a continuous take and write the plan that vo_lines.py cuts from.

    python3 src/film/tbc/vo_split.py            -> src/film/assets/tbc/vo/takes/plan_en.json, then run vo_lines.py on it

LINES below is the script: for each episode, the lines in the order they were read. The recogniser finds each
line's first and last word; the exact start and end of the voice are then taken from the sound itself (the first
and last 10 ms that rise above the room), so nothing is clipped and no silence is kept.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE.parent))
import asr  # noqa: E402

TAKES = ROOT / "src/film/assets/tbc/vo/takes"
SR = 48000
LINES = {
    "e1": ["Eight people planned that trip. Two arrived. Neither was in the chat.", "This season they are doing it properly.", "The dates came out at seven.",  # replaced after review by "The match dates came out at seven a.m." (takes/plan_e1_n3.json)
           "Seen by seven. Opened by one.", "The organiser had removed maybe.", "Danny has been typing since Tuesday."],
    "e2": ["Lee had been comparing flights since nine.", "He did not say which Friday.", "Flights. Hotel. Time off work.", "She guessed Saturday."],
    "e3": ["To buy a ticket, a supporter needs a history of buying tickets.", "The group put in nine names. One came out. The winner had not been asked."],
    "e4": ["A different team won a different match. So this one moved to Monday.", "The flight home did not.", "Every change cost money."],
    "e5": ["The subject is the only adult present in face paint.", "He is also the only adult present.", "Staff have asked him to leave. He has explained that it is his seat.",
           "The match moved while he was in the air. He flies home on Wednesday."],
    "e6": ["The kick-off time is pinned.", "It has been asked fourteen times.", "The answer came from Sydney. It was two in the morning there.", "This is the pinned message."],
}
TAKE_OF = json.loads((TAKES / "takes.json").read_text()) if (TAKES / "takes.json").exists() else {}  # {"e1_5": "e1_fix.mp3"}: a line re-read in another take


def load(p):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(p), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).astype(np.float64)


def env_db(x, hop=0.005, win=0.02):
    h, w = int(hop * SR), int(win * SR)
    k = (len(x) - w) // h
    return np.array([20 * np.log10(np.sqrt((x[i * h:i * h + w] ** 2).mean()) + 1e-9) for i in range(k)]), hop


def sentences(line):
    return [s for s in re.split(r"(?<=[.?!])\s+", line) if s]


def nw(s):
    return len(re.findall(r"[A-Za-z0-9'-]+", s))


plan = {"out": "src/film/assets/tbc/vo/en", "target_db": -22.5, "max_gain_db": 3.5, "pause": 0.34, "lines": {}}
report = []
for ep, lines in LINES.items():
    path = TAKES / f"{ep}.mp3"
    x = load(path)
    e, hop = env_db(x)
    thr = np.percentile(e, 95) - 30
    ws, _ = asr.words(str(path))
    total = sum(nw(s) for ln in lines for s in sentences(ln))
    assert len(ws) == total, f"{ep}: script has {total} words, recogniser heard {len(ws)}: {' '.join(w for w, _, _ in ws)}"

    def edge(t_a, t_b):
        """the voice between two word-level guesses: widen to where the sound actually starts and stops"""
        i0, i1 = int(t_a / hop), min(len(e) - 1, int(t_b / hop))
        while i0 > 0 and e[i0 - 1] > thr:
            i0 -= 1
        while i0 < i1 and e[i0] <= thr:
            i0 += 1
        while i1 > i0 and e[i1] <= thr:
            i1 -= 1
        return round(max(0, i0 * hop - 0.012), 3), round(min(len(x) / SR, i1 * hop + 0.02 + 0.03), 3)

    k = 0
    for n, ln in enumerate(lines, 1):
        phrases = []
        for s in sentences(ln):
            a = ws[k][1]
            b_guess = ws[k + nw(s) - 1][1] + 0.12  # the start of the last word is reliable; its end is not
            nxt = ws[k + nw(s)][1] if k + nw(s) < len(ws) else len(x) / SR
            # the end: walk forward from the last word's start to the first 150 ms of silence before the next word
            # (shorter would stop inside a word: the closure of the "t" in "flights" is 80 ms of silence before its "s")
            i = int(b_guess / hop)
            lim = int((nxt - 0.02) / hop) if nxt < len(x) / SR else len(e) - 1
            end = lim
            run = 0
            for j in range(i, lim):
                run = run + 1 if e[j] <= thr else 0
                if run >= int(0.15 / hop):
                    end = j - run + 1
                    break
            t0, t1 = edge(a + 0.02, end * hop)
            phrases.append([str(path.relative_to(ROOT)), t0, t1])
            k += nw(s)
        plan["lines"][f"{ep}_n{n}"] = phrases
        if len(phrases) > 1:  # each sentence also on its own (e1_n4a, e1_n4b), for a line whose sentences land on separate pictures
            for q, ph in enumerate(phrases):
                plan["lines"][f"{ep}_n{n}{'abc'[q]}"] = [ph]
        dur = sum(p[2] - p[1] for p in phrases) + 0.34 * (len(phrases) - 1)
        report.append(f"{ep}_n{n}  {dur:5.2f}s  " + " | ".join(f"{p[1]:.2f}-{p[2]:.2f}" for p in phrases) + f"   {ln}")
(TAKES / "plan_en.json").write_text(json.dumps(plan, indent=1))
print("\n".join(report))
