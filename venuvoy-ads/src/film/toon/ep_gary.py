"""The picture of the sketch "Gary" (vlog 06, "gary"): the tailgate in a stadium car park, two shots.

    python3 src/film/toon/ep_gary.py --sheet        a contact sheet of the whole film (work/gary/sheet.jpg)
    python3 src/film/toon/ep_gary.py --stills 0.1 "T3+0.3" "L4+0.3" "cut1+0.1" "look+0.4" "plate+1.0"
    python3 src/film/toon/ep_gary.py                the film (ads/21-vlog/venuvoy-vlog-06-gary-en_...mp4)

Run ep_gary_sound.py first: every time below is read from its timeline.

She shows us the one sandwich they brought and tells us Timo hates tailgates; behind her, from the first frame, he
stands deadpan in a giant foam cowboy hat. A stranger called Gary has adopted him. Three things are this film's own,
drawn by hooks: the hat (laid on his head between him and her, turning with his head), her sandwich (in front; it
comes up proud, sinks when she sees the hat, and droops at the end) and two small cans painted out of the background.
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import ease, key_out, on_twos, premul, read, wobble  # noqa: E402
from vlog import Vlog  # noqa: E402

# a still, bright morning: her calm hair (no flying hair to keep under his mouth, so he can stand lower), him without
# the blade of grass. The words the joke rests on get their mouth shapes by hand.
v = Vlog("gary", "ads/21-vlog/venuvoy-vlog-06-gary", luna="calm", timo="timo_clean",
         said={"american": "A M E C A C", "football": "O O A", "tailgate": "C A C A", "brought": "M O C", "sandwich": "C A C E C",
               "timo": "C E M O", "hates": "A C", "says": "C E C", "just": "C A C", "car": "C A", "park": "M A C",
               "gary": "C A E", "hello": "E O", "whos": "O C", "has": "A C", "grill": "C E C", "dont": "O C", "people": "M E M O",
               "hes": "E C", "invited": "E M A C E", "thanksgiving": "C A C C E M", "can": "C", "i": "C", "come": "C O M",   # "...Can I come?" stays small
               "family": "M A M E", "thing": "C E C"})

# drawing, a point in it (the tailgate party: the red canopy and the grill), where that lands in the frame, size
WIDE = ("bg_carpark_1", (450, 1000), (250, 640), 0.88)
NEAR = ("bg_carpark_1", (450, 1000), (225, 600), 1.04)       # eighteen per cent closer


# ---- the background: the painter put a can and a cup in two far party-goers' hands. Painted out (grown in from round
#      them) on a small patch laid over the painting.
def _no_cans():
    x0, y0, x1, y1 = 600, 1080, 680, 1170
    rgb = read(v.art / WIDE[0])[y0:y1, x0:x1, :3]
    hole = np.zeros(rgb.shape[:2], np.uint8)
    cv2.rectangle(hole, (624 - x0, 1098 - y0), (640 - x0, 1112 - y0), 1, -1)    # the can (above the hand that holds it)
    cv2.rectangle(hole, (642 - x0, 1131 - y0), (658 - x0, 1147 - y0), 1, -1)    # the cup
    fixed = cv2.inpaint((rgb[..., ::-1] * 255 + 0.5).astype(np.uint8), hole * 255, 5, cv2.INPAINT_TELEA)[..., ::-1].astype(np.float32) / 255
    alpha = cv2.GaussianBlur(cv2.dilate(hole, np.ones((5, 5), np.uint8)).astype(np.float32), (0, 0), 1.5)
    alpha[:3], alpha[-3:], alpha[:, :3], alpha[:, -3:] = 0, 0, 0, 0
    return np.dstack([fixed * alpha[..., None], alpha]).astype(np.float32), (x0, y0)


PATCH, PATCH_AT = _no_cans()


def car_park(st, t, T):
    name, src, at, k = v.shot_at(T)["bg"]
    st.put(PATCH, at[0] + (PATCH_AT[0] - src[0]) * k, at[1] + (PATCH_AT[1] - src[1]) * k, scale=k, anchor=(0.0, 0.0), depth=0.45)


# ---- the hat. It was drawn on a smaller copy of him (timo_room: timo_clean at 0.8, moved 153 px right and 550 px down,
#      measured by matching the two drawings) so that all of it fits in the picture; here it is cut out of that drawing
#      (what changed from timo_room), brought back to his own size and laid on his head with exactly the turn and lean
#      the kit gives his head, so the very same face and mouths of the rig work under it.
SC, OFF = 0.8, (153.2, 550.1)


def _hat():
    room, hat = read(v.art / "timo_room")[..., :3], read(v.art / "timo_hat")[..., :3]
    kh = key_out(hat)
    m = (np.abs(room - hat).max(axis=2) > 0.12) & (kh[..., 3] > 0.5)
    m[1150:] = False
    m = cv2.morphologyEx(m.astype(np.uint8), cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)))
    n, lab, stats, _ = cv2.connectedComponentsWithStats(m)
    m = (lab == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))).astype(np.uint8)
    ff = m.copy()
    cv2.floodFill(ff, np.zeros((m.shape[0] + 2, m.shape[1] + 2), np.uint8), (0, 0), 1)
    m = m | (1 - ff)                                                   # no holes
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15)))   # a smooth line where it meets his hair
    alpha = np.minimum(cv2.GaussianBlur(m.astype(np.float32), (0, 0), 0.8), kh[..., 3])
    ys, xs = np.where(alpha > 0.01)
    x0, y0, x1, y1 = xs.min() - 4, ys.min() - 4, xs.max() + 5, ys.max() + 5
    rgba = np.dstack([kh[y0:y1, x0:x1, :3], alpha[y0:y1, x0:x1]])
    f = v.s_timo / SC                                                  # drawing pixels of timo_room -> pixels of his sprite
    small = cv2.resize(rgba, None, fx=f, fy=f, interpolation=cv2.INTER_AREA)
    h, w = small.shape[:2]
    return premul(small), ((x0 - OFF[0]) * f + w / 2, (y0 - OFF[1]) * f + h / 2)   # the picture, and its middle in sprite pixels


HAT, HAT_MID = _hat()


def hat(st, t, T):
    """Laid between him and her, with the same moves as his head: the kit bends his head about his neck (turn `ang`,
    shift hx, hy), then lays his whole figure at his place (lean, drop, breathing, a little sway)."""
    shot = v.shot_at(T)
    if not shot["timo"]:
        return
    drop, stretch, seen = v._timo_drop(T)
    if not seen:
        return
    x0, y0, k = shot["timo"]
    t2 = on_twos(T)
    ang, hx, hy, lean = v.timo_head(T)
    s = v.s_timo
    px, py = duo.T_NECK[0] * s, duo.T_NECK[1] * s
    a = math.radians(ang)
    ux, uy = HAT_MID[0] - px, HAT_MID[1] - py
    d = (px + hx + math.cos(a) * ux - math.sin(a) * uy, py + hy + math.sin(a) * ux + math.cos(a) * uy)   # after the bend of his head
    hh, ww = v.timo.base.shape[:2]
    A = (duo.T_ANCHOR[0] * ww, duo.T_ANCHOR[1] * hh)
    tau = wobble(t2, 7.0, 0.8) * 0.3 - lean * 1.3
    sx, sy = 1 / stretch ** 0.5, stretch * (1 + math.sin(t2 * 1.9) * 0.004)
    qx, qy = k * sx * (d[0] - A[0]), k * sy * (d[1] - A[1])
    b = math.radians(tau)
    X, Y = x0 - lean * 7, y0 + drop + lean * 4
    st.put(HAT, X + math.cos(b) * qx - math.sin(b) * qy, Y + math.sin(b) * qx + math.cos(b) * qy, scale=k, turn=tau + ang, sx=sx, sy=sy,
           anchor=(0.5, 0.5), depth=0.82)


# ---- her sandwich: its own drawing (a redraw of the calm Luna with only the arm added) and its own movement, drawn in
#      front of her: it comes up proud, gives one little wiggle on "sandwich", sinks when she sees the hat, is held low
#      in the closer shot while she looks down at it, and droops a little more at the end.
ARM = duo.arm(v.art, "luna_sandwich", v.s_luna, (1100, 800, 1792, 2400), "luna_calm2")
PIVOT = (1490, 2330)                                    # the middle of the sleeve where it leaves the frame (pixels of her drawing)
NEARER = -45.0                                          # frame pixels toward her: the sleeve meets her coat
PROUD, LOW, GONE = -30.0, 70.0, 900.0                   # how far below its drawn place it is (frame pixels)


def _sandwich_pose(T):
    t = on_twos(T)
    up, wig, see, cut, droop = v.t("L1@we-0.34"), v.t("L1@sandwich"), v.t("L4-0.05"), v.t("cut1"), v.t("T9.end+0.05")
    if T < up - 0.05:
        return None
    if T < cut:
        y = ease(t, up, up + 0.36, GONE, PROUD, "back") + ease(t, see, see + 0.45, 0.0, 55.0, "io")
        w = math.sin((t - wig) * 22) * 3.5 * math.exp(-(t - wig) * 4) if t >= wig else 0.0     # a proud little wiggle
        return y - abs(w) * 3, -1.0 + w + ease(t, see, see + 0.45, 0.0, 5.0, "io")
    return LOW + ease(t, droop, droop + 0.7, 0.0, 22.0, "io"), 5.0 + ease(t, droop, droop + 0.7, 0.0, 4.0, "io")


def sandwich(st, t, T):
    pose = _sandwich_pose(T)
    if pose is None:
        return
    off, ang = pose
    x0, y0, k, lean = v.shot_at(T)["luna"]
    dx, dy, turn, sx, sy = v.luna_move(T)                                # it is hers: it sways and pops with her body
    s = v.s_luna * k
    a = math.radians(turn + lean)
    px, py = (PIVOT[0] - 663) * s, (PIVOT[1] - 1680) * s                 # from the knot of her scarf to the pivot
    x, y = x0 + dx + px * math.cos(a) - py * math.sin(a), y0 + dy + px * math.sin(a) + py * math.cos(a)
    anchor = (PIVOT[0] / duo.L_SIZE[0], PIVOT[1] / duo.L_SIZE[1])
    st.put(ARM, x + NEARER, y + off, scale=k, turn=turn + lean + ang, sx=sx, sy=sy, anchor=anchor, depth=1.0)


# ---- two shots: the two of them, he smaller and lower than usual so that the whole hat is in the picture; then a jump
#      cut eighteen per cent closer for "You don't like people." (the whole hat still in)
v.shot("start", bg=WIDE, luna=(330, 1560, 0.92, -0.6), timo=(650, 1035, 0.74), behind=car_park, mid=hat, front=sandwich)
v.shot("cut1", bg=NEAR, luna=(300, 1640, 1.09, 0.8), timo=(625, 1085, 0.86), behind=car_park, mid=hat, front=sandwich)

# ---- her face
v.faces(("start", None),                  # sunny, proud of the sandwich
        ("T3+0.12", "defend"),            # talked over from behind her: big eyes on us
        ("L4-0.10", "turn"),              # she turns to him: "Who's Gary?" (and sees the hat)
        ("L4.end+0.22", "guilty"),        # a sideways look at him: lost
        ("L6-0.10", "turn"),              # "You don't like people." to him, fond
        ("T7+0.30", "defend"),            # Thanksgiving: back to us, stunned
        ("T7.end+0.22", "read_b"))        # the music has stopped: eyes down at her sandwich; "...Can I come?" and the end
v.tilt(L1=3.0, L2=-2.8, L4=2.6, L6=2.4, L8=-1.6)

# ---- his eyes: on us from the first frame (the hook), on her when he speaks to her, on us again for the last line
v.eyes(("start", "lens"),
       ("L2+0.35", "narrow"),             # "Timo hates it.": he hears his name, and narrows his eyes at us
       ("T3-0.18", None),
       ("look", "lens"))

# ---- the words on screen
v.captions(
    split={"L1": [("American football!", None), ("The tailgate!", "L1@the"), ("We brought a sandwich.", "L1@we")],
           "L2": [("Timo hates it.", None), ("He says it’s just a car park—", "L2@he-0.15")]},
    hold={"T7": ("He’s invited me\n", "for Thanksgiving.", "T7@for")},     # the second half arrives as he says it
    place={"L2": (590, 1432), "T7": (600, 1430)},   # two-line captions: clear of her chin and inside the 4:5 version
    text={"L8": "…Can I come?"},
    on={"T3": "T3+0.20", "L4": "L4+0.12"},  # he talks over her: her words stay up a moment under his voice, then his come
    off={"L2": "T3+0.18", "T3": "L4+0.10", "T9": "plate-0.06"})
v.plate(x=282, y=650, size=0.55)          # the hat fills the top right: the plate goes left of his face, over the far party, above her head

if __name__ == "__main__":
    v.main()
