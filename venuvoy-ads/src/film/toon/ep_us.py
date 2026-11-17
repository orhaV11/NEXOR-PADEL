"""The picture of "This is us" (vlog 00, "us"): Luna introduces the two of them; Timo adds one fact.

    python3 src/film/toon/ep_us.py --sheet | --stills ... | (nothing: the film)
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog import Vlog  # noqa: E402

v = Vlog("us", "ads/21-vlog/venuvoy-vlog-00-us", luna="wind", timo="timo_clean")
NEAR2 = ("bg_hill_1", (370, 1660), (238, 596), 1.28)
NEAR3 = ("bg_hill_1", (370, 1660), (250, 440), 1.50)
v.shot("start", bg=NEAR2, luna=(372, 1556, 1.00, -1.2), timo=(872, 935, 1.00))
v.shot("cut1", bg=NEAR3, luna=(345, 1490, 1.16, 1.0), timo=(890, 830, 1.16))

v.finger(up=-0.4, down="L1.end-0.15", wag="L1", x=965)     # all there is of him at first: one finger, which wags when he is named
v.rise("up", "up.end")                                  # then the rest of him
v.faces(("L4-0.10", "turn"),                            # "I plan them!", to him, delighted
        ("cut1", None))                                 # and she beams at us while he tells us the truth
v.eyes(("cut1", "lens"))
v.tilt(L1=3.0, L2=-2.4, L4=2.2)
v.captions(off={"L4": "cut1+0.04"}, place={"L1": (430, 1432)})   # clear of his finger
v.plate(x=352, y=300, size=0.74)                                  # he is still standing there: the plate goes beside his head, over the far stadium

if __name__ == "__main__":
    v.main()
