"""The picture of "The Switch", written with the kit: the model for a new sketch's picture file.

    python3 src/film/toon/example_film.py --sheet        a contact sheet of the whole film (work/kittest/sheet.jpg)
    python3 src/film/toon/example_film.py --stills 1.0 "L7.end+0.3" "sink+0.3"
    python3 src/film/toon/example_film.py                the film (into work/kittest/out, not into ads/)

Run example_sound.py first: every time below is read from its timeline.
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import H, W, Stage, bend_x, on_twos, pop  # noqa: E402
from vlog import Vlog, held  # noqa: E402

v = Vlog("kittest", "work/kittest/out/venuvoy-sketch-01-the-switch")
FAR = ("bg_hill_2", (335, 1200), (196, 640), 0.87)     # drawing, a point in it (its stadium), where that lands in the frame, size
NEAR2 = ("bg_hill_1", (370, 1660), (238, 596), 1.28)
NEAR3 = ("bg_hill_1", (370, 1660), (250, 440), 1.50)

# ---- something only this film has: Timo face down on the ridge behind her in the first shot (drawn by a hook)
S_PRONE, P_POS, P_TURN = 0.23, (1046, 652), -12.0
prone = duo.prone(v.art, S_PRONE)


def _ground_cover():
    """The grass he lies IN: ragged blades of the background itself laid back over the lower edge of his body, and a
    soft shadow under him."""
    a = math.radians(P_TURN)
    ux, uy, vx, vy = math.cos(a), math.sin(a), -math.sin(a), math.cos(a)

    def at(s, d):
        return [P_POS[0] + ux * s + vx * d, P_POS[1] + uy * s + vy * d]

    mask = np.zeros((H, W), np.float32)
    rng = np.random.default_rng(7)
    edge = (1150 - 1306) * S_PRONE
    for s0, s1, d in ((-535.0, -452.0, edge), (-328.0, 72.0, edge), (-452.0, -328.0, 3.0)):
        s = s0
        while s < s1:
            wd, ht, ln = rng.uniform(8, 15), rng.uniform(8, 26), rng.uniform(3, 13)
            cv2.fillPoly(mask, [np.array([at(s - wd / 2, d + 5), at(s + wd / 2, d + 5), at(s + ln, d - ht)], np.int32)], 1.0, cv2.LINE_AA)
            s += rng.uniform(5, 11)
        cv2.fillPoly(mask, [np.array([at(s0, d + 2), at(s1, d + 2), at(s1, d + 9), at(s0, d + 9)], np.int32)], 1.0, cv2.LINE_AA)
    mask = cv2.GaussianBlur(mask, (0, 0), 0.8)
    st = Stage()
    v.lay(st, *FAR)
    cover = np.dstack([st.frame * mask[..., None], mask])
    shadow = np.zeros((H, W, 4), np.float32)
    cx, cy = at(-235, edge + 8)
    cv2.ellipse(shadow, (int(cx), int(cy)), (285, 17), P_TURN, 0, 360, (0, 0, 0, 0.30), -1, cv2.LINE_AA)
    return cover, cv2.GaussianBlur(shadow, (0, 0), 7)


COVER, SHADOW = _ground_cover()


def timo_on_the_ground(st, t, T):
    acc = v.accent(T, "T", ground=True)
    t2 = on_twos(T)
    heave = acc * 0.10 + math.sin(t2 * 3.3) * 0.030                       # out of breath: his back rises and falls
    lift = held(t2, v.t("T2-0.02"), v.t("T2.end-0.05"), 0.1, 0.16) * (4.5 + acc * 4.0)   # his head comes off the grass to say it
    lift += math.sin(max(0.0, t2 - v.t("L1+0.5")) * 2.1) ** 8 * 1.6
    up = v.t("T2-0.10") <= T < v.t("T2.end+0.12")                         # he objects with one finger
    a, b = pop(t2, v.t("T2-0.10"), 0.07)
    c, d = pop(t2, v.t("T2.end+0.12"), 0.05)
    state = {"0scarf": ("1", "2")[int(T * 2.1) % 2], "1pose": "finger" if up else None}
    st.put(SHADOW, W / 2, H / 2, depth=0.45)
    lying = bend_x(prone.get(**state), (850 * S_PRONE, 1120 * S_PRONE), 930 * S_PRONE, 760 * S_PRONE, lift)
    st.put(lying, P_POS[0], P_POS[1], turn=P_TURN + acc * 1.2, sx=a * c, sy=(1 + heave) * b * d, anchor=(0.86, 0.85), depth=0.45)
    st.put(COVER, W / 2, H / 2, depth=0.45)


# ---- the three shots: wide with him on the ground; the two of them; sixteen per cent closer
v.shot("start", bg=FAR, luna=(362, 1545, 0.92, 0.0), behind=timo_on_the_ground)
v.shot("cut1", bg=NEAR2, luna=(372, 1556, 1.00, -1.2), timo=(872, 935, 1.00))        # "cut1" is a whip in the sound plan
v.shot("cut2", bg=NEAR3, luna=(345, 1490, 1.16, 1.0), timo=(890, 830, 1.16))

# ---- her face
v.faces(("T2+0.95", "guilty"),            # caught
        ("L3-0.08", "defend"),            # she explains it to US, big-eyed
        ("L3@it-0.05", "guilty"),         # a look back at him
        ("L3@hikes-0.04", None),          # "hikes" is good news again
        ("cut1", "defend"),
        ("T6+0.30", "guilty"),            # his question is coming: a look out of the corner of her eye
        ("L7-0.10", "turn"),              # she rounds on him
        ("L7.end+0.30", "guilty"),        # he is looking at us; she steals a look at him
        ("cut2", "defend"), ("cut2+0.30", "guilty"),
        ("L9-0.20", "read"),              # she reads it off her phone, pleased
        ("L9.end+0.12", None),            # and beams at us: a run!
        ("thud+0.12", "down"),            # where did he go
        ("thud+0.54", "read_b"),          # eyes down, mouth shut: the next line is his
        ("L11-0.15", None))
v.tilt(L1=3.2, L3=-2.6, L5=3.0, L7=2.2, L11=-3.4)

# ---- his eyes, and what he does
v.eyes(("L7.end", "lens"),                # he looks at us the moment her line ends (and the music stops)
       ("cut2", "narrow"),
       ("L9.end+0.06", "shut"))           # a run. He closes his eyes.
v.sink("sink")                            # and goes down during the stretch called "sink" in the sound plan
v.shake("thud")
v.finger(up="T10-0.16", down="L11.end+0.10", wag="T10")   # his finger comes up with "Flip it back." and gives up after "He loves it."

# ---- her free hand
v.piece("hand", up="L7+0.02", down="L7.end+0.22", lift=105, dx=26, shake="L7@switch", rise=0.2)
v.piece("phone", up="L9-0.38", down="thud+0.05", lift=110)

# ---- the words on screen
v.captions(
    place={"T2": (600, 345),               # above him: he is the one on the ground
           "L9": (394, 1352),              # beside the phone she reads from
           "T10": (560, 1395)},            # beside his finger
    split={"L3": [("The planner had a switch.", None), ("It said “hikes”.", "L3@it")]},
    hold={"L9": ("“Tomorrow morning.\n", "Run.”", "L9@run")},     # the word arrives when she says it
    off={"L7": "L7.end+0.06", "T8": "L9-0.38", "L9": "L9.end+0.30"},
    on={"T8": "T8-0.04"},
    text={"T6": "So why did you fl—", "T8": "…What else did you flip?"})

if __name__ == "__main__":
    v.main()
