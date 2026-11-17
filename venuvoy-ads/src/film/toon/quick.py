"""One-shot shorts in bulk: a short is a few lines of text here, and the whole line of work runs from them.

    python3 src/film/toon/quick.py voices view football ...     read each short three times, cut the lines, choose the takes (about 4 cents a short)
    python3 src/film/toon/quick.py sound view ...               the soundtrack and its timeline
    python3 src/film/toon/quick.py sheet view ...               a contact sheet (work/<name>/sheet.jpg)
    python3 src/film/toon/quick.py film view ...                the film, its 4:5 version, frames for checking
    python3 src/film/toon/quick.py check view ...               the watching model on the finished film (3 cents)
    python3 src/film/toon/quick.py all view ...                 everything in order

A short stands in a PLACE that already exists (a background some film paid for) and uses only the rig: nothing is
drawn. Each line says who speaks, how, and what the two do from that line on:
    ("L", "[bright, to the camera] Timo reviews: the view.", {"face": None, "eyes": "lens"})
      face: Luna's face from this line on (None sunny, "guilty", "defend", "read", "read_b", "down", "turn")
      eyes: Timo's eyes from this line on (None on her, "lens", "narrow", "shut")
      gap:  seconds after the line before (default: 0.14 after the other speaker, 0.3 after her own line)
      piece: "phone" or "hand" comes up for this line        stop: True = the vlog music stops dead as this line ends
      cut: True = a jump cut closer just before this line     opts: options of the sound plan ("tighten", "ground", ...)
"end": what they do after the last line ({"face": ..., "eyes": ...}), held for "hold" seconds before the plate.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE))

LDN = {"L": {"voice_name": "Laomedeia", "accent": "British (Brixton)", "style": "Vocal Smile", "pace": "Rapid Fire",
             "audio_profile": "A cheerful woman of 25 with a bright, warm, quick voice; natural and unpolished, like a friend filming a vlog."},
       "T": {"voice_name": "Zubenelgenubi", "accent": "British (Brixton)", "style": "Deadpan", "pace": "Natural",
             "audio_profile": "A tall, dry man of 30 with a low, casual voice; slow, proud, deadpan; he never raises his voice."}}
CONTEXT = ("A ten-second comedy moment played by two close friends on a sports trip. It must sound like two real people talking, quick and natural, never like an advert or a "
           "narrator. LUNA, about 25, is cheerful, quick and sure of herself; to her everything is good news. TIMO, about 30, is slow, dry and proud; he never raises his "
           "voice. They answer each other fast.")

# places that exist: the look, the background and where the two stand (wide, and closer for a jump cut), the air, where the plate goes
PLACES = {
    "hill": {"look": "wind", "scene": "A phone vlog on a windy hill above a small city with a stadium far below. Wind.",
             "wide": (("bg_hill_1", (370, 1660), (238, 596), 1.28), (372, 1556, 1.00, -1.2), (872, 935, 1.00)),
             "close": (("bg_hill_1", (370, 1660), (250, 440), 1.50), (345, 1490, 1.16, 1.0), (890, 830, 1.16)),
             "air": {"file": "wind_a", "db": -33.0, "gusts": True}, "plate": {"x": 352, "y": 300, "size": 0.74}},
    "gates": {"look": "calm", "scene": "A phone vlog on the empty forecourt of a stadium, shut gates and empty crowd barriers behind. A still day, very quiet.",
              "wide": (("bg_gates_1", (1100, 1000), (800, 200), 1.02), (362, 1545, 0.92, 0.0), (872, 1110, 0.92)),
              "close": (("bg_gates_1", (1100, 1000), (860, 120), 1.22), (352, 1560, 1.10, 0.8), (886, 1240, 1.08)),   # he stays low: the plate lands above his head
              "air": {"file": "wind_a", "db": -35.0, "gusts": False}, "plate": {}},
}

SHORTS = {
    "view": {"nn": "20", "title": "Timo reviews: the view", "place": "hill", "lines": [
        ("L", "[bright, over the wind, to the camera] Timo reviews: the view.", {"face": None, "eyes": "lens"}),
        ("T", "[flat, looking at it] Sky. Grass. A stadium, very small.", {"eyes": "lens", "gap": 0.30, "opts": {"tighten": 0.18}}),
        ("L", "[turning to him, bright] Out of ten?", {"face": "turn", "gap": 0.18, "stop": True}),
        ("T", "[flat, after a beat] Four. It's a ten from inside.", {"face": None, "eyes": "lens", "gap": 0.40, "cut": True})],
        "end": {"face": "guilty"}, "hold": 0.75},
    "football": {"nn": "21", "title": "One sentence: football", "place": "hill", "lines": [
        ("L", "[bright, to the camera, a dare] Football. One sentence.", {"face": None, "eyes": None}),
        ("T", "[slow, exact, to the camera] Ninety minutes of complaining, and one of them is the best minute of your year.", {"eyes": "lens", "gap": 0.35, "stop": True, "opts": {"tighten": 0.16}}),
        ("L", "[quiet, turning to him, meaning it] ...That's actually lovely.", {"face": "turn", "gap": 0.55}),
        ("T", "[at once, flat] It was an accident.", {"eyes": None, "gap": 0.10})],
        "end": {"face": None, "eyes": "lens"}, "hold": 0.7,
        "captions": {"split": {"T2": [("Ninety minutes of complaining,", None), ("and one of them is the best minute of your year.", "T2@and")]}}},
    "roar": {"nn": "22", "title": "The people in the stadium", "place": "hill", "roar": True, "lines": [
        ("L", "[turning to him, quick] Was that a goal?", {"face": "turn", "eyes": None, "gap": 1.25}),
        ("T", "[flat] That was a goal.", {"gap": 0.12}),
        ("L", "[eager] Who for?", {"gap": 0.10, "stop": True}),
        ("T", "[to the camera, after a beat] The people in the stadium.", {"face": "guilty", "eyes": "lens", "gap": 0.45})],
        "end": {}, "hold": 0.8},
    "uphill": {"nn": "23", "title": "She loves it", "place": "hill", "lines": [
        ("T", "[flat, not out of breath, to the camera] Day three of the trip. The morning run.", {"face": "defend", "eyes": "lens"}),
        ("L", "[out of breath, big eyes, to the camera] Nobody said it was uphill.", {"gap": 0.20}),
        ("T", "[flat, to her] You added it to the plan.", {"face": "guilty", "eyes": None, "gap": 0.14, "stop": True}),
        ("T", "[to the camera, lightly, copying her sunny tune] She loves it.", {"eyes": "lens", "gap": 0.75, "cut": True})],
        "end": {"face": "defend"}, "hold": 0.7},
    "nogame": {"nn": "24", "title": "The weekend with no game", "place": "gates", "lines": [
        ("L", "[tragic, to the camera] No game this weekend. Not ours. Not anybody's.", {"face": "defend", "eyes": None, "opts": {"tighten": 0.16}}),
        ("T", "[flat, counting] There's basketball. Ice hockey. Rugby. Golf.", {"face": "guilty", "gap": 0.25, "opts": {"tighten": 0.16}}),
        ("L", "[turning to him, doubtful] You'd watch golf?", {"face": "turn", "gap": 0.16, "stop": True}),
        ("T", "[to the camera, dry] I'd watch you stay quiet for four hours.", {"eyes": "lens", "gap": 0.40})],
        "end": {"face": "defend"}, "hold": 0.75},
    "ironed": {"nn": "25", "title": "He ironed the scarf", "place": "gates", "lines": [
        ("L", "[low, to the camera, confiding] Stadium tour at ten. He's been ready since six.", {"face": "guilty", "eyes": "lens", "opts": {"tighten": 0.18}}),
        ("T", "[flat, to the camera] I'm fine either way.", {"gap": 0.20}),
        ("L", "[to the camera, delighted, a secret] He ironed the scarf.", {"face": None, "gap": 0.30, "stop": True}),
        ("L", "[sunny, to the camera] He loves it.", {"eyes": "shut", "gap": 0.80, "cut": True})],
        "end": {}, "hold": 0.6},
}


def ids(sh):
    return [f"{who}{n}" for n, (who, _, _) in enumerate(sh["lines"], 1)]


def run(*cmd):
    print("  $", " ".join(str(c) for c in cmd[1:])[:140], flush=True)
    subprocess.run([str(c) for c in cmd], check=True, cwd=ROOT)


def voices(name):
    sh = SHORTS[name]
    js = HERE / f"ep_{name}_voices.json"
    js.write_text(json.dumps({"work": f"work/{name}", "cap": 3.0, "temperature": 1, "scene": PLACES[sh["place"]]["scene"], "context": CONTEXT,
                              "lines": [[who, text] for who, text, _ in sh["lines"]], "casts": {"ldn": LDN}}, indent=1, ensure_ascii=False))
    takes = [f"ldn_g25p_{k}" for k in (1, 2, 3)]
    run(sys.executable, HERE / "voices.py", js, "ldn", "--model", "gemini-2-5-pro-tts", "--n", "3")
    run(sys.executable, HERE / "lines.py", js, *takes)
    run(sys.executable, HERE / "pick.py", js, *takes)


def chosen(name):
    """Which reading each line is taken from. All the lines of one speaker come from ONE reading when a reading is
    clean for all of them (every word found, none wrong): the voice colour shifts between readings. Among such
    readings the one the listening model liked for most lines wins. Otherwise line by line: its pick if clean, else
    the first clean take."""
    V = ROOT / "work" / name / "voice"
    picks = {int(k): v for k, v in json.loads((V / "picks.json").read_text()).items()} if (V / "picks.json").exists() else {}
    rows = {t: {r["n"]: r for r in json.loads((V / "lines" / f"{t}.json").read_text())} for t in (f"ldn_g25p_{k}" for k in (1, 2, 3)) if (V / "lines" / f"{t}.json").exists()}

    def clean(t, n):
        r = rows.get(t, {}).get(n)
        return bool(r) and not r.get("missing") and not r.get("wrong") and r["matched"].split("/")[0] == r["matched"].split("/")[1]

    lines = SHORTS[name]["lines"]
    out = {}
    for who in ("L", "T"):
        mine = [n for n, (w, _, _) in enumerate(lines, 1) if w == who]
        whole = [t for t in rows if all(clean(t, n) for n in mine)]
        if whole:
            best = max(whole, key=lambda t: sum(picks.get(n) == t for n in mine))
            out.update({n: best for n in mine})
            continue
        for n in mine:
            order = ([picks[n]] if n in picks else []) + [t for t in rows if t != picks.get(n)]
            take = next((t for t in order if clean(t, n)), None)
            if take is None:
                raise SystemExit(f"{name}: no clean take of line {n}: read it again")
            out[n] = take
    return [out[n] for n in range(1, len(lines) + 1)]


def sound(name):
    from vlog_sound import build
    sh, takes, I = SHORTS[name], chosen(name), ids(SHORTS[name])
    plan, stop, prev = [], None, None
    for n, ((who, text, o), take) in enumerate(zip(sh["lines"], takes), 1):
        gap = o.get("gap", 0.25 if n == 1 else 0.14 if who != prev else 0.30)
        if o.get("cut"):
            plan.append(("CUT", gap * 0.45))
            gap = gap * 0.55
        plan.append((I[n - 1], who, take, n, gap, o.get("opts", {})))
        if o.get("stop"):
            stop = I[n - 1]
        prev = who
    plan.append(("PLATE", sh.get("hold", 0.7) + 0.12))
    fx = []
    if sh.get("roar"):   # a far crowd roar before the first line (bought once, heard back as a roar)
        fx.append({"name": "roar", "file": "roar", "at": 0.15, "db": -15, "whole": True, "start": sh.get("roar_from", 0.0), "len": 2.4, "fade_in": 0.25, "fade_out": 0.9})
    print(name, "takes", takes)
    build(name, plan, bed={"stop": stop or "PLATE"}, ending="beat", plate_hold=1.5, ambience=PLACES[sh["place"]]["air"], fx=fx)


def picture(name):
    from vlog import Vlog
    sh, P, I = SHORTS[name], PLACES[SHORTS[name]["place"]], ids(SHORTS[name])
    v = Vlog(name, f"ads/21-vlog/venuvoy-vlog-{sh['nn']}-{name}", luna=P["look"], timo="timo_clean")
    bg, lu, ti = P["wide"]
    v.shot("start", bg=bg, luna=lu, timo=ti)
    if any(o.get("cut") for _, _, o in sh["lines"]):
        bg, lu, ti = P["close"]
        v.shot("cut1", bg=bg, luna=lu, timo=ti)
    faces, eyes = [], []
    for n, (who, text, o) in enumerate(sh["lines"]):
        when = "start" if n == 0 else ("cut1" if o.get("cut") else f"{I[n]}-0.10")
        if "face" in o:
            faces.append((when, o["face"]))
        if "eyes" in o:
            eyes.append((when, o["eyes"]))
        if o.get("piece"):
            v.piece(o["piece"], up=f"{I[n]}-0.38", down=f"{I[n]}.end+0.25", lift=110 if o["piece"] == "phone" else 100)
    end = sh.get("end", {})
    if "face" in end:
        faces.append((f"{I[-1]}.end+0.22", end["face"]))
    if "eyes" in end:
        eyes.append((f"{I[-1]}.end+0.12", end["eyes"]))
    if faces:
        v.faces(*faces)
    if eyes:
        v.eyes(*eyes)
    v.captions(**sh.get("captions", {}))
    if P["plate"]:
        v.plate(**P["plate"])
    return v


if __name__ == "__main__":
    step, names = sys.argv[1], sys.argv[2:]
    for name in names:
        print("=====", step, name, flush=True)
        if step in ("voices", "all"):
            voices(name)
        if step in ("sound", "all"):
            sound(name)
        if step in ("sheet", "film", "all"):
            sys.argv = [sys.argv[0]] + (["--sheet"] if step == "sheet" else [])
            picture(name).main()
        if step in ("check", "all"):
            run(sys.executable, HERE / "check.py", name)
        if step in ("pack", "all"):
            run(sys.executable, HERE / "pack.py", name)
