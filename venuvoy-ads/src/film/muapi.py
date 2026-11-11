"""A small client for MuAPI (api.muapi.ai), the generation service behind the Open-Generative-AI studio.

    python3 src/film/muapi.py balance
    python3 src/film/muapi.py cost   <endpoint> '<json payload>'
    python3 src/film/muapi.py submit <endpoint> '<json payload>'          -> request id
    python3 src/film/muapi.py wait   <request id> [out_file]              -> result json, and the file if asked
    python3 src/film/muapi.py batch  jobs.json out_dir                     -> runs many jobs side by side

jobs.json: [{"name": "shot01", "endpoint": "veo3.1-lite-text-to-video", "payload": {...}}, ...]
A batch writes <out_dir>/<name>.mp4 (or .png/.jpg/.mp3 by the result's extension), <name>.json with the request and
the result, and skips a job whose file is already there, so an interrupted batch can be run again.

The API key is never in this file or in the environment: the session's network proxy adds it to requests for
api.muapi.ai. The header below is a placeholder that the proxy overwrites.
"""
import json
import subprocess
import sys
import time
from pathlib import Path

BASE = "https://api.muapi.ai"
HDR = ["-H", "Content-Type: application/json", "-H", "x-api-key: proxy-injected"]


def _curl(args, timeout=60):
    r = subprocess.run(["curl", "-sS", "-m", str(timeout), "-w", "\n%{http_code}"] + args, capture_output=True, text=True)
    body, _, code = r.stdout.rpartition("\n")
    return int(code or 0), body, r.stderr.strip()


def _json(body):
    try:
        return json.loads(body)
    except Exception:
        return {"raw": body[:500]}


def balance():
    code, body, _ = _curl([f"{BASE}/api/v1/account/balance"] + HDR)
    d = _json(body)
    d.pop("email", None)
    return d


def cost(endpoint, payload):
    code, body, _ = _curl(["-X", "POST", f"{BASE}/api/v1/models/{endpoint}/estimate-cost", "-d", json.dumps(payload)] + HDR)
    return _json(body)


def submit(endpoint, payload):
    code, body, err = _curl(["-X", "POST", f"{BASE}/api/v1/{endpoint}", "-d", json.dumps(payload)] + HDR, timeout=120)
    d = _json(body)
    rid = d.get("request_id") or d.get("id")
    if code >= 300 or not rid:
        raise RuntimeError(f"submit {endpoint}: HTTP {code} {body[:300]} {err}")
    return rid


def result(rid):
    code, body, _ = _curl([f"{BASE}/api/v1/predictions/{rid}/result"] + HDR)
    return code, _json(body)


def output_url(res):
    out = res.get("outputs") or []
    if out:
        return out[0] if isinstance(out[0], str) else (out[0].get("url") if isinstance(out[0], dict) else None)
    return res.get("url") or (res.get("output") or {}).get("url") if isinstance(res.get("output"), dict) else res.get("url")


def wait(rid, every=6, limit=900):
    t0 = time.time()
    while time.time() - t0 < limit:
        code, res = result(rid)
        st = str(res.get("status", "")).lower()
        if st in ("completed", "succeeded", "success") or (code == 200 and output_url(res)):
            return res
        if st in ("failed", "error", "cancelled", "canceled"):
            return res
        time.sleep(every)
    return {"status": "timeout", "request_id": rid}


def download(url, path):
    r = subprocess.run(["curl", "-sS", "-L", "-m", "300", "-o", str(path), "-w", "%{http_code}", url], capture_output=True, text=True)
    return r.stdout.strip(), r.stderr.strip()


def batch(jobs, out_dir):
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    live = []
    for j in jobs:
        meta = out_dir / f"{j['name']}.json"
        if meta.exists() and json.loads(meta.read_text()).get("file"):
            print(f"  {j['name']}: already done")
            continue
        try:
            rid = submit(j["endpoint"], j["payload"])
            live.append((j, rid))
            print(f"  {j['name']}: submitted {rid}", flush=True)
        except Exception as e:  # noqa: BLE001
            print(f"  {j['name']}: NOT submitted: {e}", flush=True)
            meta.write_text(json.dumps({"job": j, "error": str(e)}, ensure_ascii=False, indent=1))
        time.sleep(0.6)
    t0 = time.time()
    while live and time.time() - t0 < 1500:
        for j, rid in list(live):
            code, res = result(rid)
            st = str(res.get("status", "")).lower()
            url = output_url(res) if code == 200 else None
            if st in ("failed", "error", "cancelled", "canceled") or url:
                live.remove((j, rid))
                rec = {"job": j, "request_id": rid, "result": res}
                if url:
                    ext = url.split("?")[0].rsplit(".", 1)[-1].lower()
                    ext = ext if ext in ("mp4", "mov", "webm", "png", "jpg", "jpeg", "webp", "mp3", "wav") else "mp4"
                    f = out_dir / f"{j['name']}.{ext}"
                    code_d, err = download(url, f)
                    rec.update(file=str(f) if code_d == "200" else None, url=url, download=code_d, download_error=err)
                    print(f"  {j['name']}: {st or 'done'} -> {f.name} (download {code_d}) {err[:120]}", flush=True)
                else:
                    print(f"  {j['name']}: {st}: {json.dumps(res, ensure_ascii=False)[:300]}", flush=True)
                (out_dir / f"{j['name']}.json").write_text(json.dumps(rec, ensure_ascii=False, indent=1))
        if live:
            time.sleep(6)
    for j, rid in live:
        print(f"  {j['name']}: still running after the time limit ({rid})")
        (out_dir / f"{j['name']}.json").write_text(json.dumps({"job": j, "request_id": rid, "result": {"status": "timeout"}}, ensure_ascii=False, indent=1))


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "balance":
        print(json.dumps(balance()))
    elif cmd == "cost":
        print(json.dumps(cost(sys.argv[2], json.loads(sys.argv[3]))))
    elif cmd == "submit":
        print(submit(sys.argv[2], json.loads(sys.argv[3])))
    elif cmd == "wait":
        res = wait(sys.argv[2])
        print(json.dumps(res, ensure_ascii=False)[:1500])
        if len(sys.argv) > 3 and output_url(res):
            print(download(output_url(res), sys.argv[3]))
    elif cmd == "batch":
        batch(json.load(open(sys.argv[2])), sys.argv[3])
