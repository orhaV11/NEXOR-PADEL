"""The sound of the short "Two Minutes Left" (vlog 12, "minutes"): two lines in gold afternoon light at an American
football game, a whip, and two lines in the same seats at night.

    python3 src/film/toon/ep_minutes_sound.py            the soundtrack and the timeline (work/minutes/sound)

Two things here are this film's own, done after the kit's build() because the kit has no statement for them:
word_times()  where each word really is inside its line, read from the level of the voice (the recogniser put some
              words early and ran every last word on through the silence after it; the mouths follow these times).
night()       hours pass in the whip: from the cut on, the crowd of the bought air is tired (5 dB down and dulled;
              the same recording, nothing more was bought), the mix is mastered again, and the event "night" is named.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import ROOT, SR, build, db, lp  # noqa: E402

FILM = "minutes"
A, B = "ldn_g25p_1", "ldn_g25p_2"   # two readings of the whole short. Reading 1 is clean throughout: his two lines come from it
#                                     (one tone for both: the joke), and her bright first line. Her "How long now?" is reading 2's:
#                                     the listening model heard it as the flat, hollow, worn-out one (reading 2's line 2 has a wrong
#                                     word, "This American football", and is not used). Hours lie between her two lines.

# id, who, reading, number of the line in the reading, gap before it (s after the previous line or cut), options
PLAN = [
    ("L1", "L", A, 1, 0.12, {}),                     # she starts at once; his eyes are shut behind her
    ("T2", "T", A, 2, 0.22, {}),                     # he opens his eyes on her first: a quiet warning
    ("CUT", 0.20, "whip"),                           # hours pass in a whip: the music and the sun go together
    ("L3", "L", B, 3, 0.45, {}),                     # night. She looks down for this long before she asks, without looking up
    ("T4", "T", A, 4, 0.26, {}),                     # air; then exactly as he said the first one (uncut: the punchline)
    ("PLATE", 0.92),                                 # a whistle straight after it; her eyes come up to us and stay 0.75 s
]

# where each word really is inside its line (seconds from the start of the line), read from the level of the voice,
# its low band and its hiss in steps of 20 ms
WORDS = {
    "L1": [("two", 0.14, 0.36), ("minutes", 0.36, 0.72), ("left", 0.74, 1.10), ("then", 1.36, 1.68), ("dinner", 1.70, 2.36)],
    "T2": [("its", 0.02, 0.26), ("american", 0.26, 0.78), ("football", 0.78, 1.30)],
    "L3": [("how", 0.08, 0.30), ("long", 0.30, 0.62), ("now", 0.62, 0.86)],
    "T4": [("a", 0.02, 0.10), ("minute", 0.10, 0.38), ("fifty", 0.42, 0.90)],
}


def word_times():
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    for l in tl["lines"]:
        new = WORDS[l["id"]]
        assert [w for w, _, _ in new] == [w for w, _, _ in l["words"]], (l["id"], l["words"])
        assert new[-1][2] <= l["t1"] - l["t0"] + 0.02
        l["words"] = [[w, a, b] for w, a, b in new]
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))


def night(down=-5.0, dull=1700.0, over=0.06):
    """From the cut on it is hours later: the crowd (the air stem) goes `down` dB and loses its top (a low-pass at
    `dull` Hz with a little of the original left in), crossed over `over` seconds on the cut, under the whip's gust.
    The four stems are summed again exactly as the kit sums them, and mastered again."""
    out = ROOT / "work" / FILM / "sound"
    tl = json.loads((out / "timeline.json").read_text())
    cut = tl["events"]["cuts"][0]
    stems = {name: sf.read(out / f"{name}.wav")[0] for name in ("voices", "wind", "fx", "music")}
    air = stems["wind"]
    tired = (np.stack([lp(air[:, c], dull, 2) for c in range(air.shape[1])], axis=1) * 0.8 + air * 0.2) * db(down)
    t = np.arange(len(air)) / SR
    k = np.clip((t - (cut - over / 2)) / over, 0, 1)[:, None]
    stems["wind"] = air * (1 - k) + tired * k
    sf.write(out / "wind.wav", stems["wind"], SR)
    mix = sum(stems.values())
    tail = int(0.3 * SR)
    mix[-tail:] *= (np.linspace(1, 0, tail) ** 1.5)[:, None]
    sf.write(out / "raw.wav", mix, SR)
    subprocess.run([sys.executable, str(ROOT / "src/audio/master.py"), str(out / "raw.wav"), str(out / "final.wav")], check=True)
    tl["events"]["night"] = cut
    (out / "timeline.json").write_text(json.dumps(tl, indent=1, ensure_ascii=False))
    lvl = lambda a, b: 20 * np.log10(np.sqrt(np.mean(stems["wind"][int(a * SR):int(b * SR)] ** 2)) + 1e-12)  # noqa: E731
    print(f"night from {cut} s: the crowd is at {lvl(0.5, cut - 0.3):.1f} dBFS by day and {lvl(cut + 0.3, tl['events']['plate']):.1f} dBFS at night")


if __name__ == "__main__":
    build(FILM, PLAN,
          bed={"stop": "T2", "db": -8.0},            # her vlog music stops dead as his warning ends; 1.5 dB over the kit's level: a crowd hides it more than wind does
          ambience={"file": "air", "db": -32.0, "gusts": False},   # bought: heard back as "a large crowd cheering, shouting and clapping in an open stadium-like setting"
          # the referee's whistle straight after "A minute fifty.": the clock has stopped again. From the second bought
          # whistle (the first came back as breath with no tone in it at all): 0.55 s of its steadiest stretch
          # (2.8 kHz, fluttering), far below on the field, so well under the voices.
          fx=[{"name": "whistle", "file": "whistle_b", "at": ("T4", 1, 0.06), "whole": True, "start": 2.02, "len": 0.55, "fade_in": 0.008, "fade_out": 0.14, "db": -15}],
          ending="beat", plate_hold=1.5)
    word_times()
    night()
