"""The drawn cast of "The Switch": each character is ONE base drawing with patches laid on it.

Luna (base luna_w1, 1792 x 2400): expressions replace the whole face area, a blink replaces the eye area, talking
mouths replace the mouth area. Her turned head is a second set of patches. Timo (base timo_up_4, 1536 x 2752): eyes
and mouth patches. Boxes are in the pixels of the base drawing. Each sprite is given a point of plain skin, so that a
redraw that came back a shade off is brought to the base drawing's skin and no box shows on the face.
"""
from pathlib import Path

import cv2
import numpy as np

from toon import Picture, Sprite, align, key_out, patch_mask, premul, read

ROOT = Path(__file__).resolve().parent.parent.parent.parent
KEEP, WORK = ROOT / "src" / "film" / "assets" / "toon" / "switch" / "art", ROOT / "work" / "switch" / "art"
USED = set()   # every drawing the film really uses (pack.py keeps exactly these)


class _Art:
    """ART / "name.png": the stored copy (lossless WebP in src/film/assets/toon/switch/art) when there is one, else the
    scratch copy in work/."""

    def __truediv__(self, name):
        stem = Path(name).stem
        USED.add(stem)
        kept = KEEP / f"{stem}.webp"
        return kept if kept.exists() else WORK / f"{stem}.png"


ART = _Art()

L_FACE, L_EYES, L_MOUTH = (400, 770, 930, 1325), (405, 880, 925, 1088), (450, 1110, 895, 1318)   # the mouth box holds the widest (teeth) mouth whole
L_PAD = 320   # rows added below Luna's drawing
L_HEAD, L_TMOUTH = (220, 300, 1045, 1440), (560, 1100, 975, 1312)
T_SCARF = (1040, 560, 1536, 1340)
T_EYES, T_BROWS, T_MOUTH = (418, 662, 722, 772), (392, 556, 768, 774), (498, 898, 732, 1072)


def luna(scale):
    s = Sprite(ART / "luna_w1.png", scale=scale, skin=(665, 960), pad_bottom=L_PAD, pad="repeat")   # her coat runs on below the drawing: a shake of the camera never shows its end
    for w in ("1", "2"):   # two more drawings of the hair tips and the scarf tail in the wind: only what changed is taken
        # the box starts well left of where the scarf tail leaves the knot: a box edge across the tail showed as a step
        s.add("0wind", w, ART / f"luna_wind{w}.png", (640, 300, 1792, 2150), mode="diff", grow=11, feather=6)
    # reading her phone: two drawings with the eyes in two places (she reads along the line); "down": she looks down at where he fell
    for name, file in (("guilty", "luna_guilty"), ("defend", "luna_defend"), ("read", "luna_read3"), ("read_b", "luna_read3b"), ("down", "luna_down")):
        s.add("1face", name, ART / f"{file}.png", L_FACE)
    for name in ("guilty", "defend"):   # these two faces have their own drawn blink (their brows reach into the eyes)
        s.add("1face", name + "_shut", ART / f"luna_{name}_blink.png", L_FACE)
    s.add("1face", "turn", ART / "luna_turn_dots.png", L_HEAD)  # the whole head, turned to him (redrawn once more so that its eyes are the same dots as on her other faces)
    s.add("2eyes", "shut", ART / "luna_blink.png", L_EYES)
    s.add("2eyes", "scan", ART / "luna_read3b.png", L_EYES)   # the reading eyes a little further along the line
    for m in "CAEO":
        s.add("3mouth", m, ART / f"luna_m{m}.png", L_MOUTH)
    for m in "CAE":  # the turned head has its own mouths, in another place
        s.add("3mouth", "t" + m, ART / f"luna_turn_m{m}.png", L_TMOUTH)
    return s


def _arm(file, box, scale):
    """Her other arm, taken from a drawing in which it comes up beside her: a piece on Luna's own canvas, to be laid
    over her with the very same placing, so that it can come up from below the frame and drop again."""
    base = read(ART / "luna_w1.png")[..., :3]
    other, _ = align(base, read(ART / file)[..., :3], None)
    m = patch_mask(base, other, box, thr=0.07, grow=9, feather=3)
    m[-14:] = m[-15]            # the mask fades out at the edge of its box; at the bottom the sleeve must run on whole
    m[:1575, :1325] = 0         # the end of her scarf lies here and every redraw shifts it a little: it is not part of the arm
    n, lab, stats, _ = cv2.connectedComponentsWithStats((m > 0.25).astype(np.uint8))   # the arm only: a redraw also shifts a scrap
    big = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))                              # of scarf fringe, which must not travel with it
    m = m * cv2.dilate((lab == big).astype(np.uint8), np.ones((9, 9), np.uint8))
    piece = key_out(other)
    piece[..., 3] *= m
    piece = np.concatenate([piece, np.repeat(piece[-1:], L_PAD, axis=0)], axis=0)   # the same canvas as Luna's, so the same placing fits
    return premul(cv2.resize(piece, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA))


def phone(scale):
    return _arm("luna_read3b.png", (1150, 1480, 1740, 2400), scale)   # she holds up her phone and reads from it


def hand(scale):
    return _arm("luna_shrug.png", (1150, 1380, 1792, 2400), scale)    # an open hand: what do you want from me


def timo(scale):
    s = Sprite(ART / "timo_up_4.png", scale=scale, pad_bottom=1100, skin=(570, 610))   # timo_up_3 with two mud blots on the scarf painted out by hand  # his legs run on below the drawing, behind Luna
    for w in ("1", "2"):
        s.add("0scarf", w, ART / f"timo_wind{w}.png", T_SCARF)
    s.add("2eyes", "lens", ART / "timo_lens.png", T_EYES)
    s.add("2eyes", "narrow", ART / "timo_narrow.png", T_BROWS)
    s.add("2eyes", "shut", ART / "timo_blink_b.png", T_BROWS)
    for m, file in (("C", "timo_mC2"), ("A", "timo_mA2"), ("O", "timo_mO")):
        s.add("3mouth", m, ART / f"{file}.png", T_MOUTH)
    return s


def prone(scale):
    s = Sprite(ART / "timo_prone_1.png", scale=scale)
    for w in ("1", "2"):   # the loose end of his scarf in the wind
        s.add("0scarf", w, ART / f"prone_wind{w}.png", (700, 250, 1900, 1050), mode="diff", grow=11, feather=4)
    s.add("1pose", "finger", ART / "prone_finger.png", (600, 200, 880, 880))   # one finger raised, face still in the grass
    return s


def background(name, scale):
    return Picture(ART / f"{name}.png", scale=scale)
