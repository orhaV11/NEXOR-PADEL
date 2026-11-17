"""Stills for a cut-out film: drawn by the picture model, logged in the film's ledger.

    from draw import Art
    art = Art("work/switch")                       # pictures in work/switch/art/, index in work/switch/art/index.json
    art.make({"name": prompt, ...})                # new pictures from words
    art.make({"name": (prompt, ["ref name or url", ...]), ...})   # pictures drawn from reference pictures (an edit)
A picture that already exists is not drawn again. Every picture's address at the service is kept in the index, so a
later picture can be drawn FROM it. 0.022 dollars a picture at any size.
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
import muapi  # noqa: E402
from spend import Ledger  # noqa: E402


class Art:
    def __init__(self, work, cap=5.0):
        self.dir = ROOT / work / "art"
        self.dir.mkdir(parents=True, exist_ok=True)
        self.index_path = self.dir / "index.json"
        self.index = json.loads(self.index_path.read_text()) if self.index_path.exists() else {}
        self.led = Ledger(ROOT / work / "ledger.jsonl", cap)

    def url(self, ref):
        if ref.startswith("http"):
            return ref
        if ref in self.index:
            return self.index[ref]["url"]
        p = Path(ref) if Path(ref).exists() else self.dir / ref
        key = f"upload:{p}"
        if key not in self.index:
            self.index[key] = {"url": muapi.upload(str(p))}
            self.index_path.write_text(json.dumps(self.index, indent=1))
        return self.index[key]["url"]

    def _one(self, item):
        name, spec, ratio, res = item
        prompt, refs = (spec, []) if isinstance(spec, str) else spec
        dst = self.dir / f"{name}.png"
        if refs:
            ep, payload = "nano-banana-pro-edit", {"prompt": prompt, "images_list": [self.url(r) for r in refs], "aspect_ratio": ratio, "resolution": res}
        else:
            ep, payload = "nano-banana-pro", {"prompt": prompt, "aspect_ratio": ratio, "resolution": res}
        try:
            rid = muapi.submit(ep, payload)
        except Exception as e:  # noqa: BLE001
            return name, None, f"not submitted: {str(e)[:200]}"
        res_ = muapi.wait(rid, every=5, limit=400)
        url = muapi.output_url(res_)
        if not url:
            return name, None, json.dumps(res_)[:300]
        raw = self.dir / f"{name}.src"
        muapi.download(url, raw)
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(raw), str(dst)], check=True)
        raw.unlink()
        return name, {"url": url, "prompt": prompt, "refs": refs, "endpoint": ep}, None

    def make(self, specs, ratio="9:16", res="2k", purpose="stills"):
        todo = [(n, s, ratio, res) for n, s in specs.items() if not (self.dir / f"{n}.png").exists()]
        if not todo:
            print("  all there already")
            return
        with self.led.paid(f"{purpose}: {len(todo)} pictures", expect=0.022 * len(todo)):
            with ThreadPoolExecutor(6) as ex:
                for name, rec, err in ex.map(self._one, todo):
                    if rec:
                        self.index[name] = rec
                        self.index_path.write_text(json.dumps(self.index, indent=1))
                    print(f"  {name}: {'ok' if rec else 'FAILED ' + err}", flush=True)
