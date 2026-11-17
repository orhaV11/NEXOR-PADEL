"""Ask the listening model about sound files: they are laid one after another as a numbered reel (a plain card per take).

    from hear import reel, ask
    path = reel([("TAKE 1", "a.wav"), ("TAKE 2", "b.wav")], "work/x/reel.mp4")
    print(ask(path, "question"))
Each question costs a fraction of a cent; callers put it inside the film's ledger.
"""
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import watch  # noqa: E402


def reel(items, out, gap=0.8):
    out = Path(out).resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    parts = []
    for i, (label, wav) in enumerate(items):
        p = out.with_name(f"{out.stem}_{i}.mp4")
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=c=0x202020:s=360x640:r=12", "-i", str(wav), "-vf",
                        f"drawtext=text='{label}':fontcolor=white:fontsize=56:x=(w-text_w)/2:y=(h-text_h)/2", "-af", f"apad=pad_dur={gap}", "-shortest",
                        "-c:v", "libx264", "-preset", "veryfast", "-crf", "30", "-c:a", "aac", "-ar", "48000", "-ac", "2", str(p)], check=True)
        parts.append(p)
    lst = out.with_suffix(".txt")
    lst.write_text("".join(f"file '{p}'\n" for p in parts))
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", str(lst), "-c", "copy", str(out)], check=True)
    for p in parts:
        p.unlink()
    lst.unlink()
    return str(out)


def ask(path, question, limit=500):
    return watch.ask(path, question, None, limit)
