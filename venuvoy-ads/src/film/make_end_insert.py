"""The end card over live footage: the brand's end card (transparent frames from render-plate.mjs --end) laid over
the last shot, which runs on behind it, out of focus and under a dark green scrim. The film ends in the stadium.

    node src/film/render-plate.mjs en work/apa/endplate-en --end 2.6667
    python3 src/film/make_end_insert.py work/apa/endplate-en src/film/assets/apa/clips/munich_a.mp4 3.35 \
        src/film/assets/inserts/end-en.mp4 [--blur 14 --scrim 0.55 --slow_last 1.0]
"""
import glob
import subprocess
import sys

import cv2
import numpy as np

W, H, FPS = 1080, 1920, 24
args = [a for a in sys.argv[1:] if not a.startswith("--")]
opt = lambda k, d: float(sys.argv[sys.argv.index(k) + 1]) if k in sys.argv else d  # noqa: E731
plate_dir, clip, t0, out = args[0], args[1], float(args[2]), args[3]
BLUR, SCRIM, SLOW = opt("--blur", 14), opt("--scrim", 0.55), opt("--slow_last", 1.0)
TINT = np.float32([0x1e, 0x2a, 0x0d])  # brand dark green, BGR

plates = sorted(glob.glob(f"{plate_dir}/*.png"))
n = len(plates)
dur = n / FPS
raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{t0:.3f}", "-t", f"{dur + 0.2:.3f}", "-i", clip, "-an",
                      "-vf", f"scale={W}:{H}:flags=lanczos", "-r", str(FPS), "-f", "rawvideo", "-pix_fmt", "bgr24", "-"],
                     capture_output=True, check=True).stdout
bg = np.frombuffer(raw, np.uint8).reshape(-1, H, W, 3)
enc = subprocess.Popen(["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
                        "-c:v", "libx264", "-preset", "slow", "-crf", "12", "-pix_fmt", "yuv420p", out], stdin=subprocess.PIPE)
for k, p in enumerate(plates):
    t = k / FPS
    ts = t if t <= dur - SLOW else (dur - SLOW) + (t - (dur - SLOW)) * 0.5  # the last second at half speed: a near-still ground
    pos = ts * FPS
    i = int(pos)
    a = pos - i
    f = bg[min(i, len(bg) - 1)].astype(np.float32) * (1 - a) + bg[min(i + 1, len(bg) - 1)].astype(np.float32) * a
    b = min(1.0, (k + 1) / 6)
    f = cv2.GaussianBlur(f, (0, 0), 0.5 + BLUR * b)
    f = f * (1 - SCRIM * b) + TINT * (SCRIM * b)
    ov = cv2.imread(p, cv2.IMREAD_UNCHANGED).astype(np.float32)
    al = ov[:, :, 3:4] / 255.0
    f = f * (1 - al) + ov[:, :, :3] * al
    enc.stdin.write(np.clip(f, 0, 255).astype(np.uint8).tobytes())
enc.stdin.close()
enc.wait()
print(f"{out}: {n} frames, {dur:.3f} s")
