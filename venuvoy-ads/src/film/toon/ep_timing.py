"""The picture of the short "Four Minutes Behind" (vlog 17, "timing"): one shot on a sunny station platform in the
morning.

    python3 src/film/toon/ep_timing.py --sheet        a contact sheet of the whole film (work/timing/sheet.jpg)
    python3 src/film/toon/ep_timing.py --stills 0.1 "T2+0.4" "L3+0.3" "glance+0.2" "T5+0.3" "plate+1.0"
    python3 src/film/toon/ep_timing.py                the film (ads/21-vlog/venuvoy-vlog-17-timing-en_...mp4)

Run ep_timing_sound.py first: every time below is read from its timeline (the stretch "glance" is his look down at
the stopwatch; "click" is its click).

Luna, sunny, tells her vlog that Timo is timing their travel day to the minute; behind her Timo, with a big stopwatch
hanging on a cord round his neck, has his eyes narrowed. He tells her, gravely, that they are four minutes behind; she
turns to him, worried: "Why? What happened?" The music stops dead and his eyes come to the lens: "This video." She
comes round to us, caught; his eyes go down to the stopwatch, it clicks, they come back up: "Five now."
A still, bright morning: her calm hair (it keeps his chest and the stopwatch clear), him in his stopwatch redraw.
"""
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import on_twos, read  # noqa: E402
from vlog import ROOT, Vlog, held  # noqa: E402

# ---- something only this film has: his eyes looking DOWN at the stopwatch on his chest.
#      His resting look ("on her") has the pupils at the bottom-LEFT of each eye, toward her; the stopwatch hangs below
#      and a little to the RIGHT of his face, so with that look he seemed to glance at her, not at the watch. Made once
#      by code from his own drawing and kept beside it as timo_down (delete it to make it again): each eye opening is
#      painted white again inside its outline, and the same flat pupil (its own brown, its own black rim, its own size)
#      is drawn at the bottom of the opening, a little right of the middle, cut by the lower lid as before.
DOWN = "timo_down"
EYE_BOX = (405, 640, 770, 790)          # both eyes of his base drawing (the kit's eye box stops at x 722)


def _eyes_down(src="timo_watch"):
    art = duo.Art("timing")
    if art.has(DOWN):
        return
    rgb = read(art / src)[..., :3].copy()
    x0, y0, x1, y1 = EYE_BOX
    sub = rgb[y0:y1, x0:x1]
    white = (sub.min(axis=2) > 0.80).astype(np.uint8)
    pupil = (np.abs(sub - np.array([0.28, 0.145, 0.155])).max(axis=2) < 0.06).astype(np.uint8)     # its own flat brown
    n, lab, stats, _ = cv2.connectedComponentsWithStats(white)
    eyes = sorted(range(1, n), key=lambda i: -stats[i, cv2.CC_STAT_AREA])[:2]
    brown = np.median(sub[pupil.astype(bool)], axis=0)
    rim = np.median(sub[sub.max(axis=2) < 0.15], axis=0)
    for i in eyes:
        w = (lab == i).astype(np.uint8)
        near = cv2.dilate(w, np.ones((25, 25), np.uint8))
        pn, plab, pst, _ = cv2.connectedComponentsWithStats(pupil & near)
        p = (plab == 1 + int(np.argmax(pst[1:, cv2.CC_STAT_AREA]))).astype(np.uint8)   # this eye's pupil: one round patch
        pts = cv2.findNonZero(w | p)
        hull = np.zeros_like(w)
        cv2.fillConvexPoly(hull, cv2.convexHull(pts), 1)
        inside = cv2.erode(hull, np.ones((5, 5), np.uint8)).astype(bool)   # the opening, short of its outline
        ys, xs = np.where(p)
        r = 0.5 * max(xs.max() - xs.min(), ys.max() - ys.min()) + 4.0      # the pupil with its rim
        hx = np.where(hull.any(axis=0))[0]
        cx = hx.min() + 0.56 * (hx.max() - hx.min())                       # a little right of the middle: the watch
        col = np.where(hull[:, int(cx)])[0]
        cy = col.max() - 0.62 * r                                          # low: the lower lid cuts it, as before
        yy, xx = np.mgrid[0:sub.shape[0], 0:sub.shape[1]]
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
        a_rim = np.clip(r + 0.5 - d, 0, 1)                                 # anti-aliased disc
        a_in = np.clip(r - 4.0 + 0.5 - d, 0, 1)
        new = np.ones_like(sub)                                            # white
        new = new * (1 - a_rim[..., None]) + rim * a_rim[..., None]
        new = new * (1 - a_in[..., None]) + brown * a_in[..., None]
        m = cv2.GaussianBlur(inside.astype(np.float32), (0, 0), 0.7)[..., None]
        sub[:] = sub * (1 - m) + new * m
    rgb[y0:y1, x0:x1] = sub
    cv2.imwrite(str(ROOT / "work" / "timing" / "art" / f"{DOWN}.png"), (np.clip(rgb[..., ::-1], 0, 1) * 255 + 0.5).astype(np.uint8))
    print("made", DOWN)


_eyes_down()
v = Vlog("timing", "ads/21-vlog/venuvoy-vlog-17-timing", luna="calm", timo="timo_watch", drive=True,
         extra_eyes={"down": (DOWN, EYE_BOX)},
         said={"travel": "C A C", "timos": "C E M O C", "timing": "C A M E C", "every": "E C E", "minute": "M E C",
               "were": "O C", "minutes": "M C C C", "behind": "M C A C",
               "happened": "A M C C", "video": "C C O", "five": "C A C", "now": "A O"})

# drawing, a point in it (where the rails meet, under the trees and the low sun), where that lands in the frame, size:
# the rails run away above her head on the left, the shelter and the soft waiting travellers between her and him
BG = ("bg_platform_1", (883, 1214), (300, 560), 1.3)
LUNA_AT = (344, 1556, 1.00, -1.2)      # both a little left of the first film's places: the stopwatch on his chest stays
TIMO_AT = (822, 935, 1.00)             # clear of the app buttons down the right edge


# ---- something only this film has: his head dips a little as his eyes go down to the stopwatch on his chest
_head = v.timo_head


def timo_head(T):
    ang, hx, hy, lean = _head(T)
    k = held(on_twos(T), v.t("glance"), v.t("glance.end-0.04"), 0.08, 0.12)
    return ang - k * 1.2, hx, hy + k * 7.0, lean


v.timo_head = timo_head

# ---- one shot: the two of them on the platform
v.shot("start", bg=BG, luna=LUNA_AT, timo=TIMO_AT)

# ---- her face
v.faces(("start", None),                  # sunny, proud of him
        ("T2@behind+0.06", "down"),       # four minutes behind: "oh" (not "guilty": she does not know yet that it is her)
        ("L3-0.10", "turn"),              # she turns to him: "Why? What happened?"
        ("L3.end+0.06", "guilty"),        # he is looking at us now; she steals a look at him, worried
        ("T4.end+0.10", "defend"))        # "This video.": she comes round to us, caught, and stays caught
v.tilt(L1=3.2, L3=2.2)
v.turn_shout = False                      # no line of hers is a shout: the turned head keeps its small mouth

# ---- his eyes
v.eyes(("start", "narrow"),               # a man timing something
       ("T2-0.12", None),                 # on her, gravely: four minutes behind
       ("L3.end+0.02", "lens"),           # the music stops dead and his eyes come to us
       ("glance", "down"),                # down to the stopwatch on his chest (it clicks)
       ("glance.end", "lens"))            # and back: "Five now."
v.blinks(luna=["L1@timing-0.04", "T2+0.30"],
         timo=["L1@day+0.10"])            # no blink on a punchline or in his stare

# ---- the words on screen
v.captions(
    split={"L1": [("Travel day!", None), ("Timo’s timing every minute.", "L1@timos-0.04")]},
    on={"L1": "start", "T2": "L1.end+0.02"},   # her first caption is up from frame 1 (the cover); his comes as hers goes
    off={"L1": "L1.end", "T4": "glance.end+0.05", "T5": "plate-0.05"})
v.plate(x=352, y=300, size=0.74)          # he is still standing: the plate beside his head, over the sky

if __name__ == "__main__":
    v.main()
