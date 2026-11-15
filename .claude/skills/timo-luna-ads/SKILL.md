---
name: timo-luna-ads
description: Make an animated Venuvoy ad or episode with Timo and Luna, the two faces of the brand (presenter spots where they speak to the camera, short "Done." spots, serial episodes, key art). Use whenever the user asks for a new Venuvoy ad, spot, episode or picture with the duo, or wants an existing one fixed or re-cut.
---

# Timo and Luna: the faces of Venuvoy

Everything lives in `venuvoy-ads/`. Read `venuvoy-ads/src/film/s2/docs/season_1.md` first (the serial, the canon, what
was learned), then this file. The client is the founder: answer him in Hebrew, short and plain, result first.

## Who they are

- **Timo**, 23, plans everything. Tall, flat-top hair, teal track jacket with cream sleeve stripes, navy trousers,
  thick teal-and-coral scarf, big yellow watch on his LEFT wrist, tiny teal backpack ALWAYS (also indoors). His thick
  blue folder is "the plan". His words: "No." and counting steps ("Step nineteen!"). His signature: he stares into
  the camera, stunned, without a word.
- **Luna**, 22, plans nothing: she types ONE sentence into Venuvoy. Short, coral bob, coral football shirt, denim
  shorts, long PLAIN TEAL scarf. Her phone has a coral case and its screen is never shown. Her word: "Done."
- **Big Yellow**, her giant yellow suitcase, and **the cat**, fat, ginger, never impressed. Both are in every film.
- Best friends, never a couple. Warm, funny, eye-level: they tease each other, nobody is humiliated.

What Venuvoy is, the only product facts that may be shown or said: an app that plans a football trip abroad from one
typed sentence: the match ticket, the flight, the hotel, the day around the match. Say "plan", never "book", unless
the client confirms that it books. No prices, no "download now", no invented features. Never "soccer". No real club,
league, player, stadium or city.

## What is the same in every film

1. The look: designer art-toy 3D figures in one sunny model town with a stadium. Prefixes for every picture and clip:
   `src/film/s2/series_look.json`. Reference pictures and voices: `src/film/assets/s2/cast/cast.json` (also the
   places already drawn, `place_*`).
2. Venuvoy inside the story is drawn by the edit, never by the video model: the name chip, Luna's sentence appearing
   word by word in a white bubble, then ticks (match ticket, flight, hotel, match day). Caption classes `vv chip`,
   `vv type`, `vv tick` in `src/film/overlay.html`.
3. The sign-off on the last shot: "one sentence. the whole football trip." / the chip "VENUVOY" / one small line
   (follow for ep N, follow Timo & Luna).
4. Sound: the theme `src/film/assets/s2/music/jersey_1.mp3` with one map: groove under the talk, a full stop before
   the turn, the drop's first kick on a hard cut, a stop on the last look. The sung sonic logo
   `src/film/assets/s2/brand/sonic_logo.wav` ("Ven-nu-voy") under the sign-off of presenter spots; the series' final
   hit (the track from 59.855 s) under the sign-off of episodes.
5. Text: white typed labels at the top in lower case (`pnp hook`), captions of every spoken line (`pnp`).

## Formats

- **Presenter spot** (15-20 s): the two speak to the camera. Model: `src/film/s2/meet.json` + `meet_edit.json`
  ("Meet Timo & Luna": "A football trip takes forty-two steps." / "Or one sentence." / "Done." / "He's fine.").
- **"Done." spot** (10-15 s): one football-trip headache, Timo's way, Luna's one sentence, "Done.", his stare.
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
- The watching model sees only a few frames a second and is kinder than people: use it for retelling, look at the
  frames yourself for anything shorter than a second.
