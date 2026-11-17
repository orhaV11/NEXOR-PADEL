"""The sound of "The Switch", written with the kit: the model for a new sketch's sound file.

    python3 src/film/toon/example_sound.py

(It writes into work/kittest, not into the delivered film's folder.)
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import build  # noqa: E402

B, D = "ldn_g25p_3", "ldn_g25p_5"   # two readings of the whole sketch; each line is taken from the one where it sounds best
# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends; negative = talks over), options
PLAN = [
    ("L1", "L", B, 1, 0.25, {}),
    ("T2", "T", B, 2, 0.10, {"ground": True}),                        # from the ground: dulled
    ("L3", "L", D, 3, 0.18, {"tighten": 0.16}),                       # caught: she takes a breath, no more
    ("CUT", 0.10, "whip"),                                            # minutes later: a quick whip
    ("T4", "T", B, 4, 0.14, {}),
    ("L5", "L", D, 5, 0.05, {"tighten": 0.10}),                       # a retort comes at once
    ("T6", "T", B, 6, 0.20, {"cut_in_word": ("flip", 0.30, 0.22)}),   # he is talked over: his word fades away under her first one
    ("L7", "L", D, 7, -0.22, {}),
    ("CUT", 0.77),                                                    # he looks at us for this long; then a hard cut
    ("T8", "T", B, 8, 0.22, {"even": 12.0}),                          # this reading drops to a murmur at the end: lifted
    ("L9", "L", D, 9, 0.42, {}),                                      # she gets her phone out first; the pause before "Run." is uncut
    ("HOLD", "sink", 0.65, 0.72),                                     # he stands still 0.65 s, then sinks for 0.72 s
    ("T10", "T", B, 10, 0.45, {"ground": True}),                      # the thud has its own laugh first
    ("L11", "L", D, 11, 0.38, {}),
    ("PLATE", 0.70),
]
if __name__ == "__main__":
    build("kittest", PLAN, bed={"stop": "L7"},                         # the vlog music stops dead as L7 ends (he looks at us)
          fx=[{"name": "thud", "file": "thud_a", "at": ("sink", 1, -0.04), "db": -7, "push": True}])
