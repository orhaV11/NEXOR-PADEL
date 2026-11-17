"""The trip so far: lines cut from finished films, back to back, and the plate of the first film at the end.

    python3 src/film/toon/supercut.py sofar        ads/21-vlog/venuvoy-vlog-90-sofar-en_{9x16,4x5}.mp4

Nothing is drawn or bought. A piece is cut from a film from the moment the caption of its first line comes on
(0.04 s before the line) to a little after its last line, while that caption is still up, so no caption is cut in
half and no other caption flashes. Each piece keeps its own sound; the joins get a short fade so nothing clicks.
Made again each month from whatever has been finished since.
"""
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent.parent
TMP = ROOT / "work" / "supercut"

# film work folder, its delivered file (without "-en_9x16_1080x1920.mp4"), first line, last line, seconds kept after it
CUTS = {
    "sofar": {"nn": "90", "name": "sofar", "pieces": [
        ("switch", "ads/20-sketches/venuvoy-sketch-01-the-switch", "T2", "T2", 0.30),     # Why is there a mountain in the football trip?
        ("queue", "ads/21-vlog/venuvoy-vlog-02-queue", "L3", "T4", 0.35),                 # There's no queue. / Because we're the front.
        ("basket", "ads/21-vlog/venuvoy-vlog-30-basket", "L3", "T4", 0.40),               # That was two sentences. / There was a comma.
        ("slowly", "ads/21-vlog/venuvoy-vlog-04-slowly", "T7", "T7", 0.30),               # I'm not with her.
        ("look", "ads/21-vlog/venuvoy-vlog-03-look", "T9", "T9", 0.45),                   # He knows what he did.
        ("switch", "ads/20-sketches/venuvoy-sketch-01-the-switch", "T10", "END", 0.0),    # Flip it back. / He loves it. / the plate with the sung name
    ]},
}


def lines(film):
    tl = json.loads((ROOT / "work" / film / "sound" / "timeline.json").read_text())
    return {l["id"]: l for l in tl["lines"]}, tl["events"]


def main(name):
    spec = CUTS[name]
    TMP.mkdir(parents=True, exist_ok=True)
    parts = []
    for k, (film, base, a, b, after) in enumerate(spec["pieces"]):
        L, ev = lines(film)
        t0 = L[a]["t0"] - 0.04
        order = sorted(L.values(), key=lambda l: l["t0"])
        nxt = next((l["t0"] for l in order if l["t0"] > L[b]["t0"]), None) if b != "END" else None
        t1 = ev["end"] if b == "END" else min(L[b]["t1"] + after, (nxt - 0.08) if nxt else 1e9, ev.get("plate", 1e9) - 0.04)   # never into the next caption or the plate
        src = str(ROOT / (base + "-en_9x16_1080x1920.mp4"))
        out = TMP / f"{name}_{k:02d}.mp4"
        d = t1 - t0
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{t0:.3f}", "-i", src, "-t", f"{d:.3f}",
                        "-af", f"afade=t=in:d=0.03,afade=t=out:st={max(0, d - 0.04):.3f}:d=0.04",
                        "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p", "-r", "24",
                        "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", str(out)], check=True)
        parts.append(out)
        print(f"  {film} {a}-{b}: {t0:.2f}-{t1:.2f} s ({d:.2f})")
    lst = TMP / f"{name}.txt"
    lst.write_text("".join(f"file '{p}'\n" for p in parts))
    tall = ROOT / "ads" / "21-vlog" / f"venuvoy-vlog-{spec['nn']}-{spec['name']}-en_9x16_1080x1920.mp4"
    feed = ROOT / "ads" / "21-vlog" / f"venuvoy-vlog-{spec['nn']}-{spec['name']}-en_4x5_1080x1350.mp4"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", str(lst), "-c:v", "libx264", "-preset", "medium", "-crf", "16",
                    "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", str(tall)], check=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(tall), "-vf", "crop=1080:1350:0:180", "-c:v", "libx264", "-preset", "medium", "-crf", "16",
                    "-pix_fmt", "yuv420p", "-c:a", "copy", "-movflags", "+faststart", str(feed)], check=True)
    print("film", tall)
    print("feed", feed)


if __name__ == "__main__":
    for n in sys.argv[1:] or CUTS:
        main(n)
