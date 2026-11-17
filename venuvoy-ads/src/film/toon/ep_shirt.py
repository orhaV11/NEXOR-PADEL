"""The picture of the short "They're Reloading" (vlog 13, "shirt"): one shot in the stand of an indoor basketball arena.

    python3 src/film/toon/ep_shirt.py --sheet        a contact sheet of the whole film (work/shirt/sheet.jpg)
    python3 src/film/toon/ep_shirt.py --stills 0.1 "land-0.1" "land+0.1" "L2+0.5" "L4+0.2" "plate+1.0"
    python3 src/film/toon/ep_shirt.py                the film (ads/21-vlog/venuvoy-vlog-13-shirt-en_...mp4)

Run ep_shirt_sound.py first: every time below is read from its timeline.

She waves at the T-shirt cannon down on the court (behind the phone) and begs for a shirt; the cannon fires, a rolled
shirt drops onto Timo's flat-top as her line ends and stays there. He does not move; his eyes come to us. She never
sees it. Two things are this film's own, drawn by hooks: the shirt (between him and her) and her waving hand (in front).
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

# indoors: her calm hair, him without the blade of grass. The words the joke rests on get their mouth shapes by hand.
v = Vlog("shirt", "ads/21-vlog/venuvoy-vlog-13-shirt", luna="calm", timo="timo_clean",
         said={"over": "O C", "here": "C E", "never": "C E C", "get": "C E C", "free": "C E", "shirt": "C E C", "eleven": "E C E C", "games": "C A C",
               "once": "O A C", "luna": "C O C A", "not": "C O C", "now": "C A O", "theyre": "C E", "reloading": "C E C O C E C"})

# drawing, a point in it (the warm lamps over the stand, top left), where that lands in the frame, size: the lamps across
# the top left, the crowd behind their heads, the empty seat backs behind her shoulders.
BG = ("bg_stand_1", (400, 500), (250, 300), 0.90)
LUNA_AT = (362, 1565, 0.92, -0.6)
TIMO_AT = (872, 1040, 0.96)


# ---- the shirt: a rolled T-shirt with a band round it, its own drawing on flat magenta, cut out (the redraw left a
#      smear of the reference along its bottom: only the roll itself, the largest piece above it, is kept)
def _roll():
    rgb = read(v.art / "roll_1")[..., :3]
    k = key_out(rgb)
    solid = (k[..., 3] > 0.5).astype(np.uint8)
    n, lab, st, _ = cv2.connectedComponentsWithStats(solid)
    i = 1 + int(np.argmax([st[j, cv2.CC_STAT_AREA] if st[j, cv2.CC_STAT_TOP] < 1300 else 0 for j in range(1, n)]))
    keep = cv2.dilate((lab == i).astype(np.uint8), np.ones((5, 5), np.uint8)).astype(np.float32)
    x0, y0, w, h = st[i, :4]
    k[..., 3] *= keep
    return premul(np.ascontiguousarray(k[y0 - 6:y0 + h + 6, x0 - 6:x0 + w + 6]))


ROLL = _roll()
ROLL_LONG = 262.0                                      # frame pixels from end to end at his size 1.0 (a little longer than his head is wide)
HAIR = (640, 194)                                      # the middle of the top of his flat-top (pixels of his drawing): the roll's underside sits here
ROLL_TILT = -3.5                                       # it lies a little crooked
LAND = "land"                                          # it lands as her line ends (the stretch "land" in the sound plan)
FALL = 0.24                                            # it drops in from the top edge in this long (six pictures)


def timo_point(T, p):
    """Where a point of Timo's drawing is in the frame now, with his head's own turn (the kit's timo_xy leaves the
    turn out: the roll would slide on his hair while he speaks). Returns (x, y, size, turn in degrees)."""
    x0, y0, k = v.shot_at(T)["timo"]
    drop, stretch, seen = v._timo_drop(T)
    t = on_twos(T)
    ang, hx, hy, lean = v.timo_head(T)
    s = v.s_timo
    qx, qy = p[0] * s, p[1] * s
    nx, ny = duo.T_NECK[0] * s, duo.T_NECK[1] * s
    a = math.radians(ang)
    rx, ry = qx - nx, qy - ny
    qx, qy = nx + math.cos(a) * rx - math.sin(a) * ry + hx, ny + math.sin(a) * rx + math.cos(a) * ry + hy
    ax, ay = duo.T_ANCHOR[0] * duo.T_SIZE[0] * s, duo.T_ANCHOR[1] * duo.T_SIZE[1] * s
    turn = wobble(t, 7.0, 0.8) * 0.3 - lean * 1.3
    sx, sy = k / stretch ** 0.5, k * stretch * (1 + math.sin(t * 1.9) * 0.004)
    dx, dy = (qx - ax) * sx, (qy - ay) * sy
    b = math.radians(turn)
    return x0 - lean * 7 + math.cos(b) * dx - math.sin(b) * dy, y0 + drop + lean * 4 + math.sin(b) * dx + math.cos(b) * dy, k, ang + turn


def shirt(st, t, T):
    land = v.t(LAND)
    if T < land - FALL:
        return
    x, y, k, turn = timo_point(T, HAIR)
    scale = ROLL_LONG * k / ROLL.shape[1]
    if T < land:                                       # falling: from above the top edge, faster and faster, turning over a little;
        p = (T - (land - FALL)) / FALL                 # it is going away from the lens, so it shrinks a touch as it falls
        p2 = p * p
        y_top = -ROLL.shape[0] * scale * 0.8
        st.put(ROLL, x + 34 * (1 - p), y_top + (y - y_top) * p2, scale=scale * (1.18 - 0.18 * p), turn=ROLL_TILT + turn + 32 * (1 - p),
               anchor=(0.5, 0.92), depth=0.82)
        return
    q = T - land                                       # landed: it squashes once on his hair and settles, and stays
    squash = math.exp(-q * 16) * math.cos(q * 34) * 0.20
    st.put(ROLL, x, y, scale=scale, turn=ROLL_TILT + turn, sx=1 + squash * 0.6, sy=1 - squash, anchor=(0.5, 0.92), depth=0.82)


# ---- her waving hand: the open hand of the calm Luna (drawn for "Front of the Queue": a redraw of luna_calm2 with only
#      the arm added). Its sleeve meets her coat at the bottom edge of the frame, so it cannot be lifted without a gap:
#      it waves by turning about that point. The redraw left a sliver of her coat's edge beside the sleeve (it would
#      travel with the hand): taken out; and the sleeve's inner edge, cut along her coat, gets its own outline, so that
#      when the forearm swings away from her a drawn edge shows, not a cut.
PIVOT = (1282, 2286)                                   # where the sleeve meets her coat (pixels of her drawing)


def _arm():
    a = duo.arm(v.art, "luna_shrug_calm", 1.0, (1000, 1340, 1792, 2400), "luna_calm2")
    a[1735:1885, :1196] = 0                            # the sliver
    alpha = a[..., 3]
    ink = np.zeros_like(alpha)
    for y in range(1862, PIVOT[1] + 4):                # the inner edge, row by row: a line as heavy as the drawing's own
        xs = np.where(alpha[y] > 0.5)[0]
        if len(xs):
            ink[y, xs[0]:xs[0] + 11] = 1.0
    ink = cv2.GaussianBlur(ink, (0, 0), 1.0) * (alpha > 0.02)
    a[..., :3] = a[..., :3] * (1 - ink[..., None]) + np.array([0.09, 0.07, 0.08], np.float32) * ink[..., None] * alpha[..., None]
    return cv2.resize(a, None, fx=v.s_luna, fy=v.s_luna, interpolation=cv2.INTER_AREA)     # premultiplied, as duo.arm gives it


ARM = _arm()
GONE = 820.0                                           # frame pixels below its drawn place: out of the frame


def _wave(T):
    """(pixels below its drawn place, turn in degrees) of her arm now. It turns about the sleeve's foot: to her
    (negative) and back, never far away from her coat. On twos, like her."""
    t = on_twos(T)
    down0, down1 = v.t("L2-0.30"), v.t("L2+0.12")      # the waving stops (she gives up on this round), and the hand sinks away while she starts to moan
    up = v.t("L4-0.14")                                # "Not now.": it shoots up again
    hard = max(math.exp(-max(0.0, t - v.t("cannon")) * 1.6) * (t >= v.t("cannon")), math.exp(-max(0.0, t - v.t("again")) * 1.6) * (t >= v.t("again")))
    amp = 5.5 + 3.5 * hard                             # harder when the cannon goes
    rate = 2.4 + 0.9 * hard
    if T < down1:
        calm = ease(t, v.t("land+0.30"), down0, 1.0, 0.0, "io")      # the waving dies away after the landing she has not seen
        swing = math.sin(2 * math.pi * rate * t) * amp * calm
        y = ease(T, down0 + 0.05, down1, 0.0, GONE, "in")             # then the hand goes down out of the frame
        return y, -5.0 + swing
    if T < up - 0.05:
        return None
    swing = math.sin(2 * math.pi * rate * (t - up)) * amp * min(1.0, (t - up) / 0.25)
    return ease(T, up - 0.05, up + 0.20, GONE, 0.0, "out"), -5.0 + swing


def hand(st, t, T):
    pose = _wave(T)
    if pose is None:
        return
    off, ang = pose
    x0, y0, k, lean = LUNA_AT
    dx, dy, turn, sx, sy = v.luna_move(T)              # it is hers: it sways and pops with her body
    s = v.s_luna * k
    a = math.radians(turn + lean)
    px, py = (PIVOT[0] - 663) * s, (PIVOT[1] - 1680) * s
    x, y = x0 + dx + px * math.cos(a) - py * math.sin(a), y0 + dy + px * math.sin(a) + py * math.cos(a)
    st.put(ARM, x, y + off, scale=k, turn=turn + lean + ang, sx=sx, sy=sy, anchor=(PIVOT[0] / duo.L_SIZE[0], PIVOT[1] / duo.L_SIZE[1]), depth=1.0)


# ---- one shot: the two of them in the stand; the top of the frame free over his head for the shirt to drop through
v.shot("start", bg=BG, luna=LUNA_AT, timo=TIMO_AT, mid=shirt, front=hand)

# ---- her face: she never looks at him (she would see the shirt)
v.faces(("start", None),                  # sunny, shouting over the crowd to the cannon behind the phone
        ("L2-0.10", "defend"),            # to us, sorry for herself
        ("L2@never-0.04", "down"),        # "Never once.": eyes down
        ("L2.end+0.10", None),            # her eyes go back past the lens to the court
        ("L4-0.10", "defend"),            # "Not now.": urgent, big eyes on the court
        ("L4.end+0.12", None))            # and eager again, waving, to the end
v.tilt(L1=3.0, L2=-2.6, L4=2.4)
v.blinks(luna=[1.85, "L2+0.45", "T5+0.40", "plate+0.85"],
         timo=[1.45, "L4+0.30", "plate+1.10"])          # never during his looks; a slow one while she says "Not now."

# ---- his eyes: on her; on us the moment the shirt has landed; on her for "Luna."; on us for the last word, and stay
v.eyes(("start", None),
       ("land+0.14", "lens"),
       ("T3-0.14", None),
       ("T5-0.24", "lens"))

# ---- the words on screen: under her chin, left of her waving hand
CAP = (375, 1440)                         # the hand reaches x 663 when it swings toward her: every caption ends left of it
v.captions(
    place=dict({i: CAP for i in ("L1", "L2", "T3", "L4")}, T5=(352, 1440)),
    split={"L1": [("Over here!", None), ("I never get\nthe free shirt!", "L1@i")],
           "L2": [("Eleven games.", None), ("Never once.", "L2@never")]},
    text={"T5": "They’re reloading."},
    on={"L1": "L1-0.12", "T3": "T3-0.10", "T5": "T5-0.06"},
    off={"L1": "land+0.40", "L2": "T3-0.12", "L4": "T5-0.08", "T5": "plate-0.05"})   # one caption at a time: hers go before his come up early        # the last word stays up through his look
v.plate(x=345, y=300, size=0.68)          # beside his head, above hers: he is standing at the end with the shirt on

if __name__ == "__main__":
    v.main()
