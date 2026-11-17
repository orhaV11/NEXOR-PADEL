"""A small cut-out animation engine: drawn pieces moved by hand-set keys, no video model.

A film is a Python file that builds a Film and adds shots. A shot is a function draw(stage, t) that places pictures
on the stage for the time t (seconds from the start of the shot). The engine supplies what an animator needs:

  Sprite        a drawing keyed out of its flat background, with swap patches on it (mouth shapes, a blink)
  talk()        which mouth shape a character shows at each moment, from the words of the line and its loudness
  handheld()    the small drift, sway and breathing of a phone held at arm's length
  pop(), ease() snap between poses with a little squash and stretch
  on_twos()     hold a value for two frames (12 pictures a second), the way drawn animation moves
  Stage.put()   place a picture: position, scale, turn, squash, opacity, about an anchor point

Pictures are kept as premultiplied float RGBA. The frame is 1080 x 1920 at 24 pictures a second.
"""
import json
import math
import subprocess
from pathlib import Path

import cv2
import numpy as np

W, H, FPS = 1080, 1920, 24


# ---------------------------------------------------------------- pictures

def read(path):
    """A picture file as float RGB(A) 0..1, in RGB order."""
    img = cv2.imread(str(path), cv2.IMREAD_UNCHANGED)
    if img is None:
        raise FileNotFoundError(path)
    if img.ndim == 2:
        img = cv2.cvtColor(img, cv2.COLOR_GRAY2BGR)
    if img.dtype == np.uint16:
        img = (img / 257).astype(np.uint8)
    img = img.astype(np.float32) / 255
    if img.shape[2] == 4:
        return np.dstack([img[..., 2], img[..., 1], img[..., 0], img[..., 3]])
    return img[..., ::-1].copy()


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def key_out(rgb, colour=(1.0, 0.0, 1.0), lo=0.10, hi=0.36, shrink=1):
    """Cut a drawing out of a flat magenta background. Returns straight RGBA.
    Keyness is how much more magenta than anything else a pixel is; the dark outline of a drawn figure gives a clean
    edge. The magenta that bleeds into edge pixels is taken out, and the matte is pulled in by a pixel."""
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    k = np.clip(np.minimum(r, b) - g, 0, 1)
    alpha = 1 - smooth(lo, hi, k)
    if shrink:
        alpha = cv2.erode(alpha, np.ones((3, 3), np.uint8), iterations=shrink)
        alpha = cv2.GaussianBlur(alpha, (0, 0), 0.7)
    spill = np.clip(np.minimum(r, b) - g, 0, 1)
    out = rgb.copy()
    out[..., 0] = r - spill
    out[..., 2] = b - spill
    return np.dstack([out, alpha])


def premul(rgba):
    out = rgba.copy()
    out[..., :3] *= out[..., 3:4]
    return out


def trim(rgba, pad=6):
    """Crop a straight RGBA picture to its opaque part. Returns the crop and its offset (x, y)."""
    ys, xs = np.where(rgba[..., 3] > 0.02)
    x0, x1, y0, y1 = max(xs.min() - pad, 0), min(xs.max() + pad, rgba.shape[1]), max(ys.min() - pad, 0), min(ys.max() + pad, rgba.shape[0])
    return rgba[y0:y1, x0:x1], (int(x0), int(y0))


def align(base_rgb, other_rgb, box=None):
    """Shift `other` by whole pixels so that it sits on `base` (a redraw by the picture model can land a pixel or two
    off). Measured on the part of the picture outside `box` (the part that was asked to change)."""
    a = cv2.cvtColor((base_rgb[..., :3] * 255).astype(np.uint8), cv2.COLOR_RGB2GRAY).astype(np.float32)
    b = cv2.cvtColor((other_rgb[..., :3] * 255).astype(np.uint8), cv2.COLOR_RGB2GRAY).astype(np.float32)
    if box:
        x0, y0, x1, y1 = box
        m = np.ones_like(a)
        m[y0:y1, x0:x1] = 0
        a, b = a * m, b * m
    (dx, dy), _ = cv2.phaseCorrelate(a, b)
    dx, dy = int(round(dx)), int(round(dy))
    if dx or dy:
        other_rgb = np.roll(other_rgb, (-dy, -dx), axis=(0, 1))
    return other_rgb, (dx, dy)


def patch_mask(base, other, box, thr=0.07, grow=7, feather=5):
    """Where `other` differs from `base` inside `box` (x0, y0, x1, y1): a soft mask for laying the changed part over
    the base, so that everything else stays the very same pixels."""
    d = np.abs(base[..., :3] - other[..., :3]).max(axis=2)
    m = np.zeros(d.shape, np.float32)
    x0, y0, x1, y1 = box
    m[y0:y1, x0:x1] = (d[y0:y1, x0:x1] > thr).astype(np.float32)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((grow * 2 + 1, grow * 2 + 1), np.uint8))
    m = cv2.dilate(m, np.ones((grow, grow), np.uint8))
    m = cv2.GaussianBlur(m, (0, 0), feather)
    edge = np.zeros_like(m)  # never reach the border of the box: fade to nothing there
    edge[y0 + feather:y1 - feather, x0 + feather:x1 - feather] = 1
    edge = cv2.GaussianBlur(edge, (0, 0), feather)
    return np.clip(m * 1.6, 0, 1) * edge


class Sprite:
    """A drawing with swap patches. `base` is a picture on flat magenta. Patches are other drawings of the SAME
    picture in which one thing changed (the mouth, the eyes, the whole face); only the given box is taken from them,
    so the rest of the figure is the very same pixels in every frame.

        luna = Sprite("art/luna.png", scale=0.62)
        luna.add("3mouth", "A", "art/luna_mA.png", box=(x0, y0, x1, y1))     # boxes in the pixels of the base drawing
        luna.add("2eyes", "shut", "art/luna_blink.png", box=(...))
        img = luna.get(**{"3mouth": "A", "2eyes": "shut"})

    Groups are laid in the order of their names ("1face", "2eyes", "3mouth"). `pad_bottom` repeats the last row of the
    drawing downward (legs that must reach below the frame)."""

    def __init__(self, base, scale=1.0, pad_bottom=0, skin=None, pad="legs"):
        """skin: a point (x, y) of plain skin in the drawing. Redraws come back a shade off; every patch is brought
        to the base drawing's skin colour so that no faint box shows round a mouth or the eyes.
        pad: how the drawing runs on below its last row for pad_bottom pixels: "legs" (flat trouser colour, the side
        outlines continued, a seam between the legs) or "repeat" (every line of the last row runs straight on)."""
        rgb = read(base)[..., :3]
        self.src_rgb = rgb
        self.skin = np.median(rgb[skin[1] - 5:skin[1] + 6, skin[0] - 5:skin[0] + 6].reshape(-1, 3), axis=0) if skin else None
        rgba = key_out(rgb)
        if pad_bottom and pad == "repeat":
            rgba = np.concatenate([rgba, np.repeat(rgba[-1:], pad_bottom, axis=0)], axis=0)
        elif pad_bottom:   # legs that run on below the drawing: flat trouser colour, the side outlines continued, a seam between the legs
            last = rgba[-1]
            solid = last[:, 3] > 0.5
            xs = np.where(solid)[0]
            dark = solid & (last[:, :3].max(axis=1) < 0.25)
            fill = np.median(last[solid & ~dark][:, :3], axis=0)
            row = np.zeros_like(last)
            row[solid, :3], row[solid, 3] = fill, 1.0
            row[dark, :3] = last[dark, :3]
            pad = np.repeat(row[None], pad_bottom, axis=0)
            mid = (xs.min() + xs.max()) // 2
            pad[40:, mid - 5:mid + 5, :3] = last[dark][:, :3].mean(axis=0) if dark.any() else 0.05
            rgba = np.concatenate([rgba, pad], axis=0)
        self.scale = scale
        self.size = (rgba.shape[1], rgba.shape[0])  # of the drawing, before scaling
        self.base = premul(self._small(rgba))
        self.patches, self.report = {}, {}

    def _small(self, img):
        return cv2.resize(img, None, fx=self.scale, fy=self.scale, interpolation=cv2.INTER_AREA) if self.scale != 1 else img

    def add(self, group, name, path, box, mode="box", thr=0.07, grow=7, feather=6):
        """mode "box": the whole box is taken from the other drawing, with a soft edge (right for a mouth or the eyes on
        flat skin: whatever the face showed there before is replaced). mode "diff": only what differs is taken."""
        other = read(path)[..., :3]
        if other.shape[:2] != self.src_rgb.shape[:2]:
            other = cv2.resize(other, (self.src_rgb.shape[1], self.src_rgb.shape[0]), interpolation=cv2.INTER_AREA)
        other, shift = align(self.src_rgb, other, box)
        x0, y0, x1, y1 = box
        if self.skin is not None:   # bring the redraw's skin to the base's skin
            part = other[y0:y1, x0:x1]
            near = np.abs(part - self.skin).max(axis=2) < 0.075
            if near.sum() > 200:
                theirs = np.median(part[near], axis=0)
                w = np.clip(1 - np.abs(part - theirs).max(axis=2) / 0.09, 0, 1)[..., None]
                other = other.copy()
                other[y0:y1, x0:x1] = np.clip(part + (self.skin - theirs) * w, 0, 1)
        if mode == "box":
            m = np.zeros(self.src_rgb.shape[:2], np.float32)
            m[y0 + feather:y1 - feather, x0 + feather:x1 - feather] = 1
            m = cv2.GaussianBlur(m, (0, 0), feather * 0.6)
        else:
            m = patch_mask(self.src_rgb, other, box, thr, grow, feather)
        d = np.abs(self.src_rgb - other).max(axis=2)
        d[y0:y1, x0:x1] = 0  # how much changed OUTSIDE the box tells whether the redraw kept the rest of the picture
        self.report[f"{group}:{name}"] = {"shift": shift, "changed_outside": round(float((d > 0.12).mean()), 4)}
        # keep only the box, at the size the sprite is shown
        pad = 8
        bx0, by0, bx1, by1 = max(0, x0 - pad), max(0, y0 - pad), min(m.shape[1], x1 + pad), min(m.shape[0], y1 + pad)
        k = self.scale
        sx0, sy0 = int(round(bx0 * k)), int(round(by0 * k))
        size = (int(round(bx1 * k)) - sx0, int(round(by1 * k)) - sy0)
        mask = cv2.resize(m[by0:by1, bx0:bx1], size, interpolation=cv2.INTER_AREA)[..., None]
        piece = premul(cv2.resize(key_out(other)[by0:by1, bx0:bx1], size, interpolation=cv2.INTER_AREA))
        self.patches.setdefault(group, {})[name] = (sx0, sy0, mask, piece)

    def get(self, **state):
        img = self.base.copy()
        for g in sorted(state):
            n = state[g]
            if n and n in self.patches.get(g, {}):
                x, y, mask, piece = self.patches[g][n]
                h, w = piece.shape[:2]
                img[y:y + h, x:x + w] = img[y:y + h, x:x + w] * (1 - mask) + piece * mask
        return img


class Picture:
    """A plain picture (a background, a caption, a plate) ready for the stage."""

    def __init__(self, path, scale=1.0):
        rgb = read(path)
        if rgb.shape[2] == 3:
            rgb = np.dstack([rgb, np.ones(rgb.shape[:2], np.float32)])
        if scale != 1:
            rgb = cv2.resize(rgb, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
        self.img = premul(rgb)

    def get(self, **_):
        return self.img


_grids = {}


def bend(img, pivot, y_still, y_full, angle=0.0, dx=0.0, dy=0.0):
    """Move the upper part of a picture on its own: everything above y_full turns by `angle` degrees about `pivot`
    and shifts by (dx, dy); everything below y_still stays; between them the picture bends smoothly (a neck inside a
    scarf). This is how a head tilts and nods on a body drawn in one piece."""
    if abs(angle) < 0.02 and abs(dx) < 0.2 and abs(dy) < 0.2:
        return img
    h, w = img.shape[:2]
    if (h, w) not in _grids:
        ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
        _grids[(h, w)] = (xs, ys)
    xs, ys = _grids[(h, w)]
    wgt = smooth(y_still, y_full, ys)   # 0 below, 1 above (y grows downward)
    a = np.radians(angle) * wgt
    ca, sa = np.cos(a), np.sin(a)
    px, py = pivot
    rx, ry = xs - px - dx * wgt, ys - py - dy * wgt
    map_x = (px + ca * rx + sa * ry).astype(np.float32)   # where each pixel comes from (the turn undone)
    map_y = (py - sa * rx + ca * ry).astype(np.float32)
    return cv2.remap(img, map_x, map_y, cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)


def bend_x(img, pivot, x_still, x_full, angle=0.0, dx=0.0, dy=0.0):
    """The same for a figure that lies down: everything beyond x_full turns about `pivot` (a head lifting off the
    ground), everything on the other side of x_still stays."""
    out = bend(np.ascontiguousarray(img.transpose(1, 0, 2)), (pivot[1], pivot[0]), x_still, x_full, -angle, dy, dx)
    return out if out is img else np.ascontiguousarray(out.transpose(1, 0, 2))


# ---------------------------------------------------------------- the stage

class Stage:
    def __init__(self):
        self.frame = np.zeros((H, W, 3), np.float32)
        self.cam = (0.0, 0.0, 1.0, 0.0)  # shift x, shift y, zoom, turn (degrees) of the whole picture about its centre

    def clear(self, colour=(0, 0, 0)):
        self.frame[:] = colour

    def put(self, img, x, y, scale=1.0, turn=0.0, sx=1.0, sy=1.0, anchor=(0.5, 0.5), alpha=1.0, depth=1.0):
        """Lay a premultiplied RGBA picture with its anchor point at (x, y) of the frame.
        scale, then squash (sx, sy), then turn (degrees, clockwise), all about the anchor. `depth` says how much of the
        camera's movement this layer takes: 1 moves with the camera, less than 1 lags behind (far things), 0 is glued
        to the glass (captions)."""
        h, w = img.shape[:2]
        ax, ay = anchor[0] * w, anchor[1] * h
        cx, cy, zoom, cturn = self.cam
        zoom_l = 1 + (zoom - 1) * depth
        a = math.radians(turn + cturn * depth)
        ca, sa = math.cos(a), math.sin(a)
        s_x, s_y = scale * sx * zoom_l, scale * sy * zoom_l
        # the anchor's place after the camera has moved the layer about the centre of the frame
        px, py = x - W / 2, y - H / 2
        ct, st = math.cos(math.radians(cturn * depth)), math.sin(math.radians(cturn * depth))
        qx = W / 2 + (px * ct - py * st) * zoom_l + cx * depth
        qy = H / 2 + (px * st + py * ct) * zoom_l + cy * depth
        M = np.array([[ca * s_x, -sa * s_y, qx - (ca * s_x * ax - sa * s_y * ay)],
                      [sa * s_x, ca * s_y, qy - (sa * s_x * ax + ca * s_y * ay)]], np.float32)
        warped = cv2.warpAffine(img, M, (W, H), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_CONSTANT, borderValue=0)
        if alpha != 1:
            warped = warped * alpha
        self.frame *= 1 - warped[..., 3:4]
        self.frame += warped[..., :3]


# ---------------------------------------------------------------- timing helpers

def clamp(x, a=0.0, b=1.0):
    return max(a, min(b, x))


def ease(t, t0, t1, a=0.0, b=1.0, kind="out"):
    """A value going from a to b between t0 and t1. kind: "out" arrives softly, "in" leaves softly, "io" both,
    "back" overshoots a little and settles (for things that land)."""
    p = clamp((t - t0) / max(1e-6, t1 - t0))
    if kind == "out":
        p = 1 - (1 - p) ** 3
    elif kind == "in":
        p = p ** 3
    elif kind == "io":
        p = p * p * (3 - 2 * p)
    elif kind == "back":
        c = 1.9
        p = 1 + (c + 1) * (p - 1) ** 3 + c * (p - 1) ** 2
    return a + (b - a) * p


def pop(t, t0, amount=0.05, dur=0.21):
    """Squash and stretch for a change of pose at t0: returns (sx, sy). The figure stretches up for two pictures,
    squashes for two and settles."""
    p = (t - t0) / dur
    if p < 0 or p > 1:
        return 1.0, 1.0
    k = math.sin(p * math.pi * 2) * (1 - p) * amount * 1.6
    return 1 - k * 0.6, 1 + k


def on_twos(t):
    """The time held for two frames: drawn animation moves 12 times a second."""
    return math.floor(t * FPS / 2) * 2 / FPS


def wobble(t, seed=0.0, speed=1.0):
    """A smooth wandering value between about -1 and 1 (three sines that never line up)."""
    return (math.sin(t * 1.31 * speed + seed * 12.9) + 0.6 * math.sin(t * 2.87 * speed + seed * 4.1 + 1.3) + 0.35 * math.sin(t * 5.43 * speed + seed * 7.7 + 0.4)) / 1.95


def handheld(t, amount=1.0, seed=0.0):
    """A phone held at arm's length: (shift x, shift y, zoom, turn). Slow drift, a faster small tremor, breathing."""
    x = (wobble(t, seed, 0.9) * 16 + wobble(t, seed + 3, 4.6) * 2.5) * amount
    y = (wobble(t, seed + 1, 0.8) * 20 + wobble(t, seed + 4, 5.1) * 3.0) * amount
    zoom = 1 + (wobble(t, seed + 2, 0.5) * 0.012) * amount
    turn = (wobble(t, seed + 5, 0.7) * 0.9 + wobble(t, seed + 6, 3.9) * 0.15) * amount
    return x, y, zoom, turn


# ---------------------------------------------------------------- talking

CLOSED, TEETH = set("mbp"), set("fv")
PAIRS = {"ee": "E", "ea": "E", "ie": "E", "oo": "O", "ou": "O", "ow": "O", "oa": "O", "ai": "A", "ay": "A", "au": "A", "aw": "A"}


def word_shapes(word):
    """The mouth shapes of a word, from its spelling: A open, E wide, O round, C a little open, M shut, F teeth on lip.
    English spelling is a rough guide, so the rules err toward the small open mouth: a short e or i is C, not a grin."""
    w = "".join(c for c in word.lower() if c.isalpha())
    out, i = [], 0
    while i < len(w):
        c, two = w[i], w[i:i + 2]
        if two in PAIRS:
            s, i = PAIRS[two], i + 1
        elif c in CLOSED:
            s = "M"
        elif c in TEETH:
            s = "F"
        elif c == "a":
            s = "A"
        elif c in "ow":
            s = "O"
        elif c == "u":
            s = "A"
        elif c == "i" and i + 2 < len(w) and w[i + 2] == "e" and w[i + 1] not in "aeiou":
            s = "A"                       # the long i of "like", "hike"
        elif c == "y" and i == len(w) - 1 and len(w) > 2:
            s = "E"
        elif c == "e" and i == len(w) - 1 and len(w) > 2:
            i += 1
            continue                      # a silent last e
        else:
            s = "C"
        if not out or out[-1] != s:
            out.append(s)
        i += 1
    keep = [s for k, s in enumerate(out) if s != "C" or k == 0 or k == len(out) - 1 or out[k - 1] == "C"]
    return keep or ["C"]


def loudness(wav_path, hop=1 / FPS):
    import soundfile as sf
    x, sr = sf.read(str(wav_path))
    if x.ndim > 1:
        x = x.mean(axis=1)
    n = int(sr * hop)
    env = np.array([np.sqrt(np.mean(x[i:i + n] ** 2)) for i in range(0, max(1, len(x) - n), n)])
    return env / (np.percentile(env, 95) + 1e-9)


class Talk:
    """Mouth shapes for a line: Talk(words, wav).shape(t) with t in seconds from the start of the line.
    words: [[word, start, end], ...] inside the line. The shapes of each word are spread over its length; where the
    voice is nearly silent the mouth is shut. The line is worked out once, twelve steps a second, and then cleaned the
    way an animator would: a wide or toothy shape that would flash for one step becomes the small open mouth, and a
    one-step blip between two equal shapes is dropped. `have` lists the shapes this character has drawings for."""

    FALLBACK = {"F": ["C", "E", "M"], "C": ["E", "A", "M"], "E": ["C", "A"], "O": ["C", "A"], "A": ["O", "E", "C"], "M": []}

    def __init__(self, words, wav, have=("A", "E", "O", "C", "M"), lead=1 / FPS, quiet=0.10, wide_min=2, teeth_min=3, said=None):
        """said: {"word": "A C"}: the shapes of a word set by hand (the words a joke rests on)."""
        self.env = loudness(wav)
        self.lead, self.quiet, self.have = lead, quiet, set(have)
        said = said or {}
        events = []
        for w, a, b in words:
            key = "".join(c for c in w.lower() if c.isalpha())
            shapes = said[key].split() if key in said else word_shapes(w)
            step = (b - a) / len(shapes)
            for i, s in enumerate(shapes):
                events.append((a + i * step, a + (i + 1) * step, s))
        self.end = max((b for _, _, b in words), default=0) + 0.12
        seq = []
        for k in range(int(self.end * 12) + 2):
            t = k / 12 + lead
            if t > self.end or self.level(t) < quiet:
                seq.append(None)
                continue
            s = next((s for a, b, s in events if a <= t < b), "C")
            if s == "A" and self.level(t) < 0.45:
                s = "C"
            if s not in self.have:
                s = next((f for f in self.FALLBACK.get(s, []) if f in self.have), None)
            seq.append(None if s == "M" else s)
        # runs: (shape, first step, length)
        def runs(q):
            out, i = [], 0
            while i < len(q):
                j = i
                while j < len(q) and q[j] == q[i]:
                    j += 1
                out.append((q[i], i, j - i))
                i = j
            return out
        for shape, need in (("E", teeth_min), ("A", wide_min)):
            for sh, i, n in runs(seq):
                if sh == shape and n < need:
                    seq[i:i + n] = ["C"] * n
        for sh, i, n in runs(seq):   # a one-step blip between two equal neighbours
            if n == 1 and 0 < i < len(seq) - 1 and seq[i - 1] == seq[i + 1] and seq[i - 1] is not None:
                seq[i] = seq[i - 1]
        self.seq = seq

    def level(self, t):
        i = int(t * FPS)
        return float(self.env[i]) if 0 <= i < len(self.env) else 0.0

    def shape(self, t):
        k = int(math.floor(t * 12 + 1e-6))
        return self.seq[k] if 0 <= k < len(self.seq) else None

    def accent(self, t):
        """0..1: how hard the voice hits just now (for a small nod of the head on the stressed words)."""
        return clamp(self.level(on_twos(t)) - 0.35, 0, 1)


# ---------------------------------------------------------------- the film

class Film:
    def __init__(self, work, out):
        self.work, self.out = Path(work), Path(out)
        self.shots, self.whips = [], []

    def whip(self, t, dur=0.09, far=300, blur=340):
        """A quick sideways whip of the camera across the cut at t: the last two pictures before it smear away to the
        left, the first two after it arrive from the right. It tells the eye that this is a new take, not a glitch."""
        self.whips.append((t, dur, far, blur))

    def shot(self, t0, t1, draw, camera=None):
        """draw(stage, t, T): t counts from the start of the shot, T from the start of the film."""
        self.shots.append((t0, t1, draw, camera))

    @property
    def length(self):
        return max(t1 for _, t1, _, _ in self.shots)

    def frame(self, T):
        st = Stage()
        for t0, t1, draw, camera in self.shots:
            if t0 <= T < t1:
                if camera:
                    st.cam = camera(T - t0, T)
                draw(st, T - t0, T)
        for t, dur, far, blur in self.whips:
            d = T - t
            if -dur <= d < dur:
                k = 1 - abs(d) / dur
                M = np.float32([[1, 0, -far * k if d < 0 else far * k], [0, 1, 0]])
                st.frame = cv2.blur(cv2.warpAffine(st.frame, M, (W, H), borderMode=cv2.BORDER_REFLECT), (int(30 + blur * k) | 1, 1))
        return st.frame

    def still(self, T, path, size=None):
        f = np.clip(self.frame(T), 0, 1)
        img = (f[..., ::-1] * 255 + 0.5).astype(np.uint8)
        if size:
            img = cv2.resize(img, size, interpolation=cv2.INTER_AREA)
        cv2.imwrite(str(path), img)

    def render(self, path, audio=None, crf=16, t0=0.0, t1=None, scale=None):
        n0, n1 = int(round(t0 * FPS)), int(round((t1 or self.length) * FPS))
        w, h = (W, H) if not scale else scale
        cmd = ["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-"]
        if audio:
            cmd += ["-i", str(audio)]
        cmd += ["-vf", f"scale={w}:{h}:flags=lanczos" if scale else "null", "-c:v", "libx264", "-preset", "medium", "-crf", str(crf), "-pix_fmt", "yuv420p",
                "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"]
        if audio:
            cmd += ["-c:a", "aac", "-b:a", "192k", "-shortest"]
        cmd += ["-movflags", "+faststart", str(path)]
        p = subprocess.Popen(cmd, stdin=subprocess.PIPE)
        for n in range(n0, n1):
            f = np.clip(self.frame(n / FPS), 0, 1)
            p.stdin.write((f * 255 + 0.5).astype(np.uint8).tobytes())
        p.stdin.close()
        p.wait()
        return path


def save_json(path, obj):
    Path(path).write_text(json.dumps(obj, indent=1, ensure_ascii=False))
