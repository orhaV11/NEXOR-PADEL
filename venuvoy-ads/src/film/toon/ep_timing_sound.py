"""The sound of the short "Four Minutes Behind" (vlog 17, "timing"): five lines in one shot on a sunny station
platform. Her vlog music runs under the first three and stops dead as "Why? What happened?" ends; his eyes come to the
lens with the stop; "This video."; a held beat in which she comes round to us, caught, and his eyes go down to the
stopwatch on his chest: it clicks; his eyes come back up: "Five now."

    python3 src/film/toon/ep_timing_sound.py            the soundtrack and the timeline (work/timing/sound)

The takes: every word of all three readings was heard back right and every line matched. Her two lines are from
reading 2 (the listening model's pick for her first line; her pitch 250 / 205 Hz). His three are from reading 1: the
listening model preferred reading 2 for him, but reading 2 is slow throughout (his lines 3.85 s against 2.74 s) and with
it the short ran about 11.5 s with the plate, over the 11 s a short may run; reading 1 has no wrong word, his pitch is
109-125 Hz, and each speaker stays on one reading.

Three things here are this film's own, done round the kit's build():
  the air      two bought platform airs were both heard back as "blowing air / wind" (no murmur, wheels or pigeons
               could be made out). The second ("air2") has a lower, rounder rumble that swells and recedes, but it fades
               away over its last 2.5 s, which the kit's loop would lay under the punchline: its steady first 7.2 s are
               kept as "air_platform".
  the click    the bought "stopwatch click" came back as a sustained 3.2 kHz chirping (heard as "rapid chirping or
               squeaking"), with no single click in it. The click is made here instead (made_click): a sharp broadband
               tick with a short metallic ring at 2.6, 4.2 and 6.9 kHz, and a fainter second tick of the mechanism 45 ms
               later, laid as a hit while his eyes are on the stopwatch.
  the stop     the music is laid again so that it is cut on a loud chord just after a downbeat (stop_on_the_downbeat, as
               in "smile"): the kit lays it so that a bar ENDS at the stop, where this groove has died away, and the stop
               then sounds like a fade.
"""
import subprocess
import sys
from pathlib import Path

import numpy as np
import scipy.signal as ss
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ROOT, SR, build, db, hp, lp  # noqa: E402

FILM = "timing"
RAW = ROOT / "work" / FILM / "sound" / "raw"
B, A = "ldn_g25p_2", "ldn_g25p_1"          # her lines from reading 2, his from reading 1
# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends; negative = talks over), options
PLAN = [
    ("L1", "L", B, 1, 0.08, {}),                   # bright, proud of him, to the camera: "Travel day! Timo's timing every minute."
    ("T2", "T", A, 2, 0.14, {}),                   # grave, at once: "We're four minutes behind."
    ("L3", "L", B, 3, 0.10, {"tighten": 0.14}),    # worried, turned to him: "Why? What happened?"   (the music stops dead as it ends)
    ("T4", "T", A, 4, 0.42, {}),                   # his eyes are on us from the stop: a stare, then flat: "This video."
    ("HOLD", "glance", 0.15, 0.50),                # she comes round to us, caught; his eyes go down to the stopwatch (it clicks) and come back
    ("T5", "T", A, 5, 0.14, {}),                   # flat, into the lens: "Five now."
    ("PLATE", 0.60),                               # the punchline has its air before the plate
]
BED_DB = -5.0          # the kit's default (-7.5) was not heard at all in "smile" in one of two askings
CUT_INTO_BAR = 0.12    # the music is cut this long after the downbeat of a bar (its chord sounds from 0.02 s)
AIR = {"file": "air_platform", "db": -31.0, "gusts": False}
CLICK = {"name": "click", "file": "made_click", "at": ("glance", 0, 0.24), "db": -9}


def made_click():
    """A mechanical stopwatch click: a 1 ms broadband tick with a short ring of three inharmonic partials, then a
    fainter tick of the mechanism. Written once into the film's own sounds."""
    p = RAW / "made_click.wav"
    if p.exists():
        return
    rng = np.random.default_rng(17)
    n = int(0.22 * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for at, gain, freqs in ((0.010, 1.0, (2650.0, 4180.0, 6930.0)), (0.055, 0.32, (2900.0, 4520.0, 7400.0))):
        i = int(at * SR)
        tt = t[: n - i]
        tick = hp(rng.standard_normal(len(tt)), 1800, 2) * np.exp(-tt / 0.0009)
        ring = sum(a * np.sin(2 * np.pi * f * tt + ph) * np.exp(-tt / tau)
                   for f, a, tau, ph in zip(freqs, (0.55, 0.35, 0.22), (0.0045, 0.0032, 0.0022), (0.3, 1.1, 2.0)))
        body = 0.25 * np.sin(2 * np.pi * 950 * tt) * np.exp(-tt / 0.0025)
        out[i:] += gain * (tick * 0.8 + ring + body)
    out = out / np.abs(out).max() * 0.9
    RAW.mkdir(parents=True, exist_ok=True)
    sf.write(p, out, SR)
    print("made", p.name)


def platform_air():
    """The steady first 7.2 s of the second bought air (its last 2.5 s fade away)."""
    p = RAW / "air_platform.wav"
    if p.exists():
        return
    x, sr = sf.read(RAW / "air2.wav")
    y = x[: int(7.2 * sr)].copy()
    k = int(0.05 * sr)
    y[-k:] *= np.linspace(1, 0, k)
    sf.write(p, y, sr)
    print("made", p.name)


def stop_on_the_downbeat(ev, bed_db=BED_DB, into=CUT_INTO_BAR):
    """The same loop, scaled and ducked exactly as the kit does it, laid again one step later, so that it is cut `into`
    s after the downbeat of its next bar, on a loud chord. Everything else of the kit's mix is kept; mixed and mastered
    again. (The method of ep_smile_sound.py.)"""
    out = ROOT / "work" / FILM / "sound"
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
    made_click()
    platform_air()
    ev = build(FILM, PLAN, bed={"stop": "L3", "db": BED_DB},        # the music stops dead as L3 ends: his eyes come to the lens
               ambience=AIR, fx=[CLICK], ending="beat", plate_hold=1.5)
    stop_on_the_downbeat(ev)
