"""Choose, line by line, which reading of a sketch to use: the listening model hears every take of every line.

    python3 src/film/toon/pick.py <sketch.json> <take> <take> [<take> ...]

Run lines.py on the takes first. All takes of all lines are laid on one numbered reel ("LINE 3 TAKE 2"); the
listening model names the best take of each line and says why. About one cent, through the film's ledger. Its choice
is advice: a take in which a word is missing or a direction was read aloud (see the words heard back, printed by
voices.py and lines.py) is out whatever it says. Nobody here can hear: a person must judge the voices in the end.
"""
import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent.parent
sys.path.insert(0, str(HERE))
from hear import ask, reel  # noqa: E402
from spend import Ledger  # noqa: E402

sk = json.load(open(sys.argv[1]))
takes = sys.argv[2:]
V = ROOT / sk["work"] / "voice"
items, listing = [], []
for n, (who, text) in enumerate(sk["lines"], 1):
    for k, take in enumerate(takes, 1):
        wav = V / "lines" / f"{take}_{n:02d}.wav"
        if wav.exists():
            items.append((f"LINE {n} TAKE {k}", wav))
    direction = " ".join(re.findall(r"\[(.*?)\]", text))
    listing.append(f'LINE {n} ({"the woman" if who == "L" else "the man"}; meant to be: {direction or "natural"}): "{re.sub(r"\[.*?\]\s*", "", text)}"')
led = Ledger(ROOT / sk["work"] / "ledger.jsonl", sk.get("cap", 5.0))
with led.paid(f"listening model: the best take of each line ({len(takes)} takes)"):
    path = reel(items, V / "reel_pick.mp4", gap=0.45)
    answer = ask(path, "This reel plays every line of a short comedy sketch between two friends several times: each clip is announced by a card, \"LINE n TAKE k\". "
                 "The same two voices speak all takes. For EACH line choose the take that sounds most like a real person saying it to a friend: natural rhythm, the right feeling, "
                 "clear words, nothing clipped at the start or the end, no odd noise. The lines and how each is meant:\n" + "\n".join(listing) +
                 f"\nTAKE 1 to TAKE {len(takes)} are: {', '.join(takes)}.\nAnswer first with one line of JSON, exactly like {{\"1\": 2, \"2\": 1, ...}} (line number: take number), "
                 "then one short sentence per line saying why, and name any take in which you hear a wrong word, a cut-off word or a strange sound.", limit=600)
print(answer)
m = re.search(r"\{[^{}]*\}", answer)
if m:
    try:
        picks = {int(a): takes[int(b) - 1] for a, b in json.loads(m.group(0)).items()}
        (V / "picks.json").write_text(json.dumps(picks, indent=1))
        print("picks saved in", V / "picks.json", picks)
    except Exception as e:  # noqa: BLE001
        print("could not read the picks:", e)
