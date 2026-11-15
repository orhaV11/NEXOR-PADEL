---
name: timo-luna-ads
description: Make an animated Venuvoy ad or episode with Timo and Luna, the two faces of the brand (presenter spots where they speak to the camera, short "Done." spots, serial episodes, key art). Use whenever the user asks for a new Venuvoy ad, spot, episode or picture with the duo, or wants an existing one fixed or re-cut.
---

# Timo and Luna: the faces of Venuvoy

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
- No prices, no invented features. Never "soccer". No real club, league, player, stadium or city.

## What is the same in every film

1. The look: designer art-toy 3D figures in one sunny model town with a stadium. Prefixes for every picture and clip:
   `src/film/s2/series_look.json`. Reference pictures and voices: `src/film/assets/s2/cast/cast.json` (also the
   places already drawn, `place_*`).
2. Venuvoy inside the story is drawn by the edit, never by the video model, in the product's own look ("Members'
   Club": racing green, cream, brass): the name plate with the round V crest, Luna's sentence appearing word by word on
   an ivory card, then ticks for what is planned. Caption classes `vvb chip`, `vvb type`, `vvb tick` in
   `src/film/overlay.html` (the older `vv` classes, a cream pill with violet letters, are not the brand). Ticks may
   only name things the planner really places: the match, a golf round, a hike, a run, a stadium tour, "day by day".
3. The sign-off on the last shot: "one sentence. your sports trip, planned." (`pnp sig`) / the name plate
   (`vvb chip`) / one small line of sports, "football, basketball, racing, tennis, golf and more" (`pnp more`).
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
- **Serial episode** (30-40 s): model `bet.json` + `bet_edit.json`; each opens with a one-second "previously" on the
  last image of the one before and ends on "follow for ep N".

## How a film is made

1. **Write it first and show the client the story in a few lines** before spending. He rejected films twice when
   this was skipped, and approved at once when it was done. One sentence a twelve-year-old could retell; lines of at
   most 8 words; something moving in every shot; an ending on a face.
2. **Plan** `src/film/s2/<film>.json`: for each shot the reference pictures, the start picture in words, who is in
   it, what moves and what is said. Copy the wording of `bet.json`: it names clothes, the watch wrist, the backpack,
   "the screen of the phone is never seen", "One continuous shot: the camera does not cut".
3. **Start pictures**: `python3 src/film/s2/shoot.py src/film/s2/<film>.json stills <ids> --n 2`. Draw one master
   picture per place and draw the others FROM it (its URL as the first reference), so the place and props match.
   Look at every candidate: reject off-model faces, wrong scarves, a suitcase or cat that should not be there.
4. **Clips**: `... video <ids>` (four at a time; Kling 4.0 reference-to-video, 4 s 0.94 dollars, 5 s 1.18). Then for
   every clip: a frame strip, the words (`asr.words`), and a search for a cut the model made inside the clip
   (`ffmpeg ... select='gt(scene,0.3)'`): it happens in a third of the clips; the part after the cut is often a
   usable closer shot. Use only what is clean.
5. **Edit** `src/film/s2/<film>_edit.json` for `src/film/cut.py`, music with `src/film/s2/arrange.py` (blocks placed
   by `from_s`, layer `phone`). `punch` scales about (`cx`, `cy`): `cx 0` keeps the left edge, `cx 1` the right.
   Keep labels off faces (labels sit at 17-23 % of the height, captions at 64-72 %).
6. **Draft, check, final**: `cut.py <edit> en --draft` (two minutes), look at a contact sheet, measure the stems
   (every line at least 7 dB above the music in 300-3400 Hz), ask the watching model
   (`src/film/watch.py <mp4> "<question>"`) for a cold retelling and for faults, fix, then the full render
   (about fourteen minutes) and `src/film/s2/nomusic.py` for the version without music.
7. Commit sources and renders, push, send the file. Say plainly what was checked and what needs the client's ear:
   nobody here can hear, so voices and the jingle are his to judge.

## Money and usage

About 1 dollar a clip; a 17 s presenter spot costs about 6 dollars, a 35 s episode about 10 to 15. Spend only the
existing MuAPI balance, never buy; the key is injected by the proxy. Work alone and lean: the client complained when
multi-agent checking teams used up his usage, and was happy with one cold viewing plus measurements.

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
- The watching model sees only a few frames a second and is kinder than people: use it for retelling, look at the
  frames yourself for anything shorter than a second.
