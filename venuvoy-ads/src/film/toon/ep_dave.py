"""The picture of the sketch "Official Dave" (vlog 07, "dave"): the forecourt of an ice hockey arena at winter dusk.

    python3 src/film/toon/ep_dave.py --sheet        a contact sheet of the whole film (work/dave/sheet.jpg)
    python3 src/film/toon/ep_dave.py --stills 0.1 "L3@official" "L5+0.4" "cut1+0.1" "bow+0.5" "look+0.5" "drop+0.4" "plate+1.0"
    python3 src/film/toon/ep_dave.py                the film (ads/21-vlog/venuvoy-vlog-07-dave-en_...mp4)

Run ep_dave_sound.py first: every time below is read from its timeline.

Luna holds her phone up beside her like a trophy from the first frame: tickets! A stranger sent the link, and his name
is Official Dave. Timo has her paste it into the planner's link check; she reads the answer off the phone ('Could not
verify.'), Timo looks at us, the phone pings: Dave says hurry. "Bye, Dave." The phone goes down; he is still looking
at us. One thing is this film's own, drawn by a hook: the phone in her free hand (the kit's drawing of it), because it
is up from the first frame, gives a proud little pump on "Good news!", buzzes once at the ping and, after a still half
second, goes down out of the picture at an even speed (the kit's piece drops in 0.45 s, eased).

After the independent check (render 3):
- She no longer smiles through the bad news. Her reading face rests on the small flat mouth of luna_read3b (a copy
  of luna_read3 with that mouth put in: work/dave/art/luna_read3q.png, packed as luna_read3q.webp; made from the two
  drawings, nothing bought: luna_read3b's box x 565-770, y 1160-1230 laid over luna_read3 through a mask feathered
  6 px), and on 'Could not verify.', "He says hurry." and "Bye, Dave." she talks only with the small round mouth,
  shut where the voice dips (no open mouth with teeth on these three lines).
- She is sure of herself until the check: sunny under his flat "Official Dave." and while he explains (the worried
  'defend' and 'guilty' faces before the check are gone); her eyes drop to the phone the moment he says "Paste.".
- The phone is up from frame 1 with her first caption; it buzzes visibly at the ping; it leaves in its full 0.7 s.
- The painting is graded to a blue winter dusk (darker toward the top), the lit doors kept warm.
"""
import math
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
import vlog  # noqa: E402
from toon import Talk, clamp, ease, on_twos, wobble  # noqa: E402
from vlog import Vlog, held  # noqa: E402

# the words the joke rests on get their mouth shapes by hand (the last three lines are set below, by the voice)
SAID = {"good": "C O C", "news": "C O C", "ice": "A C", "hockey": "O C E", "tickets": "C E C E C", "from": "C O M", "where": "O A",
        "man": "M A C", "sent": "C E C", "link": "C E C", "official": "O C E C A", "dave": "C A C", "its": "E C", "in": "E C", "his": "E C",
        "name": "C A M", "checks": "C E C", "ticket": "C E C", "links": "C E C", "paste": "M A C", "never": "C E C A", "trust": "C A C",
        "any": "E C"}

# a cold, STILL winter dusk: her calm hair (in the first cut her hair whipped about beside his still flat-top, and the
# fringed end of her flying scarf over his jacket was taken by the watching model for "his gloved hand"); without
# flying hair to keep under his mouth he stands lower, his eyes well inside the 4:5 version. Him without the blade of
# grass. Her reading face with the small flat mouth (see above).
v = Vlog("dave", "ads/21-vlog/venuvoy-vlog-07-dave", luna="calm", timo="timo_clean", said=SAID,
         extra_faces={"read": "luna_read3q"})


class Small(Talk):
    """Her small voice at the end: only the small round mouth, on the steps where the voice is up; shut where it dips
    (inside a long run, the lips meet at the quietest step). No open mouth with teeth: she is not pleased."""

    def __init__(self, words, wav, on=0.30):
        super().__init__(words, wav, have=("O", "M"), quiet=0.10)
        lv = [self.level(k / 12 + self.lead) for k in range(len(self.seq))]
        seq = ["O" if x >= on and k / 12 + self.lead <= self.end else None for k, x in enumerate(lv)]
        i = 0
        while i < len(seq):
            j = i
            while j < len(seq) and seq[j] == seq[i]:
                j += 1
            if seq[i] == "O" and j - i >= 5:
                mids = [k for k in range(i + 1, j - 1) if lv[k] <= lv[k - 1] and lv[k] <= lv[k + 1]]
                for k in mids or [(i + j) // 2]:
                    seq[k] = None
            i = j
        self.seq = seq


for _i in ("L9", "L10", "L11"):
    v.talk[_i] = Small(v.LINE[_i]["words"], vlog.ROOT / v.LINE[_i]["wav"])
for _i in ("L5", "L7"):     # on her turned head: the loudness of the voice leads, so the mouth closes at the dips
    v.talk[_i] = Talk(v.LINE[_i]["words"], vlog.ROOT / v.LINE[_i]["wav"], have=duo.LUNA_MOUTHS, quiet=0.10, said=dict(vlog.SAID, **SAID),
                      open_min=0.45, drive=True)

# drawing, a point in it (the lit doors of the arena, with the people going in), where that lands in the frame, size
WIDE = ("bg_forecourt_1", (420, 1450), (250, 600), 1.10)
NEAR = ("bg_forecourt_1", (420, 1450), (150, 610), 1.40)       # a quarter closer, the doors further left: at a fifth the
                                                               # watching model saw "a single continuous shot"
LUNA_A, TIMO_A = (372, 1556, 1.00, -1.2), (872, 1000, 1.00)
LUNA_B, TIMO_B = (335, 1575, 1.25, 1.0), (895, 1075, 1.25)     # his hair top at y 238: inside the 4:5 version (from 180)


def dusk(img, top=900.0, low=1600.0, f_top=0.60, f_low=0.85):
    """A blue winter dusk: the painting darker toward the top (0.6 of itself at the sky over the arena, 0.85 on the
    paving), a little bluer; what is lit warm (the doors, the lamps and their reflections) keeps its light."""
    y = np.arange(img.shape[0], dtype=np.float32)[:, None]
    f = f_top + (f_low - f_top) * np.clip((y - top) / (low - top), 0, 1)
    rgb = img[..., :3]
    warm = np.clip((rgb[..., 0] - rgb[..., 2] - 0.04) / 0.20, 0, 1)[..., None] * 0.85
    cool = np.stack([f * 0.93, f * 0.98, f * 1.06], axis=-1)
    out = img.copy()
    out[..., :3] = np.clip(rgb * (cool * (1 - warm) + warm * np.maximum(f, 0.92)[..., None]), 0, 1)
    return out


# the painting a little out of focus, as a phone's camera sees what is far behind the face it is pointed at (the people
# by the doors were drawn with a pen line, as sharp as the two in front), and graded to dusk
_bg = duo.background(v.art, WIDE[0])
_bg.img = np.ascontiguousarray(dusk(cv2.GaussianBlur(_bg.img, (0, 0), 3.0)))
v._bg[WIDE[0]] = _bg

# ---- her phone, up in her free hand from the first frame (the kit's drawing of it: luna_read3b's arm, nothing new)
# its sleeve runs on 420 rows further below than the kit's, so that held up high it still leaves the frame at the bottom
_p = duo.phone(v.art, v.s_luna)
PHONE = np.ascontiguousarray(np.concatenate([_p, np.repeat(_p[-1:], 420, axis=0)], axis=0))
ANCHOR = (duo.L_ANCHOR[0], duo.L_ANCHOR[1] * _p.shape[0] / PHONE.shape[0])
TROPHY, READ = 330.0, 150.0                      # pixels higher than drawn: held up like a trophy; at reading height
LIFT2 = 175.0                                    # the closer shot (she stands lower in it)
TOWARD = -55.0                                   # toward her, so that the sleeve meets her coat (at -45 small nubs of the
                                                 # coat's outline showed at the join)
GONE = 700.0                                     # how far it goes down in its 0.7 s: just out of the frame, at an even speed


def phone(st, t, T):
    t2 = on_twos(T)
    a, b = v.t("drop"), v.t("drop.end")
    if T >= b + 0.04:
        return
    x0, y0, k, lean = v.shot_at(T)["luna"]
    dx, dy, turn, sx, sy = v.luna_move(T)                                        # it is hers: it sways and pops with her body
    if T < v.t("cut1"):
        y = -ease(t2, v.t("L1.end+0.10"), v.t("L1.end+0.60"), TROPHY, READ, "io")       # up high for the good news, then down to read from
    else:
        y = -LIFT2
    y -= 34 * held(t2, v.t("L1@good+0.02"), v.t("L1@news.end-0.05"), 0.12, 0.30)  # "Good news!": up it goes
    y -= 26 * held(t2, v.t("L1@tickets"), v.t("L1.end+0.10"), 0.10, 0.40)       # "...tickets!": and again
    y += 14 * held(t2, v.t("bow+0.05"), v.t("L9.end"), 0.20, 0.30)              # she bows to it to paste and read: a little lower
    p = v.t("ping")
    sh = math.sin((t2 - p) * 46) * 22 * math.exp(-(t2 - p) * 4) if t2 >= p else 0.0  # the message comes in: it buzzes in her hand
    y += -10 * ease(t2, a - 0.12, a, 0.0, 1.0, "io") + GONE * clamp((t2 - a) / (b - a))   # a breath up, then down out of the frame at an even speed
    st.put(PHONE, x0 + dx + TOWARD * k + sh + wobble(t2, 11.0, 2.2) * 2.5, y0 + dy + y - abs(sh) * 0.4 + wobble(t2, 12.0, 1.9) * 3, scale=k,
           turn=turn + lean + sh * 0.25 + wobble(t2, 13.0, 1.6) * 0.7, sx=sx, sy=sy, anchor=ANCHOR, depth=1.0)


# ---- two shots: the two of them; a jump cut a quarter closer for his answer
v.shot("start", bg=WIDE, luna=LUNA_A, timo=TIMO_A, front=phone)
v.shot("cut1", bg=NEAR, luna=LUNA_B, timo=TIMO_B, front=phone)

# ---- her face: sure of herself until the check; small after it
v.faces(("start", None),                  # sunny, the phone up: tickets!
        ("L3@official-0.06", "read"),     # she reads the name off the phone, with respect
        ("L3.end+0.12", None),            # and beams at us: official. She beams on through his flat repeat
        ("L5-0.10", "turn"),              # she turns to him: "It's in his name!"
        ("cut1", None),                   # the closer shot: still sure of herself, to us, while he explains
        ("L7-0.10", "turn"),              # "You never trust any—"
        ("L7.end+0.02", "read_b"),        # "Paste.": her eyes drop to the phone at once
        ("T8.end+0.06", "read"),          # she bows to the phone, pastes it, her eyes go along the line ...
        ("bow.end-0.38", "down"),         # ... oh
        ("L9-0.06", "read"),              # 'Could not verify.' read off the phone, small
        ("L9.end+0.10", "read_b"),        # eyes on the phone, mouth shut: he looks at us
        ("ping+0.10", "read"),            # the phone buzzes: her eyes go along a new message
        ("caught", "defend"),             # "He says hurry." ... caught: big eyes on us, half a second
        ("caught.end", "read_b"),         # eyes down to the phone again
        ("L11-0.10", "read"),             # "Bye, Dave." fond
        ("drop-0.20", "guilty"))          # the phone goes: the crooked closed smile, a sideways look at him
v.tilt(L1=3.2, L3=-2.4, L5=2.6, L7=2.2, L9=-1.4, L10=-1.8, L11=-2.6)
v.blinks(luna=["L1.end+0.12", "L3+0.62", "T4+0.55", "cut1+0.95", "plate+0.85"],    # between her lines, never in the first second
         timo=[1.9, 4.6, "T4.end+0.35", "cut1+1.6", "T8.end+0.40", "L11.end+0.25", "plate+1.40"])   # one blink in his long look at us, none while she reads it

# ---- his eyes: on her (heavy lids); narrowed at the name; on us when the music stops, to the end
v.eyes(("start", None),
       ("L3@dave", "narrow"),             # the name: he is working something out
       ("cut1", None),                    # he tells her, kindly
       ("look", "lens"))                  # 'Could not verify.' He looks at us, and stays

# ---- the words on screen
# left of the phone (its left edge is at x 700 in both shots)
CAP_A, CAP_B = (405, 1432), (385, 1415)
v.captions(
    place={"L1": CAP_A, "T2": CAP_A, "L3": CAP_A, "T4": CAP_A, "L5": CAP_A,
           "T6": CAP_B, "L7": CAP_B, "T8": CAP_B, "L9": CAP_B, "L10": CAP_B, "L11": CAP_B},
    split={"L3": [("A man sent me\na link.", None), ("“Official Dave.”", "L3@official")],
           "T6": [("The planner\nchecks ticket links.", None), ("Paste it.", "T6@paste")]},
    text={"L5": "It’s in his name!", "L7": "You never\ntrust any—", "L9": "“Could not verify.”"},
    on={"L1": "start",                    # her first caption is up from frame 1 (the cover)
        "T8": "T8+0.10"},                 # he talks over her: her words stay up a moment under his voice
    off={"L7": "T8+0.08", "L11": "drop+0.30"})
v.plate(x=352, y=300, size=0.74)          # he is still standing at the end: the plate goes beside his head

if __name__ == "__main__":
    v.main()
