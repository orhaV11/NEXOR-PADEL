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
    # places an episode paid for (BORROW below copies the painting and the air once)
    "arena": {"look": "calm", "scene": "A phone vlog from the seats of a full indoor basketball arena before the game. A big, warm crowd murmur all round.",
              "wide": (("bg_arena_in_1", (400, 900), (300, 500), 1.15), (362, 1545, 0.92, -0.6), (872, 960, 0.92)),
              "close": (("bg_arena_in_1", (400, 900), (250, 400), 1.44), (338, 1490, 1.16, 1.0), (856, 975, 1.16)),
              "air": {"file": "air_arena", "db": -30.0, "gusts": False}, "plate": {"x": 360, "y": 300, "size": 0.70}},
    "stand": {"look": "calm", "scene": "A phone vlog from high in the open stand of a full American football stadium in late afternoon sun. A big crowd all round.",
              "wide": (("bg_afstand_1", (300, 760), (170, 503), 0.78), (362, 1580, 0.92, -1.0), (885, 1020, 0.92)),
              "close": (("bg_afstand_1", (300, 760), (150, 420), 0.94), (345, 1500, 1.12, 1.0), (880, 935, 1.12)),
              "air": {"file": "air_stand", "db": -32.0, "gusts": False}, "plate": {"x": 352, "y": 300, "size": 0.74}},
    "dusk": {"look": "calm", "scene": "A phone vlog on the forecourt of a stadium at dusk, ten minutes before the game: a queue at the turnstiles behind, floodlights coming on.",
             "wide": (("bg_dusk_1", (560, 1380), (330, 760), 0.80), (372, 1556, 1.00, -1.2), (872, 1010, 1.00)),
             "close": (("bg_dusk_1", (560, 1380), (300, 640), 0.95), (345, 1490, 1.16, 1.0), (888, 905, 1.16)),
             "air": {"file": "air_dusk", "db": -33.0, "gusts": False}, "plate": {"x": 352, "y": 300, "size": 0.74}},
    "street": {"look": "wind", "scene": "A phone vlog on the street outside an arena on game night, a crowd some way behind. A breeze.",
               "wide": (("bg_street_1", (700, 1150), (330, 640), 1.0), (362, 1560, 0.86, 0.0), (884, 1036, 0.84)),
               "close": (("bg_street_1", (700, 1150), (300, 540), 1.2), (345, 1500, 1.04, 1.0), (888, 960, 1.02)),
               "air": {"file": "air_street", "db": -33.0, "gusts": False}, "plate": {"y": 320}},
    "court": {"look": "calm", "scene": "A phone vlog from the back rows above a tennis court on a still, sunny day. Everybody is whispering; far off, a ball is hit.",
              "wide": (("bg_tennis_1", (430, 960), (330, 560), 0.92), (366, 1552, 0.96, -1.0), (892, 960, 0.96)),
              "close": (("bg_tennis_1", (430, 960), (265, 440), 1.06), (318, 1495, 1.11, 1.0), (886, 880, 1.11)),
              "air": {"file": "air_court", "db": -31.0, "gusts": False}, "plate": {"x": 352, "y": 300, "size": 0.74}},
}

# what a place borrows from the episode that paid for it: (episode, its file) -> the shared name
BORROW = {"art": {"bg_arena_in_1": ("look", "bg_arena_1"), "bg_afstand_1": ("minutes", "bg_stand_1x"), "bg_dusk_1": ("battery", "bg_gate_1x"),
                  "bg_street_1": ("nicer", "bg_arena_1")},
          "air": {"air_arena": ("look", "air"), "air_stand": ("minutes", "air"), "air_dusk": ("battery", "crowd"), "air_street": ("nicer", "air"), "air_court": ("slowly", "air")}}


def borrow(name):
    """Copy the painting of the short's place into the shared drawings (work/duo/art) and its air into the short's
    own sounds, once."""
    import shutil
    P = PLACES[SHORTS[name]["place"]]
    for shot in ("wide", "close"):
        bg = P[shot][0][0]
        if bg in BORROW["art"]:
            film, file = BORROW["art"][bg]
            dst = ROOT / "work" / "duo" / "art" / f"{bg}.png"
            if not dst.exists():
                shutil.copy(ROOT / "work" / film / "art" / f"{file}.png", dst)
                print("  borrowed", bg, "from", film)
    air = P["air"]["file"]
    if air in BORROW["air"]:
        film, file = BORROW["air"][air]
        dst = ROOT / "work" / name / "sound" / "raw" / f"{air}.wav"
        if not dst.exists():
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(ROOT / "work" / film / "sound" / "raw" / f"{file}.wav", dst)
            print("  borrowed", air, "from", film)

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
    # ---- "One sentence.": she names the sport they are at, he gets one sentence
    "basket": {"nn": "30", "title": "One sentence: basketball", "place": "arena", "lines": [
        ("L", "[bright, over the crowd, to the camera, a dare] Basketball. One sentence.", {"face": None, "eyes": None}),
        ("T", "[unhurried, exact, to the camera] Both teams score a hundred times, and it's settled in the last four seconds, which take half an hour.", {"eyes": "lens", "gap": 0.35, "opts": {"tighten": 0.16}}),
        ("L", "[turning to him, pleased with herself] That was two sentences.", {"face": "turn", "gap": 0.30}),
        ("T", "[at once, flat, to her] There was a comma.", {"eyes": None, "gap": 0.10, "stop": True})],
        "end": {"eyes": "lens"}, "hold": 0.8,
        "captions": {"split": {"T2": [("Both teams score a hundred times,", None), ("and it's settled in the last four seconds,", "T2@and"), ("which take half an hour.", "T2@which")]}}},
    "gridiron": {"nn": "31", "title": "One sentence: American football", "place": "stand", "lines": [
        ("L", "[bright, over the crowd, to the camera, a dare] American football. One sentence.", {"face": None, "eyes": None}),
        ("T", "[flat, exact, to the camera] Eleven seconds of sport, and then a meeting.", {"eyes": "lens", "gap": 0.35, "stop": True}),
        ("L", "[turning to him, curious] And after the meeting?", {"face": "turn", "gap": 0.35}),
        ("T", "[flat, to her, at once] Another meeting.", {"eyes": None, "gap": 0.12})],
        "end": {"face": None, "eyes": "lens"}, "hold": 0.75},
    # ---- "Question.": she reads a question off her phone; her answer is cheerful and wrong, his is one dry line
    "alone": {"nn": "40", "title": "Question: on your own", "place": "dusk", "lines": [
        ("L", "[reading off her phone, bright] Question. 'Is it weird to go to a game on your own?'", {"face": "read", "eyes": None, "piece": "phone", "opts": {"tighten": 0.18}}),
        ("T", "[to the camera, at once, flat] No. It's the best way to go.", {"face": None, "eyes": "lens", "gap": 0.12}),
        ("L", "[turning to him, amused] You've never been on your own.", {"face": "turn", "gap": 0.25, "stop": True}),
        ("T", "[flat, to her, after a beat] I've never been allowed.", {"eyes": None, "gap": 0.40})],
        "end": {"eyes": "lens"}, "hold": 0.75,
        "captions": {"split": {"L1": [("Question.", None), ("'Is it weird to go to a game on your own?'", "L1@is")]}}},
    "third": {"nn": "41", "title": "Question: name the third", "place": "street", "lines": [
        ("L", "[reading off her phone] Question. 'How many games fit in one weekend?'", {"face": "read", "eyes": None, "piece": "phone", "opts": {"tighten": 0.18}}),
        ("L", "[bright, to the camera, certain] Four!", {"face": None, "gap": 0.22}),
        ("T", "[to the camera, flat] Two. And you remember both.", {"eyes": "lens", "gap": 0.16}),
        ("L", "[turning to him, indignant] We did four in a weekend.", {"face": "turn", "gap": 0.14}),
        ("T", "[flat, to her] Name the third.", {"eyes": None, "gap": 0.14, "stop": True})],
        "end": {"face": "down", "eyes": "lens"}, "hold": 0.9, "said": {"did": "C E", "weekend": "E E C"},
        "captions": {"split": {"L1": [("Question.", None), ("'How many games fit in one weekend?'", "L1@how")]}}},
    "turnup": {"nn": "42", "title": "Question: just turn up", "place": "gates", "lines": [
        ("L", "[reading off her phone] Question. 'Do you even need a plan? Can't you just turn up?'", {"face": "read", "eyes": None, "piece": "phone", "opts": {"tighten": 0.18}}),
        ("L", "[bright, to the camera] Yes! Just turn up!", {"face": None, "gap": 0.22}),
        ("T", "[to the camera, flat] We just turned up once.", {"eyes": "lens", "gap": 0.16}),
        ("L", "[turning to him, fondly] That was a great weekend.", {"face": "turn", "gap": 0.16, "stop": True}),
        ("T", "[flat, to her] The game was the weekend after.", {"eyes": None, "gap": 0.30})],
        "end": {"face": "guilty", "eyes": "lens"}, "hold": 0.8, "said": {"that": "E C", "was": "O E", "great": "C E C", "weekend": "E E C"},
        "captions": {"split": {"L1": [("Question.", None), ("'Do you even need a plan?'", "L1@do"), ("'Can't you just turn up?'", "L1@can")]}}},
    # ---- "It's that week.": the moments of every fan's year, nothing named
    "top": {"nn": "50", "title": "First day of the season", "place": "dusk", "lines": [
        ("L", "[sunny, to the camera] First day of the season! We're top of the table.", {"face": None, "eyes": None, "opts": {"tighten": 0.18}}),
        ("T", "[flat, to her] Nobody has played.", {"gap": 0.14}),
        ("L", "[sunny, unmoved, to the camera] And we're top.", {"gap": 0.12, "stop": True}),
        ("T", "[to the camera, after a beat] It's alphabetical.", {"eyes": "lens", "gap": 0.45})],
        "end": {}, "hold": 0.8},
    "tennis": {"nn": "32", "title": "One sentence: tennis", "place": "court", "bed": None, "lines": [      # no music: everybody whispers
        ("L", "[very low, close to the phone, almost a whisper, a dare] Tennis. One sentence.", {"face": None, "eyes": None}),
        ("T", "[very low, close to the phone, almost a whisper, exact] Four thousand people keep quiet so two people can grunt.", {"eyes": "lens", "gap": 0.35, "opts": {"tighten": 0.16}}),
        ("L", "[very low, almost a whisper, turning to him, doubtful] That can't be all of it.", {"face": "turn", "gap": 0.30}),
        ("T", "[very low, almost a whisper, flat] There's also a man on a very tall chair.", {"eyes": None, "gap": 0.14})],
        "end": {"face": None, "eyes": "lens"}, "hold": 0.8, "said": {"that": "E C", "cant": "C E C", "all": "O C"},
        "captions": {"split": {"T2": [("Four thousand people keep quiet", None), ("so two people can grunt.", "T2@so")]}}},
    "early": {"nn": "43", "title": "Question: how early?", "place": "arena", "lines": [
        ("L", "[reading off her phone] Question. 'How early should I get there?'", {"face": "read", "eyes": None, "piece": "phone", "opts": {"tighten": 0.18}}),
        ("T", "[to the camera, at once, flat] Two hours.", {"face": None, "eyes": "lens", "gap": 0.12}),
        ("L", "[turning to him, baffled] What do you do for two hours?", {"face": "turn", "gap": 0.22, "stop": True}),
        ("T", "[flat, to her, after a beat] Watch you not be there.", {"eyes": None, "gap": 0.40})],
        "end": {"face": "guilty", "eyes": "lens"}, "hold": 0.8, "said": {"what": "O E", "hours": "E O C"},
        "captions": {"split": {"L1": [("Question.", None), ("'How early should I get there?'", "L1@how")]}}},
    "language": {"nn": "44", "title": "Question: the language", "place": "street", "lines": [
        ("L", "[reading off her phone] Question. 'Do I need the language?'", {"face": "read", "eyes": None, "piece": "phone", "opts": {"tighten": 0.18}}),
        ("L", "[bright, to the camera] No! Just smile!", {"face": None, "gap": 0.22}),
        ("T", "[to the camera, flat, counting] You need three words. Two. Please. Which gate.", {"eyes": "lens", "gap": 0.16, "opts": {"tighten": 0.16}}),
        ("L", "[turning to him, triumphant] That's four words.", {"face": "turn", "gap": 0.20, "stop": True}),
        ("T", "[flat, to her] Which gate is one word when you're late.", {"eyes": None, "gap": 0.30})],
        "end": {"face": "guilty", "eyes": "lens"}, "hold": 0.8, "said": {"thats": "E C", "four": "C O", "words": "O C"},
        "captions": {"split": {"L1": [("Question.", None), ("'Do I need the language?'", "L1@do")],
                               "T3": [("You need three words.", None), ("'Two.' 'Please.' 'Which gate.'", "T3@two")]},
                     "text": {"T5": "'Which gate' is one word when you're late."}}},
    "summer": {"nn": "51", "title": "The day after the season", "place": "gates", "lines": [
        ("L", "[low, to the camera, a secret] The season ended yesterday.", {"face": "guilty", "eyes": "shut"}),
        ("T", "[eyes shut, not moving, flat] Wake me in August.", {"gap": 0.30}),
        ("L", "[reading off her phone, innocent] There's baseball all summer.", {"face": "read", "piece": "phone", "gap": 0.30, "stop": True}),
        ("T", "[after a long beat, slowly, suddenly interested] ...Where?", {"eyes": "narrow", "gap": 0.75})],
        "end": {"face": None}, "hold": 0.8},
    "draw": {"nn": "52", "title": "The draw", "place": "street", "lines": [
        ("L", "[reading off her phone, small] The draw's out. We got them away.", {"face": "read", "eyes": None, "piece": "phone", "piece_to": 3}),
        ("T", "[flat, to her] How far away?", {"gap": 0.16}),
        ("L", "[still reading, smaller] ...Far.", {"gap": 0.35}),
        ("T", "[eyes shut, resigned] It'll be a Tuesday night.", {"eyes": "shut", "gap": 0.35, "stop": True}),
        ("L", "[sunny, to the camera, delighted] Sports trip!", {"face": None, "gap": 0.25})],
        "end": {}, "hold": 0.7},
    "fixtures": {"nn": "53", "title": "The fixtures are out", "place": "dusk", "lines": [
        ("L", "[reading off her phone, fast, thrilled] The fixtures are out! I've picked three weekends.", {"face": "read", "eyes": None, "piece": "phone", "opts": {"tighten": 0.18}}),
        ("T", "[flat, to her] They came out four minutes ago.", {"gap": 0.14}),
        ("L", "[sunny, to the camera] Four good minutes.", {"face": None, "gap": 0.16, "stop": True}),
        ("T", "[to the camera, after a beat] They'll move every one of those games.", {"eyes": "lens", "gap": 0.40})],
        "end": {"face": "guilty"}, "hold": 0.75},
    "leave": {"nn": "60", "title": "When do you leave?", "place": "stand", "lines": [
        ("L", "[bright, to the camera] When do you leave a game?", {"face": None, "eyes": None}),
        ("T", "[to the camera, at once, flat] Eighty-five minutes. Beat the crowd.", {"eyes": "lens", "gap": 0.12}),
        ("L", "[turning to him, accusing] He's missed four winning goals.", {"face": "turn", "gap": 0.22, "stop": True}),
        ("T", "[to the camera, flat] And not one train.", {"gap": 0.35})],
        "end": {"face": None}, "hold": 0.8, "said": {"hes": "C E C", "winning": "C E C", "goals": "O C"}},
    "pack": {"nn": "61", "title": "What do you pack?", "place": "gates", "lines": [
        ("L", "[bright, to the camera] What do you pack for a sports trip?", {"face": None, "eyes": None}),
        ("T", "[to the camera, flat] What's on the list.", {"eyes": "lens", "gap": 0.14}),
        ("L", "[turning to him, alarmed] There's a list?", {"face": "turn", "gap": 0.20, "stop": True}),
        ("T", "[to the camera, flat, proud] It's laminated.", {"gap": 0.45})],
        "end": {"face": "guilty"}, "hold": 0.8, "said": {"theres": "E E C", "list": "C E C"}},
    "source": {"nn": "62", "title": "Source?", "place": "dusk", "lines": [
        ("L", "[reading off her phone, pleased] Sunday, four p.m.: the match.", {"face": "read", "eyes": None, "piece": "phone", "piece_to": 3}),
        ("T", "[flat, to her, suspicious] Source?", {"eyes": "narrow", "gap": 0.30}),
        ("L", "[reading on, surprised] ...It actually says the source. And the day they checked it.", {"gap": 0.40, "opts": {"tighten": 0.18}}),
        ("T", "[to the camera, proud] I don't travel on a rumour.", {"eyes": "lens", "gap": 0.35, "stop": True})],
        "end": {"face": None}, "hold": 0.8,
        "captions": {"text": {"L1": "'Sunday, 4 p.m.: the match.'"}}},
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
    borrow(name)
    build(name, plan, bed=sh.get("bed", {"stop": stop or "PLATE"}), ending="beat", plate_hold=1.5, ambience=PLACES[sh["place"]]["air"], fx=fx)


def picture(name):
    from vlog import Vlog
    sh, P, I = SHORTS[name], PLACES[SHORTS[name]["place"]], ids(SHORTS[name])
    borrow(name)
    v = Vlog(name, f"ads/21-vlog/venuvoy-vlog-{sh['nn']}-{name}", luna=P["look"], timo="timo_clean", said=sh.get("said"))
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
        if o.get("piece"):     # "piece_to": the number of the last line it stays up for
            v.piece(o["piece"], up=f"{I[n]}-0.38", down=f"{I[o.get('piece_to', n + 1) - 1]}.end+0.25", lift=110 if o["piece"] == "phone" else 100)
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
