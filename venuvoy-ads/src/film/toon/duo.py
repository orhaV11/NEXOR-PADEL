"""The drawn duo of every vlog sketch: Luna as her own phone sees her, Timo standing behind her.

Each is ONE base drawing with patches laid on it (see toon.Sprite). The drawings of the rig were made for the first
sketch and live in src/film/assets/toon/switch/art (lossless WebP; scratch copies in work/switch/art). A sketch's own
new drawings (a background, a thing in her hand, something he wears) live in work/<film>/art and, once packed, in
src/film/assets/toon/<film>/art; drawings added to the rig for all films (calm hair, Timo without the blade of grass)
live in work/duo/art and src/film/assets/toon/duo/art. `Art(film) / "name"` finds a drawing wherever it is and notes
that the film uses it.

Boxes are in the pixels of the base drawings: Luna 1792 x 2400, Timo 1536 x 2752.
"""
import json
from pathlib import Path

import cv2
import numpy as np

from toon import Picture, Sprite, align, key_out, patch_mask, premul, read

ROOT = Path(__file__).resolve().parent.parent.parent.parent
ASSETS = ROOT / "src" / "film" / "assets" / "toon"
RIG = "switch"   # the film whose folders hold the rig


class Art:
    """art / "luna_w1" -> the file of that drawing: this film's stored copy, this film's scratch copy, the rig's stored
    copy, the rig's scratch copy, in that order. Every name asked for is remembered (pack.py keeps exactly those)."""

    def __init__(self, film):
        self.film, self.used = film, set()
        self.dirs = [(ASSETS / film / "art", ".webp"), (ROOT / "work" / film / "art", ".png"), (ASSETS / "duo" / "art", ".webp"), (ROOT / "work" / "duo" / "art", ".png"),
                     (ASSETS / RIG / "art", ".webp"), (ROOT / "work" / RIG / "art", ".png")]

    def __truediv__(self, name):
        stem = Path(name).stem
        self.used.add(stem)
        for d, ext in self.dirs:
            if (d / f"{stem}{ext}").exists():
                return d / f"{stem}{ext}"
        raise FileNotFoundError(f"no drawing called {stem} for the film {self.film}")

    def has(self, name):
        return any((d / f"{Path(name).stem}{ext}").exists() for d, ext in self.dirs)

    def save_used(self):
        p = ROOT / "work" / self.film / "used.json"
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(json.dumps(sorted(self.used), indent=1))


# ---------------------------------------------------------------- Luna
L_FACE, L_EYES, L_MOUTH = (400, 770, 930, 1325), (405, 880, 925, 1088), (450, 1110, 895, 1318)
L_HEAD, L_TMOUTH = (220, 300, 1045, 1440), (560, 1100, 975, 1312)
L_PAD = 320                      # rows added below her drawing: a shake of the camera never shows where it ends
L_SIZE = (1792, 2400 + L_PAD)
L_ANCHOR = (663 / L_SIZE[0], 1680 / L_SIZE[1])       # the knot of her scarf: she turns and squashes about it
L_NECK, L_STILL, L_FULL = (665, 1400), 1560, 1330    # her head turns about the base of her neck; the scarf is where the picture bends
L_CHIN = 1325                    # the lower edge of her face (captions stay below it)
ARM_BOX = (1150, 1380, 1792, 2400)   # where her free arm comes up beside her

# faces: the name used in a film -> the drawing. None is her resting face (sunny, eyes on us).
LUNA_FACES = {"guilty": "luna_guilty", "defend": "luna_defend", "read": "luna_read3", "read_b": "luna_read3b", "down": "luna_down"}
LUNA_OWN_BLINK = ("guilty", "defend")   # faces whose brows reach into the eyes: each has its own drawn blink
LUNA_EYE_BLINK = (None, "down")         # faces on which the plain shut-eyes patch fits
LUNA_MOUTHS = ("A", "E", "O", "C")


def luna(art, scale, look="wind", extra_faces=None):
    """look "wind": her hair and scarf blow (outdoors). look "calm": a neat bob and no flying scarf end (indoors, or
    a still day; the drawings luna_calm2 and luna_turn_calm, redraws of the windy ones with only the hair and scarf
    changed, so that every face still fits).
    extra_faces: {"name": "drawing"} more faces drawn for a film (redraws of luna_w1 with only the face changed)."""
    base = {"wind": "luna_w1", "calm": "luna_calm2"}.get(look, f"luna_{look}")
    s = Sprite(art / base, scale=scale, skin=(665, 960), pad_bottom=L_PAD, pad="repeat")
    if look == "wind":
        for w in ("1", "2"):   # two more drawings of the hair tips and the scarf tail in the wind: only what changed is taken
            s.add("0wind", w, art / f"luna_wind{w}", (640, 300, 1792, 2150), mode="diff", grow=11, feather=6)
    faces = dict(LUNA_FACES, **(extra_faces or {}))
    for name, file in faces.items():
        s.add("1face", name, art / file, L_FACE)
    for name in LUNA_OWN_BLINK:
        s.add("1face", name + "_shut", art / f"luna_{name}_blink", L_FACE)
    turn = {"wind": "luna_turn_dots", "calm": "luna_turn_calm2"}.get(look, f"luna_turn_{look}")
    if art.has(turn):
        s.add("1face", "turn", art / turn, L_HEAD)      # the whole head, turned to him
        for m in "CAE":                                  # the turned head has its own mouths, in another place
            s.add("3mouth", "t" + m, art / f"luna_turn_m{m}", L_TMOUTH)
    s.add("2eyes", "shut", art / "luna_blink", L_EYES)
    s.add("2eyes", "scan", art / "luna_read3b", L_EYES)   # the reading eyes a little further along the line
    for m in "CAEO":
        s.add("3mouth", m, art / f"luna_m{m}", L_MOUTH)
    return s


def arm(art, file, scale, box=ARM_BOX, base="luna_w1", keep_scarf=True):
    """Her free arm with something in its hand, taken from a drawing in which it comes up beside her (a redraw of
    luna_w1 with that one change): a piece on Luna's own canvas, laid over her with the very same placing, so that it
    can come up from below the frame and drop again."""
    b = read(art / base)[..., :3]
    other, _ = align(b, read(art / file)[..., :3], None)
    m = patch_mask(b, other, box, thr=0.07, grow=9, feather=3)
    m[-14:] = m[-15]            # the mask fades out at the edge of its box; at the bottom the sleeve must run on whole
    if keep_scarf:
        m[:1575, :1325] = 0     # the end of her scarf lies here and every redraw shifts it a little: it is not part of the arm
    n, lab, stats, _ = cv2.connectedComponentsWithStats((m > 0.25).astype(np.uint8))
    if n > 1:                   # the arm only: the largest piece
        big = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        m = m * cv2.dilate((lab == big).astype(np.uint8), np.ones((9, 9), np.uint8))
    piece = key_out(other)
    piece[..., 3] *= m
    piece = np.concatenate([piece, np.repeat(piece[-1:], L_PAD, axis=0)], axis=0)   # the same canvas as Luna's
    return premul(cv2.resize(piece, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA))


def phone(art, scale):
    return arm(art, "luna_read3b", scale, (1150, 1480, 1740, 2400))   # she holds up her phone and reads from it


def hand(art, scale):
    return arm(art, "luna_shrug", scale)                              # an open hand: what do you want from me


# ---------------------------------------------------------------- Timo
T_EYES, T_BROWS, T_MOUTH, T_SCARF = (418, 662, 722, 772), (392, 556, 768, 774), (498, 898, 732, 1072), (1040, 560, 1536, 1340)
T_PAD = 1100                     # his legs run on below the drawing, behind Luna
T_SIZE = (1536, 2752 + T_PAD)
T_ANCHOR = (614 / T_SIZE[0], 1514 / T_SIZE[1])       # the middle of his chest
T_NECK, T_STILL, T_FULL = (600, 1150), 1290, 1100
TIMO_MOUTHS = ("A", "O", "C")
TIMO_EYES = {"lens": ("timo_lens", T_EYES), "narrow": ("timo_narrow", T_BROWS), "shut": ("timo_blink_b", T_BROWS)}


def timo(art, scale, base="timo_up_4", extra_eyes=None):
    """base: the drawing of him standing. "timo_up_4" has a blade of grass on his cheek (he fell on the hill); a film
    in another place draws its own base from it with one change (no grass; a hat; sunglasses stay off his eyes).
    extra_eyes: {"name": ("drawing", box)} more eye states drawn for a film."""
    s = Sprite(art / base, scale=scale, pad_bottom=T_PAD, skin=(570, 610))
    for w in ("1", "2"):
        s.add("0scarf", w, art / f"timo_wind{w}", T_SCARF)
    for name, (file, box) in dict(TIMO_EYES, **(extra_eyes or {})).items():
        s.add("2eyes", name, art / file, box)
    for m, file in (("C", "timo_mC2"), ("A", "timo_mA2"), ("O", "timo_mO")):
        s.add("3mouth", m, art / f"{file}", T_MOUTH)
    return s


def finger(art):
    """His raised arm with one finger up, cut from the drawing of him on the ground: it comes up from below the frame."""
    f = key_out(read(art / "prone_finger")[..., :3])[215:770, 640:800]
    return premul(np.ascontiguousarray(f))


def prone(art, scale):
    """Timo face down on grass, far behind her (outdoors, on a slope)."""
    s = Sprite(art / "timo_prone_1", scale=scale)
    for w in ("1", "2"):
        s.add("0scarf", w, art / f"prone_wind{w}", (700, 250, 1900, 1050), mode="diff", grow=11, feather=4)
    s.add("1pose", "finger", art / "prone_finger", (600, 200, 880, 880))
    return s


def background(art, name, scale=1.0):
    return Picture(art / name, scale=scale)
