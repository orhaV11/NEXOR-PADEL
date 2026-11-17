"""Keep what a cut-out film was made from: work/ is scratch and is not stored, so the drawings, the voice lines, the
bought sounds and the spending ledger are copied into src/film/assets/toon/<film>/ (drawings as lossless WebP).

    python3 src/film/toon/pack.py switch

The film's scripts read from there when it exists, so the film can be rendered again on a fresh machine without
paying for anything.
"""
import json
import shutil
import sys
from pathlib import Path

import cv2

ROOT = Path(__file__).resolve().parent.parent.parent.parent
name = sys.argv[1]
WORK, KEEP = ROOT / "work" / name, ROOT / "src" / "film" / "assets" / "toon" / name
(KEEP / "art").mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(Path(__file__).resolve().parent))
cast = __import__(f"{name}_cast")
USED = set(json.loads((KEEP / "used.json").read_text())) if (KEEP / "used.json").exists() else set()
if "--scan" in sys.argv or not USED:   # load the cast once to learn which drawings it uses
    cast.luna(0.25), cast.timo(0.25), cast.prone(0.25), cast.phone(0.25), cast.hand(0.25), cast.background("bg_hill_1", 0.25), cast.background("bg_hill_2", 0.25)
    USED = set(cast.USED) | {"sheet_1", "style_a_vector"}
    (KEEP / "used.json").write_text(json.dumps(sorted(USED), indent=1))
n = 0
for p in sorted((WORK / "art").glob("*.png")):
    dst = KEEP / "art" / (p.stem + ".webp")
    if p.stem in USED and (not dst.exists() or dst.stat().st_mtime < p.stat().st_mtime):
        cv2.imwrite(str(dst), cv2.imread(str(p), cv2.IMREAD_UNCHANGED), [cv2.IMWRITE_WEBP_QUALITY, 101])
        n += 1
for p in (KEEP / "art").glob("*.webp"):
    if p.stem not in USED:
        p.unlink()
shutil.copy(WORK / "art" / "index.json", KEEP / "art" / "index.json")
TAKES = ("ldn_g25p_3", "ldn_g25p_5")   # the two readings the film is cut from
for sub, pats in (("voice", [f"{t}.{e}" for t in TAKES for e in ("wav", "json")]), ("voice/lines", [f"{t}*" for t in TAKES]), ("sound/raw", ("wind_a.wav", "thud_a.wav"))):
    (KEEP / sub).mkdir(parents=True, exist_ok=True)
    for pat in pats:
        for p in (WORK / sub).glob(pat):
            if "reel" not in p.name:
                shutil.copy(p, KEEP / sub / p.name)
shutil.copy(WORK / "ledger.jsonl", KEEP / "ledger.jsonl")
size = sum(p.stat().st_size for p in KEEP.rglob("*") if p.is_file()) / 1e6
print(f"{n} drawings packed; {KEEP.relative_to(ROOT)} holds {size:.0f} MB")
