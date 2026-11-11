"""Cut an AI-shot film from an edit list.

    python3 src/film/cut.py src/film/<film>.json en        # -> <out>-en_9x16_1080x1920.mp4 (+ _4x5_1080x1350)
    python3 src/film/cut.py src/film/<film>.json he        # same picture; Hebrew captions, straps and inserts
    python3 src/film/cut.py src/film/<film>.json en --draft  # half-size picture, no 4:5: a quick look at the cut

The edit list names the generated shots (URLs are downloaded once into work/<slug>/clips) and, per segment, the
in/out points, speed, punch-in, shake and the transition, the line that is spoken (captions are timed to the
actual speech with ASR, word by word), straps and stickers, and effects. A segment's sound can run on under the
following pictures ("audio_out", for reaction cutaways). Music cues are real audio files and are
ducked under every spoken word; everything is mixed and mastered to -14 LUFS as shipped.

Picture: every frame goes through OpenCV (cover-scale to 1080x1920, animated zoom, shake, whip/flash/zoom
transitions, a light sharpen and grain so mixed sources sit together), then captions are composited from an
HTML layer (src/film/overlay.html).
"""
import difflib
import json
import math
import re
import subprocess
import sys
from pathlib import Path

import cv2
import numpy as np
from scipy import signal as ss
from scipy.io import wavfile

HERE = Path(__file__).resolve().parent
SRC = HERE.parent
ROOT = SRC.parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(SRC / "audio"))
try:  # only needed for a clip that has no "<clip>.words.json" beside it
    import asr  # noqa: E402
except ImportError:
    asr = None

ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
EDL_PATH = Path(ARGS[0]).resolve()
LANG = ARGS[1] if len(ARGS) > 1 else "en"
DRAFT = "--draft" in sys.argv
EDL = json.load(open(EDL_PATH))
SLUG = EDL.get("slug", EDL_PATH.stem)
FPS = EDL.get("fps", 24)
SR = 48000
W, H = (540, 960) if DRAFT else (1080, 1920)
WORK = ROOT / "work" / SLUG
CUT = WORK / f"cut-{LANG}{'-draft' if DRAFT else ''}"
CUT.mkdir(parents=True, exist_ok=True)
LOOK = EDL.get("look", {})


def run(cmd, **kw):
    return subprocess.run([str(c) for c in cmd], check=True, **kw)


def tr(v):
    return v.get(LANG, v.get("en")) if isinstance(v, dict) else v


def db(x):
    return 10 ** (x / 20)


# ------------------------------------------------------------------ sources
def source(name):
    s = EDL["sources"][name]
    if "path" in s:
        return ROOT / tr(s["path"]).format(lang=LANG)
    p = WORK / "clips" / f"{name}.mp4"
    if not p.exists():
        p.parent.mkdir(parents=True, exist_ok=True)
        run(["curl", "-sS", "-m", "300", "-o", p, s["url"]])
    return p


_probe = {}


def probe(path):
    if path not in _probe:
        j = json.loads(run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,r_frame_rate,duration", "-of", "json", path],
                           capture_output=True).stdout)["streams"][0]
        a, b = j["r_frame_rate"].split("/")
        _probe[path] = {"w": j["width"], "h": j["height"], "fps": float(a) / float(b), "dur": float(j.get("duration") or 0)}
    return _probe[path]


_words = {}


def words_of(name):
    """ASR words of a whole source clip: from "<clip>.words.json" beside the clip when it is there (so a fresh
    checkout needs no speech models), otherwise recognised now and cached next to the cut."""
    if name not in _words:
        cache = WORK / "asr" / f"{name}-{LANG if 'path' in EDL['sources'][name] else 'x'}.json"
        side = Path(source(name)).with_suffix(".words.json")
        if side.exists():
            _words[name] = json.load(open(side))
        elif cache.exists():
            _words[name] = json.load(open(cache))
        else:
            if asr is None:
                sys.exit(f"{name}: no word timings beside the clip and sherpa-onnx is not installed (see src/film/asr.py)")
            ws, wt = asr.words(source(name))
            cache.parent.mkdir(parents=True, exist_ok=True)
            cache.write_text(json.dumps({"words": ws, "whisper": wt}))
            _words[name] = {"words": ws, "whisper": wt}
    return _words[name]["words"]


# ------------------------------------------------------------------ picture
E = {
    "linear": lambda p: p,
    "out": lambda p: 1 - (1 - p) ** 3,
    "in": lambda p: p ** 3,
    "inout": lambda p: 4 * p ** 3 if p < 0.5 else 1 - (-2 * p + 2) ** 3 / 2,
}
TRANS_FRAMES = 4  # a transition spends this many frames on each side of the cut
_rng = np.random.default_rng(7)
_grain = [(_rng.standard_normal((H, W)).astype(np.float32)) for _ in range(8)]


def add_grain(f, amount, seed):
    return np.clip(f.astype(np.float32) + (_grain[seed % len(_grain)] * amount)[:, :, None], 0, 255).astype(np.uint8)


def _box_mask(x0, y0, x1, y1, feather):
    m = np.zeros((H, W), np.float32)
    m[y0:y1, x0:x1] = 1
    return cv2.GaussianBlur(m, (0, 0), max(feather, 0.5))[:, :, None]


def fix_frame(f, fixes):
    """Repairs made to a source before it is cut: all coordinates are in 1080x1920 film pixels.
    clone: cover a box with the picture a little way off (a logo, a burnt-in number);
    heal:  paint out small marks of one colour inside a window from the skin around them (a graze, a cut);
    calm:  fade marks that are redder than the skin around them (spots, a scar) without repainting anything;
    key:   move one colour range to another hue (curtains that should match the hall);
    grade: gain, per-channel gains and saturation (so clips of one room sit together);
    shade: darken the top of the frame and the corners, so a bright bare wall stops pulling the eye off the face."""
    k = W / 1080
    for fx in fixes:
        if "clone" in fx:
            x0, y0, x1, y1 = [int(round(v * k)) for v in fx["clone"]]
            dx, dy = [int(round(v * k)) for v in fx.get("from", [0, 60])]
            src = np.roll(f, (-dy, -dx), axis=(0, 1))
            m = _box_mask(x0, y0, x1, y1, fx.get("feather", 6) * k)
            f = (f * (1 - m) + src * m).astype(np.uint8)
        elif "heal" in fx:
            q = fx["heal"]
            hsv = cv2.cvtColor(f, cv2.COLOR_BGR2HSV)
            h, sat, val = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
            lo, hi = q["hue"]
            hm = ((h >= lo) | (h <= hi)) if lo > hi else ((h >= lo) & (h <= hi))
            m = (hm & (sat >= q.get("s_min", 85)) & (val >= q.get("v", [50, 215])[0]) & (val <= q.get("v", [50, 215])[1])).astype(np.uint8)
            box = np.zeros_like(m)
            for x0, y0, x1, y1 in q["windows"]:
                box[int(y0 * k):int(y1 * k), int(x0 * k):int(x1 * k)] = 1
            el = lambda d: cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (max(1, int(d * k)) | 1, max(1, int(d * k)) | 1))  # noqa: E731
            m = cv2.dilate(cv2.morphologyEx(m * box, cv2.MORPH_CLOSE, el(q.get("close", 11))), el(q.get("dilate", 13)))
            if m.any():
                fill = cv2.inpaint(f, m * 255, q.get("radius", 7) * k, cv2.INPAINT_TELEA)
                a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), q.get("feather", 3) * k)[:, :, None]
                f = (f * (1 - a) + fill * a).astype(np.uint8)
        elif "key" in fx:
            q = fx["key"]
            hsv = cv2.cvtColor(f, cv2.COLOR_BGR2HSV)
            h, sat, val = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
            lo, hi = q["hue"]
            hm = ((h >= lo) | (h <= hi)) if lo > hi else ((h >= lo) & (h <= hi))
            m = (hm & (sat > q.get("s_min", 160)) & (val >= q.get("v", [15, 110])[0]) & (val <= q.get("v", [15, 110])[1])).astype(np.uint8)
            el = lambda d: cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (max(1, int(d * k)) | 1, max(1, int(d * k)) | 1))  # noqa: E731
            m = cv2.dilate(cv2.morphologyEx(m, cv2.MORPH_OPEN, el(q.get("open", 7))), el(q.get("dilate", 9)))
            if "region" in q:  # only inside this box (x0, y0, x1, y1)
                x0, y0, x1, y1 = [int(v * k) for v in q["region"]]
                box = np.zeros_like(m)
                box[y0:y1, x0:x1] = 1
                m = m * box
            for cx, cy, rx, ry in q.get("keep", []):  # never touch a face
                cv2.ellipse(m, (int(cx * k), int(cy * k)), (int(rx * k), int(ry * k)), 0, 0, 360, 0, -1)
            m = cv2.GaussianBlur(m.astype(np.float32), (0, 0), q.get("feather", 4) * k)[:, :, None]
            new = hsv.copy()
            new[:, :, 0] = q["to_hue"]
            new[:, :, 1] = np.clip(sat.astype(np.float32) * q.get("s", 0.85), 0, 255)
            new[:, :, 2] = np.clip(val.astype(np.float32) * q.get("v_gain", 0.8), 0, 255)
            f = (f * (1 - m) + cv2.cvtColor(new, cv2.COLOR_HSV2BGR) * m).astype(np.uint8)
        elif "calm" in fx:  # pull marks that are redder than the skin around them back to that skin (no repainting)
            q = fx["calm"]
            src = cv2.cvtColor(f, cv2.COLOR_BGR2LAB)
            lab = src.astype(np.float32)
            ks = int(q.get("base", 61) * k) | 1
            med = [cv2.medianBlur(src[:, :, c], ks).astype(np.float32) for c in range(3)]
            m = np.clip((lab[:, :, 1] - med[1] - q.get("da", 3.5)) / q.get("soft", 3), 0, 1)
            box = np.zeros((H, W), np.float32)
            for x0, y0, x1, y1 in q["windows"]:
                box[int(y0 * k):int(y1 * k), int(x0 * k):int(x1 * k)] = 1
            box = cv2.GaussianBlur(box, (0, 0), 8 * k)
            el = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (max(1, int(q.get("dilate", 9) * k)) | 1,) * 2)
            m = np.clip(cv2.GaussianBlur(cv2.dilate(m, el), (0, 0), q.get("feather", 3) * k) * box * q.get("gain", 1.0), 0, 1)
            for c, keep in ((0, q.get("l_keep", 0.3)), (1, q.get("keep", 0.1)), (2, q.get("keep", 0.1))):
                lab[:, :, c] = lab[:, :, c] * (1 - m) + (med[c] + (lab[:, :, c] - med[c]) * keep) * m
            f = cv2.cvtColor(np.clip(lab, 0, 255).astype(np.uint8), cv2.COLOR_LAB2BGR)
        elif "soften" in fx:  # blur a box until lettering or a crest in it cannot be read
            x0, y0, x1, y1 = [int(round(v * k)) for v in fx["soften"]]
            m = _box_mask(x0, y0, x1, y1, fx.get("feather", 14) * k)
            f = (f * (1 - m) + cv2.GaussianBlur(f, (0, 0), fx.get("sigma", 9) * k) * m).astype(np.uint8)
        elif "shade" in fx:
            q = fx["shade"]
            key = (q.get("top", 0.78), q.get("to_y", 700), q.get("vig", 0.0))
            if key not in _SHADE:
                yy = np.clip(np.arange(H, dtype=np.float32) / (key[1] * k), 0, 1)
                g = np.repeat((key[0] + (1 - key[0]) * yy)[:, None], W, axis=1)
                if key[2]:
                    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
                    g = g * (1 - key[2] * (((xs - W / 2) / (W / 2)) ** 2 + ((ys - H / 2) / (H / 2)) ** 2) / 2)
                _SHADE[key] = g[:, :, None].astype(np.float32)
            f = np.clip(f.astype(np.float32) * _SHADE[key], 0, 255).astype(np.uint8)
        elif "grade" in fx:
            q = fx["grade"]
            x = f.astype(np.float32) * q.get("gain", 1.0) * np.float32([q.get("b", 1.0), q.get("g", 1.0), q.get("r", 1.0)])
            if q.get("sat", 1.0) != 1.0:
                luma = x @ np.float32([0.114, 0.587, 0.299])
                x = luma[:, :, None] + (x - luma[:, :, None]) * q["sat"]
            f = np.clip(x, 0, 255).astype(np.uint8)
    return f


_FIXES = None
_SHADE = {}


def fixes_of(path):
    global _FIXES
    if _FIXES is None:
        _FIXES = {str(source(n)): v.get("fix") for n, v in EDL["sources"].items() if v.get("fix")}
    return _FIXES.get(str(path))


def read_frames(path, t_in, n_out, speed):
    """Yield n_out frames from path starting at t_in, cover-scaled to WxH, sampling the source at the given speed.
    Slow motion blends neighbouring frames, so it does not stutter."""
    fixes = fixes_of(path)
    info = probe(path)
    scale = max(W / info["w"], H / info["h"])
    sw, sh = math.ceil(info["w"] * scale / 2) * 2, math.ceil(info["h"] * scale / 2) * 2
    need = n_out * speed / FPS + 0.5
    p = subprocess.Popen(["ffmpeg", "-v", "error", "-ss", f"{t_in:.4f}", "-i", str(path), "-t", f"{need:.3f}", "-an",
                          "-vf", f"scale={sw}:{sh}:flags=lanczos,crop={W}:{H}", "-f", "rawvideo", "-pix_fmt", "bgr24", "-"],
                         stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    size, k, i, last, prev = W * H * 3, 0, -1, None, None
    slow = speed < 0.97
    while k < n_out:
        pos = k * speed / FPS * info["fps"] + 1e-6
        want = int(pos) + (1 if slow else 0)
        while i < want:
            buf = p.stdout.read(size)
            if len(buf) < size:
                break
            prev, last = last, np.frombuffer(buf, np.uint8).reshape(H, W, 3)
            if fixes:
                last = fix_frame(last, fixes)
            i += 1
        if last is None:
            raise RuntimeError(f"no frames from {path} at {t_in}")
        a = pos - int(pos)
        if slow and prev is not None and i == want and 0.02 < a < 0.98:
            yield cv2.addWeighted(prev, 1 - a, last, a, 0)
        elif slow and prev is not None and i == want and a <= 0.02:
            yield prev
        else:
            yield last
        k += 1
    p.kill()  # we asked for a little more than we use
    p.stdout.close()
    p.wait()


def shake_xy(t, amp, seed):
    if not amp:
        return 0.0, 0.0
    x = math.sin(t * 7.3 + seed) * 0.6 + math.sin(t * 13.1 + seed * 2.1) * 0.3 + math.sin(t * 23.7 + seed * 0.7) * 0.1
    y = math.sin(t * 6.1 + seed * 1.3) * 0.6 + math.sin(t * 11.9 + seed * 0.4) * 0.3 + math.sin(t * 29.3 + seed * 1.9) * 0.1
    return x * amp, y * amp


def whip(frame, p, direction):
    """Directional smear and shove; p goes 0 (clean) to 1 (fully gone)."""
    if p <= 0:
        return frame
    horiz = direction in ("l", "r")
    k = int(2 + p * (0.34 * (W if horiz else H)))
    w7 = max(3, int(7 * W / 1080))
    out = cv2.blur(frame, (k, w7) if horiz else (w7, k))
    sign = -1 if direction in ("l", "u") else 1
    shift = int(sign * p * p * (W if horiz else H) * 0.55)
    M = np.float32([[1, 0, shift if horiz else 0], [0, 1, 0 if horiz else shift]])
    return cv2.warpAffine(out, M, (W, H), borderMode=cv2.BORDER_REFLECT)


def band_frames(b, n, speed):
    """Frames for one band of a split screen. Old style: a slice around cy of the punched frame. With "face":
    the whole frame is returned and render_bands places that face where the band wants it."""
    z, cx, cy = b.get("punch", 1.0), b.get("cx", 0.5), b.get("cy", 0.4)
    for f in read_frames(source(b["src"]), b["in"], n, b.get("speed", speed)):
        if z != 1.0 and "face" not in b:
            ox, oy = cx * W, cy * H
            tx, ty = min(0, max(W - z * W, ox - z * ox)), min(0, max(H - z * H, oy - z * oy))
            f = cv2.warpAffine(f, np.float32([[z, 0, tx], [0, z, ty]]), (W, H), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REFLECT)
        yield f


def render_bands(seg, n, hold, enc, idx):
    """A split screen of stacked bands. "seams": the y of each divider in 1920-high film pixels (default: equal
    bands). A band with "face": [x, y] (fractions of its source frame) is scaled by "punch" and moved so that
    point lands at "at" (film y, default the middle of the band). "stagger": seconds between bands landing."""
    bands = seg["bands"]
    seams = [int(round(y * H / 1920)) for y in seg.get("seams", [])] or [i * (H // len(bands)) for i in range(1, len(bands))]
    edges = [0] + seams + [H]
    gens = [band_frames(b, n, seg.get("speed", 1.0)) for b in bands]
    stagger = int(round(seg.get("stagger", 0.0) * FPS))
    gap = max(2, int(6 * W / 1080))
    sharpen, grain = LOOK.get("sharpen", 0.35), LOOK.get("grain", 2.0)
    last = [None] * len(bands)
    for k in range(n + hold):
        out = np.zeros((H, W, 3), np.uint8)
        for bi, (b, g) in enumerate(zip(bands, gens)):
            if k < n:
                last[bi] = next(g)
            f = last[bi]
            top, bot = edges[bi], edges[bi + 1]
            if "face" in b:
                z = b.get("punch", 1.0)
                fx, fy = b["face"]
                at = b.get("at", (top + bot) / 2 * 1920 / H) * H / 1920
                tx = min(0, max(W - z * W, W / 2 - z * fx * W))
                ty = min(top, max(bot - z * H, at - z * fy * H))
                sl = cv2.warpAffine(f, np.float32([[z, 0, tx], [0, z, ty]]), (W, H), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REFLECT)[top:bot]
            else:
                y0 = int(np.clip(b.get("cy", 0.4) * H - (bot - top) / 2, 0, H - (bot - top)))
                sl = f[y0:y0 + bot - top]
            age = k - bi * stagger
            if age < 0:  # not landed yet: the band waits in the dark, so the screen is never empty
                sl = (sl * 0.28).astype(np.uint8)
            elif age < 3:  # a quick white pop as each band lands
                a = 0.6 * (1 - age / 3)
                sl = cv2.addWeighted(sl, 1 - a, np.full_like(sl, 255), a, 0)
            out[top:bot] = sl
        if sharpen:
            out = cv2.addWeighted(out, 1 + sharpen, cv2.GaussianBlur(out, (0, 0), 1.2 * W / 1080), -sharpen, 0)
        if grain:
            out = add_grain(out, grain, idx * 3 + k)
        for y in seams:
            out[y - gap // 2: y + gap // 2] = 12
        enc.stdin.write(np.ascontiguousarray(out).tobytes())


def render_segment(seg, n, hold, enc, idx):
    """Write n + hold frames of one segment to the encoder."""
    if "bands" in seg:
        return render_bands(seg, n, hold, enc, idx)
    z = seg.get("zoom") or {}
    z0, z1 = z.get("from", seg.get("punch", 1.0)), z.get("to", seg.get("punch", 1.0))
    cx, cy = z.get("cx", seg.get("cx", 0.5)), z.get("cy", seg.get("cy", 0.42))
    ease = E[z.get("ease", "linear")]
    pop = seg.get("pop", LOOK.get("pop", 0.0))
    amp = seg.get("shake", LOOK.get("shake", 0.0)) * (W / 1080)
    t_in, t_out = seg.get("trans", "cut"), seg.get("trans_out", "cut")
    rgb = seg.get("rgb", 0)
    sharpen, grain = LOOK.get("sharpen", 0.35), LOOK.get("grain", 2.0)
    total = n + hold
    last = None
    frames = read_frames(source(seg["src"]), seg["in"], n, seg.get("speed", 1.0))
    for k in range(total):
        if k < n:
            last = next(frames)
        f = last
        p = k / max(total - 1, 1)
        s = z0 + (z1 - z0) * ease(p)
        if pop and k < 6:
            s *= 1 + pop * (1 - k / 6) ** 2
        if t_in == "zoom" and k < TRANS_FRAMES:
            s *= 1 + 0.5 * (1 - (k + 1) / (TRANS_FRAMES + 1)) ** 1.5
        if t_out == "zoom" and k >= total - TRANS_FRAMES:
            s *= 1 + 0.6 * ((k - (total - TRANS_FRAMES) + 1) / TRANS_FRAMES) ** 2
        dx, dy = shake_xy((idx * 1.7 + k) / FPS, amp, idx)
        if abs(s - 1) > 1e-4 or dx or dy:
            # scale about (cx, cy), then clamp so the frame never shows an edge
            ox, oy = cx * W, cy * H
            tx, ty = ox - s * ox + dx, oy - s * oy + dy
            tx, ty = min(0, max(W - s * W, tx)), min(0, max(H - s * H, ty))
            f = cv2.warpAffine(f, np.float32([[s, 0, tx], [0, s, ty]]), (W, H), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REFLECT)
        if sharpen:
            sh = min(0.6, sharpen * s * s)  # a punched-in 720p source needs more help
            f = cv2.addWeighted(f, 1 + sh, cv2.GaussianBlur(f, (0, 0), 1.2 * W / 1080), -sh, 0)
        if rgb:
            r = int(rgb * W / 1080 * (1 + 0.5 * math.sin(k * 2.1)))
            f = f.copy()
            f[:, :, 2] = np.roll(f[:, :, 2], r, axis=1)
            f[:, :, 0] = np.roll(f[:, :, 0], -r, axis=1)
        if grain:
            f = add_grain(f, grain, idx * 3 + k)
        if t_in.startswith("whip_") and k < TRANS_FRAMES:
            f = whip(f, 1 - (k + 1) / (TRANS_FRAMES + 1), {"l": "r", "r": "l", "u": "d", "d": "u"}[t_in[-1]])
        if t_out.startswith("whip_") and k >= total - TRANS_FRAMES:
            f = whip(f, (k - (total - TRANS_FRAMES) + 1) / (TRANS_FRAMES + 1), t_out[-1])
        if t_in == "flash" and k < 5:
            a = 0.85 * (1 - k / 5) ** 1.5
            f = cv2.addWeighted(f, 1 - a, np.full_like(f, 255), a, 0)
        if t_in == "dip" and k < 4:
            f = (f * ((k + 1) / 5)).astype(np.uint8)
        enc.stdin.write(np.ascontiguousarray(f).tobytes())


# ------------------------------------------------------------------ plan the cut
segs, t = [], 0.0
for i, sg in enumerate(EDL["segments"]):
    if sg.get("skip") or (sg.get("only") and sg["only"] != LANG):
        continue
    if "bands" in sg:  # a split screen is described by its bands; it is silent and "len" seconds long
        sg = {**sg, "src": sg["bands"][0]["src"], "in": sg["bands"][0]["in"], "out": sg["bands"][0]["in"] + sg["len"], "mute": True}
    sg = {**sg, **{k: tr(sg[k]) for k in ("in", "out", "fit", "hold", "speed") if isinstance(sg.get(k), dict)}}  # {"en": .., "he": ..}: a line that runs longer in one language
    speed = sg.get("speed", 1.0)
    if "fit" in sg:  # play this stretch of source in exactly this many seconds (to land the next cut on the beat)
        speed = (sg["out"] - sg["in"]) / sg["fit"]
        if not 0.8 <= speed <= 1.3:
            print(f"  note: segment {i} ({sg['src']} {sg['in']}-{sg['out']}) would run at x{speed:.2f} to fit {sg['fit']} s")
    n = max(1, int(round((sg["out"] - sg["in"]) / speed * FPS)))
    hold = int(round(sg.get("hold", 0) * FPS))
    segs.append({**sg, "i": len(segs), "t": t, "n": n, "hold_n": hold, "dur": (n + hold) / FPS, "speed": speed})
    t += (n + hold) / FPS
TOTAL = t
N_TOTAL = sum(s["n"] + s["hold_n"] for s in segs)
print(f"cut: {len(segs)} segments, {TOTAL:.2f} s ({LANG}{', draft' if DRAFT else ''})")
BPM = EDL.get("bpm")
for s in segs:
    beat = f"beat {s['t'] * BPM / 60:6.2f}" if BPM else ""
    ln = s.get("line")
    print(f"  {s['t']:6.2f} {beat}  {s['dur']:5.2f}s  {s['src']:<12} {s['in']:.2f}-{s['out']:.2f} x{s['speed']:.2f}  {(ln.get('en') if isinstance(ln, dict) else ln) or ''}")


def film_t(s, src_t):
    """Film time of a source time inside a segment."""
    return s["t"] + (src_t - s["in"]) / s["speed"]


marks = {}
for s in segs:
    for k, v in (s.get("marks") or {}).items():
        marks[k] = round(film_t(s, v), 3)


def when(x):
    """A film time: a number, a mark name, or 'mark+0.2'."""
    if isinstance(x, (int, float)):
        return float(x)
    m = re.match(r"^([a-zA-Z_][\w-]*)\s*([+-]\s*[\d.]+)?$", str(x))
    return marks[m.group(1)] + (float(m.group(2).replace(" ", "")) if m.group(2) else 0.0)


# ------------------------------------------------------------------ captions timed to the real speech
def norm(w):
    return re.sub(r"[^a-z0-9']+", "", w.lower())


def align(script_words, heard):
    """Give each script word a (t0, t1) from the heard words [(w, t0, t1)], interpolating what was not matched."""
    if not heard:
        return None
    a, b = [norm(w) for w in script_words], [norm(w) for w, _, _ in heard]
    times = [None] * len(a)
    for blk in difflib.SequenceMatcher(None, a, b, autojunk=False).get_matching_blocks():
        for k in range(blk.size):
            times[blk.a + k] = (heard[blk.b + k][1], heard[blk.b + k][2])
    t_first, t_last = heard[0][1], heard[-1][2]
    known = [(k, tt) for k, tt in enumerate(times) if tt]
    if not known:
        step = (t_last - t_first) / len(a)
        return [(t_first + k * step, t_first + (k + 1) * step) for k in range(len(a))]
    for k in range(len(a)):
        if times[k]:
            continue
        prev = max([j for j, _ in known if j < k], default=None)
        nxt = min([j for j, _ in known if j > k], default=None)
        lo = times[prev][1] if prev is not None else t_first
        hi = times[nxt][0] if nxt is not None else t_last
        lo_k, hi_k = (prev if prev is not None else -1), (nxt if nxt is not None else len(a))
        step = (hi - lo) / (hi_k - lo_k)
        times[k] = (lo + (k - lo_k - 1) * step + step * 0.0, lo + (k - lo_k) * step)
    return times


def chunks(words, max_words, max_chars):
    out, cur = [], []
    for w in words:
        if cur and (len(cur) >= max_words or sum(len(x["w"]) for x in cur) + len(w["w"]) + len(cur) > max_chars):
            out.append(cur)
            cur = []
        cur.append(w)
        if re.search(r"[.!?…]$", w["w"]) and len(cur) >= 1:
            out.append(cur)
            cur = []
    if cur:
        out.append(cur)
    return out


tl = {"dur": TOTAL, "lang": LANG, "caps": [], "lts": [], "stickers": [], "tags": [], "notifs": [], "badges": [], "bursts": [], "gfx": []}


def tr_deep(v):
    """Pick this language's text everywhere inside a graphic's description."""
    if isinstance(v, dict):
        return tr(v) if ("en" in v or "he" in v) and all(k in ("en", "he") for k in v) else {k: tr_deep(x) for k, x in v.items()}
    if isinstance(v, list):
        return [tr_deep(x) for x in v]
    return v

speech_spans = []  # film-time spans where somebody speaks: the music ducks here
for s in segs:
    heard = []
    if s.get("line") or s.get("speech"):
        heard = [(w, a, b) for w, a, b in words_of(s["src"]) if s["in"] - 0.08 <= a < s.get("audio_out", s["out"]) - 0.02]
        if s.get("say_from") is not None:
            heard = [h for h in heard if h[1] >= s["say_from"]]
        for w, a, b in heard:
            speech_spans.append((film_t(s, a) - 0.06, film_t(s, min(b, s.get("audio_out", s["out"]))) + 0.14))
    if s.get("line") and not s.get("no_caps"):
        en_line = tr({"en": s["line"]["en"]} if isinstance(s["line"], dict) else s["line"])
        en_groups = [g.split() for g in en_line.split("|") if g.strip()]  # "|" forces a caption break
        en_words = [w for g in en_groups for w in g]
        times = align(en_words, heard)
        if times is None:
            a0, a1 = s["in"] + 0.15, s["out"] - 0.15
            step = (a1 - a0) / len(en_words)
            times = [(a0 + k * step, a0 + (k + 1) * step) for k in range(len(en_words))]
        # the recogniser lets the last word run on to the next sound, so estimate where it really ends
        span0 = times[0][0]
        span1 = min(times[-1][1], times[-1][0] + 0.18 + 0.06 * len(en_words[-1]), s.get("audio_out", s["out"]))
        if LANG == "he" and isinstance(s["line"], dict) and s["line"].get("he"):
            # Hebrew over English speech: one card per phrase ("|" splits cards; otherwise sentences), each timed
            # to the matching English sentence, so the word that carries the joke is always on screen.
            sents, cur = [], []
            for w, (a, b) in zip(en_words, times):
                cur.append((a, b, w))
                if re.search(r"[.!?…]$", w):
                    sents.append(cur)
                    cur = []
            if cur:
                sents.append(cur)
            he = s["line"]["he"]
            cards = [c.strip() for c in (he.split("|") if "|" in he else re.split(r"(?<=[.!?…])\s+", he)) if c.strip()]
            if "|" in he and len(cards) == len(en_groups):  # one Hebrew card per English caption group
                spans, k = [], 0
                for g in en_groups:
                    a, b = times[k][0], times[k + len(g) - 1]
                    spans.append((a, min(b[1], b[0] + 0.18 + 0.06 * len(g[-1]), s.get("audio_out", s["out"]))))
                    k += len(g)
            elif len(cards) == len(sents):
                spans = [(sn[0][0], min(sn[-1][1], sn[-1][0] + 0.18 + 0.06 * len(sn[-1][2]), s.get("audio_out", s["out"]))) for sn in sents]
            else:
                tot, acc, spans = sum(len(c) for c in cards), 0, []
                for c in cards:
                    spans.append((span0 + (span1 - span0) * acc / tot, span0 + (span1 - span0) * (acc + len(c)) / tot))
                    acc += len(c)
            cs = []
            for c, (a, b) in zip(cards, spans):
                hw = c.split()
                step = (b - a) / len(hw)
                cs.append([{"w": w, "t0": film_t(s, a + k * step), "t1": film_t(s, a + (k + 1) * step)} for k, w in enumerate(hw)])
        else:
            ws = [{"w": w, "t0": film_t(s, a), "t1": film_t(s, b)} for w, (a, b) in zip(en_words, times)]
            if len(en_groups) > 1:
                cs, k = [], 0
                for g in en_groups:
                    cs.append(ws[k:k + len(g)])
                    k += len(g)
            else:
                cs = chunks(ws, 3, 15)
        seg_end = s["t"] + s["dur"]
        if film_t(s, s.get("audio_out", s["out"])) - seg_end > 0.3:  # a real L-cut: the line goes on under other pictures
            seg_end = film_t(s, s["audio_out"])
        if "cap_until" in s:  # the spoken line's captions clear the screen here (another voice's caption follows)
            seg_end = min(seg_end, s["t"] + s["cap_until"])
        for k, c in enumerate(cs):
            t0 = max(s["t"] + s.get("cap_from", 0.0), c[0]["t0"] - 0.05)  # "cap_from": another voice's caption is up until then
            if t0 > seg_end - 0.12:  # never let a caption spill onto the next speaker's shot
                continue
            t1 = min(seg_end, cs[k + 1][0]["t0"] - 0.05 if k + 1 < len(cs) else c[-1]["t1"] + 0.45)
            tl["caps"].append({"t0": round(t0, 3), "t1": round(max(t1, t0 + 0.25), 3), "y": s.get("cap_y", LOOK.get("cap_y", 1240)),
                               **({"cls": s["cap_cls"]} if "cap_cls" in s else {}),
                               "words": [{"w": w["w"], "t0": round(w["t0"], 3), "t1": round(w["t1"], 3)} for w in c]})
    # "cap": a caption with its own timing, for a voice that is not this picture's (a chorus heard over a cutaway)
    for it in ([s["cap"]] if isinstance(s.get("cap"), dict) else s.get("cap") or []):
        ws_ = tr(it["text"]).split()
        t0 = s["t"] + it.get("at", 0.0)
        ln = it.get("len", max(0.5, 0.28 * len(ws_)))
        step = ln / len(ws_)
        tl["caps"].append({"t0": round(t0, 3), "t1": round(t0 + ln + it.get("hold", 0.3), 3), "y": it.get("y", s.get("cap_y", LOOK.get("cap_y", 1240))),
                           **({"cls": it["cls"]} if "cls" in it else {}), **({"size": it["size"]} if "size" in it else {}),
                           "words": [{"w": w, "t0": round(t0 + k * step, 3), "t1": round(t0 + (k + 1) * step, 3)} for k, w in enumerate(ws_)]})
    # "gfx": free graphics drawn by overlay.html by "type" (card, chat, circle); "at" and "len" are seconds in the shot,
    # every other time inside is seconds from the graphic's own start
    for it in s.get("gfx", []):
        at_ = tr(it.get("at", 0.0))  # may differ per language: {"en": .., "he": ..}
        t0 = s["t"] + at_
        g = tr_deep({k: v for k, v in it.items() if k not in ("at", "len")})
        tl["gfx"].append({**g, "t0": round(t0, 3), "t1": round(min(t0 + tr(it.get("len", s["dur"] - at_)), TOTAL), 3)})
    # "burst": things piling up around the speaker: {"kind": "tabs"|"chat", "at", "len", "step", "cards": [[x, y, rot, text, who?, me?]]}
    for it in ([s["burst"]] if isinstance(s.get("burst"), dict) else s.get("burst") or []):
        t0 = s["t"] + it.get("at", 0.0)
        cards = []
        for k, c in enumerate(it["cards"]):
            cards.append({"x": c[0], "y": c[1], "rot": c[2], "text": tr(c[3]), "t": round(t0 + k * it.get("step", 0.09), 3),
                          **({"who": tr(c[4])} if len(c) > 4 and c[4] else {}), **({"me": True} if len(c) > 5 and c[5] else {})})
        tl["bursts"].append({"t0": round(t0, 3), "t1": round(min(t0 + it.get("len", s["dur"] - it.get("at", 0.0)), TOTAL), 3), "kind": it["kind"], "cards": cards})
    for key, bucket in (("lt", "lts"), ("sticker", "stickers"), ("tag", "tags"), ("notif", "notifs"), ("badge", "badges")):
        items = s.get(key)
        for it in (items if isinstance(items, list) else [items] if items else []):
            at_ = tr(it.get("at", 0.0))
            t0 = s["t"] + at_
            e = {"t0": round(t0, 3), "t1": round(min(t0 + tr(it.get("len", s["dur"] - at_)), TOTAL), 3)}
            for f in ("name", "what", "text", "small", "title", "body", "when"):
                if f in it:
                    e[f] = tr(it[f])
            for f in ("y", "cls", "size", "rot", "rec", "x", "icon", "from", "to", "wait", "run", "more", "wsize", "scale", "stay", "drop"):
                if f in it and tr(it[f]) is not None:
                    e[f] = tr(it[f])
            tl[bucket].append(e)
# ------------------------------------------------------------------ the narrator
# "narration": [{"file": {"en": ..., "he": ...}, "at": time or mark, "text": {"en": "card one | card two", "he": ...},
#                "level": -17, "gain_db": 0, "y": 1260}]
# The recording is trimmed to the voice, laid at "at", and captioned card by card ("|" splits cards). English cards
# follow the recognised words; other languages share the time out by the length of each card.
def _decode(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-vn", "-ac", "2", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).reshape(-1, 2).astype(np.float64)


narr = []
for it in EDL.get("narration", []):
    f = it["file"]
    path = ROOT / (f.get(LANG, f.get("en")) if isinstance(f, dict) else f)
    if not path.exists():
        print(f"  note: narration file missing, skipped: {path.name}")
        continue
    a = _decode(path)
    m = np.abs(a.mean(axis=1))
    h = int(0.01 * SR)
    lv = np.array([m[k:k + h].max() for k in range(0, max(1, len(m) - h), h)])
    on = np.where(lv > 0.06 * lv.max())[0]
    v0, v1 = max(0, on[0] * h - int(0.03 * SR)), min(len(a), (on[-1] + 1) * h + int(0.1 * SR))
    a = a[v0:v1]
    t0 = when(tr(it["at"]))
    dur = len(a) / SR
    cards = [c.strip() for c in tr(it.get("text", "")).split("|") if c.strip()]
    spans = None
    if cards and LANG == "en" and asr is not None:
        side = Path(str(path) + ".words.json")
        if side.exists():
            ws = json.load(open(side))["words"]
        else:
            ws, _ = asr.words(path)
            side.write_text(json.dumps({"words": ws}))
        words_all = [w for c in cards for w in c.split()]
        times = align(words_all, [(w, x - v0 / SR, y - v0 / SR) for w, x, y in ws])
        if times:
            spans, k = [], 0
            for c in cards:
                n = len(c.split())
                spans.append((max(0.0, times[k][0]), min(dur, times[k + n - 1][0] + 0.2 + 0.06 * len(c.split()[-1]))))
                k += n
    if cards and spans is None:
        tot, acc, spans = sum(len(c) for c in cards), 0, []
        for c in cards:
            spans.append((dur * acc / tot, dur * (acc + len(c)) / tot))
            acc += len(c)
    for k, c in enumerate(cards):
        c0 = t0 + spans[k][0] - (0.05 if k else 0.0)
        c1 = t0 + (spans[k + 1][0] - 0.05 if k + 1 < len(cards) else min(dur + 0.25, spans[k][1] + 0.35))
        for sg_ in segs:  # a card whose words are (as good as) over at a cut leaves with the shot
            b = sg_["t"] + sg_["dur"]
            if c0 + 0.3 < b < c1 and b >= t0 + spans[k][1] - 0.3:
                c1 = b
        ws_ = c.split()
        step = (c1 - c0) / len(ws_)
        tl["caps"].append({"t0": round(c0, 3), "t1": round(max(c1, c0 + 0.3), 3), "y": it.get("y", LOOK.get("nar_y", LOOK.get("cap_y", 1240))), "cls": "nar",
                           "words": [{"w": w, "t0": round(c0 + j * step, 3), "t1": round(c0 + (j + 1) * step, 3)} for j, w in enumerate(ws_)]})
    narr.append({"t": t0, "a": a, "level": it.get("level", -17), "gain_db": tr(it.get("gain_db", 0))})
    print(f"  narration {t0:6.2f}-{t0 + dur:6.2f}  {tr(it.get('text', ''))[:70]}")

nar_caps = sorted([c for c in tl["caps"] if c.get("cls") == "nar"], key=lambda c: c["t0"])
for a_, b_ in zip(nar_caps, nar_caps[1:]):  # one narrator card at a time
    a_["t1"] = round(max(a_["t0"] + 0.25, min(a_["t1"], b_["t0"] - 0.04)), 3)

for it in EDL.get("labels", []):  # labels pinned for a stretch of film time: {"text", "from", "to", "y", "cls"}
    tl["tags"].append({"t0": round(when(it.get("from", 0.0)), 3), "t1": round(when(it.get("to", TOTAL)), 3), "text": tr(it["text"]),
                       **{f: it[f] for f in ("y", "cls", "rec") if f in it}})
(CUT / "timeline.json").write_text(json.dumps(tl, ensure_ascii=False, indent=1))

# ------------------------------------------------------------------ picture pass
picture = CUT / "picture.mp4"
enc = subprocess.Popen(["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgr24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
                        "-c:v", "libx264", "-preset", "medium" if DRAFT else "slow", "-crf", "14", "-pix_fmt", "yuv420p", str(picture)], stdin=subprocess.PIPE)
for s in segs:
    render_segment(s, s["n"], s["hold_n"], enc, s["i"])
enc.stdin.close()
if enc.wait() != 0:
    sys.exit("encoder failed")

if DRAFT:  # the overlay is drawn at full size; scale it down in the composite
    ov_scale = f"[1:v]scale={W}:{H}[ov];[0:v][ov]overlay=0:0:format=auto,format=yuv420p"
else:
    ov_scale = "[0:v][1:v]overlay=0:0:format=auto,format=yuv420p"
ov = CUT / "ov"
run(["node", HERE / "render-overlay.mjs", CUT / "timeline.json", ov, "--fps", FPS])
picture_ov = CUT / "picture_ov.mp4"
run(["ffmpeg", "-v", "error", "-y", "-i", picture, "-framerate", FPS, "-i", ov / "%05d.png", "-filter_complex", ov_scale,
     "-c:v", "libx264", "-preset", "medium" if DRAFT else "slow", "-crf", "19" if DRAFT else "20", "-r", FPS, picture_ov])


# ------------------------------------------------------------------ sound
def read_audio(path, t0=None, dur=None, tempo=1.0, af=None):
    """af: an ffmpeg audio filter chain for this source (a high-pass on a boomy voice, a presence lift on a dull one)."""
    cmd = ["ffmpeg", "-v", "error"]
    if t0 is not None:
        cmd += ["-ss", f"{t0:.4f}", "-t", f"{dur:.4f}"]
    cmd += ["-i", str(path), "-vn", "-ac", "2", "-ar", str(SR)]
    chain = ([af] if af else []) + ([f"atempo={tempo:.4f}"] if abs(tempo - 1) > 1e-3 else [])
    if chain:
        cmd += ["-af", ",".join(chain)]
    raw = subprocess.run(cmd + ["-f", "f32le", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).reshape(-1, 2).astype(np.float64)


def fit(a, n):
    return np.pad(a, ((0, max(0, n - len(a))), (0, 0)))[:n]


NS = int(round(TOTAL * SR))
dialog = np.zeros((NS + SR, 2))
ambient = np.zeros((NS + SR, 2))  # the natural sound of cutaways (sources with "bus": "fx"): mixed with the effects
edge = int(0.006 * SR)
ramp = np.linspace(0, 1, edge)[:, None]
# one gain per source, measured on the parts used, so a line split across cuts keeps its level
parts = {}
for s in segs:
    if s.get("mute"):
        continue
    # "audio_out": the sound runs on past the picture cut (an L-cut under the next, muted, reaction shots)
    a_out = s.get("audio_out", s["out"])
    n = int(round((a_out - s["in"]) / s["speed"] * SR)) if "audio_out" in s else int(round(s["n"] / FPS * SR))
    a = fit(read_audio(source(s["src"]), s["in"], a_out - s["in"], s["speed"], EDL["sources"][s["src"]].get("af")), n)
    if "audio_out" not in s:
        a = np.pad(a, ((0, int(round(s["dur"] * SR)) - n), (0, 0)))
    s["audio"] = a
    parts.setdefault(s["src"], []).append(s)
for name, ps in parts.items():
    m = np.concatenate([p["audio"].mean(axis=1) for p in ps])
    h = int(0.05 * SR)
    r = np.array([np.sqrt(np.mean(m[k:k + h] ** 2)) for k in range(0, max(1, len(m) - h), h)]) + 1e-9
    lvl = 20 * np.log10(np.percentile(r, 85))
    target = ps[0].get("level", EDL["sources"][name].get("level", -18))
    g = db(float(np.clip(target - lvl, -14, 20)))
    if "fixed_gain_db" in EDL["sources"][name]:  # a clip with no speech (a reaction, an ambience): do not normalise it up
        g = db(EDL["sources"][name]["fixed_gain_db"])
    bus = ambient if EDL["sources"][name].get("bus") == "fx" else dialog
    for p in ps:
        a = p["audio"] * g * db(p.get("gain_db", 0))
        fi, fo = max(edge, int(p.get("a_fade_in", 0) * SR)), max(edge, int(p.get("a_fade_out", 0) * SR))
        a[:fi] *= np.linspace(0, 1, fi)[:, None]
        a[-fo:] *= np.linspace(1, 0, fo)[:, None]
        i0 = int(round(p["t"] * SR))
        bus[i0: i0 + len(a)] += a[: len(bus) - i0]
for nr in narr:  # the narrator is a voice like any other: same bus, so the music ducks and the margins are measured
    a = nr["a"]
    mm = a.mean(axis=1)
    hh = int(0.05 * SR)
    r = np.array([np.sqrt(np.mean(mm[k:k + hh] ** 2)) for k in range(0, max(1, len(mm) - hh), hh)]) + 1e-9
    g = db(float(np.clip(nr["level"] - 20 * np.log10(np.percentile(r, 85)), -20, 24))) * db(nr["gain_db"])
    a = a * g
    a[:edge] *= ramp
    a[-edge:] *= ramp[::-1]
    i0 = int(round(nr["t"] * SR))
    dialog[i0: i0 + len(a)] += a[: len(dialog) - i0]
dialog = dialog[:NS]


def place(buf, sig, t0, gain=1.0):
    i0 = int(round(t0 * SR))
    if i0 < 0:
        sig, i0 = sig[-i0:], 0
    n = min(len(sig), len(buf) - i0)
    if n > 0:
        buf[i0: i0 + n] += sig[:n] * gain


# a voice laid in as an effect (a chorus heard over a cutaway) ducks the music like any other line
fx_voice_starts = []
for x in EDL.get("sfx", []):
    if x.get("voice"):
        t0 = when(x["at"])
        ln = (x["trim"][1] - x["trim"][0]) if "trim" in x else x.get("len", 1.0)
        speech_spans.append((t0 + x.get("voice_from", 0.0) - 0.06, t0 + x.get("voice_to", ln + 0.1)))
        fx_voice_starts.append(speech_spans[-1][0])

speech = np.zeros(NS)
duck_from = EDL.get("duck_from", 0.0)  # let the first note of the score sound before it tucks under the first word
if EDL.get("duck_mode") == "voice":
    # follow the voices themselves, so the music comes back in every real pause (the recogniser's word ends run long)
    h = int(0.01 * SR)
    dm2 = dialog[:NS].mean(axis=1) ** 2
    lvl = np.sqrt(np.convolve(dm2, np.ones(2 * h) / (2 * h), "same"))[::h]
    act = lvl > db(EDL.get("voice_gate_db", -38))
    runs, k = [], 0
    while k < len(act):  # runs of voice, as [start, end) in 10 ms steps
        if act[k]:
            j = k
            while j < len(act) and act[j]:
                j += 1
            runs.append([k, j])
            k = j
        else:
            k += 1
    merged = []
    for a, b in runs:
        if b - a < 5:  # a blip of under 50 ms is not a voice
            continue
        if merged and a - merged[-1][1] < int(EDL.get("voice_gap", 0.35) * 100):
            merged[-1][1] = b
        else:
            merged.append([a, b])
    speech_spans = [(a / 100 - 0.06, b / 100 + 0.08) for a, b in merged] + [(a, b) for a, b in speech_spans if a in fx_voice_starts]
for a, b in speech_spans:
    speech[max(0, int(max(a, duck_from) * SR)): min(NS, int(b * SR))] = 1
att, rel = math.exp(-1 / (0.03 * SR)), math.exp(-1 / (0.25 * SR))
env = np.maximum(ss.lfilter([1 - att], [1, -att], speech), ss.lfilter([1 - rel], [1, -rel], speech))
env = np.clip(env * 1.3, 0, 1)
# "unduck": at these times a line has just ended and the music must be back at once (before a stop, a drop, the end card)
_dm = dialog.mean(axis=1)


def voice_onset(t):
    """When somebody next really speaks after t: the recogniser's word starts run early (a breath, the room),
    so listen to the dialogue itself."""
    h = int(0.01 * SR)
    i = int(t * SR)
    while i + 2 * h < NS:
        if np.sqrt(np.mean(_dm[i:i + 2 * h] ** 2)) > db(-36):
            return i / SR
        i += h
    return TOTAL


for u in EDL.get("unduck", []):
    t0 = when(u)
    nxt = min([voice_onset(t0 + 0.05) - 0.06] + [a for a in fx_voice_starts if a > t0 + 0.05])
    i0, r, i1 = int(t0 * SR), int(0.06 * SR), int(nxt * SR)
    if i1 > i0 + r:
        env[i0:i0 + r] *= np.linspace(1, 0, r)
        env[i0 + r:i1] = 0
        back = min(int(0.03 * SR), i1 - i0 - r)  # tuck back under the next line over 30 ms, never in one step
        if back > 0 and i1 < len(env):
            env[i1 - back:i1] = np.linspace(0, env[i1], back)
    elif i1 <= i0 + r:
        print(f"  note: unduck at {t0:.2f} s does nothing (the next voice starts at {nxt:.2f} s)")


music = np.zeros((NS, 2))
for cue in EDL.get("music", []):
    a = read_audio(ROOT / tr(cue["file"]), af=cue.get("af"))
    at = when(cue.get("at", 0.0))
    until = when(cue["until"]) if "until" in cue else TOTAL
    a = a[int(cue.get("offset", 0.0) * SR):][: int((until - at) * SR)]
    fi, fo = int(cue.get("fade_in", 0.01) * SR), int(cue.get("fade_out", 0.05) * SR)
    if fi:
        a[:fi] *= np.linspace(0, 1, fi)[:, None]
    if fo and len(a) > fo:
        a[-fo:] *= np.linspace(1, 0, fo)[:, None] ** 1.5
    i0 = int(round(at * SR))
    e = env[i0: i0 + len(a)]
    duck = 1 - (1 - db(-cue.get("duck_db", 9))) * e
    if "split_hz" in cue:  # the bass does not mask a voice: duck it less ("duck_low_db") so the track stays full under speech
        sos = ss.butter(4, cue["split_hz"], "lowpass", fs=SR, output="sos")
        low = ss.sosfiltfilt(sos, a, axis=0)
        duck_low = 1 - (1 - db(-cue.get("duck_low_db", 3))) * e
        a = (a - low) * duck[: len(a), None] + low * duck_low[: len(a), None] * db(cue.get("low_gain_db", 0))
    else:
        a = a * duck[: len(a), None]
    place(music, a, at, db(cue.get("gain_db", -8)))

fx = np.zeros((NS, 2))
fx += ambient[:NS]
sfx = []
for x in EDL.get("sfx", []):
    t0 = when(x["at"])
    if "every" in x:  # a repeated sound (a clock tick) from "at" until the "until" mark
        k = 0
        while t0 + k * x["every"] < when(x["until"]) - 1e-6:
            sfx.append(dict(x, at=t0 + k * x["every"]))
            k += 1
    else:
        sfx.append(dict(x, at=t0))
for s in segs:
    for x in s.get("sfx", []):
        sfx.append(dict(x, at=s["t"] + x.get("at", 0.0)))
for x in sfx:
    if "file" in x:
        a = read_audio(ROOT / x["file"], af=x.get("af"))
        if "trim" in x:
            a = a[int(x["trim"][0] * SR): int(x["trim"][1] * SR)]
    else:
        import kit  # the campaign's synth kit, for the sonic logo and simple hits
        y = getattr(kit, x["synth"])(*x.get("args", []))
        y = y if y.ndim == 2 else np.stack([y, y], axis=1)
        a = ss.resample_poly(y, SR, kit.SR, axis=0)
    fo = int(x.get("fade_out", 0.02) * SR)
    if fo and len(a) > fo:
        a[-fo:] *= np.linspace(1, 0, fo)[:, None]
    place(fx, a, x["at"], db(x.get("gain_db", -6)))

mix = dialog + music + fx
for name, x in (("stem_dialog.wav", dialog), ("stem_music.wav", music), ("stem_fx.wav", fx)):
    wavfile.write(CUT / name, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))
np.save(CUT / "speech_mask.npy", speech[:: SR // 100])
(CUT / "marks.json").write_text(json.dumps(marks, indent=1))
raw = CUT / "raw.wav"
wavfile.write(raw, SR, (np.clip(mix / max(1e-9, np.max(np.abs(mix))) * 0.89, -1, 1) * 32767).astype(np.int16))
final_wav = CUT / "final.wav"
run([sys.executable, SRC / "audio" / "master.py", raw, final_wav])

# ------------------------------------------------------------------ deliver
if DRAFT:
    out = CUT / "draft.mp4"
    run(["ffmpeg", "-v", "error", "-y", "-i", picture_ov, "-i", final_wav, "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "160k",
         "-t", f"{TOTAL:.4f}", "-movflags", "+faststart", out])
    print(f"draft: {out}, {TOTAL:.2f} s")
else:
    base = ROOT / f"{EDL['out']}-{LANG}"
    base.parent.mkdir(parents=True, exist_ok=True)
    out916 = Path(f"{base}_9x16_1080x1920.mp4")
    run(["ffmpeg", "-v", "error", "-y", "-i", picture_ov, "-i", final_wav, "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
         "-t", f"{TOTAL:.4f}", "-movflags", "+faststart", out916])
    run(["ffmpeg", "-v", "error", "-y", "-i", out916, "-vf", "crop=1080:1350:0:250", "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p",
         "-c:a", "copy", "-movflags", "+faststart", f"{base}_4x5_1080x1350.mp4"])
    print(f"film: {out916} (+ 4:5), {TOTAL:.2f} s")
