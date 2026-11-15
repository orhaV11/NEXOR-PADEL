# "One Night" (pilot of Plan, No Plan): where it stands

Read this first when picking the pilot up again. The script and its reasons are in `pilot_and_toolkit.md`; the proof
reports are in `proof_raw.json` (first proof, on an earlier 30 s cut) and `proof2_raw.json` (second proof, on this cut).

## The film as cut (25.08 s, 24 fps)

Edit list: `src/film/s2/one_night.json`. Render: `python3 src/film/cut.py src/film/s2/one_night.json en` (about ten
minutes) writes `ads/17-pnp/venuvoy-pnp-one-night-en_9x16_1080x1920.mp4` and `_4x5_1080x1350.mp4`. The version without
music is mixed from the dialogue and effects stems afterwards (see "Version without music").

| film s | shot (clip) | what happens |
|---|---|---|
| 0.00-2.04 | station, wide (`s1_t1` 0.71-2.75) | label "she said she packed light"; Timo: "Did you bring your whole house?" |
| 2.04-4.04 | single on Luna (`s1_t1` 4.0-6.0, 1.8x) | "It's the small one!" |
| 4.04-6.25 | uphill (`s2_t2`) | the cat walks past them on the wall; label "overtaken by a cat" |
| 6.25-8.08 | hilltop, wide (`s3_t3g`) | Luna: "Trust me." Timo: "No." The music stops after "Trust me". |
| 8.08-9.58 | single on Timo (`s3_t3g`, 1.95x) | "No, no, no!", cut off by the drop |
| 9.58-11.38 | ride (`s4_t2`) | the drop's first kick is on the cut |
| 11.38-13.58 | ride, closer (`s5_t3`, 1.12x) | "Yes!" |
| 13.58-15.13 | the cat (`s6_streak`) | the riders shoot past in front of it (a still laid over the clip) |
| 15.13-16.67 | from behind (`s6b_t1`, 1.25x) | Timo looks back: "Too slow!" |
| 16.67-17.54 | the cat (`s6_streak`) | a meow |
| 17.54-20.29 | gate, wide (`s7ax_t1`, 1.12x) | label "stadium rule: small bags only"; "I love this suitcase." and his head goes down |
| 20.29-23.83 | closer (`s7bx_t1`) | Luna: "It can't come in, Timo." His eyes open at 23.25 s; the music stops there. |
| 23.83-25.08 | the same shot runs on | signature in plain text: "Plan, No Plan · ep 1" / "by VENUVOY"; one final hit |

The second proof (`proof2_raw.json`) was run on the cut before this one. What it found and what was done:
the opening label covered Luna's eyes after the first cut (label now ends on the cut); the signature was missing on
the last frame (now held past the end); scarves floating in opposite directions at the gate and Luna's scarf turning
coral (both gate shots filmed again: `s7ax` is drawn from the start picture of `s7bx`, so the gate, the suitcase and
the scarves match); nobody read the place as a stadium, so the last line sounded like a plot hole (the gate label);
the uphill push was where viewers would swipe (0.9 s shorter, its grunts 7 dB lower); Luna's last line was 6-8
semitones lower than her others (the new take sits at 291 Hz against 333 and 296); the laid-over riders did not
match the clips in colour (`riders_side_graded.jpg`, made with a colour transform fitted on five anchors:
suitcase, jacket, shirt, trousers, shoes). Still open and left as they are: the suitcase's ribs are horizontal in
the standing shots and vertical in the ride shots; "It's the" in Luna's first line is nearly swallowed (the caption
carries it); the watching model sometimes reads the reverse angle at 15 s as the riders having swapped seats.

## Things learned the hard way

- **Crops in the edit engine**: `punch` scales about the point (`cx`, `cy`), it does not centre the window there. With
  `cx: 0` the left edge of the clip stays the left edge of the frame, with `cx: 1` the right edge. For a window whose
  left edge should sit at fraction x0 of the clip: `cx = x0 / (1 - 1/punch)`.
- **The model cuts inside a clip.** `s7a_t3` cuts by itself to a close-up at 2.75 s. Use only the part before it.
- **One-sided details drift** (backpack, scarf ends, cuffs, the suitcase's ribs) unless the start picture shows them
  and the prompt names them. The surest way to match two shots is to draw the second start picture from a frame of
  the first (`s5` from `s4`, `s7b` from the last frame of `s7a`).
- **Never lay in a separately made voice.** The pasted "Too slow!" was heard as three different phrases and as neither
  character. A short real shot with the character's own voice element (`s6b`) cured it.
- **The track's real grid.** `beats.py` puts the first beat of `jersey_1.mp3` at 0.29 s; the kicks say the bars start
  at 0.05 s and last 1.774 s. The break is at 3.6-6.2 s, a fill at 6.26, the groove from 7.15, the drop's first kick
  at 14.24. Blocks are therefore placed with `from_s` (seconds of the track), not `from_bar`.
- **Music on a phone.** The track is nearly all bass; on a phone speaker it vanished and was then called "stock
  music". The `phone` layer in `arrange.py` (8 dB less under 150 Hz, 5 dB more above 400 Hz) and the block gains in
  `one_night_music_talk.json` / `_drop.json` keep every spoken line at least 7 dB above the music in the 300-3400 Hz
  band while the music stays clearly audible. Measure on the stems in `work/pnp-one-night/cut-en/` after every render.
- **Judging music without ears.** Ask the listening model blind, music only, where the music comes from, twice in
  different words: `python3 src/film/s2/blind_music.py <scratch_dir> <track names>`.
  `jersey_1`, `jersey_0` and `funk_1` were the only tracks called a real app sound both times.
- **The watching model samples few frames a second.** It called Timo's mouth "frozen" on a line where the frames show
  it moving, and it cannot see a fly-past of a third of a second. Look at frames for anything shorter than a second.
- **Voices** cannot be settled without a human ear: pitch per line is consistent for each character, but the listening
  model answers "same voice" and "different voices" to the same reel on different asks.

## Version without music

    D=work/pnp-one-night/cut-en
    ffmpeg -i $D/stem_dialog.wav -i $D/stem_fx.wav -filter_complex \
      "[0:a][1:a]amix=inputs=2:normalize=0,volume=<master gain from the render log>dB,alimiter=limit=0.79:attack=2:release=60:level=disabled[a]" \
      -map "[a]" -ar 48000 $D/nomusic.wav
    ffmpeg -i ads/17-pnp/venuvoy-pnp-one-night-en_9x16_1080x1920.mp4 -i $D/nomusic.wav -map 0:v -map 1:a -c:v copy \
      -c:a aac -b:a 192k ads/17-pnp/venuvoy-pnp-one-night-en_9x16_no-music.mp4

It keeps the voices at the level they have in the main version, for posting with a sound chosen inside the app.
