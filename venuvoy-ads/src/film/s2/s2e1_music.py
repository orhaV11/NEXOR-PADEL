"""Rebuild the music of season 2, episode 1 from the edit list, so that a change of timing never leaves the music behind.

    python3 src/film/s2/s2e1_music.py

The map is the series' own: the fill and two passes of the four-bar groove under the talk, ending in a full stop on
the word "pack"; silence under "Maybe... the cat?"; the drop's first kick on the hard cut to the open suitcase; the
track running on under the reading of the plan; thinner under Luna's typing; a stop when Timo turns to the camera;
the final hit under the sign-off. Times come from the segments named by their marks.
"""
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
F, DS, BARS4 = 24, 14.235, 7.089
e = json.load(open(HERE / "s2e1_edit.json"))
t, T, D = 0.0, [], []
for s in e["segments"]:
    d = round((s["out"] - s["in"]) / s.get("speed", 1.0) * F) / F + round(s.get("hold", 0) * F) / F
    T.append(t)
    D.append(d)
    t += d
END = t


def seg(mark):
    return next(i for i, s in enumerate(e["segments"]) if mark in s.get("marks", {}))


def at(mark, src_t):
    i = seg(mark)
    return T[i] + (src_t - e["segments"][i]["in"])


i5 = seg("pack")
stop = at("pack", e["segments"][i5]["marks"]["pack"])  # the word "pack"
drop, dlen = T[seg("drop")], D[seg("drop")]
read, turn = T[seg("read")], T[seg("turn")]
look, sign = at("golf", 1.0), at("golf", 3.0)
g2 = stop - BARS4
g1 = g2 - BARS4
blocks = []
if g1 > 0.05:
    blocks.append({"at": 0.0, "len": round(g1, 4), "from_s": round(7.146 - g1, 4), "layer": "phone", "gain_db": -1.5, "fade_in": 0.05, "fade_out": 0.004})
blocks += [
    {"at": round(max(g1, 0), 4), "len": BARS4, "from_s": 7.146, "layer": "phone", "gain_db": -0.5, "fade_in": 0.004, "fade_out": 0.004},
    {"at": round(g2, 4), "len": BARS4, "from_s": 7.146, "layer": "phone", "gain_db": -0.5, "fade_in": 0.004, "fade_out": 0.02},
    {"at": round(drop, 4), "len": 0.6, "from_s": DS, "layer": "phone", "gain_db": 5, "fade_in": 0.002, "fade_out": 0.004},
    {"at": round(drop + 0.6, 4), "len": round(dlen - 0.6, 4), "from_s": DS + 0.6, "layer": "phone", "gain_db": 6, "fade_in": 0.004, "fade_out": 0.004},
    {"at": round(read, 4), "len": round(turn - read, 4), "from_s": round(DS + dlen, 4), "layer": "phone", "gain_db": 0, "fade_in": 0.004, "fade_out": 0.004},
    {"at": round(turn, 4), "len": round(look - turn, 4), "from_s": round(DS + turn - drop, 4), "layer": "phone", "gain_db": -2.5, "fade_in": 0.004, "fade_out": 0.03},
]
talk = {"track": "src/film/assets/s2/music/jersey_1.mp3", "out": "src/film/assets/s2/music/bed_s2e1_talk.wav", "length": END, "trim_db": -8, "blocks": blocks}
hit = {"track": "src/film/assets/s2/music/jersey_1.mp3", "out": "src/film/assets/s2/music/bed_s2e1_hit.wav", "length": END, "trim_db": -8,
       "blocks": [{"at": round(sign, 4), "len": 0.9, "from_s": 59.855, "layer": "phone", "gain_db": -5, "fade_in": 0.002, "fade_out": 0.5}]}
for name, arr in (("s2e1_music_talk.json", talk), ("s2e1_music_hit.json", hit)):
    (HERE / name).write_text(json.dumps(arr, indent=1))
    subprocess.run([sys.executable, str(HERE / "arrange.py"), str(HERE / name)], check=True, cwd=ROOT)
print(f"film {END:.2f} s | stop {stop:.2f} | drop {drop:.2f} | look {look:.2f} | sign-off {sign:.2f}")
