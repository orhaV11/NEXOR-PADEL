"""The sound of "This is us" (vlog 00, "us"): the ten-second introduction that is pinned on top of the page.

    python3 src/film/toon/ep_us_sound.py
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import build  # noqa: E402

A, B = "ldn_g25p_1", "ldn_g25p_2"   # the listening model's choice, line by line (work/us/voice/picks.json)
PLAN = [
    ("L1", "L", A, 1, 0.25, {}),          # "Hi! I'm Luna, and that's Timo."  (his finger is already in the picture)
    ("HOLD", "up", 0.40, 0.65),           # his finger goes down; then he comes up into the frame behind her
    ("L2", "L", A, 2, 0.10, {}),          # "We go on sports trips."
    ("T3", "T", A, 3, 0.15, {}),          # "She plans them."
    ("L4", "L", B, 4, 0.06, {}),          # "I plan them!"   the music stops as she ends
    ("CUT", 0.30),                        # closer
    ("T5", "T", A, 5, 0.45, {}),          # "That was a warning."   to us, after a stare of 0.75 s from her last word
    ("PLATE", 0.85),
]
if __name__ == "__main__":
    build("us", PLAN, bed={"stop": "L4"})
