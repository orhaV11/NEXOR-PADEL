"""The picture of the short "Front of the Queue" (vlog 02, "queue"): one shot in front of shut stadium gates.

    python3 src/film/toon/ep_queue.py --sheet        a contact sheet of the whole film (work/queue/sheet.jpg)
    python3 src/film/toon/ep_queue.py --stills 0.1 "T2+0.4" "L3+0.6" "look+0.6" "plate+0.8"
    python3 src/film/toon/ep_queue.py                the film (ads/21-vlog/venuvoy-vlog-02-queue-en_...mp4)

Run ep_queue_sound.py first: every time below is read from its timeline.
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import ease, on_twos, premul, read, wobble  # noqa: E402
from vlog import Vlog, held  # noqa: E402

# a still midday on bare paving: her calm hair, him without the blade of grass. The words the joke rests on get their mouth shapes by hand.
v = Vlog("queue", "ads/21-vlog/venuvoy-vlog-02-queue", luna="calm", timo="timo_clean",
         said={"queue": "C O", "front": "C A C", "because": "M C O C", "theres": "C A C", "gates": "C A C", "open": "O M C", "its": "C", "of": "C"})

# drawing, a point in it (the nearest shutter), where that lands in the frame, size. At this size the empty lanes fill the
# upper left, the shutters stand over his head, and the far stadium the painter copied from the style picture is out of frame.
BG = ("bg_gates_1", (1100, 1000), (800, 200), 1.02)


# ---- something only this film has: the pigeon. The painter put it where Luna's hair hides it, so it is cut out of the
#      painting and stood in the empty lane above her head (drawn by a hook, at the background's own depth).
def _pigeon(k=0.8):
    rgb = read(v.art / BG[0])[1846:1950, 420:530, :3]
    paving = np.median(rgb[:8].reshape(-1, 3), axis=0)
    ink = (np.abs(rgb - paving).max(axis=2) > 0.22).astype(np.uint8)          # its dark outline and markings (not its pale shadow)
    ink = cv2.morphologyEx(ink, cv2.MORPH_CLOSE, np.ones((3, 3), np.uint8))
    outside = (1 - ink).copy()
    cv2.floodFill(outside, np.zeros((ink.shape[0] + 2, ink.shape[1] + 2), np.uint8), (0, 0), 2)
    n, lab, stats, _ = cv2.connectedComponentsWithStats((outside != 2).astype(np.uint8))     # everything inside the outline
    whole = lab == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    alpha = cv2.GaussianBlur(whole.astype(np.float32), (0, 0), 0.7)
    bird = cv2.resize(np.dstack([rgb, alpha]), None, fx=k, fy=k, interpolation=cv2.INTER_AREA)
    shadow = np.zeros((40, 110, 4), np.float32)
    cv2.ellipse(shadow, (55, 20), (36, 7), 0, 0, 360, (0.25, 0.24, 0.26, 0.20), -1, cv2.LINE_AA)
    return premul(bird), premul(cv2.GaussianBlur(shadow, (0, 0), 3))


PIGEON, P_SHADOW = _pigeon()
P_FEET = (835, 1522)                                   # where it stands, in the pixels of the painting: open paving in the lane
P_AT = (BG[2][0] + (P_FEET[0] - BG[1][0]) * BG[3], BG[2][1] + (P_FEET[1] - BG[1][1]) * BG[3])


def pigeon(st, t, T):
    t2 = on_twos(T)
    peck = sum(1.0 for a in PECKS if 0.08 <= t2 - a < 0.24)                                        # a snap down, held for two drawings, a snap up
    coo = held(t2, v.t("coo+0.04"), v.t("coo.end-0.12"), 0.10, 0.25)                               # it swells while it coos
    throb = 0.025 * (1 if int(T * 12) % 2 else -1) * coo
    st.put(P_SHADOW, P_AT[0] - 4, P_AT[1] + 1, scale=BG[3], depth=0.45)
    st.put(PIGEON, P_AT[0], P_AT[1], scale=BG[3], turn=peck * 34 + coo * 5, sx=1 + coo * 0.05, sy=1 + coo * 0.10 + throb,
           anchor=(0.62, 0.90), depth=0.45)


PECKS = (1.25, 1.67)                                   # two pecks at the paving while she talks; then it stands still until it coos

# ---- her free hand: "look around you". Its own drawing and its own movement (drawn by a hook):
#      the rig's open hand was drawn on the windy Luna, whose flying scarf end hides the inner edge of the sleeve, so on the
#      calm Luna that edge showed as a cut; this one is a redraw of the calm drawing. It joins her coat only where it was
#      drawn (no lift), and the kit's piece would hold it stock-still for three seconds: here it sinks while he explains.
LUNA_AT = (362, 1545, 0.92, 0.0)
ARM = duo.arm(v.art, "luna_shrug_calm", v.s_luna, (1000, 1340, 1792, 2400), "luna_calm2")


def hand(st, t, T):
    up, down = v.t("L3+0.02"), v.t("T4.end+0.04")
    if not (up - 0.05 <= T < down + 0.35):
        return
    t2 = on_twos(T)
    x0, y0, k, lean = LUNA_AT
    dx, dy, turn, sx, sy = v.luna_move(T)                                       # it is part of her: it sways and pops with her body
    y = ease(t2, up, up + 0.2, 760.0, 0.0, "back")                              # up from below the frame with her first word
    hit = v.t("L3@queue")
    sh = math.sin((t2 - hit) * 30) * 14 * math.exp(-(t2 - hit) * 7) if t2 >= hit else 0.0    # one beat on "queue"
    y += ease(t2, v.t("T4@were"), v.t("T4.end"), 0.0, 70.0, "io")               # it sinks while he explains: she is losing
    y += ease(t2, down, down + 0.25, 0.0, 760.0, "in")                          # and falls away when he has said it
    st.put(ARM, x0 + dx + sh + wobble(t2, 11.0, 2.2) * 2.5, y0 + dy + y - abs(sh) * 0.6 + wobble(t2, 12.0, 1.9) * 3, scale=k,
           turn=turn + lean + sh * 0.12 + wobble(t2, 13.0, 1.6) * 0.7, sx=sx, sy=sy, anchor=duo.L_ANCHOR, depth=1.0)


# ---- one shot, a little wider than the first film's two-shot: the empty queue behind them is the joke, and the top of
#      the frame stays free of his head for the plate
v.shot("start", bg=BG, luna=LUNA_AT, timo=(872, 1110, 0.92), behind=pigeon, front=hand)

# ---- her face
v.faces(("start", "defend"),              # caught: big worried eyes on us (her quiet cry for help)
        ("L1@its-0.02", "guilty"),        # "It's ..." with a look at the one who did this
        ("L1.end+0.05", "defend"),        # back to us: help
        ("T2@queue", "guilty"),           # he has heard her: a look out of the corner of her eye
        ("L3-0.10", "turn"),              # she turns to him: reasonable
        ("look+0.14", "defend"))          # nothing to answer: back to the lens, caught
v.tilt(L1=-2.6, L3=2.2)
v.blinks(luna=[1.80, "look+0.58", "plate+0.90"], timo=[1.6, "L3+0.08"])

# ---- his eyes: proud, on us; down at her when she speaks to him; back to us when he has explained it
v.eyes(("start", "lens"),
       ("L3+0.15", None),
       ("look", "lens"))

# ---- the words on screen: under her chin, left of her raised hand
CAP = (430, 1431)
v.captions(
    place={"L1": CAP, "T2": CAP, "L3": (395, 1431), "T4": CAP},
    split={"L1": [("Gates open at five.", None), ("It’s one.", "L1@its")]},     # the time arrives when she says it
    text={"L3": "There’s no queue.", "T4": "Because we’re\nthe front."},
    on={"T2": "T2-0.05", "T4": "T4+0.04"},
    off={"L3": "T4+0.04", "T4": "plate-0.05"})                                   # the punchline stays up through the look

if __name__ == "__main__":
    v.main()
