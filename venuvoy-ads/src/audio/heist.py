"""Score for "Operation: Away Day", the AI-shot comedy: a heist-movie groove in D minor (upright bass riff,
brushed swing drums, finger snaps, vibes and muted brass) under the plan, a hard tape-stop when the
kick-off moves, an over-the-top choir under the slow-motion collapse, near silence for Grandma, then the
full band for "Done. Whole trip.", the stadium and the sonic logo on the end card.

All sync points come from the cut, in a JSON file:
    python3 src/audio/heist.py out.wav --marks marks.json
marks: dur, start (groove in), cards [t...] (name-card stabs), pop (hype man), stop (phone buzz),
       slowmo [a, b], grandma, done (the groove returns), insert, stadium, end (end card)
"""
import json
import sys

from kit import *  # noqa: F401,F403

ARGS = sys.argv[1:]
OUT = ARGS[0]
M = json.load(open(ARGS[ARGS.index("--marks") + 1]))
DUR = float(M["dur"])
m = Mix(DUR)
BPM = 104
BEAT = 60 / BPM
SW = 2 / 3  # swing: the off-beat eighth lands two thirds of the way through the beat
K, SN, HT, OH = kick(f1=120, f0=46, d=0.4, click=0.2), snare(), hat(), hat(True)


def snap(seed=0):
    """A finger snap: a short, bright noise crack with a tiny body."""
    t = tt(0.09)
    n = bp(R(400 + seed).standard_normal(len(t)), 1800, 5200, 3)
    return n * np.exp(-t / 0.012) * 0.9 + np.sin(2 * np.pi * 1350 * t) * np.exp(-t / 0.006) * 0.25


def brush(seed=0):
    """A brushed snare: a soft swish into a light snare tap."""
    t = tt(0.22)
    sw = bp(R(500 + seed).standard_normal(len(t)), 2500, 9000, 2) * np.minimum(t / 0.03, 1) * np.exp(-t / 0.07) * 0.5
    return sw + snare(600 + seed)[: len(t)] * 0.35


def upright(m_, d=0.32):
    """A plucked upright bass note: the synth bass with a softer, woodier envelope."""
    y = bass_note(m_, d)
    t = tt(len(y) / SR)
    return lp(y * (0.6 + 0.4 * np.exp(-t / 0.08)), 900)


# the riff, two bars of swung eighths (None = rest)
RIFF = [38, None, 38, 41, None, 38, 43, 44, 45, None, 45, 48, None, 45, 43, 41]
DM9 = [62, 65, 69, 72, 76]


def eighth(bar_t, i):
    """Time of the i-th swung eighth in a bar that starts at bar_t."""
    beat, off = divmod(i, 2)
    return bar_t + (beat + (SW if off else 0)) * BEAT


def groove(t0, t1, full=False, seed=0):
    """The heist groove from t0 to t1 (bars from t0). full adds kick, open hats and brass."""
    bar = 4 * BEAT
    nb = int(np.ceil((t1 - t0) / bar))
    for b in range(nb):
        bt = t0 + b * bar
        for i in range(8):
            ti = eighth(bt, i)
            if ti >= t1:
                break
            note = RIFF[(b % 2) * 8 + i]
            if note is not None:
                m.add(upright(note, 0.3), ti, 0.62 if full else 0.5, pan=-0.05, room=0.2)
            m.add(HT, ti, (0.14 if i % 2 == 0 else 0.09) * (1.2 if full else 1), pan=0.35)
            beat = i // 2
            if i % 2 == 0 and beat in (1, 3):
                m.add(snap(seed + b * 8 + i), ti, 0.42 if not full else 0.3, pan=-0.3, room=0.35)
                m.add(brush(seed + b * 8 + i), ti, 0.32 if not full else 0.45, pan=0.1, room=0.25)
            if full and i % 2 == 0 and beat in (0, 2):
                m.add(K, ti, 0.75)
        # vibes on the "and" of 2 and 4
        for beat in (1, 3):
            ti = bt + (beat + SW) * BEAT
            if ti < t1:
                m.add(keys(DM9[:4], 0.35, 1.1, 70 + b * 2 + beat), ti, 0.26 if not full else 0.32, pan=0.25, room=0.4)
        if full:
            m.add(OH, bt + 3 * BEAT + SW * BEAT, 0.12, pan=0.4)
            if b % 2 == 0:
                m.add(brass([50, 57, 62, 65], 0.28, 1.0, 80 + b), bt, 0.32, pan=0.15, hall=0.2)


def stab(t, gain=0.5, notes=(62, 65, 69, 72), d=0.22):
    m.add(brass(list(notes), d, 1.15, int(t * 100) % 97), t, gain, pan=0.1, hall=0.25)


# ---- act 1: the plan, until the phone buzzes
start, stop = M["start"], M["stop"]
m.add(impact(1.2), start, 0.35, hall=0.2)
stab(start, 0.55, (50, 57, 62, 65), 0.35)
groove(start, stop, seed=1)
for c in M.get("cards", []):  # each name card lands on a brass hit and a snare crack
    stab(c, 0.42)
    m.add(SN, c, 0.28, room=0.3)
if "pop" in M:
    m.add(crash(1.6, 9), M["pop"], 0.4)
    stab(M["pop"], 0.5, (62, 66, 69, 74))
# the record stops: a bass note that winds down like a stopped tape, a scratch, then nothing
m.add(tape_stop(upright(38, 0.6), 0.6), stop, 0.5)
m.add(record_scratch(0.45), stop + 0.02, 0.3, pan=0.1)

# ---- act 2: the collapse, in slow motion and far too dramatic
a, b = M["slowmo"]
L = b - a
m.add(choir([(0.0, L, 50, "a"), (0.0, L, 57, "a"), (0.0, L, 62, "a"), (L * 0.45, L * 0.55, 65, "o")], voices=30, crowd=0.0, seed=33), a, 0.5, hall=0.6)
m.add(strings([38, 45, 50, 53], L + 0.3, 34), a, 0.32, hall=0.5)
for i in range(int(L / 0.07)):  # a timpani roll that swells into the crash
    m.add(timpani(38, 0.5), a + i * 0.07, 0.05 + 0.25 * (i * 0.07 / L) ** 2, room=0.3)
m.add(boom(1.4), b - 0.15, 0.5)

# ---- act 3: Grandma. Almost nothing: a held note so the room is not dead
g = M["grandma"]
m.add(pad([62, 69, 74], max(0.5, M["done"] - g), 1600, 35), g, 0.16, hall=0.4)
for i, n in enumerate((86, 81, 77)):
    m.add(bell(n, 1.4), g + 0.05 + i * 0.12, 0.05, pan=(-0.3, 0, 0.3)[i], hall=0.4)

# ---- act 4: "Done. Whole trip." The band comes in and stays to the end card
d = M["done"]
m.add(crash(2.2, 21), d, 0.45)
stab(d, 0.6, (50, 57, 62, 65, 69), 0.4)
groove(d, M["end"], full=True, seed=50)
melody = [(0, 69, 0.5), (1, 74, 0.5), (1.66, 77, 0.3), (2, 76, 0.5), (3, 74, 0.8), (4, 72, 0.5), (5, 74, 0.5), (5.66, 77, 0.3), (6, 81, 1.2)]
for beat, n, ln in melody:  # a brass line over the first two bars
    t = d + beat * BEAT
    if t < M["end"] - 0.2:
        m.add(brass([n, n - 12], ln, 1.2, int(beat * 10) + 40), t, 0.3, pan=-0.12, hall=0.3)
if "insert" in M:
    m.add(whoosh(0.4, True, 7), M["insert"] - 0.2, 0.35)
    m.add(ding((88, 95)), M["insert"] + 0.45, 0.25, hall=0.2)
if "stadium" in M:
    m.add(whoosh(0.5, False, 8), M["stadium"] - 0.25, 0.3)
    m.add(crash(2.4, 31), M["stadium"], 0.35)

# ---- the end card: the sonic logo on a final stab, then a pad to the end
e = M["end"]
stab(e, 0.5, (50, 57, 62, 65, 69), 0.5)
m.add(sonic_logo(), e, 0.95, hall=0.3)
m.add(pad([50, 57, 62, 65], DUR - e, 2000, 36), e + 0.1, 0.5, hall=0.4)

m.render(OUT, fade=0.5)
