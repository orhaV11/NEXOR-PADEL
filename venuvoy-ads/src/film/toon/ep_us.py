"""The picture of "This is us" (vlog 00, "us"): Luna introduces the two of them; Timo adds one fact.

    python3 src/film/toon/ep_us.py --sheet | --stills ... | (nothing: the film)
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import math  # noqa: E402

import duo  # noqa: E402
from toon import ease, on_twos  # noqa: E402
from vlog import Vlog  # noqa: E402

v = Vlog("us", "ads/21-vlog/venuvoy-vlog-00-us", luna="wind", timo="timo_clean",
         said={"trips": "C A C", "timo": "C O O O", "i": "C", "plan": "M C C"})     # the recogniser stretched the last words; 'I plan them!' is delight, not a shout
NEAR2 = ("bg_hill_1", (370, 1660), (238, 596), 1.28)
NEAR3 = ("bg_hill_1", (370, 1660), (250, 440), 1.50)
# ---- his finger, drawn BEHIND her (her scarf end swings in front of it, as it should: he stands behind her). It comes
#      up from below, wags twice on his name, waits until his name is said, and goes down evenly in 0.7 s.
FINGER = duo.finger(v.art)
F_UP, F_X, F_Y = -0.4, 962, 1850


def finger(st, t, T):
    down = v.t("L1.end+0.10")
    if not (F_UP <= T < down + 0.75):
        return
    t2 = on_twos(T)
    tw = v.t("L1@timo")
    jab = 0.9 * abs(math.sin((T - tw) * math.pi / 0.16)) if tw <= T < tw + 0.32 else 0.0
    gone = ease(t2, down, down + 0.7, 0.0, 1.0, "io")
    rise = ease(t2, F_UP, F_UP + 0.32, 900.0, 0.0, "back") + gone * 950
    st.put(FINGER, F_X + gone * 40, F_Y + rise - jab * 22, scale=1.5, turn=-5 + math.sin(t2 * 9.0) * 1.2 - jab * 3 + gone * 24, anchor=(0.5, 1.0), depth=0.9)


v.shot("start", bg=NEAR2, luna=(372, 1556, 1.00, -1.2), timo=(872, 935, 1.00), behind=finger)
v.shot("cut1", bg=NEAR3, luna=(345, 1490, 1.16, 1.0), timo=(890, 830, 1.16))

v.rise("up", "up.end")                                  # then the rest of him, evenly, eyes shut
v.faces(("L4-0.10", "turn"),                            # "I plan them!", to him, delighted
        ("cut1", None))                                 # and she beams at us while he tells us the truth
v.eyes(("up", "shut"), ("up.end+0.1", None), ("cut1", "lens"))
v.tilt(L1=3.0, L2=-2.4, L4=2.2)
v.captions(off={"L4": "cut1+0.04"}, place={"L1": (430, 1432)})   # clear of his finger
v.plate(x=352, y=300, size=0.74)                                  # he is still standing there: the plate goes beside his head, over the far stadium

if __name__ == "__main__":
    v.main()
