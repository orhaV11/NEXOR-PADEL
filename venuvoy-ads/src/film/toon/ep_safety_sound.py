"""The sound of the sketch "Safety Car" (vlog 08, "safety"): ten lines in the front row of a grandstand at a motor
racing circuit on a windy day. A racing car screams past in the first second; her vlog music runs until "So he can't
lose!" and stops dead; Timo looks at us, works it out, finds no hole in it and changes sides; a lower car passes.

    python3 src/film/toon/ep_safety_sound.py            the soundtrack and the timeline (work/safety/sound)

The lines: all of hers and his from reading 1 (clean throughout: every word heard back right), except two from
reading 2: his "That's the safety car.", which reading 1 gave in a woman's voice (pitch 235 Hz, no frame under 165 Hz,
its spectrum nearer hers; reading 2's is his, 150-180 Hz), and her first line, whose "I've" reading 1 swallows (20 dB
under the rest of the line; the watching model heard "...picked a driver!"); reading 2's is 11 dB louder there, and
the listening model chose it too.
The one bought sound, "pass" (asked for: a single racing car passing close and fast; heard back: "the loud,
pitch-rising roar of a race car engine zooming past", with a brief high cheer in its first second, which is never
used): laid twice. At the start as it is (from 2.35 s of the file, where the engine is already loud; its pitch drops
as it passes 0.15 s later, when the streak is in the middle of the frame), and at the end as "pass_low", the same recording played a quarter slower (lower and
heavier: another car), made here from it, nothing bought.
The music is laid again after the kit's build (stop_on_a_chord): it is cut on a chord, not on the dying end of a bar.
"""
import subprocess
import sys
from pathlib import Path

import numpy as np
import scipy.signal as ss
import soundfile as sf
from scipy.signal import resample_poly

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ASSETS, ROOT, SR, build, db, hp, lp  # noqa: E402

FILM = "safety"
A, B = "ldn_g25p_1", "ldn_g25p_2"


def pass_low():
    """The bought pass played 0.8 times as fast: a lower, heavier engine (48 kHz kept, so it is longer and lower)."""
    out = ROOT / "work" / FILM / "sound" / "raw" / "pass_low.wav"
    if out.exists() or (ASSETS / FILM / "sound" / "raw" / "pass_low.wav").exists():
        return
    src = next(p for p in (ROOT / "work" / FILM / "sound" / "raw" / "pass.wav", ASSETS / FILM / "sound" / "raw" / "pass.wav") if p.exists())
    x, sr = sf.read(src)
    y = resample_poly(x, 5, 4)                     # five samples for every four, played at the same rate: 0.8 times as fast
    out.parent.mkdir(parents=True, exist_ok=True)
    sf.write(out, y.astype(np.float32), sr)


def stop_on_a_chord(ev, into=0.12, bed_db=-7.5):
    """The kit lays the vlog music so that a bar ENDS where it stops; in this groove the last eighth of a bar has died
    away by then (here -31 dB, then -34 dB, in the last 0.1 s before the stop), so "stops dead" would sound like a
    fade into the wind. The same loop, scaled and ducked exactly as the kit does it, is laid again one step later, so
    that it is cut `into` s after the downbeat of a bar, on its chord. The rest of the kit's mix is kept; mastered again."""
    out = ROOT / "work" / FILM / "sound"
    stems = {k: sf.read(out / f"{k}.wav")[0][:, 0] for k in ("voices", "wind", "fx", "music")}
    voices, N = stems["voices"], len(stems["voices"])
    h = int(0.02 * SR)
    talk = np.array([np.sqrt(np.mean(voices[i:i + h] ** 2)) for i in range(0, N, h)]) > db(-40)
    talk = np.repeat(np.convolve(talk.astype(float), np.ones(70) / 70, "same"), h)[:N]
    talk = np.pad(talk, (0, N - len(talk)))
    BAR, GROOVE, BARS = 1.774, 7.146, 4                                  # as the kit: four bars of the series track's groove
    track = ROOT / "src/film/assets/s2/music/jersey_1.mp3"
    rawb = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{GROOVE}", "-t", f"{BAR * BARS}", "-i", str(track), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                          capture_output=True, check=True).stdout
    loopb = np.frombuffer(rawb, np.float32).astype(np.float64)[:int(BAR * BARS * SR)]
    loopb = loopb - lp(loopb, 150, 2) * (1 - db(-8)) + hp(loopb, 400, 2) * (db(5) - 1)
    seam = int(0.004 * SR)
    loopb[:seam] *= np.linspace(0, 1, seam)
    loopb[-seam:] *= np.linspace(1, 0, seam)
    n = int(ev["music_stop"] * SR)
    b = loopb[(np.arange(n) - n + int(into * SR)) % len(loopb)]          # the last sample is `into` s after a downbeat
    band = ss.butter(4, [300, 3400], "band", fs=SR, output="sos")
    said = np.pad(talk, (0, max(0, n - len(talk))))[:n]
    v_band = ss.sosfilt(band, voices[:n])[said > 0.6]
    b *= np.sqrt(np.mean(v_band ** 2)) / (np.sqrt(np.mean(ss.sosfilt(band, b) ** 2)) + 1e-9) * db(bed_db)
    b *= db(3.0) - (db(3.0) - 1.0) * np.clip(said * 1.5, 0, 1)
    b[:int(0.03 * SR)] *= np.linspace(0, 1, int(0.03 * SR))
    b[-int(0.006 * SR):] *= np.linspace(1, 0, int(0.006 * SR))           # cut, not faded
    music = stems["music"].copy()
    music[:n] = b                                                         # before the stop the kit's music is the bed alone
    mix = voices + stems["wind"] + stems["fx"] + music
    tail = int(0.3 * SR)
    mix[-tail:] *= np.linspace(1, 0, tail) ** 1.5
    for name, x in (("music", music), ("raw", mix)):
        sf.write(out / f"{name}.wav", np.stack([x, x], axis=1), SR, subtype="FLOAT")
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(out / "raw.wav"), str(out / "final.wav")], check=True)
    print(f"music laid again: cut {into} s after a downbeat, at {ev['music_stop']} s")


# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends; negative = talks over), options
PLAN = [
    ("L1", "L", B, 1, 0.45, {}),                 # the car tears past first; she shouts over its going, delighted (reading 2: reading 1 swallows "I've", 20 dB under the rest, and the watching model heard it cut off)
    ("T2", "T", A, 2, 0.14, {}),                 # flat, already suspicious
    ("L3", "L", A, 3, 0.16, {}),                 # proud, to us: the flag waves on "winning"
    ("CUT", 0.30),                               # a jump cut, closer
    ("T4", "T", B, 4, 0.06, {}),                 # he leans to her: dry, patient
    ("L5", "L", A, 5, 0.10, {}),                 # unbothered, at once
    ("T6", "T", A, 6, 0.30, {}),                 # a beat: slow and kind
    ("L7", "L", A, 7, 0.16, {}),                 # she turns to him, honestly puzzled
    ("CUT", 0.25),                               # a jump cut, closer again: both faces
    ("T8", "T", A, 8, 0.06, {}),                 # flat
    ("L9", "L", A, 9, 0.30, {}),                 # a beat while it sinks in: amazed. The music stops dead as it ends
    ("HOLD", "look", 0.00, 0.75),                # he looks at us; she turns back to us and the flag comes up
    ("HOLD", "narrow", 0.00, 0.45),              # one brow down: he works it out and finds no hole in it
    ("T10", "T", A, 10, 0.06, {}),               # to us, flat: he changes sides
    ("HOLD", "pass", 0.18, 0.55),                # a car goes by behind them; she waves the flag
    ("PLATE", 0.40),
]
if __name__ == "__main__":
    pass_low()
    ev = build(FILM, PLAN, bed={"stop": "L9"},                                                    # the vlog music stops dead as L9 ends (he looks at us)
          fx=[{"name": "pass1", "file": "pass", "at": 0.0, "whole": True, "start": 2.35, "len": 1.65, "fade_in": 0.02, "fade_out": 1.0, "db": -11},
              {"name": "pass2", "file": "pass_low", "at": ("pass", 0, -0.575), "whole": True, "start": 2.3, "len": 1.9, "fade_in": 0.30, "fade_out": 1.0, "db": -17}])
    stop_on_a_chord(ev, bed_db=-6.0)               # 1.5 dB over the kit's level: at -7.5 the watching model heard no stop at all
