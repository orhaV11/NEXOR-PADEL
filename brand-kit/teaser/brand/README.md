# Light it up — the launch teaser

Ten seconds of what OREVOSH is, cut for a cold scroll: a question, the mark, three real looks with three
verdicts, what the stylist reads, the feed, the week's board, the end card. It loops — the last frame is
the black of the first — so a second viewing starts before anyone decides to leave.

| file | pixels | use |
|---|---|---|
| `light-it-up-1080x1920.mp4` | 1080×1920, h264, yuv420p, 30 fps, 10.000 s, 300 frames, 1.62 MB | TikTok, Instagram Reels, YouTube Shorts. **Silent on purpose** |
| `light-it-up-cover-1080x1920.png` | 1080×1920 | The thumbnail: the end card at 9.52 s, fully in and before the fade |
| `teaser.html` | — | The renderer. Every frame is drawn from one time value by `window.seek(t)` |
| `render.js` | — | Capture, encode, cover, ffprobe check |

**No sound.** No music is licensed for this, and both platforms reward a track picked inside their own
app, so the owner adds one at upload. Nothing in the cut depends on audio; every word is on screen.

**Safe area.** TikTok and Instagram draw their own UI over the bottom 320 px and the right 180 px. Every
word, every numeral and the wordmark sit inside x < 900 and y < 1600, and centred type is centred on
x = 490, not 540. `node render.js --debug` draws the proof over the two frames where something overshoots
its resting size (the score stamp at 3.10 s, the medal at 8.14 s) and over one frame of every other beat.

## The three looks

The photographs are the owner's, used with permission, and live in `tools/brand/templates/photos/`. The
stylist judges the clothes and never the person, so the only words this cut puts beside a photograph are
the intent it was checked for and the score it was given — both from the verdict written for that outfit:

| look | intent | score | the rest of its verdict, for whoever edits this next |
|---|---|---|---|
| `look-1-streetwear.jpg` 1024×1536 | STREETWEAR | 8 | *All grey, and it holds* · a day out · FIT 8 COLOR 8 ACCESSORIES 7 · "Cuff the hem once so the sneakers read." |
| `look-2-camel.jpg` 941×1672 | DATE | 9 | *Camel and chocolate, done right* · coffee and a walk · FIT 9 COLOR 9 ACCESSORIES 8 · "Swap the black tights for sheer brown and the column runs unbroken." |
| `look-3-pink.jpg` 700×1244 | PARTY | 8 | *Full pink, full commitment* · a night that deserves it · FIT 8 COLOR 7 ACCESSORIES 8 · "Let the suit be the loudest thing: a plain shirt and a solid tie." |

A look carries its own score everywhere it appears — the feed never re-scores an outfit to fill a tile —
and no headline, tip or number that is not in this table may be written onto these photographs.

**The crops.** Every card in the cut is 9:16, the shape of the photographs, so a full-length look keeps
its head and its shoes instead of being squared off at one end. `look-2` and `look-3` are 9:16 to the
pixel and are shown whole. `look-1` is 2:3, so its 9:16 window takes 864 of the 1024 columns (x 60…924):
the man stands between x 260 and 730, so that window keeps him whole and centred, cap to sneakers, and
only trims empty concrete off the sides. `look-2` is a three-quarter-length photograph — it ends at the
boot shafts in the original, so no crop can show the toes.

The pink suit takes the week's medal at 8.00 s: that beat is 0.72 s and has to land at a glance, and the
full pink look is the one thing in the set that reads across a room. Its file is the smallest of the
three, but 700 px of source into a 440 px card is still a reduction, so it stays sharp there and in the
540 px card of the verdict beat.

## The beats

| in | out | what |
|---|---|---|
| 0.00 | 1.76 | *Does this look work?*, word by word, on black |
| 1.76 | 2.92 | The mark: the ring draws in one stroke, the flame snaps in and blooms |
| 2.92 | 4.42 | Three looks, three verdicts, half a second each: **STREETWEAR 8**, **DATE 9**, **PARTY 8**. The score stamps over the photo |
| 4.42 | 6.54 | What the stylist reads: *Fit.* *Color.* *Accessories.* *The one tip.*, the last on its gradient rule |
| 6.54 | 8.00 | The feed rushes up, the same three looks carrying the same three scores, fires popping across it, the count climbing to 486. *A community that lights it up.* |
| 8.00 | 8.72 | The week's board: the medal stamps onto the pink suit. *EVERY WEEK* |
| 8.72 | 10.00 | The mark, the wordmark, **Check the look.**, *coming soon*, 16+, out to black by 9.95 |

## Rebuild after a copy or photo change

```bash
cd brand-kit/teaser/brand
node render.js                      # frames -> mp4 -> cover -> ffprobe check
node render.js --probe 3.3,7.2,8.3  # single frames to look at while editing
node render.js --debug              # the safe-area proof
node render.js --frames-only        # stop after the PNG sequence
```

Playwright comes from `tools/e2e/node_modules`, the browser from `/opt/pw-browsers/chromium`, and
`ffmpeg`/`ffprobe` from PATH. Scratch frames are written to `/tmp/brand-real` (`TEASER_SCRATCH` moves
them), never into the repository. Type is Outfit and Heebo from the local OFL copies in
`tools/brand/templates/fonts`, never from the network; the tokens are `DESIGN.md` §1.

`teaser.html` uses **no CSS animation, transition or requestAnimationFrame**. The exporter screenshots
between frames, and anything the browser animated on its own would be caught mid-flight and the video
would stutter. Everything — transforms, opacity, dash lengths, text — is set from `t` in `window.seek(t)`,
so frame N is the same picture on every run and frame 0 and frame 299 are byte-identical. Keep it that way
when you edit, and check the loop with `md5sum` on the two end frames after a render. Two full runs give
byte-identical sequences; a `--probe` frame of the same second can differ from the sequence by a hair
(PSNR 52 dB, invisible) because Chromium has repainted the scaled photographs far fewer times by then —
judge sharpness from the encoded video or the frame sequence, not from a probe.

Nothing in the cut claims a feature the app does not have: a score out of 10 for a chosen intent, one
concrete tip, fires from the community, a weekly board. The end card says *coming soon* — there is no URL,
because there is no app to link to yet.
