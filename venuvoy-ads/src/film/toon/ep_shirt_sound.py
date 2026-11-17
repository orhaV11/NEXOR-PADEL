"""The sound of the short "They're Reloading" (vlog 13, "shirt"): five lines in one shot in the stand of an indoor
basketball arena, a T-shirt cannon fired twice far away, then the plate.

    python3 src/film/toon/ep_shirt_sound.py            the soundtrack and the timeline (work/shirt/sound)

Two things here are this film's own, done around the kit's build():
far_cannon() makes the effect laid twice, "cannon_far", from the bought "cannon" (heard back as "a sudden, loud cannon
blast, then a deep, echoing rumble that fades away"): only its first 0.6 s (the low thump and the start of the rush of
air), dulled and faded, so that it is a T-shirt cannon across the arena and not a gun next to her.
word_times() puts the last word of every line, and "here" before the pause in L1, where the voice is (the recogniser
stretches a word through the silence after it, so its mouth shapes would be spread too thin).
The air: "air" (heard back as "continuous background crowd chatter", with a ball bouncing and sneakers squeaking: a
game going on in an arena, which is where we are).
"""
import json
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ASSETS, ROOT, SR, build, db, fade, hp, lp  # noqa: E402

FILM = "shirt"
A, B, C = "ldn_g25p_4", "ldn_g25p_5", "ldn_g25p_6"   # three readings of the revised script (Timo has the last line)


def far_cannon(name="cannon_far", src="cannon", part=(0.0, 0.62)):
    """The cannon across the arena: the first 0.62 s of the bought sound (a low thump, then the rush of air begins),
    above 70 Hz and under 2.4 kHz, faded out. Written once into work/shirt/sound/raw (pack.py keeps it as an fx file)."""
    out = ROOT / "work" / FILM / "sound" / "raw" / f"{name}.wav"
    if out.exists() or (ASSETS / FILM / "sound" / "raw" / f"{name}.wav").exists():
        return
    path = next(p for p in (ASSETS / FILM / "sound" / "raw" / f"{src}.wav", ROOT / "work" / FILM / "sound" / "raw" / f"{src}.wav") if p.exists())
    x, sr = sf.read(path)
    x = x.mean(axis=1) if x.ndim > 1 else x
    assert sr == SR
    x = x[int(part[0] * SR):int(part[1] * SR)]
    x = lp(hp(x, 70), 2400, 2)
    x = fade(x, 0.004, 0.34)
    sf.write(out, x / (np.abs(x).max() + 1e-9) * db(-1.0), SR)


# words whose end the recogniser carried through a pause, measured on the loudness of the cut line (seconds in the line, before tightening)
RETIME = {"L1": {"here": 0.78}}


def word_times():
    """The last word of every line ends where the voice in that line does (not where the recogniser put it); "here"
    in L1 ends where her voice stops before the pause."""
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    for l in tl["lines"]:
        x, sr = sf.read(ROOT / l["wav"])
        h = int(0.01 * sr)
        env = np.array([np.sqrt(np.mean(x[i:i + h] ** 2)) for i in range(0, len(x) - h, h)])
        loud = env > env.max() * db(-24)
        for k, w in enumerate(l["words"][:-1]):
            if w[0] in RETIME.get(l["id"], {}):     # the voice stops inside this word's span: end it at the start of the silence
                a, b = int(w[1] * 100), int(l["words"][k + 1][1] * 100)
                quiet = [i for i in range(a + 10, b) if not loud[i:i + 6].any()]
                if quiet:
                    w[2] = round(quiet[0] * 0.01 + 0.02, 3)
        w = l["words"][-1]
        on = np.where(loud)[0]
        w[2] = round(float(max(w[1] + 0.12, min(w[2], (on[-1] + 1) * 0.01))), 3)
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))
    for l in tl["lines"]:
        print(f'  {l["id"]} words {l["words"]}')


# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends), options
PLAN = [
    ("L1", "L", A, 1, 0.10, {"tighten": 0.22}),      # she shouts it over the crowd, waving (the setup: the pause after "Over here!" kept to 0.22 s; the cannon fires in it)
    ("HOLD", "land", 0.02, 0.70),                    # the shirt lands on his hair as her line ends; the music stops dead; his eyes come to us; nobody else moves
    ("L2", "L", A, 2, 0.03, {}),                     # she has not seen it: to us, sorry for herself
    ("T3", "T", C, 3, 0.20, {}),                     # he waits for her to finish: patient
    ("L4", "L", B, 4, 0.05, {}),                     # at once, not looking at him (reading 5: the listening model heard reading 4's as casual)
    ("T5", "T", C, 5, 0.32, {}),                     # a beat: his eyes come to us; then flat (the punchline, uncut)
    ("PLATE", 0.70),                                 # his look at us after the last word (0.55 s felt cut short to the watching model); the cannon goes again; the plate
]
if __name__ == "__main__":
    far_cannon()
    build(FILM, PLAN, bed={"stop": "L1", "db": -5.0},                          # her vlog music stops dead as the shirt lands (2.5 dB over the kit's level: it plays only under one shouted line, and the watching model did not hear it)
          ambience={"file": "air", "db": -32.0, "gusts": False},
          fx=[{"name": "cannon", "file": "cannon_far", "at": ("L1", 0, 0.78), "whole": True, "start": 0.0, "len": 0.62, "fade_in": 0.004, "fade_out": 0.34, "db": -12},
              {"name": "again", "file": "cannon_far", "at": ("T5", 1, 0.34), "whole": True, "start": 0.0, "len": 0.62, "fade_in": 0.004, "fade_out": 0.34, "db": -17}],
          ending="beat", plate_hold=1.5)
    word_times()
