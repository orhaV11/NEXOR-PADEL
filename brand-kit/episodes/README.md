# Episodes — the videos for TikTok and Instagram

An **episode** is one post: a look, a verdict, and fifteen seconds of vertical video. You never open a
video editor to make one. You write a small text file that says which photo and which verdict, you run
one command, and you get an `.mp4` ready to upload and a `.png` to use as the cover.

Every episode is **silent** and **wordless**. Nothing is spoken, in any language — every word is on the
screen. That is the point: the same file works for somebody scrolling in Tel Aviv, in São Paulo and in
Bangkok without changing a single frame.

There are four kinds:

| variant | how long | what it is |
|---|---|---|
| `verdict` | 15 s | The workhorse. One look, one score, the breakdown, the one tip |
| `versus` | 15 s | Two looks side by side. "Which one?" — built to start an argument in the comments |
| `board` | 18 s | The weekly post. Three looks counting down 3, 2, 1, the winner takes the medal |
| `overlay` | 15 s | **The green-screen one.** The same beats as `verdict`, but only the graphics, on a flat green field, so you can lay them over a clip you filmed yourself |

---

## 1. The command

From the top of the repository:

```bash
node tools/brand/render-episode.js brand-kit/episodes/001-camel.json
```

That writes two files next to the JSON:

```
brand-kit/episodes/001-camel.mp4          the video
brand-kit/episodes/001-camel-cover.png    the thumbnail
```

and prints the size and the length, so you can see it worked.

**How long it takes depends on your cores.** The command photographs the video one frame at a time — 450
of them for a fifteen-second episode — on up to three browser pages at once: one fewer than the machine
has cores, three at most (so three pages on four cores or more, one page on two; `EPISODE_WORKERS=3`
forces three), and it hands every frame straight to the encoder as it comes. On three pages a
fifteen-second episode takes about half a minute; on one page about a minute (measured on four cores:
19 s of capture and encode on three pages against 30 s on one, plus the browser's start-up either way).
It prints the page count before the first frame and its progress as it goes, so you can see it has not
stalled.

Other ways to run it:

```bash
# every episode in the folder, one after another, in file-name order
# (five episodes is about two minutes on three pages)
node tools/brand/render-episode.js --all brand-kit/episodes

# just the thumbnail, for a quick look while you are still writing the words
node tools/brand/render-episode.js 001-camel.json --cover-only

# a rough cut in about ten seconds — half size, 15 frames a second — as 001-camel-preview.mp4,
# to check the timing and the words before the real render. Never upload a preview.
node tools/brand/render-episode.js 001-camel.json --preview

# the green-screen version of a normal episode, as 001-camel-overlay.mp4
node tools/brand/render-episode.js 001-camel.json --overlay

# a few single seconds as still pictures, to check something without waiting for a video
node tools/brand/render-episode.js 001-camel.json --probe 0.5,3.9,9,13.5
```

You can write just the file name — `001-camel.json` — and it will find it in this folder.

**If something is wrong it stops and tells you.** It will not make a broken video. It stops when:

* the photo is not where the JSON says it is,
* a score is not a whole number from 0 to 10,
* a field it needs is missing,
* **a line of text is too long to fit.** This one matters most. Before it renders anything it measures
  every line in the browser at the exact size it will appear, and if the tip runs past the bottom of
  its card it tells you how much it overflowed and stops. Shorten the line and run it again.

---

## 2. What goes in the file

An episode file is plain text with curly brackets. This is a whole one — `001-camel.json`:

```json
{
  "variant": "verdict",
  "lang": "en",
  "photo": "../../tools/brand/templates/photos/look-2-camel.jpg",
  "intent": "DATE",
  "score": 9,
  "headline": "Camel and chocolate, done right",
  "occasion": "coffee and a walk",
  "breakdown": { "fit": 9, "color": 9, "accessories": 8 },
  "tip": "Swap the black tights for sheer brown and the column runs unbroken.",
  "credit": "@handle"
}
```

Field by field:

| field | needed? | what it is |
|---|---|---|
| `variant` | yes | `verdict`, `versus`, `board` or `overlay` |
| `lang` | no | `en` (the default) or `he`. Hebrew lays the whole video out right to left, in Heebo |
| `photo` | yes, except for `overlay` | Where the picture is, **counted from this file**. `../../tools/brand/templates/photos/…` reaches the three supplied looks |
| `focus` | no | Which part of a photo to keep if it has to be cropped: `{"x": 0.5, "y": 0.35}`, both between 0 and 1. `0.5, 0.5` (the middle) is the default; a smaller `y` keeps more of the top |
| `intent` | yes | The short word over the look: `DATE`, `STREETWEAR`, `PARTY`, `OFFICE`… |
| `score` | yes | A whole number, 0 to 10 |
| `headline` | yes | The stylist's one line. Two lines is the limit: about **55 characters** in English, about **40** in Hebrew |
| `occasion` | no | Where the look is going: *coffee and a walk*. One line, about **35 characters**. Shown under the headline |
| `breakdown` | yes | The three numbers: `{"fit": 9, "color": 9, "accessories": 8}`. All three, all 0 to 10 |
| `tip` | yes | The one tip. At least **115 characters** fit; it wraps by itself and fails loudly if it does not |
| `credit` | no | Who the look belongs to. Shows as a small line: *Look by @handle*. **Use it whenever the look is not yours** |
| `hook` | no | Replaces the opening line (*Score it before we do.*) |
| `ask` | no | Replaces the closing line (*What did you give it?*) |
| `cover_at` | no | Which second to take the thumbnail from. The default is the tip beat |

### `versus` takes two looks instead of one

```json
{
  "variant": "versus",
  "lang": "en",
  "photoA": { "photo": "…/look-1-streetwear.jpg", "label": "A", "intent": "STREETWEAR", "score": 8 },
  "photoB": { "photo": "…/look-3-pink.jpg",       "label": "B", "intent": "PARTY",      "score": 8 },
  "question": "Which one?",
  "questionSub": "A or B in the comments."
}
```

`label` and `intent` print under each photo. `question` and `questionSub` are optional — leave them out
and it uses *Which one?* and *A or B in the comments.* If the two scores are equal the video says
**IT'S A TIE** and *You decide.*, which is the better post anyway: the comments settle it.

### `board` takes three, **best first**

```json
{
  "variant": "board",
  "lang": "en",
  "looks": [
    { "photo": "…/look-2-camel.jpg",      "intent": "DATE",       "score": 9, "headline": "Camel and chocolate, done right" },
    { "photo": "…/look-3-pink.jpg",       "intent": "PARTY",      "score": 8, "headline": "Full pink, full commitment" },
    { "photo": "…/look-1-streetwear.jpg", "intent": "STREETWEAR", "score": 8, "headline": "All grey, and it holds" }
  ]
}
```

**The first one in the list is number 1.** The video plays them backwards — 3, then 2, then 1 — and the
medal goes to the first one you wrote. `headline` is optional here.

### `overlay` needs no photo at all

Exactly the fields of a `verdict` minus `photo`, because there is no picture in it — see `005-camel-overlay.json`.

---

## 3. Adding a new episode, in three steps

1. **Copy an old one.** `cp 001-camel.json 006-my-look.json`. The number at the front just keeps the
   folder in order; use the next free one.
2. **Change the words.** Point `photo` at the new picture, and retype `intent`, `score`, `headline`,
   `occasion`, `breakdown` and `tip`. Put a `credit` in if the look is somebody else's.
3. **Run it.** `node tools/brand/render-episode.js brand-kit/episodes/006-my-look.json`. If it complains
   that a line does not fit, shorten that line and run it again. When it says `ok`, the `.mp4` and the
   `-cover.png` are sitting next to the JSON.

Keep the commas and the quotation marks exactly where they are. If you delete a comma the command will
say *is not valid JSON* and name the line.

---

## 4. Checking it before you upload

Open the `.mp4` and watch it once, on a phone if you can, and look for four things:

1. **The first second.** The opening line has to be readable before you would have scrolled past it.
2. **The tip.** It should sit comfortably inside its card with air around it. If it looks crowded, it
   is too long — shorten it, do not squint at it.
3. **The bottom and the right.** TikTok and Instagram draw their own buttons over the bottom 320 pixels
   and the right 180 pixels of the screen. Everything that has to be read is already kept out of there,
   but if you ever move something, check it again.
4. **The numbers.** The score, the three breakdown numbers and the tip must be the ones the stylist
   actually gave that look. Never write a new number next to somebody else's clothes.

Then open the `-cover.png` — that is the frame people see before they press play, and it is taken
straight out of the finished video.

### Sound

**There is no sound, on purpose.** No music is licensed for these, and both TikTok and Instagram push a
video harder when the sound was picked inside their own app. So: upload the silent file, then pick a
track in TikTok or in Reels at the moment you post. Nothing in the video depends on audio — every word
is already on the screen.

---

## 5. The green-screen overlay, and how to use it in CapCut on a phone

`overlay` renders **only the graphics** — the intent pill, the 3-2-1 count, the ring and the score, the
breakdown, the tip card, the end card — sitting on a **flat green background**, low in the frame. Nothing
in the graphics is green, nothing is see-through and nothing casts a shadow, so the green lifts out
cleanly and leaves only the graphics.

The graphics sit in the **lower middle** — nothing is drawn above 880 pixels of the 1920, so a little
under the top half of the frame is empty green. That is where **you** are: film yourself, masked, head
and shoulders in the upper half, and the verdict lands under your hands.

### Step by step, on the phone

1. **Film your clip first.** Hold the phone upright. Keep your head and shoulders in the **top half** of
   the screen — the graphics will fill the bottom half. Fifteen seconds or a little more.
2. **Render the overlay** on the computer and send yourself the file:
   `node tools/brand/render-episode.js brand-kit/episodes/005-camel-overlay.json`
   → `005-camel-overlay.mp4`. Save it to the phone's photos.
3. **Open CapCut → New project** and pick **your own clip** (not the overlay). It becomes the main track.
4. **Add the overlay on top.** Tap **Overlay** → **Add overlay** → choose `005-camel-overlay.mp4`.
   A second, smaller track appears under the timeline.
5. **Make it fill the screen.** With the overlay selected, pinch it out on the preview until its edges
   reach the edges of the frame. It is the same shape as your clip, so it lines up exactly.
6. **Key the green out.** With the overlay still selected, scroll along the bottom toolbar and look
   for **Chroma key**. In most versions of CapCut it sits inside **Cutout**, so tap **Cutout** first
   and then **Chroma key**.
7. **Pick the colour.** A small circle appears on the preview. Drag it onto a **plain green area** —
   anywhere in the empty top half is perfect — and let go.
8. **Intensity.** Slide it up until the green is completely gone and you can see your own clip through
   it. Stop as soon as the green disappears; pushing further starts eating the white letters.
9. **Shadow** (the second slider). Nudge it up a little only if a thin green rim is left around the
   edges of the cards. A small amount is enough; too much makes the letters look chewed.
10. **Line the two up.** Drag the overlay along the timeline so the score lands where you want it in
    your performance — for example, so your hand gesture happens exactly when the ring fills.
11. **Export.** Top right → **Export**, 1080p, 30fps. Upload that file, and pick your sound inside
    TikTok or Reels.

If the green will not lift cleanly, the usual cause is that the overlay was not scaled to fill the
frame, so CapCut is sampling a colour from the black bars instead of from the green.

---

## 6. What is in this folder

| file | what | the video |
|---|---|---|
| `001-camel.json` → `001-camel.mp4`, `001-camel-cover.png` | `verdict`, English, the camel coat | 15.00 s · 1.39 MB |
| `002-streetwear-he.json` → `002-streetwear-he.mp4`, `…-cover.png` | `verdict`, **Hebrew**, right to left, the grey streetwear | 15.00 s · 1.29 MB |
| `003-streetwear-vs-pink.json` → `003-streetwear-vs-pink.mp4`, `…-cover.png` | `versus`, grey streetwear against the pink suit — a tie | 15.00 s · 1.14 MB |
| `004-week-board.json` → `004-week-board.mp4`, `…-cover.png` | `board`, all three looks | 18.00 s · 2.46 MB |
| `005-camel-overlay.json` → `005-camel-overlay.mp4`, `…-cover.png` | `overlay`, the green-screen version of 001 | 15.00 s · 0.23 MB |

Every video is 1080×1920, h264, yuv420p, 30 fps, **no audio track at all**, and well under 8 MB. The
green in `005` measures rgb(0, 176, 64) at every point of the empty field, at every second of the cut —
one flat value, which is what makes the key clean.

**`week-1/`** is the first week of posts, rendered and ready: the board, the camel verdict, the
camel verdict as the chroma overlay for Wednesday's duet, streetwear against pink, and the Hebrew
streetwear verdict for the Israeli track, each with its cover, plus a `README.md` in Hebrew that says
which file goes up on which day, what to film, how to key the overlay in CapCut, and the first line
of every caption. The JSONs in it point at the same three photographs one folder further up
(`../../../tools/brand/templates/photos/…`).

**The crops.** Every box an episode puts a look in is 9:16, the shape of the frame, so a full-length
photograph keeps its head and its shoes instead of being squared off at one end. `look-2-camel`
(941×1672) and `look-3-pink` (700×1244) are 9:16 to the pixel and are shown whole. `look-1-streetwear`
is 2:3, so 864 of its 1024 columns are used: the full height stays — white cap down to the chunky
sneakers — and only empty concrete comes off the sides. Nothing in these five loses a garment. The
command prints what each crop kept, every run, so you will see it if a new photograph does.

**The end card** is the wordmark, the slogan, *coming soon*, 16+ and the question — the same end card
the launch teaser in `brand-kit/teaser/brand` uses, so two OREVOSH videos end the same way. The mark is
in it: it is the first glyph of the wordmark.

The three photographs are the owner's, used with permission, and live in
`tools/brand/templates/photos/`. The scores, headlines, occasions and tips beside them are the stylist
copy written for those exact outfits — no episode invents a number or a compliment for clothes it is
not looking at, and **no line anywhere is about a body, a face or an age.** The stylist judges the
clothes. That rule is what keeps the account safe while it duets strangers.

**Where the looks come from.** Other people's outfits arrive through **Duet** and **Stitch** on TikTok
and **Remix** on Instagram — the platforms' own features, which the original creator switched on, which
credit them automatically and often send the duet to their audience too. Nothing is downloaded and
re-uploaded. If someone has Duet and Stitch turned off, that is their answer, and you move on.

---

## 7. For whoever maintains this

The renderer is `tools/brand/render-episode.js`; the page it photographs is
`tools/brand/templates/episode.html` with `tools/brand/templates/episode.css`. The type is Outfit and
Heebo from the local OFL copies in `tools/brand/templates/fonts`, the colours are the tokens of
`DESIGN.md` §1, and **nothing touches the network** — the fonts and the photographs are local files, so
a render works with the cable pulled out.

`episode.html` has **no CSS animation, no transition and no requestAnimationFrame**, and must never get
one. The exporter takes a screenshot between frames and would catch anything the browser animated on
its own half-way through, and the video would stutter. Every position, opacity, dash length and glyph
is worked out from one number, `t`, inside `window.seek(t)`, so frame N is the same picture on every
run.

**The chroma rule.** On the green field nothing is ever half-way transparent, and nothing casts a soft
shadow. A white numeral at 50 % over `#00B140` is a pale *green* numeral, and the key either eats it or
leaves a ghost; a soft shadow survives the key as a dark halo. So in overlay mode every fade is turned
into a cut (`fade()` rounds the opacity to 0 or 1) and the motion is carried by position and scale
instead, every fill is opaque, and `--card-shadow` is `none`. Keep it that way, and keep `--ok`
(`#5ee6a0`, the one green-ish brand token) out of episodes entirely.

**How the frames get out.** Every frame is a DevTools `Page.captureScreenshot` straight off the
compositor's surface (what `page.screenshot` does underneath, minus its per-call preparation), and
goes down a pipe into ffmpeg (`image2pipe`), which encodes while the browser is already on the next
frame; nothing is written to disk on the way. One capture costs about 65 ms whatever the format or
the size — the time is the compositor handing the surface over, not the encoding — so the renderer
opens **up to three pages in separate contexts** (separate renderer processes: one fewer than the
machine has cores and three at most, so a two-core machine gets one page; `EPISODE_WORKERS` overrides
the count, and the count is printed before the first frame) and each takes every K-th frame, with a
writer putting them back in order. Before it trusts the pool it photographs frame 0 on every page and
demands the bytes match; if they ever do not, it says so and renders on one page.

**A failure leaves nothing behind.** The video is encoded as `<name>.mp4.part` and the cover as
`<name>-cover.png.part`, and they take their final names only after ffmpeg has exited 0 and the file
has been probed (size, codec, length), so a render that fails half-way never leaves a short, playable
`.mp4` under the final name and never replaces the previous good one; the `.part` is removed on the way
out. `ffmpeg` and `ffprobe` are asked for before the browser is launched, so a machine without them
gets one readable line instead of a crash mid-render.

Over a photograph a frame is a **JPEG at quality 95**; on the chroma field it stays a **PNG**. The old
pipeline wrote a 1080×1920 PNG per frame and Chromium's zlib pass over a photograph cost 0.55 s each:
`001-camel` took 242 s to capture and 255 s in all. Now: 21 s (3 pages, JPEG); the overlay went from
43 s to 16 s (3 pages, PNG); a `--preview` of the camel is 10 s. The JPEG was checked before it was
kept: the raw JPEG frame sits 52–57 dB PSNR (SSIM 0.996–0.999) from the raw PNG of the same frame,
which is far inside x264's own loss at crf 20 (43–47 dB), and the same three frames from the two
finished files — the ring at 3.2 s, the tip card at 8.6 s, the end card's lilac glow at 13.5 s — were
put side by side at 1:1: no banding on the gradient, none on the look, and nothing the eye can tell
apart. The green field is PNG so it stays exactly rgb(0, 176, 64) in every frame, as before.

`--preview` is the same walk at 15 fps, the frames halved to 540×960 by ffmpeg on the way in, JPEG at
80 and x264 `ultrafast` at crf 26, written as `<name>-preview.mp4` and no cover. It is for the timing
and the words, never for uploading.

Useful switches while editing: `--probe 0,3.9,9` writes single seconds as PNGs into the scratch folder;
`--debug-safe` draws the platforms' unsafe rectangles over a probe or a cover so you can see what
intrudes; `--keep-frames` also writes every frame into the scratch folder (`.jpg` or `.png` as
rendered); `--crf 18` overrides the encoder quality (the default is 20, and 16 for an overlay, because
chroma keying is unforgiving of the noise h264 leaves around a hard edge).

Scratch frames go to `EPISODE_SCRATCH` (by default a folder in the system temp directory), never into
the repository. Playwright comes from `tools/e2e/node_modules`, the browser from `CHROMIUM_PATH`
(default `/opt/pw-browsers/chromium`), and `ffmpeg`/`ffprobe` from `PATH`.
