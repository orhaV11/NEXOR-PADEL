"""Lay a fast fly-past over a clip: a cut-out picture crosses the frame in a few frames, smeared by its speed.

    python3 src/film/s2/streak.py <clip.mp4> <sprite_on_green.jpg> <out.mp4> --at 2.3 [--frames 8] [--height 820] [--bottom 1880] [--dir left] [--smear 0.75]
                                  [--gamma 1.5] [--gain 1.05,1.0,0.8] [--shadow 0.35] [--skip-last 2]

Used for the shot in which Timo and Luna shoot past the cat on Big Yellow: the cat's clip is generated on its own
(one animal, still camera: the safe kind of clip) and the riders are a designed still, keyed off its green
background and moved across the lower part of the frame here. Nothing about the riders is generated as video, so
nothing about them can go wrong in motion. The sound of the clip is kept as it is.

--at      second of the clip at which the picture enters the frame
--frames  how many frames the crossing takes (8 at 24 fps = a third of a second)
--height  height of the cut-out on screen, in pixels;  --bottom  row its lowest point sits on
--dir     "left": enters at the right edge and leaves at the left;  "right": the other way
--gamma   tone curve for the cut-out before the gains (1.5 darkens the middle and keeps the highlights: studio light to evening sun)
--gain    red,green,blue multipliers for the cut-out, to bring a picture drawn in studio light into the light of the clip
--skip-last  do not draw the last N frames of the crossing (when only a scrap of scarf would trail at the edge)
--shadow  darkness (0-1) of a soft shadow laid on the ground under the cut-out, so that it stands on the street
"""
import subprocess
import sys

import cv2
import numpy as np

a = sys.argv[1:]
opt = lambda k, d: (type(d)(a[a.index(k) + 1]) if k in a else d)  # noqa: E731
clip, sprite_path, out = a[0], a[1], a[2]
AT, FRAMES, HEIGHT, BOTTOM, DIR = opt("--at", 2.3), opt("--frames", 8), opt("--height", 820), opt("--bottom", 1880), opt("--dir", "left")
SMEAR = opt("--smear", 0.75)  # how much of the distance covered in one frame is smeared (0.75 = a real shutter; less = easier to read)
GAIN = [float(v) for v in opt("--gain", "1,1,1").split(",")]
SHADOW = opt("--shadow", 0.0)
GAMMA = opt("--gamma", 1.0)
SKIP_LAST = opt("--skip-last", 0)

# the cut-out: everything that is not the green screen
img = cv2.imread(sprite_path).astype(np.float32)
b, g, r = img[..., 0], img[..., 1], img[..., 2]
green = (g > 45) & (g > r * 1.25) & (g > b * 1.25)  # the screen and the shadow that falls on it
alpha = (~green).astype(np.float32)
alpha = cv2.erode(alpha, np.ones((3, 3), np.uint8))
alpha = cv2.GaussianBlur(alpha, (0, 0), 1.2)
spill = np.minimum(g, np.maximum(r, b) * 1.05)  # take the green cast off the edges
img[..., 1] = np.where(alpha < 0.999, spill, g)
img = np.clip(255.0 * (img / 255.0) ** GAMMA * np.array(GAIN[::-1], dtype=np.float32), 0, 255)  # BGR order
ys, xs = np.where(alpha > 0.5)
y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
img, alpha = img[y0:y1, x0:x1], alpha[y0:y1, x0:x1]
scale = HEIGHT / img.shape[0]
img = cv2.resize(img, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
alpha = cv2.resize(alpha, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
h, w = alpha.shape

cap = cv2.VideoCapture(clip)
fps, W, H, n = cap.get(cv2.CAP_PROP_FPS), int(cap.get(3)), int(cap.get(4)), int(cap.get(7))
step = (W + w) / FRAMES  # pixels travelled per frame
k = max(3, int(step * SMEAR)) | 1  # the smear: part of the distance covered while the shutter is open
pad = k
prem = cv2.copyMakeBorder(img * alpha[..., None], 0, 0, pad, pad, cv2.BORDER_CONSTANT, value=0)
al = cv2.copyMakeBorder(alpha, 0, 0, pad, pad, cv2.BORDER_CONSTANT, value=0)
prem = cv2.blur(prem, (k, 1))
al = cv2.blur(al, (k, 1))
sw = al.shape[1]
# the shadow on the ground: a flat soft ellipse under the cut-out, smeared by the same speed
sh_h = max(24, int(h * 0.07))
sh = np.zeros((sh_h * 2, sw), np.float32)
cv2.ellipse(sh, (sw // 2, sh_h), (int(w * 0.42), sh_h // 2), 0, 0, 360, 1.0, -1)
sh = cv2.blur(cv2.GaussianBlur(sh, (0, 0), sh_h * 0.3), (k, 1)) * SHADOW

ff = subprocess.Popen(["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{W}x{H}", "-r", str(fps), "-i", "-", "-i", clip,
                       "-map", "0:v", "-map", "1:a?", "-c:v", "libx264", "-crf", "14", "-preset", "slow", "-pix_fmt", "yuv420p", "-c:a", "copy", out], stdin=subprocess.PIPE)
f0 = int(round(AT * fps))
for i in range(n):
    ok, frame = cap.read()
    if not ok:
        break
    j = i - f0
    if 0 <= j < FRAMES - SKIP_LAST:
        centre = (W + w / 2) - step * (j + 0.5) if DIR == "left" else (-w / 2) + step * (j + 0.5)
        left = int(round(centre - sw / 2))
        top = BOTTOM - h
        xa, xb = max(0, left), min(W, left + sw)
        if SHADOW and xb > xa:  # first the shadow, on the street, then the riders over it
            st = BOTTOM - sh_h - int(h * 0.01)
            sya, syb = max(0, st), min(H, st + sh.shape[0])
            if syb > sya:
                d = sh[sya - st:syb - st, xa - left:xb - left][..., None]
                frame[sya:syb, xa:xb] = (frame[sya:syb, xa:xb].astype(np.float32) * (1 - d) + np.array([60, 30, 45], np.float32) * d * 0.5).astype(np.uint8)
        ya, yb = max(0, top), min(H, top + h)
        if xb > xa and yb > ya:
            sa = al[ya - top:yb - top, xa - left:xb - left][..., None]
            sp = prem[ya - top:yb - top, xa - left:xb - left]
            roi = frame[ya:yb, xa:xb].astype(np.float32)
            frame[ya:yb, xa:xb] = np.clip(roi * (1 - sa) + sp, 0, 255).astype(np.uint8)
    ff.stdin.write(frame.tobytes())
ff.stdin.close()
ff.wait()
print(f"{out}: fly-past at {AT} s over {FRAMES} frames, {w}x{h} px cut-out, smear {k} px")
