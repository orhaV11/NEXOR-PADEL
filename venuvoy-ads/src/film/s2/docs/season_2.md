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

Corrections made when episode 1 was cut: Luna's typed sentence is "4 nights in London: football and golf, me and Timo"
(golf rounds are placed only around London and Manchester, and a four-night London stay was checked against the planner),
so episode 2 plays in London, not Manchester. The raw room records are in `s2e1_script_room.json`.

## Episode 1, "No Folder" (43.5 s)

Plan `s2e1.json`, edit `s2e1_edit.json`, music `s2e1_music.py` (run it after any change of timing), clips in
`src/film/assets/s2/s2e1/`. Output `ads/19-pnp-s2/venuvoy-pnp-s2-ep1-no-folder-en_*`.

| shot | what happens | words |
|---|---|---|
| 1 room, wide | the wall is bare, one blank sheet | "No notes. No folder. One sentence." (label: he says he quit planning) |
| 2 Timo | his sentence appears on the Venuvoy card, four ticks | "Nine words. I counted." "Done." |
| 3 Luna | on the bean bag with a basketball | "Done? That's MY word!" |
| 4 New York, wide | she pushes Big Yellow, he strolls | "I carry one sentence." (label: so today, she carries) |
| 5 Luna | out of breath over the suitcase; the music stops on "pack" | "What did I pack?" |
| 6 Timo | silence | "Maybe... the cat?" |
| 7 the suitcase, open | zip, hard cut, the drop: the cat sits on his folder | (label: his plan for having no plan) |
| 8 Luna | reads the folder | "Step one: say 'Done.'" "Step two: Luna pushes Big Yellow?!" (label: he planned her too) |
| 9 Timo | palms open | "Step three: Luna carries the folder." (label: his plan wins. she carries.) |
| 10 Luna | types; card and ticks | "My turn. Golf!" "Done!" |
| 11 Timo | turns, walks up to the camera, stares; the sung name | "Golf?" (label: he has no plan for golf) |

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

## Money

Balance before this episode 200.0 dollars; after it about 163. Start pictures and plates about 2, clips about 34
(11 shots twice, four shots filmed again), sound beds and tests about 1.
