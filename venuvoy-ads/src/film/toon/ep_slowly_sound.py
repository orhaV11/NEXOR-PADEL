"""The sound of the sketch "Really Slowly" (vlog 04, "slowly"): nine whispered lines in the hush of a tennis stand, a
crisp packet, a thousand people going "shhh", then the sung name.

    python3 src/film/toon/ep_slowly_sound.py            the soundtrack and the timeline (work/slowly/sound)

There is no vlog music: the hush is the sound. Four things here are this film's own, done round the kit's build()
because the kit has no statement for them:
  rally_air()   the air of the place. The bought air ("air2", heard back as gentle air with soft taps about once a
                second) has its taps where they fell; here the same taps are laid as a slow rally that is heard in the
                quiet before "I'll do it really slowly." and STOPS when the packet starts. The kit then plays that file.
  lay()         a sound laid in as a stretch of a bought file with its own level and shape (the kit's fx start 50 ms
                before the loudest instant and last 0.7 s, which is right for a thud only): the long crinkle, the
                crowd's shush (the second half of "shush2": its first second is crowd chatter), the last small rustle.
  last_words()  the recogniser times the last word of a line badly (stretched through the silence after it, or, with
                these whispers, ended a third of a second early): its mouth shapes then spread too thin or stop too
                soon. Every last word ends where the voice does.
His "Luna, no" is not cut off: the packet simply starts under the "no".
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal as ss

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ASSETS, ROOT, SR, build, db, fade, hp, lp  # noqa: E402

FILM = "slowly"
OUT = ROOT / "work" / FILM / "sound"
A, B, C = "ldn_g25p_1", "ldn_g25p_2", "ldn_g25p_3"   # three readings; each line from the one where the whisper is clear and right

# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends), options
PLAN = [
    ("L1", "L", B, 1, 0.40, {}),                      # 0.4 s of picture first: her face, the packet, his eyes on it
    ("T2", "T", B, 2, 0.14, {}),                      # he has seen it
    ("L3", "L", A, 3, 0.16, {}),
    ("T4", "T", B, 4, 0.12, {"db": -1.0}),            # the "p" of "clapping" is a spike in a whisper: a decibel down, so that it does not clip
    ("CUT", 0.32),                                    # a jump cut closer: time has passed, she has not waited
    ("L5", "L", A, 5, 1.22, {}),                      # 1.2 s nobody speaks: two strokes of the rally far below; she eyes the packet
    ("T6", "T", B, 6, 0.06, {}),                      # at once: he knows what "slowly" means
    ("HOLD", "crinkle", -0.28, 1.56),                 # the packet starts under his "no": one long slow crinkle
    ("HOLD", "shush", 0.02, 0.62),                    # a thousand people: "Shhh!". She is caught; nothing moves
    ("HOLD", "sink", 0.0, 0.85),                      # the packet goes down out of sight, slowly, with one last small rustle
    ("CUT", 0.12),                                    # wider: the two of them in the silence, and all those hats
    ("T7", "T", C, 7, 0.26, {}),                      # eyes shut, to nobody
    ("L8", "L", A, 8, 0.36, {}),                      # a beat while she turns to him
    ("T9", "T", B, 9, 0.44, {}),                      # he does not even open his eyes (uncut: the last word)
    ("PLATE", 0.80),
]


def _src(name):
    return next(q for q in (ASSETS / FILM / "sound" / "raw" / f"{name}.wav", OUT / "raw" / f"{name}.wav") if q.exists())


def rally_air(ev, name="air_rally", length=22.0):
    """The bought air with its taps taken out, run on to `length` seconds, and the same taps laid back in as a slow
    rally: a stroke about every second from the start, two of them in the quiet after the first cut, none once the
    packet has started. Written to work/slowly/sound/raw/<name>.wav; the kit plays it from 0.5 s in."""
    x, sr = sf.read(_src("air2"))
    assert sr == SR
    taps = {"near": (3.235, 3.43, -5.0), "far": (5.285, 5.45, 0.0)}      # where two of the taps are in the bought air, and a trim (dB)
    cut = [(1.43, 1.56), (3.22, 3.44), (3.97, 4.08), (5.27, 5.46)]       # the four loud taps: out of the bed
    xf = int(0.06 * SR)
    bed, pos = [], 0
    for a, b in cut:
        seg = x[pos:int(a * SR)].copy()
        bed.append(seg)
        pos = int(b * SR)
    bed.append(x[pos:].copy())
    y = bed[0]
    for seg in bed[1:]:                                                  # joined with short crossfades
        ramp = np.linspace(0, 1, xf)
        y = np.concatenate([y[:-xf], y[-xf:] * (1 - ramp) + seg[:xf] * ramp, seg[xf:]])
    one, lap = y[int(0.3 * SR):], int(1.2 * SR)
    air = one.copy()
    while len(air) < length * SR:                                        # run on, with long crossfades
        ramp = np.linspace(0, 1, lap)
        air = np.concatenate([air[:-lap], air[-lap:] * (1 - ramp) + one[:lap] * ramp, one[lap:]])
    air = air[:int(length * SR)]
    quiet_from = ev["cuts"][0] + 0.16                                    # the first stroke heard in the quiet after the cut
    stop = ev["crinkle"][0] - 0.25                                       # no stroke after this: the players have heard it too
    times, t, k = [], quiet_from, 0
    while t > 0.25:                                                      # backwards to the start of the film
        times.append((t, k % 2))
        t -= 1.10 if k % 2 == 0 else 1.00
        k += 1
    t, k = quiet_from + 1.00, 1
    while t < stop:
        times.append((t, k % 2))
        t += 1.10 if k % 2 == 1 else 1.00
        k += 1
    for t, who in sorted(times):
        a, b, trim = taps["far" if who else "near"]
        tap = fade(x[int(a * SR):int(b * SR)].copy(), 0.004, 0.05) * db(trim)
        i = int((t + 0.5 - 0.045) * SR)                                  # the kit starts 0.5 s into the file; the stroke is 45 ms into the cut-out
        air[i:i + len(tap)] += tap
    (OUT / "raw").mkdir(parents=True, exist_ok=True)
    sf.write(OUT / "raw" / f"{name}.wav", air, SR)
    print("rally strokes at", [round(t, 2) for t, _ in sorted(times)], "s; none after", round(stop, 2))
    return [round(t, 3) for t, _ in sorted(times)]


def last_words():
    """Every last word ends where the voice in its line does (see the note at the top)."""
    tl = json.loads((OUT / "timeline.json").read_text())
    for l in tl["lines"]:
        x, sr = sf.read(ROOT / l["wav"])
        h = int(0.01 * sr)
        env = np.array([np.sqrt(np.mean(x[i:i + h] ** 2)) for i in range(0, len(x) - h, h)])
        loud = np.where(env > db(-36))[0]                                  # the lines are levelled; between them is true silence
        w = l["words"][-1]
        w[2] = round(float(max(w[1] + 0.12, (loud[-1] + 1) * 0.01)), 3)   # shorter OR longer: "Coincidence." was timed 0.37 s short
    (OUT / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))


def shape(n, attack, hold, decay):
    """An envelope: up in `attack` seconds, held, down over `decay` (the rest of n samples)."""
    e = np.ones(n)
    na, nh = int(attack * SR), int(hold * SR)
    e[:na] = np.linspace(0, 1, na) ** 0.7
    tail = n - na - nh
    if tail > 0:
        e[na + nh:] = np.linspace(1, 0, tail) ** 1.6
    return e


def lay(pieces):
    """pieces: [(name of the event, bought file, (from, to) in it, start in the film, how it is treated)]. Each is laid
    into the mix and the effects stem; the film is mastered again; the events and the files are named in the timeline
    (pack.py keeps the sounds named there)."""
    tl = json.loads((OUT / "timeline.json").read_text())
    mix, _ = sf.read(OUT / "raw.wav")
    fx, _ = sf.read(OUT / "fx.wav")
    for name, file, part, t, treat in pieces:
        x, sr = sf.read(_src(file))
        assert sr == SR
        x = treat(x[int(part[0] * SR):int(part[1] * SR)].copy())
        i = int(t * SR)
        x = x[:len(mix) - i]
        for y in (mix, fx):
            y[i:i + len(x)] += x[:, None]
        tl["events"][name + "_fx"] = [round(t, 3), round(t + len(x) / SR, 3)]
        tl["used"]["raw"] = sorted(set(tl["used"]["raw"]) | {file})
        rms = 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-9)
        print(f"{name}: {file} {part[0]}-{part[1]} s laid at {t:.2f}-{t + len(x) / SR:.2f} s, rms {rms:.1f} dB, peak {20 * np.log10(np.abs(x).max() + 1e-9):.1f} dB")
    sf.write(OUT / "raw.wav", mix, SR)
    sf.write(OUT / "fx.wav", fx, SR)
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(OUT / "raw.wav"), str(OUT / "final.wav")], check=True)
    tl["used"]["raw"] = sorted(set(tl["used"]["raw"]) | {"air2"})            # the bought air the rally was laid from
    (OUT / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))


def crackle(rms, ceiling=-3.0):
    """A crinkle is all short spikes (25 dB between its peaks and its level): as bought it would be quieter than a
    whisper. The spikes are rounded off so that the whole can be loud: the loudest thing in the film."""
    def f(x):
        x = hp(x, 250)
        g = 1.0
        for _ in range(30):                                              # the gain at which the rounded sound reads `rms`
            y = np.tanh(x * g / db(ceiling)) * db(ceiling)
            g *= db(rms) / (np.sqrt(np.mean(y ** 2)) + 1e-9)
        return fade(np.tanh(x * g / db(ceiling)) * db(ceiling), 0.02, 0.12)
    return f


def hush(rms, body=-9.0):
    """The crowd's shush. As bought it is a crowd's breathy noise with most of its energy between 500 and 2000 Hz: the
    watching model heard that, in the first render, as "a collective gasp or groan". A "shhh" is the same breath two
    octaves higher, so the part of the sound between 1.7 and 7.5 kHz is made the main thing and the rest is kept 9 dB
    under it. It comes all at once, stays half a second and dies away."""
    def f(x):
        hiss = ss.sosfilt(ss.butter(2, [1700, 7500], "band", fs=SR, output="sos"), x)
        low = hp(x, 600)
        x = hiss * np.sqrt(np.mean(low ** 2)) / (np.sqrt(np.mean(hiss ** 2)) + 1e-9) + low * db(body)
        x = x * shape(len(x), 0.04, 0.50, 0.0)
        return x * db(rms) / (np.sqrt(np.mean(x[int(0.06 * SR):int(0.50 * SR)] ** 2)) + 1e-9)
    return f


def rustle(rms):
    def f(x):
        x = hp(x, 250)
        return fade(x * db(rms) / (np.sqrt(np.mean(x ** 2)) + 1e-9), 0.03, 0.15)
    return f


if __name__ == "__main__":
    air = {"file": "air2", "db": -30.0, "gusts": False}
    ev = build(FILM, PLAN, bed=None, ambience=air)                       # once for the times
    strokes = rally_air(ev)
    ev = build(FILM, PLAN, bed=None, ambience=dict(air, file="air_rally"))   # and with the rally where the film wants it
    last_words()
    lay([("crinkle", "crinkle", (0.02, 1.62), ev["crinkle"][0], crackle(-17.0)),
         ("shush", "shush2", (1.25, 2.40), ev["shush"][0], hush(-19.0)),
         ("rustle", "crinkle", (3.02, 3.62), ev["sink"][0] + 0.12, rustle(-27.0))])
    tl = json.loads((OUT / "timeline.json").read_text())
    tl["events"]["strokes"] = strokes
    (OUT / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))
    print("events", json.dumps(tl["events"]))
