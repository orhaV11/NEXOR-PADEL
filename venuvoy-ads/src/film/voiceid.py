"""How alike are two voices? A number, for an editor who cannot hear.

    import voiceid
    e1 = voiceid.embed("leo_sample.mp3")                 # whole file
    e2 = voiceid.embed("shot.mp4", 0.5, 2.2)             # only 0.5-2.2 s
    voiceid.sim(e1, e2)                                  # cosine similarity, -1..1

Uses a speaker-verification network (WeSpeaker ResNet34, VoxCeleb) through sherpa-onnx; the model sits in
work/asr/spk/ and is fetched from the sherpa-onnx releases if missing. Rough guide for clean speech of 1.5 s or more:
above about 0.60 the same voice, 0.35 to 0.60 similar, below 0.35 a different person. Short or noisy lines read
low, so compare like with like (a shot against other shots) and treat the number as evidence, not a verdict.
"""
import subprocess
import sys
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
MODEL = HERE.parent.parent / "work" / "asr" / "spk" / "wespeaker_en_voxceleb_resnet34_LM.onnx"
URL = "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_en_voxceleb_resnet34_LM.onnx"
_ex = None


def _extractor():
    global _ex
    if _ex is None:
        import sherpa_onnx
        if not MODEL.exists():
            MODEL.parent.mkdir(parents=True, exist_ok=True)
            subprocess.run(["curl", "-sS", "-L", "-m", "300", "-o", str(MODEL), URL], check=True)
        _ex = sherpa_onnx.SpeakerEmbeddingExtractor(sherpa_onnx.SpeakerEmbeddingExtractorConfig(model=str(MODEL), num_threads=2))
    return _ex


def _pcm(path, t0=None, t1=None, sr=16000):
    cmd = ["ffmpeg", "-v", "error"] + (["-ss", str(t0)] if t0 is not None else []) + ["-i", str(path)] + \
          (["-t", str(t1 - (t0 or 0))] if t1 is not None else []) + ["-vn", "-ac", "1", "-ar", str(sr), "-f", "f32le", "-"]
    return np.frombuffer(subprocess.run(cmd, capture_output=True, check=True).stdout, dtype=np.float32)


def embed(path, t0=None, t1=None):
    ex = _extractor()
    x = _pcm(path, t0, t1)
    s = ex.create_stream()
    s.accept_waveform(16000, x)
    s.input_finished()
    e = np.array(ex.compute(s), dtype=np.float32)
    return e / (np.linalg.norm(e) + 1e-9)


def sim(a, b):
    return float(np.dot(a, b))


if __name__ == "__main__":
    a = sys.argv[1:]
    print(round(sim(embed(a[0]), embed(a[1])), 3))
