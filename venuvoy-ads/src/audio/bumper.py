"""Score for the 6 s bumper: one bar of groove under the sentence, keys and a pluck line for the mids,
a brass hit on the confirmed plan, the sonic logo at 3.6 s and a pad to the end.

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
END = 3.6  # the end card lands here and holds 2.4 s
K, CL, HT = kick(), clap(), hat()
CHORDS = [(0.0, [60, 64, 67], 36), (0.8, [57, 60, 64], 33), (1.6, [53, 57, 60], 41), (2.4, [50, 53, 57, 60], 38), (3.2, [55, 59, 62], 43)]
for k in range(9):
    tb = k * BEAT
    if tb >= END:
        break
    root = [r for t0, _, r in CHORDS if t0 <= tb + 1e-6][-1]
    m.add(K, tb, 0.75)
    if k % 2:
        m.add(CL, tb, 0.5, room=0.3)
    m.add(HT, tb + BEAT / 2, 0.2, pan=-0.3)
    m.add(bass_note(root - 12 if root > 40 else root, 0.2), tb + BEAT / 2, 0.45)
# the mids carry the demo on a phone speaker: keys on every chord, a pluck line over the typing and the hold
for k, (tb, ch, _) in enumerate(CHORDS):
    m.add(keys(ch, 0.5, 1.0, 50 + k), tb, 0.5, pan=-0.15, room=0.3)
    m.add(keys(ch, 0.3, 1.0, 55 + k), tb + 0.4, 0.34, pan=-0.15, room=0.3)
for i, n in enumerate((72, 76, 79, 76, 72, 76, 81, 79)):
    m.add(pluck(n, 0.28), 0.1 + i * 0.16, 0.36, pan=(0.3, -0.3)[i % 2])
for i, n in enumerate((77, 81, 84, 81, 77, 74, 77, 81, 79)):
    m.add(pluck(n, 0.28), 2.1 + i * 0.16, 0.32, pan=(-0.3, 0.3)[i % 2])
m.add(brass([65, 69, 72, 77], 0.5, 1.2, 70), 1.85, 0.5, hall=0.3)
m.add(rev_cymbal(0.6), END - 0.6, 0.4)
m.add(sonic_logo(), END, 1.0, hall=0.3)
m.add(pad([48, 55, 60, 64], DUR - END, 2200, 98), END + 0.1, 0.9, hall=0.4)
m.add(strings([60, 64, 67, 72], DUR - END, 97), END + 0.05, 0.45, hall=0.4)
FX = {
    "typing": lambda c: [(typing(c.get("dur", 1.2), 0.045), 0, 0.35, {})],
    "press": lambda c: [(thump(), 0, 0.55, {})],
    "swish": lambda c: [(whoosh(0.3, True, 61), 0, 0.28, {})],
    "ding": lambda c: [(ding((88, 95)), 0, 0.45, dict(hall=0.2))],
}
for c in CUES:
    for sig, dt, g, kw in FX.get(c["type"], lambda c: [])(c):
        m.add(sig, c["t"] + dt, g, **kw)
m.render(OUT, fade=0.5)
