"""The picture of the short "Less Than You" (vlog 16, "run"): one shot on a riverside path at first light.

    python3 src/film/toon/ep_run.py --sheet        a contact sheet of the whole film (work/run/sheet.jpg)
    python3 src/film/toon/ep_run.py --stills 0.1 "L1+0.3" "T2+0.4" "T2@flipped+0.3" "L3+0.3" "look+0.3" "T4+0.5" "T4.end+0.5" "plate+1.0"
    python3 src/film/toon/ep_run.py                the film (ads/21-vlog/venuvoy-vlog-16-run-en_...mp4)

Run ep_run_sound.py first: every time below is read from its timeline (the stretch "look" is his look into the lens).

From the first frame Luna stares down at the phone in her hand, oh; behind her Timo stands very upright in a white
towelling sweatband, already looking at us, proud of it. She rounds on him ("Why is there a run in the plan?"); he
looks down at her: "There's a switch. I flipped it." She looks at us, lowers the phone, rounds on him again ("You hate
running!"); the music stops dead, his eyes come to the lens: "I hate it less than you." She looks at us, caught.
He wins, on his feet, in the sweatband.

One thing is this film's own and made here (nothing bought): the band reaches 10 to 25 px into the box of his brows,
which the kit's blink and narrowed-brow drawings replace whole, so each blink would wipe the band's lower edge. Those
two drawings are made again with the band on them (timo_band_blink, timo_band_narrow, beside the redraw).
"""
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import duo  # noqa: E402
from toon import align, read  # noqa: E402
from vlog import ROOT, Vlog  # noqa: E402

ART = ROOT / "work" / "run" / "art"


def _with_band(src, name):
    """The kit's drawing `src` (a whole Timo with other brows or shut eyes) with the sweatband of timo_band laid on it,
    so that the patch taken from its brow box keeps the band's lower edge."""
    art = duo.Art("run")
    if art.has(name):
        art / name
        return
    clean = read(art / "timo_clean")[..., :3]
    band = read(art / "timo_band")[..., :3]
    other, _ = align(clean, read(art / src)[..., :3], duo.T_BROWS)
    changed = np.abs(clean - band).max(axis=2) > 0.12
    region = np.zeros(changed.shape, bool)
    region[360:660, 250:1060] = True
    white = (band.min(axis=2) > 0.70) & region
    white = cv2.morphologyEx(white.astype(np.uint8), cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    near = cv2.dilate(white, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (31, 31))).astype(bool)
    m = (white.astype(bool) | (near & changed)) & region
    m = cv2.morphologyEx(m.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))
    m = cv2.GaussianBlur(cv2.dilate(m, np.ones((3, 3), np.uint8)).astype(np.float32), (0, 0), 0.8)[..., None]
    out = other * (1 - m) + band * m
    cv2.imwrite(str(ART / f"{name}.png"), (np.clip(out[..., ::-1], 0, 1) * 255 + 0.5).astype(np.uint8))
    art / name
    print("made", name)


_with_band("timo_blink_b", "timo_band_blink")
_with_band("timo_narrow", "timo_band_narrow")

# a still, cold first light: her calm hair (nothing flies, so he can stand lower, his band well inside the 4:5 version);
# him in the sweatband. The loudness of the voices leads the mouths; the words the joke rests on are set by hand.
# Her two lines are said to him on the turned head: "Why", "run", "plan", "hate" and "run-" get the wide mouth (alarm,
# then a shout); nothing gets its clenched grin.
v = Vlog("run", "ads/21-vlog/venuvoy-vlog-16-run", luna="calm", timo="timo_band", drive=True,
         extra_eyes={"shut": ("timo_band_blink", duo.T_BROWS), "narrow": ("timo_band_narrow", duo.T_BROWS)},
         said={"why": "O A", "there": "C C", "a": "C", "run": "A C", "in": "C", "the": "C", "plan": "M A C",
               "theres": "C A C", "switch": "O C", "i": "A", "flipped": "C C M", "it": "C",
               "you": "O", "hate": "A C M", "running": "C A C C",
               "less": "C A C", "than": "C A C"})

# drawing, a point in it (the misty river under the far trees), where that lands in the frame, size: the river and the
# mist over her head on the left; the dawn glow behind his head
BG = ("bg_river_1", (450, 1300), (250, 470), 1.05)
LUNA_AT = (345, 1490, 1.16, 1.0)          # close
TIMO_AT = (865, 960, 1.12)                # behind her, upright; his hair top inside the 4:5 version, his ear and the band's end inside the frame

v.shot("start", bg=BG, luna=LUNA_AT, timo=TIMO_AT)

# ---- her face
v.faces(("start", "down"),                # oh: eyes down at the phone in her hand
        ("L1+0.02", "turn"),              # she rounds on him: "Why is there a run in the plan?" (on her first word: the turned head
                                          # is a closed smile while silent, and she is alarmed)
        ("L1@plan.end-0.08", "guilty"),   # as "plan" dies away (the recogniser stretches it): worried brows, a sideways look up at him: there's a switch (she knows switches)
        ("T2@flipped+0.06", "defend"),    # big eyes on us: he flipped it
        ("L3+0.01", "turn"),              # "You hate running!" (her voice starts 0.04 s into the line; the recogniser says 0.18) and she stays turned to him
                                          # in the silence: her closed smile, gotcha
        ("T4@less-0.04", "guilty"),       # "...less than you": the smile goes crooked, the brows worry
        ("T4.end+0.12", "defend"))        # and then at us, caught, with a small duck
v.tilt(L1=3.2, L3=2.6)
v.blinks(luna=["T2@switch", "plate+0.55"],
         timo=["L1@run", "T2@flipped+0.04", "plate+1.05"])  # a satisfied blink on "flipped"; none in her shout, his look at us or his last line

# ---- his eyes: on us from the first frame (proud of the band); down at her while he explains; on us again in the silence
v.eyes(("start", "lens"),
       ("T2-0.12", None),
       ("look", "lens"))

# ---- her phone: up from the first frame; she lowers it when he says he flipped it
v.piece("phone", up=-0.6, down="T2@flipped+0.10", lift=110, dx=-45)

# ---- the words on screen
v.captions(
    on={"L1": "start"},                   # up from frame 1: the first frame is often the cover
    place={"L1": (372, 1395), "T2": (395, 1395)},   # left of the phone in her hand
    text={"L1": "Why is there a run\nin the plan?"},
    hold={"T2": ("There’s a switch.\n", "I flipped it.", "T2@i"),
          "T4": ("I hate it\n", "less than you.", "T4@less")},   # the end of his line arrives when he says it
    off={"T4": "plate-0.05"})
v.plate(x=352, y=300, size=0.74)          # he is standing at the end: the plate beside his head

if __name__ == "__main__":
    v.main()
