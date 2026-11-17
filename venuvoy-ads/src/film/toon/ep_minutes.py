"""The picture of the short "Two Minutes Left" (vlog 12, "minutes"): two takes of one selfie in the stand at an
American football game, hours apart and joined by a whip: gold afternoon, then night under the lights.

    python3 src/film/toon/ep_minutes.py --sheet        a contact sheet of the whole film (work/minutes/sheet.jpg)
    python3 src/film/toon/ep_minutes.py --stills 0.1 "L1@then+0.2" "T2+0.6" "cut1-0.05" "cut1+0.2" "L3+0.4" "T4+0.5" "whistle+0.3" "plate+0.8"
    python3 src/film/toon/ep_minutes.py                the film (ads/21-vlog/venuvoy-vlog-12-minutes-en_...mp4)

Run ep_minutes_sound.py first: every time below is read from its timeline.

Two things here are this film's own, because the kit has no statement for them:
_paintings()  ONE painting was bought (bg_stand_1, the stand in late-afternoon gold). From it, by code: bg_stand_1x
              (the same with the yard numbers on the field painted out: nothing readable in a background; and
              without the fourth light mast, which stood exactly where his head is) and bg_stand_night (the same
              place at night: a deep blue sky, the stands cold and dark, the field green under the lights, the
              banks of lamps switched on).
gold(), cold()  the light on the two of them: warm by day, cold and a little dark at night (grade= of each shot).
_tired()      her face at night (luna_tired): the rig's eyes-down face with heavy upper lids drawn on by code (no
              drawing bought); it stands in for "read_b". And a small round mouth "o" (from the rig's "oh" face) for
              her tired words: her own O is a big "oh!" and her C an open smile.

An open stand: her windy look all through (his scarf flaps too). Two takes joined by a whip: by day the two of them
as usual, his eyes shut in the first frame (he knows this game) and open on her for his warning; at night she is
closer to her phone and worn out, and his eyes are shut from the first frame of the take to the last.
"""
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from vlog import ROOT, Vlog  # noqa: E402


def _tired(src="luna_read3b", name="luna_tired", mid=0.54, side=0.46, lid_tone=0.95):
    """Her face at night, made by code from the rig's luna_read3b (eyes down, mouth a small closed line): heavy
    upper lids drawn over both eyes. Each eye's white is found, the part of the eye above a gentle arc (`mid` of the
    eye's height in the middle, `side` at its ends) is filled with her skin a shade darker (the lid), and a lid line
    in the ink of her outlines and at their weight runs across it, clipped to the eye's own outline. Nothing else of
    the drawing changes, so it sits on luna_w1 like every other face. Made once (--paint makes it again)."""
    out = ROOT / "work" / "minutes" / "art" / f"{name}.png"
    art = duo.Art("minutes")
    if art.has(name) and "--paint" not in sys.argv:
        return name
    X = cv2.imread(str(art / src), cv2.IMREAD_COLOR)
    H, W = X.shape[:2]
    x0, y0, x1, y1 = 380, 860, 950, 1110                         # round her eyes (duo.L_EYES and a margin)
    white = np.zeros((H, W), np.uint8)
    white[y0:y1, x0:x1] = X[y0:y1, x0:x1].min(axis=2) > 200
    n, cc, st, _ = cv2.connectedComponentsWithStats(white)
    skin = np.median(X[1180:1200, 520:560].reshape(-1, 3), axis=0)                # her cheek beside the mouth
    ink = np.median(X[y0:y1, x0:x1][X[y0:y1, x0:x1].max(axis=2) < 80], axis=0)      # the ink of her outlines
    N = X.astype(np.float32)
    yy, xx = np.mgrid[0:H, 0:W]
    for i in range(1, n):
        if st[i, cv2.CC_STAT_AREA] < 5000:
            continue
        cs, _ = cv2.findContours((cc == i).astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        eye = np.zeros((H, W), np.uint8)
        cv2.drawContours(eye, cs, -1, 1, -1)                     # the white with the pupil in it
        eye = cv2.morphologyEx(eye, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))
        ex, ey, ew, eh = st[i, :4]
        xc, thick = ex + ew / 2, 9                               # her eye outlines are 9 px
        lid = lambda x: ey + eh * (side + (mid - side) * (1 - np.clip((x - xc) / (ew / 2), -1, 1) ** 2))   # noqa: E731
        cover = cv2.GaussianBlur(((yy < lid(xx)) & (cv2.dilate(eye, np.ones((5, 5), np.uint8)) > 0)).astype(np.float32), (0, 0), 0.8) \
            * cv2.GaussianBlur(eye.astype(np.float32), (0, 0), 0.8)
        N = N * (1 - cover[..., None]) + skin * lid_tone * cover[..., None]
        xs = np.arange(ex - thick, ex + ew + thick + 1)
        line = np.zeros((H, W), np.uint8)
        cv2.polylines(line, [np.round(np.stack([xs, lid(xs)], 1) * 16).astype(np.int32)], False, 255, thick, cv2.LINE_AA, 4)
        a = line.astype(np.float32) / 255 * cv2.GaussianBlur(cv2.dilate(eye, np.ones((thick + 1, thick + 1), np.uint8)).astype(np.float32), (0, 0), 1.0)
        N = N * (1 - a[..., None]) + ink * a[..., None]
    out.parent.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(out), np.clip(N + 0.5, 0, 255).astype(np.uint8))
    return name


# her face at night is the tired one: it takes the place of "read_b" (eyes down, mouth shut), so that her head still
# bows to it as the kit bows it for read_b
v = Vlog("minutes", "ads/21-vlog/venuvoy-vlog-12-minutes", luna="wind", timo="timo_clean", extra_faces={"read_b": _tired()})
v.luna.add("3mouth", "o", v.art / "luna_down", duo.L_MOUTH)     # a small round mouth (from the rig's "oh" face): her tired words barely open it

# Four short lines: every mouth is set by hand, twelve steps a second from the start of each line, against the word
# times of the sound file (- is the shut mouth). Left to itself the kit, which opens a mouth by loudness, gave his
# flat punchline one small shape from end to end, and her hollow question a smile.
MOUTHS = {
    #        .  T  wo .  m  i  nu te s  l  e  ft .  -  -  -  Th e  n  .  d  i  nn er .  .  .  r  -
    "L1": "- C O O - C C C C C A A C - - - C C C C - C C A A A A C -",      # "left!" and the long "dinner" wide open: bright, hungry
    #        I  t  s  A  m  e  ri c  a  n  f  oo t  b  a  ll
    "T2": "C C - C - C C - C C O - - O C -",                                 # low: the small mouth, round on "foot" and "ball"
    #        .  h  ow .  l  o  ng n  ow .
    "L3": "- o o - o - - - o o -",                                           # hollow: the small round mouth, barely open (her O is a big "oh!", her C an open smile)
    #        A  m  i  nu t  f  i  f  t  y  .
    "T4": "C - C C - - C - C C C -",                                         # exactly as flat as the first
}
for line, shapes in MOUTHS.items():
    v.talk[line].seq = [None if x == "-" else x for x in shapes.split()]


# ---- the place: one bought painting, mended and turned to night by code (made once, kept beside it)
NUMBERS = [(150, 1122, 183, 1138), (186, 1143, 221, 1158), (219, 1166, 257, 1182), (265, 1192, 309, 1213),     # the yard numbers on the field (boxes in the painting)
           (316, 1226, 361, 1250), (380, 1263, 435, 1289), (455, 1307, 513, 1336), (543, 1359, 608, 1394)]
BANKS = [(70, 580, 175, 665), (585, 535, 700, 635), (920, 455, 1070, 580), (1320, 355, 1515, 520)]            # the four banks of lamps
MASTS_OUT = [(1300, 1536)]                                                                                     # the mast in these columns of the painting is taken out


def _paintings(src="bg_stand_1", day="bg_stand_1x", night="bg_stand_night"):
    out = ROOT / "work" / v.film / "art"
    if v.art.has(day) and v.art.has(night) and "--paint" not in sys.argv:     # --paint: make the two again (after a change of the numbers below)
        return day, night
    X8 = cv2.imread(str(v.art / src), cv2.IMREAD_COLOR)
    H, W = X8.shape[:2]
    # 1. each yard number is painted across its yard line: the box is filled with the grass round it, and the line is drawn through again
    L = cv2.cvtColor(X8, cv2.COLOR_BGR2LAB)[..., 0].astype(np.float32)
    src8 = X8.copy()
    for x0, y0, x1, y1 in NUMBERS:
        ends = []
        for xc in (x0 - 3, x1 + 3):
            col = L[y0 - 4:y1 + 4, xc - 1:xc + 2].mean(axis=1)
            col = col - np.median(col)
            mid = (y0 + y1) / 2 - (y0 - 4)
            k = int(np.argmax(col * np.exp(-((np.arange(len(col)) - mid) / (0.3 * len(col))) ** 2)))      # the line through the number: the one nearest the middle of the box
            ys = np.arange(max(0, k - 2), min(len(col), k + 3))
            wgt = np.clip(col[ys], 0, None)
            ends.append((xc, float((ys * wgt).sum() / (wgt.sum() + 1e-6)) + y0 - 4, src8[int(round(k + y0 - 4)), xc].astype(float), float((col > col[k] * 0.5).sum())))
        (xa, ya, ca, wa), (xb, yb, cb, wb) = ends
        m = np.zeros((H, W), np.uint8)
        m[y0:y1, x0:x1] = 255
        X8 = cv2.inpaint(X8, m, 5, cv2.INPAINT_TELEA)
        line = np.zeros((H, W), np.float32)
        cv2.line(line, (int(round(xa * 16)), int(round(ya * 16))), (int(round(xb * 16)), int(round(yb * 16))), 1.0, max(1, int(round(min(wa, wb) * 0.7))), cv2.LINE_AA, 4)
        line = cv2.GaussianBlur(line, (0, 0), 0.7)[..., None]
        for i in range(x0 - 3, x1 + 4):
            t = (i - xa) / (xb - xa)
            X8[y0 - 4:y1 + 4, i] = (X8[y0 - 4:y1 + 4, i] * (1 - line[y0 - 4:y1 + 4, i]) + (ca * (1 - t) + cb * t) * line[y0 - 4:y1 + 4, i]).astype(np.uint8)
    yy = np.mgrid[0:H, 0:W][0].astype(np.float32)

    def sky_and_masts(img):
        ff = np.zeros((H + 2, W + 2), np.uint8)                   # the sky: all that is joined to the top edge in the sky's own smooth colours
        for sx in range(20, W, 60):
            if ff[6, 1 + sx] == 0:
                cv2.floodFill(img, ff, (sx, 5), 0, (4, 4, 4), (4, 4, 4), cv2.FLOODFILL_MASK_ONLY | (255 << 8))
        sky = ff[1:-1, 1:-1] > 0
        sky[800:] = False
        rim = np.array([np.where(sky[:, x])[0].max() if sky[:, x].any() else 0 for x in range(W)], np.float32)   # the top of the far stand: the last sky row of each column
        rim = cv2.GaussianBlur(cv2.dilate(rim[None], np.ones((1, 61), np.uint8)), (0, 0), 12)[0]                 # (not the columns of the poles)
        return sky, rim, (~sky) & (yy < rim[None] - 2)            # the masts: what is not sky above it

    # 2. a mast that stands where his head is goes: its rows are filled with the sky to its left and right
    sky, rim, mast = sky_and_masts(X8)
    n, cc, stats, _ = cv2.connectedComponentsWithStats(mast.astype(np.uint8))
    for i in range(1, n):
        x, y, w, h, area = stats[i]
        if area > 500 and any(a <= x + w / 2 < b for a, b in MASTS_OUT):
            gone = cv2.dilate((cc == i).astype(np.uint8), np.ones((9, 9), np.uint8)) > 0
            gone &= yy < rim[None] - 1
            for row in np.where(gone.any(axis=1))[0]:
                xs = np.where(gone[row])[0]
                a, b = max(xs.min() - 2, 0), min(xs.max() + 2, W - 1)
                left, right = X8[row, a].astype(np.float32), X8[row, b].astype(np.float32)
                tt = ((np.arange(a, b + 1) - a) / max(1, b - a))[:, None]
                X8[row, a:b + 1] = (left * (1 - tt) + right * tt + 0.5).astype(np.uint8)
    cv2.imwrite(str(out / f"{day}.png"), X8)

    # 3. the same place at night
    P = X8[..., ::-1].astype(np.float32) / 255
    lum = P @ np.array([0.299, 0.587, 0.114], np.float32)
    grey = lum[..., None]
    sky, rim, mast = sky_and_masts(X8)
    faces, fronts, halo = np.zeros((H, W), np.float32), np.zeros((H, W), np.float32), np.zeros((H, W), np.float32)
    for x0, y0, x1, y1 in BANKS:                                  # the lamps: the light round shapes on the front of each bank
        box = np.zeros((H, W), bool)
        box[y0:y1, x0:x1] = True
        inside = mast & box
        if inside.sum() < 500:                                    # (a mast that was taken out)
            continue
        thr = cv2.threshold((lum[inside] * 255).astype(np.uint8), 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)[0] / 255
        light = cv2.morphologyEx((inside & (lum > thr)).astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
        n, cc, stats, _ = cv2.connectedComponentsWithStats(light)
        big = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))     # the largest light shape is the front of the bank itself
        f = np.isin(cc, [i for i in range(1, n) if i != big and stats[i, cv2.CC_STAT_AREA] >= 15]).astype(np.float32)
        faces += f
        fronts += (cc == big)
        for sig, peak in ((0.30 * (x1 - x0), HALO[0]), (1.1 * (x1 - x0), HALO[1])):      # a soft painted halo, the size of the bank
            g = cv2.GaussianBlur(f, (0, 0), sig)
            halo += g / g.max() * peak
    faces, fronts = cv2.GaussianBlur(np.clip(faces, 0, 1), (0, 0), 0.8)[..., None], np.clip(fronts, 0, 1)[..., None]
    r, g, b = P[..., 0], P[..., 1], P[..., 2]
    # the field: the olive greens below the far stand as one piece (its sunlit part), and its far end, which lies in
    # the stadium's shadow in the colours of the wall behind it (traced by hand: FAR_END)
    field = cv2.morphologyEx(((g > 0.93 * r) & (b < 0.86 * g) & (yy > rim[None] + 150)).astype(np.uint8), cv2.MORPH_OPEN, np.ones((9, 9), np.uint8))
    n, cc, stats, _ = cv2.connectedComponentsWithStats(field)
    field = (cc == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))).astype(np.uint8)
    cv2.fillPoly(field, [np.array(FAR_END, np.int32)], 1)
    field = cv2.morphologyEx(field, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (61, 61)))
    rows = np.where(field[:, :4].any(axis=1))[0]
    field[rows.min():rows.max() + 1, 0] = 1                       # (the field runs out of the painting on the left: closed there)
    outline, _ = cv2.findContours(field, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    cv2.drawContours(field, outline, -1, 1, -1)                   # with all that stands on it (the players, the goalpost)
    field = cv2.GaussianBlur(field.astype(np.float32), (0, 0), 2)[..., None]
    c = lambda *x: np.array(x, np.float32)                        # noqa: E731
    land = (P * 0.5 + grey * 0.5) * c(*LAND)                      # the stands and seats: half their colour, cold and dark
    # the field under the lights: no sun and no shadow any more (the shaded grass, greyer than the sunlit, is lifted to
    # the level of the sunlit), one green all over, its white lines kept, a little of its own colours left (the players)
    shade = np.clip((b / np.maximum(g, 0.05) - 0.70) / 0.22, 0, 1)
    shade = cv2.GaussianBlur(shade * shade * (3 - 2 * shade), (0, 0), 1.5)[..., None]
    even = grey * (1 + shade * (SUN_OVER_SHADE - 1))
    grass = c(*GRASS) * (even / 0.62) ** 1.3 * 0.82 + P * c(0.55, 0.72, 0.85) * 0.18
    N = land * (1 - field) + grass * field
    # what stands on the field (its white lines, the yellow goalpost, the players) keeps its own colours under the
    # white lights instead of turning green with the grass: all that differs from the grass round it (a median of
    # 41 px) by more than LINE_DIFF in Lab
    lab = lambda a: cv2.cvtColor(a, cv2.COLOR_BGR2LAB).astype(np.float32)                 # noqa: E731
    diff = np.linalg.norm(lab(X8) - lab(cv2.medianBlur(X8, 41)), axis=2)
    mark = cv2.GaussianBlur(np.clip((diff - LINE_DIFF) / 10.0, 0, 1), (0, 0), 0.8)[..., None] * field
    N = N * (1 - mark) + P * c(*LIT) * mark
    t =(np.clip((rim[None] - yy) / 720.0, 0, 1) ** 0.75)[..., None]
    night_sky = (c(*HAZE) * (1 - t) + c(*DEEP) * t) * (1 + (lum - cv2.GaussianBlur(lum, (0, 0), 60))[..., None] * 1.5)   # deep blue above, a haze of light over the stadium, its own soft streaks
    s = cv2.GaussianBlur(sky.astype(np.float32), (0, 0), 1.0)[..., None]
    N = N * (1 - s) + night_sky * s
    m = cv2.GaussianBlur(mast.astype(np.float32), (0, 0), 0.8)[..., None]
    N = N * (1 - m) + (P * 0.4 + grey * 0.6) * c(0.22, 0.27, 0.46) * m                   # the masts dark against the sky
    N = N * (1 - fronts * 0.5) + c(0.30, 0.36, 0.55) * fronts * 0.5                      # the fronts of the banks a little lit
    N = 1 - (1 - N) * (1 - np.clip(halo, 0, 1)[..., None] * c(0.78, 0.88, 1.0))
    N = N * (1 - faces) + c(1.0, 0.97, 0.86) * faces                                     # the lamps on
    cv2.imwrite(str(out / f"{night}.png"), (np.clip(N, 0, 1)[..., ::-1] * 255 + 0.5).astype(np.uint8))
    return day, night


HALO = (0.25, 0.08)                                    # how strong the halo of a bank of lamps is: near it, and far round it (kept faint: no glow in the house style)
LAND, GRASS = (0.40, 0.49, 0.80), (0.33, 0.55, 0.28)   # the night's colour on the stands; the green of the field under the lights
SUN_OVER_SHADE = 1.40                                  # how much lighter the sunlit grass of the painting is than its shaded grass
FAR_END = [(0, 1080), (100, 1077), (200, 1077), (257, 1080), (300, 1094), (400, 1128), (500, 1166), (600, 1209), (640, 1228), (600, 1262), (0, 1262)]   # the shaded far end of the field
HAZE, DEEP = (0.185, 0.255, 0.470), (0.050, 0.085, 0.215)   # the night sky just over the stadium, and high above
LINE_DIFF, LIT = 10.0, (0.86, 0.94, 1.05)              # how far from the grass a mark on the field is; the floodlight on it
DAY, NIGHT = _paintings()
v.art / "bg_stand_1"                                   # (asked for every time: pack.py keeps the bought painting itself too)


# ---- the light on the two of them
def gold(frame, T):
    return frame * np.array([1.035, 1.0, 0.93], np.float32)


def cold(frame, T):
    g = frame @ np.array([0.299, 0.587, 0.114], np.float32)
    return (frame * 0.90 + g[..., None] * 0.10) * np.array([0.84, 0.89, 1.0], np.float32)


# ---- two takes: by day the two of them as usual, the rim of the far stand and its lamps over her head, the field
#      behind her shoulder; at night (after a whip) closer, the lit lamps between her head and his.
#      drawing, a point in it (the rim of the far stand, at the left), where that lands in the frame, size
#      (the tops of the goalpost stay behind her head: two yellow sticks out of her hair would be a fault)
LUNA2, TIMO2 = (345, 1490, 1.16, 1.0), (890, 830, 1.16)              # the kit's close places: both 26 per cent bigger
NIGHT2 = (NIGHT, (300, 760), (60, 560), 0.92)                          # the painting closer too
#      by day she stands at the kit's wide place (her chin then stays clear of the caption under it), he 35 px higher
#      with her (his mouth over her hair: y < 1545 - 487 x 0.92 - 90 = 1007), and the painting 60 px higher (less
#      empty sky over them in the first second)
v.shot("start", bg=(DAY, (300, 760), (170, 443), 0.78), luna=(362, 1545, 0.92, -1.0), timo=(885, 985, 0.92), grade=gold)
v.shot("cut1", bg=NIGHT2, luna=LUNA2, timo=TIMO2, grade=cold)   # "cut1" is a whip in the sound plan

# ---- her face
v.faces(("start", None),                  # sunny, to us: two minutes left!
        ("cut1", "read_b"),               # night: eyes down, mouth shut; she asks without looking up
        ("whistle+0.04", "defend"))       # a minute fifty, and the whistle: her eyes come up to us
v.tilt(L1=3.2, L3=0.8)
v.blinks(luna=[1.36, "T2+0.50", "plate+0.70"],
         timo=[])                         # he does not blink: shut, open on her, and shut again

# ---- his eyes: shut in the first frame (he knows this game); open on her to warn her; and at night shut from the
#      first frame of the take to the last, "A minute fifty." said without opening them
v.eyes(("start", "shut"),
       ("L1.end+0.02", None),
       ("cut1", "shut"))

# ---- the words on screen
v.captions(
    split={"L1": [("Two minutes left!", None), ("Then dinner.", "L1@then")]},   # one line at a time, each low under her chin; the dinner arrives when she says it
    text={"T2": "It’s American football."},
    off={"T4": "plate-0.06"})                                            # the punchline stays up through the whistle and her look
v.plate(x=352, y=300, size=0.74)                                         # he is still standing: the plate goes beside his head

if __name__ == "__main__":
    v.main()
