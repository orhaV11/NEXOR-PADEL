"""Look at a generated clip without eyes or ears on it: a labelled frame strip, a full-size face crop,
what was really said (with word times), and the loudness of the track.

    python3 src/film/inspect_clip.py <name> <url-or-path> [--out work/tests] [--frames 8]

Writes <out>/<name>.mp4 (downloaded if a URL), <name>_strip.jpg, <name>_face.jpg and prints one summary block.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import asr  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]


def sh(cmd, **kw):
    return subprocess.run([str(c) for c in cmd], check=True, capture_output=True, **kw)


def frame(path, t, scale=None):
    vf = ["-vf", f"scale={scale}:-2"] if scale else []
    raw = sh(["ffmpeg", "-v", "error", "-ss", f"{t:.3f}", "-i", path, "-frames:v", "1", *vf, "-f", "image2pipe", "-c:v", "png", "-"]).stdout
    import io
    return Image.open(io.BytesIO(raw)).convert("RGB")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    opt = lambda k, d: sys.argv[sys.argv.index(k) + 1] if k in sys.argv else d  # noqa: E731
    name, src = args[0], args[1]
    out = ROOT / opt("--out", "work/tests")
    n = int(opt("--frames", 8))
    out.mkdir(parents=True, exist_ok=True)
    mp4 = out / f"{name}.mp4"
    if src.startswith("http"):
        if not mp4.exists():
            sh(["curl", "-sS", "-m", "300", "-o", mp4, src])
    else:
        mp4 = Path(src)
    info = json.loads(sh(["ffprobe", "-v", "error", "-show_entries", "stream=codec_type,width,height,r_frame_rate,duration,sample_rate", "-of", "json", mp4]).stdout)
    v = next(s for s in info["streams"] if s["codec_type"] == "video")
    has_audio = any(s["codec_type"] == "audio" for s in info["streams"])
    dur = float(v.get("duration") or 0)
    ts = [dur * (i + 0.5) / n for i in range(n)]
    ims = [frame(mp4, t, 300) for t in ts]
    w, h = ims[0].size
    strip = Image.new("RGB", (w * n, h + 18), "white")
    d = ImageDraw.Draw(strip)
    for i, (t, im) in enumerate(zip(ts, ims)):
        strip.paste(im, (i * w, 18))
        d.text((i * w + 4, 3), f"{t:.2f}s", fill="black")
    strip.save(out / f"{name}_strip.jpg", quality=86)
    # a native-resolution crop of the upper-middle of the frame at three moments: where realism is judged
    crops = []
    for t in (dur * 0.25, dur * 0.5, dur * 0.8):
        im = frame(mp4, t)
        W, H = im.size
        crops.append(im.crop((int(W * 0.14), int(H * 0.12), int(W * 0.86), int(H * 0.62))))
    cw, ch = crops[0].size
    face = Image.new("RGB", (cw * 3, ch))
    for i, c in enumerate(crops):
        face.paste(c, (i * cw, 0))
    if face.width > 2100:
        face = face.resize((2100, int(face.height * 2100 / face.width)), Image.LANCZOS)
    face.save(out / f"{name}_face.jpg", quality=90)
    print(f"== {name}: {v['width']}x{v['height']} {v['r_frame_rate']} fps, {dur:.2f} s, audio={'yes' if has_audio else 'NO'}")
    if has_audio:
        lu = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(mp4), "-af", "ebur128", "-f", "null", "-"], capture_output=True, text=True).stderr
        integ = [ln.split()[1] for ln in lu.splitlines() if ln.strip().startswith("I:")]
        ws, wt = asr.words(mp4)
        print(f"   loudness {integ[-1] if integ else '?'} LUFS")
        print("   parakeet: " + " ".join(f"{a}@{s:.2f}" for a, s, e in ws))
        print(f"   whisper : {wt}")
        x, sr = asr.load(mp4)
        hop = int(0.1 * sr)
        env = [20 * np.log10(np.sqrt(np.mean(x[i:i + hop] ** 2)) + 1e-9) for i in range(0, max(1, len(x) - hop), hop)]
        print("   level per 0.1 s: |" + "".join(" .:-=+*#%@"[max(0, min(9, int((e + 60) / 5)))] for e in env) + "|")


if __name__ == "__main__":
    main()
