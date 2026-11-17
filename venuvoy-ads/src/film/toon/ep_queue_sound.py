"""The sound of the short "Front of the Queue" (vlog 02, "queue"): four lines in one shot, a pigeon, then the plate.

    python3 src/film/toon/ep_queue_sound.py            the soundtrack and the timeline (work/queue/sound)
    python3 src/film/toon/ep_queue_sound.py --split    only cut reading 3 again (see resplit)

Three things here are this film's own, done after the kit's build() because the kit has no statement for them:
resplit() (two lines the cutter left as one), last_words() (the recogniser's stretched last word) and coo() (a soft
sound laid in whole: the kit's fx start 50 ms before the loudest instant, which is right for a thud only).
"""
import json
import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ASSETS, ROOT, SR, build, db, fade, hp  # noqa: E402

FILM = "queue"
A, C, CS = "ldn_g25p_1", "ldn_g25p_3", "ldn_g25p_3s"   # two readings; CS is reading 3 with its lines 3 and 4 cut apart here


def resplit(take=C, new=CS, n=3):
    """In reading 3 Timo's retort comes 0.22 s after her line: lines.py wants a quarter second of silence between two
    lines, so it kept lines 3 and 4 as ONE line and called line 4 missing. Cut that stretch again at its longest
    silence (between the start of her last word and his second word); the other lines are copied as they are.
    Written once into work/queue/voice/lines as the take `new`; nothing is done when it is already there (or stored)."""
    V = ROOT / "work" / FILM / "voice"
    for ext in ("wav", "json"):                                         # the reading itself is kept under the new name too: pack.py stores the readings the plan names
        if (V / f"{take}.{ext}").exists() and not (V / f"{new}.{ext}").exists():
            shutil.copy(V / f"{take}.{ext}", V / f"{new}.{ext}")
    for d in (ASSETS / FILM / "voice" / "lines", V / "lines"):
        if (d / f"{new}.json").exists():
            return
    meta = json.loads((V / f"{take}.json").read_text())
    rows = json.loads((V / "lines" / f"{take}.json").read_text())
    x, sr = sf.read(V / f"{take}.wav")
    norm = lambda w: "".join(c for c in w.lower() if c.isalpha())  # noqa: E731
    words = [(norm(w), a, b) for w, a, b in meta["words"] if norm(w)]
    before = sum(len(r["words"]) for r in rows[:n - 1])                 # the words of the lines before the merged one
    first = words[before:before + len(rows[n - 1]["words"])]            # her line, as the recogniser timed it
    second = words[before + len(first):]                                # his line
    h = int(0.01 * sr)
    env = np.array([20 * np.log10(np.sqrt(np.mean(x[i:i + h] ** 2)) + 1e-9) for i in range(0, len(x) - h, h)])
    lo, hi = int((first[-1][1] + 0.1) * 100), int(second[1][1] * 100)
    best, i = (0, lo, lo), lo
    while i < hi:                                                       # the longest silence in between
        if env[i] < -46:
            j = i
            while j < hi and env[j] < -46:
                j += 1
            best = max(best, (j - i, i, j))
            i = j
        else:
            i += 1
    _, q0, q1 = best
    t0a, t1a = rows[n - 1]["t0"], q0 / 100 + 0.06
    t0b, t1b = q1 / 100 - 0.03, rows[n - 1]["t1"]
    last = np.where(env[int(second[-1][1] * 100):] > -46)[0]
    end_b = second[-1][1] + (last[-1] + 1) / 100 if len(last) else second[-1][2]   # where his last word has really died away
    out = [dict(r) for r in rows[:n - 1]]
    for k, r in enumerate(rows[:n - 1], 1):
        shutil.copy(V / "lines" / f"{take}_{k:02d}.wav", V / "lines" / f"{new}_{k:02d}.wav")
    script = [t for _, t in json.loads((Path(__file__).resolve().parent / "ep_queue_voices.json").read_text())["lines"]]
    for k, (who, t0, t1, ws) in enumerate((("L", t0a, t1a, [[w, a, min(b, q0 / 100)] for w, a, b in first]),
                                           ("T", t0b, t1b, [[w, max(a, q1 / 100) if m == 0 else a, min(b, end_b)] for m, (w, a, b) in enumerate(second)])), n):
        ws[0][1] = min(ws[0][1], t0 + 0.03) if k > n else ws[0][1]      # his first word starts where his voice does, not where the recogniser put it
        seg = x[int(t0 * sr):int(t1 * sr)].copy()
        f = int(0.008 * sr)
        seg[:f] *= np.linspace(0, 1, f)
        seg[-f:] *= np.linspace(1, 0, f)
        sf.write(V / "lines" / f"{new}_{k:02d}.wav", seg, sr)
        text = script[k - 1].split("] ", 1)[-1]
        out.append({"n": k, "who": who, "text": text, "t0": round(t0, 3), "t1": round(t1, 3), "dur": round(t1 - t0, 3),
                    "words": [[w, round(float(a - t0), 3), round(float(b - t0), 3)] for w, a, b in ws], "matched": f"{len(ws)}/{len(text.split())}",
                    "peak_db": round(float(20 * np.log10(np.abs(seg).max() + 1e-9)), 1)})
    (V / "lines" / f"{new}.json").write_text(json.dumps(out, indent=1))
    print(f"{new}: the silence between lines {n} and {n + 1} is {(q1 - q0) / 100:.2f} s, at {q0 / 100:.2f}-{q1 / 100:.2f} s of the reading")
    for o in out:
        print("  ", o["n"], o["who"], f'{o["t0"]:6.2f}-{o["t1"]:6.2f} ({o["dur"]:.2f} s) {o["matched"]}  {o["text"]}  {o["words"]}')


def last_words():
    """The recogniser stretches the last word of a line through the silence after it, so the mouth shapes of that word
    are spread too thin and its last shape never shows ("queue" would end on a half-open mouth). The last word of every
    line now ends where the voice in that line does."""
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    for l in tl["lines"]:
        x, sr = sf.read(ROOT / l["wav"])
        h = int(0.01 * sr)
        env = np.array([np.sqrt(np.mean(x[i:i + h] ** 2)) for i in range(0, len(x) - h, h)])
        loud = np.where(env > env.max() * db(-22))[0]
        w = l["words"][-1]
        w[2] = round(float(max(w[1] + 0.12, min(w[2], (loud[-1] + 1) * 0.01))), 3)
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))


def coo(at=("look", 0.20), part=(2.08, 2.88), peak=-13.0):
    """One phrase of the bought pigeon (heard back as "the gentle cooing of a pigeon or dove") in the silence of the
    look: laid into the mix and the effects stem, mastered again, and named in the timeline (event "coo")."""
    out = ROOT / "work" / FILM / "sound"
    src = next(q for q in (ASSETS / FILM / "sound" / "raw" / "coo.wav", out / "raw" / "coo.wav") if q.exists())
    x, sr = sf.read(src)
    assert sr == SR
    x = fade(hp(x[int(part[0] * SR):int(part[1] * SR)], 180), 0.03, 0.18)
    x = x / (np.abs(x).max() + 1e-9) * db(peak)
    tl = json.loads((out / "timeline.json").read_text())
    t = tl["events"][at[0]][0] + at[1]
    i = int(t * SR)
    for name in ("raw", "fx"):
        y, _ = sf.read(out / f"{name}.wav")
        y[i:i + len(x)] += x[:len(y) - i, None]
        sf.write(out / f"{name}.wav", y, SR)
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(out / "raw.wav"), str(out / "final.wav")], check=True)
    tl["events"]["coo"] = [round(t, 3), round(t + len(x) / SR, 3)]
    tl["used"]["raw"] = sorted(set(tl["used"]["raw"]) | {"coo"})          # pack.py keeps the sounds named here
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))
    print(f'coo {tl["events"]["coo"]}')


# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends), options
PLAN = [
    ("L1", "L", A, 1, 0.20, {"db": 2.0}),            # her quiet cry for help, to us (0.2 s of picture first); a low hushed reading, 7 dB under him between 300 and 3400 Hz: lifted
    ("T2", "T", A, 2, 0.16, {}),                     # he has heard: proud, flat
    ("L3", "L", CS, 3, 0.34, {"db": 1.0}),           # a beat while she turns to him
    ("T4", "T", CS, 4, 0.06, {}),                    # at once: to him it is obvious (uncut: the punchline)
    ("HOLD", "look", 0.10, 0.97),                    # his eyes come back to us; she turns to the lens, caught; a pigeon coos (1.25 s fell flat for the watching model)
    ("PLATE", 0.0),
]
if __name__ == "__main__":
    resplit()
    if "--split" not in sys.argv:
        build(FILM, PLAN, bed={"stop": "L3"},                                   # her vlog music stops dead on "There's no queue."
              ambience={"file": "wind_a", "db": -35.0, "gusts": False},         # a still midday: the air of the first film, lower, without its gusts
              ending="beat", plate_hold=1.5)
        last_words()
        coo()
