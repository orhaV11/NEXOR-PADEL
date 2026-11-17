"""The soundtrack of "The Switch", and the timing every picture follows.

    python3 src/film/toon/switch_sound.py [sung|beat] [bed]

The lines come from two readings of the same cast (chosen line by line with the listening model). Each line is
levelled, its long inner silences are shortened, and the lines are laid one after another with hand-set gaps: the
gaps ARE the comic timing, so they live here in one table. Timo's two lines from the ground are dulled as if spoken
into the grass and sit a little under the others. Under everything: wind on the hill. Effects: the thud when he
drops, and the sung name on the closing plate. "bed" adds the vlog's own music under the first two shots; it stops
dead when Timo looks at the lens. Writes work/switch/sound/final.wav (mastered) and work/switch/sound/timeline.json.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal as ss

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
WORK = ROOT / "work" / "switch"
KEEP = ROOT / "src" / "film" / "assets" / "toon" / "switch"   # the stored copies of what was recorded and bought
LINES = KEEP / "voice" / "lines" if (KEEP / "voice" / "lines").exists() else WORK / "voice" / "lines"
RAW = KEEP / "sound" / "raw" if (KEEP / "sound" / "raw").exists() else WORK / "sound" / "raw"
OUT = WORK / "sound"
(OUT / "lines").mkdir(parents=True, exist_ok=True)
SR = 48000
BED_DB = -13.0   # the vlog music sits this far under the voices in the band where speech lives (300-3400 Hz)
B, D = "ldn_g25p_3", "ldn_g25p_5"  # the quicker reading (all of Timo, Luna's first line) and the livelier one (the rest of Luna)

# id, who, reading, number in the reading, gap before it (seconds after the previous line ends; negative = talks over), options
PLAN = [
    ("L1", "L", B, 1, 0.25, {}),
    ("T2", "T", B, 2, 0.10, {"ground": True}),
    ("L3", "L", D, 3, 0.30, {"tighten": 0.20}),   # caught: she takes a moment
    ("CUT", 0.10),
    ("T4", "T", B, 4, 0.14, {}),
    ("L5", "L", D, 5, 0.05, {"tighten": 0.10}),
    ("T6", "T", B, 6, 0.20, {"cut_in_word": ("flip", 0.30, 0.22)}),   # he is talked over: his word fades away under her first one
    ("L7", "L", D, 7, -0.22, {}),
    ("CUT", 0.77),   # he looks at us for this long
    ("T8", "T", B, 8, 0.22, {"even": 12.0}),   # "did you flip?" drops to a murmur in this reading: lift it out of the wind
    ("L9", "L", D, 9, 0.42, {}),   # she gets her phone out first; the pause before "Run." is hers, uncut
    ("SINK", 0.45, 0.44),  # he stands quite still for this long after her line, then sinks, and takes this long
    ("T10", "T", B, 10, 0.26, {"ground": True}),
    ("L11", "L", D, 11, 0.38, {}),   # she looks down at him, then back at us
    ("PLATE", 1.0),   # first his finger comes up beside her; then the name
]
TEXT = {"L1": "Day two of the football trip!", "T2": "Why is there a mountain in the football trip?", "L3": "The planner had a switch. It said “hikes”.",
        "T4": "You hate hikes.", "L5": "I like the idea of hikes.", "T6": "So why did you fl—", "L7": "It was a switch!",
        "T8": "…What else did you flip?", "L9": "“Tomorrow morning. Run.”", "T10": "Flip it back.", "L11": "He loves it."}


def db(x):
    return 10 ** (x / 20)


def level(x, target=-20.0):
    """Bring the sounding part of a line to one level (dBFS RMS)."""
    h = int(0.02 * SR)
    r = np.array([np.sqrt(np.mean(x[i:i + h] ** 2)) for i in range(0, max(1, len(x) - h), h)])
    loud = r[r > db(-42)]
    return x * db(target) / (np.sqrt(np.mean(loud ** 2)) + 1e-9)


def tighten(x, words, max_gap):
    """Shorten the silences inside a line to max_gap seconds; the word times move with them."""
    h = int(0.01 * SR)
    env = np.array([np.sqrt(np.mean(x[i:i + h] ** 2)) for i in range(0, len(x) - h, h)])
    quiet = env < db(-46)
    out, cuts, i = [], [], 0
    while i < len(quiet):
        if quiet[i]:
            j = i
            while j < len(quiet) and quiet[j]:
                j += 1
            gap = (j - i) * 0.01
            if gap > max_gap and i > 0 and j < len(quiet):
                keep = max_gap / 2
                cuts.append((i * 0.01 + keep, j * 0.01 - keep))
            i = j
        else:
            i += 1
    pos = 0
    for a, b in cuts:
        out.append(x[pos:int(a * SR)])
        pos = int(b * SR)
    out.append(x[pos:])
    y = np.concatenate(out)
    def move(t):
        return t - sum(min(b, t) - a for a, b in cuts if t > a)
    return y, [[w, round(move(a), 3), round(move(b), 3)] for w, a, b in words], round(sum(b - a for a, b in cuts), 3)


def even(x, floor=-27.0, most=6.0):
    """Lift the quiet parts of a line (a trailing-off ending) toward `floor` dBFS, by at most `most` dB, smoothly;
    silence is left alone."""
    h = int(0.03 * SR)
    r = np.array([np.sqrt(np.mean(x[i:i + h] ** 2)) for i in range(0, max(1, len(x) - h), h)]) + 1e-9
    d = 20 * np.log10(r)
    g = np.where(d > -50, np.clip(floor - d, 0, most), 0.0)
    g = np.convolve(g, np.ones(5) / 5, "same")
    gain = np.interp(np.arange(len(x)), np.arange(len(g)) * h + h / 2, g)
    return x * 10 ** (gain / 20)


def fade(x, a=0.006, b=0.012):
    x = x.copy()
    na, nb = int(a * SR), int(b * SR)
    x[:na] *= np.linspace(0, 1, na)
    x[-nb:] *= np.linspace(1, 0, nb)
    return x


def lp(x, f, order=4):
    return ss.sosfilt(ss.butter(order, f, "low", fs=SR, output="sos"), x)


def hp(x, f, order=2):
    return ss.sosfilt(ss.butter(order, f, "high", fs=SR, output="sos"), x)


def whistle(d=0.16):
    """A referee's pea whistle: two close tones that beat against each other, a fast flutter, a breath of noise."""
    t = np.arange(int(d * SR)) / SR
    flutter = 0.72 + 0.28 * np.sin(2 * np.pi * 38 * t)
    tone = np.sin(2 * np.pi * 2850 * t) + 0.8 * np.sin(2 * np.pi * 3020 * t + 0.6) + 0.25 * np.sin(2 * np.pi * 5800 * t)
    noise = ss.sosfilt(ss.butter(2, [2500, 6000], "band", fs=SR, output="sos"), np.random.default_rng(3).standard_normal(len(t))) * 0.5
    env = np.minimum(1, t / 0.012) * np.minimum(1, (d - t) / 0.03)
    return (tone * flutter + noise) * env * 0.2


meta = {r["n"]: r for take in (B, D) for r in []}
reads = {take: {r["n"]: r for r in json.loads((LINES / f"{take}.json").read_text())} for take in (B, D)}
T, timeline, events = 0.0, [], {}
voices = np.zeros(SR * 40)
for row in PLAN:
    if row[0] == "CUT":
        events.setdefault("cuts", []).append(round(T + row[1], 3))
        T += row[1]
        continue
    if row[0] == "SINK":
        events["sink"] = [round(T + row[1], 3), round(T + row[1] + row[2], 3)]
        T += row[1] + row[2]
        continue
    if row[0] == "PLATE":
        events["plate"] = round(T + row[1], 3)
        continue
    lid, who, take, n, gap, o = row
    r = reads[take][n]
    x, sr = sf.read(LINES / f"{take}_{n:02d}.wav")
    assert sr == SR
    words = r["words"]
    cut = 0.0
    if o.get("tighten"):
        x, words, cut = tighten(x, words, o["tighten"])
    if o.get("cut_in_word"):  # the line is cut off inside a word: the other one talks over it
        w, into, tail = o["cut_in_word"]
        t_cut = min(next(a for ww, a, b in words if ww.startswith(w[:3])) + into, len(x) / SR)
        x = x[:int(t_cut * SR)]
        words = [[ww, a, min(b, t_cut - tail * 0.6)] for ww, a, b in words if a < t_cut - tail * 0.6]
        x = fade(x, 0.006, tail)  # he trails off under her first word
    x = level(fade(x), -20.5 if who == "L" else -18.5)
    x = even(x, -27.0, o.get("even", 5.0))
    x = hp(x, 90)
    if o.get("ground"):  # spoken into the grass: dull, a little quieter, still understood
        x = level(lp(x, 2300, 4) + lp(x, 5500, 2) * db(-11), -20.5)   # two decibels under his standing voice, the consonants still there
    t0 = T + gap
    voices[int(t0 * SR):int(t0 * SR) + len(x)] += x
    sf.write(OUT / "lines" / f"{lid}.wav", x, SR)
    timeline.append({"id": lid, "who": who, "text": TEXT[lid], "t0": round(t0, 3), "t1": round(t0 + len(x) / SR, 3), "words": words,
                     "wav": str((OUT / "lines" / f"{lid}.wav").relative_to(ROOT)), "tightened": cut, "ground": bool(o.get("ground"))})
    T = t0 + len(x) / SR

plate = events["plate"]
END = round(plate + 2.2, 3)
events["end"] = END
N = int(END * SR)
voices = voices[:N]

# wind: one recording, looped with long crossfades, a touch louder in the gaps between the lines
w, _ = sf.read(RAW / "wind_a.wav")
w = w.mean(axis=1) if w.ndim > 1 else w
w = hp(w[int(0.5 * SR):int(19.5 * SR)], 70)
xf = int(1.5 * SR)
loop = w.copy()
while len(loop) < N + xf:
    ramp = np.linspace(0, 1, xf)
    loop = np.concatenate([loop[:-xf], loop[-xf:] * (1 - ramp) + w[:xf] * ramp, w[xf:]])
wind = loop[:N] / (np.sqrt(np.mean(loop[:N] ** 2)) + 1e-9) * db(-33)
h = int(0.02 * SR)
talk = np.array([np.sqrt(np.mean(voices[i:i + h] ** 2)) for i in range(0, N, h)]) > db(-40)
talk = np.repeat(np.convolve(talk.astype(float), np.ones(70) / 70, "same"), h)[:N]
talk = np.pad(talk, (0, N - len(talk)))
wind *= db(1.5) - (db(1.5) - 1.0) * np.clip(talk * 1.5, 0, 1)   # +1.5 dB where nobody speaks
tw = np.arange(N) / SR                                             # gusts: the wind rises and falls, never the same twice
wind *= db(2.6 * (np.sin(tw * 1.13 + 0.4) * 0.6 + np.sin(tw * 2.71 + 2.0) * 0.3 + np.sin(tw * 0.47 + 1.1) * 0.5))
wind[:int(0.05 * SR)] *= np.linspace(0, 1, int(0.05 * SR))

fx = np.zeros(N)
# the thud when he lands, with a low push under it
th, _ = sf.read(RAW / "thud_a.wav")
th = th.mean(axis=1) if th.ndim > 1 else th
pk = int(np.argmax(np.abs(th)))
th = th[max(0, pk - int(0.05 * SR)):pk + int(0.7 * SR)]
th = fade(th / (np.abs(th).max() + 1e-9) * db(-7), 0.004, 0.25)
tt = np.arange(int(0.22 * SR)) / SR
push = np.sin(2 * np.pi * (78 - 120 * tt) * tt) * np.exp(-tt * 16) * db(-8)
t_thud = events["sink"][1] - 0.04
events["thud"] = round(t_thud, 3)
i = int(t_thud * SR)
fx[i:i + len(th)] += th
fx[i:i + len(push)] += push
# the first cut is a quick whip of the camera: a gust of the same wind, swelled and gone in a quarter of a second
g = hp(w[int(6.0 * SR):int(6.0 * SR) + int(0.26 * SR)], 350)
g = g / (np.abs(g).max() + 1e-9) * np.hanning(len(g)) ** 1.5 * db(-13)
i = int((events["cuts"][0] - 0.15) * SR)
fx[i:i + len(g)] += g
music = np.zeros(N)
ENDING = "beat" if "beat" in sys.argv else "sung"   # "sung": the sung name; "beat": two bars of the series' own track, no voice
if "bed" in sys.argv:   # the music she put on her vlog: under the talk, a little up in the gaps, and gone the moment he looks at us
    stop = next(l for l in timeline if l["id"] == "L7")["t1"] + 0.02
    BAR, GROOVE, BARS = 1.774, 7.146, 4        # the series track: four bars of its groove, looped
    raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{GROOVE}", "-t", f"{BAR * BARS}", "-i", str(ROOT / "src/film/assets/s2/music/jersey_1.mp3"), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    loop = np.frombuffer(raw, np.float32).astype(np.float64)[:int(BAR * BARS * SR)]
    loop = loop - lp(loop, 150, 2) * (1 - db(-8)) + hp(loop, 400, 2) * (db(5) - 1)   # nearly all bass as recorded: thinned below, lifted above, so that a phone plays it
    seam = int(0.004 * SR)
    loop[:seam] *= np.linspace(0, 1, seam)
    loop[-seam:] *= np.linspace(1, 0, seam)
    n = int(stop * SR)
    bed = loop[(np.arange(n) - n) % len(loop)]     # laid so that a bar ends exactly where the music stops
    band = ss.butter(4, [300, 3400], "band", fs=SR, output="sos")
    said = np.pad(talk, (0, max(0, n - len(talk))))[:n]
    v_band = ss.sosfilt(band, voices[:n])[said > 0.6]
    b_band = ss.sosfilt(band, bed)
    bed *= np.sqrt(np.mean(v_band ** 2)) / (np.sqrt(np.mean(b_band ** 2)) + 1e-9) * db(BED_DB)   # so far under the voices, where speech lives
    bed *= db(3.0) - (db(3.0) - 1.0) * np.clip(said * 1.5, 0, 1)     # +3 dB where nobody speaks
    bed[:int(0.03 * SR)] *= np.linspace(0, 1, int(0.03 * SR))
    bed[-int(0.006 * SR):] *= np.linspace(1, 0, int(0.006 * SR))     # cut, not faded
    music[:n] += bed
    events["music_stop"] = round(stop, 3)
if ENDING == "sung":
    logo, lsr = sf.read(ROOT / "src/film/assets/s2/brand/sonic_logo.wav")
    logo = logo.mean(axis=1) if logo.ndim > 1 else logo
    logo = ss.resample_poly(logo, SR, lsr) if lsr != SR else logo
    t_logo = plate + 0.12
    logo = logo * db(-3.5)
else:
    raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", "14.235", "-t", "2.6", "-i", str(ROOT / "src/film/assets/s2/music/jersey_1.mp3"), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"], capture_output=True, check=True).stdout
    logo = np.frombuffer(raw, np.float32).astype(np.float64)
    logo = fade(logo / (np.abs(logo).max() + 1e-9) * db(-9), 0.003, 0.45)
    t_logo = plate + 0.05
events["logo"] = round(t_logo, 3)
i = int(t_logo * SR)
logo = logo[:max(0, N - i)]
music[i:i + len(logo)] += logo

mix = voices + wind + fx + music
tail = int(0.3 * SR)
mix[-tail:] *= np.linspace(1, 0, tail) ** 1.5
for name, x in (("voices", voices), ("wind", wind), ("fx", fx), ("music", music), ("raw", mix)):
    sf.write(OUT / f"{name}.wav", np.stack([x, x], axis=1), SR)
subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(OUT / "raw.wav"), str(OUT / "final.wav")], check=True)
(OUT / "timeline.json").write_text(json.dumps({"lines": timeline, "events": events}, indent=1, ensure_ascii=False))
for l in timeline:
    print(f'{l["t0"]:6.2f}-{l["t1"]:6.2f}  {l["id"]:4s} {l["text"]}' + (f'   (silences cut {l["tightened"]} s)' if l["tightened"] else ""))
print("events", json.dumps(events))
print(f"length {END} s")
