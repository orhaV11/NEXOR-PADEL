"""The picture of the sketch "Twenty Years" (vlog 05, "glove"): three shots in the top rows of a ballpark on a still,
sunny afternoon.

    python3 src/film/toon/ep_glove.py --sheet        a contact sheet of the whole film (work/glove/sheet.jpg)
    python3 src/film/toon/ep_glove.py --stills 0.1 "land-0.1" "land+0.1" "cut2+0.3" "plate+1.0"
    python3 src/film/toon/ep_glove.py                the film (ads/21-vlog/venuvoy-vlog-05-glove-en_...mp4)

Run ep_glove_sound.py first: every time below is read from its timeline.

Timo has brought his glove, as he has for twenty years: he stands behind her with it held open at his chest, eyes
shut, and lectures her on readiness; on "ready" a ball drops out of the sky into it. He does not look down. Two things
are this film's own, drawn between him and her by one hook: his forearm with the open glove (its own drawing on flat
magenta, not a redraw of him: it sits at his chest from the first frame and moves with him), and the ball (drawn
here in code: a white circle with a dark outline and two red seams).
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import key_out, on_twos, premul, read, wobble  # noqa: E402
from vlog import Vlog  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent.parent.parent

# ---- his base drawing for this film, made here from timo_clean (nothing bought): his near forearm and the hand in his
#      pocket painted out below the elbow (the background left of his torso, his jacket over it, the torso's edge and the
#      pocket's slit drawn on again), so that the forearm holding the glove can rise from that elbow instead.
ELBOW = (175, 2160)                                    # the middle of the bend of his near arm (pixels of his drawing)
TORSO = ((362, 2122), (351, 2470))                     # his torso's left edge where the forearm crossed it
INK = np.array([0.09, 0.07, 0.08], np.float32)         # the outline colour of the drawings


def make_base(name="timo_glove"):
    art = duo.Art("glove")
    if art.has(name):
        return
    rgb = read(art / "timo_clean")[..., :3].copy()
    teal = np.median(rgb[2000:2100, 420:560].reshape(-1, 3), axis=0)
    H_, W_ = rgb.shape[:2]
    yy, xx = np.mgrid[0:H_, 0:W_]
    (x0, y0), (x1, y1) = TORSO
    edge = x0 + (x1 - x0) * np.clip((yy - y0) / (y1 - y0), 0, 1)        # the torso's edge, row by row
    region = (yy > ELBOW[1]) & (yy < 2500) & (xx < 520)                  # below the elbow: the forearm, the cuff and the hand
    over = np.zeros((H_, W_), np.uint8)                                  # the part of it that lay over his torso
    cv2.fillPoly(over, [np.array([(362, 2118), (440, 2170), (492, 2215), (510, 2290), (505, 2445), (450, 2492), (352, 2480)], np.int32)], 1)
    left = region & (xx < edge - 3)
    rgb[left] = (1.0, 0.0, 1.0)                                          # background (keyed out)
    rgb[(over > 0) & ~left] = teal                                       # his jacket
    cv2.line(rgb, (362, 2100), (351, 2482), tuple(float(c) for c in INK), 10, cv2.LINE_AA)       # the torso's edge
    slit = np.array([(500, 2196), (494, 2262), (472, 2350), (440, 2425), (408, 2478)], np.int32)
    cv2.polylines(rgb, [slit], False, tuple(float(c) for c in INK), 8, cv2.LINE_AA)              # the pocket's slit, empty now
    out = ROOT / "work" / "glove" / "art" / f"{name}.png"
    cv2.imwrite(str(out), (np.clip(rgb[..., ::-1], 0, 1) * 255 + 0.5).astype(np.uint8))


make_base()

# outdoors on a still day: her calm hair; him without the blade of grass and with his near hand out of his pocket.
# The words the joke rests on get their mouths by hand.
v = Vlog("glove", "ads/21-vlog/venuvoy-vlog-05-glove", luna="calm", timo="timo_glove",
         said={"brought": "M O C", "glove": "C O C", "twenty": "C E C E", "years": "C E C", "caught": "C O C", "catching": "C A C E C",
               "about": "A O C", "being": "M E C", "ready": "C E C E", "chase": "C E C", "ball": "M O C", "timo": "C E M O", "finished": "C E C E C",
               "your": "O A", "there": "C A", "isnt": "C E C", "ive": "A C", "won": "O A C", "baseball": "M E C M O C", "thrilled": "C A C", "hes": "C", "its": "C",
               "ever": "C A"})          # her words carry no bared-teeth E: on her sunny face it reads as a grimace

# ---- the shots: the two of them; seventeen per cent closer (a jump cut); seventeen per cent closer again
BG_A = ("bg_park_1", (399, 1486), (300, 1000), 0.92)    # drawing, a point in it (the infield), where that lands in the frame, size
BG_B = ("bg_park_1", (399, 1486), (270, 1050), 1.08)
BG_C = ("bg_park_1", (399, 1486), (240, 1100), 1.26)
LUNA_A, TIMO_A = (362, 1545, 0.92, -0.6), (872, 1010, 0.92)
LUNA_B, TIMO_B = (352, 1525, 1.08, 0.6), (882, 1060, 1.08)
LUNA_C, TIMO_C = (342, 1505, 1.26, -0.4), (895, 1110, 1.26)


# ---- his forearm with the glove (its own drawing on flat magenta): the sleeve comes in from the drawing's bottom left
#      corner at 38 degrees. Stood upright, cut where it is as long as his forearm, and closed with a rounded elbow made
#      from its own last row (outlines and stripe drawn in). Each frame it is laid from his elbow to his chest; the
#      pocket of the glove (where the ball lands) is its anchor.
ARM_TURN = 38.3                                        # the sleeve's slant in the drawing (degrees from vertical)
CHEST = (610, 1600)                                    # where the pocket sits on him (pixels of his drawing; his chest is (614, 1514), the scarf ends at 1340)
GLOVE_PER_SIZE = 0.24                                  # the drawing's scale for each unit of Timo's size: the glove nearly a head wide
CUFF = 715                                             # the bottom of the cuff, below the pocket (pixels of the upright drawing)
SLIM = 0.78                                            # the sleeve below the cuff, as wide as his own arm


def _pocket():
    """The middle of the dark brown hollow of the glove (pixels of the drawing)."""
    rgb = read(v.art / "glove_1")[..., :3]
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    dark = (r > g + 0.06) & (r > b + 0.08) & (r < 0.42) & (r > 0.22)
    dark[:, :560] = False                              # not the laces of the thumb on the left
    dark[1150:] = False
    n, lab, st, cen = cv2.connectedComponentsWithStats(dark.astype(np.uint8))
    i = 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA]))
    return float(cen[i][0]), float(cen[i][1])


def _arm():
    """(premultiplied picture, anchor = the pocket as fractions, the vector pocket -> elbow in its pixels)."""
    rgb = read(v.art / "glove_1")[..., :3]
    k = key_out(rgb)
    h, w = k.shape[:2]
    pad = 900
    big = np.zeros((h + 2 * pad, w + 2 * pad, 4), np.float32)
    big[pad:pad + h, pad:pad + w] = k
    inside = np.zeros(big.shape[:2], np.float32)
    inside[pad:pad + h, pad:pad + w] = 1
    c = (pad + 550.0, pad + 1975.0)                    # the middle of the sleeve
    M = cv2.getRotationMatrix2D(c, ARM_TURN, 1.0)      # stood upright (turned against the clock): the sleeve now points straight down
    big = cv2.warpAffine(big, M, big.shape[1::-1], flags=cv2.INTER_LINEAR)
    inside = cv2.warpAffine(inside, M, inside.shape[1::-1], flags=cv2.INTER_NEAREST)
    px, py = M @ np.array([pad + POCKET[0], pad + POCKET[1], 1.0])
    a = big[..., 3] > 0.5
    whole = None
    for y in range(int(c[1]), big.shape[0]):           # the lowest row where the sleeve is whole, outlines and all...
        xs = np.where(a[y])[0]
        if len(xs) < 50 or inside[y, max(0, xs.min() - 14):xs.max() + 15].min() < 1:
            break
        whole = y
    whole -= 6
    big = np.concatenate([big[:whole + 1], np.repeat(big[whole:whole + 1], 900, axis=0)], axis=0)   # ...run on downward
    cuff = int(py + CUFF)                              # below its cuff the sleeve narrows to his own arm's width
    a = big[..., 3] > 0.5
    for y in range(cuff, big.shape[0]):
        xs = np.where(a[y])[0]
        if len(xs) < 20:
            break
        xl, xr = xs.min() - 3, xs.max() + 4
        f = 1.0 - (1.0 - SLIM) * min(1.0, (y - cuff) / 90.0)
        n = max(2, int(round((xr - xl) * f)))
        squeezed = cv2.resize(big[y, xl:xr][None], (n, 1), interpolation=cv2.INTER_AREA)[0]
        big[y, xl:xr] = 0
        x0 = int(round((xl + xr) / 2 - n / 2))
        big[y, x0:x0 + n] = squeezed
    a = big[..., 3] > 0.5
    want = math.hypot(CHEST[0] - ELBOW[0], CHEST[1] - ELBOW[1]) * v.s_timo / GLOVE_PER_SIZE   # pocket to elbow, in these pixels
    cut = int(py + want)
    for _ in range(3):                                 # the elbow's middle is on the sleeve's axis in that row
        xs = np.where(a[cut])[0]
        ax = (xs.min() + xs.max()) / 2
        cut = int(py + math.sqrt(want ** 2 - (ax - px) ** 2))
    xs = np.where(a[cut])[0]
    xl, xr = xs.min() - 2, xs.max() + 2
    ax, r = (xl + xr) / 2, (xr - xl) / 2
    body = big[:cut + 1].copy()
    row = big[cut, xl:xr + 1]
    cap = np.zeros((int(r) + 2, body.shape[1], 4), np.float32)
    for dy in range(1, int(r) + 2):                    # the elbow: the last row of the sleeve, narrower and narrower
        half = math.sqrt(max(0.0, r * r - dy * dy))
        if half < 1:
            break
        n = int(2 * half)
        squeezed = cv2.resize(row[None], (n, 1), interpolation=cv2.INTER_AREA)[0]
        x0 = int(round(ax - n / 2))
        cap[dy - 1, x0:x0 + n] = squeezed
    ring = np.zeros(cap.shape[:2], np.float32)         # its outline, as heavy as the sleeve's
    cv2.ellipse(ring, (int(ax), -1), (int(r) - 5, int(r) - 5), 0, 0, 180, 1.0, 11, cv2.LINE_AA)
    cap[..., :3] = cap[..., :3] * (1 - ring[..., None]) + INK * ring[..., None]
    cap[..., 3] = np.maximum(cap[..., 3], ring * (cap[..., 3] > 0.02))
    body = np.concatenate([body, cap], axis=0)
    ys, xs = np.where(body[..., 3] > 0.01)
    x0, x1, y0 = xs.min() - 4, xs.max() + 5, ys.min() - 4
    body = np.ascontiguousarray(body[y0:, x0:x1])
    return premul(body), ((px - x0) / body.shape[1], (py - y0) / body.shape[0]), (ax - px, cut - py)


POCKET = _pocket()
ARM, ARM_ANCHOR, ARM_VEC = _arm()


def body_point(T, p):
    """Where a point of Timo's body (below his head) is in the frame now, with his body's own sway and lean, as the kit
    lays him. Returns (x, y, his size in this shot, whether he is seen)."""
    x0, y0, k = v.shot_at(T)["timo"]
    drop, stretch, seen = v._timo_drop(T)
    t = on_twos(T)
    lean = v.timo_head(T)[3]
    b = math.radians(wobble(t, 7.0, 0.8) * 0.3 - lean * 1.3)
    s = v.s_timo
    dx = (p[0] - duo.T_ANCHOR[0] * duo.T_SIZE[0]) * s * k / stretch ** 0.5
    dy = (p[1] - duo.T_ANCHOR[1] * duo.T_SIZE[1]) * s * k * stretch * (1 + math.sin(t * 1.9) * 0.004)
    return x0 - lean * 7 + math.cos(b) * dx - math.sin(b) * dy, y0 + drop + lean * 4 + math.sin(b) * dx + math.cos(b) * dy, k, seen


# ---- the ball: drawn here. A white ball with a dark outline, a flat grey shadow on its lower right and two red seams.
def _ball(d=240):
    s = 4                                              # drawn four times larger and shrunk: smooth edges
    D = d * s
    img = np.zeros((D, D, 4), np.float32)
    c, R = D / 2, D / 2 - 3 * s
    cv2.circle(img, (int(c), int(c)), int(R), (0.97, 0.95, 0.90, 1.0), -1, cv2.LINE_AA)
    shade = np.zeros((D, D), np.float32)
    cv2.circle(shade, (int(c + R * 0.22), int(c + R * 0.24)), int(R * 1.0), 1.0, -1, cv2.LINE_AA)
    inner = np.zeros((D, D), np.float32)
    cv2.circle(inner, (int(c - R * 0.10), int(c - R * 0.12)), int(R * 0.93), 1.0, -1, cv2.LINE_AA)
    crescent = shade * (1 - inner) * (img[..., 3] > 0)
    img[..., :3] = img[..., :3] * (1 - crescent[..., None]) + np.array([0.80, 0.79, 0.76], np.float32) * crescent[..., None]
    red = (0.80, 0.12, 0.12, 1.0)
    for side in (-1, 1):                               # the two seams: curves that bow toward the middle, with short stitches across
        pts = []
        for k in range(41):
            u = -0.78 + 1.56 * k / 40
            x = c + side * (R * 0.80 - R * 0.30 * math.cos(u * math.pi / 2.2))
            y = c + R * u
            pts.append((x, y))
        pts = np.array(pts)
        keep = (pts[:, 0] - c) ** 2 + (pts[:, 1] - c) ** 2 < (R * 0.93) ** 2
        cv2.polylines(img, [pts[keep].astype(np.int32)], False, red, int(2.2 * s), cv2.LINE_AA)
        for k in range(3, 40, 5):
            if not keep[k]:
                continue
            x, y = pts[k]
            dx, dy = pts[k + 1] - pts[k - 1]
            nx, ny = -dy, dx
            n = math.hypot(nx, ny) + 1e-9
            L = R * 0.075
            cv2.line(img, (int(x - nx / n * L), int(y - ny / n * L)), (int(x + nx / n * L), int(y + ny / n * L)), red, int(1.8 * s), cv2.LINE_AA)
    cv2.circle(img, (int(c), int(c)), int(R), (0.09, 0.07, 0.08, 1.0), int(5.5 * s), cv2.LINE_AA)   # the outline, as heavy as the drawings'
    img = cv2.resize(img, (d, d), interpolation=cv2.INTER_AREA)
    return premul(img)


BALL = _ball()
BALL_PER_SIZE = 0.30                                   # the ball is this much of the glove's width
FALL = 0.25                                            # it drops in from the top edge in a quarter of a second
LAND = "land"                                          # it lands on "ready" (the effect "land" in the sound plan)


def _streaks(h=230, w=60):
    """Two short speed lines, grey, for the falling ball (drawn above it)."""
    img = np.zeros((h * 4, w * 4, 4), np.float32)
    for x, l in ((w * 4 * 0.30, 0.95), (w * 4 * 0.70, 0.70)):
        cv2.line(img, (int(x), int(h * 4 * (1 - l))), (int(x), h * 4 - 8), (0.30, 0.30, 0.34, 0.85), 11, cv2.LINE_AA)
    return premul(cv2.resize(img, (w, h), interpolation=cv2.INTER_AREA))


STREAKS = _streaks()


def _dip(q, A):
    """How far the glove is pushed down q seconds after the ball lands: down at once, a little back past, still."""
    return 0.0 if q < 0 else A * math.exp(-q * 8.0) * math.sin(q * 18.0)


def glove(st, t, T):
    px, py, k, seen = body_point(T, CHEST)
    if not seen:
        return
    ex, ey, _, _ = body_point(T, ELBOW)
    land = v.t(LAND)
    dip = _dip(on_twos(T) - land, 60.0 * k)
    py += dip                                                      # the glove gives under the ball; the elbow stays: the forearm pivots
    wx, wy = ex - px, ey - py
    scale = math.hypot(wx, wy) / math.hypot(*ARM_VEC)
    turn = math.degrees(math.atan2(wy, wx) - math.atan2(ARM_VEC[1], ARM_VEC[0]))
    st.put(ARM, px, py, scale=scale, turn=turn, anchor=ARM_ANCHOR, depth=0.82)
    bd = BALL_PER_SIZE * 1347 * scale / BALL.shape[1]
    if T < land - FALL:
        return
    bx, by = px - 6 * k, py + 10 * k                               # it rests a little low in the pocket
    if T < land:                                                   # falling: from above the top edge, faster and faster, drawn long
        p = (T - (land - FALL)) / FALL
        y_top = -BALL.shape[0] * bd
        yy = y_top + (by - y_top) * p ** 1.6
        xx = bx - 260 * k * (1 - p ** 4)                           # down the sky between their heads (clear of his face), into the glove at the last moment
        st.put(STREAKS, xx, yy - BALL.shape[0] * bd * 0.55, scale=bd * 1.4, anchor=(0.5, 1.0), depth=0.82)
        st.put(BALL, xx, yy, scale=bd, sx=0.86, sy=1.28, depth=0.82)
        return
    q = T - land                                                   # landed: it squashes once into the leather and stays
    squash = math.exp(-q * 18) * math.cos(q * 30) * 0.12
    st.put(BALL, bx, by, scale=bd, sx=1 + squash, sy=1 - squash, turn=20, anchor=(0.5, 0.5), depth=0.82)


v.shot("start", bg=BG_A, luna=LUNA_A, timo=TIMO_A, mid=glove)
v.shot("cut1", bg=BG_B, luna=LUNA_B, timo=TIMO_B, mid=glove)
v.shot("cut2", bg=BG_C, luna=LUNA_C, timo=TIMO_C, mid=glove)

# ---- her face
v.faces(("start", None),                  # to us, amused, fond
        ("L3-0.12", "turn"),              # "Ever caught one?": to him, really asking
        ("cut1", None),                   # to us, half listening to the lecture
        ("land+0.10", "down"),            # oh: her eyes go to the glove
        ("L5-0.10", "turn"),              # "Timo."
        ("L5.end+0.16", "defend"),        # "I'm not finished.": big eyes at us
        ("L7-0.08", "guilty"),            # "Your glove.": small, a sideways look at it
        ("L9-0.12", "turn"),              # "It's in there.": whispered, to him
        ("L9.end+0.20", "defend"),        # "I've won baseball.": big eyes at us
        ("L11-0.12", None))               # "He's thrilled.": sunny again, to the end
v.tilt(L1=3.0, L3=2.4, L5=2.2, L7=-2.2, L9=2.0, L11=-3.0)

# ---- his eyes: shut (a man waiting for rain); open on her; one brow down at the question; shut through the lecture;
#      open on us after the cut, and there to the end. He never looks down.
v.eyes(("start", "shut"),
       ("T2-0.18", None),
       ("L3.end+0.08", "narrow"),
       ("cut1", "shut"),
       ("cut2+0.15", "lens"))
v.blinks(luna=[1.2, "T2+0.5", "T4+0.9", "T4+3.3", "T6+0.6", "T8+0.3", "T10+0.5", "plate+0.9"],
         timo=["L3+0.35", "L9+0.45", "L11+0.40", "plate+1.30"])

# ---- the words on screen
v.captions(
    split={"T4": [("It’s not about catching.", None), ("It’s about being ready.", "T4+1.18"), ("You don’t chase the ball—", "T4@you")]},     # (said: "…the ball, the ba—"; the full words cannot be read in 1.3 s)
    text={"L3": "Ever caught one?"})
v.plate(x=345, y=290, size=0.68)          # beside his head, above hers: he is standing at the end

if __name__ == "__main__":
    v.main()
