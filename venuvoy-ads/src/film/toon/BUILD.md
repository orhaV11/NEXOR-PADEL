# How to build a vlog sketch with the kit

Read `RIG.md` first (what the two drawn characters can do). Then read the two example files, which are the first
film written with the kit and run as they stand: `example_sound.py` and `example_film.py`. A new sketch is three
small files of its own and nothing else:

    src/film/toon/ep_<name>_voices.json     the words, how they are said, the voices
    src/film/toon/ep_<name>_sound.py        the table of lines and gaps (the comic timing), the air, the music
    src/film/toon/ep_<name>.py              who does what when (shots, faces, eyes, hands, captions)

`<name>` is one short lowercase word. Scratch goes to `work/<name>/` (not stored); the film goes to
`ads/21-vlog/venuvoy-vlog-<NN>-<name>-en_9x16_1080x1920.mp4` with its 4:5 version beside it. Run everything from the
project root (`venuvoy-ads/`). Do NOT edit the shared files (`toon.py`, `duo.py`, `vlog.py`, `vlog_sound.py`,
`text.mjs`, the other tools, anything of another sketch): if the kit cannot do something, do it in your own picture
file with `behind=` / `front=` (see the example), or leave it out and say so. Do not commit to git.

## Money

Every paid tool below writes to the film's ledger (`work/<name>/ledger.jsonl`) and stops at the film's cap. A
sketch should cost about 60 cents, a short about 20. Never buy credits, never call a video model, never ask anyone
for a key (the service key is injected by the network; the code sends a placeholder). One reading set, one drawing
per thing: draw again only for a named defect, with the prompt changed. See what was spent:
`python3 src/film/toon/spend.py work/<name>/ledger.jsonl`.

## 1. Voices (about 15 cents)

Copy `switch_voices.json` to `ep_<name>_voices.json` and change: `"work": "work/<name>"`, `"cap"`, the `scene` (the
place, in two sentences), the `context` (keep its wording about two real friends, quick and natural, never an advert;
describe the two as they are in THIS sketch), the `lines`, and in `casts` keep ONLY `"ldn"` exactly as it is (the
voices the founder has heard: Luna = Laomedeia, Timo = Zubenelgenubi) but rewrite the two `audio_profile` texts to
fit the scene (not "out of breath from walking uphill" unless she is).

Each line is `["L" or "T", "[how it is said] The words."]`. Directions are short and concrete ("[flat, not looking
up]", "[bright, to the camera]", "[off-screen, muffled]", "[cutting in fast]"). A line that is cut off ends with a
dash and the next line starts fast. No stage directions outside the brackets.

    python3 src/film/toon/voices.py src/film/toon/ep_<name>_voices.json ldn --model gemini-2-5-pro-tts --n 3
    python3 src/film/toon/lines.py  src/film/toon/ep_<name>_voices.json ldn_g25p_1 ldn_g25p_2 ldn_g25p_3
    python3 src/film/toon/pick.py   src/film/toon/ep_<name>_voices.json ldn_g25p_1 ldn_g25p_2 ldn_g25p_3

Three readings cost about 3 cents; if the first three give no usable take of some line, read three more.
`lines.py` now also prints `WRONG WORD HEARD` when a word came back as another word ("fronts" for "front"): such a
line is out, whatever `pick.py` says. A line shown as `MISSING` was not found: use another reading.
Take all the lines of a speaker from ONE reading when it is clean throughout (the voice colour shifts between
readings); mix readings only for a line that is wrong in the best one.
`voices.py` prints the words heard back in each reading: a reading in which a direction was read aloud, a word is
missing or the speakers are swapped is out. `lines.py` cuts each reading into its lines and prints `matched n/n` per
line: use only lines that matched fully. `pick.py` lets the listening model name the best take of each line
(`work/<name>/voice/picks.json`); it is advice. If no reading is usable, change the directions and read once more
(`--n 2` makes `ldn_g25p_1..2` again only if those files are gone: move the old ones away first).

## 2. Sound and timing (free, or 2 cents for the air of a place)

Write `ep_<name>_sound.py` after `example_sound.py`: one row per line, in order, with the take chosen for it and
the GAP before it. The gaps are the comic timing: a retort 0.05, a normal answer 0.12 to 0.2, a beat before a turn
0.3 to 0.45, a look into the camera 0.75 (as a `("CUT", 0.77)` after the line, or a `("HOLD", "look", 0.1, 0.7)`),
an interruption about -0.2 with `"cut_in_word"` on the line that is cut. `"tighten": 0.16` trims long silences
inside a line (not inside a punchline). `"ground": True` for a line spoken from outside the picture.

- The air: outdoors in wind, nothing to do (the wind of the first film). Any other place: buy ONE sound,
  `python3 src/film/toon/sfx.py <name> air "the murmur of a big crowd in an indoor arena before a game, no music, no announcer, no whistle" 10`,
  read what the listening model says it really is, and pass `ambience={"file": "air", "db": -32, "gusts": False}`.
- One effect at most (a thud exists: `"file": "thud_a"`). A bought effect must be heard back as what it should be.
  A HIT (a thud, a smack) is laid from just before its loudest moment; a SOFT sound (a groan, a coo, a crinkle) needs
  `"whole": True` with `"start"` and `"len"` (see the docstring of `build`). `"at"` may name a line: `("L3", 1, 0.2)`
  is 0.2 s after L3 ends. `pack.py` keeps only the sounds the plan names (the air and the `fx` files).
- The music: `bed={"stop": "<line id>"}` plays the vlog music from the start and stops it dead as that line ends:
  put the stop on the line before a look into the camera. A piece with no such moment: `bed={"stop": "PLATE"}`.
- A sketch ends `ending="sung"` (the sung name, plate 2.4 s, the default). A short ends
  `ending="beat", plate_hold=1.5`.

Run it; it prints every line's times and the events. Listen with your eyes: read the gaps aloud against a clock.

## 3. Drawings (2 cents each)

    from draw import Art
    art = Art("work/<name>")
    art.make({"bg_track": (PROMPT, ["work/switch/art/style_a_vector.png"])}, ratio="9:16", res="2k", purpose="background: the track")

- **A background** (9:16): "A background painting for a 2D animated comedy, with NO characters and no people.
  Vertical 9:16. Seen from a standing person's eye height, the way a phone held at arm's length sees what is behind
  its owner (never from above). <the place, plainly: what is near, what is far, where the light is>. The horizon is tilted two or
  three degrees. Painted softly by hand in the manner of the background of the reference picture: gentle muted
  colours, soft edges, far less detail and contrast than a character would have, no black outlines. Take only the
  painting manner from the reference picture and nothing of what it shows. No text, no signs, no letters, no logos." Ask for the interesting thing (the court, the track, the pitch) to sit in the UPPER
  LEFT third: Luna covers the lower left, Timo the right. Distant people only as soft shapes.
- **A thing in her free hand** (3:4, from `work/switch/art/luna_w1.png`): copy the wording of the shrugging hand in
  `work/switch/art/index.json` (key `luna_shrug`): ONE change, the forearm in the yellow sleeve comes up from the
  bottom right, the hand with the thing at chest height to the RIGHT of her body over the magenta background, clear
  of her scarf and hair, the sleeve outlined on both sides, a strip of magenta between her body and the forearm;
  change NOTHING else. Use it with `v.piece("<drawing>", up=..., down=..., lift=...)`. The piece is whatever the
  redraw shows where the base drawing has only background, so it works on the calm look too. `lift` raises it
  (the sleeve runs on below): up to about 110 is safe; ask for the hand at the height you want rather than lifting
  far. A caption in the middle may collide with a raised hand: look, and move the caption with `place=`.
  On the CALM look (indoors, a still day) draw it from `work/duo/art/luna_calm2.png` instead and pass
  `base="luna_calm2"` to `v.piece`. A tall thing (a foam finger, a scarf held up) needs its own box, e.g.
  `box=(1140, 560, 1792, 2400)`. A thing that is waved, lowered and raised again cannot be a `piece` (it only rises,
  holds and drops): draw it with a `front=` hook of your own, as `ep_queue.py` and `ep_look.py` do. Stand her arm a
  little toward her (about 45 px) so the sleeve meets her coat: in the free-arm place bottom right, a watching model
  took a foam finger for Timo's.
- **Something Timo wears or carries** (9:16, from `work/duo/art/timo_clean.png`): "Redraw the reference picture ...
  with ONE change only. THE CHANGE: he now wears <thing>, <where exactly>. His face, eyes, brows, mouth, head,
  scarf, jacket and pose do not change or move. Change NOTHING else ..." The thing must leave his brows, eyes and
  mouth free (they are separate drawings laid on top). Use it as `Vlog(..., timo="<drawing>")`.
- **A new face for her** (3:4, from `luna_w1.png`): only if the script cannot live without it; copy the wording of
  `luna_guilty` in the index; pass `extra_faces={"smug": "<drawing>"}` to `Vlog`. She does not blink on it.

A redraw at another aspect ratio than its reference comes back reframed and useless. After each redraw check that it
sits on its reference:

    python3 - <<'EOF'
    import sys; sys.path.insert(0, "src/film/toon")
    import numpy as np; from toon import read, align
    a = read("work/switch/art/luna_w1.png")[..., :3]; b = read("work/<name>/art/<drawing>.png")[..., :3]
    b, shift = align(a, b, None); d = np.abs(a - b).max(axis=2) > 0.12
    print("shift", shift, "changed share", d.mean().round(3))     # shift (0, 0) or one pixel; the changed share is the new thing only
    EOF

and LOOK at it (Read tool). Indoors: `Vlog("<name>", out, luna="calm", timo="timo_clean")` (a neat bob, no wind).
Outdoors in wind: `luna="wind"` and `timo="timo_clean"`.

## 4. The picture

Write `ep_<name>.py` after `example_film.py`. Order of work:

1. The shots and where the two stand. Start from the places that worked: Luna wide `(362, 1545, 0.92, 0.0)`, with
   him `(372, 1556, 1.0, -1.2)`, close `(345, 1490, 1.16, 1.0)`; Timo `(872, 935, 1.0)`, close `(890, 830, 1.16)`.
   Two shots in a row must differ in size by 15 per cent or be joined by a whip (`("CUT", gap, "whip")` in the
   sound plan). With her windy hair his mouth must stay above it: timo y < luna y - 487 x size - 90.
   The background's own point of interest goes top left: `bg=(drawing, point in the drawing, where it lands, size)`;
   the picture must cover the whole frame with 60 px to spare (look at the corners of the frames).
   THE PLATE at the end is about 900 px wide and sits at the top (y 207 to 497): it must not land on Timo. If he is
   still standing at the end, either stand him low in the last shot (his y at least 480 + 682 x size: possible with
   the calm look, where no flying hair has to stay under his mouth) or shrink and move the plate beside his head:
   `v.plate(x=352, y=300, size=0.74)`.
2. `--sheet` (a contact sheet of the whole film, two frames a second) and LOOK at it. Then her faces, his eyes, the
   hands, the finger, the sink, the captions; `--sheet` again; `--stills` at every moment something changes.
3. The kit prints a warning for every caption outside the safe area (x 40 to 955, y 190 to 1510) or up too briefly:
   move it with `place=`, split it with `split=`, or shorten the words with `text=` (the meaning must stay).
4. The whole film: `python3 src/film/toon/ep_<name>.py` (about four minutes for a short, seven or more for a
   sketch, longer when other films render at the same time). It also writes the 4:5 version and,
   in `work/<name>/qc/`, four frames a second and a contact sheet.

### What a film may add in its own picture file

`v.shot(..., behind=f, mid=f, front=f, grade=g)`: `f(st, t, T)` draws more with `st.put(picture, x, y, scale=, turn=,
anchor=, depth=)` (pictures are premultiplied RGBA; see `toon.py`): `behind` the two of them (a car that streaks
past: a blurred bar of colour moved across; a far crowd that ripples), `mid` between Timo and Luna (something he
holds up, a thing that lands on his hair), `front` in front of both and under the captions (rain, a pillar close to
the lens). `g(frame, T)` changes the finished picture of the shot (a cold grade for night, a jolt of light).
`v.timo_xy(T, point)` and `v.luna_xy(T, point)` say where a point of their drawings is in the frame now, so a
cut-out can sit on a head or beside a hand and move with it. `v.t("L3.end+0.2")` reads a time; `v.accent(T, "L")`
is how hard a voice hits now (0 to 1); `from vlog import held` gives a pose that rises before one time and falls
after another. A thing that is not part of a character is its own small drawing on flat magenta (from words, with
the style reference), cut out with `toon.key_out` and `toon.premul`; look at `duo.finger` for the pattern.
Whatever you add must still look drawn by the same hand: flat colours, a dark outline, no photographs, no glow.

## 5. Check it yourself before you say it is done

Look at every strip `work/<name>/qc/around_<time>.jpg` (EVERY frame round each cut, each hand coming up or going
down, each change of face, the plate: a fault two frames long hides between frames taken four a second). Words she
says while turned to him show the small mouth unless the word is set as a shout (`said={"no": "A"}` in `Vlog(...)`).
Look at `work/<name>/qc/sheet.jpg` and at full-size frames (`--stills ... --full`, or ffmpeg from the film) of: the first
frame, both sides of every cut, every moment a hand, a thing or Timo enters or leaves, the last frame.

- The first second: her face close and something odd behind her; it must make sense with the sound off.
- One caption at a time; none on a face, none under the app buttons, all inside the 4:5 version.
- With the sound off, is it always clear who speaks? When Timo is out of the picture his caption needs his finger
  beside it and her mouth shut (the face "read_b" or "down").
- Nothing floats, no straight edge of a drawing shows, no box round a mouth or an eye, no scrap that travels with
  a hand. A new drawing joins her body where it should.
- Whatever leaves is seen leaving; half a second of stillness before it goes.
- No stretch over three seconds on one face with nothing new: add a face, a look, a hand.
- Timo: dry, clever, dignified. Luna: a grown woman.
- No real name of a club, league, player, stadium, sponsor or city anywhere; nothing readable in a background.
- Product words: plan, build, find, compare, check, track. Never book, buy, sell, reserve, flight, hotel, price, AI.
- Every line in its speaker's voice. The speech model now and then reads a line of Timo's in a woman's voice (most
  often when HE speaks first in the dialogue: let her speak first) with every word right, so the word check does not
  catch it. Test it: `python3 -c "import sys,json; sys.path.insert(0,'src/film/toon'); from pitch import right_voice;
  [print(l['id'], right_voice(l['wav'], l['who'])) for l in json.load(open('work/<name>/sound/timeline.json'))['lines']]"`
  (False = swapped: take that line from another reading). Timo sits at 90-140 Hz, Luna at 200-260.
- A mouth shut on a loud word, or the wide mouth on a quiet one: the default shapes come from the spelling, and the
  recogniser stretches the last word of a line. Set the word by hand in `said=` (e.g. `{"best": "C", "trips": "C A C"}`).
- Timo's `rise()` is now even (it used to overshoot by 250 px); `sink()` is unchanged.
- `Vlog(..., drive=True)` lets the loudness of the voice lead the mouths: closed lips from the spelling only where
  the voice really dips, and a mouth held open for long closes at the dips inside it. All the quick shorts use it;
  it removed most of the "mouth shut on a loud word / frozen open" faults the checkers found.
- Her free arm now leaves in 0.7 s at an even speed (it used to vanish in one picture). Keep her phone up for as
  long as her face is "read": a reading face over an empty hand reads as a glitch.
- On her turned head the "E" mouth is a clenched grin and the "A" a gape: set `v.turn_shout = False` unless a line
  is a shout (both then become the small mouth). Her "down" face is the round "oh" of surprise; eyes down with the
  mouth shut is "read_b".
- His look into the lens is best when it lands WITH the music's stop, before his line (in quick.py: `"eyes_at"`);
  set blinks by hand (`v.blinks(timo=[...])`) so none falls on a punchline.
- A caption must not give the punchline before it is said: split it at the word (`split`) or hold the last words
  (`hold`). Her first caption is up from frame 1 (`on={"L1": "start"}`): the first frame is often the cover.

Then ask the watching model once (3 cents): `python3 src/film/toon/check.py <name>`. Compare what it HEARD with
the script word for word; look yourself at every fault it names with a time; fix what is real, render again.

## 6. Keep it

`python3 src/film/toon/pack.py <name>` copies the film's own drawings, readings, sounds and ledger into
`src/film/assets/toon/<name>/`. Then report: the two film files, seconds, dollars (from the ledger), what you
checked, what is still weak, and what only a person's ear can judge.
