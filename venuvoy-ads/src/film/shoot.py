"""Shoot a series with a recurring cast through MuAPI (see muapi.py).

    python3 src/film/shoot.py <shoot.json> [--only id,id] [--retake id,id] [--dry]

shoot.json:
  { "dir": "src/film/assets/<series>",          portraits/, shots/ and state.json are kept here
    "cast": { "gran": { "portrait": "<text-to-image prompt of the character in their interview seat>",
                        "image": "or a still that already exists (a frame of an approved clip)",
                        "edit": "with image: what to clean in that still before it is used" }, ... },
    "shots": [ { "id": "e1_s1", "cast": "gran" | null, "setup": "seat" | "away" | "none",
                 "scene": "away: where they are now, light, wardrobe; none: the whole scene with the people in it",
                 "action": "what they do before, while and after speaking, with the silent end beat",
                 "delivery": "calm, matter-of-fact", "line": "the exact line, or empty for a silent shot",
                 "audio": "what else is heard", "accent": "an English accent" } ] }

How a recurring character is kept: the portrait is made once (or taken from an approved clip) and every seated
shot starts from that picture, so the face, the clothes and the room return exactly, like a real interview
set-up. For a shot somewhere else the portrait is first redrawn in the new place (same person), and the shot
starts from that. Every shot is Veo 3.1 Lite, 8 seconds, 720p, with the line lip-synced.

After each shot the recogniser reads the line back; the result and its similarity to the script are stored, so a
take where the words went wrong is found and can be shot again (--retake). Nothing is generated twice: state.json
remembers what exists. --dry prints what would be shot and what it would cost, and spends nothing.
"""
import difflib
import json
import re
import subprocess
import sys
import threading
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE))
import muapi  # noqa: E402

args = [a for a in sys.argv[1:] if not a.startswith("--")]
opt = lambda k: (sys.argv[sys.argv.index(k) + 1].split(",") if k in sys.argv else [])  # noqa: E731
ONLY, RETAKE, DRY = set(opt("--only")), set(opt("--retake")), "--dry" in sys.argv
plan = json.load(open(args[0]))
D = ROOT / plan["dir"]
(D / "portraits").mkdir(parents=True, exist_ok=True)
(D / "shots").mkdir(parents=True, exist_ok=True)
STATE = D / "state.json"
state = json.loads(STATE.read_text()) if STATE.exists() else {"cast": {}, "shots": {}, "away": {}}
state.setdefault("away", {})
T2I, EDIT, I2V, T2V = "nano-banana-2", "nano-banana-2-edit", "veo3.1-lite-image-to-video", "veo3.1-lite-text-to-video"
STYLE = "Handheld television documentary footage, shoulder camera, natural light, slight camera sway, photoreal."
TAIL = "No music. No laugh track. No subtitles, no captions, no on-screen text, no logos."
LOCK = threading.Lock()
spent = [0.0]


def save():
    STATE.write_text(json.dumps(state, ensure_ascii=False, indent=1))


def price(endpoint, payload):
    c = muapi.cost(endpoint, payload).get("cost", 0) or 0
    with LOCK:
        spent[0] += c
    return c


def run(endpoint, payload, out):
    rid = muapi.submit(endpoint, payload)
    res = muapi.wait(rid, limit=1200)
    url = muapi.output_url(res)
    if not url:
        raise RuntimeError(f"{endpoint}: {json.dumps(res, ensure_ascii=False)[:400]}")
    code, err = muapi.download(url, out)
    if code != "200":
        raise RuntimeError(f"download {url}: {code} {err}")
    return url


def norm(s):
    return re.sub(r"[^a-z0-9 ]+", "", s.lower().replace("-", " ")).split()


def heard(path):
    import asr
    ws, _ = asr.words(str(path))
    return " ".join(w for w, _, _ in ws)


def speech(shot):
    line = (shot.get("line") or "").strip()
    if not line:
        return "Nobody speaks. ", f'Audio: {shot.get("audio", "quiet room tone")}. '
    acc = f', in {shot["accent"]}' if shot.get("accent") else ""
    return (f'They say, {shot.get("delivery", "plainly")}{acc}: "{line}" ',
            f'Audio: the voice close and clear, {shot.get("audio", "quiet room tone")}. ')


def prompt_for(shot):
    say, audio = speech(shot)
    act = (shot.get("action", "").rstrip(". ") + ". ") if shot.get("action") else ""
    if shot["setup"] == "seat":
        return f"{STYLE} The person sits exactly as in the image and speaks to an interviewer just off camera. {say}{act}{audio}{TAIL}"
    if shot["setup"] == "away":
        return f"{STYLE} The scene is exactly as in the image. {say}{act}{audio}{TAIL}"
    return f'{shot["scene"].rstrip(". ")}. {say}{act}{audio}{TAIL}'


def do_portrait(name):
    c, st = plan["cast"][name], state["cast"].setdefault(name, {})
    if st.get("portrait_url"):
        return
    if c.get("image"):  # a still that already exists: only an upload
        if DRY:
            print(f"portrait {name}: from {c['image']}" + (f", cleaned ${price(EDIT, {'prompt': c['edit'], 'images_list': ['PORTRAIT'], 'aspect_ratio': '9:16'})}" if c.get("edit") else " (free)"))
            return
        url = muapi.upload(str(ROOT / c["image"]))
        if c.get("edit"):  # the still is cleaned once (a badge, lettering, marks) and every shot starts from the clean one
            url = run(EDIT, {"prompt": c["edit"], "images_list": [url], "aspect_ratio": "9:16"}, D / "portraits" / f"{name}.jpg")
    else:
        payload = {"prompt": c["portrait"], "aspect_ratio": "9:16"}
        cost = price(T2I, payload)
        if DRY:
            print(f"portrait {name}: ${cost}")
            return
        url = run(T2I, payload, D / "portraits" / f"{name}.jpg")
    with LOCK:
        st["portrait_url"] = url
        save()
    print(f"portrait {name}: done", flush=True)


def wanted(shot):
    sid = shot["id"]
    if ONLY and sid not in ONLY:
        return False
    return sid in RETAKE or not state["shots"].get(sid, {}).get("file")


def do_shot(shot):
    sid, name = shot["id"], shot.get("cast")
    cs = state["cast"].get(name, {}) if name else {}
    base = {"prompt": prompt_for(shot), "aspect_ratio": "9:16", "duration": 8, "resolution": "720p"}
    start = None
    if shot["setup"] == "away":  # the same person, redrawn in the new place; the shot starts from that picture
        start = state["away"].get(sid)
        if not start:
            ep = {"prompt": f'{shot["scene"].rstrip(". ")}. It is the same person as in the reference image: the same face, hair, age and build. '
                            "A frame from a television documentary, photoreal, unretouched skin, natural light, vertical 9:16. No text, no logos, no lettering on clothes.",
                  "images_list": [cs.get("portrait_url", "PORTRAIT")], "aspect_ratio": "9:16"}
            c0 = price(EDIT, ep)
            if DRY:
                print(f"{sid}: away picture ${c0}")
            else:
                start = run(EDIT, ep, D / "portraits" / f"{sid}_away.jpg")
                with LOCK:
                    state["away"][sid] = start
                    save()
    elif shot["setup"] == "seat":
        start = cs.get("portrait_url", "PORTRAIT")
    endpoint, payload = (I2V, {**base, "image_url": start or "PORTRAIT"}) if shot["setup"] != "none" else (T2V, base)
    cost = price(endpoint, payload)
    if DRY:
        print(f"{sid}: {endpoint} ${cost}")
        return
    out = D / "shots" / f"{sid}.mp4"
    with LOCK:
        take = len(state["shots"].get(sid, {}).get("takes", [])) + 1
        if out.exists():
            out.rename(D / "shots" / f"{sid}.take{take - 1}.mp4")
    try:
        url = run(endpoint, payload, out)
    except Exception as e:  # noqa: BLE001
        print(f"{sid}: FAILED {e}", flush=True)
        return
    with LOCK:  # the recogniser is not thread-safe: one at a time
        line = (shot.get("line") or "").strip()
        h = heard(out) if line else ""
        score = difflib.SequenceMatcher(None, norm(line), norm(h)).ratio() if line else 1.0
        rec = state["shots"].setdefault(sid, {"takes": []})
        rec.update(file=str(out.relative_to(ROOT)), url=url, endpoint=endpoint, heard=h, score=round(score, 2), line=line)
        rec["takes"].append({"url": url, "heard": h, "score": round(score, 2)})
        save()
    print(f"{sid}: take {take} score {score:.2f} | {h}", flush=True)


with ThreadPoolExecutor(6) as ex:
    list(ex.map(do_portrait, plan["cast"]))
with ThreadPoolExecutor(6) as ex:
    list(ex.map(do_shot, [s for s in plan["shots"] if wanted(s)]))
print(f"{'would cost' if DRY else 'cost of this run'}: about ${spent[0]:.2f}; balance {muapi.balance().get('balance')}")
