"""The sound of the short "Four Percent" (vlog 10, "battery"): four lines in one shot at a stadium gate, and in the
middle of his word "phone" her phone dies: every sound stops on one frame, 0.8 s of nothing, then the plate.

    python3 src/film/toon/ep_battery_sound.py            the soundtrack and the timeline (work/battery/sound)
    python3 src/film/toon/ep_battery_sound.py --prepare  only make the mended copy of reading 2 again (see prepare)

Two things here are this film's own, because the kit has no statement for them:
prepare()  a copy of reading 2 (take "ldn_g25p_2s") in which "Four. Why?" has the breath between its two sentences
           that the script asks for, and in which the word times of her lines are set from the sound itself (the
           cutter matched "kick-off" to the heard "kick" and left "off" without a mouth).
die()      after the kit's build(): the phone dies. Voice, crowd and music are cut on the frame the picture goes
           black (the event "black" in the timeline); the crowd does not come back under the plate (the phone that
           heard it is dead); the mix is mastered again, and the silence is measured.
"""
import json
import math
import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ASSETS, ROOT, SR, build  # noqa: E402

FILM, FPS = "battery", 24
B, C, BS = "ldn_g25p_2", "ldn_g25p_3", "ldn_g25p_2s"   # two readings; BS is reading 2 mended here (see prepare)
BREATH = 0.18                                          # between "Four." and "Why?": what reading 3 leaves there by itself
# her words as the sound itself has them (seconds inside each line of reading 2; read off its loudness, 10 ms steps)
WORDS = {1: [["ten", 0.02, 0.23], ["minutes", 0.23, 0.72], ["to", 0.74, 0.86], ["kick", 0.88, 1.04], ["off", 1.08, 1.50]],
         2: [["whats", 0.02, 0.30], ["your", 0.32, 0.53], ["battery", 0.56, 1.02]],
         3: [["four", 0.04, 0.64], ["why", 0.70 + BREATH, 1.26 + BREATH]]}


def prepare(take=B, new=BS):
    """Reading 2 says "Four. Why?" with 30 ms between the two sentences: no room for her face to change from caught to
    innocent. The line gets BREATH seconds of silence at its quietest point between the two words (a true silence in
    the reading, so nothing is cut). Lines 1 and 2 are copied as they are; all three get the word times of WORDS.
    Written once into work/battery/voice/lines as the take `new`; nothing is done when it is already there (or stored)."""
    V = ROOT / "work" / FILM / "voice"
    for ext in ("wav", "json"):                         # the reading itself under the new name too: pack.py stores the readings the plan names
        if (V / f"{take}.{ext}").exists() and not (V / f"{new}.{ext}").exists():
            shutil.copy(V / f"{take}.{ext}", V / f"{new}.{ext}")
    if "--prepare" not in sys.argv and any((d / f"{new}.json").exists() for d in (ASSETS / FILM / "voice" / "lines", V / "lines")):
        return
    rows = json.loads((V / "lines" / f"{take}.json").read_text())
    out = []
    for r in rows:
        n = r["n"]
        x, sr = sf.read(V / "lines" / f"{take}_{n:02d}.wav")
        r = dict(r)
        if n == 3:
            h = int(0.01 * sr)
            lo, hi = int(0.60 * sr), int(0.74 * sr)
            q = min(range(lo, hi - h, h // 2), key=lambda i: float(np.mean(x[i:i + h] ** 2)))   # the quietest 10 ms between the two words
            cut = q + h // 2
            f = int(0.004 * sr)
            a, b = x[:cut].copy(), x[cut:].copy()
            a[-f:] *= np.linspace(1, 0, f)
            b[:f] *= np.linspace(0, 1, f)
            x = np.concatenate([a, np.zeros(int(BREATH * sr)), b])
            r["dur"], r["t1"] = round(len(x) / sr, 3), round(r["t0"] + len(x) / sr, 3)
            print(f"{new}: line 3 gets {BREATH} s of silence at {cut / sr:.3f} s (there the reading is at "
                  f"{10 * math.log10(float(np.mean(x[q:q + h] ** 2)) + 1e-12):.0f} dB)")
        if n in WORDS:
            r["words"] = WORDS[n]
        sf.write(V / "lines" / f"{new}_{n:02d}.wav", x, sr)
        out.append(r)
    (V / "lines" / f"{new}.json").write_text(json.dumps(out, indent=1))
    for o in out:
        print("  ", o["n"], o["who"], f'({o["dur"]:.2f} s)', o["text"], o["words"])


def die():
    """Her phone dies inside his word. The line was cut by the kit 0.19 s into "phone" as the recogniser timed it
    (his "f" really starts 0.07 s earlier: the cut lies in the middle of the vowel, "on that pho-"). The picture goes
    black on the last frame boundary at least 15 ms before that end, and on that same sample voice, crowd and music
    stop (2 ms, so that nothing clicks). The crowd stays dead to the end: the plate has its beat alone."""
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    last = tl["lines"][-1]
    plate = tl["events"]["plate"]
    black = math.floor((last["t1"] - 0.015) * FPS) / FPS
    i, j, f = int(round(black * SR)), int(round(plate * SR)), int(0.002 * SR)
    stems = {name: sf.read(out / f"{name}.wav")[0] for name in ("voices", "wind", "fx", "music")}
    for name, y in stems.items():
        y[i - f:i] *= np.linspace(1, 0, f)[:, None]
        y[i:(j if name == "music" else len(y))] = 0.0          # the music stem holds the plate's beat too: that stays
        sf.write(out / f"{name}.wav", y, SR)
    mix = sum(stems.values())
    tail = int(0.3 * SR)
    mix[-tail:] *= (np.linspace(1, 0, tail) ** 1.5)[:, None]
    sf.write(out / "raw.wav", mix, SR)
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(out / "raw.wav"), str(out / "final.wav")], check=True)
    y, sr = sf.read(out / "final.wav")
    assert sr == SR
    hole = np.abs(y[i:j]).max()
    before = np.sqrt(np.mean(y[i - int(0.25 * SR):i] ** 2))
    print(f"black {black:.3f} s (frame {round(black * FPS)}); the last quarter second before it is at {20 * math.log10(before + 1e-12):.1f} dBFS; "
          f"from there to the plate ({plate:.3f} s, {plate - black:.2f} s) the mastered sound peaks at {20 * math.log10(hole + 1e-12):.0f} dBFS")
    assert hole < 1e-4, "the silence is not silent"
    # the timeline the picture reads: the moment of the black, and the line as far as it is heard
    cut = round(black - last["t0"], 3)
    last["words"] = [[w, a, min(b, cut)] for w, a, b in last["words"] if a < cut]
    last["words"][-1][1] = min(last["words"][-1][1], 1.36)     # his "f" starts here (the recogniser put it 0.07 s later)
    last["words"][-2][2] = min(last["words"][-2][2], last["words"][-1][1])
    last["t1"], last["text"] = round(black, 3), "Because the tickets are on that ph—"
    tl["events"]["black"] = [round(black, 3), plate]
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))
    print(f'black {tl["events"]["black"]}; his last words: {last["words"][-3:]}')


# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends), options
PLAN = [
    ("L1", "L", BS, 1, 0.25, {}),                                    # bright, to us; he is already peering at the corner of her screen
    ("T2", "T", BS, 2, 0.18, {}),                                    # low, close behind her
    ("L3", "L", BS, 3, 0.40, {}),                                    # a beat: she looks at her own screen. "Four." small, a breath, "Why?" innocent
    ("T4", "T", C, 4, 0.30, {"cut_in_word": ("phone", 0.19, 0.004)}),  # a beat, then slow and patient; cut dead inside "phone" (uncut otherwise: the punchline)
    ("PLATE", 0.80),                                                 # black and silent this long; then the plate with its beat
]
if __name__ == "__main__":
    prepare()
    if "--prepare" not in sys.argv:
        # Her vlog music would run to the plate: die() cuts it in the middle of a bar. It is 1.5 dB over the kit's level and
        # the crowd 2 dB under the first mix: at the kit's level the watching model heard no music under the talk (the
        # babble of a crowd hides it more than wind does), and here the music must be heard to die.
        build(FILM, PLAN, bed={"stop": "PLATE", "db": -9.5},
              ambience={"file": "crowd", "db": -33.0, "gusts": False},           # bought: heard back as "the continuous ambient chatter of a crowd, indistinct voices"
              ending="beat", plate_hold=1.5)
        die()
