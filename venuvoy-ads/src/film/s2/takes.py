"""Look at every take of every shot before one is chosen.

    python3 src/film/s2/takes.py <plan.json> [id ...] [--watch]

For each take of each shot (clips/<id>_t<k>.mp4 in the plan's "out" folder) this writes a frame strip with times
(review/<id>.jpg: one row per take) and prints what a person choosing a take needs to know:

  words     what is really said, with times (two recognisers; where they disagree, look closer)
  cuts      a cut the model made inside the clip (it happens in a third of the clips)
  zoom      how much the camera moved in or out between the first and the last frame, measured on the background
            (1.00 = locked off; under 0.93 or over 1.07 the model pulled the camera, whatever the text asked)
  shift     how far the background slid sideways or up and down, in pixels of a 1080 x 1920 frame
  pitch     the middle pitch of the voice in Hz (Timo speaks around 130-180, Luna around 205-335: a take far outside
            is another voice)
  level     how loud the quiet parts and the loud parts are (dBFS)

--watch also asks the watching model (about half a cent a take) to say what happens and what looks or sounds wrong.
Everything is also written to review/review.json. Nothing is chosen here: the choice goes into the edit list.
"""
import json
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import cv2
import numpy as np

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE.parent))
import asr  # noqa: E402

args = [a for a in sys.argv[1:] if not a.startswith("--")]
plan = json.load(open(args[0]))
ONLY = set(args[1:])
OUT = ROOT / plan["out"]
REV = OUT / "review"
REV.mkdir(parents=True, exist_ok=True)
WATCH = "--watch" in sys.argv


def frame(path, t):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-ss", f"{t:.3f}", "-i", str(path), "-frames:v", "1", "-vf", "scale=540:960",
                          "-f", "rawvideo", "-pix_fmt", "gray", "-"], capture_output=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(960, 540) if len(raw) == 960 * 540 else None


def duration(path):
    return float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(path)],
                                capture_output=True, text=True).stdout or 0)


def camera(path, dur):
    """Scale and shift of the background between the first and the last frame (ORB points, a similarity fit)."""
    a, b = frame(path, 0.08), frame(path, max(0.1, dur - 0.12))
    if a is None or b is None:
        return None, None
    orb = cv2.ORB_create(3000)
    ka, da = orb.detectAndCompute(a, None)
    kb, db_ = orb.detectAndCompute(b, None)
    if da is None or db_ is None:
        return None, None
    m = cv2.BFMatcher(cv2.NORM_HAMMING, crossCheck=True).match(da, db_)
    if len(m) < 30:
        return None, None
    pa = np.float32([ka[x.queryIdx].pt for x in m])
    pb = np.float32([kb[x.trainIdx].pt for x in m])
    M, inl = cv2.estimateAffinePartial2D(pa, pb, method=cv2.RANSAC, ransacReprojThreshold=3.0)
    if M is None:
        return None, None
    scale = float(np.hypot(M[0, 0], M[1, 0]))
    centre = np.float32([270, 480])
    moved = M[:, :2] @ centre + M[:, 2] - centre
    return round(scale, 3), [round(float(moved[0]) * 2), round(float(moved[1]) * 2)]


def cuts(path):
    err = subprocess.run(["ffmpeg", "-i", str(path), "-vf", "select='gt(scene,0.3)',showinfo", "-f", "null", "-"],
                         capture_output=True, text=True).stderr
    return [round(float(x.split("pts_time:")[1].split()[0]), 2) for x in err.splitlines() if "pts_time:" in x]


def sound(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-vn", "-ac", "1", "-ar", "16000", "-f", "f32le", "-"],
                         capture_output=True).stdout
    x = np.frombuffer(raw, np.float32)
    if len(x) < 4000:
        return None, None, None
    h = 800
    lv = np.array([20 * np.log10(np.sqrt(np.mean(x[i:i + h] ** 2)) + 1e-9) for i in range(0, len(x) - h, h)])
    f0 = []
    for i in range(0, len(x) - 640, 320):  # autocorrelation pitch on the loud frames
        w = x[i:i + 640]
        if np.sqrt(np.mean(w ** 2)) < 0.03:
            continue
        w = w - w.mean()
        ac = np.correlate(w, w, "full")[639:]
        lo, hi = 16000 // 420, 16000 // 85
        k = lo + int(np.argmax(ac[lo:hi]))
        if ac[k] > 0.45 * ac[0]:
            f0.append(16000 / k)
    return (round(float(np.median(f0))) if len(f0) > 6 else None), round(float(np.percentile(lv, 10))), round(float(np.percentile(lv, 95)))


def strip(path, dur, label, dst):
    n = 10
    step = max(0.05, (dur - 0.1) / (n - 1))
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(path), "-vf",
                    f"fps={1 / step:.4f},scale=216:384,drawtext=text='%{{pts\\:hms}}':fontcolor=yellow:fontsize=18:x=4:y=4:box=1:boxcolor=black@0.4,"
                    f"drawtext=text='{label}':fontcolor=white:fontsize=18:x=4:y=360:box=1:boxcolor=black@0.5,tile={n}x1", "-frames:v", "1", str(dst)], check=True)


def review(job):
    sid, k, path = job
    dur = duration(path)
    tmp = REV / f"_{sid}_t{k}.jpg"
    strip(path, dur, f"{sid} take {k}", tmp)
    words, text = asr.words(str(path))
    scale, shift = camera(path, dur)
    pitch, quiet, loud = sound(path)
    r = {"shot": sid, "take": k, "file": str(path.relative_to(ROOT)), "dur": round(dur, 2), "words": words, "text": text,
         "cuts": cuts(path), "zoom": scale, "shift": shift, "pitch": pitch, "quiet": quiet, "loud": loud, "strip": str(tmp)}
    if WATCH:
        import watch
        r["watch"] = watch.ask(str(path), "Describe second by second what happens in this clip and transcribe exactly what is said and by whom. "
                               "Then list anything that looks broken, morphing or unnatural (hands, faces, objects appearing or vanishing, a camera that moves), "
                               "and anything in the sound that is wrong (a voice that sounds robotic, extra words, music). Be brief and concrete.")
    return r


jobs = []
for s in plan["shots"]:
    if ONLY and s["id"] not in ONLY:
        continue
    k = 1
    while (OUT / "clips" / f'{s["id"]}_t{k}.mp4').exists():
        jobs.append((s["id"], k, OUT / "clips" / f'{s["id"]}_t{k}.mp4'))
        k += 1
with ThreadPoolExecutor(3) as ex:
    out = list(ex.map(review, jobs))
old = json.loads((REV / "review.json").read_text()) if (REV / "review.json").exists() else []
keep = [r for r in old if (r["shot"], r["take"]) not in {(o["shot"], o["take"]) for o in out}]
(REV / "review.json").write_text(json.dumps(sorted(keep + out, key=lambda r: (r["shot"], r["take"])), ensure_ascii=False, indent=1))
for sid in dict.fromkeys(r["shot"] for r in out):
    rows = [r for r in out if r["shot"] == sid]
    ins = sum((["-i", r["strip"]] for r in rows), [])
    dst = REV / f"{sid}.jpg"
    if len(rows) > 1:
        subprocess.run(["ffmpeg", "-v", "error", "-y"] + ins + ["-filter_complex", f"vstack=inputs={len(rows)}", str(dst)], check=True)
    else:
        Path(rows[0]["strip"]).replace(dst)
    for r in rows:
        Path(r["strip"]).unlink(missing_ok=True)
        flag = []
        if r["cuts"]:
            flag.append(f"CUT at {r['cuts']}")
        if r["zoom"] is not None and not 0.93 <= r["zoom"] <= 1.07:
            flag.append("CAMERA MOVED")
        said = " ".join(w for w, _, _ in r["words"])
        t0 = r["words"][0][1] if r["words"] else None
        t1 = r["words"][-1][2] if r["words"] else None
        print(f'{sid} take {r["take"]}: {r["dur"]} s | zoom {r["zoom"]} shift {r["shift"]} | pitch {r["pitch"]} Hz | quiet {r["quiet"]} loud {r["loud"]} dB | '
              f'"{said}" ({t0}-{t1}) | second opinion: "{r["text"]}"' + (" | " + "; ".join(flag) if flag else ""))
        if WATCH:
            print("   watcher:", " ".join(r["watch"].split())[:700])
    print(f"   strip: {dst}")
