"""Shoot a film of "Plan, No Plan": a start picture for every shot, then the clip.

    python3 src/film/s2/shoot.py <plan.json> stills [id ...] [--n 2]      draw start pictures (n candidates each)
    python3 src/film/s2/shoot.py <plan.json> video  [id ...] [--takes 2]  film the clips from the picked pictures
                                                                          (--takes N: until every shot has N takes)
    python3 src/film/s2/shoot.py <plan.json> cost                         what the clips will cost, nothing spent
    python3 src/film/s2/shoot.py <plan.json> sheet                        one contact sheet of every picked picture

plan.json
  { "cast": "src/film/assets/s2/cast/cast.json",      the reference pictures and voice samples (URLs)
    "out":  "src/film/assets/s2/pilot",               stills/, clips/ and state.json live here
    "look": "words added to every start-picture prompt",
    "film": "words added to every clip prompt",
    "shots": [ { "id": "s1",
                 "refs": ["lineup", "timo_face"],     cast pictures the start picture is drawn from (keys of cast.json)
                 "still": "what the first frame shows",
                 "pick": 1,                            which candidate is used (1-based); set after looking
                 "elements": ["Timo", "Luna"],         who is in the clip (names in ELEMENTS below; "@Timo" in the prompt)
                 "video": "what moves and what is said; lines in double quotes",
                 "duration": 5, "resolution": "1080p" } ] }

How the people stay the same: every clip is Kling 4.0 reference-to-video. Each character is an "element" with its
reference pictures and, for Timo and Luna, a voice sample, so the face AND the voice return in every shot. The start
picture is passed as @image_1 and the prompt begins "Start exactly from @image_1": the clip only has to add small
movement to a frame that already shows the pose. Start pictures are cropped to exactly 9:16 before use, because a
picture of a slightly different shape is what puts a band at the edge of a clip.

Nothing is made twice: state.json remembers every picture and clip. To make one again, delete its entry or pass
--redo. A failed job costs nothing.
"""
import json
import subprocess
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE.parent))
import muapi  # noqa: E402

STILL_MODEL, VIDEO_MODEL = "nano-banana-pro-edit", "kling-4.0-reference-to-video"
ELEMENTS = {  # name -> (pictures in cast.json, tag, description, voice key)
    "Timo": (["timo_front", "timo_34", "timo_face", "timo_back"], "character", "tall thin young man, flat-top hair, teal track jacket, navy trousers, yellow watch", "voice_timo"),
    "Luna": (["luna_front", "luna_34", "luna_face", "luna_back"], "character", "short young woman, coral bob, coral football shirt, denim shorts", "voice_luna"),
    "BigYellow": (["big_side", "big_34"], "prop", "giant yellow hard-shell suitcase, violet wheels, short fixed handle", None),
    "Cat": (["cat_side", "cat_front"], "animal", "fat ginger street cat, cream belly, grumpy half-closed eyes", None),
}

VALUED = {"--n", "--takes"}  # options that take a value
args, _skip = [], False
for _a in sys.argv[1:]:
    if _skip:
        _skip = False
    elif _a in VALUED:
        _skip = True
    elif not _a.startswith("--"):
        args.append(_a)
opt = lambda k, d=None: (sys.argv[sys.argv.index(k) + 1] if k in sys.argv else d)  # noqa: E731
PLAN_PATH, CMD, ONLY = args[0], args[1], set(args[2:])
plan = json.load(open(PLAN_PATH))
cast = json.load(open(ROOT / plan["cast"]))
OUT = ROOT / plan["out"]
(OUT / "stills").mkdir(parents=True, exist_ok=True)
(OUT / "clips").mkdir(parents=True, exist_ok=True)
STATE = OUT / "state.json"
state = json.loads(STATE.read_text()) if STATE.exists() else {"stills": {}, "clips": {}}
REDO = "--redo" in sys.argv


def save():
    """Write state.json, keeping what another run of this script wrote meanwhile (stills and clips are often made at
    the same time by two runs; without this the later writer wiped the earlier one's entries)."""
    disk = json.loads(STATE.read_text()) if STATE.exists() else {}
    for sid, cands in disk.get("stills", {}).items():
        mine = state["stills"].setdefault(sid, {})
        for k, v in cands.items():
            mine.setdefault(k, v)
    for k, v in disk.get("uploads", {}).items():
        state.setdefault("uploads", {}).setdefault(k, v)
    for sid, c in disk.get("clips", {}).items():
        mine = state["clips"].setdefault(sid, {"takes": []})
        have = {t["take"] for t in mine["takes"]}
        mine["takes"] += [t for t in c.get("takes", []) if t["take"] not in have]
        mine["takes"].sort(key=lambda t: t["take"])
        mine.setdefault("file", c.get("file"))
    STATE.write_text(json.dumps(state, ensure_ascii=False, indent=1))


def shots():
    return [s for s in plan["shots"] if not ONLY or s["id"] in ONLY]


def run(endpoint, payload, path, limit=1500):
    """Submit, wait, download. Returns (url, error)."""
    try:
        rid = muapi.submit(endpoint, payload)
    except Exception as e:  # noqa: BLE001
        return None, f"submit failed: {str(e)[:300]}"
    res = muapi.wait(rid, every=8, limit=limit)
    url = muapi.output_url(res)
    if not url:
        return None, json.dumps(res, ensure_ascii=False)[:400]
    muapi.download(url, path)
    return url, None


def crop916(src, dst):
    """Exactly 9:16, cut from the middle, so the clip has no band."""
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(src), "-vf", "crop='min(iw,ih*9/16)':'min(ih,iw*16/9)',scale=1080:1920:flags=lanczos", "-q:v", "2", str(dst)], check=True)


def do_stills():
    n = int(opt("--n", 2))
    jobs = []
    for s in shots():
        have = state["stills"].setdefault(s["id"], {})
        for k in range(1, n + 1):
            if str(k) in have and not REDO:
                continue
            prompt = f'{s.get("look", plan.get("look", ""))} {s["still"]}'.strip()  # a shot may bring its own look (an interior)
            payload = {"prompt": prompt, "images_list": [cast[r] if r in cast else r for r in s["refs"]], "aspect_ratio": "9:16", "resolution": "2k"}
            jobs.append((s["id"], k, payload))

    def one(j):
        sid, k, payload = j
        raw = OUT / "stills" / f"{sid}_{k}_raw.jpg"
        url, err = run(STILL_MODEL, payload, raw, limit=500)
        if err:
            return sid, k, None, err
        crop916(raw, OUT / "stills" / f"{sid}_{k}.jpg")
        raw.unlink()
        return sid, k, url, None

    with ThreadPoolExecutor(6) as ex:
        for sid, k, url, err in ex.map(one, jobs):
            if url:
                state["stills"][sid][str(k)] = {"url": url}
            print(f"still {sid}_{k}: {'ok' if url else 'FAILED ' + err}", flush=True)
            save()


def element(name):
    pics, tag, desc, voice = ELEMENTS[name]
    e = {"name": name, "image_urls": [cast[p] for p in pics if p in cast][:4], "tag": tag, "description": desc[:100]}
    if voice and cast.get(voice):
        e["voice_url"] = cast[voice]
    return e


def payload_of(s):
    pick = str(s.get("pick", 1))
    still = OUT / "stills" / f'{s["id"]}_{pick}.jpg'
    key = f'{s["id"]}_{pick}'
    up = state.setdefault("uploads", {})
    if key not in up or REDO:  # the cropped picture is what the clip starts from
        up[key] = muapi.upload(str(still))
        save()
    prompt = f'Start exactly from @image_1 as the first frame. {plan.get("film", "")} {s["video"]}'.strip()
    return {"prompt": prompt, "images_list": [up[key]], "elements": [element(n) for n in s.get("elements", [])],
            "duration": s.get("duration", 5), "resolution": s.get("resolution", "1080p"), "aspect_ratio": "9:16"}


def do_video():
    want = int(opt("--takes", 1))
    todo = []  # (shot, take number): with --takes N a shot is filmed until it has N takes; --redo adds one more
    for s in shots():
        if not s.get("video"):
            continue
        have = len(state["clips"].get(s["id"], {}).get("takes", []))
        new_takes = max(0, want - have) or (1 if REDO or not have else 0)
        if new_takes:
            s["_payload"] = payload_of(s)
        todo += [(s, have + k + 1) for k in range(new_takes)]

    # A job is paid for when it is submitted and takes minutes; if this machine restarts meanwhile, the clip must not
    # be lost or paid twice. So every request id is written to pending.json the moment it exists, and a new run first
    # collects what an earlier run left there.
    PENDING = OUT / "pending.json"
    lock = threading.Lock()

    def pending(add=None, drop=None):
        with lock:
            cur = json.loads(PENDING.read_text()) if PENDING.exists() else []
            if add:
                cur.append(add)
            if drop:
                cur = [x for x in cur if x["rid"] != drop]
            PENDING.write_text(json.dumps(cur, indent=1))
            return cur

    def collect(sid, take, rid, prompt, pick):
        path = OUT / "clips" / f"{sid}_t{take}.mp4"
        res = muapi.wait(rid, every=8, limit=1500)
        url = muapi.output_url(res)
        if not url:
            if str(res.get("status")) != "timeout":
                pending(drop=rid)
            return None, json.dumps(res, ensure_ascii=False)[:400], path
        muapi.download(url, path)
        with lock:
            c = state["clips"].setdefault(sid, {"takes": []})
            c["takes"] = [t for t in c["takes"] if t["take"] != take] + [{"take": take, "url": url, "file": str(path.relative_to(ROOT)), "pick": pick, "prompt": prompt, "rid": rid}]
            c["takes"].sort(key=lambda t: t["take"])
            c["file"] = str(path.relative_to(ROOT))
            save()
        pending(drop=rid)
        return url, None, path

    left = pending()
    if left:
        print(f"collecting {len(left)} clip(s) an earlier run was waiting for", flush=True)
        with ThreadPoolExecutor(6) as ex:
            for x, (url, err, path) in zip(left, ex.map(lambda x: collect(x["shot"], x["take"], x["rid"], x.get("prompt", ""), x.get("pick", 1)), left)):
                print(f'clip {x["shot"]} take {x["take"]}: {"ok (collected)" if url else "FAILED " + str(err)}', flush=True)
        state.update(json.loads(STATE.read_text()))
        todo = []
        for s in shots():
            if not s.get("video"):
                continue
            have = len(state["clips"].get(s["id"], {}).get("takes", []))
            new_takes = max(0, want - have) or (1 if REDO or not have else 0)
            if new_takes:
                s["_payload"] = payload_of(s)
            todo += [(s, have + k + 1) for k in range(new_takes)]

    def one(job):
        s, take = job
        t0 = time.time()
        try:
            rid = muapi.submit(VIDEO_MODEL, s["_payload"])
        except Exception as e:  # noqa: BLE001
            return s, take, None, None, f"submit failed: {str(e)[:300]}", round(time.time() - t0)
        pending(add={"shot": s["id"], "take": take, "rid": rid, "prompt": s["_payload"]["prompt"], "pick": s.get("pick", 1)})
        url, err, path = collect(s["id"], take, rid, s["_payload"]["prompt"], s.get("pick", 1))
        return s, take, path, url, err, round(time.time() - t0)

    with ThreadPoolExecutor(6) as ex:
        for s, take, path, url, err, secs in ex.map(one, todo):
            print(f'clip {s["id"]} take {take}: {"ok" if url else "FAILED " + str(err)} ({secs} s)', flush=True)
    print("balance", muapi.balance().get("balance"))


def do_cost():
    tot = 0
    for s in shots():
        if not s.get("video"):
            continue
        c = muapi.cost(VIDEO_MODEL, {"prompt": "x", "elements": [{"name": "A", "image_urls": ["https://example.com/a.jpg", "https://example.com/b.jpg"]}],
                                     "duration": s.get("duration", 5), "resolution": s.get("resolution", "1080p"), "aspect_ratio": "9:16"}).get("cost") or 0
        tot += c
        print(f'{s["id"]}: {s.get("duration", 5)} s {s.get("resolution", "1080p")} ${c}')
    print(f"total ${tot:.2f}; balance {muapi.balance().get('balance')}")


def do_sheet():
    pics = [str(OUT / "stills" / f'{s["id"]}_{s.get("pick", 1)}.jpg') for s in shots()]
    pics = [p for p in pics if Path(p).exists()]
    ins = sum((["-i", p] for p in pics), [])
    flt = "".join(f"[{i}:v]scale=270:480[v{i}];" for i in range(len(pics))) + "".join(f"[v{i}]" for i in range(len(pics))) + f"hstack=inputs={len(pics)}"
    out = OUT / "strip.jpg"
    subprocess.run(["ffmpeg", "-v", "error", "-y"] + ins + ["-filter_complex", flt, str(out)], check=True)
    print(out)


{"stills": do_stills, "video": do_video, "cost": do_cost, "sheet": do_sheet}[CMD]()
