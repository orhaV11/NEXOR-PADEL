"""Build the app insert of a film: the product demo (transparent frames from render-plate.mjs) laid over the
previous live shot, which racks out of focus and takes a dark green tint, so the product arrives inside the scene
instead of cutting away to a graphic.

    node src/film/render-plate.mjs en work/apa2/plate-en --type 0.75 --hold 0.625 --plan 2.0
    python3 src/film/make_app_insert.py work/apa2/plate-en src/film/assets/apa/clips/rosie_c.mp4 3.9 5.3 \
        src/film/assets/inserts/app-en.mp4 [--punch 1.2 --cx 0.45 --cy 0.3] [--r 1 --b 1 --sat 1]

The background plays the given stretch of the clip forwards and backwards for as long as the plate lasts.
"""
import glob
import subprocess
import sys

import cv2
import numpy as np

W, H, FPS = 1080, 1920, 24
args = [a for a in sys.argv[1:] if not a.startswith("--")]
opt = lambda k, d: float(sys.argv[sys.argv.index(k) + 1]) if k in sys.argv else d  # noqa: E731
plate_dir, clip, t0, t1, out = args[0], args[1], float(args[2]), float(args[3]), args[4]
punch, cx, cy = opt("--punch", 1.2), opt("--cx", 0.45), opt("--cy", 0.3)
GR, GB, GSAT = opt("--r", 1.0), opt("--b", 1.0), opt("--sat", 1.0)  # the grade the film gives this source
TINT = np.float32([0x1e, 0x2a, 0x0d])  # brand dark green, BGR

plates = sorted(glob.glob(f"{plate_dir}/*.png"))
sw, sh = int(round(W * punch / 2)) * 2, int(round(H * punch / 2)) * 2
x0, y0 = int((sw - W) * cx), int((sh - H) * cy)
raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{t0:.3f}", "-t", f"{t1 - t0:.3f}", "-i", clip, "-an",
                      "-vf", f"scale={sw}:{sh}:flags=lanczos,crop={W}:{H}:{x0}:{y0}", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"],
                     capture_output=True, check=True).stdout
bg = np.frombuffer(raw, np.uint8).reshape(-1, H, W, 3)
order = list(range(len(bg))) + list(range(len(bg) - 2, 0, -1))  # there and back again

enc = subprocess.Popen(["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
                        "-c:v", "libx264", "-preset", "slow", "-crf", "12", "-pix_fmt", "yuv420p", out], stdin=subprocess.PIPE)
for k, p in enumerate(plates):
    f = bg[order[k % len(order)]].astype(np.float32) * np.float32([GB, 1.0, GR])
    if GSAT != 1.0:
        luma = f @ np.float32([0.114, 0.587, 0.299])
        f = luma[:, :, None] + (f - luma[:, :, None]) * GSAT
    b = min(1.0, (k + 1) / 6) ** 0.7  # the focus pulls away over the first six frames
    f = cv2.GaussianBlur(f, (0, 0), 1.5 + 24 * b)
    f = f * (1 - 0.56 * b) + TINT * (0.56 * b) * 0.9
    ov = cv2.imread(p, cv2.IMREAD_UNCHANGED).astype(np.float32)
    a = ov[:, :, 3:4] / 255.0
    f = f * (1 - a) + ov[:, :, :3] * a
    enc.stdin.write(np.clip(f, 0, 255).astype(np.uint8).tobytes())
enc.stdin.close()
enc.wait()
print(f"{out}: {len(plates)} frames, {len(plates) / FPS:.3f} s")
