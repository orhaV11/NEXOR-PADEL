"""The picture of "The Switch": Luna's selfie vlog on the hill, animated to the soundtrack.

    python3 src/film/toon/switch_film.py                 the film (1080 x 1920) with its sound
    python3 src/film/toon/switch_film.py --draft         half size, quick
    python3 src/film/toon/switch_film.py --stills 1.0 4.2 ...   single frames into work/switch/stills/
    python3 src/film/toon/switch_film.py --text          set the captions in type again first

Every time comes from work/switch/sound/timeline.json (written by switch_sound.py), so a change of the comic timing
moves the picture with it.

Three shots, each framed differently so that a jump cut reads as a cut: (1) wide, Timo face down on the ridge behind
her; (2) the two of them, Timo standing behind; (3) closer and higher, she holds up her phone and reads from it.
"""
import json
import math
import sys
from pathlib import Path

import cv2
import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE))
import switch_cast as cast  # noqa: E402
from toon import H, W, Film, Picture, Stage, Talk, bend, bend_x, clamp, ease, handheld, key_out, on_twos, pop, premul, read, wobble  # noqa: E402

WORK = ROOT / "work" / "switch"
tl = json.loads((WORK / "sound" / "timeline.json").read_text())
LINE = {l["id"]: l for l in tl["lines"]}
EV = tl["events"]
CUT1, CUT2 = EV["cuts"]
SINK0, SINK1 = EV["sink"]
PLATE, END, THUD = EV["plate"], EV["end"], EV["thud"]

# ---------------------------------------------------------------- the cast, the place, the words
S_LUNA, S_TIMO, S_PRONE = 0.64, 0.50, 0.23
luna, timo, prone = cast.luna(S_LUNA), cast.timo(S_TIMO), cast.prone(S_PRONE)
phone, hand = cast.phone(S_LUNA), cast.hand(S_LUNA)
bg_far = cast.background("bg_hill_2", 1.0)    # the ridge with the path (the first shot); its stadium is at (335, 1200)
bg_near = cast.background("bg_hill_1", 1.0)   # the hill and the city below (after the jump cuts); its stadium is at (370, 1660)
plate = Picture(WORK / "text" / "plate.png")

# the mouth shapes of the words the jokes rest on, set by hand (A open, O round, C a little open, M shut)
SAID = {"day": "C A", "two": "O", "trip": "C M", "why": "O A", "mountain": "M O C", "planner": "M A C", "switch": "O C", "said": "C", "hikes": "A C", "you": "O",
        "hate": "A C M", "like": "A C", "idea": "A C A", "so": "O", "did": "C", "fl": "C", "flip": "C M", "it": "C", "was": "O A", "what": "O A", "else": "C M",
        "tomorrow": "C O M O", "morning": "M O C", "run": "A C", "back": "M A C", "he": "C", "loves": "A C"}
talk = {i: Talk(l["words"], ROOT / l["wav"], have=("A", "E", "O", "C") if l["who"] == "L" else ("A", "O", "C"), quiet=0.10 if l["who"] == "L" else 0.055, said=SAID)
        for i, l in LINE.items()}

L_ANCHOR = (663 / 1792, 1680 / (2400 + cast.L_PAD))   # the knot of her scarf: she turns and squashes about it
T_ANCHOR = (614 / 1536, 1514 / 3852)   # the middle of his chest (the drawing is padded downward)
# her head moves on her shoulders: it turns about the base of her neck; the scarf is where the picture bends
L_NECK, L_STILL, L_FULL = (665 * S_LUNA, 1400 * S_LUNA), 1560 * S_LUNA, 1330 * S_LUNA
T_NECK, T_STILL, T_FULL = (600 * S_TIMO, 1150 * S_TIMO), 1290 * S_TIMO, 1100 * S_TIMO
# where each of them stands in each shot: the anchor's place in the frame, the size, a lean of the whole figure
PLACE = {1: {"luna": (362, 1545, 0.92, 0.0)},
         2: {"luna": (372, 1556, 1.00, -1.2), "timo": (872, 935, 1.00)},
         3: {"luna": (345, 1490, 1.16, 1.0), "timo": (890, 830, 1.16)}}   # sixteen per cent closer than the second: a punch-in, not a bump
P_POS, P_TURN = (1046, 652), -12.0     # Timo on the ground in the first shot: his ankles, and the slope he lies on


def word_time(line, word, into=0.0):
    l = LINE[line]
    return l["t0"] + next(a for w, a, b in l["words"] if w.lower().startswith(word)) + into


HIKES = word_time("L3", "hike")
RUN = word_time("L9", "run")
LENS = LINE["L7"]["t1"]                 # he looks at us the moment her line ends (and the music stops)
PHONE_UP, PHONE_DOWN = LINE["L9"]["t0"] - 0.38, THUD + 0.05
GLANCE = LENS + 0.30
SIDE = LINE["T6"]["t0"] + 0.30
HAND_UP, HAND_DOWN = LINE["L7"]["t0"] + 0.02, LENS + 0.22
READ0, READ1 = LINE["L9"]["t0"] - 0.20, LINE["L9"]["t1"] + 0.12
LOOK_DOWN, BACK_TO_US = THUD + 0.12, LINE["L11"]["t0"] - 0.15

# ---------------------------------------------------------------- captions: one at a time, each where its speaker is
LOW, SKY = (W / 2, 1395), (600, 345)
LOW1 = (W / 2, 1440)   # in the first shot she is lower in the frame: two lines of caption must stay off her chin
_L = LINE
CAPS = [  # name, who, words (and a held-back last word), on, off, place, pops in
    ("L1", "L", "Day two of the football trip!", None, _L["L1"]["t0"], _L["T2"]["t0"], LOW1, True),
    ("T2", "T", "Why is there a mountain in the football trip?", None, _L["T2"]["t0"], _L["L3"]["t0"], SKY, True),      # above him: he is the one on the ground
    ("L3a", "L", "The planner had a switch.", None, _L["L3"]["t0"], word_time("L3", "it"), LOW1, True),
    ("L3b", "L", "It said “hikes”.", None, word_time("L3", "it"), CUT1 + 0.04, LOW1, True),
    ("T4", "T", "You hate hikes.", None, _L["T4"]["t0"], _L["L5"]["t0"], LOW, True),
    ("L5", "L", "I like the idea of hikes.", None, _L["L5"]["t0"], _L["T6"]["t0"], LOW, True),
    ("T6", "T", "So why did you fl—", None, _L["T6"]["t0"], _L["L7"]["t0"], LOW, True),
    ("L7", "L", "It was a switch!", None, _L["L7"]["t0"], LENS + 0.06, LOW, True),                                       # gone as he looks at us
    ("T8", "T", "…What else did you flip?", None, _L["T8"]["t0"] - 0.04, PHONE_UP, LOW, True),                             # gone when her phone comes up
    ("L9a", "L", "“Tomorrow morning.\n", "Run.”", _L["L9"]["t0"], RUN, (394, 1352), True),                               # beside the phone she reads from;
    ("L9b", "L", "“Tomorrow morning.\nRun.”", None, RUN, _L["L9"]["t1"] + 0.30, (394, 1352), False),                     # the word arrives when she says it
    ("T10", "T", "Flip it back.", None, _L["T10"]["t0"], _L["L11"]["t0"], (560, 1395), True),                            # beside his finger, which comes up where he went down
    ("L11", "L", "He loves it.", None, _L["L11"]["t0"], _L["L11"]["t1"] + 0.16, LOW, True),
]
if not all((WORK / "text" / f"{c[0]}.png").exists() for c in CAPS) or "--text" in sys.argv:   # set the captions in type
    import subprocess
    spec = {"out": "work/switch/text", "items": [{"name": n, "kind": "cap", "who": w, "text": t, **({"hidden": h} if h else {})} for n, w, t, h, *_ in CAPS] + [{"name": "plate", "kind": "plate"}]}
    (WORK / "text_spec.json").write_text(json.dumps(spec, ensure_ascii=False, indent=1))
    subprocess.run(["node", str(HERE / "text.mjs"), str(WORK / "text_spec.json")], check=True)
cap_pic = {c[0]: Picture(WORK / "text" / f"{c[0]}.png") for c in CAPS}

# ---------------------------------------------------------------- who does what, when (film time)

def luna_face(T):
    if T < CUT1:
        if T < LINE["T2"]["t0"] + 0.95:
            return None                      # delighted: it is day two!
        if T >= HIKES - 0.04:
            return None                      # "hikes" is good news again
        return "defend" if LINE["L3"]["t0"] - 0.08 <= T < word_time("L3", "it") - 0.05 else "guilty"   # caught; she explains it to US, big-eyed; a look back at him
    if T < CUT2:
        if T >= GLANCE:
            return "guilty"                  # he is looking at us; she steals a look at him, and smiles as if nothing
        if T >= LINE["L7"]["t0"] - 0.10:
            return "turn"                    # she rounds on him
        return "guilty" if T >= SIDE else "defend"   # his question is coming: a look out of the corner of her eye
    if T < READ0:
        return "defend" if T < CUT2 + 0.30 else "guilty"   # what else did she flip: caught, then sheepish
    if T < READ1:
        return "read"                        # she reads it off her phone, pleased
    if T < LOOK_DOWN:
        return None                          # and beams at us: a run!
    if T < LOOK_DOWN + 0.42:
        return "down"                        # where did he go
    if T < BACK_TO_US:
        return "read_b"                      # ah. There he is. (eyes down, mouth shut: the next line is his, not hers)
    return None


FACE_CHANGES = [LINE["T2"]["t0"] + 0.95, LINE["L3"]["t0"] - 0.08, word_time("L3", "it") - 0.05, HIKES - 0.04, CUT2 + 0.30, SIDE, LINE["L7"]["t0"] - 0.10, GLANCE, READ0, READ1, LOOK_DOWN, LOOK_DOWN + 0.42, BACK_TO_US]
LUNA_BLINKS = [1.02, 3.6, 5.9, 7.72, CUT1 + 0.75, LINE["L5"]["t0"] - 0.22, LINE["L5"]["t1"] + 0.12, SIDE + 0.42, CUT2 + 0.5, READ1 + 0.16, LOOK_DOWN + 0.5, BACK_TO_US + 1.6,
               BACK_TO_US + 3.0]   # every face she blinks on has its own drawn blink
TIMO_BLINKS = [LINE["T4"]["t1"] + 0.3, LINE["L5"]["t0"] + 1.5, LINE["L7"]["t0"] + 0.14, LINE["T8"]["t1"] + 0.2]   # the third: he is cut off, and blinks


def blink(T, times, d=0.125):
    return any(t <= T < t + d for t in times)


def wind(T, seed=0.0, rate=6.5):
    """Which drawing of the wind-blown parts shows now: the base and two others, changing about four times a second."""
    u = T * rate * 0.62 + seed * 3.1 + 0.35 * math.sin(T * 1.7 + seed)      # uneven holds of about a fifth to a third of a second
    return (None, "1", None, "2")[int(u) % 4]                                 # always through the base drawing: no jump from one far state to the other


def speaking(T, who):
    for i, l in LINE.items():
        if l["who"] == who and l["t0"] - 0.05 <= T <= l["t1"] + 0.05:
            return i
    return None


def accent(T, who):
    i = speaking(T, who)
    return talk[i].accent(T - LINE[i]["t0"]) if i and not LINE[i]["ground"] else 0.0


def luna_state(T):
    face = luna_face(T)
    st = {"1face": face, "0wind": wind(T, 0.0)}
    if face in (None, "down") and blink(T, LUNA_BLINKS):
        st["2eyes"] = "shut"
    elif face in ("guilty", "defend") and blink(T, LUNA_BLINKS):
        st["1face"] = face + "_shut"
    elif face == "read" and int((T - READ0) / 0.34) % 2:
        st["2eyes"] = "scan"                 # her eyes go along the line
    i = speaking(T, "L")
    if i:
        m = talk[i].shape(T - LINE[i]["t0"])
        if m:
            if face == "read" and m == "E":
                m = "C"                      # no bared teeth while she reads
            st["3mouth"] = ({"C": "tC", "A": "tA", "E": "tE", "O": "tA"}[m] if face == "turn" else m)   # the turned head has no round mouth: the open one holds the vowel
    return st


def timo_state(T):
    st = {"0scarf": wind(T, 4.2)}
    if T >= CUT2:
        st["2eyes"] = "shut" if T >= LINE["L9"]["t1"] + 0.06 else "narrow"   # a run. He closes his eyes.
    elif T >= LENS:
        st["2eyes"] = "lens"                 # he looks at us. That is all.
    if blink(T, TIMO_BLINKS, 0.21):
        st["2eyes"] = "shut"
    i = speaking(T, "T")
    if i and not LINE[i]["ground"]:
        m = talk[i].shape(T - LINE[i]["t0"])
        if m:
            st["3mouth"] = m
    return st


def held(t, on, off, rise=0.16, fall=0.35):
    """0..1: up shortly before `on`, down again after `off` (for a pose held through a line)."""
    return ease(t, on - rise, on + 0.10, 0.0, 1.0, "back") - ease(t, off, off + fall, 0.0, 1.0, "io")


# how her head tilts into each of her lines (degrees; positive leans toward Timo, on our right)
LUNA_TILT = {"L1": 3.2, "L3": -2.6, "L5": 3.0, "L7": 2.2, "L11": -3.4}


def luna_move(T):
    """Her body: a pop at every change of expression and a restless little sway."""
    t = on_twos(T)
    sx = sy = 1.0
    for c in FACE_CHANGES:
        a, b = pop(t, c, 0.032)
        sx, sy = sx * a, sy * b
    acc = accent(T, "L")
    return wobble(t, 3.0, 0.9) * 5, wobble(t, 2.0, 1.1) * 4 + acc * 2.5, wobble(t, 1.0, 1.3) * 0.55, sx, sy


def luna_head(T):
    """Her head on her shoulders: it tilts into each line, dips on the words she hits, ducks when she is caught, bows
    to the phone, and goes a little way ahead of a turn. Returns (turn in degrees, shift x, shift y)."""
    t = on_twos(T)
    acc = accent(T, "L")
    ang = wobble(t, 5.0, 1.0) * 0.8
    for i, a in LUNA_TILT.items():
        ang += a * held(t, LINE[i]["t0"], LINE[i]["t1"])
    side = 1.0 if ang >= 0 else -1.0
    ang += acc * 1.5 * side
    dx, dy = 0.0, acc * 7.0
    duck = held(t, LINE["T2"]["t0"] + 0.95, HIKES - 0.1, 0.05, 0.2) + (held(t, CUT2 + 0.05, READ0 - 0.08, 0.02, 0.15) if T >= CUT2 else 0.0)
    dy += duck * 9
    ang -= duck * 1.6
    reading = held(t, READ0, READ1 - 0.1, 0.1, 0.22)
    ang += reading * 3.0
    dy += reading * 8
    dx += reading * 5
    down = held(t, LOOK_DOWN, BACK_TO_US - 0.05, 0.02, 0.2)
    ang += down * 5.0
    dy += down * 12
    dx += down * 8
    lead = LINE["L7"]["t0"] - 0.10                 # the drawn turn of her head: the head starts a moment early and overshoots
    dx += ease(t, lead - 0.09, lead, 0.0, 9.0, "in") * (1.0 if t < lead else 0.0) + (ease(t, lead, lead + 0.25, 12.0, 0.0, "out") if lead <= t < CUT2 else 0.0)
    return ang, dx, dy


def timo_head(T):
    t = on_twos(T)
    acc = accent(T, "T")
    lean = 0.0   # he leans a little toward her while he speaks, and straightens after
    for l in LINE.values():
        if l["who"] == "T" and not l["ground"]:
            lean = max(lean, held(t, l["t0"], l["t1"], 0.12, 0.4))
    ang = wobble(t, 8.0, 0.8) * 0.5 - lean * 2.8 - acc * 1.3
    dy = acc * 3.5
    cut_off = LINE["L7"]["t0"] + 0.04              # talked over: his head goes back a little, and stays back
    if cut_off <= T < LENS:
        ang += ease(t, cut_off, cut_off + 0.14, 0.0, 2.6, "back")
    if LENS <= T < CUT2:                           # his head comes round to us, once, and stays
        ang += ease(t, LENS, LENS + 0.16, 0.0, 2.4, "back")
    if T >= LINE["L9"]["t1"] + 0.06:               # eyes shut: his head sinks a little before the rest of him does
        k = ease(t, LINE["L9"]["t1"] + 0.06, LINE["L9"]["t1"] + 0.36, 0.0, 1.0, "io")
        ang, dy = ang - k * 1.5, dy + k * 9
    return ang, 0.0, dy, lean


def camera(seed=0.0):
    def cam(t, T):
        x, y, zoom, turn = handheld(T, 1.0, seed)
        k = math.exp(-(T - THUD) * 9) if T >= THUD else 0.0   # the ground shakes a little when he lands
        y += math.sin((T - THUD) * 55) * 24 * k
        return x, y, zoom, turn
    return cam


def lay(st, pic, src, at, k, depth=0.45):
    """Put a background so that its point `src` (in the picture's own pixels) lands on `at` in the frame, at size k."""
    h, w = pic.img.shape[:2]
    st.put(pic.img, at[0] + (w / 2 - src[0]) * k, at[1] + (h / 2 - src[1]) * k, scale=k, depth=depth)


def caption(st, T):
    for name, who, text, held_word, on, off, place, pops in CAPS:
        if on - 0.04 <= T < off - 0.04:
            k = ease(T, on - 0.04, on + 0.09, 0.9, 1.0, "back") if pops else 1.0
            st.put(cap_pic[name].get(), place[0], place[1], scale=k, depth=0.0)


def draw_luna(st, T, shot):
    x0, y0, k, tilt = PLACE[shot]["luna"]
    dx, dy, turn, sx, sy = luna_move(T)
    ang, hx, hy = luna_head(T)
    img = bend(luna.get(**luna_state(T)), L_NECK, L_STILL, L_FULL, ang, hx, hy)
    st.put(img, x0 + dx, y0 + dy, scale=k, turn=turn + tilt, sx=sx, sy=sy, anchor=L_ANCHOR, depth=1.0)
    if shot == 2 and HAND_UP - 0.05 <= T < HAND_DOWN + 0.3:    # "It was a switch!": her free hand flies up, shakes once on the word, and falls
        t = on_twos(T)
        rise = ease(t, HAND_UP, HAND_UP + 0.2, 720.0, 0.0, "back") + ease(t, HAND_DOWN, HAND_DOWN + 0.22, 0.0, 720.0, "in")
        hit = word_time("L7", "switch")
        shake = math.sin((t - hit) * 30) * 14 * math.exp(-(t - hit) * 7) if t >= hit else 0.0
        st.put(hand, x0 + dx + 26 + shake, y0 + dy - 105 + rise - abs(shake) * 0.6, scale=k, turn=turn + tilt + shake * 0.12, sx=sx, sy=sy, anchor=L_ANCHOR, depth=1.0)
    if shot == 3 and PHONE_UP - 0.05 <= T < PHONE_DOWN + 0.3:   # her other hand brings the phone up from below, and drops it when he drops
        t = on_twos(T)
        rise = ease(t, PHONE_UP, PHONE_UP + 0.36, 760.0, 0.0, "back") + ease(t, PHONE_DOWN, PHONE_DOWN + 0.2, 0.0, 760.0, "in")
        st.put(phone, x0 + dx + wobble(t, 11.0, 2.2) * 2.5, y0 + dy - 110 + rise + wobble(t, 12.0, 1.9) * 3, scale=k, turn=turn + tilt + wobble(t, 13.0, 1.6) * 0.7,
               sx=sx, sy=sy, anchor=L_ANCHOR, depth=1.0)


def draw_timo(st, T, shot, drop=0.0, stretch=1.0):
    x0, y0, k = PLACE[shot]["timo"]
    t = on_twos(T)
    ang, hx, hy, lean = timo_head(T)
    img = bend(timo.get(**timo_state(T)), T_NECK, T_STILL, T_FULL, ang, hx, hy)
    breathe = 1 + math.sin(t * 1.9) * 0.004
    st.put(img, x0 - lean * 7, y0 + drop + lean * 4, scale=k, turn=wobble(t, 7.0, 0.8) * 0.3 - lean * 1.3, sx=1 / stretch ** 0.5, sy=stretch * breathe,
           anchor=T_ANCHOR, depth=0.82)


# ---------------------------------------------------------------- the first shot: Timo face down on the ridge

FAR = ((335, 1200), (196, 640), 0.87)      # the far background: its stadium lands here, at this size
NEAR2 = ((370, 1660), (238, 596), 1.28)    # the near background in the second shot
NEAR3 = ((370, 1660), (250, 440), 1.50)    # and in the third: closer, the horizon lower


def _still_frame(draw):
    s = Stage()
    draw(s)
    return s.frame


def _ground_cover():
    """The grass he lies IN: ragged blades of the background itself laid back over the lower edge of his body, and a
    soft shadow under him. Both are frame-sized layers that move with the background."""
    a = math.radians(P_TURN)
    ux, uy = math.cos(a), math.sin(a)                      # along his body, from head to feet
    vx, vy = -uy, ux                                       # down, across his body

    def at(s, d):
        return [P_POS[0] + ux * s + vx * d, P_POS[1] + uy * s + vy * d]

    mask = np.zeros((H, W), np.float32)
    rng = np.random.default_rng(7)
    edge = (1150 - 1306) * S_PRONE                         # the lower outline of his body, above the line his hand lies on
    for s0, s1, d in ((-535.0, -452.0, edge), (-328.0, 72.0, edge), (-452.0, -328.0, 3.0)):   # his hand and scarf end lie in front: the grass starts below them
        s = s0
        while s < s1:
            wd, ht, ln = rng.uniform(8, 15), rng.uniform(8, 26), rng.uniform(3, 13)
            cv2.fillPoly(mask, [np.array([at(s - wd / 2, d + 5), at(s + wd / 2, d + 5), at(s + ln, d - ht)], np.int32)], 1.0, cv2.LINE_AA)
            s += rng.uniform(5, 11)
        cv2.fillPoly(mask, [np.array([at(s0, d + 2), at(s1, d + 2), at(s1, d + 9), at(s0, d + 9)], np.int32)], 1.0, cv2.LINE_AA)
    mask = cv2.GaussianBlur(mask, (0, 0), 0.8)
    rgb = _still_frame(lambda st: lay(st, bg_far, *FAR))
    cover = np.dstack([rgb * mask[..., None], mask])
    shadow = np.zeros((H, W, 4), np.float32)
    cx, cy = at(-235, edge + 8)
    cv2.ellipse(shadow, (int(cx), int(cy)), (285, 17), P_TURN, 0, 360, (0, 0, 0, 0.30), -1, cv2.LINE_AA)
    return cover, cv2.GaussianBlur(shadow, (0, 0), 7)


COVER, SHADOW = _ground_cover()


def shot1(st, t, T):
    lay(st, bg_far, *FAR)
    # Timo, face down on the ridge behind her; he speaks into the ground and only his back gives a small heave
    i = speaking(T, "T")
    acc = talk[i].accent(T - LINE[i]["t0"]) if i else 0.0
    t2 = on_twos(T)
    heave = acc * 0.10 + math.sin(t2 * 3.3) * 0.030                       # he is out of breath: his back rises and falls
    lift = held(t2, LINE["T2"]["t0"] - 0.02, LINE["T2"]["t1"] - 0.05, 0.1, 0.16) * (4.5 + acc * 4.0)   # his head comes off the grass to say it, and drops back
    lift += math.sin(max(0.0, t2 - LINE["L1"]["t0"] - 0.5) * 2.1) ** 8 * 1.6   # now and then it tries to, without a word
    up = LINE["T2"]["t0"] - 0.10 <= T < LINE["T2"]["t1"] + 0.12          # he objects with one finger and does not lift his head
    a, b = pop(t2, LINE["T2"]["t0"] - 0.10, 0.07)
    c, d = pop(t2, LINE["T2"]["t1"] + 0.12, 0.05)
    state = {"0scarf": ("1", "2")[int(T * 2.1) % 2], "1pose": "finger" if up else None}   # the loose end of his scarf flaps, twice a second
    st.put(SHADOW, W / 2, H / 2, depth=0.45)
    lying = bend_x(prone.get(**state), (850 * S_PRONE, 1120 * S_PRONE), 930 * S_PRONE, 760 * S_PRONE, lift)
    st.put(lying, P_POS[0], P_POS[1], turn=P_TURN + acc * 1.2, sx=a * c, sy=(1 + heave) * b * d, anchor=(0.86, 0.85), depth=0.45)
    st.put(COVER, W / 2, H / 2, depth=0.45)
    draw_luna(st, T, 1)
    caption(st, T)


def shot2(st, t, T):
    lay(st, bg_near, *NEAR2)
    draw_timo(st, T, 2)
    draw_luna(st, T, 2)
    caption(st, T)


def shot3(st, t, T):
    lay(st, bg_near, *NEAR3)
    if T < SINK1 + 0.3:   # he goes straight down, the way a puppet leaves
        t2 = on_twos(T)
        drop = -ease(t2, SINK0 - 0.25, SINK0, 0.0, 60.0, "io") + 2300.0 * clamp((t2 - SINK0) / (SINK1 - SINK0)) ** 1.3   # up a little, then down, steadily: a lift going down
        draw_timo(st, T, 3, drop=drop, stretch=1 + 0.10 * clamp((t2 - SINK0) / (SINK1 - SINK0)))
    draw_luna(st, T, 3)
    caption(st, T)
    if FINGER <= T < FINGER_DOWN + 0.6:   # his finger comes up from where he fell, with his line: the same objection as on the hill
        t2 = on_twos(T)
        jab = talk["T10"].accent(T - LINE["T10"]["t0"]) if LINE["T10"]["t0"] <= T <= LINE["T10"]["t1"] else 0.0
        gone = ease(t2, FINGER_DOWN, FINGER_DOWN + 0.5, 0.0, 1.0, "in")       # "He loves it." The finger gives up: it tips over and sinks
        rise = ease(t2, FINGER, FINGER + 0.32, 900.0, 0.0, "back") + gone * 950
        st.put(arm, 925 + gone * 40, 2010 + rise - jab * 22, scale=1.5, turn=-5 + wobble(t2, 9.0, 2.0) * 1.2 - jab * 3 + gone * 24, anchor=(0.5, 1.0), depth=0.9)
    if T >= PLATE:
        k = ease(T, PLATE, PLATE + 0.22, 0.6, 1.0, "back")
        st.put(plate.get(), W / 2, 352, scale=k * 1.04, alpha=clamp((T - PLATE) / 0.08), depth=0.0)   # in the sky, where he stood: her face stays clear


FINGER, FINGER_DOWN = LINE["T10"]["t0"] - 0.16, LINE["L11"]["t1"] + 0.10
_f = key_out(read(cast.ART / "prone_finger.png")[..., :3])[215:770, 640:800]   # his raised arm, cut from the drawing of him on the ground
arm = premul(np.ascontiguousarray(_f))

film = Film(WORK, ROOT / "ads" / "20-sketches" / "venuvoy-sketch-01-the-switch")
film.whip(CUT1)   # the first cut is a new take, minutes later: a quick whip says so
film.shot(0.0, CUT1, shot1, camera(0.0))
film.shot(CUT1, CUT2, shot2, camera(3.3))
film.shot(CUT2, END, shot3, camera(6.1))

if __name__ == "__main__":
    if "--stills" in sys.argv:
        out = WORK / "stills"
        out.mkdir(exist_ok=True)
        for a in sys.argv[sys.argv.index("--stills") + 1:]:
            film.still(float(a), out / f"t{float(a):05.2f}.jpg", (540, 960))
        print("stills in", out)
    elif "--draft" in sys.argv:
        film.render(WORK / "draft.mp4", WORK / "sound" / "final.wav", crf=24, scale=(540, 960))
        print("draft", WORK / "draft.mp4")
    else:
        film.out.parent.mkdir(parents=True, exist_ok=True)
        dst = str(film.out) + "-en_9x16_1080x1920.mp4"
        film.render(dst, WORK / "sound" / "final.wav", crf=16)
        print("film", dst)
