"""Luna's trip vlog: the kit a sketch's picture is built from.

A sketch's picture file is a list of statements about WHO DOES WHAT WHEN; everything else (talking mouths, blinks,
heads that tilt into lines, the wind, the handheld phone, captions one at a time, the brand plate, the 4:5 version,
frames for checking) is done here, the same way in every film.

    from vlog import Vlog
    v = Vlog("earplugs", "ads/21-vlog/venuvoy-vlog-02-earplugs")           # reads work/earplugs/sound/timeline.json
    v.shot("start", bg=("bg_track_1", (700, 1500), (300, 620), 0.9), luna=(362, 1545, 0.92, 0.0))                # wide: only her
    v.shot("cut1",  bg=("bg_track_1", (700, 1500), (240, 600), 1.25), luna=(372, 1556, 1.0, -1.2), timo=(872, 935, 1.0))
    v.faces(("start", None), ("T2+0.9", "guilty"), ("L3@hikes-0.04", None), ("cut1", "defend"), ("L7-0.1", "turn"))
    v.eyes(("L7.end", "lens"), ("cut2", "narrow"), ("L9.end+0.06", "shut"))
    v.piece("phone", up="L9-0.38", down="thud+0.05", lift=110)
    v.sink("sink")                                                          # he goes down during the stretch called "sink" in the sound plan
    v.finger(up="T10-0.16", down="L11.end+0.1", wag="T10")
    v.captions(place={"T2": (600, 345)}, split={"L3": [("The planner had a switch.", None), ("It said “hikes”.", "L3@it")]})
    v.main()

TIMES are written as text and read against the soundtrack, so the picture follows any change of the comic timing:
    "L3"  the start of line L3        "L3.end"  its end        "L3@hikes"  where the word beginning "hikes" starts
    "cut1", "cut2"  the cuts          "plate", "end", "start"  and any stretch named in the sound plan ("sink", "sink.end")
    each may be followed by an offset: "L7.end+0.3", "cut2-0.1". A plain number is seconds.

PLACES: luna=(x, y, size, lean) puts the knot of her scarf at (x, y) of the 1080 x 1920 frame; timo=(x, y, size) puts
the middle of his chest there. The first sketch used luna (362, 1545, 0.92) wide, (372, 1556, 1.0) with him behind,
(345, 1490, 1.16) close; timo (872, 935, 1.0) and (890, 830, 1.16). His mouth must stay above her flying hair:
timo y < luna y - 487 * size - 90.  bg=(drawing, a point in the drawing, where that point lands in the frame, size).

Run:  python3 <film>.py --stills 1.0 "L7.end+0.2" ...   frames into work/<film>/stills/ (half size; add --full for 1080 x 1920)
      python3 <film>.py --sheet                          a contact sheet of the whole film, two frames a second
      python3 <film>.py --draft                          half size with sound
      python3 <film>.py                                  the film, its 4:5 version, and frames for checking
      add --text to set the captions in type again.
"""
import hashlib
import json
import math
import re
import subprocess
import sys
from pathlib import Path

import cv2
import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE))
import duo  # noqa: E402
from toon import FPS, H, W, Film, Picture, Talk, bend, clamp, ease, handheld, on_twos, pop, wobble  # noqa: E402

# the mouth shapes of common words, set by hand (A open, O round, C a little open, M shut); a film may add its own
SAID = {"day": "C A", "two": "O", "trip": "C M", "why": "O A", "mountain": "M O C", "planner": "M A C", "switch": "O C", "said": "C", "hikes": "A C", "you": "O",
        "hate": "A C M", "like": "A C", "idea": "A C A", "so": "O", "did": "C", "flip": "C M", "it": "C", "was": "O A", "what": "O A", "else": "C M",
        "tomorrow": "C O M O", "morning": "M O C", "run": "A C", "back": "M A C", "he": "C", "loves": "A C", "no": "O", "we": "O C", "are": "A", "i": "A",
        "my": "M A", "me": "M C", "the": "C", "a": "C", "is": "C", "this": "C", "that": "A C", "have": "A C", "how": "A O", "who": "O", "where": "O A C", "when": "O C",
        "one": "O A C", "three": "C E", "four": "C O", "five": "C A C", "yes": "C", "okay": "O C A", "oh": "O", "wow": "O A O", "please": "M E C", "plan": "M A C",
        "sports": "C O C", "game": "C A M", "match": "M A C", "seat": "C E C", "seats": "C E C", "team": "C E M", "time": "C A M", "home": "O M", "now": "A O"}
_T = re.compile(r"^\s*([A-Za-z_]\w*)(?:@([\w'’]+))?(\.end)?\s*(?:([+-])\s*([\d.]+))?\s*$")


def held(t, on, off, rise=0.16, fall=0.35):
    """0..1: up shortly before `on`, down again after `off` (a pose held through a line)."""
    return ease(t, on - rise, on + 0.10, 0.0, 1.0, "back") - ease(t, off, off + fall, 0.0, 1.0, "io")


class Vlog:
    def __init__(self, film, out, luna="wind", timo="timo_up_4", s_luna=0.64, s_timo=0.50, said=None, extra_faces=None, extra_eyes=None, seed=0, drive=False):
        """film: the name of the work folder (work/<film>). out: the delivery file without its ending, from the project
        root. luna: her look ("wind" or "calm"). timo: his base drawing, or None for a film without him."""
        self.film, self.work, self.out, self.seed = film, ROOT / "work" / film, ROOT / out, seed
        tl = json.loads((self.work / "sound" / "timeline.json").read_text())
        self.LINE = {l["id"]: l for l in tl["lines"]}
        self.order = [l["id"] for l in tl["lines"]]
        self.EV = tl["events"]
        self.END = self.EV["end"]
        self.cuts = list(self.EV.get("cuts", []))
        self.art = duo.Art(film)
        self.s_luna, self.s_timo, self.look = s_luna, s_timo, luna
        self.luna = duo.luna(self.art, s_luna, luna, extra_faces)
        self.timo = duo.timo(self.art, s_timo, timo, extra_eyes) if timo else None
        said = dict(SAID, **(said or {}))
        self.talk = {i: Talk(l["words"], ROOT / l["wav"], have=duo.LUNA_MOUTHS if l["who"] == "L" else duo.TIMO_MOUTHS, quiet=0.10 if l["who"] == "L" else 0.055, said=said,
                             open_min=0.45 if l["who"] == "L" else 0.25, drive=drive)     # he speaks flat and low: his open mouth must not wait for a shout
                     for i, l in self.LINE.items()}
        self.shots, self._faces, self._eyes = [], [(0.0, None)], [(0.0, None)]
        self._pieces, self._fingers, self._moves, self._shakes, self._bg, self._imgs = [], [], [], [], {}, {}
        self._tilt = {}
        self._blinks = {"L": None, "T": None}
        self.caps, self.plate_y, self.plate_x, self.plate_size, self._finger_img = None, 352, None, 1.0, None
        self.film_ = Film(self.work, self.out)

    # ------------------------------------------------------------ times
    def t(self, x):
        if isinstance(x, (int, float)):
            return float(x)
        m = _T.match(x)
        if not m:
            raise ValueError(f"cannot read the time {x!r}")
        name, word, end, sign, off = m.groups()
        if name in self.LINE:
            l = self.LINE[name]
            if word:
                hit = next(((a, b) for w, a, b in l["words"] if re.sub(r"[^a-z]", "", w.lower()).startswith(re.sub(r"[^a-z]", "", word.lower()))), None)
                if hit is None:
                    raise ValueError(f"line {name} has no word beginning {word!r}: {[w for w, _, _ in l['words']]}")
                base = l["t0"] + (hit[1] if end else hit[0])
            else:
                base = l["t1"] if end else l["t0"]
        elif name == "start":
            base = 0.0
        elif re.fullmatch(r"cut\d+", name):
            base = self.cuts[int(name[3:]) - 1]
        elif name in self.EV:
            v = self.EV[name]
            base = (v[1] if end else v[0]) if isinstance(v, list) else v
        else:
            raise ValueError(f"no line or event called {name!r}")
        return base + (float(off) * (1 if sign == "+" else -1) if off else 0.0)

    # ------------------------------------------------------------ what a film says
    def shot(self, start, bg, luna, timo=None, whip=None, seed=None, cap_y=None, behind=None, mid=None, front=None, grade=None):
        """A shot from `start` until the next one. behind(st, t, T), mid(st, t, T) and front(st, t, T) may draw more:
        behind the two of them; between Timo and Luna (something he holds, a thing that lands on him); in front of
        them and under the captions. grade(frame, T) may change the finished picture of this shot before the captions
        are laid (night, rain light): it gets and returns the frame as float RGB 0..1."""
        t0 = self.t(start)
        if whip is None:
            whip = any(abs(t0 - w) < 0.02 for w in self.EV.get("whips", []))
        self.shots.append({"t0": t0, "bg": bg, "luna": luna, "timo": timo, "whip": whip, "seed": seed if seed is not None else 3.3 * len(self.shots) + self.seed,
                           "cap_y": cap_y, "behind": behind, "mid": mid, "front": front, "grade": grade})

    def faces(self, *pairs):
        """Luna's face from each time on: None (sunny), "guilty", "defend" (caught), "read", "read_b" (eyes down, mouth
        shut), "down" (oh), "turn" (to him), or a face a film added."""
        self._faces = sorted([(self.t(a), b) for a, b in pairs] + ([] if any(self.t(a) <= 0.001 for a, _ in pairs) else [(0.0, None)]), key=lambda p: p[0])

    def eyes(self, *pairs):
        """Timo's eyes from each time on: None (on her), "lens" (on us), "narrow", "shut"."""
        self._eyes = sorted([(self.t(a), b) for a, b in pairs] + ([] if any(self.t(a) <= 0.001 for a, _ in pairs) else [(0.0, None)]), key=lambda p: p[0])

    def tilt(self, **deg):
        """How far her head leans into a line (degrees; positive toward Timo): tilt(L1=3.2, L3=-2.6). Others alternate."""
        self._tilt.update(deg)

    def blinks(self, luna=None, timo=None):
        """Blink times set by hand (lists of times). Without this they are scattered automatically."""
        if luna is not None:
            self._blinks["L"] = [self.t(x) for x in luna]
        if timo is not None:
            self._blinks["T"] = [self.t(x) for x in timo]

    def piece(self, name, up, down, lift=0, dx=0, shake=None, box=None, rise=0.36, base="luna_w1"):
        """Her free arm comes up from below with something in its hand. name: "phone", "hand", or a drawing of this
        film (a redraw of luna_w1 with only the arm added). lift: pixels higher than drawn (110 keeps a phone inside
        the 4:5 version). shake: a time at which it shakes once (a word she hits)."""
        if name not in self._imgs:
            self._imgs[name] = duo.phone(self.art, self.s_luna) if name == "phone" else duo.hand(self.art, self.s_luna) if name == "hand" \
                else duo.arm(self.art, name, self.s_luna, box or duo.ARM_BOX, base)
        self._pieces.append({"img": self._imgs[name], "up": self.t(up), "down": self.t(down), "lift": lift, "dx": dx, "shake": self.t(shake) if shake else None, "rise": rise})

    def finger(self, up, down, wag=None, x=925, y=2010):
        """Timo's raised finger comes up from below the frame at x, wags on the line `wag`, tips over and sinks at `down`."""
        self._finger_img = self._finger_img if self._finger_img is not None else duo.finger(self.art)
        self._fingers.append({"up": self.t(up), "down": self.t(down), "wag": wag, "x": x, "y": y})

    def sink(self, t0, t1=None):
        """Timo goes straight down out of the frame between t0 and t1 (or during the stretch of that name in the sound
        plan), and is gone until rise()."""
        a, b = (self.t(t0), self.t(t0 + ".end")) if t1 is None else (self.t(t0), self.t(t1))
        self._moves.append(("sink", a, b))

    def rise(self, t0, t1):
        """Timo comes up into the frame between t0 and t1 (he was below it before)."""
        self._moves.append(("rise", self.t(t0), self.t(t1)))

    def shake(self, at):
        """The phone is jolted (a landing)."""
        self._shakes.append(self.t(at))

    def captions(self, place=None, split=None, hold=None, off=None, on=None, text=None):
        """One caption for every line, on from its first word until the next line starts (never two at once).
        place: {"T2": (600, 345)} where a caption sits (the default is low, under her chin, in the middle).
        split: {"L3": [("The planner had a switch.", None), ("It said “hikes”.", "L3@it")]} a long line as two captions.
        hold:  {"L9": ("“Tomorrow morning.\\n", "Run.”", "L9@run")} the last words arrive when they are said.
        off / on: {"L7": "L7.end+0.06"} another moment to take a caption off / put it on.  text: {"L1": "..."} other wording."""
        place, split, hold, off, on, text = place or {}, split or {}, hold or {}, off or {}, on or {}, text or {}
        caps = []
        ids = self.order
        for k, i in enumerate(ids):
            l = self.LINE[i]
            nxt = self.LINE[ids[k + 1]]["t0"] if k + 1 < len(ids) else None
            end = l["t1"] + 0.16 if nxt is None else min(nxt, l["t1"] + 0.45)
            cut = next((c for c in self.cuts if l["t1"] - 0.05 < c <= (nxt if nxt is not None else self.END)), None)
            if cut is not None:
                end = min(end, cut + 0.04)
            if i in off:
                end = self.t(off[i])
            start = self.t(on[i]) if i in on else l["t0"]
            parts = split.get(i) or [(text.get(i, l["text"]), None)]
            if i in hold:
                a, b, when = hold[i]
                tw = self.t(when)
                caps.append([i + "a", l["who"], a, b, start, tw, place.get(i), True])
                caps.append([i + "b", l["who"], a + b, None, tw, end, place.get(i), False])
                continue
            times = [start] + [self.t(w) for _, w in parts[1:]] + [end]
            for n, (words, _) in enumerate(parts):
                caps.append([i + ("" if len(parts) == 1 else "abcdef"[n]), l["who"], words, None, times[n], times[n + 1], place.get(i), True])
        self.caps = caps

    def plate(self, y=352, x=None, size=1.0):
        """Where the brand plate lands at the end (its middle), and how big. It must not cover a face: when Timo is
        still standing at the end, either stand him low enough in the last shot that the sky above him is free, or
        make the plate smaller and put it beside his head, e.g. plate(x=350, y=300, size=0.74)."""
        self.plate_y, self.plate_x, self.plate_size = y, x, size

    # ------------------------------------------------------------ helpers a film may use in behind= / front=
    def lay(self, st, name, src, at, k, depth=0.45):
        """Put a background so that its point `src` (in the drawing's own pixels) lands on `at` in the frame, at size k."""
        if name not in self._bg:
            self._bg[name] = duo.background(self.art, name)
        img = self._bg[name].img
        h, w = img.shape[:2]
        st.put(img, at[0] + (w / 2 - src[0]) * k, at[1] + (h / 2 - src[1]) * k, scale=k, depth=depth)

    def speaking(self, T, who):
        for i, l in self.LINE.items():
            if l["who"] == who and l["t0"] - 0.05 <= T <= l["t1"] + 0.05:
                return i
        return None

    def accent(self, T, who, ground=False):
        i = self.speaking(T, who)
        return self.talk[i].accent(T - self.LINE[i]["t0"]) if i and (ground or not self.LINE[i]["ground"]) else 0.0

    def shot_at(self, T):
        cur = self.shots[0]
        for s in self.shots:
            if s["t0"] <= T + 1e-6:
                cur = s
        return cur

    # ------------------------------------------------------------ the acting
    @staticmethod
    def _value(sched, T):
        v = sched[0][1]
        for a, b in sched:
            if a <= T + 1e-6:
                v = b
        return v

    def _segments(self, sched):
        return [(a, (sched[k + 1][0] if k + 1 < len(sched) else self.END), b) for k, (a, b) in enumerate(sched)]

    def _prepare(self):
        self.shots.sort(key=lambda s: s["t0"])
        starts = [s["t0"] for s in self.shots]
        self.face_changes = [a for a, _ in self._faces[1:] if all(abs(a - c) > 0.03 for c in starts)]
        rng = np.random.default_rng(11 + int(self.seed * 7))
        if self._blinks["L"] is None:   # her blinks: every two to three and a half seconds, never on a change of face or a cut
            out, t = [], 1.0
            while t < self.END - 0.3:
                if all(not (-0.22 < t - c < 0.34) for c in [a for a, _ in self._faces] + starts):
                    out.append(round(t, 3))
                t += rng.uniform(2.0, 3.4)
            self._blinks["L"] = out
        if self._blinks["T"] is None:
            out, t = [], 1.7
            while t < self.END - 0.3:
                if all(not (-0.25 < t - c < 0.4) for c in [a for a, _ in self._eyes] + starts):
                    out.append(round(t, 3))
                t += rng.uniform(3.0, 4.6)
            self._blinks["T"] = out
        lines_l = [i for i in self.order if self.LINE[i]["who"] == "L"]
        cycle = (3.2, -2.6, 3.0, 2.2, -3.4)
        self.tilts = {i: self._tilt.get(i, cycle[k % len(cycle)]) for k, i in enumerate(lines_l)}
        # he is talked over: one of her lines starts before his has ended
        self.cut_offs = []
        for k, i in enumerate(self.order[:-1]):
            a, b = self.LINE[i], self.LINE[self.order[k + 1]]
            if a["who"] == "T" and b["who"] == "L" and b["t0"] < a["t1"] - 0.08 and not a["ground"]:
                until = next((t for t, e in self._eyes if t > b["t0"] and e is not None), b["t1"])
                self.cut_offs.append((b["t0"] + 0.04, min(until, b["t1"] + 0.6)))
        if self.caps is None:
            self.captions()
        self._typeset()

    def luna_state(self, T):
        face = self._value(self._faces, T)
        st = {"1face": face}
        if self.look == "wind":
            st["0wind"] = wind(T, 0.0)
        blinking = any(t <= T < t + 0.125 for t in self._blinks["L"])
        if face in duo.LUNA_EYE_BLINK and blinking:
            st["2eyes"] = "shut"
        elif face in duo.LUNA_OWN_BLINK and blinking:
            st["1face"] = face + "_shut"
        elif face == "read":
            since = max(a for a, b in self._faces if a <= T + 1e-6)
            if int((T - since) / 0.34) % 2:
                st["2eyes"] = "scan"             # her eyes go along the line
        i = self.speaking(T, "L")
        if i:
            m = self.talk[i].shape(T - self.LINE[i]["t0"])
            if m:
                if face in ("read", "read_b") + tuple(getattr(self, "no_teeth", ())) and m == "E":
                    m = "C"                      # no bared teeth while she reads (or on faces a film names in no_teeth: the 'defend' face's E is a clenched grin)
                # the turned head has three mouths: the round one becomes the small one (the wide one only where a word is set as "A": a shout)
                # (turn_shout = False: no line is a shout, the wide mouth becomes the middle one)
                # without shouts (turn_shout = False) the turned head keeps its small mouth: its "E" and its wide one are a clenched grin and a gape
                shout = getattr(self, "turn_shout", True)
                st["3mouth"] = ({"C": "tC", "A": "tA" if shout else "tC", "E": "tE" if shout else "tC", "O": "tC"}[m] if face == "turn" else m)
        return st

    def luna_move(self, T):
        """Her body: a pop at every change of expression and a restless little sway."""
        t = on_twos(T)
        sx = sy = 1.0
        for c in self.face_changes:
            a, b = pop(t, c, 0.032)
            sx, sy = sx * a, sy * b
        acc = self.accent(T, "L")
        return wobble(t, 3.0, 0.9) * 5, wobble(t, 2.0, 1.1) * 4 + acc * 2.5, wobble(t, 1.0, 1.3) * 0.55, sx, sy

    def luna_head(self, T):
        """Her head on her shoulders: it tilts into each line, dips on the words she hits, ducks when she is caught,
        bows to what she reads, looks down, and goes a little way ahead of a turn. (turn in degrees, shift x, shift y)"""
        t = on_twos(T)
        acc = self.accent(T, "L")
        ang = wobble(t, 5.0, 1.0) * 0.8
        for i, a in self.tilts.items():
            ang += a * held(t, self.LINE[i]["t0"], self.LINE[i]["t1"])
        ang += acc * 1.5 * (1.0 if ang >= 0 else -1.0)
        dx, dy = 0.0, acc * 7.0
        starts = [s["t0"] for s in self.shots]
        for a, b, face in self._segments(self._faces):
            if face is None or not (a - 0.4 <= T <= b + 0.5):
                continue
            at_cut = any(abs(a - c) < 0.03 for c in starts)
            rise = 0.02 if at_cut else 0.06
            on = a + 0.05 if at_cut else a
            if face in ("guilty", "defend"):
                k = held(t, on, b - 0.08, rise, 0.2) * (0.9 if face == "guilty" else 0.5)
                dy, ang = dy + k * 9, ang - k * 1.6
            elif face == "read":
                k = held(t, on, b - 0.1, 0.1, 0.22)
                ang, dy, dx = ang + k * 3.0, dy + k * 8, dx + k * 5
            elif face in ("down", "read_b"):
                k = held(t, on, b - 0.05, rise, 0.2)
                ang, dy, dx = ang + k * 5.0, dy + k * 12, dx + k * 8
            elif face == "turn":                 # the drawn turn of her head: the head starts a moment early and overshoots
                dx += (ease(t, a - 0.09, a, 0.0, 9.0, "in") if t < a else 0.0) + (ease(t, a, a + 0.25, 12.0, 0.0, "out") if a <= t < b else 0.0)
        return ang, dx, dy

    def timo_state(self, T):
        st = {"0scarf": wind(T, 4.2)} if self.look == "wind" else {}
        e = self._value(self._eyes, T)
        if e:
            st["2eyes"] = e
        if e != "shut" and any(t <= T < t + 0.21 for t in self._blinks["T"]):
            st["2eyes"] = "shut"
        i = self.speaking(T, "T")
        if i and not self.LINE[i]["ground"]:
            m = self.talk[i].shape(T - self.LINE[i]["t0"])
            if m:
                st["3mouth"] = m
        return st

    def timo_head(self, T):
        t = on_twos(T)
        acc = self.accent(T, "T")
        lean = 0.0   # he leans a little toward her while he speaks, and straightens after
        for l in self.LINE.values():
            if l["who"] == "T" and not l["ground"]:
                lean = max(lean, held(t, l["t0"], l["t1"], 0.12, 0.4))
        ang = wobble(t, 8.0, 0.8) * 0.5 - lean * 2.8 - acc * 1.3
        dy = acc * 3.5
        for a, b in self.cut_offs:               # talked over: his head goes back a little, and stays back
            if a <= T < b:
                ang += ease(t, a, a + 0.14, 0.0, 2.6, "back")
        starts = [s["t0"] for s in self.shots]
        for a, b, e in self._segments(self._eyes):
            if e == "lens" and a <= T < b and all(abs(a - c) > 0.03 for c in starts):
                ang += ease(t, a, a + 0.16, 0.0, 2.4, "back")       # his head comes round to us, once, and stays
            if e == "shut" and a <= T and any(k == "sink" and a < s0 < a + 1.5 for k, s0, _ in self._moves):
                k = ease(t, a, a + 0.3, 0.0, 1.0, "io")              # eyes shut: his head sinks a little before the rest of him does
                ang, dy = ang - k * 1.5, dy + k * 9
        return ang, 0.0, dy, lean

    def _timo_drop(self, T):
        """How far below his place he is now, how stretched, and whether he is in the picture at all."""
        t = on_twos(T)
        drop, stretch, seen = 0.0, 1.0, True
        first_rise = min((a for k, a, b in self._moves if k == "rise"), default=None)
        first_sink = min((a for k, a, b in self._moves if k == "sink"), default=None)
        if first_rise is not None and (first_sink is None or first_rise < first_sink) and T < first_rise:
            seen = False                         # he starts below the frame
        for kind, a, b in sorted(self._moves, key=lambda m: m[1]):
            if kind == "sink":
                if T >= b + 0.3:
                    seen = False
                elif T >= a - 0.25:
                    p = clamp((t - a) / (b - a))
                    drop = -ease(t, a - 0.25, a, 0.0, 60.0, "io") + 2300.0 * p ** 1.3   # up a little, then down, steadily: a lift going down
                    stretch = 1 + 0.10 * p
            elif T >= a:
                seen = True
                drop = ease(t, a, b, 2100.0, 0.0, "io") if T < b else 0.0      # evenly, no bounce past his place (a "back" ease overshot by 250 px)
        return drop, stretch, seen

    # ------------------------------------------------------------ drawing
    def draw_luna(self, st, T, place):
        x0, y0, k, lean = place
        dx, dy, turn, sx, sy = self.luna_move(T)
        ang, hx, hy = self.luna_head(T)
        s = self.s_luna
        img = bend(self.luna.get(**self.luna_state(T)), (duo.L_NECK[0] * s, duo.L_NECK[1] * s), duo.L_STILL * s, duo.L_FULL * s, ang, hx, hy)
        st.put(img, x0 + dx, y0 + dy, scale=k, turn=turn + lean, sx=sx, sy=sy, anchor=duo.L_ANCHOR, depth=1.0)
        t = on_twos(T)
        for n, p in enumerate(self._pieces):     # her other hand brings something up from below, and drops it
            if not (p["up"] - 0.05 <= T < p["down"] + 0.75):
                continue
            # it goes down as it is seen going: 0.7 s at an even speed (in 0.2 s on twos it vanished in one picture)
            rise = ease(t, p["up"], p["up"] + p["rise"], 760.0, 0.0, "back") + ease(t, p["down"], p["down"] + 0.7, 0.0, 760.0, "linear")
            hit = p["shake"]
            sh = math.sin((t - hit) * 30) * 14 * math.exp(-(t - hit) * 7) if hit is not None and t >= hit else 0.0
            st.put(p["img"], x0 + dx + p["dx"] + sh + wobble(t, 11.0 + n, 2.2) * 2.5, y0 + dy - p["lift"] + rise - abs(sh) * 0.6 + wobble(t, 12.0 + n, 1.9) * 3, scale=k,
                   turn=turn + lean + sh * 0.12 + wobble(t, 13.0 + n, 1.6) * 0.7, sx=sx, sy=sy, anchor=duo.L_ANCHOR, depth=1.0)

    def draw_timo(self, st, T, place):
        drop, stretch, seen = self._timo_drop(T)
        if not seen:
            return
        x0, y0, k = place
        t = on_twos(T)
        ang, hx, hy, lean = self.timo_head(T)
        s = self.s_timo
        img = bend(self.timo.get(**self.timo_state(T)), (duo.T_NECK[0] * s, duo.T_NECK[1] * s), duo.T_STILL * s, duo.T_FULL * s, ang, hx, hy)
        breathe = 1 + math.sin(t * 1.9) * 0.004
        st.put(img, x0 - lean * 7, y0 + drop + lean * 4, scale=k, turn=wobble(t, 7.0, 0.8) * 0.3 - lean * 1.3, sx=1 / stretch ** 0.5, sy=stretch * breathe,
               anchor=duo.T_ANCHOR, depth=0.82)

    def timo_xy(self, T, point, shot=None):
        """Where a point of Timo's drawing is in the frame now, e.g. the top of his hair (560, 175), his chest
        (614, 1514), his far shoulder (1250, 1330) (pixels of his base drawing, 1536 wide): for laying a thing ON him
        in mid= (put it with depth=0.82, his depth, and scale by the size returned). Returns (x, y, size, seen)."""
        shot = shot or self.shot_at(T)
        x0, y0, k = shot["timo"]
        drop, stretch, seen = self._timo_drop(T)
        ang, hx, hy, lean = self.timo_head(T)
        ax, ay = duo.T_ANCHOR[0] * duo.T_SIZE[0], duo.T_ANCHOR[1] * duo.T_SIZE[1]
        head = hy * k if point[1] < duo.T_FULL else 0.0
        return x0 - lean * 7 + (point[0] - ax) * self.s_timo * k, y0 + drop + lean * 4 + (point[1] - ay) * self.s_timo * k * stretch + head, self.s_timo * k, seen

    def luna_xy(self, T, point, shot=None):
        """Where a point of Luna's drawing is in the frame now, e.g. the top of her head (665, 440), her chin
        (665, 1325) (pixels of her base drawing, 1792 wide): for laying a thing on or beside her in front=
        (depth=1.0). Returns (x, y, size)."""
        shot = shot or self.shot_at(T)
        x0, y0, k, _ = shot["luna"]
        dx, dy, turn, sx, sy = self.luna_move(T)
        ang, hx, hy = self.luna_head(T)
        head = (hx * k, hy * k) if point[1] < duo.L_FULL else (0.0, 0.0)
        return x0 + dx + (point[0] - 663) * self.s_luna * k + head[0], y0 + dy + (point[1] - 1680) * self.s_luna * k + head[1], self.s_luna * k

    def cap_place(self, shot):
        if shot["cap_y"]:
            return (W / 2, shot["cap_y"])
        x0, y0, k, _ = shot["luna"]
        chin = y0 + (duo.L_CHIN - 1680) * self.s_luna * k
        return (W / 2, clamp(chin + 95, 1395, 1445))

    def _draw(self, st, t, T):
        shot = self.shot_at(T)
        self.lay(st, *shot["bg"])
        if shot["behind"]:
            shot["behind"](st, t, T)
        if shot["timo"] and self.timo is not None:
            self.draw_timo(st, T, shot["timo"])
        if shot["mid"]:
            shot["mid"](st, t, T)
        self.draw_luna(st, T, shot["luna"])
        if shot["front"]:
            shot["front"](st, t, T)
        if shot["grade"]:
            st.frame = np.clip(shot["grade"](st.frame, T), 0, 1).astype(np.float32)
        for name, who, text, hid, on, off, place, pops in self.caps:
            if on - 0.04 <= T < off - 0.04:
                x, y = place or self.cap_place(self.shot_at(on + 0.02))
                k = ease(T, on - 0.04, on + 0.09, 0.9, 1.0, "back") if pops else 1.0
                st.put(self.cap_pic[name].get(), x, y, scale=k, depth=0.0)
        t2 = on_twos(T)
        for f in self._fingers:                  # his finger comes up from below with his line, and gives up
            if f["up"] <= T < f["down"] + 0.6:
                jab = self.talk[f["wag"]].accent(T - self.LINE[f["wag"]]["t0"]) if f["wag"] and self.LINE[f["wag"]]["t0"] <= T <= self.LINE[f["wag"]]["t1"] else 0.0
                gone = ease(t2, f["down"], f["down"] + 0.5, 0.0, 1.0, "in")
                rise = ease(t2, f["up"], f["up"] + 0.32, 900.0, 0.0, "back") + gone * 950
                st.put(self._finger_img, f["x"] + gone * 40, f["y"] + rise - jab * 22, scale=1.5, turn=-5 + wobble(t2, 9.0, 2.0) * 1.2 - jab * 3 + gone * 24, anchor=(0.5, 1.0), depth=0.9)
        P = self.EV["plate"]
        if T >= P:
            k = ease(T, P, P + 0.22, 0.6, 1.0, "back")
            st.put(self.plate_pic.get(), self.plate_x or W / 2, self.plate_y, scale=k * 1.04 * self.plate_size, alpha=clamp((T - P) / 0.2), depth=0.0)   # it arrives over five pictures

    def _camera(self, shot):
        def cam(t, T):
            x, y, zoom, turn = handheld(T, 1.0, shot["seed"])
            for ts in self._shakes:
                if T >= ts:
                    y += math.sin((T - ts) * 55) * 24 * math.exp(-(T - ts) * 9)
            return x, y, zoom, turn
        return cam

    # ------------------------------------------------------------ type
    def _typeset(self):
        text_dir = self.work / "text"
        text_dir.mkdir(parents=True, exist_ok=True)
        spec = {"out": f"work/{self.film}/text", "items": [{"name": n, "kind": "cap", "who": w, "text": t, **({"hidden": h} if h else {})} for n, w, t, h, *_ in self.caps] + [{"name": "plate", "kind": "plate"}]}
        mark = hashlib.md5((json.dumps(spec, ensure_ascii=False) + (HERE / "text.mjs").read_text()).encode()).hexdigest()
        stamp = text_dir / "_spec.md5"
        if "--text" in sys.argv or not stamp.exists() or stamp.read_text() != mark or not all((text_dir / f"{c[0]}.png").exists() for c in self.caps):
            (self.work / "text_spec.json").write_text(json.dumps(spec, ensure_ascii=False, indent=1))
            subprocess.run(["node", str(HERE / "text.mjs"), str(self.work / "text_spec.json")], check=True, stdout=subprocess.DEVNULL)
            stamp.write_text(mark)
        self.cap_pic = {c[0]: Picture(text_dir / f"{c[0]}.png") for c in self.caps}
        self.plate_pic = Picture(text_dir / "plate.png")
        for name, who, text, hid, on, off, place, pops in self.caps:   # where each caption really sits: inside the safe area?
            a = self.cap_pic[name].img[..., 3]
            ys, xs = np.where(a > 0.3)
            x, y = place or self.cap_place(self.shot_at(on + 0.02))
            h, w = a.shape
            box = (x - w / 2 + xs.min(), y - h / 2 + ys.min(), x - w / 2 + xs.max(), y - h / 2 + ys.max())
            if box[0] < 40 or box[2] > 955 or box[3] > 1510 or box[1] < 190:
                print(f"  caption {name} {text!r} reaches x {box[0]:.0f}-{box[2]:.0f}, y {box[1]:.0f}-{box[3]:.0f}: outside the safe area (x 40-955: app buttons on the right; y 190-1510: the 4:5 version)")
            if off - on < 0.5 + 0.045 * len(text):
                print(f"  caption {name} {text!r} is up for {off - on:.2f} s: short for {len(text)} letters")

    # ------------------------------------------------------------ out
    def build(self):
        self._prepare()
        for k, s in enumerate(self.shots):
            t1 = self.shots[k + 1]["t0"] if k + 1 < len(self.shots) else self.END
            if s["whip"] and s["t0"] > 0:
                self.film_.whip(s["t0"])
            self.film_.shot(s["t0"], t1, self._draw, self._camera(s))
        return self.film_

    def sheet(self, path, step=0.5, cols=10):
        ims, T = [], 0.0
        while T < self.END:
            f = np.clip(self.film_.frame(T), 0, 1)
            im = cv2.resize((f[..., ::-1] * 255 + 0.5).astype(np.uint8), (216, 384), interpolation=cv2.INTER_AREA)
            cv2.putText(im, f"{T:.1f}", (6, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 0), 4)
            cv2.putText(im, f"{T:.1f}", (6, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
            ims.append(im)
            T += step
        while len(ims) % cols:
            ims.append(np.zeros_like(ims[0]))
        cv2.imwrite(str(path), np.vstack([np.hstack(ims[i:i + cols]) for i in range(0, len(ims), cols)]))

    def main(self):
        film = self.build()
        sound = self.work / "sound" / "final.wav"
        if "--stills" in sys.argv:
            out = self.work / "stills"
            out.mkdir(exist_ok=True)
            for a in sys.argv[sys.argv.index("--stills") + 1:]:
                if a.startswith("--"):
                    break
                T = self.t(float(a) if re.fullmatch(r"[\d.]+", a) else a)
                film.still(T, out / f"t{T:05.2f}.jpg", None if "--full" in sys.argv else (540, 960))
                print(f"{a} = {T:.2f} s -> {out / f't{T:05.2f}.jpg'}")
        elif "--sheet" in sys.argv:
            self.sheet(self.work / "sheet.jpg")
            print("sheet", self.work / "sheet.jpg")
        elif "--draft" in sys.argv:
            film.render(self.work / "draft.mp4", sound, crf=24, scale=(540, 960))
            print("draft", self.work / "draft.mp4")
        else:
            self.out.parent.mkdir(parents=True, exist_ok=True)
            tall, feed = str(self.out) + "-en_9x16_1080x1920.mp4", str(self.out) + "-en_4x5_1080x1350.mp4"
            film.render(tall, sound, crf=16)
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", tall, "-vf", "crop=1080:1350:0:180", "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-pix_fmt", "yuv420p",
                            "-c:a", "copy", "-movflags", "+faststart", feed], check=True)   # the feed version: rows 180 to 1530 of the tall one
            qc = self.work / "qc"
            qc.mkdir(exist_ok=True)
            for f in qc.glob("f_*.jpg"):
                f.unlink()
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", tall, "-vf", "fps=4,scale=540:960", "-q:v", "3", str(qc / "f_%03d.jpg")], check=True)
            self.sheet(qc / "sheet.jpg")
            # a fault of two frames hides between frames taken four a second: EVERY frame round each change, as one strip per moment
            moments = sorted({round(t, 2) for t in [s["t0"] for s in self.shots[1:]] + [p[k] for p in self._pieces for k in ("up", "down")] + [f[k] for f in self._fingers for k in ("up", "down")]
                              + [m[1] for m in self._moves] + [m[2] for m in self._moves] + [a for a, _ in self._faces[1:]] + [self.EV["plate"]] if 0.3 < t < self.END - 0.3})
            cap = cv2.VideoCapture(tall)
            for t in moments:
                row = []
                for n in range(int(round((t - 0.21) * FPS)), int(round((t + 0.33) * FPS))):
                    cap.set(cv2.CAP_PROP_POS_FRAMES, n)
                    ok, f = cap.read()
                    if ok:
                        row.append(cv2.resize(f, (216, 384), interpolation=cv2.INTER_AREA))
                if row:
                    cv2.imwrite(str(qc / f"around_{t:05.2f}.jpg"), np.hstack(row))
            print("film", tall)
            print("feed", feed)
            print("frames for checking (four a second), a contact sheet, and every frame round each change (around_<time>.jpg) in", qc)
        self.art.save_used()


def wind(T, seed=0.0, rate=6.5):
    """Which drawing of the wind-blown parts shows now: the base and two others, changing about four times a second,
    always through the base drawing (no jump from one far state to the other)."""
    u = T * rate * 0.62 + seed * 3.1 + 0.35 * math.sin(T * 1.7 + seed)
    return (None, "1", None, "2")[int(u) % 4]
