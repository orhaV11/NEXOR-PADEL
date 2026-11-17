# Plan, No Plan: season 2

Written on 17 Nov 2026, after the founder added budget and asked for films "at a high level, no blunders: text, sound,
looks; consistent, clean, smooth; it must feel like animation and not like cheap AI". Read `venuvoy_facts.md` and
`season_1.md` first.

## The season (from the writers' room: four writers, three judges, one head writer)

PREMISE AND THREAD: The swap. Timo has typed his first sentence and says he has quit planning; in truth he now plans by hand only what one sentence never covers (himself, Luna, the cat, Big Yellow), and Luna, who never read a plan in her life, carries his folder. Each episode is a different sports trip with one sport to watch and one thing to do; whoever's way did not win that day types the next trip in ONE sentence, and that Venuvoy card, a new place and a new sport, is the last thing before the stare, so the brand moment is the cliffhanger. Under it run a friendly count with nothing staked and never next to a game's result, and one coral page at the back of the folder ("surprise Luna"), seen from episode 2 and opened in episode 4.
EP 1, No Folder (New York, basketball): he carries "one sentence", she pushes Big Yellow, and his plan for having no plan, hidden inside it, says she will push and carry; his plan wins, she carries, and she types Manchester: football and a round of golf.
EP 2, Forty-Two Swings (Manchester, a football match and a round of golf): Luna carries the folder round the course like a golf bag while Timo reads out 42 steps for one swing ("The plan took one sentence. So I planned the swing."); she just swings, the cat sits on his ball, her way wins and he pushes Big Yellow again; he types his second sentence, a motorsport weekend.
EP 3, Pit Stop (a motorsport weekend): a wheel comes off Big Yellow on the way to the track, one sentence cannot mend a suitcase, and Timo's 42-step pit stop can: his first win that is no trick, in a field Venuvoy never claims; Luna types a match and a hike in the mountains.
EP 4, Uphill (a match and a hike): Big Yellow has to go up a mountain and neither way works alone, so they carry it together, one side each; at the top the coral page is his surprise, a sentence written by hand for a sport neither has tried, and she typed one for him the same night: two "Done."s, this time Luna stares into the camera, follow for season 3. (Before episodes 3 and 4 are written, place and month are checked against the catalog: motorsport with no fixed race date, the hike only in one of the nine countries where the planner places hikes, the last sport only one with events, such as tennis or rugby.)

Corrections made when episode 1 was cut: Luna's typed sentence is "4 nights in London next weekend: football and golf, me and Timo"
(golf rounds are placed only around London and Manchester, and a four-night London stay was checked against the planner),
so episode 2 plays in London, not Manchester. The raw room records are in `s2e1_script_room.json`.

## Episode 1, "No Folder" (44.8 s)

Plan `s2e1.json`, edit `s2e1_edit.json`, music `s2e1_music.py` (run it after any change of timing), clips in
`src/film/assets/s2/s2e1/`. Output `ads/19-pnp-s2/venuvoy-pnp-s2-ep1-no-folder-en_*`.

| shot | what happens | words |
|---|---|---|
| 1 room, wide | the wall is bare, one blank sheet | "No notes. No folder. One sentence." (label: he says he quit planning) |
| 2 Timo | his sentence appears on the Venuvoy card: "next week in New York, basketball, me and Luna"; four ticks | "Nine words. I counted." "Done." |
| 3 Luna | on the bean bag with a basketball | "Done? That's MY word!" |
| 4 New York, wide | she pushes Big Yellow, he strolls | "I packed one sentence." (label: so today, she pushes) |
| 5 Luna | lifts her head off the suitcase, out of breath; the music stops on "pack" | "What did I pack?" |
| 6 Timo | silence; the pause between his two halves is played faster (0.85 s instead of 1.35) | "Maybe..." "the cat?" (two captions, so the second half is not read early) |
| 6b her hand | close on the zipper: one stroke, the zip in silence | |
| 7 the suitcase, open, from above | hard cut, the drop, a slow push-in: the cat sits on his folder | (label: his plan for having no plan) |
| 8 Luna | reads the folder | "Step one: say 'Done.'" "Step two: Luna pushes Big Yellow?!" (label: he planned her too) |
| 9 Timo | palms open | "Step three: Luna carries the folder." (label: his plan wins. she carries.) |
| 10 Luna | types "4 nights in London next weekend: football and golf, me and Timo"; four ticks | "My turn. Golf!" "Done." |
| 11 Timo | the music stops on the cut; one small word, the turn, the stare; the sung name | "Golf?" (label: he has no plan for golf) |

Shots 4, 5, 7 and 11 are second versions (`a4c`, `a5b`, `a7b`, `a11b` in the plan) and 6b was added (`a6y`): three independent checkers looked
at the frames of the first cut and found a different cat in the suitcase, a third cat behind Timo at the end, a line of
Timo's that sounded flat, letters on a taxi's roof sign, two captions printed over each other for half a second, and a
caption cut by the 4:5 crop. All were fixed before the final render. What the checkers found that could NOT be fixed
without filming everything again is listed under "Known limits" below.

## How this episode was made (the method for every film from now on)

1. **Script in a room.** Four writers with different angles, three judges (the founder's eye, what the video model
   can film cleanly, product truth), one head writer. The founder's-eye judge is the one to believe on plot.
2. **Roots of drift fixed before filming.** Luna's four reference pictures showed a teal-and-coral scarf; that is why
   her scarf drifted. They were redrawn plain teal (`cast.json`, the old ones kept as `luna_*_v1`). The standing film
   text in `series_look.json` now asks for a camera locked off on a tripod (it used to ask for a hand-held wobble).
3. **Two takes of every shot** (`shoot.py ... video --takes 2`), then `takes.py <plan> [ids] --watch`: words, cuts
   inside the clip, camera movement, how much the picture changes (catches a figure that jumps to another size), the
   pitch of the voice. About one take in four does not start from its start picture or reframes: discard it.
   A voice far from the character's range (Luna under 220 Hz, Timo over 200 Hz in a normal line) is another voice:
   film again with "in her usual bright lively voice". Check each character's lines side by side with the listening
   model before cutting (a numbered reel of the lines).
4. **Eyelines.** Decide who stands on which side and draw every single so that a look or a pointing hand goes toward
   the other person's side. One close-up had to be redrawn for this.
5. **Sound** (new options of `cut.py`, all in `s2e1_edit.json`): `"clean": true` strips each spoken clip down to
   the voice (the clips' own backgrounds used to jump at every cut); `"ambience"` lays one bed per place
   (`src/film/assets/s2/sound/amb_*.wav`, about -30 dBFS in the gaps, crossfaded at a change of place);
   `"foley_db"` on a segment gives back the clip's own sound of the action (wheels, steps, paper) under the voice;
   `"dialog_fx"` warms the voices a little; `"duck_attack"`, `"duck_release"`, `"duck_lead"` make the music lean back
   gently; `"tail_fade"` eases the last moment out. A shouted line can be thrown out by the cleaner: the engine
   notices and keeps that segment as it was. A sound that explains a hard cut (the zipper before the open suitcase)
   is worth more than a label.
6. **A background detail that reads wrong** (a giant ball on a roof looked like a ball floating in the air) is
   painted out with a clean still: source option `"fix": [{"plate": [x0, y0, x1, y1], "file": ...}]`, for a locked
   shot in which nobody crosses the box; otherwise reframe.
7. **Checks before the final render**: contact sheet; margins of every line against the music (all above 9 dB here);
   the watching model for a cold retelling, a read-back of every text, a description of the soundtrack; three
   independent checkers on the frames (continuity, text and layout, truth). The "strict sound supervisor" question
   gives 4 to 6 out of 10 to everything, also to the cleaned mix; trust the focused questions instead (is the
   background continuous, is a line clean, what is heard at this cut).
8. **The machine restarts** now and then in the middle of a long job. `shoot.py` writes every request to
   `pending.json` and collects it on the next run; a clip that was paid for can also be found again through the
   service's history (`GET /api/v1/history`).

9. **Three checkers on the frames, each with one job** (continuity against the cast pictures; text and layout,
   including the 4:5 crop; truth of every claim and how the story reads with the sound off). Give them frames at
   every caption change, not at three moments of a shot: an overlap of two captions lasted half a second and sat
   between the frames. Tell them not to run the product's code; a sentence can be checked by reading the parser.
10. **A typed sentence says when** ("next week", "next weekend", "this weekend"). "A week in New York" gives the
    product a length but no dates, and the planner then waits on its first step.
11. **A wide shot may take its voice from another take.** Lips are a few pixels high there. The better way is still
    a new take: the second version of shot 4 was filmed from a corrected start picture with a clearer line.
12. **Pick the take by ear as well as by eye**: a numbered reel of one character's lines, with the question "which
    clip does not sound like the same person, and which is flat". Twice the take that looked best had the worse voice
    (a pitch far above or below the character's range is the measurable sign).
13. **Background writing** (a taxi's roof sign) is painted out on a clean frame and laid over the shot, or the start
    picture is corrected with an edit pass ("keep everything exactly, change only: ...") and the shot filmed again.

## Known limits of episode 1 (for episode 2)

- Who stands on which side changes between shots (Timo is left of Luna in the wide shot, right of her at the end).
  From episode 2: one side per character per place, written into every start picture.
- The room of shot 2 does not show the pins and the sheet where shot 1 has them.
- Luna's scarf is teal in the wide shots and greyer in her close-ups. Write "the same saturated teal as Timo's jacket".
- Timo's backpack is larger in side views than on the cast sheet. Write "tiny, square, ends at mid-back".
- The blue folder is a thick hard-cover folder everywhere now; keep the frame of shot 10 as its reference.

## Money

Balance before this episode 200.0 dollars; after it 152.6. Start pictures and plates about 3, clips about 44
(11 shots twice, nine shots filmed again or added), sound beds, listening and watching about 1.

## What the watching model can and cannot tell

Asked "rate clarity, funny, polished, next episode", the same film got 9/7/9/7.5 with one wording of the question and
6/5/6/5 with another, eight times in a row, before and after the last four fixes. Its numbers do not measure a change.
What it does well: retelling the story cold, reading every text back, describing one stretch shot by shot ("is it
clear how the suitcase came to be open?"), comparing takes of one line on a numbered reel, and naming its weakest
moment: when five answers of eight name the same second, that second needs work (here: the cut to the open suitcase,
which got the zipper close-up before it).
