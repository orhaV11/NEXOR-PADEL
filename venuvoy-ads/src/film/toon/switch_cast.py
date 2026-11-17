"""The drawn cast of "The Switch": each character is ONE base drawing with patches laid on it.

Luna (base luna_w1, 1792 x 2400): expressions replace the whole face area, a blink replaces the eye area, talking
mouths replace the mouth area. Her turned head is a second set of patches. Timo (base timo_up_1, 1536 x 2752): eyes
and mouth patches. Boxes are in the pixels of the base drawing.
"""
from pathlib import Path

from toon import Picture, Sprite

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

L_FACE, L_EYES, L_MOUTH = (400, 770, 930, 1325), (405, 880, 925, 1088), (505, 1122, 848, 1318)
L_HEAD, L_TMOUTH = (220, 300, 1045, 1440), (560, 1100, 975, 1312)
L_HAIR, L_SCARF = (1045, 380, 1792, 1300), (780, 1300, 1792, 2150)   # what the wind moves
T_SCARF = (1040, 560, 1536, 1340)
T_EYES, T_BROWS, T_MOUTH = (418, 662, 722, 772), (392, 556, 768, 774), (498, 898, 732, 1072)


def luna(scale):
    s = Sprite(ART / "luna_w1.png", scale=scale)
    for w in ("1", "2"):   # two more drawings of the hair tips and the scarf tail in the wind
        s.add("0hair", w, ART / f"luna_wind{w}.png", L_HAIR)
        s.add("0scarf", w, ART / f"luna_wind{w}.png", L_SCARF)
    for name, file in (("guilty", "luna_guilty"), ("defend", "luna_defend"), ("side", "luna_read"), ("read", "luna_read2")):
        s.add("1face", name, ART / f"{file}.png", L_FACE)
    s.add("1face", "turn", ART / "luna_turn.png", L_HEAD)  # the whole head: its skin and its mouths come from the same drawing
    s.add("2eyes", "shut", ART / "luna_blink.png", L_EYES)
    for m in "CAEO":
        s.add("3mouth", m, ART / f"luna_m{m}.png", L_MOUTH)
    for m in "CAE":  # the turned head has its own mouths, in another place
        s.add("3mouth", "t" + m, ART / f"luna_turn_m{m}.png", L_TMOUTH)
    return s


def timo(scale):
    s = Sprite(ART / "timo_up_1.png", scale=scale, pad_bottom=1100)  # his legs run on below the drawing, behind Luna
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
        s.add("0scarf", w, ART / f"prone_wind{w}.png", (880, 300, 1800, 800))
    s.add("1pose", "finger", ART / "prone_finger.png", (600, 200, 880, 880))   # one finger raised, face still in the grass
    return s


def background(name, scale):
    return Picture(ART / f"{name}.png", scale=scale)
