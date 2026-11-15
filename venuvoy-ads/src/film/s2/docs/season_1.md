# Plan, No Plan: the serial (season 1)

The client rejected the stand-alone pilot "One Night" (15 Nov): "you skimped on the plot", and he felt the heavy
checking teams wasted his usage. He approved this instead: a serial in which Venuvoy is part of the story. Work lean:
write and check alone, no multi-agent proofs unless he asks.

> **Corrected on 15 Nov 2026.** The founder asked that Venuvoy never be shown as football only, and a study of the product
> (`venuvoy_facts.md`) found that it does not handle flights or hotels and does not sell tickets. All four episodes were
> re-rendered with corrected on-screen text; nothing was filmed again. What changed: the sign-off, the ticks, the typed
> sentences (now ones the product can really read: a place, a time, a sport), the Venuvoy look (`vvb` classes), "bought"
> instead of "has" for the ticket, and the word "bet" outside episode 1. The spoken lines are as filmed.

## The season in one sentence
Timo planned the trip to the match for three weeks. Luna remembers the evening before, types ONE sentence into
Venuvoy, and everything is planned. They bet on whose trip will be better. Loser carries her suitcase.

## Episodes (all four made, 15 Nov)
1. **The Bet** (37 s). His wall of plans; "Can I come?"; "The match is TOMORROW!"; she types one sentence ("London this
   weekend, football, two of us: me and Timo"), four ticks (the match, ticket links, getting around, day by day),
   "Done."; "My plan is still better." / "Bet?" / "Bet."; next morning his
   42 steps against her one sentence; the wind takes his plan; "Need a plan?"; his stare, the cat in his folder.
   Plan `bet.json`, edit `bet_edit.json`.
2. **No Ticket** (33 s). His ticket was in the folder. "Timo, I have your..." / "No. My plan is in here." Swan boat
   and donkey against her nap on the train with the cat. He arrives wet with a duck on his head: "Step forty-two.
   Done." "I still have no ticket!" She tries again, he says no, she looks at us: "in her pocket: both match tickets".
   Plan `ep2.json`, edit `ep2_edit.json`.
3. **The Small One** (30 s). The suitcase footage of the first pilot ("One Night") with a new opening ("You're
   losing. You carry it.", the bet at Luna 2, Timo 0) and a new last label ("he still thinks he has no ticket").
   One new clip. Plan `ep3.json`, edit `ep3_edit.json` (built from `one_night.json`).
4. **Full Time** (32 s). "No ticket. No plan. Nothing!" "Timo! Listen. I typed: me AND Timo." "Since yesterday?"
   "You kept saying no." "I love your plan." They run in; the cat guards Big Yellow; the goal; Luna wins the bet and
   rides home on the suitcase; Timo secretly types his first sentence ("a week in New York, basketball, me and Luna");
   "I saw that!"; his stare. A label says where the tickets came from: "she bought both tickets last night".
   Plan `ep4.json`, edit `ep4_edit.json`.

The thread that carries viewers from episode to episode: the sentence Luna typed in episode 1 said "two of us: me and
Timo", so she planned for two and bought both tickets, and he never lets her finish the sentence. Every episode opens with a one-second
"previously" on the last image of the one before and ends on "follow for ep N". Posting kit: `ads/17-pnp/posting-kit.md`.

Money: the four episodes cost about 34 dollars in total (ep 1 14.5, ep 2 10.2, ep 3 1.0, ep 4 9.9, one retake 1.0);
about 12.6 dollars remain.

## How Venuvoy appears, the same way in every episode
- In the story: Luna's phone is never read. What she types and what comes back are drawn by the edit, in the product's
  own look: the green name plate "VENUVOY" with the V crest, her sentence on an ivory card appearing word by word, then
  ticks for what is planned. Caption classes `vvb chip`, `vvb type`, `vvb tick` in `src/film/overlay.html`.
- Product facts only, from `venuvoy_facts.md`: a sports trip planned from one typed sentence that names a place, a time
  and a sport (real city names are allowed inside the typed sentence, as in the product's own examples; no club, league
  or stadium names). Ticks: the match (or the game), ticket links, getting around, day by day; where the sentence asks
  for them, a golf round or a hike. Never a flight, a hotel or a ticket itself. The film says "planned", never "booked".
- Tickets in the story are bought by the characters ("she bought his ticket"), never supplied by Venuvoy.
- The sign-off on the last shot, every episode: "one sentence. your sports trip, planned." / the name plate /
  "ep N · follow for ep N+1" / "football, basketball, motorsport, tennis, golf and more".
- No betting words next to a match: "the bet" appears only in episode 1, where the stake (carrying a suitcase) is shown.

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
