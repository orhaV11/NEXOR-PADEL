"""The sound of the sketch "The Look" (vlog 03, "look"): nine lines behind the basket of an indoor arena, two cuts of
silence (his look at her, his look at us), a crowd's groan for the miss, then the sung name.

    python3 src/film/toon/ep_look_sound.py            the soundtrack and the timeline (work/look/sound)

One thing here is this film's own, done after the kit's build(): word_times() (the recogniser stretches a word through
the silence after it, so its mouth shapes would be spread too thin: the last word of every line, and "Sorry" and
"What" in the two lines with a pause in them, are put where the voice is).
The bought sounds: "air" (heard back as a cheering, shouting crowd that swells and fades) and "groan" (heard back as
a loud collective groan of disappointment that tapers into chatter). A third, "miss" (a ball off the rim, then the
groan), came back as a thud and footsteps and is NOT used: the miss is the groan alone, and the picture carries it.
"""
import json
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ROOT, build, db  # noqa: E402

FILM = "look"
A, B, C = "ldn_g25p_1", "ldn_g25p_2", "ldn_g25p_3"   # three readings of the whole sketch; each line from the one the listening model chose


# words the recogniser put in the wrong place, measured on the loudness of the line (seconds inside the line)
RETIME = {"L5": {"what": (0.05, 0.25), "look": (0.28, 0.61)},      # "What | look?": the stop between them is at 0.26
          "L6": {"sorry": (0.12, 0.70), "what": (1.22, 1.33)}}     # "Sorry!" ends at 0.70; half a second of nothing; "What" begins at 1.22


def word_times():
    """The last word of every line ends where the voice in that line does (not where the recogniser put it)."""
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    for l in tl["lines"]:
        for w in l["words"]:
            if w[0] in RETIME.get(l["id"], {}):
                w[1], w[2] = RETIME[l["id"]][w[0]]
        x, sr = sf.read(ROOT / l["wav"])
        h = int(0.01 * sr)
        env = np.array([np.sqrt(np.mean(x[i:i + h] ** 2)) for i in range(0, len(x) - h, h)])
        loud = np.where(env > env.max() * db(-22))[0]
        w = l["words"][-1]
        w[2] = round(float(max(w[1] + 0.12, min(w[2], (loud[-1] + 1) * 0.01))), 3)
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))


# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends), options
PLAN = [
    ("L1", "L", B, 1, 0.15, {"tighten": 0.14}),      # she calls it over the crowd (the setup, not a punchline: its breaths are trimmed)
    ("T2", "T", C, 2, 0.18, {}),                     # flat. He does not move
    ("L3", "L", A, 3, 0.10, {}),                     # she rounds on him at once
    ("T4", "T", C, 4, 0.38, {}),                     # a beat: he decides (reading 1, the listening model's choice, drops 17 dB on "the look": reading 3 keeps the words level)
    ("L5", "L", B, 5, 0.14, {}),                     # amused
    ("HOLD", "stare", -0.20, 0.55),                  # his eyes come down on her, and stay (her line's last 0.27 s is silence: the stretch starts inside it; 0.67 s from "look?" to "Sorry!")
    ("L6", "L", A, 6, 0.00, {}),                     # a reflex: "Sorry!" (the pause before "What did I do?" is the reading's own, uncut)
    ("T7", "T", C, 7, 0.32, {}),                     # quiet, satisfied; the vlog music stops dead as he ends
    ("CUT", 0.22),                                   # a jump cut, a fifth closer
    ("HOLD", "look", 0.18, 0.78),                    # his eyes lift to us: the flat look, in silence
    ("HOLD", "miss", 0.00, 1.05),                    # the crowd groans: the shooter has missed
    ("L8", "L", B, 8, 0.00, {}),                     # hushed, to him, over the end of the groan
    ("T9", "T", B, 9, 0.42, {}),                     # a beat, then the last word, to us
    ("PLATE", 1.00),                                 # the end picture holds a second: he looks at us, she looks up at him
]
if __name__ == "__main__":
    build(FILM, PLAN, bed={"stop": "T7"},
          ambience={"file": "air", "db": -30.0, "gusts": False},
          fx=[{"name": "groan", "file": "groan", "at": ("miss", 0, 0.0), "whole": True, "start": 0.0, "len": 1.75, "fade_in": 0.02, "fade_out": 0.75, "db": -9}])
    word_times()
