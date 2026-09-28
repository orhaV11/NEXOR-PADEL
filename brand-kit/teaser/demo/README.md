# The ten seconds — launch teaser

A 12-second product demo of the whole loop: a look goes in, a score comes out, the one tip lands, the
community fires it. Cut so the first two seconds already say what the app is.

| file | pixels | use |
|---|---|---|
| `orevosh-teaser-1080x1920.mp4` | 1080×1920, h264, yuv420p, 30 fps, 12.00 s, 0.92 MB | TikTok, Instagram Reels, YouTube Shorts. **Silent on purpose** |
| `orevosh-teaser-cover-1080x1920.png` | 1080×1920 | Thumbnail option A: the frame at 7.8 s, the one tip |
| `orevosh-teaser-cover-alt-1080x1920.png` | 1080×1920 | Thumbnail option B: the frame at 3.36 s, the score landing on 9 |
| `teaser.html` | — | The renderer. Every frame is drawn from one time value by `window.seek(t)` |
| `render-teaser.js` | — | Capture, encode, both covers, verify |

**Two covers, one pick.** Both come out of the finished MP4, so each is exactly a frame of the video.
The tip frame sells what the app *does*; the score frame sells the number. Pick one per platform — there
is no need to ship both.

**No sound.** No music is licensed, and both platforms reward a sound added inside the app: the poster
picks a track in TikTok or Reels and it plays over the cut. Nothing in the video depends on audio, and
every word is on screen.

**Safe area.** TikTok and Instagram draw their own UI over the bottom 320 px and the right 180 px. Every
word, the wordmark and the mark sit inside x < 900 and y < 1600. The phone's bottom corners are the only
thing that runs past the line, and nothing is written there.

## The look

The outfit being checked is `tools/brand/templates/photos/look-2-camel.jpg` — a real look, used with the
owner's permission — in all three places a photo appears: the card the finger taps on the check screen,
the block that settles into the result layout, and the feed card at the end. The check screen is the real
app capture with the look laid into its photo slot and the occasion field refilled; the capture's own
*Change photo or clip* pill and dock disc are put back on top, so the screen stacks exactly as it shipped.

**The crop.** Every slot the app gives a look is 4:5, and the photo is 941 × 1672 — taller than 4:5 at
any width — so the crop is the full width and the top 1176 px, anchored to the top. That keeps the camel
coat, the white turtleneck, the layered necklaces, the quilted bag, the coffee, the brown mini skirt and
the top of the tights in frame. **It loses the knee-high boots**: a 4:5 crop of this photo cannot hold
both the head and the boots, and the alternatives — cutting the face at the jaw, or dropping the head
altogether — both read worse in a 12-second cut. The one tip is about the tights, which are in frame; the
boots it implies are not. If the boots have to be visible, the fix is a wider source photo, not a
different crop of this one.

## The copy

Every score, headline, occasion and tip in the cut is this look's stylist copy, and none of it is
invented for the render:

| | |
|---|---|
| intent | DATE |
| score | 9 |
| headline | *Camel and chocolate, done right* |
| occasion | *coffee and a walk* |
| breakdown | FIT 9 · COLOR 9 · ACCESSORIES 8 |
| the one tip | *Swap the black tights for sheer brown and the column runs unbroken.* |

Two fits had to move for the longer copy, and nothing else did:

- the one tip is 40 px, a step down from the 48 px the shorter tip took. Its longest line measures 670 px
  against the card's 696 px of content, so both lines stay on one line each.
- the result headline is 52 px, not 54. At 54 px *Camel and chocolate,* ran to 512 px in a 510 px column
  and wrapped to a third line over the occasion.

The *Reads as Date 72%* row is the app's own intent read, not a stylist line, and is unchanged.

## The beats

| in | out | what |
|---|---|---|
| 0.00 | 1.40 | The check screen. The photo is tapped, the intent chips light one by one, **Date** lands. Over it: *Where is this going?* |
| 1.40 | 2.40 | The photo settles into the frame, the ring starts, a thin counter ticks the seconds |
| 2.40 | 3.90 | The ring fills, the gradient sweeping round the stroke, and lands on **9** with /10 under it at **10.0s** |
| 3.90 | 4.60 | The headline arrives word by word: *Camel and chocolate, done right* |
| 4.60 | 6.60 | The breakdown snaps in: **FIT 9 · COLOR 9 · ACCESSORIES 8**, then *Reads as Date 72%*. Held to be read |
| 6.60 | 8.80 | **The one tip**, on its accent bar: *Swap the black tights for sheer brown and the column runs unbroken.* The longest beat |
| 8.80 | 10.60 | Posted. The card lands in the feed, the fire bursts, the count ticks 0 → 3, a comment slides in. *Then the community decides.* |
| 10.60 | 12.00 | The mark ignites, the wordmark, **Check the look.**, *coming soon*, 16+ |

The order is the shot list for a live-action version: the same seven beats, the same lines, the same
lengths.

## Rebuild after a copy change

```bash
cd brand-kit/teaser/demo
node render-teaser.js                       # frames -> mp4 -> both covers -> ffprobe check
node render-teaser.js --probe 0,3.36,7.8,11.9   # single frames to look at while editing
```

Playwright comes from `tools/e2e/node_modules`, the browser from `CHROMIUM_PATH` (default
`/opt/pw-browsers/chromium`), and `ffmpeg`/`ffprobe` from PATH. Scratch frames are written to
`/tmp/demo/frames` (`TEASER_SCRATCH` to move them) and deleted after the encode, so nothing lands in the
repository.

`teaser.html` uses **no CSS animation, transition or requestAnimationFrame**: a screenshot between frames
would catch an element mid-flight and the export would stutter. Everything — transforms, opacity, dash
lengths, text — is set from `t` in `window.seek(t)`, so frame N is always the same picture. Keep it that
way when you edit.

Swapping in a different look is a photo and a copy change, not a re-cut: point the three `look-2-camel.jpg`
sources at the new file, set `SCORE` and `bdVals` in the timeline, and retype the headline, occasion, tip,
feed head and badge. Then re-measure — a longer headline or tip will overflow before it wraps, because
both are `white-space: nowrap`.

Type is Outfit for display and Heebo for body, loaded from the local OFL copies in
`tools/brand/templates/fonts`, never from the network; the tokens are `DESIGN.md` §1. The phone beats put
the real capture from `tools/brand/templates/screens` inside the same CSS phone frame the story and store
templates use. The score ring, the breakdown rings, the tip card, the feed card, the fire burst and the
count are rebuilt as live elements so they move. The renderer fails loudly if a font or a capture does not
load.

Nothing in the cut claims a feature the app does not have: the score out of 10 for a chosen intent, the
three-part breakdown, one concrete tip, a private check until it is posted, fires and comments after.
The stylist judges the clothes and never the person — no line anywhere is about a body, a face or an age.
The end card says *coming soon* — there is no URL, because there is no app to link to yet.
