"""Still posts made from the same drawings as the films: "She said. He said." (swipe posts of two or three slides),
covers, the profile picture. No voice, no model: every picture here is free.

    python3 src/film/toon/stills.py                 every post whose background exists, into ads/22-stills/<post>/
    python3 src/film/toon/stills.py walkable hill   only the posts named
    python3 src/film/toon/stills.py --list          what is ready and what waits for a background

Each slide is composed on the films' own 1080 x 1920 stage and saved twice: tall (9:16) and feed (4:5, rows 180 to
1530, the same crop as the films), so nothing important may sit above row 190 or below row 1510.
A slide: {"bg": (drawing, point in it, where that lands, size), "look": "wind" | "calm",
          "luna": (x, y, size, lean) with "face" (None, "guilty", "defend", "read", "down", "turn"), "mouth" (None, "C", "A", "E", "O"), "tilt" (degrees),
          "timo": (x, y, size) with "eyes" (None, "lens", "narrow", "shut"), "tmouth",
          "hand": "hand" | "phone" (her free arm), "say": [("L" | "T", "the words", (x, y))], "chip": (x, y, size)}
"""
import json
import subprocess
import sys
from pathlib import Path

import cv2
import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE))
import duo  # noqa: E402
from toon import H, W, Picture, Stage, bend  # noqa: E402

OUT = ROOT / "ads" / "22-stills"
WORK = ROOT / "work" / "stills"
CROP = (180, 1530)


class AnyArt(duo.Art):
    """Finds a drawing of ANY film (a background an episode paid for is free here)."""

    def __init__(self):
        super().__init__("stills")
        for d in sorted((duo.ASSETS).glob("*/art")):
            self.dirs.append((d, ".webp"))
        for d in sorted((ROOT / "work").glob("*/art")):
            self.dirs.append((d, ".png"))


art = AnyArt()
_sprites, _bgs, _pieces = {}, {}, {}
S_L, S_T = 0.64, 0.50


def luna(look):
    if look not in _sprites:
        _sprites[look] = duo.luna(art, S_L, look)
    return _sprites[look]


def timo(base="timo_clean"):
    if base not in _sprites:
        _sprites[base] = duo.timo(art, S_T, base)
    return _sprites[base]


def compose(s):
    st = Stage()
    if isinstance(s["bg"], str):          # a flat brand colour, "#f4eee1"
        c = s["bg"].lstrip("#")
        st.clear(tuple(int(c[i:i + 2], 16) / 255 for i in (0, 2, 4)))
    else:
        name, src, at, k = s["bg"]
        if name not in _bgs:
            _bgs[name] = duo.background(art, name)
        img = _bgs[name].img
        h, w = img.shape[:2]
        st.put(img, at[0] + (w / 2 - src[0]) * k, at[1] + (h / 2 - src[1]) * k, scale=k)
    if s.get("finger"):                   # his raised finger, from below
        if "finger" not in _pieces:
            _pieces["finger"] = duo.finger(art)
        x, y, k = s["finger"]
        st.put(_pieces["finger"], x, y, scale=k, turn=-5, anchor=(0.5, 1.0))
    if s.get("timo"):
        x, y, k = s["timo"]
        state = {"2eyes": s.get("eyes"), "3mouth": s.get("tmouth")}
        st.put(timo(s.get("timo_base", "timo_clean")).get(**{a: b for a, b in state.items() if b}), x, y, scale=k, turn=s.get("timo_lean", 0.0), anchor=duo.T_ANCHOR)
    if s.get("luna"):
        x, y, k, lean = s["luna"]
        look = s.get("look", "wind")
        state = {"1face": s.get("face"), "3mouth": s.get("mouth"), "0wind": s.get("wind")}
        pic = luna(look).get(**{a: b for a, b in state.items() if b})
        pic = bend(pic, (duo.L_NECK[0] * S_L, duo.L_NECK[1] * S_L), duo.L_STILL * S_L, duo.L_FULL * S_L, s.get("tilt", 0.0), 0.0, 0.0)
        st.put(pic, x, y, scale=k, turn=lean, anchor=duo.L_ANCHOR)
        if s.get("hand"):
            if s["hand"] not in _pieces:
                _pieces[s["hand"]] = duo.phone(art, S_L) if s["hand"] == "phone" else duo.hand(art, S_L)
            st.put(_pieces[s["hand"]], x, y - s.get("lift", 100), scale=k, turn=lean, anchor=duo.L_ANCHOR)
    return st


# places that work (the films' own)
HILL2 = ("bg_hill_1", (370, 1660), (238, 596), 1.28)
HILL3 = ("bg_hill_1", (370, 1660), (250, 440), 1.50)
HILLW = ("bg_hill_2", (335, 1200), (196, 640), 0.87)
GATES = ("bg_gates_1", (1100, 1000), (800, 200), 1.02)
L_CLOSE, L_TWO, L_WIDE = (470, 1600, 1.30, 0.0), (372, 1556, 1.0, -1.2), (362, 1545, 0.92, 0.0)
T_TWO, T_LOW = (872, 935, 1.0), (872, 1110, 0.92)        # T_LOW only with the calm look (no flying hair under his mouth)
SAY_LOW, SAY_L1 = (540, 1400), (540, 1425)
CHIP = (300, 262, 0.6)


def she_he(bg_close, bg_two, look, hers, his, face=None, mouth="A", his_eyes="lens", third=None, tilt=3.0):
    """Slide 1: her face close, her line. Slide 2: wider, Timo behind her looking at us, his line (and the chip).
    third: ("L", words, face) a last slide where she answers."""
    two_t = T_LOW if look == "calm" else T_TWO
    slides = [{"bg": bg_close, "look": look, "luna": L_CLOSE, "face": face, "mouth": mouth, "tilt": tilt, "say": [("L", hers, (540, 1415))]},
              {"bg": bg_two, "look": look, "luna": L_TWO, "face": "guilty" if his_eyes == "lens" and his else None, "tilt": -1.5, "timo": two_t, "eyes": his_eyes,
               "tmouth": "C" if his else None, "say": [("T", his, SAY_LOW)] if his else [], "chip": None if third else CHIP}]
    if third:
        who, words, f = third
        slides.append({"bg": bg_two, "look": look, "luna": L_TWO, "face": f, "mouth": "A" if f is None else "C", "tilt": 2.5, "timo": two_t, "eyes": "shut" if who == "L" else "lens",
                       "say": [(who, words, SAY_LOW)], "chip": CHIP})
    return slides


# ---------------------------------------------------------------- the posts (name: slides). More are added as episodes bring backgrounds.
POSTS = {
    "walkable": she_he(HILL3, HILL2, "wind", "It's walkable.", "Everything is walkable, Luna.\nThat's not the question."),
    "photos": [  # she wins this one
        {"bg": HILL3, "look": "wind", "luna": L_CLOSE, "face": None, "mouth": "C", "tilt": 3.0, "say": [("L", "You said the hill was pointless.", (540, 1415))]},
        {"bg": HILL2, "look": "wind", "luna": L_TWO, "face": None, "tilt": -1.5, "timo": T_TWO, "eyes": "lens", "tmouth": "C", "say": [("T", "It was.", SAY_LOW)]},
        {"bg": HILL2, "look": "wind", "luna": L_TWO, "face": None, "mouth": "A", "tilt": 3.0, "timo": T_TWO, "eyes": "shut", "say": [("L", "You took nine photos.", SAY_LOW)], "chip": CHIP}],
    "restday": she_he(HILL3, HILL2, "wind", "I planned us a rest day.", "What's on the rest day.", third=("L", "...A run.", "guilty")),
    "eat": she_he(GATES, GATES, "calm", "We'll eat at the stadium.", "We'll queue at the stadium.\nEating is separate."),
    "extratime": she_he(GATES, GATES, "calm", "Extra time! More football!", "Our train."),
}


# places the episodes paid for (copied into work/duo/art under their own names)
DUSK2 = ("bg_dusk_1", (560, 1380), (330, 760), 0.80)
DUSK3 = ("bg_dusk_1", (560, 1380), (300, 640), 0.95)
STREET2 = ("bg_street_1", (700, 1150), (330, 640), 1.0)
STREET3 = ("bg_street_1", (700, 1150), (300, 540), 1.2)
AF2 = ("bg_afstand_1", (300, 760), (170, 503), 0.78)
AF3 = ("bg_afstand_1", (300, 760), (150, 420), 0.94)
TENNIS2 = ("bg_tennis_1", (430, 960), (330, 560), 0.92)
TENNIS3 = ("bg_tennis_1", (430, 960), (265, 440), 1.06)

POSTS.update({
    "feelitout": she_he(GATES, GATES, "calm", "We don't need a plan.\nWe'll feel it out.", "Last time we felt it out,\nwe heard the first goal\nfrom a bus."),
    "twogames": she_he(DUSK3, DUSK2, "calm", "Two games in one day.\nI checked. It works.", "Show me the bit\nin between.", his_eyes="narrow"),
    "toprow": she_he(AF3, AF2, "calm", "Top row!\nYou can see everything.", "I can see the weather."),
    "climbing": she_he(DUSK3, DUSK2, "calm", "There's a climbing wall\nnear the stadium!", "There's a bench\nnear the stadium.", third=("L", "The bench is in the plan.", None)),   # she wins this one
    "statue": she_he(STREET3, STREET2, "wind", "The group chat says\nmeet at the statue.", "There are four statues."),
    "shower": she_he(STREET3, STREET2, "wind", "It's only a shower.", "It's been a shower\nsince Thursday."),
    "quiet": she_he(TENNIS3, TENNIS2, "calm", "Why's everyone so quiet?", "Luna.", his_eyes="shut"),
})


CREAM = "#f4eee1"
T_SOLO, L_SOLO = (560, 1410, 1.42), (500, 1420, 1.24, 0.0)      # one of them alone, large
GUIDE_SAY = (540, 1420)
POSTS["read-timo"] = [   # "How to read Timo, by Luna": one drawn look a slide
    {"bg": CREAM, "timo": T_SOLO, "title": ("How to read Timo\nby Luna", (540, 300), 76), "say": [("L", "This is his happy face.", GUIDE_SAY)]},
    {"bg": CREAM, "timo": T_SOLO, "eyes": "narrow", "say": [("L", "Also his happy face.\nHe's doing maths.", GUIDE_SAY)]},
    {"bg": CREAM, "timo": T_SOLO, "eyes": "lens", "say": [("L", "This one's for you.\nI'm not supposed to see it.", GUIDE_SAY)]},
    {"bg": CREAM, "timo": T_SOLO, "eyes": "shut", "say": [("L", "Resting. He does this\nwhen I read him the plan.", GUIDE_SAY)]},
    {"bg": CREAM, "finger": (560, 1560, 2.3), "say": [("L", "He has a question.\nIt's always one question.", (540, 1440))]},
    {"bg": CREAM, "timo": T_SOLO, "eyes": "lens", "tmouth": "C", "say": [("T", "It's four faces, Luna.", GUIDE_SAY)], "chip": (540, 290, 0.7)},
]
POSTS["read-luna"] = [   # "How to read Luna, by Timo. Under protest."
    {"bg": CREAM, "look": "calm", "luna": L_SOLO, "title": ("How to read Luna\nby Timo. Under protest.", (540, 292), 64), "say": [("T", "Default. Tells you nothing.\nCould be a goal, could be a bus.", GUIDE_SAY)]},
    {"bg": CREAM, "look": "calm", "luna": L_SOLO, "face": "read", "hand": "phone", "lift": 60, "say": [("T", "She has found something\nin the planner. Brace.", GUIDE_SAY)]},
    {"bg": CREAM, "look": "calm", "luna": L_SOLO, "face": "guilty", "say": [("T", "She has pressed it.", GUIDE_SAY)]},
    {"bg": CREAM, "look": "calm", "luna": L_SOLO, "face": "defend", "say": [("T", "She is telling you about it.\nNot me. You.", GUIDE_SAY)]},
    {"bg": CREAM, "look": "calm", "luna": L_SOLO, "face": "down", "say": [("T", "It's a run.", GUIDE_SAY)]},
    {"bg": CREAM, "look": "calm", "luna": L_SOLO, "face": "turn", "mouth": "tA", "hand": "hand", "lift": 60, "say": [("T", "This is the whole defence.", GUIDE_SAY)]},
    {"bg": CREAM, "look": "calm", "luna": L_SOLO, "mouth": "A", "say": [("L", "He loves it.", GUIDE_SAY)], "chip": (540, 290, 0.7)},
]


GREEN = "#1e4d38"


def square(name, slide, size=1080, window=(0, 420)):
    """A square picture cut from a composed slide (the profile picture, a highlight cover): window = its left, top."""
    st = compose(slide)
    f = (np.clip(st.frame, 0, 1)[..., ::-1] * 255 + 0.5).astype(np.uint8)
    x, y = window
    d = OUT / "profile"
    d.mkdir(parents=True, exist_ok=True)
    cv2.imwrite(str(d / f"venuvoy-{name}_{size}.png"), cv2.resize(f[y:y + 1080, x:x + 1080], (size, size), interpolation=cv2.INTER_AREA))


def profile():
    # her face low and sunny, his face above hers, eyes on us; everything that matters inside the circle
    square("profile", {"bg": GREEN, "look": "calm", "luna": (420, 1500, 0.98, -2.0), "timo": (742, 1180, 0.98), "eyes": "lens"}, window=(0, 420))
    square("highlight-the-two", {"bg": GREEN, "look": "calm", "luna": (500, 1585, 1.2, 0.0)}, window=(0, 420))
    square("highlight-trips", {"bg": GREEN, "look": "calm", "luna": (500, 1585, 1.2, 0.0), "face": "read"}, window=(0, 420))
    square("highlight-he-said", {"bg": GREEN, "timo": (560, 1500, 1.5), "eyes": "lens"}, window=(0, 420))
    square("highlight-ask-timo", {"bg": GREEN, "finger": (540, 1730, 2.2)}, window=(0, 420))
    print("profile picture and four highlight covers ->", OUT / "profile")


def typeset(posts):
    items = []
    for name, slides in posts.items():
        for n, s in enumerate(slides, 1):
            for k, (who, words, _) in enumerate(s.get("say", [])):
                longest = max(len(x) for x in words.split("\n"))     # big type for a short line, smaller for a long one: never more than three lines
                items.append({"name": f"{name}_{n}_{k}", "kind": "say", "who": who, "text": words, "size": 76 if longest <= 20 else 68 if longest <= 26 else 60, "width": 1000})
            if s.get("title"):
                items.append({"name": f"{name}_{n}_title", "kind": "title", "text": s["title"][0], "size": s["title"][2] if len(s["title"]) > 2 else 84, "width": 960})
    items += [{"name": "chip", "kind": "chip"}, {"name": "plate", "kind": "plate"}]
    WORK.mkdir(parents=True, exist_ok=True)
    (WORK / "spec.json").write_text(json.dumps({"out": "work/stills/text", "items": items}, ensure_ascii=False, indent=1))
    subprocess.run(["node", str(HERE / "cards.mjs"), str(WORK / "spec.json")], check=True, stdout=subprocess.DEVNULL)


def ready(slides):
    return all(isinstance(s["bg"], str) or art.has(s["bg"][0]) for s in slides)


def save(st, path):
    f = (np.clip(st.frame, 0, 1)[..., ::-1] * 255 + 0.5).astype(np.uint8)
    cv2.imwrite(str(path) + "_9x16.jpg", f, [cv2.IMWRITE_JPEG_QUALITY, 95])
    cv2.imwrite(str(path) + "_4x5.jpg", f[CROP[0]:CROP[1]], [cv2.IMWRITE_JPEG_QUALITY, 95])


def build(names=None):
    todo = {n: s for n, s in POSTS.items() if (not names or n in names) and ready(s)}
    typeset(todo)
    chip = Picture(WORK / "text" / "chip.png")
    for name, slides in todo.items():
        d = OUT / name
        d.mkdir(parents=True, exist_ok=True)
        for n, s in enumerate(slides, 1):
            st = compose(s)
            for k, (who, words, at) in enumerate(s.get("say", [])):
                pic = Picture(WORK / "text" / f"{name}_{n}_{k}.png").img
                ys = np.where(pic[..., 3].max(axis=1) > 0.3)[0]
                low = at[1] + 62                                     # the words stand ON this line (their last row), so a longer text grows upward
                st.put(pic, at[0], low - (ys.max() - pic.shape[0] / 2), depth=0.0)
            if s.get("title"):
                st.put(Picture(WORK / "text" / f"{name}_{n}_title.png").img, s["title"][1][0], s["title"][1][1], depth=0.0)
            if s.get("chip"):
                x, y, k = s["chip"]
                st.put(chip.img, x, y, scale=k, depth=0.0)
            if s.get("plate"):
                st.put(Picture(WORK / "text" / "plate.png").img, s["plate"][0], s["plate"][1], scale=s["plate"][2], depth=0.0)
            save(st, d / f"venuvoy-still-{name}-{n}")
        print(name, len(slides), "slides ->", d)


if __name__ == "__main__":
    if "--profile" in sys.argv:
        profile()
    elif "--list" in sys.argv:
        for n, s in POSTS.items():
            print(f'{n:12s} {"ready" if ready(s) else "waits for " + ", ".join(sorted({x["bg"][0] for x in s if not art.has(x["bg"][0])}))}')
    else:
        build([a for a in sys.argv[1:] if not a.startswith("--")] or None)
