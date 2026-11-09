"""Score for the 6 s bumper: one bar of groove under the sentence, a lift on the plan, the sonic logo
at 3.2 s and a short pad to the end.

    python3 src/audio/bumper.py out.wav --dur 6 --cues cues.json [--lang en|he]
"""
import json
import sys

from kit import *  # noqa: F401,F403

ARGS = sys.argv[1:]
OUT = ARGS[0]
opt = lambda n, d=None: ARGS[ARGS.index(n) + 1] if n in ARGS else d
DUR = float(opt("--dur", 6))
CUES = json.load(open(opt("--cues"))) if opt("--cues") else []
m = Mix(DUR)
BEAT = 0.4  # 150 BPM: the bumper is in a hurry
K, CL, HT = kick(), clap(), hat()
for k in range(8):
    tb = k * BEAT
    if tb >= 3.2:
        break
    m.add(K, tb, 0.75)
    if k % 2:
        m.add(CL, tb, 0.5, room=0.3)
    m.add(HT, tb + BEAT / 2, 0.2, pan=-0.3)
    m.add(bass_note(36 if k < 4 else 31, 0.2), tb + BEAT / 2, 0.45)
for k, (tb, ch) in enumerate([(0.0, [60, 64, 67]), (0.8, [57, 60, 64]), (1.6, [53, 57, 60]), (2.4, [55, 59, 62])]):
    m.add(keys(ch, 0.5, 1.0, 50 + k), tb, 0.3, pan=-0.15, room=0.3)
m.add(rev_cymbal(0.6), 2.6, 0.4)
m.add(sonic_logo(), 3.2, 1.0, hall=0.3)
m.add(pad([48, 55, 60, 64], DUR - 3.2, 2200, 98), 3.3, 0.9, hall=0.4)
m.add(strings([60, 64, 67, 72], DUR - 3.2, 97), 3.25, 0.45, hall=0.4)
FX = {
    "typing": lambda c: [(typing(c.get("dur", 1.4), 0.045), 0, 0.35, {})],
    "press": lambda c: [(thump(), 0, 0.55, {})],
    "swish": lambda c: [(whoosh(0.3, True, 61), 0, 0.28, {})],
    "ding": lambda c: [(ding((88, 95)), 0, 0.45, dict(hall=0.2))],
}
for c in CUES:
    for sig, dt, g, kw in FX.get(c["type"], lambda c: [])(c):
        m.add(sig, c["t"] + dt, g, **kw)
m.render(OUT, fade=0.5)
