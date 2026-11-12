"""Build episodes of "To Be Confirmed" from their sources: film every chat and board scene, then cut.

    python3 src/film/tbc/make.py 1 3          # episodes 1 and 3: scenes, then the final 9:16 and 4:5
    python3 src/film/tbc/make.py all --draft  # all six as half-size drafts
    python3 src/film/tbc/make.py 2 --scenes   # only film episode 2's scenes again

A scene script is src/film/tbc/scenes/e<N>_<name>.json; one with a "scene" field is a paperwork board
(board.html), any other is the group chat (chat.html). Each is filmed to src/film/assets/tbc/scenes/<same name>.mp4,
which is what the edit list src/film/tbc/e<N>.json cuts from.
"""
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
args = [a for a in sys.argv[1:] if not a.startswith("--")]
eps = [1, 2, 3, 4, 5, 6] if not args or args == ["all"] else [int(a) for a in args]
for n in eps:
    for sc in sorted((HERE / "scenes").glob(f"e{n}_*.json")):
        board = "scene" in json.loads(sc.read_text())
        out = ROOT / "src/film/assets/tbc/scenes" / (sc.stem + ".mp4")
        cmd = ["node", str(ROOT / "src/film/render-chat.mjs"), str(sc), "en", str(out)] + (["--page", "board.html"] if board else [])
        subprocess.run(cmd, check=True, cwd=ROOT)
    if "--scenes" not in sys.argv:
        subprocess.run([sys.executable, str(ROOT / "src/film/cut.py"), str(HERE / f"e{n}.json"), "en"] + (["--draft"] if "--draft" in sys.argv else []), check=True, cwd=ROOT)
