# "To Be Confirmed": the final cut sheet

`the original plan (no longer on disk)` is the showrunner's plan (bible, beats, every word). A script doctor then reviewed it
(`the script doctor's report (no longer on disk)`). THIS FILE is what was decided; where it differs from plan.json, this file wins.
Everything not mentioned here stays as plan.json has it. All paths are relative to
`/home/user/NEXOR-PADEL/venuvoy-ads`.

## Rules that changed for every episode
- EVERY SPOKEN WORD IS ON SCREEN ONCE. Where the narrator reads words that are already the graphic (the receipts
  "Seen by 7 / Opened by 1", the checklist rows), there is NO narrator caption: the graphic is the caption, and it
  must be at caption size at that moment (push the picture in on it if needed). Everywhere else the narrator is
  captioned (the engine does this from the "narration" entries; a narration entry without "text" has no caption).
- At the cut into the sign-off the corner wordmark, the day tag and the chip go OFF. Only the sign-off shows.
- The plan's beat times were estimated on paper. The real narration is recorded (lengths below) and the real
  takes have their own timings: build each beat around the real sound. A narrator line never overlaps a spoken
  line; leave 0.2-0.35 s of air before and after a line; a joke that lands in silence gets its silence.
  Total lengths may come out 1-2 s shorter than planned; do not pad.
- Frame 0 is a face, already mid-expression, with the first caption up by 0.3 s. No fade in, no slate.
- No music under the episodes. Sound is the clips' own sound, room tones, and small exact effects. The only
  "musical" thing is the closing figure in the sign-off.
- Lee and Marcus are ONE man now: the bedroom man of both clips is LEE. "Marcus" is a text-only member of the chat.

## The sign-off (last 1.8 s of every episode)
Picture: `src/film/assets/apa/clips/j22_gran_a.mp4` in 3.70 out 4.62, muted, slowed to fill 1.8 s, slow push-in
(as in src/film/danny.json: zoom 1.10 -> 1.17 at cx 0.75, cy 0.8 hid nothing important; check the frame).
On it: gfx "status" with name "DANNY'S GRANDMOTHER", the episode's status line, and foot
"Next episode: Thursday. Confirmed." Sound: clock_tick at +0.15 and +0.95, cup at +0.55, two_note at +0.75
(quiet: the figure must not be louder than the voices were), then a hard cut on the last frame.
Episode 3 is the exception: its sign-off runs on the grandmother's spoken shot as it continues (see below).
Statuses: 1 "NOTHING BOOKED." · 2 "HAS KEPT THE MONTH FREE." · 3 "ONE TICKET. HER OWN." · 4 "FREE ON MONDAY." ·
5 "TRAVELS ON THE DAY." · 6 "NOT IN THE CHAT."

## Footage
New shots (Veo, 720 x 1280, 24 fps, 7.9 s; word timings beside each as `<clip>.words.json`):
- `src/film/assets/tbc/shots/e1_s1.mp4` MAYA at her kitchen table. "I made a spreadsheet." voice 0.74-1.78;
  "I wanted to check the link worked." voice 2.46-3.98. Use 0.45-2.05 and 2.25-4.15. From 4.25 a stranger's hand
  enters bottom-left: never show the bottom-left corner after 4.2 (the standard Maya framing below hides it).
- `src/film/assets/tbc/shots/e2_s1.mp4` MAYA. "Wait for the date and the flights are gone." voice 0.26-1.90;
  "So you guess." voice 3.64-4.40, with a small hand gesture. Silent tail 5.2-7.9: she holds the interviewer's
  look, then looks down and her eyes close (about 6.9-7.4). Clean throughout.
- `src/film/assets/tbc/shots/e4_s1.mp4` MAYA. "I'd booked three hotels." 0.36-1.72; "Saturday, Sunday, Monday."
  2.26-3.68; "I cancelled Monday on Tuesday." 4.22-5.98. From 4.2 a stranger's hand is at the bottom-left and
  after 6.1 hands move the laptop: use 0.15-6.1 only, in the standard Maya framing.
- STANDARD MAYA FRAMING for all three: punch-in 1.3 with the crop's centre at x 0.60 and its top edge near the
  top of the source (in the engine: "punch": 1.3, "cx": 0.60, "cy": 0.42 or lower; check a frame: her face in the
  upper middle, the lamp top right, no hand at the bottom-left). The same framing in every episode: it is her seat.
- `src/film/assets/tbc/shots/e3_s1.mp4` VIC outside the ground at night, a reporter's microphone at the bottom.
  "I buy tickets to matches I can't go to." voice about 0.5-3.5; "Keeps my record up." about 4.5-6.2; then he
  holds the look. From about 6.6 a second dark object rises at the bottom-right edge: a punch-in of 1.12-1.15
  held to the top (as his old clip was framed in danny.json: punch 1.14, cx 0.45, cy 0.3) removes it; check 6.6-7.9.
  He speaks with force (mouth wide), not flat; it plays as certainty. Crowd noise runs under the whole clip.
- `src/film/assets/tbc/shots/e4_s2.mp4` VIC, same place. He looks down (about 1.0-2.3), then
  "It's my daughter's wedding." about 2.4-4.0, a wait, "I did ask her." about 5.4-7.0. Use from about 1.7.
  Same framing as e3_s1.
Existing clips (words beside each):
- `src/film/assets/apa/clips/j06_maybe_a.mp4` JO: "We voted on the dates." / "Maybe won." (0.05-1.30, 2.25-3.80;
  punch 1.06 at cx 0.42, cy 0.30, as in danny.json).
- `src/film/assets/apa/clips/munich_a.mp4` the grandmother sipping tea in a roaring stand (no words). Take the
  two "key" colour fixes for this source from danny.json unchanged. The man pinned DANNY stands behind her
  left shoulder (danny.json pins him at x 235, y 362 on the freeze at 4.50).
- `src/film/assets/apa/clips/j22_gran_a.mp4` the grandmother in her armchair (sign-off: 3.70-4.62 muted).
- `src/film/assets/tbc/clips/gran_go.mp4` the grandmother: "I'm not in the group chat. I just go." voice
  0.46-1.80 and 2.26-3.08. Picture holds until 7.1 (after that her cup hand breaks): use 0.15-5.25 only.
- `src/film/assets/tbc/clips/j01_tab_a.mp4` LEE in a dark bedroom lit by a laptop: "I closed one tab." /
  "It was the flights." (0.10-4.30 for the lines, 4.30-6.00 after them).
- `src/film/assets/tbc/clips/f_selfie.mp4` LEE, selfie in daylight: "They just moved the kick-off." is 2.40-4.20.
  Punch 1.35 at cx 0.385, cy 0.57 (this hides a garbled banner on the wall, a can and a laptop). "Again." is NOT used.
- `src/film/assets/tbc/clips/j12_early_a.mp4` the face-painted man alone in an empty stand with a sandwich:
  "I'm a bit early." / "Two days." Punch 1.4 held to the top (cx 0.5, cy 0.357), never more than 1.5 (the clip is
  720 wide and goes soft); this crop removes two large crests on his scarf.

## Narration (recorded; `src/film/assets/tbc/vo/en/<name>.wav`, word timings in `<name>.wav.words.json`)
Whole lines, and each sentence of a multi-sentence line on its own with a letter (a, b, c):
- e1_n1 4.49 s "Eight people planned that trip. Two arrived. Neither was in the chat." (a 1.66, b 1.04, c 1.39)
- e1_n2 2.34 "This season they are doing it properly."
- e1_n3 1.64 "The dates came out at seven."
- e1_n4 2.77 "Seen by seven. Opened by one." (a 1.21, b 1.36)
- e1_n5 2.12 "The organiser had removed 'maybe'."
- e1_n6 2.21 "Danny has been typing since Tuesday."
- e2_n1 2.69 "Lee had been comparing flights since nine."
- e2_n2 1.87 "He did not say which Friday."
- e2_n3 2.95 "Flights. Hotel. Time off work." (a 0.78, b 0.73, c 1.04: place a, b, c separately, one per stamp)
- e2_n4 1.50 "She guessed Saturday."
- e3_n1 3.78 "To buy a ticket, a supporter needs a history of buying tickets."
- e3_n2 5.19 "The group put in nine names. One came out. The winner had not been asked." (a 1.94, b 1.13, c 1.76)
- e4_n1 4.70 "A different team won a different match. So this one moved to Monday." (a 2.42, b 2.07)
- e4_n2 1.64 "The flight home did not."
- e4_n3 1.92 "Every change cost money."
- e5_n1 3.32 "The subject is the only adult present in face paint."
- e5_n2 2.41 "He is also the only adult present."
- e5_n3 4.19 "Staff have asked him to leave. He has explained that it is his seat." (a 1.83, b 2.16)
- e5_n4 3.99 "The match moved while he was in the air. He flies home on Wednesday." (a 2.17, b 1.62)
- e6_n1 1.69 "The kick-off time is pinned."
- e6_n2 2.31 "It has been asked fourteen times."
- e6_n3 3.19 "The answer came from Sydney. It was two in the morning there." (a 1.57, b 1.49)
- e6_n4 1.84 "This is the pinned message."
(Lengths are approximate; read the file. Captions use the spelling above: "organiser", "kick-off", "fourteen".)

## The six episodes as decided

### 1. "Episode 1: Should Be Fine" (was "Right."), about 31 s. Post caption: "Episode 1. The dates are out. There is a spreadsheet."
As plan.json, with these changes:
- Chat at 10.9: narrator says e1_n3 "The dates came out at seven." over "Danny left" and the divider
  "8 MONTHS LATER"; THEN Vic's "Right." (07:02) pops in silence; then "Maya added Danny" (8 members); then
  "Danny is typing…" arrives at the bottom and stays for the rest of the episode's chat screens.
- Name tag: MAYA / ORGANISER, UNOPPOSED.
- "Seen by seven. Opened by one." (e1_n4a, e1_n4b): no narrator caption; "Seen by 7" lands on the first sentence
  and "Opened by 1" on the second, with the picture pushed in on the message and its card so the receipts read
  at caption size.
- Stamp on the grey freeze: "TO BE\nCONFIRMED" with small "EPISODE 1".

### 2. "Episode 2: So You Guess", about 25 s. Post caption: "Episode 2. Everything gets booked."
As plan.json, with these changes:
- Narration after Vic's bubble "Hundred percent. I'll pay you Friday." is e2_n2 "He did not say which Friday."
  (replaces the "unsecured lender" line). The chip OWE MAYA: 6 then lands in silence with a click.
- Checklist: "Flights." "Hotel." "Time off work." (e2_n3a, b, c) each land with their green stamp; no narrator
  caption; then a full second of nothing; then the red TO BE CONFIRMED on DATE OF MATCH, with the thud and jolt.
- e2_s1's last look is at the laptop (there is no phone on her table).
- Tile on the frozen frame: kicker "MATCH", title "SATURDAY*", foot "*a guess".

### 3. "Episode 3: A History of Buying Tickets", about 28 s. Post caption: "Episode 3. One ticket short."
NEW ORDER (the grandmother is not seen until her card turns green):
1. 0.0-2.9 e3_s1 first sentence, "I buy tickets to matches I can't go to." Captions in two cards
   ("I buy tickets to matches | I can't go to."). Name tag VIC / IN. HUNDRED PERCENT.
2. 2.9-7.6 the two-box loop graphic with e3_n1; stamp NOT ELIGIBLE lands as he finishes; day tag
   "61 DAYS BEFORE*" from here.
3. 7.6-10.2 e3_s1 second sentence, "Keeps my record up.", then the counters ATTENDED 11 · BOUGHT 19 land in his
   silent look.
4. about 3.8 s of chat: Vic "Got six. All on my phone." / Maya "One short. Put a name in for the last ticket.
   Any name." / Lee "My landlord."
5. about 6.4 s of name cards: nine cards turn to UNSUCCESSFUL one by one under e3_n2a "The group put in nine
   names."; the ninth, already carrying the name DANNY'S GRANDMOTHER, turns green and reads SUCCESSFUL under its
   name on e3_n2b "One came out."; e3_n2c "The winner had not been asked." There is NO typed line repeating it.
6. about 2.3 s of chat, nothing said: "Maya added Danny's grandmother" (9 members), then "Danny's grandmother
   left" (8 members).
7. gran_go 0.15-3.35 "I'm not in the group chat. I just go." Captions. No pin on her.
8. Sign-off continues on the same shot, 3.35-5.25, with status "ONE TICKET. HER OWN." and the usual foot.

### 4. "Episode 4: It's Been Moved", about 27 s. Post caption: "Episode 4. The match has moved to Monday."
As plan.json, with these changes:
- Name tag on the first shot: LEE. The "Again." insert is cut.
- One graphic beat for the week strip: e4_n1a "A different team won a different match." e4_n1b "So this one moved
  to Monday." (the MATCH tile slides to Monday on "moved"; the day count 40 is struck and 42 written), then e4_n2
  "The flight home did not." (FLIGHT HOME shows red on Sunday).
- The typed list: narrator e4_n3 "Every change cost money." over three lines: FLIGHTS: MOVED, FOR A FEE / HOTEL:
  BOOKED AGAIN / T-SHIRTS: PRINTED. THEY SAY SATURDAY. Then, in silence, a fourth line is typed, THE MATCH: MOVED,
  and the red stamp lands on it: NO CHARGE. There is no fifth line.
- Chat: Maya "All moved to Monday. Do not ask." Then "Vic is typing…" (in the header, while Danny's stays at the
  bottom).
- Vic's name tag: VIC / HOLDS ALL SIX TICKETS.

### 5. "Elsewhere: A Bit Early", about 24 s. Post caption: "Elsewhere. A man has reached his seat."
As plan.json, with these changes: the stamp is ARRIVED (no question mark); the punch-in starts at 1.5 and eases
back to 1.4, never more. No stretch of any piece beyond twofold; if it reads as a frozen video, bring the week
strip forward.

### 6. "In the Chat: It's Pinned", about 18.5 s. Post caption: "In the chat. Somebody has asked what time kick-off is."
1. e2_s1's silent tail (her look, then eyes closing), with e6_n1 "The kick-off time is pinned."
2. Chat. The pinned bar is collapsed and cut off, as messengers show it: "Pinned · Kick-off: Mon…". Priya:
   "what time's kick-off?" / Marcus: "what time is it again" / Jo: "is it 7:45 or 8". Narrator e6_n2 "It has been
   asked fourteen times." There is NO counter chip.
3. Maya: "It's pinned." / Marcus: "where"
4. Sydney (no time note): "Top of the chat." / Marcus: "cheers Sydney". Narrator e6_n3 "The answer came from
   Sydney. It was two in the morning there."
5. The bar slides down and opens; narrator e6_n4 "This is the pinned message." Then two typed lines, KICK-OFF:
   MONDAY and TIME:, and the red stamp TO BE CONFIRMED lands on the second, in silence. Nothing is said after
   the thud; hold it about 1.2 s.
6. Sign-off, status "NOT IN THE CHAT."

## Files an episode's editor writes
- The edit list: `src/film/tbc/e<N>.json` with "slug": "tbc-e<N>", "out": "ads/16-tbc/venuvoy-tbc-e<N>",
  "title": the episode's title.
- Chat and board scripts: `src/film/tbc/scenes/e<N>_<name>.json`; filmed to `src/film/assets/tbc/scenes/e<N>_<name>.mp4`.
- Nothing else in the repository. Other editors are working on the other episodes at the same time: do not edit
  the shared tools (cut.py, overlay.html, chat.html, board.html, stamp.js, kit.py, render-chat.mjs). If a tool
  cannot do what the episode needs, work within what it can do and report the gap.


## House style (the same in all six, so they read as one series)
- Edit list settings: "fps": 24, "duck_from": 0.0, "duck_mode": "voice",
  "look": { "pop": 0.03, "shake": 0, "sharpen": 0.12, "grain": 3.0, "cap_y": 1260, "nar_y": 1300 }.
  Chat and board segments take "pop": 0 (text must not breathe) and no zoom unless the cut sheet asks for a push-in.
- THE WORDMARK: film "labels" entry { "text": "VENUVOY", "y": 258, "cls": "bug" } (top-left, x 60), from the
  first cut at or after 3.0 s, over footage and boards. NOT over chat screens (the chat's header is there): give
  it separate from/to ranges around them. It ends where the sign-off starts.
- THE DAY COUNT: film "labels" entry { "text": "96 DAYS BEFORE*", "small": "*to be confirmed", "y": 262,
  "xr": 1040 } from where the plan first shows it, straight through footage, boards and chat screens, ending
  where the sign-off starts. On Episode 4's week strip the correction is this label's "strike" (40 -> 42), NOT
  the board's own "count" (leave the board's "count" out, so there is one day count and it looks the same
  everywhere). Tags that are not a day count ("LAST SEASON", "02:14", "ELSEWHERE") sit top-left under the
  wordmark's row (x 60, y 306) and may be up from frame 0.
- THE CHIP "OWE MAYA: 6" lives in the chat's header only (chat.html "chip"): it lands once in Episode 2, and is
  already there (t <= 0) on every chat screen of Episodes 3, 4 and 6. It is not drawn over footage or boards.
- "Danny is typing…" (chat.html "typingbar") arrives in Episode 1 where the cut sheet says and is already there
  (t <= 0) on every later chat screen in every episode.
- The chat's header: name "Away trip – who's in?", second line "8 members" (7 before Danny is added in Episode 1;
  9 for a moment in Episode 3). Maya's bubbles are "me": true with a time.
- NAME TAGS are lower thirds: name in capitals and one short line under it (MAYA / ORGANISER, UNOPPOSED), left,
  with the top at about y 1085 so they sit above the speaker's caption and never touch it; up for the speaker's
  first two seconds or so. Check on a frame that it does not cover the mouth.
- CAPTIONS: speakers' lines come from the segment's "line" (cards split with "|", at most about four words a
  card); the narrator's from the "narration" entry's "text". Use the spellings in this file.
- STAMPS: one red stamp with a thud per episode (green stamps on the checklist are small and quiet: a soft
  thud at about -14 dB each). The thud goes 0.06 s after the stamp's hit time, in the overlay, the chat and the
  boards alike; the frame jolts with it (overlay stamps need the segment's "jolt" at the hit time).
- SOUND: no music. Footage plays its own sound. Chat and board scenes are never digitally silent: lay
  `src/film/assets/apa/audio/bed_hall_quiet.flac` under them, low (about 35-40 dB under the narrator; measure it).
  Small exact effects: "msg" for an incoming message (Maya's own messages: the same, quieter), "sys_tick" for a
  system line, "vote" with args [0..5] for poll votes, "click" for the chip, "typing" very low where the cut
  sheet asks for typing ticks, "thud" for stamps. Effects sit well under the voices; nothing may mask a word.
  The film is mastered to -14 LUFS by the engine.
- THE SIGN-OFF is identical in every episode apart from its status line (see above), and the name label must
  not lie across her face: check the frame and adjust the zoom, not the label.


## House numbers (settled by Episode 1, `src/film/tbc/e1.json`: copy them, so the six sound and sit alike)
- Sign-off sounds, exactly: { "synth": "clock_tick", "at": "signoff+0.15", "gain_db": -8 }, { "synth": "cup", "at":
  "signoff+0.55", "gain_db": -10 }, { "synth": "two_note", "at": "signoff+0.75", "gain_db": -12 }, { "synth":
  "clock_tick", "args": [1], "at": "signoff+0.95", "gain_db": -8 }. Sign-off segment: "in": 3.7, "out": 4.62,
  "fit": 1.8, "mute": true, "zoom": { "from": 1.1, "to": 1.17, "cx": 0.75, "cy": 0.8 }.
- Room tone under chat and board scenes: bed_hall_quiet.flac, "offset": 3.0, "gain_db": -12, "duck_db": 0,
  "fade_in": 0.5 (about 37-39 dB under the narrator).
- The red stamp's thud: "gain_db": -6, at the hit + 0.06. Small green stamps: -14.
- Chat sounds: "msg" -10 for an incoming message and -15 for Maya's own; "sys_tick" -6; "vote" -10 (args 0..5);
  "typing" -26 (args [length, 0.11, a seed]); chip "click" -10.
- A narrator caption over a chat screen sits at "y": 1288 (at 1300 a two-line box touches the chat's input bar).
- A speaker's one-card caption: the engine splits a sentence into cards of about three words; write the cards
  yourself with "|" where the split matters, and look at how e1.json holds a short line as one card.


## DECISIONS AFTER THE FIRST REVIEW (a cold viewer watched Episode 1; these override anything above, in all six)
1. THE SIGN-OFF IS LONGER AND IN TWO STEPS (1.8 s could not be read by a stranger). Now 3.2 s:
   - Picture: `src/film/assets/tbc/clips/gran_go.mp4` "in": 3.6, "out": 6.8, real time, muted (she lifts the
     rose teacup, sips, begins to lower it; do not go past 6.9, her hand breaks at 7.1), slow push-in
     "zoom": { "from": 1.06, "to": 1.12, "cx": 0.5, "cy": 0.45 }; check that the label sits on her hair, not
     across her eyes. (Episode 3: the sign-off simply continues her spoken shot, 3.35 to 6.55, same zoom values as
     her line so there is no jump.)
   - gfx: { "type": "status", "at": 0.15, "name": "DANNY'S GRANDMOTHER", "status": "<this episode's>",
     "foot": "Next episode: Thursday. Confirmed.", "foot_at": 1.0, "foot_size": 56 }: the label is read alone for
     about a second, then the wordmark and the line arrive and hold for two.
   - Sounds: clock_tick at signoff+0.15, clock_tick args [1] at +0.95, clock_tick at +1.75, clock_tick args [1]
     at +2.55 (all -8 dB); two_note at signoff+1.15 (-12 dB) so it rings out under the line. No cup sound.
     Hard cut on the last frame.
2. NO CORNER WORDMARK. Remove every "VENUVOY" label of cls "bug" from the edit list. The brand appears only in
   the sign-off. (A logo in the corner from second 3 made a stranger read the film as an advert.)
3. Jo's caption is “MAYBE” WON. (with curly double quotes), and the narrator's caption is
   The organiser had removed ‘maybe’. (curly single quotes).
4. Episode 1's narrator line at the first chat is re-recorded: "The match dates came out at seven a.m."
   (file `src/film/assets/tbc/vo/en/e1_n3.wav`, replaced; read its new length). Caption: the same words.
5. Episode 1's stadium shot is replaced by `src/film/assets/tbc/clips/gran_stand.mp4` when that file exists (the
   same grandmother as at home, in the roaring stand); if it does not exist, keep munich_a. Either way the shot
   must keep MOVING under "Two arrived." and the two name pins (slow it down if it is short), and freeze only at
   the moment the colour drains, on the very frame it has reached: no jump into the freeze.
6. A PUNCHLINE GETS ITS SILENCE. After a person's punchline hold their face, silent, for 0.5-0.7 s before the cut
   (use the tail of the take; check the hazards). After a stamp, hold it at least 1.2 s with nothing said.
   After a thud nothing is heard for at least 0.8 s.
7. NAME TAGS MUST BE READ: bring the tag in on the first frame of the person's shot, hold it at least 2.2 s, and
   where the person has a second shot in the episode show it again there. Its second line may take "wsize": 44.
8. CHAT SCREENS MUST NOT SIT EMPTY OR SMALL. Where a chat screen holds only a few lines, push the picture in on
   the thread ("zoom" on the segment, about 1.2-1.35, anchored so every word of the thread, the typing indicator
   and the day count's corner stay clear) once the header has been seen, or stage the beat as its own close
   script the way Episode 1's receipts were. Cut any stretch where nothing arrives and nobody speaks for more
   than about 1.2 s, unless it is the silence after a hit.
9. A narrator caption must not cover the thing the picture is about (the teacup, a face): move that caption up
   or down with the narration entry's "y".
10. The first caption is on screen on frame 0 (start the first segment a few frames into the line's lead-in, or
    use the segment's own "cap" so it is up from the first frame).

11. `src/film/assets/tbc/clips/gran_stand.mp4` EXISTS (7.9 s, 720 x 1280, no words; its own sound is the crowd's
    roar). Use it in Episode 1 instead of munich_a, with no colour-key fixes (those were for munich_a). What
    happens in it: she sits calm in the roaring stand with the rose teacup; lifts it 0.4-1.5; sips 1.5-4.6;
    lowers it 4.8-5.6; looks ahead, composed, 5.6-7.9. The man to pin as DANNY is the one in the dark jacket
    with his fist up directly behind her, picture-left of her head (about x 0.33, y 0.30 of the source frame).
    There is enough real movement for the whole beat: play it in real time under "Eight people planned that
    trip. Two arrived. Neither was in the chat." with the pins landing while she sips, and freeze only where the
    colour drains for "This season they are doing it properly." A few fans' scarves carry a small blurred patch
    like a badge; if one reads as a crest at full size, soften that spot (see the "soften" fix in danny.json).

12. MAYA'S EPISODE 1 SHOT IS RETAKEN: `src/film/assets/tbc/shots/e1_s1b.mp4` replaces e1_s1.mp4 (whose close-up
    showed bared, discoloured teeth). "I made a spreadsheet." voice 0.76-1.88, then two full seconds of silence
    in which she holds the look; "I wanted to check the link worked." voice 3.92-5.54, then she holds the
    interviewer's look and at about 6.6 lowers her eyes. No stray hands anywhere in the take. Use 0.50-2.35 for
    the first beat and 3.70-6.15 for the second (the last 0.6 s is her silent look: the hold the punchline needs).
    Same standard Maya framing.

13. SOUND, after measuring Episode 3 as a phone would play it (300 Hz - 3.4 kHz): the stamp's thud and the closing
    notes have been rebuilt in kit.py to carry in that range, and every narrator file now has a presence EQ (same
    names, same lengths, same word times). Nothing to change in an edit list for this, but measure again: the
    narrator should sit level with the interviewees in that band (within about 2 dB), and the red stamp's thud
    should be clearly audible in it without being louder than a spoken line. NO DIGITAL SILENCE AT CUTS: room
    tone and clip sound must overlap every cut by at least 0.1 s (start a bed a little before the cut it enters
    on and end the one before a little after); check final.wav for any 5 ms window under -65 dB.
14. The board's losing name cards now all but vanish when the winner grows (no cut-off words beside it). On a
    board, do not let the graphic give away the narrator's sentence before he reaches it: a box, tile or row
    that IS the punchline appears on its word, not before.


## EVERYTHING THE LOST PLAN SAID THAT AN EDITOR STILL NEEDS (the plan file is gone; this section replaces it)
Day counts (the label "NN DAYS BEFORE*" with the small line "*to be confirmed"): Episode 1: 96 (from the first
chat screen on). Episode 2: 89 (arrives with the narrator's first line). Episode 3: 61 (from the loop graphic on).
Episode 4: 40, struck and corrected to 42 when the match tile slides. Episode 5 ("Elsewhere"): no day count; a
tag ELSEWHERE top-left (x 60, y 306) from frame 0. Episode 6 ("In the Chat"): 37.

### Episode 1, beat by beat
1. Jo, j06_maybe_a 0.05-1.30, jump cut to about 2.37-3.83 (a little tighter): "We voted on the dates." /
   “MAYBE” WON. Tag LAST SEASON (x 60, y 306) from frame 0 until the colour drains. Keep the first caption up
   through the jump cut.
2. gran_stand from about 1.25, real time, about 5.7 s: narrator e1_n1. Pin DANNY (the man behind her) on "Two",
   pin DANNY'S GRANDMOTHER a moment later, both read before "Neither was in the chat." The narrator's caption
   sits clear of the teacup.
3. The frame it has reached freezes as the colour drains (0.6 s): narrator e1_n2 "This season they are doing it
   properly." The stamp "TO BE\nCONFIRMED" (small line "EPISODE 1" only if it can be read; else leave it out)
   lands as he finishes, with the jolt and the thud; held 1.25 s, nothing said.
4. Chat: "Danny left" already there; divider "8 MONTHS LATER"; narrator e1_n3 "The match dates came out at seven
   a.m."; then Vic "Right." (07:02) in silence; "Maya added Danny" and the header goes from 7 to 8 members;
   "Danny is typing…" arrives. Pushed in on the thread; no stretch of waiting.
5. Maya, e1_s1b 0.02-2.25: "I made a spreadsheet." Name tag MAYA / ORGANISER, UNOPPOSED from her first frame.
6. The receipts, close: Maya's bubble "It's all in here." with the card "Trip plan / spreadsheet" (07:31);
   "Seen by 7" lands on e1_n4a, "Opened by 1" on e1_n4b; no narrator caption; "Danny is typing…" under it.
7. Maya, e1_s1b 3.70-6.15: "I wanted to check the link worked." and her silent look. Name tag again.
8. The poll (Maya's, 07:44): "Are you in?" Yes / No; narrator e1_n5 "The organiser had removed ‘maybe’."; Yes
   gets 1; after he finishes, "Jo added an option: Should be fine" and five votes land on it in silence, ticks
   rising. The dot that marks a choice stays on Yes (the organiser's own vote); only counts and bars move.
9. Narrator e1_n6 "Danny has been typing since Tuesday." over the typing indicator; typing ticks, a little louder.
10. Sign-off: NOTHING BOOKED.

### Episode 2, beat by beat
1. Lee, j01_tab_a 0.10-4.30: "I closed one tab." / "It was the flights." Tag 02:14 top-left from frame 0; name
   tag LEE. Hold his face about 0.6 s after "flights."
2. The rest of the take, slowed: narrator e2_n1 "Lee had been comparing flights since nine." The day count
   arrives here. Leave about 0.8 s after the line so the viewer can look at 02:14 and do the sum.
3. Chat: Maya "Booked everyone. I've paid. Pay me back when you can." (07:40); Vic "Hundred percent. I'll pay you
   Friday." (07:41); narrator e2_n2 "He did not say which Friday."; then the chip OWE MAYA: 6 lands in the
   header with a click, in silence, and is held only about half a second. "Danny is typing…" from frame 0.
4. The checklist board (file line THE STUDY · FILE 02, title STATUS OF BOOKINGS): FLIGHTS gets a green BOOKED on
   e2_n3a, HOTEL a green BOOKED on e2_n3b, TIME OFF WORK a green APPROVED on e2_n3c (one and the same soft stamp
   sound for the three); a full second of nothing; then DATE OF MATCH gets the red TO BE CONFIRMED with the thud.
5. Maya, e2_s1 0.1 to about 4.6: "Wait for the date | and the flights are gone. | So you guess." (cards split
   there, so the if-then shape reads). Name tag MAYA / ORGANISER.
6. "She guessed Saturday." (e2_n4) over LIVE footage of her holding the look (the same take running on, about
   4.6-6.6): never a frozen frame. The calendar tile (kicker MATCH, title SATURDAY*, NO foot line) drops in only
   after the narrator has finished, and his caption goes as it lands; hold it about 0.9 s.
7. Sign-off: HAS KEPT THE MONTH FREE.

### Episode 4, beat by beat
1. Lee, f_selfie 2.40-4.20: "They just moved the kick-off." Name tag LEE. (The first editor anchored the 1.35
   crop low and left, about cx 0.04, cy 0.9, so tag and caption stay off his mouth; the banner and laptop must
   stay out.) Hold his face after the line.
2. The week strip (file line THE STUDY · FILE 04, title THE WEEKEND; days FRI SAT SUN MON; tiles FLIGHT OUT on
   FRI, THE MATCH on SAT, FLIGHT HOME on SUN): e4_n1a "A different team won a different match."; e4_n1b "So this
   one moved to Monday." with THE MATCH sliding to MON on "moved" (red MOVED tag) and the day count struck from
   40 to 42; then e4_n2 "The flight home did not." as FLIGHT HOME turns red.
3. Maya, e4_s1 0.15-6.1: "I'd booked three hotels. | Saturday, Sunday, Monday. | I cancelled Monday | on
   Tuesday." Name tag MAYA / ORGANISER. No frozen frame at the end: cut on her last live frame before 6.1.
4. The typed list (file line THE STUDY · FILE 04, title WHAT COULD BE MOVED): e4_n3 "Every change cost money."
   over FLIGHTS: MOVED, FOR A FEE / HOTEL: BOOKED AGAIN / T-SHIRTS: PRINTED. THEY SAY SATURDAY.; then in silence
   THE MATCH: MOVED is typed and the red stamp NO CHARGE lands on it with the thud; held 1.2 s.
5. Chat: Maya "All moved to Monday. Do not ask." (19:26); then a second typing indicator, "Vic is typing…",
   above Danny's. Chip OWE MAYA: 6 already there.
6. Vic, e4_s2 from about 1.7: "It's my daughter's wedding." / "I did ask her." Name tag VIC / HOLDS ALL SIX
   TICKETS. Hold his look after the last word.
7. Sign-off: FREE ON MONDAY.

### Episode 5 ("Elsewhere: A Bit Early"), beat by beat
1. j12_early_a (the face-painted man alone in the empty stand with a sandwich), punch 1.5 easing to 1.4, held to
   the top: "I'm a bit early." / "Two days." Tag ELSEWHERE from frame 0. Hold his face after "Two days."
2. Narrator e5_n1 "The subject is the only adult present in face paint." over him.
3. Narrator e5_n2 "He is also the only adult present." (the empty stand around him must be in frame here).
4. Narrator e5_n3 "Staff have asked him to leave. He has explained that it is his seat." (he eats).
   The take is short for beats 2-4: slow it (never more than twofold), use every part of it once, and bring the
   week strip forward rather than hold a frozen frame.
5. The week strip (see src/film/tbc/demo_board_week_elsewhere.json for the layout the plan asked for: a pin
   REACHED HIS SEAT on the day he arrived, THE MATCH moving two days later, FLIGHT HOME before it): narrator
   e5_n4 "The match moved while he was in the air. He flies home on Wednesday." Then the red stamp ARRIVED (no
   question mark) with the thud; held 1.2 s.
6. Sign-off: TRAVELS ON THE DAY.

## WHAT THE CRITICS FOUND IN EPISODES 2 AND 3 (all of it applies to every episode)
- THE SIGN-OFF, final form (3.6 s; this replaces decision 1's timings): gran_go "in": 3.25, "out": 6.85, real
  time, muted, zoom 1.06 to 1.12 at cx 0.5, cy 0.45 (check her mouth is closed on the first frame; if not, start
  at 3.35). gfx "status" "at": 0.15, "foot_at": 1.3, "foot_size": 56. Clock ticks at signoff+0.15, +0.95 (tock),
  +1.75, +2.55 (tock), +3.35 (all -8 dB); two_note at signoff+1.45 (-12 dB). Her eyes come up to the lens at
  about a second in and she lowers the cup to a small smile at the end: the foot arrives after the look, and the
  smile is on screen for the last half second. (Episode 3: its sign-off continues her spoken shot, cut the same
  way: label first, then the foot after her look.)
- A held picture must be alive. Never freeze a person under a narrator's line: use the live tail of the take.
  The only freezes in the series are the drained stand in Episode 1 and a frame held for less than half a second.
- Never slow a shot in which something moves fast across the frame (a hand, a microphone): frame blending
  shows a ghost. Vic's silent look in Episode 3 plays in real time and is cut before the microphone leaves.
- A graphic must not finish before the narrator's sentence: build it on his words (the loop's second box on
  "a history of", the loop closing on "tickets."; a moved tile on "moved").
- The name cards: hold the whole grid about 0.8 s so the names can be read, then turn the cards quickly
  (about 0.22 s apart) in a scrambled order, so the last one standing is not obvious.
- Give a person a beat to be seen before their caption appears when they have not been seen before (about
  0.25 s), and keep a name tag up through the silent look after the line.
- Two texts must never ask to be read at once. A caption leaves when a tile or stamp lands.
- Nothing is explained: no footnote that says what the line already said.
- Mix: every room, chat and board has a quiet tone under it (about -40 dBFS), the same kind in every scene of
  an episode, so a pause is a held breath and never a hole; message pops must be audible on a phone (about
  -24 dBFS at their loudest); the red stamp is the heaviest sound of its episode.

## AS SHIPPED (after the last cold viewing; these numbers are what the six edit lists now hold)
- The sign-off: gran_go 3.25-6.85 slowed to fill 4.3 s ("fit": 4.3), status at 0.15, "foot_at": 1.2,
  "foot_size": 52; ticks at +0.15, +0.95, +1.75, +2.55, +3.35, +4.15; the two notes at +1.35.
- Maya's name tag is MAYA / ORGANISER, UNOPPOSED in every episode, so each stands on its own.
- Text that carries a joke stays out of the right-hand strip where the apps draw their buttons (x > 940 between
  about rows 900 and 1550): the receipts in Episode 1 end near x 940, the red stamp in Episode 2 is nudged in.
- Episode 5 has a second shot, `src/film/assets/tbc/clips/early_next.mp4`, filmed from the last frame of the
  first; under its narration the caption sits at y 1360 because the shot invented a white collar and tie.
- Lengths: 36.8, 31.5, 32.1, 33.3, 25.2 and 21.7 seconds.
- Money spent on generation for this batch: about 2.6 dollars (two stills, eight shots of eight seconds).
