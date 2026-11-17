"""Covers: the first picture of every finished film with its title, for the grid of the page.

    python3 src/film/toon/covers.py            a cover for every film below whose file exists, into ads/24-covers/

The picture is the film's own first frame (no caption is on it yet: the first word comes a quarter of a second in).
The title is set in the cover type of the still posts (cards.mjs, "title"), low, where her captions stand, and inside
the square middle of the frame (rows 420 to 1500) so that a square or 3:4 grid crop keeps it whole. A title never gives
the joke away: it names the situation, not the punchline.
"""
import json
import subprocess
import sys
from pathlib import Path

import cv2
import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE))

OUT = ROOT / "ads" / "24-covers"
WORK = ROOT / "work" / "covers"

# film file (without "-en_9x16_1080x1920.mp4"): (title, the time of the picture in seconds)
COVERS = {
    "ads/20-sketches/venuvoy-sketch-01-the-switch": ("The Switch", 0.0),
    "ads/21-vlog/venuvoy-vlog-00-us": ("This is us", 0.0),
    "ads/21-vlog/venuvoy-vlog-02-queue": ("Front of the queue", 0.0),
    "ads/21-vlog/venuvoy-vlog-03-look": ("The look", 0.0),
    "ads/21-vlog/venuvoy-vlog-04-slowly": ("Really slowly", 0.0),
    "ads/21-vlog/venuvoy-vlog-05-glove": ("Twenty years", 0.0),
    "ads/21-vlog/venuvoy-vlog-06-gary": ("Gary", 0.0),
    "ads/21-vlog/venuvoy-vlog-07-dave": ("Official Dave", 0.0),
    "ads/21-vlog/venuvoy-vlog-08-safety": ("Safety car", 0.0),
    "ads/21-vlog/venuvoy-vlog-09-pace": ("Big Matches", 0.0),
    "ads/21-vlog/venuvoy-vlog-10-battery": ("Ten minutes to kick-off", 0.0),
    "ads/21-vlog/venuvoy-vlog-11-nicer": ("Look what I got", 0.0),
    "ads/21-vlog/venuvoy-vlog-12-minutes": ("Two minutes left", 0.0),
    "ads/21-vlog/venuvoy-vlog-13-shirt": ("They're throwing shirts", 0.0),
    "ads/21-vlog/venuvoy-vlog-14-wave": ("The wave", 0.0),
    "ads/21-vlog/venuvoy-vlog-15-smile": ("At the golf", 0.0),
    "ads/21-vlog/venuvoy-vlog-16-run": ("Run day", 0.0),
    "ads/21-vlog/venuvoy-vlog-17-timing": ("Travel day", 0.0),
    "ads/21-vlog/venuvoy-vlog-18-coat": ("A man outside", 0.0),
    "ads/21-vlog/venuvoy-vlog-19-tour": ("Stadium tour", 0.0),
    "ads/21-vlog/venuvoy-vlog-20-view": ("Timo reviews: the view", 0.0),
    "ads/21-vlog/venuvoy-vlog-21-football": ("One sentence: football", 0.0),
    "ads/21-vlog/venuvoy-vlog-22-roar": ("Was that a goal?", 0.0),
    "ads/21-vlog/venuvoy-vlog-23-uphill": ("The morning run", 0.0),
    "ads/21-vlog/venuvoy-vlog-24-nogame": ("No game this weekend", 0.0),
    "ads/21-vlog/venuvoy-vlog-25-ironed": ("Stadium tour at ten", 0.0),
    "ads/21-vlog/venuvoy-vlog-30-basket": ("One sentence: basketball", 0.0),
    "ads/21-vlog/venuvoy-vlog-31-gridiron": ("One sentence: American football", 0.0),
    "ads/21-vlog/venuvoy-vlog-40-alone": ("Question: on your own?", 0.0),
    "ads/21-vlog/venuvoy-vlog-41-third": ("Question: how many games?", 0.0),
    "ads/21-vlog/venuvoy-vlog-42-turnup": ("Question: just turn up?", 0.0),
    "ads/21-vlog/venuvoy-vlog-50-top": ("First day of the season", 0.0),
    "ads/21-vlog/venuvoy-vlog-32-tennis": ("One sentence: tennis", 0.0),
    "ads/21-vlog/venuvoy-vlog-43-early": ("Question: how early?", 0.0),
    "ads/21-vlog/venuvoy-vlog-44-language": ("Question: the language", 0.0),
    "ads/21-vlog/venuvoy-vlog-51-summer": ("The season ended yesterday", 0.0),
    "ads/21-vlog/venuvoy-vlog-52-draw": ("The draw", 0.0),
    "ads/21-vlog/venuvoy-vlog-53-fixtures": ("The fixtures are out", 0.0),
    "ads/21-vlog/venuvoy-vlog-60-leave": ("When do you leave?", 0.0),
    "ads/21-vlog/venuvoy-vlog-61-pack": ("What do you pack?", 0.0),
    "ads/21-vlog/venuvoy-vlog-62-source": ("Sunday, 4 p.m.", 0.0),
    "ads/21-vlog/venuvoy-vlog-90-sofar": ("The trip so far", 0.3),
}
LOW = 1440          # the title's last row stands here (inside the square middle, under her chin)


def frame(mp4, t):
    out = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{t:.3f}", "-i", mp4, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"],
                         check=True, capture_output=True).stdout
    return np.frombuffer(out, np.uint8).reshape(1920, 1080, 3).copy()


def main():
    todo = {k: v for k, v in COVERS.items() if (ROOT / (k + "-en_9x16_1080x1920.mp4")).exists()}
    items = []
    for k, (title, _) in todo.items():
        items.append({"name": Path(k).name, "kind": "title", "text": title, "size": 104 if len(title) <= 14 else 92 if len(title) <= 24 else 80, "width": 900})
    WORK.mkdir(parents=True, exist_ok=True)
    (WORK / "spec.json").write_text(json.dumps({"out": "work/covers/text", "items": items}, ensure_ascii=False, indent=1))
    subprocess.run(["node", str(HERE / "cards.mjs"), str(WORK / "spec.json")], check=True, stdout=subprocess.DEVNULL)
    OUT.mkdir(parents=True, exist_ok=True)
    for k, (title, t) in todo.items():
        try:
            img = frame(str(ROOT / (k + "-en_9x16_1080x1920.mp4")), t).astype(np.float32) / 255
        except subprocess.CalledProcessError:       # a film being written just now: its cover comes next time
            print("  skipped (being rendered):", k)
            continue
        pic = cv2.imread(str(WORK / "text" / f"{Path(k).name}.png"), cv2.IMREAD_UNCHANGED).astype(np.float32) / 255   # BGRA, straight alpha
        ys = np.where(pic[..., 3].max(axis=1) > 0.3)[0]
        xs = np.where(pic[..., 3].max(axis=0) > 0.3)[0]
        pic = pic[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        h, w = pic.shape[:2]
        y0, x0 = LOW - h, (1080 - w) // 2
        a = pic[..., 3:4]
        img[y0:y0 + h, x0:x0 + w] = img[y0:y0 + h, x0:x0 + w] * (1 - a) + pic[..., :3] * a
        name = Path(k).name.replace("venuvoy-", "venuvoy-cover-").replace("cover-sketch-", "cover-").replace("cover-vlog-", "cover-")
        out = (img * 255 + 0.5).clip(0, 255).astype(np.uint8)
        cv2.imwrite(str(OUT / f"{name}_9x16.jpg"), out, [cv2.IMWRITE_JPEG_QUALITY, 93])
        if y0 < 420:
            print("  WARNING", name, "title starts above the square middle")
        print(name, "->", title)


if __name__ == "__main__":
    main()
