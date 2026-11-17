"""The sound of the short "Less Than You" (vlog 16, "run"): four lines in one shot on a riverside path at first light.
Her vlog music runs under the first three lines and stops dead as "You hate running!" ends; his eyes come to the lens
in the silence (only the air, and a stranger's steps going past on the gravel), and he says it.

    python3 src/film/toon/ep_run_sound.py            the soundtrack and the timeline (work/run/sound)

Her two lines are from reading 2, his two from reading 1: the listening model chose exactly that (one reading per
speaker, one voice colour each). The recognisers hear her "Why's there" as "Why is there" in all three readings (the
contraction written out; nothing else differs): her caption says "Why is there", as heard.
The air, bought: heard back as "a continuous gentle breeze with birds chirping" (no water, but a still morning outdoors
with birds). The steps, bought: heard back as "steady, rhythmic footsteps crunching on gravel"; laid low, faded in and
out, in the silence after the music stops: somebody out there is running, happily.
"""
import subprocess
import sys
from pathlib import Path

import numpy as np
import scipy.signal as ss
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ROOT, SR, build, db, hp, lp  # noqa: E402

L, T = "ldn_g25p_2", "ldn_g25p_1"
# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends; negative = talks over), options
PLAN = [
    ("L1", "L", L, 1, 0.50, {}),                 # the first half second: she stares at her phone, oh. Then, alarmed, she turns to him
    ("T2", "T", T, 2, 0.16, {}),                 # calm, at once, quietly pleased
    ("L3", "L", L, 3, 0.10, {}),                 # outraged, quick
    ("HOLD", "look", 0.10, 0.62),                # the music has stopped dead; his eyes come to the lens
    ("T4", "T", T, 4, 0.06, {}),                 # flat, into the lens
    ("PLATE", 0.95),                             # she gets it, caught; the last picture holds, then the plate's beat
]
BED_DB = -5.0          # as in "smile": at the kit's -7.5 the music was not heard at all in one of two askings
CUT_INTO_BAR = 0.12    # the music is cut this long after the downbeat of a bar (its chord sounds from 0.02 s)


def stop_on_the_downbeat(ev, bed_db=BED_DB, into=CUT_INTO_BAR):
    """(From "smile".) The kit lays the vlog music so that a bar ENDS where it stops; the last eighth of that groove
    has died away by then, so the stop sounds like a fade into the air. The same loop, scaled and ducked exactly as
    the kit does it, is laid again so that it is cut `into` s after the downbeat of its next bar, on a loud chord.
    Everything else of the kit's mix is kept; it is mixed and mastered again."""
    out = ROOT / "work" / "run" / "sound"
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
    ev = build("run", PLAN, bed={"stop": "L3", "db": BED_DB},                                  # the music stops dead as L3 ends, before his look
               ambience={"file": "air", "db": -32.0, "gusts": False},
               fx=[{"name": "steps", "file": "steps", "at": ("L3", 1, 0.06), "whole": True,   # a stranger runs past on the gravel in the silence,
                    "start": 0.6, "len": 1.25, "db": -14, "fade_in": 0.18, "fade_out": 0.55}],  # gone as his line starts
               ending="beat", plate_hold=1.5)
    stop_on_the_downbeat(ev)
