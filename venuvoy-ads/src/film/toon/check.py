"""Ask the watching model about a finished sketch: what a stranger sees, hears and thinks. About 3 cents.

    python3 src/film/toon/check.py <film> [<file.mp4>]        (default: the newest 9x16 file the film rendered)

Three askings side by side: STORY (what happens, every line, the joke, how things enter and leave), VIEWER (an
ordinary person scrolling: marks, what fell flat, anything odd) and SOUND (every word, the music, the air, the
name). Answers are printed and kept in work/<film>/check.txt. The model looks at about one frame a second and its
marks move by two points between askings: use it for WHAT IT UNDERSTOOD and for faults named with a time, then look
at those frames yourself. It cannot replace a person's ear for the voices.
"""
import json
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))
import watch  # noqa: E402
from spend import Ledger  # noqa: E402

film = sys.argv[1]
work = ROOT / "work" / film
if len(sys.argv) > 2:
    mp4 = Path(sys.argv[2])
else:
    cands = sorted(ROOT.glob(f"ads/**/*{film}*9x16*.mp4"), key=lambda p: p.stat().st_mtime) or sorted(work.glob("**/*9x16*.mp4"), key=lambda p: p.stat().st_mtime)
    mp4 = cands[-1]
lines = json.loads((work / "sound" / "timeline.json").read_text())["lines"]
script = "\n".join(f'{"WOMAN" if l["who"] == "L" else "MAN"}: {l["text"]}' for l in lines)
Q = {
    "STORY": "Watch this short vertical video with its sound. Without guessing beyond what you see and hear: (1) describe what happens, shot by shot, with times; (2) write every spoken line, "
             "word for word, with who says it; (3) explain the joke in two sentences; (4) what is being advertised and when did you first realise it; (5) describe the very first second: who and "
             "what is in the picture; (6) name everything that comes into the picture or leaves it (a hand, an object, a person) and say exactly how it comes or goes; (7) what is the last picture.",
    "VIEWER": "Watch this short vertical video with its sound, as an ordinary person scrolling a feed would. Answer plainly and briefly: (1) In one sentence, what kind of video is this and how "
              "was it made, as far as you can tell? (2) Did it make you smile? Which moment was funniest, and which moment fell flat? (3) Score 1-10 each: how it looks, how it moves, how the "
              "voices sound, how funny it is, overall. (4) Do the voices sound like real people talking or like computer voices? (5) Was anything confusing or odd to look at: a part that floats, "
              "a hard edge, something that pops in or vanishes, text over a face, a mouth that does not move? Give the time. (6) Is it always clear who is speaking, even with the sound off? "
              "(7) Would you watch another one with these two characters?",
    "SOUND": "Listen carefully to the sound of this video. (1) Write down every spoken line, word for word, with who says it. The script is:\n" + script + "\nName every place where what you hear "
             "differs from it. (2) Is there background music? From when to when, and is it too loud, right or too quiet against the voices? (3) What else do you hear besides voices and music "
             "(the place, an effect), and does it fit the picture? (4) Is any line much louder or quieter than the others, clipped at its start or end, or hard to understand? (5) What is heard "
             "over the last picture, and is it clear? (6) Anything harsh, cheap or wrong, with times.",
}
led = Ledger(work / "ledger.jsonl")
with led.paid(f"watching model: three askings of {mp4.name}"):
    watch.url_of(mp4)
    with ThreadPoolExecutor(3) as ex:
        res = list(ex.map(lambda kv: (kv[0], watch.ask(mp4, kv[1], None, 1100)), Q.items()))
text = "".join(f"\n=================== {k} ===================\n{r}\n" for k, r in res)
(work / "check.txt").write_text(f"{mp4}\n{text}")
print(text)
