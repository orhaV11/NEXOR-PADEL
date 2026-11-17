---
name: timo-luna-ads
description: Make an animated Venuvoy ad or episode with Timo and Luna, the two faces of the brand (presenter spots where they speak to the camera, short "Done." spots, serial episodes, key art). Use whenever the user asks for a new Venuvoy ad, spot, episode or picture with the duo, or wants an existing one fixed or re-cut.
---

# Timo and Luna: the faces of Venuvoy

## STOP: read this first (the reset of 17 Nov 2026)

The founder rejected everything made so far ("it all feels dumb", "start over on everything", "pretty bad and far too
expensive") and asked that nothing be sent to him until it is finished. Read `venuvoy-ads/src/film/reset/RESET.md`
before anything else. Everything BELOW this section describes the old films and the old method; it is kept as a
record of the tools and of the product facts, not as a way to make new films.

What he asked for, in his words: a comic duo who are the faces of the brand, like the Israeli ad duos he named
(Bisli, "the pigeons", Toto): international, humorous, nice, at eye level; authentic, funny, really fun to watch, not
feeling machine-made, speaking to today's audience. In practice:

- One situation, one joke, 15 to 22 seconds. The joke must be funny read aloud before anything is drawn.
- Two friends who bicker in everyday adult speech and interrupt each other; both can be the fool. A moment every
  sports traveller knows; a new sport each time; it works for someone who has seen nothing before.
- NO labels that explain the story, NO product card with ticks, NO typed sentence on screen, NO episode numbers,
  at most one text on screen at a time. The brand is the last two seconds, and now and then the joke itself.
- No glossy 3D toy look, no nursery materials, no slow simple sentences from smiling figures.
- Voices carry it. Nobody here can hear: a person must judge them (the founder, on the finished film).

Money, checked on 17 Nov: season 2 episode 1 cost 47 dollars because 44 clips were filmed at about 1 dollar each for
a film that uses 14. Rules: a cap of 5 dollars a film, checked before every paid call; script, then voices, then
stills (0.022), and a moving clip only where nothing else will do (Veo 3.1 lite: 0.30 a clip); ONE take per shot; a
retake only for a named defect after the start picture or the text was changed; report dollars and the share of paid
seconds on screen with every delivery. The `--takes 2` habit written further down is cancelled.

Do not start a new film until the founder has agreed to a direction in words. Words cost nothing; show him the script.

## The method now: drawn cut-out sketches (agreed by the founder on 17 Nov: "this is more the direction")

The founder agreed to: Timo and Luna as a comic duo in short drawn 2D sketches, voices from a voice model ("make it
sound good"). The first one is "The Switch" (`venuvoy-ads/ads/20-sketches/`). No video model is used at all: the
characters are still drawings moved in code. A whole film costs about 2 dollars. The scripts still to make are in
`venuvoy-ads/src/film/reset/RESET.md` (the script bank); the next one should be about another sport.

Everything is in `venuvoy-ads/src/film/toon/`. For a new sketch copy the four `switch_*` files and change them.

1. **Voices first** (`voices.py`, a sketch json like `switch_voices.json`): the whole dialogue is read in one go by the
   dialogue speech model (`gemini-2-5-pro-tts`, 2 to 5 cents a reading), with a scene, a note on each voice and a
   direction in square brackets before a line. Make several readings, read every one back (a direction is sometimes
   spoken aloud), cut them into lines at the real silences (`lines.py`), and choose line by line on a numbered reel
   with the listening model (`hear.py`). The cast that won: Luna = Laomedeia, Timo = Zubenelgenubi, accent "British
   (Brixton)". Nobody here can hear: say so to the founder; he is the first human ear.
2. **Sound and timing** (`switch_sound.py`): one table of lines and the gaps between them. The gaps ARE the comic
   timing: a beat needs 0.3 to 0.45 s of air, a stare 0.75 s; a retort may come after 0.05 s; an interruption runs
   0.2 s under the other voice. Wind bed with gusts, the vlog music under the first shots (thinned for phone speakers,
   13 dB under the voices where speech lives) which stops dead on the stare, a thud, the sung name on the plate. It
   writes `timeline.json`; the picture reads every time from it.
3. **Drawings** (`draw.py`, 2.2 cents each): ONE base drawing of each character on flat magenta, clean TV-animation
   line, flat colours. Every other state is a redraw of that same picture with "ONE change only ... Change NOTHING
   else", at the SAME aspect ratio as the base (otherwise it is reframed and useless). Only a box (a face, the eyes,
   a mouth) or the differing pixels (hair in the wind, a raised arm) is taken from it, so the rest stays the very same
   pixels. Needed per character: three or four mouths, a blink FOR EACH FACE whose brows differ, the expressions the
   script names, two wind states. An arm that comes up beside the body (a phone, a shrug) is its own piece and can
   rise and fall. Give each sprite a point of plain skin: redraws come back a shade off.
4. **The film** (`switch_film.py` on `toon.py`): who does what when, in film time. What makes it read as animation
   and not as a slideshow: the head moves on the shoulders (`bend`: tilt into each line, dip on stressed words),
   a pop at every change of face, held poses on twos, blinks, eyes that move while reading, a different framing for
   every shot, a whip across a cut that skips time, a handheld camera, captions one at a time in the speaker's colour.
   A figure on the ground needs ground contact: grass of the background laid back over its lower edge and a shadow.
   Let every body run on below the frame (`pad_bottom`), or a camera shake shows where the drawing ends.
5. **Check before sending**: frames at every caption hand-over and at every new piece; the watching model for story,
   craft and sound (three questions, about 3 cents); then independent checkers on frames. Fix, render, then send ONE
   file. Tell the founder what was checked by a model and what only he can judge (the voices, the sung name).
6. **Keep it**: `python3 src/film/toon/pack.py switch --scan` stores the drawings, voices and sounds the film uses in
   `src/film/assets/toon/switch/` (the `work/` folder is not in git).

Captions are set by `text.mjs` in the project's own fonts; it fails if a font did not load (a page built in memory
cannot read font files: it must be opened as a file).

Everything lives in `venuvoy-ads/`. Read first `venuvoy-ads/src/film/s2/docs/venuvoy_facts.md` (what Venuvoy really
is and what an ad may say), then `venuvoy-ads/src/film/s2/docs/season_1.md` (the serial, the canon, what was learned),
then this file. The client is the founder: answer him in Hebrew, short and plain, result first.

## Who they are

- **Timo**, 23, plans everything. Tall, flat-top hair, teal track jacket with cream sleeve stripes, navy trousers,
  thick teal-and-coral scarf, big yellow watch on his LEFT wrist, tiny teal backpack ALWAYS (also indoors). His thick
  blue folder is "the plan". His words: "No." and counting steps ("Step nineteen!"). His signature: he stares into
  the camera, stunned, without a word.
- **Luna**, 22, plans nothing: she types ONE sentence into Venuvoy. Short, coral bob, coral football shirt, denim
  shorts, long PLAIN TEAL scarf. Her phone has a coral case and its screen is never shown. Her word: "Done."
- **Big Yellow**, her giant yellow suitcase, and **the cat**, fat, ginger, never impressed. Both are in every film.
- Best friends, never a couple. Warm, funny, eye-level: they tease each other, nobody is humiliated.

What Venuvoy is (checked against the product's code on 15 Nov 2026, full sheet in `venuvoy_facts.md`): a sports-trip
planner, a web app in Hebrew and English. You say where, when and which sports you love, in a form or in ONE typed
sentence, and it builds two or three different, priced, day-by-day plans around real fixtures. Its own words:
"Venuvoy plans sports trips around real fixtures." / "A sports trip you can trust."
- Far more than football: 13 sports with events (football, basketball, baseball, ice hockey, American football,
  motor racing, rugby, tennis, golf and a few more), about 11,700 events in 56 countries. Things travellers do
  themselves: hikes, runs, golf rounds, about 16,000 places to play (padel, climbing, karting, pools), stadium tours
  and sports museums. An opt-in, adults-only casino evening as a place to visit.
- It does NOT sell or book anything: no tickets (it shows the official way to get one), no flights (no flight search
  at all), no hotels (only a search link). Never write "book", "buy", "flight", "hotel", "AI plans your trip",
  "anywhere in the world" or "download the app". Say plan, build, find, compare, check, track.
- Always "sports trip", never only "football trip". A film may be about a football match; the sign-off stays broad.
- Casinos are not shown in the mascot films until the founder decides how (the product's own rule: a place to visit,
  adults only, "does not offer or promote gambling"; platforms restrict gambling content).
- The sentence a character types must be one the product can read: a place, WHEN ("this weekend", "next week", "next weekend"; a length alone, "a week in ...", gives the product no dates), a sport ("London this weekend,
  football, two of us: me and Timo"). Real city names are allowed there, as in the product's own examples.
- Tickets are bought by the characters, never supplied by Venuvoy. The tick is "ticket links", not "ticket guide".
- No betting words next to a match. No prices, no invented features. Never "soccer". No real club, league, player or
  stadium; "motorsport", not "racing".

## What is the same in every film

1. The look: designer art-toy 3D figures in one sunny model town with a stadium. Prefixes for every picture and clip:
   `src/film/s2/series_look.json`. Reference pictures and voices: `src/film/assets/s2/cast/cast.json` (also the
   places already drawn, `place_*`).
2. Venuvoy inside the story is drawn by the edit, never by the video model, in the product's own look ("Members'
   Club": racing green, cream, brass): the name plate with the round V crest, Luna's sentence appearing word by word on
   an ivory card, then ticks for what is planned. Caption classes `vvb chip`, `vvb type`, `vvb tick` in
   `src/film/overlay.html` (the older `vv` classes, a cream pill with violet letters, are not the brand). Ticks may
   only name things the planner really gives: the match (the game), ticket links, getting around, "day by day", and,
   where the typed sentence asks for them, a golf round, a hike, a run or a stadium tour.
3. The sign-off on the last shot: "one sentence. your sports trip, planned." (`pnp sig`) / the name plate
   (`vvb chip`) / one small line of sports, "football, basketball, motorsport, tennis, golf and more" (`pnp more`). Episodes add
   "ep N · follow for ep N+1" (`pnp brand`) between the plate and the sports line.
   Model: `src/film/s2/roll_edit.json`.
4. Sound: the theme `src/film/assets/s2/music/jersey_1.mp3` with one map: groove under the talk, a full stop before
   the turn, the drop's first kick on a hard cut, a stop on the last look. The sung sonic logo
   `src/film/assets/s2/brand/sonic_logo.wav` ("Ven-nu-voy") under the sign-off of presenter spots; the series' final
   hit (the track from 59.855 s) under the sign-off of episodes.
5. Text: white typed labels at the top in lower case (`pnp hook`), captions of every spoken line (`pnp`).

## Formats

- **Presenter spot** (15-20 s): the two speak to the camera. Model: `src/film/s2/meet.json` + `meet_edit.json`
  ("Meet Timo & Luna": "A football trip takes forty-two steps." / "Or one sentence." / "Done." / "He's fine.").
- **"Done." spot** (10-16 s): one trip-planning headache, Timo's way, Luna's one sentence, "Done.", his stare.
  Model: `src/film/s2/roll.json` + `roll_edit.json` ("40 metres long").
- **Serial episode** (30-45 s): model `s2e1.json` + `s2e1_edit.json` (season 2; season 1: `bet.json`). It ends on
  Timo's stare, the sung name and "follow for ep N"; from episode 2 it opens with a one-second "previously".

## How a film is made

Read `venuvoy-ads/src/film/s2/docs/season_2.md` ("How this episode was made"): it is the method, step by step, with
the commands. In short:

1. **Write it first and show the client the story in a few lines** before spending. For an episode, use a writers'
   room (several drafts, judges, one head writer). One sentence a twelve-year-old could retell; lines of at most 8
   words; something moving in every shot; an ending on a face; one open question.
2. **Plan** `src/film/s2/<film>.json` from `s2e1.json`: its wording names clothes, the watch wrist, the backpack,
   "the screen of the phone is never seen". Look and film prefixes come from `series_look.json` (camera locked off).
   Decide who stands on which side; every close-up looks and points toward the other person's side.
3. **Start pictures**: `shoot.py <plan> stills <ids> --n 2`. One master picture per place, the others drawn FROM it.
   Draw each picture at its final framing (nothing is enlarged in the edit by more than a quarter) and leave the top
   third empty where the Venuvoy card will sit. Reject off-model faces, wrong clothes, a second cat.
4. **Clips**: `shoot.py <plan> video <ids> --takes 2`, then `takes.py <plan> <ids> --watch`. Throw out a take that
   reframes, cuts, starts from another framing or speaks in another voice. Compare each character's lines side by
   side with the listening model.
5. **Edit** `<film>_edit.json` for `src/film/cut.py` from `s2e1_edit.json`: `"clean": true`, one `"ambience"` bed
   per place, `"foley_db"` where an action must be heard, the Venuvoy graphics (`vvb chip`, `vvb type`, `vvb tick`),
   labels and captions kept off faces and inside rows 250 to 1600 (the 4:5 crop). Music from the theme with a script
   like `s2e1_music.py`, so a change of timing rebuilds it.
6. **Draft, check, final**: `cut.py <edit> en --draft`, a contact sheet, margins of every line against the music,
   the watching model (cold retelling, read-back of every text, the soundtrack described), independent checkers on
   the frames; fix; full render; `nomusic.py` for the version without music.
7. Commit sources and renders, push, send the file. Say plainly what was checked and what needs the client's ear:
   nobody here can hear, so voices and the sung name are his to judge.

## Money and usage

About 1 dollar a clip (4 s 0.94, 5 s 1.18 at 1080p); with two takes a shot and a few retakes, a 40 s episode costs
about 35 dollars and a 16 s spot about 15. Spend only the MuAPI balance, never buy; the key is injected by the proxy
(the `muapi` command-line tool, installed with `pip install muapi-cli`, works with `MUAPI_API_KEY=proxy-injected`).
The client asked for the highest level; he also once objected to usage spent on checking a film he did not like, so
check in proportion and always show him the story first.

## What goes wrong

- One-sided details drift between shots (backpack, scarf ends, the watch wrist, the suitcase's ribs): put them in the
  start picture and in the clip text, and prefer start pictures drawn from a neighbouring frame.
- Props vanish between a wide shot and a closer one: name them in every start picture of the scene.
- The video model adds its own music or a bass sting inside a clip: mute that segment.
- A pasted-in voice never matches: every line must be spoken inside a clip by the character's own voice element.
- The video model sometimes speaks the words of the voice sample instead of the line ("It's the small one"): read
  every clip back with `asr.words`, and avoid lines that begin like the sample ("It's ...").
- The video model pulls the camera back during a close shot whatever the text says: hold the size with
  `zoom {from, to, cx, cy}` in the edit.
- A sound in a clip that is not speech (a cat) still makes the music step back when `duck_mode` is "voice": leave
  that option out when the film has loud music under shots without words.
- The cat (and any small prop) becomes another cat when its reference pictures are not the FIRST references of the
  start picture: put `cat_front`, `cat_side` and a crop of the cat from an earlier clip first, and describe it
  ("plain ginger felt, no stripes, flat grumpy face, cream muzzle"). Check every shot's cat against the cast sheet.
- A background brings writing with it (a taxi's roof sign) and a detail that reads wrong (a ball on a roof): ask
  the start picture for no vehicles and no signs, and correct a picture with an edit pass rather than a crop.
- Two captions of one segment print over each other when the first one's `hold` runs into the second one's `at`:
  look at a frame at EVERY caption change before the final render. Labels and plates start at y 276 or lower and
  everything ends above y 1570, or the 4:5 version touches or cuts them.
- A sentence typed in a film says when ("next week", "next weekend"): without it the product has no dates.
- The voice cleaner can take 6 to 15 dB off a bright, shouted word; the engine then keeps that segment's own sound
  and says so in the log. Set `"clean": false` on such a segment so that the result does not flip between renders.
- A take can look right and sound wrong. Before the cut, put each character's lines on a numbered reel and ask the
  listening model which clip is not the same person or sounds flat; film that line again.
- Do not let a checker run the product's code: tell every agent that the product repository is for reading only.
- The watching model sees only a few frames a second and is kinder than people: use it for retelling, look at the
  frames yourself for anything shorter than a second.
