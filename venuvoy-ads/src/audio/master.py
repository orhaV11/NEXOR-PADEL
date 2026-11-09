"""Master a mix once: two-pass EBU R128 loudnorm to the social target, then report what came out.

    python3 src/audio/master.py in.wav out.wav [--lufs -14] [--tp -1]

Pass 1 measures, pass 2 applies the measured values in linear mode (no pumping), as the video-use
skill recommends for anything that ships: integrated -14 LUFS, true peak at or under -1 dBTP.
"""
import json
import re
import subprocess
import sys


def measure(path, extra=""):
    cmd = ["ffmpeg", "-hide_banner", "-nostats", "-i", path, "-af", f"loudnorm=I=-14:TP=-1:LRA=11:print_format=json{extra}", "-f", "null", "-"]
    err = subprocess.run(cmd, capture_output=True, text=True).stderr
    return json.loads(re.findall(r"\{[^{}]+\}", err, re.S)[-1])


def main():
    args = sys.argv[1:]
    src, out = args[0], args[1]
    lufs = float(args[args.index("--lufs") + 1]) if "--lufs" in args else -14.0
    tp = float(args[args.index("--tp") + 1]) if "--tp" in args else -1.0
    m = measure(src)
    flt = (
        f"loudnorm=I={lufs}:TP={tp}:LRA=11:measured_I={m['input_i']}:measured_TP={m['input_tp']}:"
        f"measured_LRA={m['input_lra']}:measured_thresh={m['input_thresh']}:offset={m['target_offset']}:linear=true"
    )
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", src, "-af", flt, "-ar", "44100", "-c:a", "pcm_s16le", out], check=True)
    r = measure(out)
    print(f"mastered {out}: {float(r['input_i']):.1f} LUFS, true peak {float(r['input_tp']):.1f} dBTP (was {float(m['input_i']):.1f} LUFS)")


if __name__ == "__main__":
    main()
