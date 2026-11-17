"""Readings of a whole sketch by a dialogue speech model, several casts side by side.

    python3 src/film/toon/voices.py <sketch.json> <cast name> [<cast name> ...] [--model gemini-3-8-flash-tts] [--n 1]

sketch.json: {"work": "work/switch", "cap": 5.0, "scene": "...", "context": "...",
              "lines": [["L", "[direction] words"], ...],
              "casts": {"a": {"L": {"voice_name": ..., "accent": ..., "style": ..., "pace": ..., "audio_profile": ...}, "T": {...}}}}
Each reading is saved as <work>/voice/<cast>_<model tag>_<k>.wav (48 kHz mono) with a .json beside it (the request and
the words heard back by two recognisers, with times). Money goes through the film's ledger.
"""
import json
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))
import asr  # noqa: E402
import muapi  # noqa: E402
from spend import Ledger  # noqa: E402

args = [a for a in sys.argv[1:] if not a.startswith("--")]
opt = lambda k, d=None: (sys.argv[sys.argv.index(k) + 1] if k in sys.argv else d)  # noqa: E731
sk = json.load(open(args[0]))
casts = [a for a in args[1:] if a not in (opt("--model"), opt("--n"))]
MODEL, N = opt("--model", "gemini-3-8-flash-tts"), int(opt("--n", 1))
TAG = {"gemini-3-8-flash-tts": "g38", "gemini-2-5-pro-tts": "g25p", "gemini-3-1-flash-tts": "g31", "gemini-3-8-flash-lite-tts": "g38l"}.get(MODEL, MODEL[:6])
OUT = ROOT / sk["work"] / "voice"
OUT.mkdir(parents=True, exist_ok=True)
led = Ledger(ROOT / sk["work"] / "ledger.jsonl", sk.get("cap", 5.0))


def one(job):
    cast, k = job
    name = f"{cast}_{TAG}_{k}"
    wav = OUT / f"{name}.wav"
    if wav.exists():
        return name, "already there"
    ids = {sid: f"Speaker {i + 1}" for i, sid in enumerate(sk["casts"][cast])}  # the service wants "Speaker N"
    spk = [{"speaker_id": ids[sid], **v} for sid, v in sk["casts"][cast].items()]
    payload = {"speakers": spk, "dialogue_turns": [{"speaker_id": ids[s], "text": t} for s, t in sk["lines"]],
               "scene": sk.get("scene"), "sample_context": sk.get("context"), "temperature": sk.get("temperature", 1)}
    try:
        rid = muapi.submit(MODEL, payload)
    except Exception as e:  # noqa: BLE001
        return name, f"NOT submitted: {str(e)[:200]}"
    res = muapi.wait(rid, every=4, limit=300)
    url = muapi.output_url(res)
    if not url:
        return name, "FAILED " + json.dumps(res)[:300]
    raw = OUT / f"{name}.src"
    muapi.download(url, raw)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(raw), "-ac", "1", "-ar", "48000", str(wav)], check=True)
    raw.unlink()
    words, text = asr.words(str(wav))
    (OUT / f"{name}.json").write_text(json.dumps({"model": MODEL, "cast": sk["casts"][cast], "payload": payload, "words": words, "text": text}, ensure_ascii=False, indent=1))
    dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(wav)], capture_output=True, text=True).stdout)
    return name, f"{dur:.1f} s | heard: {' '.join(w for w, _, _ in words)}"


jobs = [(c, k) for c in casts for k in range(1, N + 1)]
with led.paid(f"voice readings {MODEL}: {', '.join(casts)} x{N}"):
    with ThreadPoolExecutor(6) as ex:
        for name, msg in ex.map(one, jobs):
            print(f"{name}: {msg}", flush=True)
