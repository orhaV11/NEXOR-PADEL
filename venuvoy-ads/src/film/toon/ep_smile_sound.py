"""The sound of the short "Too Much" (vlog 15, "smile"): four lines in one shot beside a golf fairway. Her vlog music
runs under the first three and stops dead as "Do the big one." ends; then his "big one" (a look into the lens that
changes nothing) has 0.95 s of only the air, and she calls it off.

    python3 src/film/toon/ep_smile_sound.py            the soundtrack and the timeline (work/smile/sound)

All four lines come from reading 1 (the listening model chose it for every line; every word heard back right; the
pitch puts each line in its speaker's voice). Her "Okay." is high (about 375 Hz) but no higher than her own stressed
words in line 1 of the same reading ("golf!" 428, "Timo" 390, "smile" 450): her exclamation, kept; reading 3's
"Okay. Too much." is lower but the listening model found it "too slow and disconnected".

After the fixer's pass: the first gap 0.10 and a 1.3 s plate (a short runs 6 to 10 s); the music 2.5 dB up and laid
again so that it is cut on a loud chord instead of at a bar's end (see stop_on_the_downbeat); the far clap 9 dB up
and 0.11 s earlier (it was not heard at all; a watching model now hears "applause").
"""
import subprocess
import sys
from pathlib import Path

import numpy as np
import scipy.signal as ss
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ROOT, SR, build, db, hp, lp  # noqa: E402

A = "ldn_g25p_1"
# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends; negative = talks over), options
PLAN = [
    ("L1", "L", A, 1, 0.10, {}),                 # bright, a little hushed: her open hand presents him ("Timo, smile!")
    ("T2", "T", A, 2, 0.14, {}),                 # flat, to the lens, at once
    ("L3", "L", A, 3, 0.36, {}),                 # a beat: she turns to him, coaxing
    ("HOLD", "look", 0.10, 0.85),                # the music has stopped dead; he looks into the lens: his big one. Nothing changes.
    ("L4", "L", A, 4, 0.05, {}),                 # quick, wincing: her hand flies up in front of the lens
    ("PLATE", 0.62),
]
BED_DB = -5.0          # the kit's default is -7.5: at that level the music was not heard at all in one of two askings
CUT_INTO_BAR = 0.12    # the music is cut this long after the downbeat of a bar (its chord sounds from 0.02 s)


def stop_on_the_downbeat(ev, bed_db=BED_DB, into=CUT_INTO_BAR):
    """The kit lays the vlog music so that a bar ENDS where it stops. In this groove the last eighth of the bar has
    died away by then (its hit 0.2 s before the cut, the music at -33 dB at the cut, under the air), so the stop
    sounded like a fade into the air. Here the same loop, scaled and ducked exactly as the kit does it, is laid again
    one step later, so that it is cut `into` s after the downbeat of its next bar, on a loud chord. Everything else
    of the kit's mix is kept; it is mixed and mastered again."""
    out = ROOT / "work" / "smile" / "sound"
    stems = {k: sf.read(out / f"{k}.wav")[0][:, 0] for k in ("voices", "wind", "fx", "music")}
    voices, N = stems["voices"], len(stems["voices"])
    h = int(0.02 * SR)
    talk = np.array([np.sqrt(np.mean(voices[i:i + h] ** 2)) for i in range(0, N, h)]) > db(-40)
    talk = np.repeat(np.convolve(talk.astype(float), np.ones(70) / 70, "same"), h)[:N]
    talk = np.pad(talk, (0, N - len(talk)))
    BAR, GROOVE, BARS = 1.774, 7.146, 4
    track = ROOT / "src/film/assets/s2/music/jersey_1.mp3"
    rawb = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{GROOVE}", "-t", f"{BAR * BARS}", "-i", str(track), "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                          capture_output=True, check=True).stdout
    loopb = np.frombuffer(rawb, np.float32).astype(np.float64)[:int(BAR * BARS * SR)]
    loopb = loopb - lp(loopb, 150, 2) * (1 - db(-8)) + hp(loopb, 400, 2) * (db(5) - 1)
    seam = int(0.004 * SR)
    loopb[:seam] *= np.linspace(0, 1, seam)
    loopb[-seam:] *= np.linspace(1, 0, seam)
    n = int(ev["music_stop"] * SR)
    b = loopb[(np.arange(n) - n + int(into * SR)) % len(loopb)]       # the last sample is `into` s into the loop's first bar
    band = ss.butter(4, [300, 3400], "band", fs=SR, output="sos")
    said = np.pad(talk, (0, max(0, n - len(talk))))[:n]
    v_band = ss.sosfilt(band, voices[:n])[said > 0.6]
    b *= np.sqrt(np.mean(v_band ** 2)) / (np.sqrt(np.mean(ss.sosfilt(band, b) ** 2)) + 1e-9) * db(bed_db)
    b *= db(3.0) - (db(3.0) - 1.0) * np.clip(said * 1.5, 0, 1)
    b[:int(0.03 * SR)] *= np.linspace(0, 1, int(0.03 * SR))
    b[-int(0.006 * SR):] *= np.linspace(1, 0, int(0.006 * SR))       # cut, not faded
    music = stems["music"].copy()
    music[:n] = b                                                     # before the stop the kit's music is the bed alone
    mix = voices + stems["wind"] + stems["fx"] + music
    tail = int(0.3 * SR)
    mix[-tail:] *= np.linspace(1, 0, tail) ** 1.5
    for name, x in (("music", music), ("raw", mix)):
        sf.write(out / f"{name}.wav", np.stack([x, x], axis=1), SR, subtype="FLOAT")
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(out / "raw.wav"), str(out / "final.wav")], check=True)
    print(f"music laid again: cut {into} s after a downbeat, at {ev['music_stop']} s")


if __name__ == "__main__":
    ev = build("smile", PLAN, bed={"stop": "L3", "db": BED_DB},                                 # the music stops dead as L3 ends, before his look
               ambience={"file": "air", "db": -32.0, "gusts": False},                          # bought: heard back as "gentle moving air and soft bird chirps"
               fx=[{"name": "clap", "file": "clap", "at": ("L4", 1, -0.05), "whole": True,    # far off, the gallery's polite ripple of applause (the steady
                    "start": 2.5, "len": 1.4, "db": -6, "fade_in": 0.15, "fade_out": 0.6}],   # clapping after its swell: the cheer at its start is left out),
                                                                                               # from the end of "much": 0.7 s alone before the plate's beat
               ending="beat", plate_hold=1.3)
    stop_on_the_downbeat(ev)
