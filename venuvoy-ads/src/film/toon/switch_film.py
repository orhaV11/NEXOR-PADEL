"""The picture of "The Switch": Luna's selfie vlog on the hill, animated to the soundtrack.

    python3 src/film/toon/switch_film.py                 the film (1080 x 1920) with its sound
    python3 src/film/toon/switch_film.py --draft         half size, quick
    python3 src/film/toon/switch_film.py --stills 1.0 4.2 ...   single frames into work/switch/stills/

Every time comes from work/switch/sound/timeline.json (written by switch_sound.py), so a change of the comic timing
moves the picture with it.
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
from toon import FPS, H, W, Film, Picture, Talk, clamp, ease, handheld, on_twos, pop, wobble  # noqa: E402

WORK = ROOT / "work" / "switch"
tl = json.loads((WORK / "sound" / "timeline.json").read_text())
LINE = {l["id"]: l for l in tl["lines"]}
EV = tl["events"]
CUT1, CUT2 = EV["cuts"]
SINK0, SINK1 = EV["sink"]
PLATE, END = EV["plate"], EV["end"]

# ---------------------------------------------------------------- the cast, the place, the words
S_LUNA, S_TIMO = 0.64, 0.50
luna, timo, prone = cast.luna(S_LUNA), cast.timo(S_TIMO), cast.prone(0.31)
bg_far = cast.background("bg_hill_2", 0.80)   # the path with Timo lying on it (the first shot)
bg_near = cast.background("bg_hill_1", 0.80)  # the hill and the city below (after the jump cuts)
caps = {i: Picture(WORK / "text" / f"{i}.png") for i in LINE}
plate = Picture(WORK / "text" / "plate.png")
talk = {i: Talk(l["words"], ROOT / l["wav"], have=("A", "E", "O", "C") if l["who"] == "L" else ("A", "O", "C")) for i, l in LINE.items()}

L_ANCHOR = (663 / 1792, 1680 / 2400)   # the knot of her scarf: she turns and squashes about it
T_ANCHOR = (614 / 1536, 1514 / 3852)   # the middle of his chest (the drawing is padded downward)
L_POS, T_POS = (392, 1545), (806, 935)


def word_time(line, word, into=0.0):
    l = LINE[line]
    return l["t0"] + next(a for w, a, b in l["words"] if w.startswith(word)) + into


HIKES = word_time("L3", "hike")

# ---------------------------------------------------------------- who does what, when (film time)

def luna_face(T):
    if T < CUT1:
        if T < LINE["T2"]["t0"] + 0.95:
            return None                      # delighted: it is day two!
        return None if T >= HIKES - 0.04 else "guilty"   # caught... and then "hikes" is good news again
    if T < CUT2:
        return "turn" if T >= LINE["L7"]["t0"] - 0.10 else "defend"
    if T < LINE["L9"]["t0"] - 0.12:
        return "guilty"
    if T < LINE["L9"]["t1"] + 0.10:
        return "read"
    if T < THUD:
        return "guilty"
    if T < THUD + 0.62:
        return "turn"                        # she looks round at where he was standing
    if T < LINE["L11"]["t0"] - 0.14:
        return "guilty"
    return None


LUNA_BLINKS = [2.55, 6.1, 8.55, 10.9, 16.2, 22.9, 24.0]
TIMO_BLINKS = [9.0, 10.2, 11.35, 14.1, 16.55]
THUD = EV["thud"]


def wind(T, seed=0.0, rate=6.5):
    """Which drawing of the wind-blown parts shows now: the base and two others, changing about six times a second,
    never in a regular loop."""
    k = int(T * rate + seed * 3.1)
    r = (math.sin(k * 12.9898 + seed * 78.233) * 43758.5453) % 1.0
    return (None, "1", "2")[int(r * 3)] if k % 2 else (None, "1", None, "2")[(k // 2) % 4]


def blink(T, times, d=0.125):
    return any(t <= T < t + d for t in times)


def speaking(T, who):
    for i, l in LINE.items():
        if l["who"] == who and l["t0"] - 0.05 <= T <= l["t1"] + 0.05:
            return i
    return None


def luna_state(T):
    face = luna_face(T)
    w = wind(T, 0.0)
    st = {"1face": face, "0hair": w, "0scarf": w}   # hair and scarf are one drawing of the wind: they change together
    if face not in ("turn", "read") and blink(T, LUNA_BLINKS):
        st["2eyes"] = "shut"
    i = speaking(T, "L")
    if i:
        m = talk[i].shape(T - LINE[i]["t0"])
        if m:
            st["3mouth"] = ({"C": "tC", "A": "tA", "E": "tE", "O": "tC"}[m] if face == "turn" else m)
    return st, i


def timo_state(T):
    st = {"0scarf": wind(T, 4.2)}
    if T >= CUT2:
        st["2eyes"] = "shut" if T >= LINE["L9"]["t1"] + 0.02 else "narrow"
    elif T >= LINE["L7"]["t1"] - 0.25:
        st["2eyes"] = "lens"                 # he looks at us. That is all.
    if blink(T, TIMO_BLINKS, 0.21):
        st["2eyes"] = "shut"
    i = speaking(T, "T")
    if i and not LINE[i]["ground"]:
        m = talk[i].shape(T - LINE[i]["t0"])
        if m:
            st["3mouth"] = m
    return st, i


FACE_CHANGES = [0.0, LINE["T2"]["t0"] + 0.95, HIKES - 0.04, CUT1, LINE["L7"]["t0"] - 0.10, CUT2, LINE["L9"]["t0"] - 0.12, LINE["L9"]["t1"] + 0.10, THUD, THUD + 0.62,
                LINE["L11"]["t0"] - 0.14]


def luna_move(T):
    """Her body: a pop at every change of expression, a nod on the words she hits, a restless little sway."""
    t = on_twos(T)
    sx = sy = 1.0
    for c in FACE_CHANGES:
        a, b = pop(t, c, 0.035)
        sx, sy = sx * a, sy * b
    i = speaking(T, "L")
    acc = talk[i].accent(T - LINE[i]["t0"]) if i else 0.0
    turn = wobble(t, 1.0, 1.3) * 0.7 - acc * 1.6
    dy = wobble(t, 2.0, 1.1) * 4 + acc * 7
    dx = wobble(t, 3.0, 0.9) * 5
    lean = 1.0
    if T >= CUT2:   # she leans into the phone to read, and comes back when she has read it
        lean = ease(T, LINE["L9"]["t0"] - 0.15, LINE["L9"]["t0"] + 0.2, 1.0, 1.085, "out") - ease(T, LINE["L9"]["t1"] + 0.1, LINE["L9"]["t1"] + 0.4, 0.0, 0.085, "io")
    return dx, dy, turn, sx, sy, lean


def camera(scale=1.0, seed=0.0):
    def cam(t, T):
        x, y, zoom, turn = handheld(T, 1.0, seed)
        # the ground shakes a little when he lands
        k = math.exp(-(T - EV["thud"]) * 9) if T >= EV["thud"] else 0.0
        y += math.sin((T - EV["thud"]) * 55) * 24 * k
        return x, y, zoom * scale, turn
    return cam


def wind_marks(st, T, seed, depth=0.6):
    """Pale streaks of wind and a few blades of grass tumbling across from the left, held on twos."""
    t = on_twos(T)
    rng = np.random.default_rng(seed)
    layer = np.zeros((H, W, 4), np.float32)
    for k in range(40):
        t0, y, grass = rng.uniform(0, END), rng.uniform(280, 1250), rng.random() < 0.45
        dur, ln, drift, spin = rng.uniform(0.38, 0.6), rng.uniform(150, 260), rng.uniform(-110, 70), rng.uniform(5, 11)
        if not (t0 <= t < t0 + dur):
            continue
        p = (t - t0) / dur
        x = -220 + p * (W + 520)
        yy = y + drift * p + math.sin(p * 7 + k) * 28
        if grass:
            a = p * spin + k
            dx, dy = math.cos(a) * 30, math.sin(a) * 30
            cv2.line(layer, (int(x - dx), int(yy - dy)), (int(x + dx), int(yy + dy)), (0.34, 0.50, 0.22, 1.0), 6, cv2.LINE_AA)
        else:
            pts = np.array([[x + i * ln / 6, yy + math.sin(i * 0.9 + p * 5) * 7] for i in range(7)], np.int32)
            cv2.polylines(layer, [pts], False, (1.0, 1.0, 1.0, 0.5), 4, cv2.LINE_AA)
    layer[..., :3] *= layer[..., 3:4]
    if layer[..., 3].max() > 0:
        st.put(layer, W / 2, H / 2, depth=depth)


def caption(st, T):
    for i, l in LINE.items():
        if l["t0"] - 0.04 <= T < min(l["t1"] + 0.14, END):
            if any(l["t0"] < c <= T for c in (CUT1, CUT2)):
                continue                     # a caption does not live across a jump cut
            k = ease(T, l["t0"] - 0.04, l["t0"] + 0.09, 0.9, 1.0, "back")
            st.put(caps[i].get(), W / 2, 1492, scale=k, depth=0.0)


def draw_luna(st, T, zoom=1.0, pivot=(540, 1100)):
    state, _ = luna_state(T)
    dx, dy, turn, sx, sy, lean = luna_move(T)
    x = pivot[0] + (L_POS[0] - pivot[0]) * zoom + dx
    y = pivot[1] + (L_POS[1] - pivot[1]) * zoom + dy
    st.put(luna.get(**state), x, y, scale=zoom * lean, turn=turn, sx=sx, sy=sy, anchor=L_ANCHOR, depth=1.0)


def draw_timo(st, T, zoom=1.0, pivot=(540, 1100), drop=0.0, stretch=1.0):
    state, i = timo_state(T)
    t = on_twos(T)
    acc = talk[i].accent(T - LINE[i]["t0"]) if i and not LINE[i]["ground"] else 0.0
    x = pivot[0] + (T_POS[0] - pivot[0]) * zoom
    y = pivot[1] + (T_POS[1] - pivot[1]) * zoom + drop + acc * 4
    breathe = 1 + math.sin(t * 1.9) * 0.004
    lean = 0.0   # he leans a little toward her while he speaks, and straightens after
    for j, l in LINE.items():
        if l["who"] == "T" and not l["ground"]:
            lean = max(lean, ease(t, l["t0"] - 0.12, l["t0"] + 0.25, 0.0, 1.0, "out") - ease(t, l["t1"] + 0.1, l["t1"] + 0.5, 0.0, 1.0, "io"))
    st.put(timo.get(**state), x - lean * 12, y + lean * 6, scale=zoom, turn=wobble(t, 7.0, 0.8) * 0.3 - acc * 0.9 - lean * 2.6, sx=1 / stretch ** 0.5, sy=stretch * breathe,
           anchor=T_ANCHOR, depth=0.82)


# ---------------------------------------------------------------- the three shots

def shot1(st, t, T):
    st.put(bg_far.get(), W / 2 + 40, H / 2 - 30, depth=0.45)
    wind_marks(st, T, 11, 0.5)
    # Timo, face down on the path behind her; he speaks into the ground and only his back gives a small heave
    i = speaking(T, "T")
    acc = talk[i].accent(T - LINE[i]["t0"]) if i else 0.0
    t2 = on_twos(T)
    heave = acc * 0.10 + (math.sin(t2 * 2.2) * 0.012)
    up = LINE["T2"]["t0"] - 0.10 <= T < LINE["T2"]["t1"] + 0.12          # he objects with one finger and does not lift his head
    a, b = pop(t2, LINE["T2"]["t0"] - 0.10, 0.06)
    c, d = pop(t2, LINE["T2"]["t1"] + 0.12, 0.05)
    state = {"0scarf": ("1", "2")[int(T * 5.3 + math.sin(T * 1.7)) % 2], "1pose": "finger" if up else None}
    px, py = 780, 752
    shadow = np.zeros((H, W, 4), np.float32)   # he lies ON the hill: a soft shadow under him and grass in front of him
    cv2.ellipse(shadow, (px - 20, py + 8), (330, 26), -11, 0, 360, (0, 0, 0, 0.28), -1, cv2.LINE_AA)
    shadow = cv2.GaussianBlur(shadow, (0, 0), 9)
    st.put(shadow, W / 2, H / 2, depth=0.55)
    st.put(prone.get(**state), px, py, turn=-11 + acc * 1.6, sx=a * c, sy=(1 + heave) * b * d, anchor=(0.86, 0.85), depth=0.55)
    tufts = np.zeros((H, W, 4), np.float32)
    rng = np.random.default_rng(5)
    for k in range(46):
        u = rng.uniform(-1, 1)
        bx, by = px - 20 + u * 335, py + 6 - u * 65 + rng.uniform(-4, 14)
        hgt, lean = rng.uniform(26, 58), rng.uniform(14, 34) + math.sin(t2 * 6 + k) * 5
        col = ((0.42, 0.50, 0.27, 1.0), (0.34, 0.43, 0.22, 1.0), (0.50, 0.56, 0.33, 1.0))[k % 3]
        cv2.line(tufts, (int(bx), int(by)), (int(bx + lean), int(by - hgt)), col, 5, cv2.LINE_AA)
    tufts[..., :3] *= tufts[..., 3:4]
    st.put(tufts, W / 2, H / 2, depth=0.55)
    draw_luna(st, T)
    caption(st, T)


def shot2(st, t, T):
    st.put(bg_near.get(), W / 2 - 60, H / 2 - 40, depth=0.45)
    wind_marks(st, T, 23, 0.5)
    draw_timo(st, T)
    draw_luna(st, T)
    caption(st, T)


def shot3(st, t, T):
    z = 1.12
    st.put(bg_near.get(), W / 2 - 110, H / 2 - 20, scale=1.1, depth=0.45)
    wind_marks(st, T, 37, 0.5)
    if T < SINK1 + 0.3:   # he goes straight down, the way a puppet leaves
        t2 = on_twos(T)
        drop = -ease(t2, SINK0 - 0.17, SINK0, 0.0, 26.0, "out") + ease(t2, SINK0, SINK1, 0.0, 2326.0, "in")   # up a little, then gone
        draw_timo(st, T, z, drop=drop, stretch=1 + 0.16 * clamp((t2 - SINK0) / (SINK1 - SINK0)))
    draw_luna(st, T, z)
    caption(st, T)
    if T >= PLATE:
        k = ease(T, PLATE, PLATE + 0.22, 0.6, 1.0, "back")
        st.put(plate.get(), W / 2, 440, scale=k * 1.04, alpha=clamp((T - PLATE) / 0.08), depth=0.0)   # in the sky, where he stood: her face stays clear


film = Film(WORK, ROOT / "ads" / "20-sketches" / "venuvoy-sketch-01-the-switch")
film.shot(0.0, CUT1, shot1, camera(1.0, 0.0))
film.shot(CUT1, CUT2, shot2, camera(1.0, 3.3))
film.shot(CUT2, END, shot3, camera(1.0, 6.1))

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
