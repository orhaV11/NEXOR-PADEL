# Plan, No Plan: the serial (season 1)

The client rejected the stand-alone pilot "One Night" (15 Nov): "you skimped on the plot", and he felt the heavy
checking teams wasted his usage. He approved this instead: a serial in which Venuvoy is part of the story. Work lean:
write and check alone, no multi-agent proofs unless he asks.

## The season in one sentence
Timo planned the trip to the match for three weeks. Luna remembers the evening before, types ONE sentence into
Venuvoy, and everything is planned. They bet on whose trip will be better. Loser carries her suitcase.

## Episodes
1. **The Bet** (37 s, made). His wall of plans; "Can I come?"; "The match is TOMORROW!"; she types one sentence, four
   ticks (match ticket, flight, hotel, match day), "Done."; "My plan is still better." / "Bet?" / "Bet."; next
   morning: his 42 steps against her one sentence; the wind takes his plan at the station; "Need a plan?"; his stare,
   the cat in his empty folder.
2. **Travel day** (to write). 42 steps against one sentence on the way there. He arrives wrecked, she arrives fresh.
3. **Match day** (to write). The day around the match. The suitcase material of "One Night" belongs here (uphill,
   the ride, the cat, the gate, "It can't come in, Timo"): nine finished clips, so this episode needs few new ones.
4. **Full time** (to write). At the stadium: who won the bet, who carries the suitcase, and Timo secretly types his
   first sentence.

## How Venuvoy appears, the same way in every episode
- In the story: Luna's phone is never read. What she types and what comes back are drawn by the edit: the name chip
  "VENUVOY", her sentence in a white bubble appearing word by word, then ticks for what is planned. Caption classes
  `vv chip`, `vv type`, `vv tick` in `src/film/overlay.html`.
- Product facts only: a football trip abroad planned from one typed sentence: the match ticket, the flight, the
  hotel, the day around the match. Nothing else may be claimed. The film says "planned", never "booked".
- The sign-off on the last shot, every episode: "one sentence. the whole football trip." / the chip "VENUVOY" /
  "Plan, No Plan · ep N".

## What stays identical (canon)
- Timo: teal track jacket with cream stripes, navy trousers, teal-and-coral scarf, yellow watch on his LEFT wrist,
  tiny teal backpack ALWAYS, also indoors (the video model adds it anyway; start pictures must show its straps).
- Luna: coral bob, coral football shirt, denim shorts, PLAIN TEAL scarf (no coral). Big Yellow is hers.
- The cat is in every episode and is never impressed.
- Look and film prefixes: `src/film/s2/series_look.json`. Places already drawn are in `cast.json` as `place_*`.
- Text: white typed labels at the top (lower case), captions of every spoken line (`pnp`), the Venuvoy graphics.
- Music: the series theme `jersey_1.mp3`, always with the same map: groove under the talk, a full stop before the
  turn, the drop's first kick on the hard cut, a stop on the last look, one final hit under the sign-off.
- The ending: Timo's stare into the camera.

## Making an episode (what worked for "The Bet")
Plan `src/film/s2/bet.json` (start pictures and clip prompts), edit `src/film/s2/bet_edit.json`, music
`bet_music_talk.json` / `bet_music_hit.json`. One master picture per place, the other start pictures drawn from it;
2-3 candidates each; film four clips at a time. Check every clip for a cut the model made inside it (it did so in 5
of 14 clips here; the part after the cut is often a usable closer shot). 14 clips and the stills cost about 14.5
dollars. Draft render first (`--draft`, two minutes), then the full render (fourteen minutes).
