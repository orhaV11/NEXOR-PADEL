"""The sound of the sketch "Twenty Years" (vlog 05, "glove"): eleven lines in the top rows of a ballpark on a still,
sunny afternoon, three shots, a ball that drops into Timo's glove on "ready", then the sung name.

    python3 src/film/toon/ep_glove_sound.py            the soundtrack and the timeline (work/glove/sound)

Three things here are this film's own, done around the kit's build() because the kit has no statement for them:
glove_pop()   the effect laid on the catch, "pop", made once from the bought "smack" (heard back as "a single sharp,
              sudden smack"; measured, it is a 40 ms burst near 12 kHz at the very end of the file: a tick, which a
              phone hardly plays): the burst itself for the attack, plus the same burst played 4.5 times slower for a
              leather body near 2.5 kHz. Nothing more was bought.
word_times()  the last word of every line ends where the voice in that line does (the recogniser carries it on
              through the silence after it, which would spread its mouth shapes too thin).
crowd()       the bought air ("air", heard back as "a steady rushing of moving air with faint, indistinct chatter of a
              distant crowd") rises as the ball comes down out of the sky and stays up a little after the catch: the
              crowd far below saw it. The stems are summed again exactly as the kit sums them and mastered again.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal as ss

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ASSETS, ROOT, SR, build, db, fade, hp, lp  # noqa: E402

FILM = "glove"
A, B, C = "ldn_g25p_1", "ldn_g25p_2", "ldn_g25p_3"   # three readings of the whole sketch (all words heard back in all three,
#   except "brought" in reading 1's first line). His lines are reading 2's (one dark, low, flat voice throughout), except
#   "I've won baseball." from reading 1: the listening model's choice for the punchline, and measured no further from
#   reading 2's voice than reading 2's own take. Her lines are the listening model's choices (her readings do not differ
#   in colour more than her lines do within one reading); her whisper "It's in there." is reading 1's, the only true whisper.
LAND_AFTER_READY = 0.06                              # the ball lands just after "ready" begins: on the stressed syllable


def lines_json(take):
    keep = ASSETS / FILM / "voice" / "lines" / f"{take}.json"
    return json.loads((keep if keep.exists() else ROOT / "work" / FILM / "voice" / "lines" / f"{take}.json").read_text())


def ready_at():
    """Seconds into T4 (reading 2, line 4, not tightened) where "ready" begins."""
    l = next(r for r in lines_json(B) if r["n"] == 4)
    return next(a for w, a, b in l["words"] if w.startswith("ready"))


def glove_pop(name="pop", src="smack"):
    out = ROOT / "work" / FILM / "sound" / "raw" / f"{name}.wav"
    if out.exists() or (ASSETS / FILM / "sound" / "raw" / f"{name}.wav").exists():
        return
    path = next(p for p in (ASSETS / FILM / "sound" / "raw" / f"{src}.wav", ROOT / "work" / FILM / "sound" / "raw" / f"{src}.wav") if p.exists())
    x, sr = sf.read(path)
    x = x.mean(axis=1) if x.ndim > 1 else x
    assert sr == SR
    burst = x[int(2.966 * SR):]                                   # the burst (from just before it rises to the end of the file)
    burst = burst * np.hanning(2 * len(burst))[len(burst):] ** 0.5   # its cut-off end faded
    slow = ss.resample_poly(burst, 9, 2)                          # 4.5 times slower: the same crack a leather pop low
    slow = lp(hp(slow, 180), 5200, 2)
    own = np.convolve(np.abs(slow), np.ones(int(0.004 * SR)) / int(0.004 * SR), "same")
    slow = slow / (own + own.max() * 0.05)                        # its own ragged envelope (the burst's sub-bursts, slowed) taken out...
    tt = np.arange(len(slow)) / SR
    slow = slow * np.minimum(1.0, tt / 0.0015) * np.exp(-tt / 0.026)   # ...and one clean slap put on: up in 1.5 ms, gone in about 0.1 s
    n = len(slow)
    attack = np.zeros(n)
    attack[:len(burst)] = burst / (np.abs(burst).max() + 1e-9) * 0.45
    pop = slow / (np.abs(slow).max() + 1e-9) + attack
    pop = fade(np.concatenate([np.zeros(int(0.01 * SR)), pop]), 0.002, 0.03)
    sf.write(out, pop / (np.abs(pop).max() + 1e-9) * db(-1.0), SR)


def word_times():
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    for l in tl["lines"]:
        x, sr = sf.read(ROOT / l["wav"])
        h = int(0.01 * sr)
        env = np.array([np.sqrt(np.mean(x[i:i + h] ** 2)) for i in range(0, len(x) - h, h)])
        loud = env > env.max() * db(-24)
        on = np.where(loud)[0]
        w = l["words"][-1]
        w[2] = round(float(max(w[1] + 0.12, min(w[2], (on[-1] + 1) * 0.01))), 3)
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))


def crowd(rise=3.0, cheer=4.5, stay=2.0):
    """The air: up `rise` dB over the 1.3 s before the catch (the ball is in the air), `cheer` dB just after it,
    settling to `stay` dB over 2.5 s and staying there to the end (the crowd round them cheering)."""
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    land = tl["events"]["land"]
    stems = {name: sf.read(out / f"{name}.wav")[0] for name in ("voices", "wind", "fx", "music")}
    t = np.arange(len(stems["wind"])) / SR
    up = np.where(t < land, rise * np.clip((t - (land - 1.3)) / 1.3, 0, 1) ** 2,
                  stay + (cheer - stay) * np.exp(-np.clip(t - land, 0, None) / 1.1) * np.clip((t - land) / 0.15, 0, 1) + (rise - stay) * np.clip(1 - (t - land) / 0.15, 0, 1))
    stems["wind"] = stems["wind"] * db(up)[:, None]
    sf.write(out / "wind.wav", stems["wind"], SR)
    mix = sum(stems.values())
    tail = int(0.3 * SR)
    mix[-tail:] *= (np.linspace(1, 0, tail) ** 1.5)[:, None]
    sf.write(out / "raw.wav", mix, SR)
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(out / "raw.wav"), str(out / "final.wav")], check=True)


# id, who, reading, number of the line in the reading, gap before it (s after the previous line or cut), options
PLAN = [
    ("L1", "L", B, 1, 0.25, {}),                     # to us, amused, fond; he stands behind her with his eyes shut and the glove open
    ("T2", "T", B, 2, 0.10, {}),                     # his eyes open on her: proud, flat
    ("L3", "L", C, 3, 0.14, {}),                     # turned to him, really asking
    ("CUT", 0.40),                                   # the question lands; his brow goes down; a jump cut closer
    ("T4", "T", B, 4, 0.06, {"cut_in_word": ("ball", 0.68, 0.22)}),   # eyes shut, the lecture; the ball lands on "ready"; he goes on. Cut off inside "the ball—"
    ("L5", "L", A, 5, -0.20, {}),                    # "Timo." over his last word
    ("T6", "T", B, 6, 0.08, {}),                     # calm, eyes still shut
    ("L7", "L", B, 7, 0.30, {}),                     # a beat; small. The music stops dead as it ends
    ("CUT", 0.06),                                   # tighter: both faces and the glove with the ball in it
    ("T8", "T", B, 8, 0.95, {}),                     # his eyes open on us and stay there 0.8 s before he speaks
    ("L9", "L", A, 9, 0.22, {}),                     # whispered, awed, turned to him
    ("T10", "T", A, 10, 0.42, {}),                   # a beat; to the lens, the same flat face (uncut: the punchline)
    ("L11", "L", A, 11, 0.30, {}),                   # sunny, to us
    ("PLATE", 0.75),                                 # she beams; he stares at us with the ball in his glove
]

if __name__ == "__main__":
    glove_pop()
    build(FILM, PLAN, bed={"stop": "L7"},                                       # her vlog music stops dead on "Your glove."
          ambience={"file": "air", "db": -32.0, "gusts": False},
          fx=[{"name": "land", "file": "pop", "at": ("T4", 0, ready_at() + LAND_AFTER_READY), "whole": True, "start": 0.0, "len": 0.32,
               "fade_in": 0.002, "fade_out": 0.06, "db": -9, "push": True}])   # laid whole: as a hit the kit would start it at the loudest moment (the body) and lose the attack
    word_times()
    crowd()
    tl = json.loads((ROOT / "work" / FILM / "sound" / "timeline.json").read_text())
    print("land", tl["events"]["land"], " T4@ready", round(tl["lines"][3]["t0"] + ready_at(), 3))
