"""Buy one sound for a sketch (the air of a place, or one effect) and hear back what it really is.

    python3 src/film/toon/sfx.py <film> <name> "<what to hear, in plain words>" [seconds]

Saved as work/<film>/sound/raw/<name>.wav (48 kHz mono); 1.5 cents, through the film's ledger. The listening model
then says what it hears (half a cent): these sounds often come back as something else (a whistle came back as a
gunshot), so read the answer before using it. For the air of a place ask for 10 seconds; for an effect, 2 to 4.
"""
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
import muapi  # noqa: E402
from hear import ask, reel  # noqa: E402
from spend import Ledger  # noqa: E402

film, name, prompt = sys.argv[1:4]
seconds = int(sys.argv[4]) if len(sys.argv) > 4 else 8
out = ROOT / "work" / film / "sound" / "raw"
out.mkdir(parents=True, exist_ok=True)
wav = out / f"{name}.wav"
led = Ledger(ROOT / "work" / film / "ledger.jsonl")
if wav.exists():
    print("already there:", wav)
else:
    with led.paid(f"sound: {name}", expect=0.03):
        res = muapi.wait(muapi.submit("mmaudio-v2/text-to-audio", {"prompt": prompt, "duration": seconds}), every=4, limit=300)
        url = muapi.output_url(res)
        if not url:
            sys.exit(f"FAILED: {res}")
        raw = out / f"{name}.src"
        muapi.download(url, raw)
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(raw), "-ac", "1", "-ar", "48000", str(wav)], check=True)
        raw.unlink()
        heard = ask(reel([(name.upper()[:12], wav)], out / f"_{name}_reel.mp4"),
                    "Listen to this sound. In two sentences: what exactly do you hear, second by second? Is there any voice, music or sudden bang in it? Does it loop smoothly or does it change?")
    print(f"{wav}\nASKED FOR: {prompt}\nTHE LISTENING MODEL HEARS: {heard}")
