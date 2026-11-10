"""Cut "Operation: Away Day", the AI-shot comedy, from its Higgsfield shots (src/heist/edl.json):
download, trim, freeze and push-in, name cards and subtitles, the app screen and end card from the
campaign bumper, the heist score ducked under the dialogue, mastering to -14 LUFS, and a 4:5 crop.

    python3 src/heist/cut.py en    # ads/14-heist/venuvoy-heist-en_9x16_1080x1920.mp4 (+ _4x5_1080x1350)
    python3 src/heist/cut.py he    # the same film with Hebrew cards, subtitles, app screen and end card

The spoken lines stay in English in both versions; the Hebrew version subtitles them.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from scipy import signal as ss
from scipy.io import wavfile

SRC = Path(__file__).resolve().parent.parent
ROOT = SRC.parent
WORK = ROOT / "work" / "heist"
OUT = ROOT / "ads" / "14-heist"
LANG = sys.argv[1] if len(sys.argv) > 1 else "en"
EDL = json.load(open(SRC / "heist" / "edl.json"))
FPS, SR, W, H = EDL["fps"], 48000, 1080, 1920
CUT = WORK / f"cut-{LANG}"
CUT.mkdir(parents=True, exist_ok=True)
OUT.mkdir(parents=True, exist_ok=True)


def run(cmd):
    subprocess.run([str(c) for c in cmd], check=True)


def tr(v):
    return v[LANG] if isinstance(v, dict) else v


def fetch(name):
    s = EDL["sources"][name]
    if "path" in s:
        return ROOT / s["path"].format(lang=LANG)
    p = WORK / "clips" / f"{name}.mp4"
    if not p.exists():
        p.parent.mkdir(parents=True, exist_ok=True)
        run(["curl", "-sS", "-m", "300", "-o", p, s["url"]])
    return p


def read_audio(src, t0, dur):
    p = CUT / "tmp.wav"
    run(["ffmpeg", "-v", "error", "-y", "-ss", f"{t0:.4f}", "-t", f"{dur:.4f}", "-i", src, "-vn", "-ac", "2", "-ar", SR, "-c:a", "pcm_s16le", p])
    a = wavfile.read(p)[1].astype(float) / 32768
    n = int(round(dur * SR))
    return np.pad(a, ((0, max(0, n - len(a))), (0, 0)))[:n]


def frames_rms(x, hop=0.05):
    h = int(hop * SR)
    m = x.mean(axis=1) if x.ndim > 1 else x
    return np.array([np.sqrt(np.mean(m[i:i + h] ** 2)) for i in range(0, max(1, len(m) - h), h)]) + 1e-9


# ---------------------------------------------------------------- picture and dialogue, segment by segment
segs, t = [], 0.0
for i, sg in enumerate(EDL["segments"]):
    src = fetch(sg["src"])
    n = int(round((sg["out"] - sg["in"]) * FPS))
    hold = int(round(sg.get("hold", 0) * FPS))
    vf = []
    if "zoom" in sg:  # a slow digital push-in, worked at double size so the crop does not shimmer
        z = sg["zoom"]
        vf += [f"scale={2 * W}:{2 * H}:flags=lanczos",
               f"zoompan=z='1+({z['to']}-1)*on/{max(n - 1, 1)}':x='(iw-iw/zoom)*{z['cx']}':y='(ih-ih/zoom)*{z['cy']}':d=1:s={W}x{H}:fps={FPS}"]
    else:
        vf += [f"scale={W}:{H}:flags=lanczos", f"fps={FPS}"]
    vf += ["setsar=1"]
    if hold:
        vf.append(f"tpad=stop_mode=clone:stop_duration={hold / FPS:.4f}")
    v = CUT / f"seg{i:02d}.mp4"
    run(["ffmpeg", "-v", "error", "-y", "-ss", f"{sg['in']:.4f}", "-i", src, "-vf", ",".join(vf), "-an", "-frames:v", n + hold,
         "-c:v", "libx264", "-preset", "slow", "-crf", "14", "-pix_fmt", "yuv420p", "-r", FPS, v])
    dur = (n + hold) / FPS
    a = np.zeros((int(round(dur * SR)), 2)) if sg.get("mute") else np.pad(read_audio(src, sg["in"], n / FPS), ((0, int(round(dur * SR)) - int(round(n / FPS * SR))), (0, 0)))
    segs.append({**sg, "i": i, "t": t, "dur": dur, "video": v, "audio": a})
    t += dur
TOTAL = t
print(f"cut: {len(segs)} segments, {TOTAL:.2f} s")

# one gain per source shot, measured on the parts we use, so a line split by a jump cut keeps its level
for name in {s["src"] for s in segs if not s.get("mute")}:
    parts = [s for s in segs if s["src"] == name]
    lvl = 20 * np.log10(np.percentile(np.concatenate([frames_rms(s["audio"]) for s in parts]), 85))
    target = parts[0].get("level", -18)
    g = 10 ** (np.clip(target - lvl, -12, 18) / 20)
    for s in parts:
        s["audio"] = s["audio"] * g
    print(f"  {name}: speech level {lvl:.1f} dBFS -> {target} ({20 * np.log10(g):+.1f} dB)")

fade = int(0.008 * SR)
ramp = np.linspace(0, 1, fade)[:, None]
dialog = np.zeros((int(round(TOTAL * SR)) + SR, 2))
speech = np.zeros(len(dialog))
for s in segs:
    a = s["audio"].copy()
    a[:fade] *= ramp
    a[-fade:] *= ramp[::-1]
    i0 = int(round(s["t"] * SR))
    dialog[i0: i0 + len(a)] += a
    if s.get("speech"):  # where the music has to get out of the way: voiced frames of this segment
        b, aa = ss.butter(4, [250 / (SR / 2), 3800 / (SR / 2)], btype="band")
        vb = ss.filtfilt(b, aa, a.mean(axis=1))
        h = int(0.02 * SR)
        e = np.array([np.sqrt(np.mean(vb[k:k + h] ** 2)) for k in range(0, len(vb), h)]) + 1e-9
        on = 20 * np.log10(e) > 20 * np.log10(np.percentile(e, 90)) - 16
        rep = np.repeat(on.astype(float), h)[: len(a)]
        speech[i0: i0 + len(rep)] = rep[: len(speech) - i0]
dialog = dialog[: int(round(TOTAL * SR))]
speech = speech[: len(dialog)]

with open(CUT / "concat.txt", "w") as f:
    f.writelines(f"file '{s['video']}'\n" for s in segs)
picture = CUT / "picture.mp4"
run(["ffmpeg", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", CUT / "concat.txt", "-c", "copy", picture])


# ---------------------------------------------------------------- name cards and subtitles
def film(s, src_t):
    return s["t"] + (src_t - s["in"])


tl = {"dur": TOTAL, "lang": LANG, "cards": [], "subs": []}
for s in segs:
    if "card" in s:
        c = s["card"]
        tl["cards"].append({"t0": s["t"] + c["at"], "t1": s["t"] + c["at"] + c["len"], "name": tr(c["name"]), "what": tr(c["what"])})
    for sb in s.get("subs", []):
        tl["subs"].append({"t0": round(film(s, sb["from"]), 3), "t1": round(film(s, sb["to"]), 3), "text": tr(sb["text"])})
(CUT / "timeline.json").write_text(json.dumps(tl, ensure_ascii=False, indent=1))
ov = CUT / "ov"
run(["node", SRC / "heist" / "render-overlay.mjs", CUT / "timeline.json", ov, "--fps", FPS])
picture_ov = CUT / "picture_ov.mp4"
run(["ffmpeg", "-v", "error", "-y", "-i", picture, "-framerate", FPS, "-i", ov / "%05d.png",
     "-filter_complex", "[0:v][1:v]overlay=0:0:format=auto,format=yuv420p", "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-r", FPS, picture_ov])
# CRF 20 looks the same as 16 at 100% on this footage, at about half the size (the AI shots carry film grain)

# ---------------------------------------------------------------- the score, on the cut's own marks
marks = {"dur": round(TOTAL, 3), "cards": []}
for s in segs:
    for k, v in s.get("marks", {}).items():
        ft = round(film(s, v), 3)
        if k == "card":
            marks["cards"].append(ft)
        elif k == "slowmo_a":
            marks.setdefault("slowmo", [0, 0])[0] = ft
        elif k == "slowmo_b":
            marks.setdefault("slowmo", [0, 0])[1] = ft
        else:
            marks[k] = ft
(CUT / "marks.json").write_text(json.dumps(marks, indent=1))
print("  marks:", json.dumps(marks))
run([sys.executable, SRC / "audio" / "heist.py", CUT / "music.wav", "--marks", CUT / "marks.json"])
msr, music = wavfile.read(CUT / "music.wav")
music = ss.resample_poly(music.astype(float) / 32768, SR, msr, axis=0)
music = np.pad(music, ((0, max(0, len(dialog) - len(music))), (0, 0)))[: len(dialog)]

# duck the music under the voices: 40 ms down, 300 ms back up, 10 dB deep
env = np.zeros_like(speech)
att, rel = np.exp(-1 / (0.04 * SR)), np.exp(-1 / (0.30 * SR))
from scipy.signal import lfilter  # noqa: E402
up = lfilter([1 - att], [1, -att], speech)  # fast rise
env = np.maximum(up, lfilter([1 - rel], [1, -rel], speech))  # slow fall
duck = 1 - (1 - 10 ** (-10 / 20)) * np.clip(env * 1.4, 0, 1)
bed = music[:, :2] * 10 ** (-6 / 20) * duck[:, None]
mix = dialog + bed
for name, x in (("stem_dialog.wav", dialog), ("stem_music.wav", bed)):  # kept for the speech-over-music check
    wavfile.write(CUT / name, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))
np.save(CUT / "speech_mask.npy", speech[:: SR // 100])


# a few effects the shots did not bring along
def buzz(d=0.5):
    tt = np.arange(int(d * SR)) / SR
    y = np.sign(np.sin(2 * np.pi * 175 * tt)) * 0.3 + np.sin(2 * np.pi * 350 * tt) * 0.2
    gate = ((tt % 0.25) < 0.17).astype(float)
    return (y * gate * np.exp(-tt / 0.6))[:, None] * [1, 1]


def twang(d=0.7):
    tt = np.arange(int(d * SR)) / SR
    f = 150 * (1 + 0.35 * np.exp(-tt / 0.05))
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / 0.18) * 0.5
    return y[:, None] * [1, 1]


FX = {"buzz": buzz, "twang": twang}
for s in segs:
    for fx in s.get("sfx", []):
        y = FX[fx["type"]]()
        i0 = int(round((s["t"] + fx["at"]) * SR))
        n = min(len(y), len(mix) - i0)
        mix[i0: i0 + n] += y[:n] * 0.5

raw = CUT / "raw.wav"
wavfile.write(raw, SR, (np.clip(mix / max(1e-9, np.max(np.abs(mix))) * 0.89, -1, 1) * 32767).astype(np.int16))
final_wav = CUT / "final.wav"
run([sys.executable, SRC / "audio" / "master.py", raw, final_wav])

# ---------------------------------------------------------------- deliver
base = OUT / f"venuvoy-heist-{LANG}"
out916 = Path(f"{base}_9x16_1080x1920.mp4")
run(["ffmpeg", "-v", "error", "-y", "-i", picture_ov, "-i", final_wav, "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
     "-t", f"{TOTAL:.4f}", "-movflags", "+faststart", out916])
run(["ffmpeg", "-v", "error", "-y", "-i", out916, "-vf", "crop=1080:1350:0:250", "-c:v", "libx264", "-preset", "slow", "-crf", "20", "-pix_fmt", "yuv420p",
     "-c:a", "copy", "-movflags", "+faststart", f"{base}_4x5_1080x1350.mp4"])
print(f"film: {out916} (+ 4:5), {TOTAL:.2f} s")
