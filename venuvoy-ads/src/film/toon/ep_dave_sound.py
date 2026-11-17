"""The sound of the sketch "Official Dave" (vlog 07, "dave"): the forecourt of an ice hockey arena at winter dusk.

    python3 src/film/toon/ep_dave_sound.py

A stranger has sent Luna a link to tickets; his name is Official Dave. Timo has her paste it into the planner's free
link check, which comes back 'Could not verify.' Then Dave messages: hurry.

Her lines are from reading 1 (one voice colour; the listening model's choice for six of her seven lines), except
"It's in his name!" from reading 2 (in reading 1 its first word is clipped to "Is"; a cut follows it, so the change of
reading falls across the cut). His lines are from reading 1 (low and dead flat; in reading 2 his "Official Dave." rises
like a question), except "Paste." from reading 2 (in reading 1 the word is drawn out to almost a second: it must land
short, over her). Reading 3 is out: "planet" for "planner", and an invented "Could not verify his identity."

The air is the bought far crowd, dulled, with the bought cold wind low under it (work/dave/make_sounds.py makes the
mix); the one effect is the message ping (made there too: the bought one came back as a sonar pulse and a whine).

After the independent check: the ping sat 0.3 s after "verify" and was heard as the app's error chime, so it now
lands 0.62 s into his stare (0.66 s after the verdict, alone in the silence), and the stare is 1.05 s so that her
"He says hurry." follows the ping by about half a second. The 0.25 s are paid back: her first line starts at 0.12
(the phone is up from the first frame now), and her caught face is half a second as the script says.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from vlog_sound import build  # noqa: E402

A, B = "ldn_g25p_1", "ldn_g25p_2"
# id, who, reading, number of the line in the reading, gap before it (s after the previous line ends; negative = talks over), options
PLAN = [
    ("L1", "L", A, 1, 0.12, {}),                                        # "Good news! Ice hockey tickets!" phone up like a trophy from frame 1
    ("T2", "T", A, 2, 0.10, {}),                                        # "From where?" flat, at once
    ("L3", "L", A, 3, 0.14, {"tighten": 0.20}),                         # "A man sent me a link. Official Dave." the name read with respect
    ("T4", "T", A, 4, 0.40, {}),                                        # a beat, then "Official Dave." dead flat
    ("L5", "L", B, 5, 0.12, {}),                                        # she turns to him: "It's in his name!"
    ("CUT", 0.30),                                                      # a jump cut, a fifth closer
    ("T6", "T", A, 6, 0.14, {}),                                        # "The planner checks ticket links. Paste it."
    ("L7", "L", A, 7, 0.06, {"cut_in_word": ("any", 0.22, 0.10)}),      # "You never trust an—": he talks over the end of it
    ("T8", "T", B, 8, -0.20, {}),                                       # "Paste." under her voice
    ("HOLD", "bow", 0.12, 0.95),                                        # she bows to the phone, pastes, reads along a line ... oh
    ("L9", "L", A, 9, 0.06, {}),                                        # "Could not verify." small. The music stops dead as it ends
    ("HOLD", "look", 0.04, 1.05),                                       # his flat look into the lens; the ping comes into it late, on its own
    ("L10", "L", A, 10, 0.10, {"tighten": 0.12}),                       # "He says hurry." smaller still
    ("HOLD", "caught", 0.10, 0.50),                                     # her caught face to us, half a second; eyes down again
    ("L11", "L", A, 11, 0.10, {}),                                      # "Bye, Dave." to the phone, fond
    ("HOLD", "drop", 0.50, 0.70),                                       # the phone stays still half a second, then sinks out evenly
    ("PLATE", 0.30),
]
if __name__ == "__main__":
    build("dave", PLAN, bed={"stop": "L9"},
          ambience={"file": "forecourt", "db": -32.0, "gusts": False},
          fx=[{"name": "ping", "file": "ping2", "at": ("look", 0, 0.62), "whole": True, "start": 0.0, "len": 0.75,
               "fade_in": 0.002, "fade_out": 0.25, "db": -15}])
