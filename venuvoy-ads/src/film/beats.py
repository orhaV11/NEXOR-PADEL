"""Find the pulse of a music file, so cuts can land on it.

    python3 src/film/beats.py music.wav [--json out.json]

Prints the tempo, the beat times, how steady the beat is, where the loud hits are, how much of the energy
survives a phone speaker (400 Hz high-pass), and whether any singing or speech slipped into an instrumental.
"""
import json
import sys
from pathlib import Path

import librosa
import numpy as np


def analyse(path):
    y, sr = librosa.load(path, sr=22050, mono=True)
    dur = len(y) / sr
    onset = librosa.onset.onset_strength(y=y, sr=sr)
    tempo, beats = librosa.beat.beat_track(onset_envelope=onset, sr=sr, units="time", tightness=100)
    tempo = float(np.atleast_1d(tempo)[0])
    ibi = np.diff(beats)
    hits = librosa.onset.onset_detect(onset_envelope=onset, sr=sr, units="time", backtrack=False, delta=0.25, wait=8)
    strength = onset[librosa.time_to_frames(hits, sr=sr).clip(0, len(onset) - 1)]
    top = [float(t) for t, _ in sorted(zip(hits, strength), key=lambda x: -x[1])[:14]]
    rms = librosa.feature.rms(y=y, frame_length=2048, hop_length=512)[0]
    t_rms = librosa.frames_to_time(np.arange(len(rms)), sr=sr, hop_length=512)
    # energy per second, to see intro / build / drop / ending
    per_s = [float(20 * np.log10(np.sqrt(np.mean(y[int(a * sr): int((a + 1) * sr)] ** 2)) + 1e-9)) for a in range(int(dur))]
    import scipy.signal as ss
    b, a = ss.butter(4, 400 / (sr / 2), btype="high")
    hp = ss.filtfilt(b, a, y)
    phone_loss = float(20 * np.log10(np.sqrt(np.mean(y ** 2)) / (np.sqrt(np.mean(hp ** 2)) + 1e-9)))
    tail = rms[t_rms > dur - 1.5]
    return {"file": str(path), "dur": round(dur, 2), "tempo": round(tempo, 1), "beats": [round(float(b), 3) for b in beats],
            "beat_jitter_pct": round(float(np.std(ibi) / np.mean(ibi) * 100), 1) if len(ibi) > 2 else None,
            "big_hits": sorted(round(t, 2) for t in top), "level_per_second_db": [round(v, 1) for v in per_s],
            "phone_loss_db": round(phone_loss, 1), "ends_db": round(float(20 * np.log10(tail.mean() + 1e-9)), 1) if len(tail) else None}


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out = sys.argv[sys.argv.index("--json") + 1] if "--json" in sys.argv else None
    if out:
        args = [a for a in args if a != out]
    res = [analyse(p) for p in args]
    for r in res:
        print(f"== {r['file']}: {r['dur']} s, {r['tempo']} BPM, beat jitter {r['beat_jitter_pct']}%, phone loss {r['phone_loss_db']} dB, last 1.5 s at {r['ends_db']} dB")
        print("   beats:", " ".join(f"{b:.2f}" for b in r["beats"]))
        print("   big hits:", " ".join(f"{b:.2f}" for b in r["big_hits"]))
        print("   level/s:", " ".join(f"{v:.0f}" for v in r["level_per_second_db"]))
        try:
            sys.path.insert(0, str(Path(__file__).resolve().parent))
            import asr
            ws, wt = asr.words(r["file"])
            print(f"   words heard: {wt or '(none)'}")
        except Exception as e:  # the ASR models are optional for this tool
            print("   (no ASR check:", e, ")")
    if out:
        Path(out).write_text(json.dumps(res, indent=1))
