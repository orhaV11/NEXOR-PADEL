"""The sound of the sketch "Gary" (vlog 06, "gary"): the tailgate in a stadium car park.

    python3 src/film/toon/ep_gary_sound.py

Timo is the deadpan one in a giant foam cowboy hat; a stranger with a grill has adopted him. Her lines are all from
reading 3 (one voice colour; the listening model chose it for "You don't like people." and "...Can I come?", the two
lines her part rests on), his all from reading 1 (the listening model's choice for every one of his lines; low and
flat). Reading 2 is out: the recogniser heard "Garry" in it.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import build  # noqa: E402

L, T = "ldn_g25p_3", "ldn_g25p_1"
# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends; negative = talks over), options
PLAN = [
    ("L1", "L", L, 1, 0.25, {}),                                        # "American football! The tailgate! We brought a sandwich."
    ("L2", "L", L, 2, 0.20, {"tighten": 0.16,                           # confiding to us; her pause after "Timo hates it." shortened
                             "cut_in_word": ("park", 0.14, 0.10)}),     # "...just a car pa—": he talks over the end of it
    ("T3", "T", T, 3, -0.20, {}),                                       # "Gary says hello." calm, over her
    ("L4", "L", L, 4, 0.38, {}),                                        # a beat: she turns and sees the hat. "Who's Gary?"
    ("T5", "T", T, 5, 0.15, {}),                                        # "Gary has a grill." as if that explains it
    ("CUT", 0.35),                                                      # it lands; a jump cut, closer
    ("L6", "L", L, 6, 0.16, {}),                                        # "You don't like people."
    ("T7", "T", T, 7, 0.12, {}),                                        # "He's invited me for Thanksgiving." The music stops dead as it ends
    ("L8", "L", L, 8, 0.80, {}),                                        # a grill sizzles in the silence; "...Can I come?" small, eyes down
    ("HOLD", "look", 0.28, 0.55),                                       # his eyes come to us, a flat look
    ("T9", "T", T, 9, 0.06, {}),                                        # "It's a family thing."
    ("PLATE", 1.00),                                                    # the end picture holds for the laugh, then the sung name
]
if __name__ == "__main__":
    build("gary", PLAN, bed={"stop": "T7"},
          ambience={"file": "air", "db": -32, "gusts": False},
          fx=[{"name": "sizzle", "file": "sizzle", "at": ("T7", 1, 0.10), "whole": True, "start": 0.4, "len": 1.7,
               "fade_in": 0.18, "fade_out": 0.7, "db": -15}])
