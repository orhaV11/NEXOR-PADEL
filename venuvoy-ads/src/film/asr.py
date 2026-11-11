"""Speech-to-text with word times, for checking what a generated clip really says and for timing subtitles.

    python3 src/film/asr.py clip.mp4 [more.mp4 ...] [--json out.json]

Two offline models from the sherpa-onnx releases (work/asr/): NVIDIA Parakeet TDT 0.6b v2 gives the words
with timestamps, Whisper small.en gives a second opinion on the text. Where they disagree, look closer.
"""
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import sherpa_onnx

ROOT = Path(__file__).resolve().parents[2]
ASR = ROOT / "work" / "asr"
_P, _W = None, None


def parakeet():
    global _P
    if _P is None:
        d = ASR / "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8"
        _P = sherpa_onnx.OfflineRecognizer.from_transducer(
            encoder=str(d / "encoder.int8.onnx"), decoder=str(d / "decoder.int8.onnx"), joiner=str(d / "joiner.int8.onnx"),
            tokens=str(d / "tokens.txt"), model_type="nemo_transducer", num_threads=4)
    return _P


def whisper():
    global _W
    if _W is None:
        d = ASR / "sherpa-onnx-whisper-small.en"
        _W = sherpa_onnx.OfflineRecognizer.from_whisper(
            encoder=str(d / "small.en-encoder.int8.onnx"), decoder=str(d / "small.en-decoder.int8.onnx"),
            tokens=str(d / "small.en-tokens.txt"), num_threads=4)
    return _W


def load(path, sr=16000):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-vn", "-ac", "1", "-ar", str(sr), "-f", "f32le", "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).copy(), sr


def words(path):
    """[(word, start_s, end_s)] from Parakeet, plus Whisper's text."""
    x, sr = load(path)
    if len(x) < sr // 4:
        return [], ""
    s = parakeet().create_stream()
    s.accept_waveform(sr, x)
    parakeet().decode_stream(s)
    r = s.result
    out, cur, t0 = [], "", 0.0
    toks, times = list(r.tokens), list(r.timestamps)
    for i, (tok, t) in enumerate(zip(toks, times)):
        if tok.startswith(" ") or tok.startswith("▁") or not cur:
            if cur:
                out.append((cur, round(t0, 2), round(t, 2)))
            cur, t0 = tok.strip(" ▁"), t
        else:
            cur += tok
    if cur:
        out.append((cur, round(t0, 2), round(min(len(x) / sr, t0 + 0.6), 2)))
    w = whisper().create_stream()
    w.accept_waveform(sr, x)
    whisper().decode_stream(w)
    return out, w.result.text.strip()


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    js = sys.argv[sys.argv.index("--json") + 1] if "--json" in sys.argv else None
    if js:
        args = [a for a in args if a != js]
    res = {}
    for p in args:
        ws, wt = words(p)
        res[p] = {"words": ws, "whisper": wt}
        print(f"== {p}\n  parakeet: " + " ".join(f"{w}@{a:.2f}" for w, a, b in ws) + f"\n  whisper : {wt}")
    if js:
        Path(js).write_text(json.dumps(res, indent=1))
