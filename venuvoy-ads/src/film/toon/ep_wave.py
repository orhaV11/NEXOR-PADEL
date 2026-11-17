"""The picture of the short "They Were Early" (vlog 14, "wave"): one shot at the top of a ballpark's upper deck.

    python3 src/film/toon/ep_wave.py --sheet        a contact sheet of the whole film (work/wave/sheet.jpg)
    python3 src/film/toon/ep_wave.py --stills 0.1 "wave_peak" "rise+0.5" "L2" "T3.end+0.3" "plate+1.0"
    python3 src/film/toon/ep_wave.py                the film (ads/21-vlog/venuvoy-vlog-14-wave-en_...mp4)

Run ep_wave_sound.py first: every time below is read from its timeline.

The wave comes round the stadium: she shouts it in, her open hand shoots up on "Wooo!", the roar passes. The place
beside her is empty: Timo would not stand for it. Half a second after it has gone he comes up into the frame, slowly,
eyes shut, and stays up. She turns to him: "It's gone." He: "They were early." His eyes come to us.
"""
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from toon import on_twos, read  # noqa: E402
from vlog import Vlog  # noqa: E402

# outdoors, a breeze at the top of the stand: her windy hair, him without the blade of grass
v = Vlog("wave", "ads/21-vlog/venuvoy-vlog-14-wave", luna="wind", timo="timo_clean",
         said={"wave": "O A C M", "here": "C E", "comes": "C A A M", "woo": "A", "its": "C", "gone": "A A C",
               "they": "C", "were": "O", "early": "A A C"})    # his words are short: a one-step open mouth would be cleaned away
# "wave" ends shut: the recogniser runs the word on through the breath before "Here", where her mouth hung half open.
# "woo" is her widest open mouth, the one she has on "wave!": the small round one read as a startled "ooh", not a cheer.
v.turn_shout = False                    # no shouting on the turned head: "It's gone." is said gently

# drawing, a point in it (the pitcher's mound of the far field), where that lands in the frame, size. The frame shows the
# painting's x 21-1021, y 771-2550: the far tiers across the top, the field top left, the lower bowl curving round toward
# us on the right, and the people of their own deck sitting just beside and behind them.
BG = ("bg_park_1", (220, 1290), (215, 560), 1.08)


# ---- something only this film has: THE WAVE runs through the painted crowd. The crowd is pushed up where the crest is
#      (people standing up), a stripe that travels round the bowl: across the far tiers from the left, round the curve
#      on the right, and through the people of their own deck beside her on "Wooo!", then on past the lens. It is the
#      painting itself, moved (no new drawing); on twos, like the two of them.
BOX = (0, 790, 1104, 1880)                                   # the part of the painting the crowd lives in (x0, y0, x1, y1)
CROWD = [                                                    # where the crowd is painted, kept clear of the rails, the field and the seats
    [(0, 834), (870, 834), (985, 890), (1050, 975), (1090, 1080), (1104, 1080), (1104, 1380), (1000, 1400), (968, 1300),
     (950, 1060), (885, 995), (790, 960), (640, 944), (0, 942)],                                       # the far tier and its strip on the right
    [(0, 968), (600, 968), (760, 990), (865, 1040), (905, 1130), (912, 1400), (900, 1490), (780, 1550), (640, 1610),
     (520, 1655), (500, 1600), (580, 1500), (625, 1400), (662, 1300), (630, 1215), (560, 1172), (430, 1138), (0, 1126)],   # the lower bowl, round the curve
    [(1104, 1455), (1104, 1850), (1000, 1840), (900, 1812), (800, 1792), (700, 1778), (600, 1772), (525, 1772), (525, 1690),
     (600, 1662), (700, 1640), (800, 1600), (870, 1560), (905, 1495), (1000, 1462)],                   # their own deck: heads and shoulders
]
FAR = 2                                                      # the first two are far: arms go up there; the near people only stand
PATH = [(-100, 900), (500, 900), (800, 950), (950, 1150), (1000, 1450), (900, 1650), (600, 1750), (200, 1850)]   # round the bowl toward the lens
U_AT = [0, 600, 904, 1154, 1458, 1682, 1998, 2410]           # the distance along it at each point
LIFT = [8, 8, 9, 10, 26, 32, 28]                          # how far the crest lifts the crowd there (pixels of the painting): far small, near big
WIDE = [168, 182, 210, 238, 294, 322, 322]                   # how wide the crest is (wide, so that its edge is soft and no block shows)
SKIN = [(0.95, 0.85, 0.73), (0.92, 0.79, 0.66), (0.86, 0.70, 0.56), (0.76, 0.58, 0.45), (0.94, 0.86, 0.76)]   # the people standing (a head
                                                             # and two arms, \o/): the painting's own skin tones, none near white (white strokes read as scratches)
SHIRT = [(0.96, 0.93, 0.86), (0.78, 0.86, 0.93), (0.95, 0.88, 0.62), (0.93, 0.93, 0.90), (0.86, 0.52, 0.46), (0.96, 0.93, 0.86)]   # their shirts,
                                                             # caught by the sun as they stand: what makes the band bright on a phone
SPACING = 16                                                 # one standing person every 16 px of the painting (13 x 12 thin strokes were a field of scratches)
LIGHT = 0.50                                                 # how much lighter the crowd is where it stands: this soft bright band is what
                                                             # a phone shows of the wave (at 0.26 it was nearly gone, at 0.40 faint)
LIGHT_NEAR = 0.20                                            # the people of their own deck, drawn close: only a touch (0.5 washed them out like a glow)


def _wave_maps():
    x0, y0, x1, y1 = BOX
    mask = np.zeros((y1 - y0, x1 - x0), np.float32)
    far = np.zeros_like(mask)
    for n, poly in enumerate(CROWD):
        cv2.fillPoly(mask, [np.array([(x - x0, y - y0) for x, y in poly], np.int32)], 1.0, cv2.LINE_AA)
        if n < FAR:
            cv2.fillPoly(far, [np.array([(x - x0, y - y0) for x, y in poly], np.int32)], 1.0, cv2.LINE_AA)
    inside = mask > 0.5
    light = (LIGHT_NEAR + (LIGHT - LIGHT_NEAR) * cv2.GaussianBlur(far, (0, 0), 12)).astype(np.float32)   # how much lighter, pixel by pixel
    far = cv2.erode(far, np.ones((9, 9), np.uint8))
    mask = cv2.GaussianBlur(cv2.erode(mask, np.ones((15, 15), np.uint8)), (0, 0), 9)       # it fades out well before a rail: no straight edge shows
    below = np.zeros(mask.shape, np.float32)                                                  # how far down each pixel is from the region's lower edge
    for y in range(mask.shape[0] - 2, -1, -1):
        below[y] = np.where(inside[y + 1], below[y + 1] + 1, 0)
    ys, xs = np.mgrid[y0:y1:4, x0:x1:4].astype(np.float32)                                    # u: where along the path each pixel is
    best, u = np.full(xs.shape, 1e9, np.float32), np.zeros(xs.shape, np.float32)
    for (ax, ay), (bx, by), ua, ub in zip(PATH, PATH[1:], U_AT, U_AT[1:]):
        dx, dy = bx - ax, by - ay
        p = np.clip(((xs - ax) * dx + (ys - ay) * dy) / (dx * dx + dy * dy), 0, 1)
        d = (xs - ax - p * dx) ** 2 + (ys - ay - p * dy) ** 2
        take = d < best
        best, u = np.where(take, d, best), np.where(take, ua + p * (ub - ua), u)
    u = cv2.resize(cv2.GaussianBlur(u, (0, 0), 6), (x1 - x0, y1 - y0), interpolation=cv2.INTER_LINEAR)
    paint = np.ascontiguousarray(read(v.art / BG[0])[y0:y1, x0:x1, :3])
    rng = np.random.default_rng(14)                                                           # the far crowd: one site a person, on a jittered grid
    sites = []
    for yy in range(8, y1 - y0 - 6, SPACING):
        for xx in range(6, x1 - x0 - 6, SPACING):
            x, y = xx + rng.uniform(-6, 6), yy + rng.uniform(-5, 5)
            if far[int(y), int(x)] > 0.5 and rng.random() < 0.9:
                sites.append((x, y, float(u[int(y), int(x)]), SKIN[rng.integers(len(SKIN))], rng.uniform(0.5, 3.0), rng.uniform(0.8, 1.2)))
    return mask, np.ascontiguousarray(u, np.float32), paint, below, sites, light[..., None]


W_MASK, W_U, W_PAINT, W_BELOW, W_SITES, W_LIGHT = _wave_maps()
_W_ROWS, _W_COLS = (np.ascontiguousarray(a) for a in np.mgrid[0:W_MASK.shape[0], 0:W_MASK.shape[1]].astype(np.float32))
_W_LIFT, _W_WIDE = np.interp(W_U, U_AT[1:], LIFT).astype(np.float32), np.interp(W_U, U_AT[1:], WIDE).astype(np.float32)
_wave_cache = {}


def crest(T):
    """Where the crest is along the path now: at the far left as the film starts, round the curve on "Here it comes!",
    at the people beside her on "Wooo!", then on past the lens."""
    hit = v.t("L1@woo-0.07")      # from the first frame the crest is already on the far side, in the middle of the picture (it started
    return float(np.interp(T, [-0.4, 0.0, hit - 0.8, hit, hit + 0.25, hit + 0.7, hit + 1.2], [300, 600, 1154, 1682, 1998, 2410, 2900]))   # top left at u 150)


def _wave_picture(uc):
    g = np.exp(-((W_U - uc) / _W_WIDE) ** 2) * W_MASK
    d = np.minimum(_W_LIFT * g, W_BELOW * 0.6)                                       # never pulls up what lies below the crowd (a rail, a seat)
    moved = cv2.remap(W_PAINT, _W_COLS, (_W_ROWS + d).astype(np.float32), cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)
    moved = moved * (1 - W_LIGHT * g[..., None]) + W_LIGHT * g[..., None]            # lighter where they stand: shirts and faces up in the sun
    arms = np.zeros(moved.shape[:2] + (4,), np.float32)
    P = lambda p: (int(round(p[0] * 4)), int(round(p[1] * 4)))                        # noqa: E731  (a point, at a quarter pixel)
    for x, y, u, col, spread, size in W_SITES:                                       # and they stand up with their arms in the air: \o/
        h = float(np.exp(-((u - uc) / np.interp(u, U_AT[1:], WIDE)) ** 2))
        if h < 0.25:
            continue
        up = ((h - 0.25) / 0.75) ** 0.7                                              # how far up they are now
        s = size * (0.8 + u / 3000)                                                  # nearer people are bigger
        r = 3.0 * s                                                                  # a head the size of the painted ones
        hx, hy = x, y - float(d[int(y), int(x)]) - 2 - 4 * up * s                    # the head rises with the crowd, and a little more
        shirt = SHIRT[int(x * 7 + y * 13) % len(SHIRT)]                              # the body under it: a soft rounded shirt
        cv2.ellipse(arms, P((hx, hy + r * 1.9)), (int(round(r * 1.1 * 4)), int(round(r * 1.5 * 4))), 0, 0, 360, (*shirt, 1.0), -1, cv2.LINE_AA, 2)
        for side in (-1, 1):                                                         # arms from the shoulders, nearly straight up, above the head
            a = (hx + side * r * 0.75, hy + r * 0.5)
            b = (hx + side * (r * 0.9 + (0.8 + 0.7 * spread) * s), hy - (3 + 10 * up) * s)
            cv2.line(arms, P(a), P(b), (*col, 1.0), 2, cv2.LINE_AA, 2)
        cv2.circle(arms, P((hx, hy)), int(round(r * 4)), (*col, 1.0), -1, cv2.LINE_AA, 2)
    arms = cv2.GaussianBlur(arms, (0, 0), 0.9)
    aa = np.clip(np.maximum(g * 6, 0), 0, 1)[..., None]
    out = np.clip(moved, 0, 1) * aa
    al = arms[..., 3:4] * 0.85
    out = out * (1 - al) + arms[..., :3] * al                                         # premultiplied: the arms over the moved crowd
    return np.dstack([out, np.maximum(aa, al)]).astype(np.float32)


def wave(st, t, T):
    t2 = round(on_twos(T), 4)
    uc = crest(t2)
    if not (-150 < uc < 2800):
        return
    if t2 not in _wave_cache:
        _wave_cache.clear()
        _wave_cache[t2] = _wave_picture(uc)
    name, src, at, k = BG
    st.put(_wave_cache[t2], at[0] + (BOX[0] - src[0]) * k, at[1] + (BOX[1] - src[1]) * k, scale=k, anchor=(0.0, 0.0), depth=0.45)


v.shot("start", bg=BG, luna=(372, 1556, 1.00, -1.2), timo=(872, 935, 1.00), behind=wave)
v.rise("rise", "rise.end")              # he was sitting below the frame; he comes up after the wave has gone

v.faces(("start", None),                # sunny, shouting the wave in
        ("rise.end+0.15", "turn"))      # he has stopped at full height; she turns to him (and stays turned, looking up at him)
v.eyes(("rise", "shut"),                # he comes up with his eyes shut
       ("T3-0.22", None),               # they open on her
       ("T3.end+0.10", "lens"))         # and come to us
v.tilt(L1=3.2, L2=2.2)
v.blinks(luna=["L1.end+0.3", "rise+2.0"])       # one in the stillness after the cheer, one while he comes up behind her (none on his arrival)
v.piece("hand", up="L1@woo-0.06", down="L1.end-0.30", lift=120, dx=-40, shake="L1@woo+0.30", rise=0.16)   # it goes down as her
                                        # "Wooo" trails off and is gone (0.7 s) about half a second before his hair comes into view
v.captions(split={"L1": [("The wave! Here it comes!", None), ("WOOO!", "L1@woo")]},
           off={"T3": "T3.end+0.50"})          # up through the first half of his look at us: long enough to read
v.plate(x=352, y=300, size=0.74)        # he is still standing: the plate goes beside his head

if __name__ == "__main__":
    v.main()
