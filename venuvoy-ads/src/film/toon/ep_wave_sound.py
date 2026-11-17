"""The sound of the short "They Were Early" (vlog 14, "wave"): the top of a ballpark's upper deck on a bright
afternoon. The wave comes round the stadium as a roar that rises, passes right through them on her "Wooo!" and dies
away; Timo, who stayed sitting, stands up for it after it has gone.

    python3 src/film/toon/ep_wave_sound.py            the soundtrack and the timeline (work/wave/sound)

Two things here are this film's own, done round the kit's build() because the kit has no statement for them:
  duck_music()  the vlog music 9 dB lower under her first line, back up as the roar dies (it is heard while he rises).
  crowd_wave()  the wave as a travelling roar. The bought swell ("swell", heard back as crowd chatter that swells into
                cheering) is loudest only in its last second and ends there, so it cannot be laid as recorded: its last
                three seconds are laid under her line with a rising gain, so that the roar peaks on "Wooo!"; then a
                cheering stretch of the same file is laid on, losing its top as it fades (the wave moving away round the
                bowl), and gone before Timo stands up.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ROOT, SR, build, db, fade, hp, lp  # noqa: E402

FILM = "wave"
OUT = ROOT / "work" / FILM / "sound"
B, C = "ldn_g25p_2", "ldn_g25p_3"   # her two lines from one reading (C, clean throughout); his from B (the listening model's pick)

# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends), options
PLAN = [
    ("L1", "L", C, 1, 0.15, {"cut_in_word": ("woo", 1.05, 0.35)}),   # "The wave! Here it comes! Wooo!"   the roar rises under it and peaks on
                                          # "Wooo!"; her held "Wooo" (1.8 s as read) is cut 1.05 s in and faded over 0.35 s under the roar:
                                          # the crest has gone past the lens by then, and she must not cheer at an empty crowd
    ("HOLD", "rise", -0.20, 3.0),         # Timo comes up, slowly, eyes shut. The kit's rise is a smoothstep from 2100 px down: over 3.0 s he
                                          # is below the frame for the first ~0.9 s (it starts under the fading "Wooo", so he is first seen
                                          # about half a second after her hand has gone and the roar has died), then in view for ~2 s, at
                                          # about 1,000 px/s at most and slowing into his place, so that his
                                          # face is seen as her hair and scarf pass in front of it (at 1.7 s it was a 0.6 s pop)
    ("L2", "L", C, 2, 0.50, {"db": 1.0}), # he holds, alone, at full height; she turns to him: "It's gone."   (the music stops dead as she ends;
                                          # 1 dB up so that the gentle line sits 5 dB over the music, which is not dipped before its stop)
    ("T3", "T", B, 3, 0.50, {}),          # a beat; his eyes open on her: "They were early."
    ("PLATE", 0.85),                      # his eyes come to us for a stare of 0.75 s; then the plate
]


def _src(name):
    return next(q for q in (ROOT / "src/film/assets/toon" / FILM / "sound" / "raw" / f"{name}.wav", OUT / "raw" / f"{name}.wav") if q.exists())


def duck_music(depth=-9.0, back=0.7):
    """The vlog music sits `depth` dB lower under her first line (the wave drowns it: under her shout its drum hits were
    as loud as her words) and comes back up over `back` s as the line ends and the roar dies, so that it is heard on its
    own while he rises and its dead stop after "It's gone." is a beat. Written into the music stem and the mix."""
    tl = json.loads((OUT / "timeline.json").read_text())
    L1 = next(l for l in tl["lines"] if l["id"] == "L1")
    mix, _ = sf.read(OUT / "raw.wav")
    mus, _ = sf.read(OUT / "music.wav")
    t = np.arange(len(mus)) / SR
    g = np.interp(t, [0.0, L1["t1"] - 0.1, L1["t1"] - 0.1 + back], [db(depth), db(depth), 1.0])
    mix += mus * (g - 1)[:, None]
    sf.write(OUT / "raw.wav", mix, SR)
    sf.write(OUT / "music.wav", mus * g[:, None], SR)


def crowd_wave(ev, peak_rms=-19.5):
    """Lay the wave's roar into the mix and the effects stem, master again, and name it in the timeline."""
    tl = json.loads((OUT / "timeline.json").read_text())
    L1 = next(l for l in tl["lines"] if l["id"] == "L1")
    woo = L1["t0"] + next(a for w, a, b in L1["words"] if w.lower().startswith("woo"))
    x, sr = sf.read(_src("swell"))
    assert sr == SR
    x = hp(x, 120)
    P = woo + 0.10                                   # the loudest moment: a beat into her "Wooo!"
    loud = 5.35                                      # the middle of the loudest second of the bought swell (5.0-5.7 s)
    a0 = max(0.0, loud - P)                          # the film's first instant in the file
    near = x[int(a0 * SR):]                          # coming closer: the rest of the file, laid from the film's start
    t_near = np.arange(len(near)) / SR
    g = np.interp(t_near, [0.0, P - 0.55, P - 0.05, 99.0], [db(-2.0), db(-1.0), 1.0, 1.0])     # it comes closer and closer (the file itself rises 9 dB)
    near = near * g
    away = x[int(0.10 * SR):int(2.10 * SR)]          # going away: a cheering stretch of the same crowd, its top taken off as it fades
    rms = lambda y: np.sqrt(np.mean(y ** 2)) + 1e-9   # noqa: E731
    away = away * rms(near[-int(0.3 * SR):]) / rms(away[:int(0.3 * SR)])   # as loud as the roar where it takes over
    t_away = np.arange(len(away)) / SR
    w = np.clip(t_away / 1.4, 0, 1)
    away = away * (1 - w) + lp(away, 900, 2) * w
    away = away * np.interp(t_away, [0.0, 0.35, 1.0, 1.6], [1.0, db(-3.0), db(-14.0), db(-34.0)])
    j = len(near) - int(0.25 * SR)                   # joined with a quarter-second crossfade where the file ends
    ramp = np.linspace(0, 1, int(0.25 * SR))
    roar = np.zeros(j + len(away))
    roar[:len(near)] += near
    roar[j:j + len(ramp)] *= 1 - ramp
    roar[j:j + len(ramp)] += away[:len(ramp)] * ramp
    roar[j + len(ramp):] += away[len(ramp):]
    roar = fade(roar, 0.4, 0.3)
    h = int(0.1 * SR)
    r = np.array([np.sqrt(np.mean(roar[i:i + h] ** 2)) for i in range(0, len(roar) - h, h)])
    roar *= db(peak_rms) / (r.max() + 1e-9)          # its loudest tenth of a second sits at peak_rms
    mix, _ = sf.read(OUT / "raw.wav")
    fx, _ = sf.read(OUT / "fx.wav")
    roar = roar[:len(mix)]
    for y in (mix, fx):
        y[:len(roar)] += roar[:, None]
    sf.write(OUT / "raw.wav", mix, SR)
    sf.write(OUT / "fx.wav", fx, SR)
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(OUT / "raw.wav"), str(OUT / "final.wav")], check=True)
    end = len(roar) / SR
    tl["events"]["wave"] = [0.0, round(end, 3)]
    tl["events"]["wave_peak"] = round(P, 3)
    tl["used"]["raw"] = sorted(set(tl["used"]["raw"]) | {"swell"})
    (OUT / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))
    env = " ".join(f"{20 * np.log10(v + 1e-9):.0f}" for v in r[::2])
    print(f"wave: peak {P:.2f} s, gone by {end:.2f} s; level every 0.2 s (before trim): {env}")


if __name__ == "__main__":     # the vlog music sits 5.5 dB under the kit's level here: with the roar on top it buried her first line
    ev = build(FILM, PLAN, bed={"stop": "L2", "db": -13.0}, ending="beat", plate_hold=1.5, ambience={"file": "air", "db": -32.0, "gusts": False})
    duck_music()
    crowd_wave(ev, peak_rms=-21.0)
