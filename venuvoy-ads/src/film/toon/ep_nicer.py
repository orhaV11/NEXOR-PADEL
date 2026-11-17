"""The picture of the short "It Is Nicer" (vlog 11, "nicer"): one shot in the street outside an arena on game night.

    python3 src/film/toon/ep_nicer.py --sheet        a contact sheet of the whole film (work/nicer/sheet.jpg)
    python3 src/film/toon/ep_nicer.py --stills 0.1 "T2+0.9" "L3+0.5" "T4@put" "sink+0.35" "plate+0.8"
    python3 src/film/toon/ep_nicer.py                the film (ads/21-vlog/venuvoy-vlog-11-nicer-en_...mp4)

Run ep_nicer_sound.py first: every time below is read from its timeline.
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import Talk, align, clamp, ease, key_out, on_twos, read, wobble  # noqa: E402
from vlog import ROOT, Vlog, wind  # noqa: E402

# an evening street with a light wind: her blowing hair, him without the blade of grass. The words the joke rests on
# get their mouth shapes by hand.
v = Vlog("nicer", "ads/21-vlog/venuvoy-vlog-11-nicer", luna="wind", timo="timo_clean",
         said={"basketball": "A A C C C O O O", "night": "A A C", "look": "O C", "got": "O C", "thats": "A C", "other": "A C", "teams": "C C M C"})
# Her "It's nicer." is a plain, pleased fact, said long, to him: on the turned head the open mouth is a shout, so the
# word gets her grin first and the small mouth after (the kit gives one set of shapes per word for both speakers).
v.talk["L3"] = Talk(v.LINE["L3"]["words"], ROOT / v.LINE["L3"]["wav"], have=duo.LUNA_MOUTHS, quiet=0.10, said={"its": "C", "nicer": "E E C C C"})
# His last line is one loud word ("is") and the rest low, so the kit, which opens a mouth by loudness, gave him one
# small shape all through the punchline. Set by hand, twelve steps a second from the start of the line:
#                    It  .  .  -  -  IS .  .  .  .  ni .  ce .  r  .  -  -  -  Put it a  w  ay .  -
v.talk["T4"].seq = [None if x == "-" else x for x in "C C C - - A A C C C A A C C C C - - - O C C O A A - - -".split()]

# drawing, a point in it (the front of the crowd, by the doors), where that lands in the frame, size: the lit doors
# fill the upper left, the crowd in green and cream runs across the frame above her head and behind him.
BG = ("bg_arena_1", (700, 1150), (330, 640), 1.0)
LUNA_AT = (362, 1560, 0.86, 0.0)
TIMO_AT = (884, 1036, 0.84)
# The painter drew every one of the crowd with a pen line, as sharp as the two in front (the watching model saw
# "copy-pasted" figures): the painting is put a little out of focus, so that the crowd is soft shapes in green and
# cream and the two of them stand clear of it. (The kit keeps each background it has loaded; this one is handed to it.)
_street = duo.background(v.art, BG[0])
_street.img = cv2.GaussianBlur(_street.img, (0, 0), 2.2)
v._bg[BG[0]] = _street


# ---- something only this film has: the other team's scarf, held up in her fist (drawn by a hook).
#      The drawing shows her whole arm, bent, with the elbow in the picture; a piece that has to go straight down out
#      of the frame needs a forearm that comes up from below, like the rig's phone. So the drawing is taken apart:
#      the scarf (everything pink or brown, with its outline), the fist and forearm (down to where the elbow begins),
#      and under both a plain sleeve that runs on down out of the picture. The scarf is its own layer so that its
#      hanging ends can blow a little (three states, changed the way the rig changes her hair in the wind).
Y_SWAY, Y_CUT, Y_ENDS, Y_FRINGE = 1420, 1850, 2140, 2300     # rows of her drawing: the scarf hangs free from; the elbow begins; the scarf's ends; its fringe ends
SLEEVE, INK = (254 / 255, 189 / 255, 4 / 255), (0.075, 0.06, 0.065)


def _scarf_layers():
    base = read(v.art / "luna_w1")[..., :3]
    new, _ = align(base, read(v.art / "luna_scarf_1")[..., :3], None)
    h, w = new.shape[:2]
    r, g, b = new[..., 0], new[..., 1], new[..., 2]
    rgba = key_out(new, lo=0.2, hi=0.5)                 # the candy pink is a little magenta itself: a key that leaves it whole
    inside = cv2.erode((rgba[..., 3] > 0.99).astype(np.uint8), np.ones((7, 7), np.uint8)) > 0
    rgba[..., :3] = np.where(inside[..., None], new, rgba[..., :3])     # ... and its true colour (the key takes magenta out of the edges only)
    box = np.zeros((h, w), bool)
    box[1040:2345, 1090:] = True
    fresh = box & (rgba[..., 3] > 0.5) & ((key_out(base)[..., 3] < 0.5) | (np.abs(new - base).max(axis=2) > 0.12))   # drawn new: not her hair, coat or own scarf
    dark = new.max(axis=2) < 0.25
    pink = (r > 0.8) & (b - g > 0.03) & (g > 0.45) & (b > 0.55)
    brown = (r > 0.2) & (r < 0.47) & (g < 0.26) & (b < 0.24) & (r - g > 0.08)
    cloth = cv2.morphologyEx(((pink | brown) & fresh).astype(np.uint8), cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    yy, xx = np.mgrid[0:h, 0:w]
    near = cv2.dilate(cloth, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (27, 27))) > 0
    close = cv2.dilate(cloth, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))) > 0
    line = dark & box & ((near & fresh) | close)                        # the outline round the cloth (not the lines of her hair and own scarf that pass close to it)
    line &= ~((yy >= 2020) & (yy <= 2095) & (xx >= 1383) & (xx <= 1496))             # ... nor the ends of the elbow's outline between the two halves
    line &= ~((yy >= 1560) & (yy <= 2010) & (xx < 1161 - (yy - 1700) * 0.087))       # ... nor those of the upper arm's, left of the left half
    scarf = cv2.morphologyEx(((cloth > 0) | line).astype(np.uint8), cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
    n, lab, stats, _ = cv2.connectedComponentsWithStats(scarf)
    scarf = np.isin(lab, [i for i in range(1, n) if stats[i, cv2.CC_STAT_AREA] > 3000])
    n, lab, stats, _ = cv2.connectedComponentsWithStats((~scarf).astype(np.uint8))   # pinholes in it are closed
    scarf |= np.isin(lab, [i for i in range(1, n) if stats[i, cv2.CC_STAT_AREA] < 500])
    arm = fresh & ~scarf & (((yy < 1400) & (xx >= 1300)) | ((yy >= 1400) & (yy < 1740) & (xx >= 1374)) | ((yy >= 1740) & (yy < Y_CUT) & (xx >= 1350)))
    arm = cv2.morphologyEx(arm.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    n, lab, stats, _ = cv2.connectedComponentsWithStats(arm)
    arm = lab == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))        # the fist and the forearm: one piece

    def layer(mask):
        grown = cv2.dilate(mask.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        m = cv2.GaussianBlur((mask | (grown & (rgba[..., 3] < 0.98))).astype(np.float32), (0, 0), 0.8)   # with its soft edge against the background, not a rim of what lay beside it
        out = rgba.copy()
        out[..., 3] *= m
        out[..., :3] *= out[..., 3:]
        return np.concatenate([out, np.zeros((duo.L_PAD, w, 4), np.float32)], axis=0)      # Luna's own canvas, premultiplied

    # the sleeve under them: hidden support under the scarf's right half from the cuff down; the whole width of the
    # forearm from where the elbow was; its own outline where it comes out below the scarf's ends
    H = h + duo.L_PAD
    sl = np.zeros((H, w, 4), np.float32)
    sl[Y_SWAY - 20:1740, 1450:1586] = (*SLEEVE, 1.0)
    sl[1740:Y_ENDS, 1330:1586] = (*SLEEVE, 1.0)
    sl[Y_ENDS:, 1372:1591] = (*INK, 1.0)
    sl[Y_ENDS:, 1382:1581] = (*SLEEVE, 1.0)
    sl = cv2.GaussianBlur(sl, (0, 0), 0.8)
    return layer(scarf), layer(arm), sl


def _scarf_pieces():
    """The piece in three states of the wind: the scarf's hanging ends pushed sideways, more the lower they hang."""
    scarf, arm, sleeve = _scarf_layers()
    H, w = scarf.shape[:2]
    yy, xx = np.mgrid[0:H, 0:w].astype(np.float32)
    hang = np.clip((yy - Y_SWAY) / (Y_FRINGE - Y_SWAY), 0, 1.2) ** 1.4
    out = {}
    for state, push in ((None, 0.0), ("1", 15.0), ("2", -9.0)):
        s = cv2.remap(scarf, xx - push * hang, yy, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0) if push else scarf
        img = sleeve.copy()
        for top in (arm, s):
            img = top + img * (1 - top[..., 3:])
        out[state] = cv2.resize(img, None, fx=v.s_luna, fy=v.s_luna, interpolation=cv2.INTER_AREA)
    return out


SCARF = _scarf_pieces()
LIFT, DX = 150, 0                                      # how much higher than drawn she holds it (pixels of the frame at size 1)
WON = "T4@nicer+0.30"                                  # "It is nicer." has landed: she turns to us with it, beaming (and then he goes on)


def scarf(st, t, T):
    t2 = on_twos(T)
    x0, y0, k, lean = LUNA_AT
    dx, dy, turn, sx, sy = v.luna_move(T)                                       # it is part of her: it sways and pops with her body
    hit = v.t("L1@look")
    sh = math.sin((t2 - hit) * 30) * 14 * math.exp(-(t2 - hit) * 7) if t2 >= hit else 0.0      # "Look": she gives it one shake
    y = -22 * ease(t2, v.t("L3-0.05"), v.t("L3+0.18"), 0.0, 1.0, "back")       # "It's nicer": up a little, to him
    won = v.t(WON)
    y -= 30 * ease(t2, won, won + 0.16, 0.0, 1.0, "back")                       # he has agreed: higher still, to us
    sh += math.sin((t2 - won) * 30) * 10 * math.exp(-(t2 - won) * 7) if t2 >= won else 0.0
    a, b = v.t("sink"), v.t("sink.end")
    y += -16 * ease(t2, a - 0.14, a, 0.0, 1.0, "io") + 1026 * clamp((t2 - a) / (b - a))   # a breath up, then down out of the frame at an even speed
    if T >= b + 0.1:
        return
    st.put(SCARF[wind(T, 2.3)], x0 + dx + DX * k + sh + wobble(t2, 11.0, 2.2) * 2.5, y0 + dy - LIFT * k + y - abs(sh) * 0.6 + wobble(t2, 12.0, 1.9) * 3, scale=k,
           turn=turn + lean + sh * 0.12 + wobble(t2, 13.0, 1.6) * 0.7, sx=sx, sy=sy, anchor=duo.L_ANCHOR, depth=1.0)


# ---- one shot: the two of them, the scarf, and the street of green and cream behind
v.shot("start", bg=BG, luna=LUNA_AT, timo=TIMO_AT, front=scarf)

# ---- her face
v.faces(("start", None),                  # sunny, to us: look what I got
        ("T2@other+0.05", "read_b"),      # he has spoken: she looks down at it, pleased with it, mouth shut
        ("L3-0.10", "turn"),              # and turns to him: a simple fact
        (WON, None),                      # he agrees: she beams at us (told you) ... and the smile stays on while he finishes
        ("sink+0.08", "guilty"))          # it goes down: a sideways look at him, a crooked smile
v.tilt(L1=3.2, L3=2.2)
_state = v.luna_state


def _beam(T):
    """She is not speaking when she turns back to us, so the kit keeps her mouth shut: here it is her widest grin,
    until "away" has reached her (then the small smile that was under it, which stays on)."""
    st = _state(T)
    if v.t(WON) + 0.04 <= T < v.t("T4@away+0.10") and st.get("1face") is None:
        st["3mouth"] = "E"
    return st


v.luna_state = _beam
v.blinks(luna=[0.92, "T2+0.30", "T4.end+0.17", "plate+0.75"],     # one blink in the frozen smile after "Put it away."
         timo=[1.25, "L3+0.25"])                                   # none at the end: his look at us does not blink (the watching model took a blink there for shut eyes)

# ---- his eyes: on her and on the thing; narrowed while he weighs it; on her to say it; on us when it is gone
v.eyes(("start", None),
       ("L3.end+0.06", "narrow"),
       ("T4-0.04", None),
       ("sink.end-0.16", "lens"))

# ---- the words on screen: under her chin, left of the scarf
CAP = (345, 1430)
v.captions(
    place={"L1": CAP, "T2": CAP, "L3": CAP, "T4": CAP},
    hold={"T4": ("It is nicer.\n", "Put it away.", "T4@put")},                    # the order arrives when he gives it
    text={"L1": "Basketball night!\nLook what I got!", "T2": "That’s the\nother team’s.", "L3": "It’s nicer."},
    off={"T4": "plate-0.06"})                                                     # the punchline stays up while nobody moves and while the scarf goes
v.plate(y=320)

if __name__ == "__main__":
    v.main()
