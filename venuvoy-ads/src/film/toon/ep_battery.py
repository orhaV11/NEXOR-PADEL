"""The picture of the short "Four Percent" (vlog 10, "battery"): one shot at a stadium gate at dusk, then black.

    python3 src/film/toon/ep_battery.py --sheet        a contact sheet of the whole film (work/battery/sheet.jpg)
    python3 src/film/toon/ep_battery.py --stills 0.1 "T2+0.4" "L3+0.3" "L3@why+0.2" "T4+0.9" "black-0.03" "black+0.1" "plate+0.8"
    python3 src/film/toon/ep_battery.py                the film (ads/21-vlog/venuvoy-vlog-10-battery-en_...mp4)

Run ep_battery_sound.py first: every time below is read from its timeline (the event "black" is the frame her phone dies).
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import bend, ease, on_twos, wobble  # noqa: E402
from vlog import ROOT, Vlog  # noqa: E402


class Gate(Vlog):
    """Timo leans in over her shoulder. The kit stands him upright and gives him a 7-pixel lean while he speaks; here
    his whole figure tips about the middle of his chest, comes nearer (larger) and straightens again, as POSE says."""

    def draw_timo(self, st, T, place):
        x0, y0, k = place
        t = on_twos(T)
        tip, near, dx, dy = pose(t)
        ang, hx, hy, lean = self.timo_head(T)
        s = self.s_timo
        img = bend(self.timo.get(**self.timo_state(T)), (duo.T_NECK[0] * s, duo.T_NECK[1] * s), duo.T_STILL * s, duo.T_FULL * s, ang, hx, hy)
        breathe = 1 + math.sin(t * 1.9) * 0.004
        st.put(img, x0 - lean * 7 + dx, y0 + lean * 4 + dy, scale=k * near, turn=wobble(t, 7.0, 0.8) * 0.3 - lean * 1.3 + tip, sy=breathe,
               anchor=duo.T_ANCHOR, depth=0.82)


# a still evening in a crowd: her calm hair, him without the blade of grass. The words the joke rests on get their mouth shapes by hand.
v = Gate("battery", "ads/21-vlog/venuvoy-vlog-10-battery", luna="calm", timo="timo_clean",
         said={"ten": "C", "minutes": "M C", "to": "O", "kick": "C", "off": "O C", "whats": "O A C", "your": "O", "battery": "M A C", "four": "O", "why": "O A",
               "because": "M C O C", "tickets": "C", "on": "O", "phone": "C O"})


# ---- the place. The painting has its gates in the middle of its height; with the queue standing above Luna's head the
#      painting ends before the frame does, so its paving is carried on downward (its last rows mirrored: plain paving,
#      behind the two of them). Made once by code from the bought painting, kept beside it as bg_gate_1x.
def _longer(name="bg_gate_1", new="bg_gate_1x", more=560):
    src = v.art / name                                   # (asked for every time: pack.py keeps the painting itself too)
    if not v.art.has(new):
        img = cv2.imread(str(src), cv2.IMREAD_UNCHANGED)
        cv2.imwrite(str(ROOT / "work" / v.film / "art" / f"{new}.png"), np.vstack([img, img[-more:][::-1]]))
    return new


# drawing, a point in it (the feet of the queue), where that lands in the frame, size
BG = (_longer(), (560, 1380), (330, 760), 0.80)

# ---- Timo's lean, in the order it happens: (tip in degrees, size, shift x, shift y)
PEER = (-7.5, 1.10, -8.0, 36.0)       # into the lens, at the corner where her battery is
UP = (-1.0, 1.00, 0.0, 0.0)           # he has his answer: he straightens
TO_HER = (-4.5, 1.02, -8.0, 10.0)     # and explains it to her, patiently


def pose(t):
    a = ease(t, 0.0, 1.7, 0.45, 1.0, "io")                                   # he comes nearer and nearer the lens while she talks
    b = ease(t, v.t("L3@four+0.45"), v.t("L3@four+0.75"), 0.0, 1.0, "back")   # "Four.": up, away from it (in her breath before "Why?")
    c = ease(t, v.t("T4-0.16"), v.t("T4+0.30"), 0.0, 1.0, "io")              # "Because...": down to her
    return tuple(UP[i] + (PEER[i] - UP[i]) * a * (1 - b) + (TO_HER[i] - UP[i]) * c for i in range(4))


# ---- her phone dies: from the event "black" on the picture is black (the caption and the plate are laid over it by the kit)
BLACK = round(v.t("black") * 24) / 24      # the frame itself (the timeline keeps three decimals: 6.917 for 166/24)


def dead(frame, T):
    return np.zeros_like(frame) if T >= BLACK - 1e-4 else frame


# ---- one shot: her face close, his larger than usual over her shoulder, the gate and its queue above her head
v.shot("start", bg=BG, luna=(372, 1556, 1.00, -1.2), timo=(835, 1040, 1.22), grade=dead)

# ---- her face
v.faces(("start", None),                  # sunny: ten minutes!
        ("T2.end-0.05", "defend"),        # she looks at her own screen: big eyes on us
        ("L3@why-0.08", None),            # innocent again
        ("T4@tickets+0.12", "guilty"))    # it dawns: a look at him
v.tilt(L1=3.2, L3=-2.6)
v.blinks(luna=[1.93, "T4+0.25"], timo=[])       # he does not blink: a stare, then eyes shut, then eyes on her

# ---- his eyes
v.eyes(("start", "narrow"),               # narrowed at the lens: he is reading her screen
       ("L3@four+0.45", "shut"),          # four. He closes his eyes.
       ("T4-0.14", None))                 # and opens them on her

# ---- the words on screen
v.captions(
    hold={"L3": ("…Four. ", "Why?", "L3@why"),                                  # each arrives when it is said
          "T4": ("Because the tickets\nare on that ", "ph—", "T4@phone")},
    text={"T2": "What’s your battery?"},
    place={"T4": (540, 1436)},                                                   # two lines: a little lower, clear of her chin
    off={"T4": BLACK + 0.30})                                                    # his half sentence stays on the black for 0.3 s
v.plate(y=860)                                                                   # on black: in the middle (of the 4:5 version too)

if __name__ == "__main__":
    v.main()
