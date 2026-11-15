"""Let a model WATCH a video (picture and sound) and answer questions about it.

    python3 src/film/watch.py <video.mp4> "<question>" [--raw]

The editor of these films cannot hear and cannot play video; this gives every clip and every finished film a viewer
that can. It sends a small copy of the file (540 px wide) to MuAPI's "gemini-video-vision" model and prints the
answer. About $0.004 a viewing.

    import watch
    text = watch.ask("film.mp4", "What happens in this video? ...")
    answers = watch.panel("film.mp4", {"muted": "...", "fan": "..."})      # several viewers side by side

A viewer like this is kinder than a person and sometimes invents details, so ask it to RETELL and to DESCRIBE,
compare that with what was intended, and never take its praise as proof.
"""
import json
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import muapi  # noqa: E402

_urls = {}


def _small(path, width=540):
    """A light copy for upload: the model does not need the master."""
    out = Path(tempfile.gettempdir()) / f"watch_{abs(hash((str(path), Path(path).stat().st_mtime)))}.mp4"
    if not out.exists():
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(path), "-vf", f"scale={width}:-2", "-c:v", "libx264", "-crf", "25",
                        "-preset", "veryfast", "-c:a", "aac", "-b:a", "128k", str(out)], check=True)
    return out


def url_of(path):
    key = (str(path), Path(path).stat().st_mtime)
    if key not in _urls:
        _urls[key] = muapi.upload(str(_small(path)))
    return _urls[key]


def ask(path, question, system=None, limit=400):
    payload = {"prompt": question, "video_url": url_of(path)}
    if system:
        payload["system_prompt"] = system
    res = {}
    for _ in range(3):  # the service sometimes answers "internal error": ask again
        rid = muapi.submit("gemini-video-vision", payload)
        res = muapi.wait(rid, every=5, limit=limit)
        out = res.get("outputs") or []
        if out and isinstance(out[0], str):
            return out[0]
    return "NO ANSWER: " + json.dumps(res, ensure_ascii=False)[:600]


def panel(path, questions):
    """{name: question} -> {name: answer}, asked side by side."""
    url_of(path)  # upload once
    with ThreadPoolExecutor(max(1, len(questions))) as ex:
        return dict(zip(questions, ex.map(lambda q: ask(path, q), questions.values())))


if __name__ == "__main__":
    a = [x for x in sys.argv[1:] if not x.startswith("--")]
    print(ask(a[0], a[1]))
