"""Score for the sports montage "Pick your sport" (15 s, 120 BPM): a driving groove where every sport
lands on a brass stab one step higher than the last (C, Dm, Em, F, G, Am), big hits under the numbers,
a lighter keys groove under the app demo with a lift on the plan, and the sonic logo.

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

# intro (0-0.5): the title is already up at frame 0, so the score opens on the hit, then a quick tom fill
m.add(brass([60, 64, 67, 72], 0.5, 1.2), 0.0, 0.55, hall=0.3)
m.add(timpani(36, 1.0), 0.0, 0.5)
m.add(SN, 0.0, 0.35, room=0.25)
m.add(crash(1.4, 3), 0.0, 0.35)
for i in range(4):
    m.add(tom(150 - 16 * i, 0.3, 120 + i), 0.25 + i * BEAT / 8, 0.45 + 0.06 * i, pan=-0.3 + 0.2 * i, room=0.3)

# the six sports, one per second from 0.5: four on the floor, a stab climbing with each one
for s, (ch, root) in enumerate(STEPS):
    t0 = 0.5 + s
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

# the numbers (6.5-8.5): three big hits, then a snare build into the demo
for i, tb in enumerate((6.5, 7.0, 7.5)):
    m.add(taiko(60 + 4 * i, 1.0, 30 + i), tb, 0.85, room=0.3, hall=0.2)
    m.add(brass([n + 12 for n in STEPS[5][0]], 0.3, 1.2, 40 + i), tb, 0.45, hall=0.25)
    m.add(CL, tb, 0.45, room=0.3, hall=0.15)
    m.add(SN, tb, 0.3, room=0.2)
m.add(strings([57, 60, 64, 69], 2.0, 50), 6.5, 0.25, hall=0.4)
for i in range(8):
    m.add(SN, 8.0 + i * BEAT / 8, 0.12 + 0.04 * i, room=0.2)
m.add(rev_cymbal(0.6), 7.9, 0.4)

# the demo (8.5-12): a lighter groove under the typing, a hit on the plan, a lift into the logo
DEMO = [(8.5, [60, 64, 67], 36), (9.5, [57, 60, 64], 33), (10.5, [53, 57, 60], 41), (11.25, [55, 59, 62], 43)]
for k in range(7):
    tb = 8.5 + k * BEAT
    under_typing = tb < 10.0
    m.add(K, tb, 0.55 if under_typing else 0.75)
    if k % 2:
        m.add(CL, tb, 0.35 if under_typing else 0.5, room=0.3, hall=0.1)
    m.add(shaker(), tb + BEAT / 2, 0.16, pan=0.25)
    m.add(HT, tb + BEAT / 2, 0.14, pan=-0.3)
for i, (tb, ch, root) in enumerate(DEMO):
    nxt = DEMO[i + 1][0] if i + 1 < len(DEMO) else 12.0
    m.add(keys(ch, min(0.9, nxt - tb), 1.0, 60 + i), tb, 0.52, pan=-0.15, room=0.3)
    if nxt - tb > 0.6:
        m.add(keys(ch, 0.4, 1.0, 64 + i), tb + 0.5, 0.36, pan=-0.15, room=0.3)
    for q in range(int(round((nxt - tb) / (BEAT / 2)))):
        m.add(bass_note(root + (12 if q % 2 else 0), 0.2), tb + q * BEAT / 2, 0.45)
for i, n in enumerate((72, 76, 79, 76, 72, 76, 79, 84)):
    m.add(pluck(n, 0.3), 8.5 + i * 0.25, 0.4, pan=(0.3, -0.3)[i % 2])
m.add(brass([65, 69, 72, 77], 0.6, 1.2, 70), 10.55, 0.5, hall=0.3)
m.add(choir([(0.0, 1.0, 60, "a"), (0.0, 1.0, 65, "a"), (0.0, 1.0, 69, "a")], voices=24, crowd=0.4, seed=9), 10.55, 0.4, hall=0.5)
for i in range(8):
    m.add(SN, 11.5 + i * BEAT / 8, 0.1 + 0.035 * i, room=0.2)
m.add(rev_cymbal(0.6), 11.4, 0.4)

# end card
m.add(sonic_logo(), 12.0, 0.8, hall=0.3)
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
    "typing": lambda c: [(typing(c.get("dur", 1.1), 0.045), 0, 0.4, {})],
    "press": lambda c: [(thump(), 0, 0.6, {})],
    "swish2": lambda c: [(whoosh(0.3, True, 61), 0, 0.3, {})],
    "ding": lambda c: [(ding((88, 95)), 0, 0.5, dict(hall=0.2))],
    "logo": lambda c: [],
}
for c in CUES:
    for sig, dt, g, kw in FX.get(c["type"], lambda c: [])(c):
        pan = kw.pop("pan", 0.0)
        m.add(sig, c["t"] + dt, g, pan=pan, **kw)

m.render(OUT, fade=0.7)
