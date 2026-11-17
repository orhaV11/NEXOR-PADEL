"""The soundtrack of a vlog sketch, and the timing every picture follows.

A sketch's sound file is a table and one call:

    from vlog_sound import build
    PLAN = [
        ("L1", "L", "ldn_g25p_3", 1, 0.25, {}),                 # id, who, reading, number of the line in the reading, gap before it, options
        ("T2", "T", "ldn_g25p_3", 2, 0.10, {"ground": True}),
        ("CUT", 0.10, "whip"),                                  # a cut this long after the last line ("whip": a quick whip, with a gust)
        ("HOLD", "sink", 0.65, 0.72),                           # a named stretch with no talk: starts this long after the last line, lasts this long
        ("PLATE", 0.70),                                        # the brand plate this long after the last line
    ]
    build("switch", PLAN, bed={"stop": "L7"}, fx=[{"name": "thud", "file": "thud_a", "at": ("sink", 1, -0.04), "db": -7, "push": True}])

The gaps ARE the comic timing (seconds after the previous line ends; negative = talks over): a beat needs 0.3 to 0.45,
a stare 0.75, a retort 0.05, an interruption about -0.2. Options of a line:
    "tighten": 0.16          shorten the silences inside the line to this many seconds (never inside a punchline)
    "cut_in_word": ("flip", 0.30, 0.22)   cut the line off this long into that word, fading over the last value: he is talked over
    "even": 12.0             lift a trailing-off ending by up to this many decibels
    "ground": True           spoken from the ground or from outside the frame: dulled, two decibels under his standing voice
    "db": -2.0               a private trim
Lines come from readings cut by lines.py (work/<film>/voice/lines, or the stored copy). It writes
work/<film>/sound/final.wav (mastered to -14 LUFS), the stems and timeline.json: lines with word times, and events
(cuts, whips, the named stretches, plate, end, effects, music_stop).
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
ASSETS = ROOT / "src" / "film" / "assets" / "toon"
RIG = "switch"
SR = 48000


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


def _mono(path):
    x, sr = sf.read(path)
    x = x.mean(axis=1) if x.ndim > 1 else x
    return ss.resample_poly(x, SR, sr) if sr != SR else x


def build(film, plan, bed=None, ending="sung", plate_hold=2.4, ambience=None, fx=(), bed_db=-11.0, logo_db=-5.5, lead_out=None):
    """bed: None, or {"stop": "<line id>"} (the vlog music from the start; it stops dead as that line ends) or
    {"stop": "PLATE"}. ending: "sung" (the sung name on the plate), "beat" (a bar of the series' track, for a short)
    or None. ambience: {"file": "wind_a", "db": -33, "gusts": True} (default) - a file in the film's or the rig's
    sound/raw. fx: [{"name", "file", "at": ("<event>", index, offset) or seconds, "db", "push": bool}]."""
    work, keep = ROOT / "work" / film, ASSETS / film
    lines_dir = keep / "voice" / "lines" if (keep / "voice" / "lines").exists() else work / "voice" / "lines"
    raws = [d for d in (keep / "sound" / "raw", work / "sound" / "raw", ASSETS / RIG / "sound" / "raw", ROOT / "work" / RIG / "sound" / "raw") if d.exists()]

    def raw(name):
        for d in raws:
            for ext in (".wav", ".mp3"):
                if (d / f"{name}{ext}").exists():
                    return d / f"{name}{ext}"
        raise FileNotFoundError(f"no sound called {name}")

    out = work / "sound"
    (out / "lines").mkdir(parents=True, exist_ok=True)
    takes = sorted({r[2] for r in plan if len(r) == 6})
    reads = {t: {r["n"]: r for r in json.loads((lines_dir / f"{t}.json").read_text())} for t in takes}
    T, timeline, events, used_raw = 0.0, [], {"cuts": [], "whips": []}, set()
    voices = np.zeros(SR * 90)
    for row in plan:
        if row[0] == "CUT":
            events["cuts"].append(round(T + row[1], 3))
            if len(row) > 2 and row[2] == "whip":
                events["whips"].append(round(T + row[1], 3))
            T += row[1]
            continue
        if row[0] == "HOLD":
            events[row[1]] = [round(T + row[2], 3), round(T + row[2] + row[3], 3)]
            T += row[2] + row[3]
            continue
        if row[0] == "PLATE":
            events["plate"] = round(T + row[1], 3)
            continue
        lid, who, take, n, gap, o = row
        r = reads[take][n]
        x, sr = sf.read(lines_dir / f"{take}_{n:02d}.wav")
        assert sr == SR
        words, cut = r["words"], 0.0
        if o.get("tighten"):
            x, words, cut = tighten(x, words, o["tighten"])
        if o.get("cut_in_word"):  # the line is cut off inside a word: the other one talks over it
            w, into, tail = o["cut_in_word"]
            t_cut = min(next(a for ww, a, b in words if ww.startswith(w[:3])) + into, len(x) / SR)
            x = x[:int(t_cut * SR)]
            words = [[ww, a, min(b, t_cut - tail * 0.6)] for ww, a, b in words if a < t_cut - tail * 0.6]
            x = fade(x, 0.006, tail)
        x = level(fade(x), -20.5 if who == "L" else -18.5)
        x = even(x, -27.0, o.get("even", 5.0))
        x = hp(x, 90)
        if o.get("ground"):  # spoken into the ground or from outside the frame: dull, a little under his standing voice, the consonants still there
            x = level(lp(x, 2300, 4) + lp(x, 5500, 2) * db(-11), -20.5)
        x = x * db(o.get("db", 0.0))
        t0 = T + gap
        voices[int(t0 * SR):int(t0 * SR) + len(x)] += x
        sf.write(out / "lines" / f"{lid}.wav", x, SR)
        timeline.append({"id": lid, "who": who, "text": r["text"], "t0": round(t0, 3), "t1": round(t0 + len(x) / SR, 3), "words": words,
                         "wav": str((out / "lines" / f"{lid}.wav").relative_to(ROOT)), "tightened": cut, "ground": bool(o.get("ground")), "take": take, "n": n})
        T = t0 + len(x) / SR

    plate = events.setdefault("plate", round(T + 0.7, 3))
    END = round(plate + plate_hold, 3)
    events["end"] = END
    N = int(END * SR)
    voices = voices[:N]
    h = int(0.02 * SR)
    talk = np.array([np.sqrt(np.mean(voices[i:i + h] ** 2)) for i in range(0, N, h)]) > db(-40)
    talk = np.repeat(np.convolve(talk.astype(float), np.ones(70) / 70, "same"), h)[:N]
    talk = np.pad(talk, (0, N - len(talk)))

    # the air of the place: one recording, looped with long crossfades, a touch louder between the lines, rising and falling
    amb = dict({"file": "wind_a", "db": -33.0, "gusts": True}, **(ambience or {}))
    used_raw.add(amb["file"])
    w = _mono(raw(amb["file"]))
    w = hp(w[int(0.5 * SR):min(len(w), int(19.5 * SR))], 70)
    xf = min(int(1.5 * SR), len(w) // 3)
    loop = w.copy()
    while len(loop) < N + xf:
        ramp = np.linspace(0, 1, xf)
        loop = np.concatenate([loop[:-xf], loop[-xf:] * (1 - ramp) + w[:xf] * ramp, w[xf:]])
    air = loop[:N] / (np.sqrt(np.mean(loop[:N] ** 2)) + 1e-9) * db(amb["db"])
    air *= db(1.5) - (db(1.5) - 1.0) * np.clip(talk * 1.5, 0, 1)   # +1.5 dB where nobody speaks
    if amb.get("gusts"):
        tw = np.arange(N) / SR
        air *= db(2.6 * (np.sin(tw * 1.13 + 0.4) * 0.6 + np.sin(tw * 2.71 + 2.0) * 0.3 + np.sin(tw * 0.47 + 1.1) * 0.5))
    air[:int(0.05 * SR)] *= np.linspace(0, 1, int(0.05 * SR))

    def when(at):
        if isinstance(at, (int, float)):
            return float(at)
        name, idx, off = (list(at) + [0, 0.0])[:3] if not isinstance(at, str) else (at, 0, 0.0)
        v = events[name]
        return (v[idx] if isinstance(v, list) else v) + off

    eff = np.zeros(N)
    for f in fx:
        used_raw.add(f["file"])
        x = _mono(raw(f["file"]))
        pk = int(np.argmax(np.abs(x)))
        x = x[max(0, pk - int(0.05 * SR)):pk + int(f.get("len", 0.7) * SR)]
        x = fade(x / (np.abs(x).max() + 1e-9) * db(f.get("db", -7)), 0.004, min(0.25, len(x) / SR / 3))
        t = when(f["at"])
        events[f.get("name", f["file"])] = round(t, 3)
        i = int(t * SR)
        x = x[:max(0, N - i)]
        eff[i:i + len(x)] += x
        if f.get("push"):   # a low push under a landing
            tt = np.arange(int(0.22 * SR)) / SR
            push = (np.sin(2 * np.pi * (78 - 120 * tt) * tt) * np.exp(-tt * 16) * db(-8))[:max(0, N - i)]
            eff[i:i + len(push)] += push
    for t in events["whips"]:   # a whip of the camera: a gust of the same air, swelled and gone in a quarter of a second
        g = hp(w[int(6.0 * SR) % max(1, len(w) - SR):][:int(0.26 * SR)], 350)
        g = g / (np.abs(g).max() + 1e-9) * np.hanning(len(g)) ** 1.5 * db(-13)
        i = int((t - 0.15) * SR)
        eff[i:i + len(g)] += g

    music = np.zeros(N)
    track = ROOT / "src/film/assets/s2/music/jersey_1.mp3"
    if bed:   # the music she put on her vlog: under the talk, a little up in the gaps, and gone the moment he looks at us
        stop = (plate - 0.04) if bed["stop"] == "PLATE" else next(l for l in timeline if l["id"] == bed["stop"])["t1"] + 0.02
        BAR, GROOVE, BARS = 1.774, 7.146, 4        # the series track: four bars of its groove, looped
        rawb = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{GROOVE}", "-t", f"{BAR * BARS}", "-i", str(track), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                              capture_output=True, check=True).stdout
        loopb = np.frombuffer(rawb, np.float32).astype(np.float64)[:int(BAR * BARS * SR)]
        loopb = loopb - lp(loopb, 150, 2) * (1 - db(-8)) + hp(loopb, 400, 2) * (db(5) - 1)   # nearly all bass as recorded: thinned below, lifted above, so that a phone plays it
        seam = int(0.004 * SR)
        loopb[:seam] *= np.linspace(0, 1, seam)
        loopb[-seam:] *= np.linspace(1, 0, seam)
        n = int(stop * SR)
        b = loopb[(np.arange(n) - n) % len(loopb)]     # laid so that a bar ends exactly where the music stops
        band = ss.butter(4, [300, 3400], "band", fs=SR, output="sos")
        said = np.pad(talk, (0, max(0, n - len(talk))))[:n]
        v_band = ss.sosfilt(band, voices[:n])[said > 0.6]
        b *= np.sqrt(np.mean(v_band ** 2)) / (np.sqrt(np.mean(ss.sosfilt(band, b) ** 2)) + 1e-9) * db(bed.get("db", bed_db))
        b *= db(3.0) - (db(3.0) - 1.0) * np.clip(said * 1.5, 0, 1)     # +3 dB where nobody speaks
        b[:int(0.03 * SR)] *= np.linspace(0, 1, int(0.03 * SR))
        b[-int(0.006 * SR):] *= np.linspace(1, 0, int(0.006 * SR))     # cut, not faded
        music[:n] += b
        events["music_stop"] = round(stop, 3)
    if ending == "sung":
        logo = _mono(ROOT / "src/film/assets/s2/brand/sonic_logo.wav") * db(logo_db)
        t_logo = plate + 0.12
    elif ending == "beat":
        rawb = subprocess.run(["ffmpeg", "-v", "error", "-ss", "14.235", "-t", f"{min(2.6, plate_hold + 0.2)}", "-i", str(track), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"], capture_output=True, check=True).stdout
        logo = np.frombuffer(rawb, np.float32).astype(np.float64)
        logo = fade(logo / (np.abs(logo).max() + 1e-9) * db(-9), 0.003, 0.45)
        t_logo = plate + 0.05
    else:
        logo, t_logo = np.zeros(1), plate
    events["logo"] = round(t_logo, 3)
    i = int(t_logo * SR)
    logo = logo[:max(0, N - i)]
    music[i:i + len(logo)] += logo

    mix = voices + air + eff + music
    tail = int(0.3 * SR)
    mix[-tail:] *= np.linspace(1, 0, tail) ** 1.5
    for name, x in (("voices", voices), ("wind", air), ("fx", eff), ("music", music), ("raw", mix)):
        sf.write(out / f"{name}.wav", np.stack([x, x], axis=1), SR)
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(out / "raw.wav"), str(out / "final.wav")], check=True)
    (out / "timeline.json").write_text(json.dumps({"lines": timeline, "events": events, "used": {"takes": takes, "raw": sorted(used_raw)}}, indent=1, ensure_ascii=False))
    for l in timeline:
        print(f'{l["t0"]:6.2f}-{l["t1"]:6.2f}  {l["id"]:4s} {l["text"]}' + (f'   (silences cut {l["tightened"]} s)' if l["tightened"] else ""))
    print("events", json.dumps(events))
    print(f"length {END} s")
    return events
