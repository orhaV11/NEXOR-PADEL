"""The picture of the sketch "Safety Car" (vlog 08, "safety"): three shots, each closer, in the front row of a
grandstand at a motor racing circuit on a windy, overcast day.

    python3 src/film/toon/ep_safety.py --sheet        a contact sheet of the whole film (work/safety/sheet.jpg)
    python3 src/film/toon/ep_safety.py --stills 0.15 "cut1+0.1" "L7+0.3" "look+0.4" "pass+0.3" "plate+1.0"
    python3 src/film/toon/ep_safety.py                the film (ads/21-vlog/venuvoy-vlog-08-safety-en_...mp4)

Run ep_safety_sound.py first: every time below is read from its timeline.

Luna has picked a driver to cheer for: the one with lights on the roof, who is winning. Timo tells her it is the
safety car; every answer of his makes her surer, until "So he can't lose!". The music stops, he looks at us, works it
out, finds no hole in it, and changes sides: "...Come on, safety car." A car goes by; she waves her flag.
Two things are this film's own, drawn by hooks:
  - the cars: a soft blurred bar of one colour that crosses the track from right to left in about a quarter of a
    second (twice: red at the start, orange at the end), BEHIND the catch fence: the wires and posts of the painting
    are laid back over it (found where the painting differs from a version of itself smeared along the track).
  - her small flag (dark green over cream, no emblem): its own drawing, luna_flag (a redraw of luna_w1 with only the
    arm added), turned about the cuff of her sleeve to wave it, its cloth rippling in the wind (six drawings made
    here by bending the cloth), held up in the first two shots, lowered when she turns to him puzzled, and up again
    to the end.
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np
from scipy.ndimage import median_filter

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import ease, on_twos, premul, read, wobble  # noqa: E402
from vlog import Vlog  # noqa: E402

# a windy day: her blowing hair; him without the blade of grass, nothing on him. The words the joke rests on get their
# mouth shapes by hand (none of hers has the bared-teeth E: on her sunny face it reads as a grimace)
v = Vlog("safety", "ads/21-vlog/venuvoy-vlog-08-safety", luna="wind", timo="timo_clean",
         said={"motor": "M O C", "racing": "C A C", "ive": "A C", "picked": "M C C", "driver": "C A C",
               "which": "O C", "one": "O A C", "lights": "C A C", "on": "O C", "roof": "C O C", "hes": "C", "winning": "O C C",
               "thats": "C A C", "safety": "C A C C", "car": "C A", "in": "C", "front": "C A C", "not": "C O C", "race": "C A C",
               "then": "C", "why": "O A", "is": "C", "nobody": "C O M O C", "passing": "M A C C", "him": "C M",
               "theyre": "C A", "allowed": "A O C", "so": "O", "he": "C", "cant": "C A C", "lose": "C O C", "come": "C O M"})
v.turn_shout = False          # turned to him she keeps the small mouth (its wide one is a gape)

# ---- the three shots: each a fifth closer than the one before (the characters and the painting; at seventeen per
#      cent the watching model took the film for one continuous shot)
TRACK = ("bg_track_1", (400, 1150))           # the drawing and a point in it: the middle of the track at the left
BG_A = TRACK + ((250, 510), 0.93)             # where that point lands in the frame, and the size: the track above her head
BG_B = TRACK + ((220, 400), 1.12)
BG_C = TRACK + ((200, 285), 1.35)
LUNA_A, TIMO_A = (362, 1545, 0.86, 0.0), (870, 1000, 0.86)     # his mouth above her flying hair: y < luna y - 487 x size - 90
LUNA_B, TIMO_B = (352, 1548, 1.04, -0.8), (884, 930, 1.04)
LUNA_C, TIMO_C = (300, 1590, 1.25, 0.8), (905, 885, 1.25)    # she a little left: room for the flag between her face and the edge

# ---- the cars --------------------------------------------------------------------------------------------------------
SLOPE = 0.1237                                # the track runs down to the right in the painting: y = 1100 + 0.1237 x
LANE = lambda x: 1100.0 + SLOPE * x           # noqa: E731   the middle of the track (pixels of the painting)
CAR_LEN, CAR_H = 560.0, 66.0                  # the streak in pixels of the painting: long, a car's height
STREAKS = [("pass1", 0.15, 0.26, (0.86, 0.13, 0.11)),         # (time it is in the middle of the frame, from, seconds to cross, colour)
           ("pass", 0.25, 0.26, (0.95, 0.52, 0.10))]


def _bar(colour):
    """A soft blurred bar of one colour, its head at the left (it moves left): a firm head, a long fading tail."""
    s = 2
    w, h = int(CAR_LEN * s), int(CAR_H * 2.2 * s)
    xx = np.linspace(0, 1, w)[None, :]
    yy = np.linspace(-1.1, 1.1, h)[:, None]
    along = np.clip(xx / 0.05, 0, 1) ** 0.6 * np.clip(1.25 * (1 - xx), 0, 1) ** 1.3    # in fast at the head, a long fading tail
    across = np.exp(-(np.abs(yy) / 0.62) ** 6)                          # a soft edge above and below
    a = np.clip(along * across * 1.05, 0, 1).astype(np.float32)
    a = cv2.GaussianBlur(a, (0, 0), 1.4 * s)
    img = np.dstack([np.full_like(a, colour[0]), np.full_like(a, colour[1]), np.full_like(a, colour[2]), a])
    return premul(cv2.resize(img, (int(CAR_LEN), int(CAR_H * 2.2)), interpolation=cv2.INTER_AREA))


BARS = {name: _bar(c) for name, _, _, c in STREAKS}
FY0, FY1 = 1010, 1400                         # the strip of the painting the cars cross


def _fence():
    """The catch fence of the painting, cut out of it: wires and posts are what differs from the painting smeared
    along the track (a median 81 pixels long), below the top wire of the mesh (so not the white line or the kerbs)."""
    paint = read(v.art / TRACK[0])[..., :3]
    band = paint[FY0:FY1]
    flat = median_filter(band, size=(1, 81, 1))
    d = np.abs(band - flat).max(axis=2)
    m = (d > 0.045).astype(np.uint8)
    n, lab, stats, _ = cv2.connectedComponentsWithStats(m, connectivity=8)
    m = np.isin(lab, 1 + np.where(stats[1:, cv2.CC_STAT_AREA] > 40)[0]).astype(np.uint8)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)))   # the insides of the posts
    yy, xx = np.mgrid[FY0:FY1, 0:band.shape[1]]
    top = 1040.0 + xx * (150.0 / 1536)        # 30 px above the mesh's top wire (1070 at the left, 1200 at the right)
    m = m * (yy > top)
    a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 0.8)
    return premul(np.dstack([band, a]).astype(np.float32))


FENCE = _fence()


def _frame_xy(bg, x, y):
    name, src, at, k = bg
    return at[0] + (x - src[0]) * k, at[1] + (y - src[1]) * k


def cars(st, t, T):
    bg = v.shot_at(T)["bg"]
    _, src, at, k = bg
    drawn = False
    for name, off, dur, _ in STREAKS:
        mid = v.t(name) + off
        if not (mid - dur * 0.8 <= T <= mid + dur * 0.8):
            continue
        # the head runs from the right edge of the frame to beyond the left one (the tail with it), evenly, on ones
        x_left = src[0] + (-40 - at[0]) / k - CAR_LEN
        x_right = src[0] + (1120 - at[0]) / k
        hx = x_right + (x_left - x_right) * ((T - (mid - dur / 2)) / dur)
        X, Y = _frame_xy(bg, hx, LANE(hx))
        st.put(BARS[name], X, Y, scale=k, turn=math.degrees(math.atan(SLOPE)), anchor=(0.0, 0.5), depth=0.45)
        drawn = True
    if drawn:                                  # the fence stands in front of the track
        X, Y = _frame_xy(bg, 0, FY0)
        st.put(FENCE, X, Y, scale=k, anchor=(0.0, 0.0), depth=0.45)


# ---- her flag ----------------------------------------------------------------------------------------------------------
ARM = duo.arm(v.art, "luna_flag", v.s_luna, (1150, 1000, 1792, 2400), "luna_w1")
PIVOT = (1460, 2330)                          # the middle of the sleeve where it leaves the frame (pixels of her drawing): it turns about this
STICK_X, CLOTH_Y = 1428, 1352                 # right of this column and above this row is the cloth (pixels of her drawing)
NEARER = -45.0                                # frame pixels toward her: the sleeve runs into her coat, it is hers


def _ripples(n=6, amp=13.0, wave=250.0):
    """The cloth bent by a travelling wave, fixed at the stick: n drawings of a loop."""
    s = v.s_luna
    h, w = ARM.shape[:2]
    x0, y1 = int(STICK_X * s), int(CLOTH_Y * s)
    ys, xs = np.mgrid[0:y1, x0:w].astype(np.float32)
    reach = (xs - x0) / (w - x0)
    out = []
    for k in range(n):
        ph = 2 * math.pi * k / n
        dy = amp * s * np.sin(2 * math.pi * (xs - x0) / (wave * s) - ph) * reach
        dx = -3.0 * s * (1 - np.cos(2 * math.pi * (xs - x0) / (wave * s) - ph)) * reach
        img = ARM.copy()
        img[:y1, x0:] = cv2.remap(ARM, xs + dx, ys - dy, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
        out.append(img)
    return out


FLAGS = _ripples()
GONE = 1250.0                                 # how far below its place it is when out of the frame (frame pixels at size 1)


def _flag_pose(T):
    """(pixels below its drawn place, turn in degrees, or None when it is out of the frame) at the time T."""
    t = on_twos(T)
    still0, down0, down1 = v.t("T6@race-0.30"), v.t("T6@race+0.25"), v.t("T6@race+0.95")      # still for half a second, then lowered evenly
    up0 = v.t("L9.end+0.06")
    if T < down0:
        cheer = 7.0 * held(t, -1.0, v.t("L1.end-0.1"), 0.1, 0.4)                               # she cheers with it, shouting
        win = 9.0 * held(t, v.t("L3@winning-0.1"), v.t("L3.end+0.05"), 0.08, 0.35)            # and on "winning"
        front = 5.0 * held(t, v.t("L5@front-0.1"), v.t("L5.end"), 0.08, 0.3)                  # and on "front"
        idle = 1.8 * (1.0 - ease(t, still0 - 0.2, still0, 0.0, 1.0, "io"))
        amp = max(cheer, win, front) + idle
        return 0.0, -2.0 + amp * math.sin(2 * math.pi * 2.6 * t)
    if T < down1:
        return ease(T, down0, down1, 0.0, GONE, "io") if T >= down0 else 0.0, -2.0 - 4.0 * ease(T, down0, down1, 0.0, 1.0, "io")
    if T < up0:
        return None
    rise = ease(t, up0, up0 + 0.45, GONE, 0.0, "out")                            # up from below, seen coming
    wave = 3.0 * held(t, v.t("pass+0.05"), v.t("end"), 0.15, 0.3) + 1.5          # it waves at the car, and on through the name,
    return rise, 1.0 + wave * math.sin(2 * math.pi * 2.4 * t)                     # swinging outward, never across her face


def held(t, on, off, rise=0.16, fall=0.35):
    return ease(t, on - rise, on, 0.0, 1.0, "io") - ease(t, off, off + fall, 0.0, 1.0, "io")


def flag(st, t, T):
    pose = _flag_pose(T)
    if pose is None:
        return
    off, ang = pose
    shot = v.shot_at(T)
    x0, y0, k, lean = shot["luna"]
    dx, dy, turn, sx, sy = v.luna_move(T)     # it is hers: it sways and pops with her body
    s = v.s_luna * k
    a = math.radians(turn + lean)
    px, py = (PIVOT[0] - 663) * s, (PIVOT[1] - 1680) * s
    x, y = x0 + dx + px * math.cos(a) - py * math.sin(a), y0 + dy + px * math.sin(a) + py * math.cos(a)
    anchor = (PIVOT[0] / duo.L_SIZE[0], PIVOT[1] / duo.L_SIZE[1])
    img = FLAGS[int(on_twos(T) * 12) % len(FLAGS)]            # the cloth ripples twelve drawings a second
    tip, lift, nearer = FLAG_AT[id(shot["luna"])]
    st.put(img, x + nearer, y + off * k - lift, scale=k, turn=turn + lean + ang + tip, sx=sx, sy=sy, anchor=anchor, depth=1.0)


# in each shot: how far it leans toward her (degrees) and how much higher it is held (frame pixels), so that the cloth
# stays clear of the flying end of her scarf (the same green and cream) and inside the frame
# (in the closest shot it also stands further toward her: her face fills the left, the frame ends on the right)
FLAG_AT = {id(LUNA_A): (0.0, 0.0, NEARER), id(LUNA_B): (-3.0, 60.0, NEARER), id(LUNA_C): (-4.0, 40.0, -120.0)}

v.shot("start", bg=BG_A, luna=LUNA_A, timo=TIMO_A, behind=cars, front=flag)
v.shot("cut1", bg=BG_B, luna=LUNA_B, timo=TIMO_B, behind=cars, front=flag)
v.shot("cut2", bg=BG_C, luna=LUNA_C, timo=TIMO_C, behind=cars, front=flag)

# ---- her face
v.faces(("start", None),                  # sunny, shouting it over the engine
        ("T6+0.30", "turn"),              # she looks up at him, a small patient smile, and asks him: honestly puzzled
        ("T8.end-0.20", "down"),          # "They're not allowed.": oh. Eyes down: it sinks in
        ("L9-0.08", "turn"),              # "So he can't lose!": to him, amazed
        ("L9.end+0.10", None))            # and back to us, sunny, the flag up: to the end
v.tilt(L1=3.2, L3=-2.6, L5=3.0, L7=2.2, L9=2.6)

# ---- his eyes: shut as the car screams past; on her; on us when the music stops; narrowed while he works it out;
#      on us again for the last line and to the end
v.eyes(("start", "shut"),
       ("T2-0.25", None),
       ("L5.end-0.25", "shut"),           # "He's in front.": he shuts his eyes for a breath
       ("T6+0.05", None),
       ("look", "lens"),
       ("narrow", "narrow"),
       ("narrow.end", "lens"))

# ---- the words on screen
CAP_A, CAP_B, CAP_C = (420, 1425), (395, 1430), (420, 1430)     # left of the flag
v.captions(
    text={"L1": "Motor racing!\nI've picked a driver!", "L3": "Lights on the roof.\nHe's winning!",
          "L7": "Then why is nobody\npassing him?", "T10": "…Come on,\nsafety car."},
    on={"T2": "T2-0.12", "T4": "cut1+0.04"},                         # his short lines up a moment early (time to read them),
    off={"L1": "T2-0.12", "L3": "cut1+0.04", "T10": "plate-0.05"},   # and never two captions at once (a caption shows until off-0.04)
    place={"L1": CAP_A, "T2": CAP_A, "L3": CAP_A, "T4": CAP_B, "L5": CAP_B, "T6": CAP_B, "L9": CAP_C, "T10": (330, 1420)})
v.plate(x=352, y=285, size=0.68)          # he is standing at the end: the plate beside his head, above hers

if __name__ == "__main__":
    v.main()
