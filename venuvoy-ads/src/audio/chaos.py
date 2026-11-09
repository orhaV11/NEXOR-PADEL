"""Score for the comedy spot "Planning a football trip be like" (15 s): panic music that speeds up and
climbs while the chaos piles up, a tape stop and a record scratch on the freeze, a calm groove for
Venuvoy, and the sonic logo on the end card.

    python3 src/audio/chaos.py out.wav --dur 15 --cues cues.json [--lang en|he]
"""
import json
import sys

from kit import *  # noqa: F401,F403

ARGS = sys.argv[1:]
OUT = ARGS[0]
opt = lambda n, d=None: ARGS[ARGS.index(n) + 1] if n in ARGS else d
DUR = float(opt("--dur", 15))
CUES = json.load(open(opt("--cues"))) if opt("--cues") else []
m = Mix(DUR)

# ------------------------------------------------------------------ panic (0-6 s): accelerando 150 -> 220 BPM, up a semitone every 1.5 s
FREEZE = 6.0
panic = Mix(FREEZE + 0.6)
t, i = 0.0, 0
riff = [0, 12, 3, 12, 7, 12, 5, 3]
K, HT, SN, CL = kick(), hat(), snare(), clap()
while t < FREEZE:
    key = 45 + int(t // 1.5)  # A2, then up
    panic.add(pluck(key + 24 + riff[i % 8], 0.11), t, 0.32, pan=(-0.25, 0.25)[i % 2])
    panic.add(HT, t, 0.1 + 0.06 * (i % 2 == 0), pan=-0.3)
    if i % 2 == 0:
        panic.add(bass_note(key + (12 if i % 8 == 6 else 0), 0.12), t, 0.5)
    if i % 4 == 0:
        panic.add(K, t, 0.65)
        panic.add(click(0.012, 2500, 6000, 100 + i), t, 0.18 if (i // 4) % 2 else 0.12, pan=0.5)  # the clock: tick, tock
    if i % 8 == 4:
        panic.add(SN, t, 0.45, room=0.2)
    bpm = 150 + 70 * (t / FREEZE) ** 1.3
    t += 60 / bpm / 4
    i += 1
panic.add(riser(3.0), 3.0, 0.35)
stem = panic.dry[: int(FREEZE * SR)] + convolve(panic.room[: int(FREEZE * SR)], reverb_ir(0.8, 0.15, 7000, 0.005, 3)) * 0.8
stem = tape_stop(stem, 0.38)
m.add(stem, 0.0, 0.85)

# ------------------------------------------------------------------ calm (6.6-12 s): a relaxed groove at 100 BPM
B0, BT = 6.6, 0.6
CHORDS = [[48, 52, 55, 59], [45, 48, 52, 55], [41, 45, 48, 52], [43, 47, 50, 55]]  # Cmaj7 Am7 Fmaj7 G
ROOTS = [36, 33, 29, 31]
for k in range(9):
    tb = B0 + k * BT
    if tb >= 12.0:
        break
    if k % 2 == 0:
        m.add(kick(110, 46, 0.4, 0.15), tb, 0.55)
    else:
        m.add(lp(snare(200 + k), 3500), tb, 0.32, room=0.35)
        m.add(clap(210 + k), tb, 0.3, room=0.35)
    for s in range(2):
        m.add(shaker(300 + k * 2 + s), tb + s * BT / 2, 0.22, pan=0.35)
        m.add(hat(False, 320 + k * 2 + s), tb + s * BT / 2 + BT / 4, 0.12, pan=-0.3)
for bar in range(3):
    tb = B0 + bar * 4 * BT
    ch = CHORDS[bar % 4]
    for off in (0.0, 1.5 * BT, 2.5 * BT):
        if tb + off < 12.0:
            m.add(keys([n + 12 for n in ch], 0.9, 1.0, 400 + bar), tb + off, 0.52, pan=-0.15, room=0.4)
    for q in range(8):  # a plucked line keeps the calm section present on small speakers
        tq = tb + q * BT / 2
        if tq < 12.0:
            m.add(pluck(ch[(0, 2, 1, 3, 2, 1, 3, 2)[q]] + 24, 0.3), tq, 0.34, pan=0.25, hall=0.2)
    for off in (0.0, 2 * BT):
        if tb + off < 12.0:
            m.add(sub(ROOTS[bar % 4], 1.0), tb + off, 0.4)
for k, (tb, n) in enumerate([(8.6, 84), (9.0, 88), (9.4, 91)]):  # the checklist climbs
    m.add(bell(n, 1.4), tb, 0.0, hall=0.0)
m.add(choir([(0.0, 1.8, 60, "a"), (0.0, 1.8, 64, "a"), (0.0, 1.8, 67, "a")], voices=18, crowd=0.0, seed=8), 9.95, 0.3, hall=0.6)

# ------------------------------------------------------------------ end card
m.add(sonic_logo(), 12.0, 0.7, hall=0.3)
m.add(pad([48, 55, 60, 64], DUR - 12.0, 2200, 98), 12.1, 0.9, hall=0.4)
m.add(strings([60, 64, 67, 72], DUR - 12.0, 97), 12.05, 0.5, hall=0.4)
for k, n in enumerate((79, 76, 84, 79)):
    m.add(bell(n), 13.2 + k * 0.45, 0.18, pan=(-0.3, 0.3)[k % 2], hall=0.35)

# ------------------------------------------------------------------ effects from the page
FX = {
    "pop": lambda c: [(pop(1000, 380), 0, 0.4, {})],
    "typing": lambda c: [(typing(c.get("dur", 1.0), 0.035 if c.get("fast") else 0.07), 0, 0.4 if c.get("fast") else 0.3, {})],
    "alarm": lambda c: [(alarm(0.8), 0, 0.3, {}), (glitch(0.2), 0, 0.25, {})],
    "stamp": lambda c: [(stamp(), 0.02, 0.6, dict(room=0.2))],
    "bonk": lambda c: [(error_bonk(), 0, 0.45, {})],
    "notif": lambda c: [(notif(), 0, 0.42, dict(pan=0.3))],
    "buzz": lambda c: [(buzz(0.3), 0, 0.45, {})],
    "beeps": lambda c: [(beeps(7), 0, 0.4, {})],
    "scribble": lambda c: [(scribble(0.45), 0, 0.5, {})],
    "error": lambda c: [(error_bonk(), 0, 0.4, dict(pan=-0.3)), (notif(), 0.05, 0.3, dict(pan=0.4))],
    "glitch": lambda c: [(glitch(0.25), 0, 0.4, {})],
    "scratch": lambda c: [(record_scratch(0.55), 0, 0.55, {})],
    "whoosh": lambda c: [(whoosh(0.45), -0.1, 0.32, {})],
    "press": lambda c: [(thump(), 0, 0.55, {})],
    "swish": lambda c: [(whoosh(0.3, True, 61), 0, 0.25, {})],
    "ding": lambda c: [(ding((84 + 4 * c.get("n", 0), 91 + 4 * c.get("n", 0))), 0, 0.8, dict(hall=0.2))],
    "done": lambda c: [(ding((96, 100)), 0, 0.35, dict(hall=0.3)), (bell(84, 2.0), 0, 0.25, dict(hall=0.4))],
    "logo": lambda c: [],
}
for c in CUES:
    for sig, dt, g, kw in FX.get(c["type"], lambda c: [])(c):
        pan = kw.pop("pan", 0.0)
        m.add(sig, c["t"] + dt, g, pan=pan, **kw)

m.render(OUT, fade=0.7)
