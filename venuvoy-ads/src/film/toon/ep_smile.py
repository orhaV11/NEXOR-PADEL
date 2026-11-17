"""The picture of the short "Too Much" (vlog 15, "smile"): one shot beside a golf fairway on a bright, breezy morning.

    python3 src/film/toon/ep_smile.py --sheet        a contact sheet of the whole film (work/smile/sheet.jpg)
    python3 src/film/toon/ep_smile.py --stills 0.1 "T2+0.4" "L3+0.3" "look+0.4" "L4+0.3" "plate+0.8"
    python3 src/film/toon/ep_smile.py                the film (ads/21-vlog/venuvoy-vlog-15-smile-en_...mp4)

Run ep_smile_sound.py first: every time below is read from its timeline (the stretch "look" is his big smile).

Luna presents Timo with her open hand ("Timo, smile!"); he says, flat into the lens, that this is him smiling; she
turns to him ("Do the big one."); the music stops dead and he looks into the lens again: nothing changes, one blink;
she winces and her hand flies up out of her shoulder, palm to the lens, in front of his chest ("Okay. Too much."), and
then looks at us, caught. The film ends on her fingers across the right of the frame beside his face, which is on us,
unchanged. (The script had the hand half over his face: the watching model then invented a huge grin behind it and took
the hand for his; so his whole face stays in view and her sleeve runs into her own coat.)
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from toon import ease, key_out, on_twos, premul, read, wobble  # noqa: E402
from vlog import ROOT, Vlog  # noqa: E402

# a bright, breezy morning: her blowing hair, him without the blade of grass and with nothing on him.
# Mouths (set against the loudness of each word): he says "smiling" through nearly still lips, with no open mouth at
# all (his wide "A" on "smi-" was a frame a watching model could take for a grin); her "Timo" closes only on its "m"
# and "smile" closes at its start ("sm-") and stays open to the end of the word.
v = Vlog("smile", "ads/21-vlog/venuvoy-vlog-15-smile", luna="wind", timo="timo_clean",
         said={"were": "O C", "at": "A C", "golf": "O C", "timo": "C E E E M O O C", "smile": "M A A A C",
               "this": "C", "is": "C", "me": "M C", "smiling": "M C C C",
               "do": "O", "big": "M C", "one": "O A C", "okay": "O C A", "too": "O", "much": "M A C"})

# drawing, a point in it (the red flag on the far green), where that lands in the frame, size: the green and its flag
# above her head on the left, the quiet gallery behind its rope between her and him, the trees leaning in the wind
BG = ("bg_golf_1", (368, 1118), (240, 540), 1.0)
LUNA_AT = (372, 1556, 1.00, -1.2)
TIMO_AT = (872, 935, 1.00)


# ---- something only this film has: her open hand pushed up in front of the lens on his side ("Too much.")
#      The drawing (hand_lens_2, itself redrawn from hand_lens_1) has the hand and the yellow cuff right, but also
#      left-overs of her body: a blurred coat sleeve on the left, a small hand on the right, a ghost of her scarf inside
#      the sleeve. Kept: the hand (everything inside its own black outline) and the sleeve between its two outlines;
#      the inside of the sleeve below the rim of the cuff is painted again in its own flat yellow; the sleeve runs on
#      below the drawing. Made once by code, kept beside the drawing as hand_lens_cut (delete it to make it again).
#      The fixer's pass: the wrist in one light tone, the heel of the palm snapped right to its outline, and a dark
#      smudge under the right end of the cuff's rim painted out.
CUT = "hand_lens_cut"


def _hand_cut(src="hand_lens_2"):
    path = v.art / src                              # (asked for every time: pack.py keeps the drawing itself too)
    if v.art.has(CUT):
        return
    rgb = read(path)[..., :3]
    h, w = rgb.shape[:2]
    rgba = key_out(rgb)
    dark = (rgb.max(axis=2) < 0.32).astype(np.uint8)
    # the hand: every patch of skin inside its outline (the palm lines cut it into several: fingers, palm, ball of the
    # thumb), down to the rim of the cuff; not the left-overs (a yellow coat on the left, a pinker small hand on the right)
    wall = cv2.dilate(dark, np.ones((3, 3), np.uint8))
    wall[1700:] = 1
    n, lab, stats, cen = cv2.connectedComponentsWithStats((1 - wall).astype(np.uint8), connectivity=4)
    skin = np.array([0.995, 0.69, 0.62])
    inside = np.zeros((h, w), bool)
    for i in range(1, n):
        if stats[i, cv2.CC_STAT_AREA] < 1500 or cen[i][0] > 1420:
            continue
        if np.abs(rgb[lab == i].mean(axis=0) - skin).max() < 0.08:
            inside |= lab == i
    inside = cv2.morphologyEx(inside.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8)).astype(bool)   # the palm lines between the patches
    hand = cv2.dilate(inside.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (31, 31))).astype(bool) & (wall.astype(bool) | inside)   # with its outline (and the pixel between)
    hand |= inside
    # the sleeve: between its two outlines (outer edges from (598, 1660) to (558, 2400) and (1362, 1675) to (1404, 2400))
    yy, xx = np.mgrid[0:h, 0:w]
    left = 598 + (yy - 1660) * (558 - 598) / (2400 - 1660)
    right = 1362 + (yy - 1675) * (1404 - 1362) / (2400 - 1675)
    sleeve = (yy >= 1640) & (xx >= left) & (xx <= right)
    keep = ((hand | sleeve) & (rgba[..., 3] > 0.5)).astype(np.uint8)
    n, lab, stats, _ = cv2.connectedComponentsWithStats(keep)
    keep = lab == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))      # one piece: no scrap of the left-overs at the corner of the cuff
    keep = cv2.dilate(keep.astype(np.uint8), np.ones((3, 3), np.uint8)) & ((hand | sleeve).astype(np.uint8))
    keep = cv2.GaussianBlur(keep.astype(np.float32), (0, 0), 1.0)
    out = rgba.copy()
    out[..., 3] *= keep
    # the wrist where it goes into the cuff: a blurred ghost of her scarf lies on it; its own two flat tones again
    near_line = cv2.dilate(dark, np.ones((7, 7), np.uint8)).astype(bool)
    tone = np.median(rgb[1450:1530, 850:1000].reshape(-1, 3), axis=0), np.median(rgb[1600:1660, 1130:1190].reshape(-1, 3), axis=0)
    for x in range(748, 1200):
        ys = np.where(dark[1690:1800, x])[0]
        bottom = 1690 + (ys.min() if len(ys) else 60)
        rows = np.arange(1480, bottom)[~near_line[1480:bottom, x]]
        px = out[rows, x, :3]
        d0, d1 = np.abs(px - tone[0]).max(axis=1), np.abs(px - tone[1]).max(axis=1)
        ghost = np.minimum(d0, d1) > 0.05
        snap = np.where((d0 <= d1)[:, None], tone[0], tone[1])
        own = tone[0] if x < 1112 else tone[1]
        out[rows, x, :3] = np.where(ghost[:, None], own, snap)          # every pixel one of the two flat tones; the ghost's smudges the tone of their side
        crease = np.where(dark[1540:1625, x])[0] if 1125 <= x < 1214 else []
        below = rows > (1540 + crease.max() if len(crease) else 1603)    # below the heel of the palm (its crease): the wrist, in the light tone
        out[rows[below], x, :3] = tone[0]                                # only (it had a dark band with a ruled left edge and a square corner)
    for x in range(1200, 1262):                                          # the heel of the palm to its outline, snapped to the two tones as well
        rows = np.arange(1480, 1600)[inside[1480:1600, x] & ~near_line[1480:1600, x]]
        px = out[rows, x, :3]
        d0, d1 = np.abs(px - tone[0]).max(axis=1), np.abs(px - tone[1]).max(axis=1)
        out[rows, x, :3] = np.where((d0 <= d1)[:, None], tone[0], tone[1])
    # a dark zigzag smudge (a left-over) in the shaded right side of the cuff, just under its rim: that side's own ochre
    ochre = np.median(rgb[1800:1880, 1270:1320].reshape(-1, 3), axis=0)
    for x in range(1246, 1337):
        ys = np.where(dark[1640:1800, x])[0]
        top = 1640 + (ys.max() if len(ys) else 0) + 4
        rows = np.arange(top, 1830)[~near_line[top:1830, x]]
        px = out[rows, x, :3]
        smudge = (np.abs(px - ochre).max(axis=1) > 0.03) & (px[:, 0] > 0.6) & (px[:, 2] < 0.2)
        out[rows[smudge], x, :3] = ochre
    # the inside of the sleeve below the cuff's rim, its own flat yellow again (the ghost of her scarf goes)
    yellow = np.median(rgb[2250:2390, 900:1150].reshape(-1, 3), axis=0)
    rim = np.full(w, h, int)
    for x in range(640, 1236):                      # the lowest dark row of the rim's front edge in each column
        ys = np.where(dark[1690:1800, x])[0]
        rim[x] = 1690 + ys.max() + 3 if len(ys) else 1790
    paint = (xx >= 640) & (xx < 1236) & (yy > rim[None, :].repeat(h, 0)) & (yy >= 1740)
    paint = cv2.GaussianBlur(paint.astype(np.float32), (0, 0), 0.8)
    out[..., :3] = out[..., :3] * (1 - paint[..., None]) + yellow * paint[..., None]
    out = np.concatenate([out, np.repeat(out[-1:], 900, axis=0)], axis=0)       # the sleeve runs on below the drawing
    img = (np.clip(np.dstack([out[..., 2], out[..., 1], out[..., 0], out[..., 3]]), 0, 1) * 255 + 0.5).astype(np.uint8)
    cv2.imwrite(str(ROOT / "work" / v.film / "art" / f"{CUT}.png"), img)
    print("made", CUT)


_hand_cut()
_HAND = None
HAND_S = 0.50                       # the hand's size in the frame (drawing pixels to frame pixels): a little bigger than her face, it is at the lens
HAND_TIP = (924, 222)               # the tip of its middle finger, in the drawing
HAND_AT = (1000, 740)               # where that tip is held in the frame: beside his chin, so that his face stays in view, unchanged
HAND_TURN = 10.0                    # the fingers lean out to the right: the sleeve runs down into her own shoulder (a watching model
                                    # took the hand for his when its sleeve went off the right edge beside his jacket), the thumb stays off her face


def lens_hand(st, t, T):
    """Her hand flies up from below the frame in front of the lens (0.16 s, overshooting a little), trembles there
    and stays to the end."""
    global _HAND
    up = v.t("L4-0.10")
    if T < up - 0.01:
        return
    if _HAND is None:
        raw = read(v.art / CUT)
        _HAND = premul(raw)
    tt = on_twos(T)
    rise = ease(tt, up, up + 0.14, 1500.0, -26.0, "out") + ease(tt, up + 0.14, up + 0.30, 0.0, 26.0, "io")   # past its place by a little, and back
    much = v.t("L4@much")
    jolt = math.sin((tt - much) * 26) * 10 * math.exp(-(tt - much) * 8) if tt >= much else 0.0       # a small push on "much"
    a = math.radians(HAND_TURN)                     # it comes up along her forearm, out of her shoulder
    x = HAND_AT[0] - rise * math.sin(a) + wobble(tt, 21.0, 1.7) * 4
    y = HAND_AT[1] + rise * math.cos(a) + wobble(tt, 22.0, 1.5) * 5 - abs(jolt) * 0.6
    h, w = _HAND.shape[:2]
    st.put(_HAND, x, y, scale=HAND_S, turn=HAND_TURN + wobble(tt, 23.0, 1.3) * 0.8 + jolt * 0.15,
           anchor=(HAND_TIP[0] / w, HAND_TIP[1] / h), depth=1.0)


# ---- one shot: the two of them, the far green with its flag above her head
v.shot("start", bg=BG, luna=LUNA_AT, timo=TIMO_AT, front=lens_hand)

# ---- her face
v.faces(("start", None),                  # beaming, her open hand presenting him
        ("L3-0.12", "turn"),              # she turns to him: "Do the big one." (and waits for it, a small closed smile)
        ("L4-0.08", "guilty"),            # his big one: she winces, ducks and steals a look at him
        ("L4.end+0.30", "defend"))        # and then at us, caught: you saw that
v.tilt(L1=3.2, L3=2.2, L4=-2.8)
v.blinks(luna=["L1@timo-0.03"],                     # in her breath between "golf!" and "Timo", not on a word
         timo=["L1@golf+0.25", "look+0.42"])        # he blinks once in his big smile, and that is all that moves

# ---- his eyes
v.eyes(("start", "lens"),                 # the least smiling face in sport, flat into the lens
       ("L3-0.02", None),                 # she turns to him: he looks down at her
       ("look", "lens"))                  # the music has stopped: his big one. It is the same face.

# ---- her free hand: the open hand, presenting him
#      a little right of where it was and 10 px lower (it was lift=60, dx=10): the dark-green end of her blowing scarf
#      then goes behind the sleeve instead of framing the wrist like a dark glove (only a few fringes peep out at the
#      cuff now and then); lower than this and the palm drops out of the 4:5 version (its bottom is y 1530)
v.piece("hand", up=-0.6, down="T2.end+0.12", lift=50, dx=45, shake="L1@timo")

# ---- the words on screen
v.captions(
    split={"L1": [("We’re at the golf!", None), ("Timo, smile!", "L1@timo")]},
    hold={"L4": ("Okay. ", "Too much.", "L4@too")},
    place={"L4": (330, 1424)},            # clear of her hand at the lens
    off={"L4": "plate-0.05"})             # and up until the plate comes
v.plate(x=352, y=300, size=0.74)          # he is still standing: the plate beside his head, over the sky

if __name__ == "__main__":
    v.main()
