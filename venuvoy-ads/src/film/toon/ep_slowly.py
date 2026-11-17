"""The picture of the sketch "Really Slowly" (vlog 04, "slowly"): the top row of a tennis stand, a crisp packet.

    python3 src/film/toon/ep_slowly.py --sheet        a contact sheet of the whole film (work/slowly/sheet.jpg)
    python3 src/film/toon/ep_slowly.py --stills 0.1 "crinkle+0.8" "shush+0.2" "sink+0.4" "L8+0.6" "plate+1.0"
    python3 src/film/toon/ep_slowly.py                the film (ads/21-vlog/venuvoy-vlog-04-slowly-en_...mp4)

Run ep_slowly_sound.py first: every time below is read from its timeline.
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import clamp, ease, on_twos, smooth, wobble  # noqa: E402
from vlog import Vlog, held  # noqa: E402

# a still sunny afternoon: her calm hair, him without the blade of grass. They whisper: small mouths, set by hand for
# the words the jokes rest on (a whisper has no loud vowels, so the automatic shapes come out as one held half-open mouth).
v = Vlog("slowly", "ads/21-vlog/venuvoy-vlog-04-slowly", luna="calm", timo="timo_clean",
         said={"were": "O C", "at": "C M", "tennis": "C M C", "dont": "C O C", "open": "O M C", "im": "C M", "starving": "C A M C",
               "wait": "O C", "for": "M O", "clapping": "C M C", "ill": "C", "do": "O", "really": "C M C", "slowly": "C O O C",
               "luna": "O C A", "no": "O", "not": "C O C", "with": "O C", "her": "C", "have": "C M", "same": "C M", "scarf": "C A M",
               "coincidence": "O C M C M C"})

# drawing, a point in it (the middle of the court), where that lands in the frame, size: the court sits upper left in every shot
COURT = (430, 960)
BG_A = ("bg_stand_1", COURT, (330, 560), 0.92)
BG_B = ("bg_stand_1", COURT, (265, 440), 1.06)
BG_C = ("bg_stand_1", COURT, (300, 560), 0.85)
LUNA_A, TIMO_A = (366, 1552, 0.96, -1.0), (892, 960, 0.96)       # the two of them, the packet in front of his jacket
LUNA_B, TIMO_B = (318, 1495, 1.11, 1.0), (886, 880, 1.11)        # sixteen per cent closer: the packet is the subject
LUNA_C, TIMO_C = (372, 1450, 0.92, 0.0), (900, 1100, 0.92)       # wider: two scarves side by side, a head between them in height, the top free for the plate

# ---- something only this film has: the crisp packet in her free hand (drawn by a hook, not by v.piece: it is there from
#      the first frame, it trembles while she opens it "really slowly", and it goes down slowly, seen leaving).
#      The drawing is a redraw of the calm Luna with the arm added; the arm joins her coat along the whole edge of her
#      body, so it is never lifted: it stays where it was drawn, and when it leaves it slides ALONG that edge.
ARM = duo.arm(v.art, "luna_packet", v.s_luna, (990, 700, 1792, 2400), "luna_calm2")
EDGE = (0.32, 0.95)                                                # the direction of the edge of her coat, down and outward


def _crumpled(n=4, amp=6.0):
    """The packet while it is being opened: the same drawing with the foil pushed about a little, four ways, shown in
    turn twelve times a second (its outline and its shine boil; the hand below it and the sleeve stay as drawn)."""
    s = v.s_luna
    h, w = ARM.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    foil = 1 - smooth(1290 * s, 1430 * s, ys)                      # all of it above her fingers, fading out where she grips it
    rng = np.random.default_rng(4)
    out = []
    for _ in range(n):
        a, b, c, d = rng.uniform(0, 6.28, 4)
        dx = amp * foil * (np.sin(ys / 23 + a) * 0.6 + np.sin((xs + ys) / 37 + b) * 0.4)
        dy = amp * foil * (np.sin(xs / 27 + c) * 0.6 + np.sin((xs - ys) / 41 + d) * 0.4)
        out.append(cv2.remap(ARM, (xs + dx).astype(np.float32), (ys + dy).astype(np.float32), cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0))
    return out


CRUMPLED = _crumpled()


def packet(st, t, T):
    c0, c1, s0, s1 = v.t("crinkle"), v.t("crinkle.end"), v.t("sink"), v.t("sink.end")
    if T >= s1 + 0.1:
        return
    t2 = on_twos(T)
    x0, y0, k, lean = v.shot_at(T)["luna"]
    dx, dy, turn, sx, sy = v.luna_move(T)                                    # it is part of her: it sways and pops with her body
    hit = 0.14                                                              # "look what I have": one waggle as the film opens
    sh = math.sin((t2 - hit) * 30) * 13 * math.exp(-(t2 - hit) * 7) if t2 >= hit else 0.0
    open_ = held(t2, c0 + 0.10, c1 - 0.06, 0.12, 0.10)                       # she is opening it: it trembles all the way
    n = int(round(t2 * 12))
    jx, jy, jr = ((n * 37) % 11 - 5) * 1.25 * open_, ((n * 53) % 7 - 3) * 1.1 * open_, ((n * 29) % 5 - 2) * 0.22 * open_
    pull = ease(t2, c0, c1, 0.0, 1.0, "io") * 10                             # and comes a little toward her as it goes
    slide = clamp((T - s0) / (s1 - s0)) * 1090 * k if T >= s0 else 0.0       # it goes down along the edge of her coat, at an even speed
    px = x0 + dx + sh + jx - pull + slide * EDGE[0] + wobble(t2, 11.0, 2.2) * 2.0
    py = y0 + dy - abs(sh) * 0.5 + jy + slide * EDGE[1] + wobble(t2, 12.0, 1.9) * 2.5
    rustle = held(t2, s0 + 0.12, s0 + 0.56, 0.04, 0.10) if T >= s0 else 0.0    # one last small rustle on its way down
    img = CRUMPLED[n % len(CRUMPLED)] if max(open_, rustle) > 0.3 else ARM
    st.put(img, px, py, scale=k, turn=turn + lean + sh * 0.08 + jr + wobble(t2, 13.0, 1.6) * 0.5, sx=sx, sy=sy, anchor=duo.L_ANCHOR, depth=1.0)


# ---- the three shots. (His drawing has a scarf end that flies out to the right; on this still day he stands far
#      enough right in every shot for it to stay outside the frame.)
v.shot("start", bg=BG_A, luna=LUNA_A, timo=TIMO_A, front=packet)
v.shot("cut1", bg=BG_B, luna=LUNA_B, timo=TIMO_B, front=packet)
v.shot("cut2", bg=BG_C, luna=LUNA_C, timo=TIMO_C)

# ---- her face
v.faces(("start", None),                  # thrilled, to us
        ("T2@open", "guilty"),            # he has seen the packet: a look out of the corner of her eye
        ("T4@clapping+0.10", "read"),     # her eyes go down to the packet
        ("cut1", "read"),                 # time has passed. She is still looking at it
        ("L5-0.14", None),                # a perfectly reasonable plan, to us
        ("T6+0.22", "read"),              # she is not listening: back to the packet, and her head bows to it
        ("shush+0.03", "defend"),         # a thousand people. Caught: big eyes on us, she ducks
        ("sink+0.08", "read_b"),          # she puts the packet away: her eyes go down with it (so do ours)
        ("sink+0.62", "guilty"),          # and a look at him
        ("cut2", "guilty"),
        ("L8-0.12", "turn"),              # she rounds on him, and looks up at him through his answer
        ("T9.end+0.30", "defend"))        # "Coincidence." She turns back to us: did you hear that
v.tilt(L1=3.0, L3=-2.4, L5=2.6, L8=2.2)
v.blinks(luna=["L1.end+0.06", "L3.end+0.10", "L5.end+0.05", "shush+0.44"],          # between her lines, never in the first second
         timo=["T2.end+0.10", "cut1+0.80", "crinkle+1.02"])                           # the last one into the lens, while the packet goes on

# ---- his eyes
v.eyes(("start", "narrow"),               # fixed on the packet
       ("T2-0.05", None),                 # down at her: "Don't open that."
       ("L5@really", "narrow"),           # "really slowly": he knows
       ("T6-0.04", None),
       ("crinkle+0.34", "lens"),          # he looks at us while it goes on
       ("shush+0.10", "shut"))            # and shuts his eyes for the rest of the film
v.shake("shush")                          # the picture jolts with the crowd's "Shhh!" (twice the kit's jolt)
v.shake("shush")

# ---- the words on screen: low and left of the packet while it is up; above her head in the last shot, where her scarf must be seen
LOW_A, LOW_B, HIGH = (392, 1436), (372, 1425), (392, 612)
v.captions(
    place={"L1": LOW_A, "T2": LOW_A, "L3": LOW_A, "T4": LOW_A, "L5": LOW_B, "T6": LOW_B, "T7": HIGH, "L8": HIGH, "T9": HIGH},
    hold={"L5": ("I’ll do it\n", "really slowly.", "L5@really")},              # the plan arrives when she says it
    text={"L1": "We’re at the tennis.", "T2": "Don’t open that.", "L3": "I’m starving.", "T4": "Wait for\nthe clapping.",
          "T7": "I’m not with her.", "L8": "We have\nthe same scarf."},
    off={"T4": "cut1+0.04", "T6": "crinkle+0.50", "T9": "plate-0.10"})

if __name__ == "__main__":
    v.main()
