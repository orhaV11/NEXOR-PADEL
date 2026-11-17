"""The picture of the sketch "The Look" (vlog 03, "look"): behind the basket of an indoor arena, two shots.

    python3 src/film/toon/ep_look.py --sheet        a contact sheet of the whole film (work/look/sheet.jpg)
    python3 src/film/toon/ep_look.py --stills 0.1 "T2+0.4" "stare+0.4" "L6+0.3" "look+0.5" "plate+1.0"
    python3 src/film/toon/ep_look.py                the film (ads/21-vlog/venuvoy-vlog-03-look-en_...mp4)

Run ep_look_sound.py first: every time below is read from its timeline.

The whole stand waves at the shooter; Timo does not move. He looks at her once (she says sorry), then at us (the
shooter misses). Two things are this film's own, drawn by hooks: the sticks the crowd waves (behind the two of them)
and her foam finger (in front), which she pumps, drops, brings up as a shield and lets sink away.
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import ease, on_twos, read  # noqa: E402
from vlog import Vlog  # noqa: E402

# indoors: her calm hair, him without the blade of grass. The words the joke rests on get their mouth shapes by hand.
v = Vlog("look", "ads/21-vlog/venuvoy-vlog-03-look", luna="calm", timo="timo_clean",
         said={"basketball": "M A C M O C", "behind": "M C A C", "basket": "M A A C", "everybody": "C M A E", "distracts": "C A C", "shooter": "O C",
               "dont": "O C", "wave": "O A C", "then": "C", "do": "O", "something": "A M C C", "ill": "A C", "give": "C", "him": "C M",
               "look": "C O C", "sorry": "C O E", "exactly": "C C C", "missed": "M E C C", "knows": "O C"})

# drawing, a point in it (the liveliest patch of waving sticks), where that lands in the frame, size. At these sizes the
# dark roof and the padded barrier are out of the frame: the two of them stand IN the stand, the crowd all round them.
WIDE = ("bg_arena_1", (400, 900), (300, 500), 1.15)
NEAR = ("bg_arena_1", (400, 900), (250, 400), 1.44)        # a quarter closer


# ---- something only this film has: the stand WAVES. Pale sticks like the painted ones, wagging on twos behind the two
#      of them (at the background's own depth and softness); twelve drawings, a loop of one second.
def _sticks(n_phases=12, half=2):
    paint = read(v.art / WIDE[0])[..., :3]
    lively = paint[780:1250, 0:740].reshape(-1, 3)
    pale = lively[(lively.max(axis=1) > 0.86) & (lively.max(axis=1) - lively.min(axis=1) < 0.14)]
    colour = np.median(pale, axis=0) if len(pale) > 500 else np.array([0.95, 0.93, 0.88])
    rng = np.random.default_rng(5)
    sites = []
    for _ in range(4000):
        x, y = rng.uniform(20, 1500), rng.uniform(800, 1560)
        left = x < 750 - (y - 895) * 0.76 - 45 and y < 1230            # the bank of seats on the left of the stairs
        right = x > 1046 - (y - 750) * 0.70 + 70 and y < 1380          # and the one on the right
        behind_him = 715 < x < 995 and y < 900                          # no stick may stand up out of his hair
        if (left or right) and not behind_him and all((x - a) ** 2 + (y - b) ** 2 > 78 ** 2 for a, b, *_ in sites):
            sites.append((x, y, rng.uniform(72, 118), rng.uniform(-24, 24), rng.uniform(13, 27), int(rng.choice((2, 3))), rng.uniform(0, 6.28)))
        if len(sites) >= 46:
            break
    y0, y1 = 620, 1600                                                  # the strip of the painting the sticks live in
    out = []
    for ph in range(n_phases):
        mask = np.zeros(((y1 - y0) // half, 1536 // half), np.float32)
        for x, y, length, lean, swing, cycles, phase in sites:
            for back, strength in ((1, 0.30), (0, 1.0)):               # the drawing before, faintly: a smear, as the painter did
                a = math.radians(lean + swing * math.sin(2 * math.pi * cycles * (ph - back) / n_phases + phase))
                tip = (x + math.sin(a) * length, y - math.cos(a) * length)
                layer = np.zeros_like(mask)
                cv2.line(layer, (int(x / half), int((y - y0) / half)), (int(tip[0] / half), int((tip[1] - y0) / half)), 1.0, 6, cv2.LINE_AA)
                mask = np.maximum(mask, layer * strength)
        mask = cv2.GaussianBlur(mask, (0, 0), 1.3) * 0.82
        out.append(np.dstack([colour[None, None, :] * mask[..., None], mask]).astype(np.float32))
    return out, y0, half


def _no_ears(box=(715, 630, 880, 860)):
    """The painter put four pale sticks exactly where the top of Timo's hair stands (two in a V over it in the first
    shot, two that peep out of it in the second): they read as ears on him. They are painted out (what is behind
    them grown in from their edges) on a small patch that is laid over the painting."""
    x0, y0, x1, y1 = box
    rgb = read(v.art / WIDE[0])[y0:y1, x0:x1, :3]
    sticks = np.zeros(rgb.shape[:2], np.uint8)
    for line in (((778, 660), (790, 700), (807, 738)), ((839, 650), (828, 690), (824, 724)),      # the V
                 ((745, 750), (760, 800), (781, 840)), ((847, 756), (838, 790), (832, 822))):     # the two below it
        cv2.polylines(sticks, [np.array([(x - x0, y - y0) for x, y in line], np.int32)], False, 1, 27, cv2.LINE_8)
    fixed = cv2.inpaint((rgb[..., ::-1] * 255 + 0.5).astype(np.uint8), sticks * 255, 9, cv2.INPAINT_TELEA)[..., ::-1].astype(np.float32) / 255
    soft = cv2.GaussianBlur(fixed, (0, 0), 2.5)                         # as soft as the painting
    alpha = cv2.GaussianBlur(cv2.dilate(sticks, np.ones((5, 5), np.uint8)).astype(np.float32), (0, 0), 3.0)
    alpha[:6], alpha[-6:], alpha[:, :6], alpha[:, -6:] = 0, 0, 0, 0
    return np.dstack([soft * alpha[..., None], alpha]).astype(np.float32), (x0, y0)


STICKS, S_Y0, S_HALF = _sticks()
PATCH, PATCH_AT = _no_ears()


def crowd(st, t, T):
    name, src, at, k = v.shot_at(T)["bg"]
    st.put(PATCH, at[0] + (PATCH_AT[0] - src[0]) * k, at[1] + (PATCH_AT[1] - src[1]) * k, scale=k, anchor=(0.0, 0.0), depth=0.45)
    ph = int(math.floor(T * 12 + 1e-6)) % len(STICKS)
    st.put(STICKS[ph], at[0] + (0 - src[0]) * k, at[1] + (S_Y0 - src[1]) * k, scale=k * S_HALF, anchor=(0.0, 0.0), depth=0.45)


# ---- her foam finger: its own drawing (a redraw of the calm Luna with only the arm added: the forearm stands clear of
#      her body all the way down, so it is a thing of its own) and its own movement, drawn by a hook in front of her.
#      It stands a little nearer to her than it was drawn, its sleeve running into her coat at the bottom of the frame:
#      the watching model took a foam finger that stood in front of Timo for HIS.
ARM = duo.arm(v.art, "luna_foam", v.s_luna, (1140, 560, 1792, 2400), "luna_calm2")
PIVOT = (1480, 2330)                                    # the middle of the sleeve where it leaves the frame (pixels of her drawing): it turns about this
NEARER = -45.0                                          # frame pixels toward her
UP, SHIELD, GONE = -70.0, -150.0, 1180.0                # how far below its drawn place it is (frame pixels): pumping; held up; out of the frame


def _foam_pose(T):
    """(pixels below its drawn place, turn in degrees) of the foam finger at the time T. Her pumping and trembling are
    drawn on twos like her; the three fast moves (the drop, the shield, the sink) on ones, so none jumps."""
    t = on_twos(T)
    drop, shield = v.t("L3-0.04"), v.t("L6-0.06")
    calm, sink0, sink1 = v.t("L6@what-0.20"), v.t("L6@what+0.30"), v.t("L6.end+0.15")
    if T < drop:                                                         # she pumps it at the shooter, hard; "I don't wave." stops it
        amp = 1.0 - ease(t, 1.0, 1.6, 0.0, 0.2, "io") - ease(t, v.t("T2+0.10"), v.t("T2+0.45"), 0.0, 0.8, "io")   # hardest in the first second
        w = 2 * math.pi * 3.2 * t
        return UP - 42.0 * amp * math.sin(w), 2.0 + 2.6 * amp * math.sin(w + 1.3)
    if T < shield:                                                       # she rounds on him: it drops right out of the frame (a lone finger
        return ease(T, drop, drop + 0.45, UP, GONE, "io"), ease(T, drop, drop + 0.45, 2.0, 6.0, "io")   # left at the edge read as HIS objection)
    if T < sink0:                                                        # the look: it shoots up from below between them, a shield, trembles,
        k = ease(T, shield, shield + 0.25, 0.0, 1.03, "out") - ease(T, shield + 0.25, shield + 0.40, 0.0, 0.03, "io")    # and is still
        shiver = 0.8 * min(1.0, max(0.0, t - shield) / 0.3) * (1.0 - ease(t, calm - 0.25, calm, 0.0, 1.0, "io"))         # before it goes
        return GONE + (SHIELD - GONE) * k, 6.0 + (0.5 - 6.0) * min(k, 1.0) + math.sin(2 * math.pi * 5.0 * t) * shiver
    p = min(1.0, (T - sink0) / (sink1 - sink0))                          # "...What did I do?": she lowers it out of the frame at an even
    return SHIELD + (GONE - SHIELD) * p, 0.5 + 4.5 * p                   # speed; "Exactly." plays on a clean frame


def foam(st, t, T):
    if T >= v.t("cut1") or v.t("L3-0.04") + 0.45 <= T < v.t("L6-0.06") or T >= v.t("L6.end+0.15"):
        return                                                           # out of the frame
    x0, y0, k, lean = v.shot_at(T)["luna"]
    dx, dy, turn, sx, sy = v.luna_move(T)                                # it is hers: it sways and pops with her body
    s = v.s_luna * k
    a = math.radians(turn + lean)
    px, py = (PIVOT[0] - 663) * s, (PIVOT[1] - 1680) * s                 # from the knot of her scarf to the pivot
    x, y = x0 + dx + px * math.cos(a) - py * math.sin(a), y0 + dy + px * math.sin(a) + py * math.cos(a)
    anchor = (PIVOT[0] / duo.L_SIZE[0], PIVOT[1] / duo.L_SIZE[1])
    off, ang = _foam_pose(T)
    st.put(ARM, x + NEARER, y + off, scale=k, turn=turn + lean + ang, sx=sx, sy=sy, anchor=anchor, depth=1.0)


# ---- two shots: the two of them in the waving stand; then a quarter closer for the look (a fifth closer was not
#      seen as a cut by the watching model). In the closer shot he grows more than she does (0.92 to 1.26, as the script
#      has it: "Timo larger") and she sits a little lower: the look is the picture, not her caught face
v.shot("start", bg=WIDE, luna=(362, 1545, 0.92, -0.6), timo=(872, 960, 0.92), behind=crowd, front=foam)
v.shot("cut1", bg=NEAR, luna=(330, 1540, 1.16, 1.0), timo=(872, 1060, 1.26), behind=crowd, front=foam)    # his hair top at y 216: inside the 4:5 version (from 180)

# ---- her face
v.faces(("start", None),                  # sunny, shouting it over the crowd
        ("T2+0.30", "guilty"),            # "I don't wave.": a look at him out of the corner of her eye
        ("L3-0.10", "turn"),              # she rounds on him
        ("T4+0.15", None),                # "I'll give him the look.": she shares it with us, amused
        ("L5-0.10", "turn"),              # and asks him
        ("L6-0.08", "defend"),            # the look has landed: back to us, caught
        ("L6+0.80", "guilty"),            # a glance at him in the pause
        ("L6@what-0.05", "defend"),       # "...What did I do?" to us
        ("T7+0.22", "guilty"),            # "Exactly.": another look at him
        ("cut1", "defend"),               # the shot: big eyes on the court (on us)
        ("L8-0.14", "turn"))              # she turns to him: "He missed!", and stays looking up at him, a small smile, through
                                          # the name (the script's end picture; turned back to us, caught, she read as cowed)
v.tilt(L1=3.0, L3=2.4, L5=2.0, L6=-3.0, L8=2.2)
v.blinks(luna=[1.25, 3.30, "T2+0.56", "T4+0.55", "L6.end+0.14", "groan+0.24", "plate+1.05"],
         timo=["T4-0.30", "L6+1.55", "T9.end+0.50", "plate+1.45"])       # never during a look (his eyes are shut until she pushes him)

# ---- his eyes: the look into the lens is kept for the payoff. Shut and serene in the waving stand; narrowed when she
#      pushes him (he is working it out); down on her once; then, for the first time, on us
v.eyes(("start", "shut"),
       ("L3+0.30", "narrow"),             # "Then do something!"
       ("stare", None),                   # "What look?": his eyes come down on her, and stay
       ("look", "lens"))                  # after the cut they lift to us; the music has stopped

# the kit moves his head when he speaks and when his eyes come to us, not when they go down to her: for the look at her
# his head dips a little toward her and stays there until the cut (his own head move, wrapped round the kit's)
_kit_head = v.timo_head


def _head_with_the_look(T):
    out = list(_kit_head(T))              # (turn in degrees, shift x, shift y, lean)
    k = ease(on_twos(T), v.t("stare"), v.t("stare+0.16"), 0.0, 1.0, "back") if T < v.t("cut1") else 0.0
    out[0] -= 2.6 * k
    out[2] += 5.0 * k
    return tuple(out)


v.timo_head = _head_with_the_look

# ---- the words on screen: under her chin, left of the foam finger
CAP = (410, 1427)
v.captions(
    place={i: CAP for i in ("L1", "T2", "L3", "T4", "L5", "L6", "T7")},
    split={"L1": [("Basketball!", None), ("Behind the basket,", "L1@behind"), ("everybody distracts\nthe shooter!", "L1@everybody")],
           "L6": [("Sorry!", None), ("…What did I do?", "L6@what")]},
    text={"T2": "I don’t wave.", "T4": "I’ll give him\nthe look."},
    on={"L1": "L1-0.12", "T2": "T2-0.12", "T4": "T4-0.30"},               # his short lines are up a moment early: time to read them
    off={"L1": "T2-0.14", "L3": "T4-0.32", "T9": "plate-0.06"})            # the last word stays up through the look
v.plate(x=345, y=300, size=0.68)          # beside his head, above hers: he is standing at the end (clear of his hair by about 60 px)

if __name__ == "__main__":
    v.main()
