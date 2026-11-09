"""15-second cutdowns of the flagship reels, for paid placements where the first seconds carry the ad.

    python3 src/cutdown.py en      # ads/08-reel-en/venuvoy-reel-en-15s_9x16_1080x1920.mp4
    python3 src/cutdown.py he      # ads/07-reel-he/venuvoy-reel-he-15s_9x16_1080x1920.mp4
    python3 src/cutdown.py anthem en   # ads/10-anthem/venuvoy-anthem-en-15s_9x16_1080x1920.mp4 (+ 4:5)

The picture is cut from the full reel with video-use's render.py (frame-accurate extract per range,
30 ms edge fades, lossless concat). Ranges start and end on the reel's own beats, and every range
keeps its payoff on screen for at least a second. The sound is not spliced from the long mix: the
music is written for 15 s (compose.py --form), the effects sit on the cut's own timeline, and the
mix is mastered once (master.py) before it is muxed over the cut picture.

render.py is found through $VIDEO_USE (the video-use checkout) or ~/.claude/skills/video-use.
"""
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

SRC = Path(__file__).resolve().parent
ADS = SRC.parent / "ads"
VIDEO_USE = Path(os.environ.get("VIDEO_USE", Path.home() / ".claude/skills/video-use"))

# (start, end) in the full reel, and what each range is for. Total 15.0 s; cuts land at 2, 3.5, 6.5, 9, 12.
RANGES = [
    (0.0, 2.0, "hook: the pile of open questions and the tab count"),
    (2.0, 3.5, "brand: crest, wordmark, the line"),
    (6.0, 9.0, "one sentence in, real plans out, push-in on the checked source"),
    (12.5, 15.0, "tickets: a lookalike link flagged, the official one confirmed"),
    (18.0, 21.0, "kick-off moved, handled: the alert and the replacement"),
    (31.0, 34.0, "end card: the call to action"),
]
FORM = "hook,lift,groove,groove2*3,final"  # the lift lands on the crest at 2 s, the stinger on the end card at 12 s
OUTRO = 12.0
CUES = [  # the cut's own timeline
    {"t": 0.0, "type": "stamp"},
    {"t": 3.5, "type": "click"},
    {"t": 6.5, "type": "swell"}, {"t": 7.35, "type": "buzz"}, {"t": 8.0, "type": "chime"},
    {"t": 9.0, "type": "swell"}, {"t": 9.2, "type": "ding"},
    {"t": 11.9, "type": "crowd", "dur": 3.0}, {"t": 12.05, "type": "sparkle"},
]


def run(cmd):
    print("  $", " ".join(str(c) for c in cmd[:4]), "…")
    subprocess.run([str(c) for c in cmd], check=True)


# The hero spot's 15 s cut: picture and its own mix are cut together, every join on the 120 BPM beat grid
# (0.5 s), so the music keeps its pulse and every effect stays on its frame; 30 ms fades at each join.
ANTHEM_RANGES = [
    (0.0, 4.0, "stadium open and the globe: your team, their stadium, one sentence away"),
    (5.5, 8.0, "the plan arrives and the push-in on the confirmed fixture"),
    (8.0, 10.0, "real fixtures, honest tickets"),
    (16.0, 19.5, "the peak: be there, the world is your stadium"),
    (23.0, 26.0, "end card with the sonic logo"),
]


def anthem_cut(lang):
    src = ADS / "10-anthem" / f"venuvoy-anthem-{lang}_9x16_1080x1920.mp4"
    out = ADS / "10-anthem" / f"venuvoy-anthem-{lang}-15s_9x16_1080x1920.mp4"
    render_py = VIDEO_USE / "helpers" / "render.py"
    with tempfile.TemporaryDirectory(prefix=f"vv-acut-{lang}-") as tmp:
        tmp = Path(tmp)
        edl = {"sources": {"hero": str(src)}, "ranges": [{"source": "hero", "start": a, "end": b, "beat": n} for a, b, n in ANTHEM_RANGES]}
        (tmp / "edl.json").write_text(json.dumps(edl, indent=2))
        cut = tmp / "cut.mp4"
        run([sys.executable, render_py, tmp / "edl.json", "-o", cut, "--fps", "30", "--no-subtitles", "--no-loudnorm"])
        dur = sum(b - a for a, b, _ in ANTHEM_RANGES)
        run(["ffmpeg", "-y", "-loglevel", "error", "-i", cut, "-vn", "-ac", "2", "-ar", "44100", "-c:a", "pcm_s16le", tmp / "raw.wav"])
        run([sys.executable, SRC / "audio" / "master.py", tmp / "raw.wav", tmp / "music.wav"])
        run(["ffmpeg", "-y", "-loglevel", "error", "-i", cut, "-i", tmp / "music.wav", "-map", "0:v", "-map", "1:a", "-c:v", "copy",
             "-c:a", "aac", "-b:a", "192k", "-t", dur, "-movflags", "+faststart", out])
        run(["ffmpeg", "-y", "-loglevel", "error", "-i", out, "-vf", "crop=1080:1350:0:250", "-c:v", "libx264", "-preset", "slow", "-crf", "17",
             "-pix_fmt", "yuv420p", "-c:a", "copy", "-movflags", "+faststart", str(out).replace("_9x16_1080x1920", "_4x5_1080x1350")])
    print(f"cutdown: {out} (+ 4:5)")


def main():
    if len(sys.argv) > 2 and sys.argv[1] == "anthem":
        return anthem_cut(sys.argv[2])
    lang = sys.argv[1] if len(sys.argv) > 1 else "en"
    folder = {"en": "08-reel-en", "he": "07-reel-he"}[lang]
    full = ADS / folder / f"venuvoy-reel-{lang}_9x16_1080x1920.mp4"
    out = ADS / folder / f"venuvoy-reel-{lang}-15s_9x16_1080x1920.mp4"
    render_py = VIDEO_USE / "helpers" / "render.py"
    if not render_py.exists():
        sys.exit(f"video-use not found at {VIDEO_USE} (set VIDEO_USE)")
    with tempfile.TemporaryDirectory(prefix=f"vv-cut-{lang}-") as tmp:
        tmp = Path(tmp)
        edl = {"sources": {"reel": str(full)}, "ranges": [{"source": "reel", "start": a, "end": b, "beat": note} for a, b, note in RANGES]}
        (tmp / "edl.json").write_text(json.dumps(edl, indent=2))
        picture = tmp / "picture.mp4"
        run([sys.executable, render_py, tmp / "edl.json", "-o", picture, "--fps", "30", "--no-subtitles", "--no-loudnorm"])
        dur = sum(b - a for a, b, _ in RANGES)
        (tmp / "cues.json").write_text(json.dumps(CUES))
        run([sys.executable, SRC / "audio" / "compose.py", tmp / "raw.wav", "--form", FORM, "--dur", dur, "--outro", OUTRO, "--cues", tmp / "cues.json"])
        run([sys.executable, SRC / "audio" / "master.py", tmp / "raw.wav", tmp / "music.wav"])
        run(["ffmpeg", "-y", "-loglevel", "error", "-i", picture, "-i", tmp / "music.wav", "-map", "0:v", "-map", "1:a", "-c:v", "copy",
             "-c:a", "aac", "-b:a", "192k", "-t", dur, "-movflags", "+faststart", out])  # -t, not -shortest: keeps all 450 frames
    print(f"cutdown: {out}")


if __name__ == "__main__":
    main()
