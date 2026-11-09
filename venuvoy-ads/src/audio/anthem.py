"""Score for the hero spot "The world is your stadium" (28 s, 120 BPM, C major): an original terrace
chant over stomps and claps, brass, strings and a crowd, arranged to the picture, plus the page's effects.

    python3 src/audio/anthem.py out.wav --dur 28 --cues cues.json [--lang en|he]

Bars (2 s): 0-1 chant over the stadium and globe · 2-3 a quieter verse under the product demo ·
4-5 a hit and a "hey!" on each feature · 6-7 the chant again and a build · 8-9 the chorus over the
fireworks · 10 breakdown under the price · 11.5 the sonic logo on the end card, then a pad.
"""
import json
import sys

from kit import *  # noqa: F401,F403

ARGS = sys.argv[1:]
OUT = ARGS[0]
opt = lambda n, d=None: ARGS[ARGS.index(n) + 1] if n in ARGS else d
DUR = float(opt("--dur", 28))
CUES = json.load(open(opt("--cues"))) if opt("--cues") else []

BEAT = 0.5
CH = {"C": [48, 52, 55, 60], "Am": [45, 48, 52, 57], "F": [41, 45, 48, 53], "G": [43, 47, 50, 55]}
ROOT = {"C": 36, "Am": 33, "F": 29, "G": 31}
BARS = ["C", "Am", "F", "G", "C", "G", "Am", "G", "F", "C", "Am", "G"]  # 0..11 (bar 11 is the half bar 22-23)
m = Mix(DUR)

# ------------------------------------------------------------------ drums
STOMP, CLAP, KICK, HAT, SHK, SN = stomp(), clap(), kick(), hat(), shaker(), snare()


def groove(t0, bars, hats=True, sixteenth=False, gain=1.0):
    for b in range(bars):
        for k in range(4):
            tb = t0 + (b * 4 + k) * BEAT
            if k in (0, 2):
                m.add(STOMP, tb, 0.85 * gain, room=0.25, hall=0.12)
                m.add(KICK, tb, 0.55 * gain)
            else:
                m.add(CLAP, tb, 0.55 * gain, pan=0.05, room=0.35, hall=0.2)
            if hats:
                for s in range(4 if sixteenth else 2):
                    m.add(HAT, tb + s * BEAT / (4 if sixteenth else 2), (0.16 if s % 2 else 0.24) * gain, pan=-0.3)


def fill(t0, n=8, span=BEAT * 2):
    for i in range(n):
        m.add(tom(150 - 9 * i, 0.4, 90 + i), t0 + i * span / n, 0.5 + 0.03 * i, pan=-0.4 + 0.8 * i / n, room=0.3)


groove(0, 2)
fill(3.0, 8, 1.0)
# verse: a lighter groove under the product, with a plucked arpeggio for momentum
for b in range(2):
    for k in range(4):
        tb = 4 + (b * 4 + k) * BEAT
        if k in (0, 2):
            m.add(KICK, tb, 0.72)
        else:
            m.add(CLAP, tb, 0.62, room=0.3, hall=0.15)
        for s in range(2):
            m.add(SHK, tb + s * BEAT / 2, 0.26, pan=0.4)
            m.add(HAT, tb + s * BEAT / 2, 0.22, pan=-0.3)
        m.add(keys([n + 12 for n in (CH["F"] if b == 0 else CH["G"])], 0.4, 1.0, 80 + b * 4 + k), tb + BEAT / 2, 0.34, pan=-0.2, room=0.3)
for i in range(16):
    ch = CH["F"] if i < 8 else CH["G"]
    m.add(pluck(ch[(0, 1, 2, 3, 2, 1, 2, 3)[i % 8]] + 24), 4.0 + i * BEAT / 2, 0.48, pan=-0.25 + 0.5 * ((i % 4) / 3), hall=0.2)
groove(8, 3, sixteenth=True)
for b, ch in enumerate(("C", "G", "Am")):
    for k in range(4):
        tb = 8.0 + b * 2.0 + k * BEAT
        m.add(keys([n + 12 for n in CH[ch]], 0.35, 1.1, 90 + b * 4 + k), tb + BEAT / 2, 0.36, pan=-0.2, room=0.3)
        for q in range(2):
            m.add(pluck(CH[ch][(k * 2 + q) % 4] + 24, 0.25), tb + q * BEAT / 2, 0.3, pan=0.25, hall=0.15)
# a hit on each of the three reasons: taiko for weight, clap, snare and a click so phone speakers hear it
for tb in (8.0, 10.0, 12.0):
    m.add(taiko(64, 1.0, int(tb * 10)), tb, 0.8, room=0.3, hall=0.25)
    m.add(clap(int(tb * 3) + 1, 0.016, 4), tb, 0.6, room=0.3, hall=0.2)
    m.add(snare(int(tb * 5)), tb, 0.45, room=0.25)
    m.add(click(0.02, 2000, 4500, int(tb * 7)), tb, 0.5)
groove(14, 1, sixteenth=True)
m.add(clap(77, 0.016, 4), 14.1, 0.55, room=0.3)
m.add(snare(78), 14.1, 0.4, room=0.25)
for i in range(16):  # the snare build into the chorus
    m.add(SN, 14.0 + i * BEAT / 4, 0.08 + 0.02 * i, room=0.2)
for i in range(16):
    m.add(SN, 15.0 + i * BEAT / 8, 0.13 + 0.017 * i, room=0.2)
m.add(riser(2.0), 14.0, 0.35)
m.add(rev_cymbal(0.9), 15.1, 0.5)
groove(16, 2, sixteenth=True, gain=1.1)
for tc in (16.0, 18.0):
    m.add(crash(2.4, int(tc)), tc, 0.6, hall=0.2)
fill(19.0, 8, 1.0)
# breakdown, then a short lift into the logo
for i in range(8):
    m.add(SN, 22.0 + i * BEAT / 4, 0.08 + 0.04 * i, room=0.25)
m.add(rev_cymbal(0.8), 22.2, 0.45)

# ------------------------------------------------------------------ harmony
for b, ch in enumerate(BARS):
    t0 = b * 2.0
    dur = 1.0 if b == 11 else 2.0
    if t0 >= 23:
        break
    m.add(strings([n + 12 for n in CH[ch]], dur + 0.3, 30 + b), t0, 0.45 if 20 <= t0 < 23 else 0.24, hall=0.35)
    if 20 <= t0 < 23:
        # the breakdown keeps some middle: keys on the off-beats and a plucked line, so phones still hear music
        m.add(pad(CH[ch] + [CH[ch][0] + 24], dur + 0.2, 2400, 40 + b), t0, 0.38, hall=0.4)
        m.add(sub(ROOT[ch], dur), t0, 0.4)
        for k in range(int(dur / BEAT)):
            m.add(keys([n + 12 for n in CH[ch]], 0.45, 1.0, 60 + b * 4 + k), t0 + k * BEAT + BEAT / 2, 0.64, pan=-0.2, room=0.35)
            m.add(pluck(CH[ch][(k * 2) % 4] + 24, 0.3), t0 + k * BEAT, 0.56, pan=0.25, hall=0.2)
            m.add(SHK, t0 + k * BEAT + BEAT / 2, 0.24, pan=0.4)
            if k % 2:
                m.add(CLAP, t0 + k * BEAT, 0.22, room=0.4, hall=0.2)  # a soft backbeat keeps the pulse audible
        continue
    # bass: quarter notes, eighths in the chorus
    step = BEAT / 2 if 16 <= t0 < 20 else BEAT
    k = 0
    while k * step < dur - 1e-6:
        m.add(bass_note(ROOT[ch] + (12 if (k % 4 == 3 and step < BEAT) else 0), min(step * 0.9, 0.3)), t0 + k * step, 0.5)
        k += 1
# verse bells: a small motif over F and G
for i, (tb, n) in enumerate([(4.0, 81), (4.5, 84), (5.0, 88), (5.75, 86), (6.0, 83), (6.5, 86), (7.0, 91), (7.5, 88)]):
    m.add(bell(n, 1.2), tb, 0.12, pan=-0.3 + 0.08 * i, hall=0.3)
m.add(choir([(0.0, 2.0, 53, "u"), (0.0, 2.0, 57, "u"), (2.0, 2.0, 55, "u"), (2.0, 2.0, 59, "u")], voices=16, crowd=0.1, seed=3), 4.0, 0.32, hall=0.5)

# ------------------------------------------------------------------ the chant (an original tune)
A = [(0.0, 0.5, 55, "o"), (0.5, 0.5, 60, "o"), (1.0, 0.25, 60, "o"), (1.25, 0.25, 62, "o"), (1.5, 0.5, 64, "o"),
     (2.0, 0.5, 64, "a"), (2.5, 0.5, 62, "o"), (3.0, 1.0, 60, "o")]
B = [(12.0, 0.5, 55, "o"), (12.5, 0.5, 60, "o"), (13.0, 0.25, 60, "o"), (13.25, 0.25, 62, "o"), (13.5, 0.5, 64, "o"),
     (14.0, 0.75, 67, "a"), (14.75, 0.25, 65, "o"), (15.0, 0.5, 64, "o"), (15.5, 0.5, 62, "o")]
CHORUS = [(16.0, 0.5, 69, "o"), (16.5, 0.5, 69, "o"), (17.0, 0.5, 72, "a"), (17.5, 0.5, 69, "o"),
          (18.0, 0.5, 67, "o"), (18.5, 0.25, 64, "o"), (18.75, 0.25, 67, "o"), (19.0, 1.0, 72, "a")]
for notes, gain, seed in ((A, 0.95, 11), (B, 0.9, 12), (CHORUS, 1.1, 13)):
    t0 = notes[0][0]
    rel = [(t - t0, d, n - 12 if n >= 69 else n, v) for t, d, n, v in notes]  # a terrace sings low
    m.add(choir(rel, voices=34, crowd=0.75, octave_up=0.3, seed=seed), t0, gain, hall=0.55)
# brass doubles the chorus an octave down, with stabs on the big moments
for t, d, n, v in CHORUS:
    m.add(brass([n - 12, n - 24], d + 0.05, 1.1, int(t * 7)), t, 0.32, pan=-0.15, hall=0.25)
for tb, ch, d, g in ((0.0, "C", 0.7, 0.55), (2.0, "Am", 0.45, 0.35), (8.0, "C", 0.3, 0.45), (10.0, "G", 0.3, 0.45), (12.0, "Am", 0.3, 0.45), (14.1, "G", 0.3, 0.4), (16.0, "F", 0.6, 0.5)):
    m.add(brass([n + 12 for n in CH[ch]], d, 1.2, int(tb * 11)), tb, g, pan=0.15, hall=0.3)
for tb in (8.0, 10.0):
    m.add(hey(int(tb * 3)), tb + 0.02, 0.55, hall=0.4)
m.add(timpani(36, 1.8), 0.0, 0.55, hall=0.3)
m.add(timpani(41, 1.5), 16.0, 0.5, hall=0.3)

# ------------------------------------------------------------------ the end card: logo, then a pad
m.add(sonic_logo(), 23.0, 1.0, hall=0.3)
m.add(strings([60, 64, 67, 72], DUR - 23.0, 99), 23.05, 0.8, hall=0.4)
for k in range(int((DUR - 24.0) / 1.0)):
    m.add(keys([60, 64, 67, 72], 0.9, 0.8, 120 + k), 24.0 + k * 1.0, 0.3, pan=-0.15, room=0.4)
m.add(pad([48, 55, 60, 64], DUR - 23.0, 2200, 98), 23.1, 1.0, hall=0.4)
m.add(sub(36, DUR - 23.2), 23.2, 0.35)
for k, n in enumerate((79, 76, 84, 79, 72, 76, 84)):
    if 24.2 + k * 0.5 < DUR - 0.6:
        m.add(bell(n), 24.2 + k * 0.5, 0.34, pan=(-0.3, 0.3)[k % 2], hall=0.35)

# ------------------------------------------------------------------ effects from the page
FX = {
    "hit": lambda c: [(impact(), 0, 0.55, dict(hall=0.25))],
    "roar": lambda c: [(roar(c.get("dur", 3)), 0, 0.42, {})],
    "cheer": lambda c: [(cheer(c.get("dur", 3.5)), 0, 0.62, {})],
    "slam": lambda c: [(whoosh(0.3, False), -0.12, 0.25, {}), (boom(0.45), 0.02, 0.22, {})],
    "whip": lambda c: [(whip(), 0, 0.5, {})],
    "jet": lambda c: [(jet_flyby(c.get("dur", 1.8)), 0, 0.42, dict(hall=0.15))],
    "whoosh": lambda c: [(whoosh(0.45), 0, 0.3, {})],
    "typing": lambda c: [(typing(c.get("dur", 1.2)), 0, 0.32, {})],
    "press": lambda c: [(thump(), 0, 0.55, {})],
    "swish": lambda c: [(whoosh(0.28, True, 60 + k), k * 0.09, 0.22, {}) for k in range(3)],
    "ding": lambda c: [(ding((88, 95)), 0, 0.42, dict(hall=0.2))],
    "stamp": lambda c: [(stamp(), 0.03, 1.0, dict(room=0.2)), (click(0.02, 1500, 4000), 0.03, 0.5, {})],
    "rip": lambda c: [(rip(), 0, 0.5, {})],
    "error": lambda c: [(error_bonk(), 0, 0.42, {})],
    "buzz": lambda c: [(buzz(), 0, 0.55, {})],
    "notif": lambda c: [(notif(), 0, 0.45, {})],
    "whistle": lambda c: [(whistle(0.55), 0, 0.4, dict(hall=0.3))],
    "swarm": lambda c: [(whoosh(0.5, True, 70 + k), k * 0.16, 0.16, dict(pan=-0.6 + 0.17 * k)) for k in range(8)],
    "thud": lambda c: [(boom(0.6), 0, 0.42, {}), (click(0.02, 900, 3500), 0, 0.5, {})],
    "kickball": lambda c: [(ball_kick(), 0, 0.6, dict(room=0.3))],
    "dribble": lambda c: [(dribble(), 0, 0.55, dict(room=0.3))],
    "pock": lambda c: [(tennis_pock(), 0, 0.5, dict(room=0.3))],
    "golf": lambda c: [(golf_swing(), -0.25, 0.45, {})],
    "engine": lambda c: [(engine_pass(1.0), -0.3, 0.42, {})],
    "rugby": lambda c: [(rugby_thud(), 0, 0.6, dict(room=0.3))],
    "fireworks": lambda c: [(firework(200 + k), t - c["t"], 0.38, dict(pan=(-0.6, 0.5, -0.2, 0.7, -0.5, 0.3, 0.0)[k % 7], hall=0.3)) for k, t in enumerate(c.get("times", []))],
    "logo": lambda c: [],  # the score already places the sonic logo at 23 s
}
for c in CUES:
    for sig, dt, g, kw in FX.get(c["type"], lambda c: [])(c):
        pan = kw.pop("pan", 0.0)
        m.add(sig, c["t"] + dt, g, pan=pan, **kw)

m.render(OUT, fade=0.8)
