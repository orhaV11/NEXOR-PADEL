# The ten seconds — launch teaser

A 12-second product demo of the whole loop: a look goes in, a score comes out, the one tip lands, the
community fires it. Cut so the first two seconds already say what the app is.

| file | pixels | use |
|---|---|---|
| `orevosh-teaser-1080x1920.mp4` | 1080×1920, h264, yuv420p, 30 fps, 12.00 s, 0.69 MB | TikTok, Instagram Reels, YouTube Shorts. **Silent on purpose** |
| `orevosh-teaser-cover-1080x1920.png` | 1080×1920 | The thumbnail: the frame at 7.8 s, the one tip |
| `teaser.html` | — | The renderer. Every frame is drawn from one time value by `window.seek(t)` |
| `render-teaser.js` | — | Capture, encode, cover, verify |

**No sound.** No music is licensed, and both platforms reward a sound added inside the app: the poster
picks a track in TikTok or Reels and it plays over the cut. Nothing in the video depends on audio, and
every word is on screen.

**Safe area.** TikTok and Instagram draw their own UI over the bottom 320 px and the right 180 px. Every
word, the wordmark and the mark sit inside x < 900 and y < 1600. The phone's bottom corners are the only
thing that runs past the line, and nothing is written there.

## The beats

| in | out | what |
|---|---|---|
| 0.00 | 1.40 | The check screen. The photo is tapped, the intent chips light one by one, **Date** lands. Over it: *Where is this going?* |
| 1.40 | 2.40 | The photo settles into the frame, the ring starts, a thin counter ticks the seconds |
| 2.40 | 3.90 | The ring fills, the gradient sweeping round the stroke, and lands on **7** with /10 under it at **10.0s** |
| 3.90 | 4.60 | The headline arrives word by word: *Clean casual with one weak link* |
| 4.60 | 6.60 | The breakdown snaps in: **FIT 7 · COLOR 8 · ACCESSORIES 4**, then *Reads as Date 72%*. Held to be read |
| 6.60 | 8.80 | **The one tip**, on its accent bar: *Swap the running shoes for plain white leather sneakers.* The longest beat |
| 8.80 | 10.60 | Posted. The card lands in the feed, the fire bursts, the count ticks 0 → 3, a comment slides in. *Then the community decides.* |
| 10.60 | 12.00 | The mark ignites, the wordmark, **Check the look.**, *coming soon*, 16+ |

The order is the shot list for a live-action version: the same seven beats, the same lines, the same
lengths.

## Rebuild after a copy change

```bash
cd brand-kit/teaser/demo
node render-teaser.js                       # frames -> mp4 -> cover -> ffprobe check
node render-teaser.js --probe 0,4.5,7.8,11.9   # single frames to look at while editing
```

Playwright comes from `tools/e2e/node_modules`, the browser from `CHROMIUM_PATH` (default
`/opt/pw-browsers/chromium`), and `ffmpeg`/`ffprobe` from PATH. Scratch frames are written to
`/tmp/demo/frames` (`TEASER_SCRATCH` to move them) and deleted after the encode, so nothing lands in the
repository.

`teaser.html` uses **no CSS animation, transition or requestAnimationFrame**: a screenshot between frames
would catch an element mid-flight and the export would stutter. Everything — transforms, opacity, dash
lengths, text — is set from `t` in `window.seek(t)`, so frame N is always the same picture. Keep it that
way when you edit.

Type is Outfit for display and Heebo for body, loaded from the local OFL copies in
`tools/brand/templates/fonts`, never from the network; the tokens are `DESIGN.md` §1. The phone beats put
the real captures from `tools/brand/templates/screens` inside the same CSS phone frame the story and store
templates use. The score ring, the breakdown rings, the tip card, the feed card, the fire burst and the
count are rebuilt as live elements so they move. The renderer fails loudly if a font or a capture does not
load.

Nothing in the cut claims a feature the app does not have: the score out of 10 for a chosen intent, the
three-part breakdown, one concrete tip, a private check until it is posted, fires and comments after.
The stylist judges the clothes. The end card says *coming soon* — there is no URL, because there is no
app to link to yet.
