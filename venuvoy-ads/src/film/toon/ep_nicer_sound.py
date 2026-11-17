"""The sound of the short "It Is Nicer" (vlog 11, "nicer"): four lines in one shot outside an arena, then the plate.

    python3 src/film/toon/ep_nicer_sound.py            the soundtrack and the timeline (work/nicer/sound)

Two things here are this film's own, done after the kit's build() because the kit has no statement for them:
word_times() (the recogniser's word times, set by hand from the level of each line: it stretches a last word through
the silence after it and puts some words a tenth of a second early, and the mouths follow these times) and swell()
(the home crowd of the bought air rises while the other team's scarf goes down: the same recording, lifted for a
moment; nothing more was bought).
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ROOT, SR, build, db  # noqa: E402

FILM = "nicer"
A, C = "ldn_g25p_1", "ldn_g25p_3"   # two readings of the whole short; the listening model chose lines 1-3 of the first and line 4 of the third

# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends), options
PLAN = [
    ("L1", "L", A, 1, 0.12, {}),                     # the scarf is up from the first frame; she starts at once
    ("T2", "T", A, 2, 0.10, {}),                     # flat, at once
    ("L3", "L", A, 3, 0.22, {}),                     # she has looked at it; she turns to him: a simple fact
    ("T4", "T", C, 4, 0.40, {}),                     # the music has stopped; he weighs it (uncut: the pauses inside the punchline are his)
    ("HOLD", "sink", 0.45, 0.70),                    # nobody moves for 0.45 s; then the scarf goes down for 0.7 s
    ("PLATE", 0.10),
]

# where each word really is inside its line (seconds from the start of the line), read from the level of the voice,
# its low band and its hiss in steps of 20 ms; the recogniser's times were up to 0.12 s early and its last words ran on
WORDS = {
    "L1": [("basketball", 0.02, 0.72), ("night", 0.72, 1.23), ("look", 1.31, 1.42), ("what", 1.48, 1.60), ("i", 1.60, 1.72), ("got", 1.80, 2.14)],
    "T2": [("thats", 0.03, 0.44), ("the", 0.46, 0.64), ("other", 0.70, 1.00), ("teams", 1.02, 1.58)],
    "L3": [("its", 0.07, 0.36), ("nicer", 0.38, 1.38)],
    "T4": [("it", 0.03, 0.26), ("is", 0.43, 0.80), ("nicer", 0.82, 1.34), ("put", 1.56, 1.68), ("it", 1.68, 1.78), ("away", 1.78, 2.06)],
}


def word_times():
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    for l in tl["lines"]:
        new = WORDS[l["id"]]
        assert [w for w, _, _ in new] == [w for w, _, _ in l["words"]], (l["id"], l["words"])
        assert new[-1][2] <= l["t1"] - l["t0"] + 0.02
        l["words"] = [[w, a, b] for w, a, b in new]
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))


def swell(up=7.0, lead=0.15, fall=1.0):
    """The crowd of the street (heard back as "a large crowd cheering, chanting and shouting") comes up by `up` dB
    while the scarf goes down and ebbs away under the plate: laid into the mix and the air stem, mastered again, and
    named in the timeline (event "swell")."""
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    a, b = tl["events"]["sink"]
    plate = tl["events"]["plate"]
    air, sr = sf.read(out / "wind.wav")
    assert sr == SR
    t = np.arange(len(air)) / SR
    s = lambda x: np.clip(x, 0, 1) ** 2 * (3 - 2 * np.clip(x, 0, 1))  # noqa: E731
    env = s((t - (a - lead)) / (b - a + lead)) * (1 - s((t - (plate + 0.1)) / fall))   # up through the sink, down again before the film's own fade
    extra = air * ((db(up) - 1) * env)[:, None]
    for name in ("raw", "wind"):
        y, _ = sf.read(out / f"{name}.wav")
        sf.write(out / f"{name}.wav", y + extra, SR)
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(out / "raw.wav"), str(out / "final.wav")], check=True)
    tl["events"]["swell"] = [round(a - lead, 3), round(plate + 0.1 + fall, 3)]
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))
    print(f'swell {tl["events"]["swell"]} (+{up} dB at its top)')


if __name__ == "__main__":
    build(FILM, PLAN, bed={"stop": "L3"},                                    # her vlog music stops dead on "It's nicer."
          ambience={"file": "air", "db": -33.0, "gusts": False},             # the bought air of the street: a crowd, some way off
          ending="beat", plate_hold=1.5)
    word_times()
    swell()
