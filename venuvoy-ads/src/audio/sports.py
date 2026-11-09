"""Score for the sports montage "Pick your sport" (15 s, 120 BPM): a driving groove where every sport
lands on a brass stab one step higher than the last (C, Dm, Em, F, G, Am), big hits under the numbers,
stomps and shouts under the line, and the sonic logo.

    python3 src/audio/sports.py out.wav --dur 15 --cues cues.json [--lang en|he]
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
BEAT = 0.5
K, CL, HT, SN, ST = kick(), clap(), hat(), snare(), stomp()
STEPS = [([48, 52, 55, 60], 36), ([50, 53, 57, 62], 38), ([52, 55, 59, 64], 40), ([53, 57, 60, 65], 41), ([55, 59, 62, 67], 43), ([57, 60, 64, 69], 45)]

# intro: a hit and a tom fill
m.add(brass([60, 64, 67, 72], 0.6, 1.2), 0.0, 0.55, hall=0.3)
m.add(timpani(36, 1.2), 0.0, 0.5)
for i in range(6):
    m.add(tom(150 - 12 * i, 0.35, 120 + i), 0.5 + i * BEAT / 6 * 1.0, 0.5 + 0.05 * i, pan=-0.4 + 0.16 * i, room=0.3)

# the six sports: four on the floor, a stab climbing with each one
for s, (ch, root) in enumerate(STEPS):
    t0 = 1.0 + s
    m.add(brass([n + 12 for n in ch], 0.35, 1.15, 10 + s), t0, 0.5, pan=0.12, hall=0.25)
    m.add(crash(1.2, 20 + s), t0, 0.28 if s else 0.45)
    for k in range(2):
        tb = t0 + k * BEAT
        m.add(K, tb, 0.8)
        if k == 1:
            m.add(CL, tb, 0.5, room=0.3, hall=0.1)
        for q in range(4):
            m.add(HT, tb + q * BEAT / 4, 0.18 if q % 2 == 0 else 0.11, pan=-0.3)
        for q in range(2):
            m.add(bass_note(root + (12 if q else 0), 0.2), tb + q * BEAT / 2, 0.5)
    m.add(pluck(ch[-1] + 24, 0.3), t0 + 0.75, 0.22, pan=0.3)

# the numbers: three big hits, then a snare build
for i, tb in enumerate((7.0, 7.5, 8.0)):
    m.add(taiko(60 + 4 * i, 1.0, 30 + i), tb, 0.85, room=0.3, hall=0.2)
    m.add(brass([n + 12 for n in STEPS[5][0]], 0.3, 1.2, 40 + i), tb, 0.45, hall=0.25)
m.add(strings([57, 60, 64, 69], 2.0, 50), 7.0, 0.25, hall=0.4)
for i in range(12):
    m.add(SN, 8.4 + i * BEAT / 4 * 0.8, 0.1 + 0.035 * i, room=0.2)
m.add(rev_cymbal(0.7), 8.3, 0.4)

# the line: stomp-clap with the crowd shouting, a big landing on "whole trip"
for k in range(6):
    tb = 9.0 + k * BEAT
    if k % 2 == 0:
        m.add(ST, tb, 0.9, room=0.25, hall=0.12)
        m.add(K, tb, 0.5)
    else:
        m.add(CL, tb, 0.6, room=0.35, hall=0.2)
for tb in (9.0, 9.5, 10.0):
    m.add(hey(int(tb * 7)), tb + 0.02, 0.36, hall=0.4)
m.add(brass([60, 64, 67, 72, 76], 1.0, 1.3, 77), 10.75, 0.6, hall=0.35)
m.add(choir([(0.0, 1.2, 60, "a"), (0.0, 1.2, 64, "a"), (0.0, 1.2, 67, "a")], voices=28, crowd=0.6, seed=9), 10.75, 0.45, hall=0.5)
m.add(crash(2.0, 99), 10.75, 0.45)

# end card
m.add(sonic_logo(), 12.0, 1.0, hall=0.3)
m.add(pad([48, 55, 60, 64], DUR - 12.0, 2200, 98), 12.1, 0.9, hall=0.4)
m.add(strings([60, 64, 67, 72], DUR - 12.0, 97), 12.05, 0.5, hall=0.4)
for k, n in enumerate((79, 76, 84, 79)):
    m.add(bell(n), 13.2 + k * 0.45, 0.18, pan=(-0.3, 0.3)[k % 2], hall=0.35)

FX = {
    "hit": lambda c: [(impact(), 0, 0.5, dict(hall=0.25))],
    "kickball": lambda c: [(ball_kick(), 0, 0.7, dict(room=0.3))],
    "whoosh": lambda c: [(whoosh(0.4), 0.05, 0.3, {})],
    "dribble": lambda c: [(dribble(), 0, 0.65, dict(room=0.35))],
    "swish": lambda c: [(swish(), 0, 0.7, {})],
    "pock": lambda c: [(tennis_pock(), 0, 0.6 * c.get("g", 1), dict(room=0.35))],
    "golf": lambda c: [(golf_swing(), -0.3, 0.55, {})],
    "plop": lambda c: [(plop(), 0, 0.6, {})],
    "ooh": lambda c: [(ooh(1.2), 0, 0.35, dict(hall=0.4))],
    "engine": lambda c: [(engine_pass(1.0), -0.1, 0.55, {})],
    "rugby": lambda c: [(rugby_thud(), 0, 0.7, dict(room=0.3))],
    "roar": lambda c: [(roar(c.get("dur", 1.5)), 0, 0.4, {})],
    "thud": lambda c: [(boom(0.6), 0, 0.4, {})],
    "stomp": lambda c: [],
    "logo": lambda c: [],
}
for c in CUES:
    for sig, dt, g, kw in FX.get(c["type"], lambda c: [])(c):
        pan = kw.pop("pan", 0.0)
        m.add(sig, c["t"] + dt, g, pan=pan, **kw)

m.render(OUT, fade=0.7)
