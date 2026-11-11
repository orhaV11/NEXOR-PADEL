"""Download new takes, gate each one, and tile them for a look.

    python3 src/film/takes.py work/<film>/takes.json [name ...]

takes.json: [{ "name": "j01_a", "url": "https://...mp4", "line": "What it should say." }, ...]
Clips land in work/<film>/clips/<name>.mp4, gate results in work/<film>/gates.json, and one strip per take in
work/<film>/sheets/ (plus sheet_<n>.jpg with four takes per page).
"""
import json
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import gate  # noqa: E402
import inspect_clip  # noqa: E402

spec = Path(sys.argv[1]).resolve()
only = set(sys.argv[2:])
base = spec.parent
(base / "clips").mkdir(exist_ok=True)
(base / "sheets").mkdir(exist_ok=True)
gates_p = base / "gates.json"
gates = json.load(open(gates_p)) if gates_p.exists() else {}
strips = []
for tk in json.load(open(spec)):
    name = tk["name"]
    if only and name not in only:
        continue
    mp4 = base / "clips" / f"{name}.mp4"
    if not mp4.exists():
        subprocess.run(["curl", "-sS", "-m", "300", "-o", str(mp4), tk["url"]], check=True)
    if name not in gates or gates[name].get("line") != tk.get("line"):
        g = gate.check(mp4, tk["line"]) if tk.get("line") else {"heard": gate.asr.words(mp4)[1], "words": gate.asr.words(mp4)[0], "ok_words": True, "ok_tail": True, "ok_music": True, "dur": 0, "match": None, "missing": [], "extra": [], "tail_slack": None, "gap_tone_db": None}
        g["line"] = tk.get("line")
        gates[name] = g
    g = gates[name]
    ok = g["ok_words"] and g["ok_tail"] and g["ok_music"]
    print(f"{'PASS' if ok else 'FAIL'} {name}: heard \"{g['heard']}\"" + ("" if ok else f"  [words {g['ok_words']} match {g['match']} missing {g['missing']} extra {g['extra']}; tail {g['tail_slack']}; tone {g['gap_tone_db']}]"))
    print("      " + " ".join(f"{w}@{a:.2f}" for w, a, b in g["words"]))
    strip_p = base / "sheets" / f"{name}.jpg"
    if not strip_p.exists():
        import io
        info = json.loads(subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=duration", "-of", "json", str(mp4)], capture_output=True, check=True).stdout)
        dur = float(info["streams"][0]["duration"])
        n = 8
        ims = [inspect_clip.frame(mp4, dur * (i + 0.5) / n, 300) for i in range(n)]
        w, h = ims[0].size
        s = Image.new("RGB", (w * n, h + 20), "white")
        d = ImageDraw.Draw(s)
        d.text((4, 4), f"{name}   {'PASS' if ok else 'FAIL'}   \"{g['heard']}\"", fill="black")
        for i, im in enumerate(ims):
            s.paste(im, (i * w, 20))
            d.text((i * w + 4, 24), f"{dur * (i + 0.5) / n:.2f}s", fill="yellow")
        s.save(strip_p, quality=85)
    strips.append(strip_p)
gates_p.write_text(json.dumps(gates, indent=1))
for k in range(0, len(strips), 4):
    ims = [Image.open(p) for p in strips[k:k + 4]]
    sheet = Image.new("RGB", (max(i.width for i in ims), sum(i.height for i in ims)), "white")
    y = 0
    for im in ims:
        sheet.paste(im, (0, y))
        y += im.height
    out = base / "sheets" / f"sheet_{'_'.join(sorted(only))[:40] or 'all'}_{k // 4 + 1}.jpg"
    sheet.save(out, quality=84)
    print("sheet:", out)
