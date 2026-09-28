# OREVOSH — Design system: Ring of Fire

**The logo is the system.** The mark is one bold ring (the O of OREVOSH) with a single lick of fire breaking out of it:
the frame you step into, the score that lands inside it, and the moment a look catches fire. Sources:
`src/FitCheck.Api/wwwroot/brand/mark.svg`, `mark-light.svg` (deeper pair for light grounds), `wordmark.svg`,
`wordmark-light.svg`, and the concept notes in `brand/CONCEPT.md`. Everything on screen is built from the same three
ideas: the ring, the lick of fire, and the lilac-to-rose gradient that runs across the ring.

**The feel:** young, chic, lit. Dark stage, one warm gradient, fire only where something caught fire, rounded and
friendly shapes (the lettering is monoline rounded), big confident type, short punchy motion. It keeps what made the
previous system ours: the floating text dock with the check control raised in the middle, the Explore front page with a
hero and a numbered index, the profile statline, hashtag challenges as a quiet side feature. It drops the gallery mood:
no brass, no grain, no serif, no frames and mats.

## 1. Tokens

(Round 21) The stage is warm plum now. The values in this table and in the ground, shape and shadow lines under it are
the current ones; the old values, the new tokens (glass, amber, mint, the scrim, the ring track, the fire ink, one pastel
per occasion) and every component that changed are in **Round 21 — the look** at the end of this page, which wins
wherever an older section gives a different number.

| token | value | role |
|---|---|---|
| `--bg` | `#14101e` | the stage: a warm plum-black |
| `--bg-2` | `#1b1527` | top of the page gradient, masthead, sticky feed head, composer |
| `--surface` | `#211a2e` | solid surfaces: the dock, sheets, the ring's inner disc (cards are glass since Round 21) |
| `--surface-2` | `#2e2541` | raised: chips at rest, action pills, fields, the brands band, placeholders, pressed rows |
| `--line` | `#3b3150` | hairlines |
| `--line-soft` | `rgba(255,255,255,.09)` | quieter hairlines (action outlines, index rows) |
| `--ink` | `#f9f5ff` | text |
| `--ink-2` | `#d1c8e0` | body, captions |
| `--ink-3` | `#a99ebd` | labels, meta (7.4:1 on the stage, 5.7:1 on `--surface-2`) |
| `--accent` | `#b39dff` | lilac: links, #tags, @mentions, active marks, ranks 1–3 (a pressed chip is the gradient since Round 21) |
| `--accent-2` | `#ff8fb1` | rose: the warm end of the gradient, brand marks |
| `--accent-ink` | `#150f2e` | ink on the gradient |
| `--grad` | `linear-gradient(135deg, #b39dff 0%, #ff8fb1 100%)` | **the only gradient**: primary buttons, the score ring, pressed chips and the secondary button's outline (Round 21), the follow label, brand portrait rings, the sheet's top edge, the active dock label's dot |
| `--accent-tint` | `rgba(179,157,255,.16)` | active dock block, unread rows, selected states |
| `--fire` | `#ff6a2b` | **reactions only**: a lit reaction's edge, the burst, the flame in the mark, the "weak" verdict dot, the medals; fire as a word on its own wash (a lit count, the weak verdict word) is `--fire-ink` `#ff8a5a` since Round 21 |
| `--fire-2` | `#ffa83a` | the flame's tip |
| `--fire-tint` | `rgba(255,106,43,.14)` | fill of a pressed fire action |
| `--ok` | `#6ff0ad` | "works" dot, the keep row, success |
| `--danger` | `#ff5d7a` | report, delete, errors |

Ground (Round 21): `html { background: var(--bg) }`, and one fixed `body::before` layer that paints the aurora at the
top of every screen (`--glow`: lilac at the top-start, rose at the top-end, a breath of amber between) and a faint rose
glow at its foot (`--glow-2`); a second fixed layer breathes on opacity. No grain, no blur except the masthead (`--bg-2`
at 78% + `blur(12px)`, opaque where the engine has no backdrop-filter).

Shape: rounded everywhere. `--radius: 22px` (cards, tickets, the hero figure), `--radius-sm: 14px` (photos inside grids,
inputs, the stamp of the app icon), `--pill: 999px` (chips, segments, buttons, the dock, the follow label, product links,
the score ring). Sheets 24px on top. Avatars are circles.

Spacing: 4 · 8 · 12 · 16 · 20 · 24 · 32. Gutter 16, cards 14 from the edge with 20 between, sections 24 apart, 12 inside.
Touch targets ≥ 44px (dock blocks 56, the mark 60, chips 40 in 44 rows, actions 42 in a 50 row, index rows 46, follow 44).

Shadows (Round 21: lit from above, lifted on plum rather than black): card `inset 0 1px 0 rgba(255,255,255,.07), 0 14px
36px rgba(8,4,20,.55)`; dock `inset 0 1px 0 rgba(255,255,255,.08), 0 18px 44px rgba(8,4,20,.65)`; the mark's halo, warmed
to rose, `0 0 0 7px rgba(179,157,255,.14), 0 12px 30px rgba(255,143,177,.4)`; primary button `inset 0 1px 0
rgba(255,255,255,.35), 0 10px 28px rgba(255,143,177,.32)`.

## 2. Type

Self-hosted (Round 21): `wwwroot/fonts/fonts.css` declares Outfit (500–800), Heebo and Cairo (400–800) from ten variable
woff2 subset files on this origin, and `index.html` preloads the three the first paint draws. There is no Google Fonts link
anywhere any more; the Round 21 section says why.

| role | stack |
|---|---|
| `--font-display` — h1, card headline, hero headline, profile name, segments, every numeral (score, counts, stats, ranks, unread) | `"Outfit", "Heebo", system-ui, sans-serif` — Outfit 700/800; Hebrew falls to Heebo 800 |
| `--font-body` — UI, body, captions, buttons, chips, meta | `"Heebo", system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans Hebrew", sans-serif` |
| labels (`h2.rule` list heads, dock, tags, stat labels, tabs, action labels) | Heebo 700 11px uppercase, tracking .08em, `--ink-3`; `[dir=rtl]` 12.5px tracking .02em. (Round 21: a plain `h2` is a real heading, 700 17px display face in `--ink`, Heebo 800 18px in Hebrew) |

Scale: wordmark 22px tall in the masthead (the SVG, never text) · h1 36/1.02 (Explore, Activity, states; the result
headline 38, 40 in Hebrew, Round 21) · profile name 28/1 · card headline 22/1.15 (Round 21) · hero headline 22/1.12 ·
segments 24/1 · action count 16 · caption Heebo 15/1.5 `--ink-2` · meta 12.5 · score ring numeral (Round 21) 25 (card) /
20 (Explore hero, wall) / 16 (grid) / 82 on the result's own ring · statline numerals 22 ·
colophon 13 · index rank 14 tabular · dock labels Heebo 700 11.5 caps (Hebrew 12.5) · CHECK 10.5.
Numerals are `direction: ltr` everywhere. No italics (Heebo has no Hebrew italic).

## 3. The mark in the interface

- **Masthead**: the wordmark SVG (`brand/wordmark.svg`) inlined in `index.html` as `<a class="wordmark" href="#/">` with
  `aria-label="OREVOSH"`, 22px tall, at the start edge, `direction: ltr` in both languages (it is a logo). The globe and
  the Join pill stay at the end.
- **The check control** (`.tab.check .mark` in the dock): the mark SVG (`brand/mark.svg`, inlined in `index.html`) 60×60
  on a `--bg` disc (`border-radius: 50%; background: var(--bg); padding: 6px`) with the halo shadow, raised 24px above the
  dock's top edge (`position: absolute; inset-block-start: -24px; inset-inline-start: 50%; margin-inline-start: -30px`).
  The word CHECK sits at the dock's bottom edge. **Tap**: the ring draws itself clockwise (`stroke-dasharray`/`dashoffset`
  on the circle, 450ms ease-out) and the flame pops (scale .6→1.08→1, 260ms, transform-origin at its base) — add class
  `lit` on pointerdown, remove on animationend; `prefers-reduced-motion` skips it. **While a check runs** (the loading
  screen): the same mark at 96px with the flame breathing (scale .92–1.06, 900ms loop) replaces the pulse dot.
- **The score ring** (`.score-badge`, keeps its class and its "7/10" text): a 44px circle, 3px gradient ring
  (`background: var(--grad)` with a `--surface` inner disc via a pseudo-element or `border: 3px solid transparent;
  background: linear-gradient(var(--surface), var(--surface)) padding-box, var(--grad) border-box`), the numeral Outfit 800
  22 `--ink`, "/10" Heebo 700 8 `--ink-3` under it; sits at the photo's bottom-end corner straddling the edge
  (`inset-block-end: -14px; inset-inline-end: 14px`). Grid: 30px / 15 numeral; hero and wall: 36px / 18.
- **Fire**: the reaction icon is the mark's lick (`ICONS.flame`); pressed = `--fire` icon, count and label with `--fire-tint`
  fill and a `--fire` outline; the double-tap burst is the flame at 110px scaling .4→1.1 and fading (as today, `--fire`).
- **App icons / favicon**: the mark on a `#0b0b0f` rounded tile (192, 512, apple-touch 180 full-bleed, maskable 512 with
  the mark at 66%); `favicon.svg` = `mark.svg`. Regenerate with the Playwright script from the SVG.

## 4. Navigation — the pill dock

`nav.tabbar`: fixed, `inset-inline: 14px; inset-block-end: calc(10px + var(--safe-b))`, 56px tall, `--surface`, opaque,
`border: 1px solid var(--line)`, `border-radius: var(--pill)`, dock shadow, max width `calc(var(--col) - 28px)` centred.
`.tabbar-inner`: `grid-template-columns: 1fr 1fr 80px 1fr 1fr`. Four **text destinations** (no icons): Heebo 700 11.5px
caps `--ink-3`, full-height tap blocks. Active: label `--ink` and a 6px gradient dot centred 6px under the label
(`::after`). Pressed: `--accent-tint` pill behind the label (`inset: 8px 4px`). The Activity tab shows the unread count as
an Outfit 700 13 numeral in `--fire` after the label (`#activity-badge`, static, no pill, `direction: ltr`). The check
column holds the raised mark (§3) and the word CHECK. `--tabbar: 92px` (56 + 10 lift + 26 overhang); toast at
`calc(var(--tabbar) + var(--safe-b) + 12px)`. RTL: the grid mirrors; the mark does not (a logo keeps its flame top-right).

## 5. The look card

`article.card`: `--surface`, radius 18, `margin: 0 14px 20px`, `padding: 10px 10px 6px`, card shadow.
- **Head** (`padding: 4px 4px 12px`): avatar 36 (circle, 2px `--line` ring; brands get a 2px gradient ring), name Heebo 700
  15 + BRAND mark (9px caps, `--accent-2` text, 1px `--accent-2` 60% outline, pill), `@handle · 9 min ago` 12.5 `--ink-3`,
  the intent as a **label pill** (11px caps `--accent`, `--accent-tint` fill, pill, no border), the `…` button 44×44.
- **Photo** (`a.card-photo`): radius 14, `overflow: visible` for the ring, no frame; `img` 4:5 cover, radius inherit.
- **Score ring** at the bottom-end corner (§3).
- **Body** (`padding: 22px 4px 6px`): headline Outfit 700 21 `--ink`; caption Heebo 15 `--ink-2`, tags/mentions `--accent`
  600; **Featured by NEXOR** as a gradient-outlined pill (11px caps, sparkle 12px, 1px gradient border via the padding-box
  trick, `--ink`); READS AS DATE AT 72% as caps 11 `--ink-3` + a 3px gradient bar on a `--surface-2` track (pill);
  challenge marker caps 11 `--accent`; product links as outlined pills (36px, bag icon, price `--accent`).
- **Actions** (hairline above, `padding: 8px 0 2px`, row 50): each a **42px outlined pill** (1px `--line-soft`,
  transparent): fire = icon 19 + Outfit 700 16 count + `.lbl` caps 11 `--ink-3` "FIRE"; comments = icon + count + "COMMENTS"/
  "COMMENT"; save and share 42×42 icon pills; pressed fire = `--fire` everything on `--fire-tint`; saved = `--accent` icon
  and border. Votes tag at the end as a gradient pill.
Grid prints (`.grid a`): radius 12, gap 8, `padding-inline: 14px`, 30px score ring at bottom-end −8/6.

## 6. Explore

Same structure as before, re-skinned: **front** (kicker THIS WEEK caps `--accent`, h1 Outfit 800 36, dateline Heebo 13
`--ink-3`, a `--line` rule); **search** as a pill input (48px, `--surface-2`, icon `--accent` at the start, no border,
focus = 2px `--accent` ring); **hero** (58%/1fr, figure radius 16 with a 36px score ring, kicker THIS WEEK'S LOOK
`--accent`, headline Outfit 700 22, name + intent label pill, READ THE LOOK → in `--accent` caps pinned to the bottom, the
arrow flipped in RTL); **trending index** (`ol.index`: 46px rows, `--line-soft` hairlines, rank Outfit 700 14 tabular
`--accent` for 1–3 else `--ink-3`, tag Heebo 600 16 `--ink`, dotted leader `--line`, count 13 `--ink-3`); **brands band**
(full-bleed `--surface-2`, brand cards 128 wide: a 128×160 portrait with radius 14 and a 3px gradient ring, name Outfit
700 18, followers caps `--ink-3`, follow pill 36); **the wall** (`grid.wall`: 2 columns, gap 24×14, second column dropped
32px, figures radius 16 with 36px rings, captions: rank Outfit 700 13 `--accent` + name Outfit 700 16 + intent caps 10.5);
**hashtag challenges** (outlined tickets radius 18, the hashtag first in `--accent` Outfit 700 15, title Outfit 700 20,
meta caps 10.5). Section heads: caps 11 `--ink-3` with a `--line-soft` hairline to the end of the line.

## 7. Profile

Portrait 84 circle (3px gradient ring for brands, 2px `--line` for people); name Outfit 800 28 + BRAND mark; `@handle`
12.5 `--ink-3`; **the follow label** under the handle: 44px pill, `min-inline-size: 150px`, gradient fill + `--accent-ink`
FOLLOW when inviting, outlined `--line` + `--accent` check + FOLLOWING when following; bio + website in `.profile-bio`
(Heebo 15 `--ink-2`, website `--accent`, physical text-align pin as today); **statline** between two `--line` rules:
numerals Outfit 700 22 `--ink` (fire in `--fire`), labels caps 10.5, separators 5px gradient dots; **colophon** Heebo 13
`--ink-3` with `--ink-2` numerals; own-profile index list `.links` (52px rows, Heebo 600 16, `--accent` icons at the end);
tabs caps 11 start-aligned, active `--ink` with a 3px gradient underline; grid per §5.

## 8. Controls

| component | look |
|---|---|
| `.btn` | 52px pill, `--grad` fill, `--accent-ink`, Heebo 700 15, primary shadow; pressed `scale(.985)`; disabled: `--surface-2` fill, `--ink-3` text, 1px dashed `--line`, no shadow |
| `.btn-secondary` | transparent, 1px `--line`, `--ink` |
| `.btn-ghost` | text `--ink-2`, no border |
| `.btn-danger` | transparent, 1px `--danger`, `--danger` |
| `.btn-sm` / follow | 36px pill (44 on the profile), caps 11 |
| `.btn-text` | `--accent`, underlined, 500 |
| `.pill` | 36px outlined `--line`; `.pill.accent` gradient fill |
| `.chip` | 40px pill, `--surface-2`, Heebo 500 14 `--ink-2`; pressed: `--accent` fill, `--accent-ink`, 700; disabled 35% |
| `.segments` / `.segment` | the feed switch: two coins named after the halves of the mark, the flame (For you) and the ring (Your circle), Outfit 700 17 with a 22px glyph. The current one is **lit**: a 44px gradient pill with the word in `--accent-ink`, the flame in `--fire`, the ring in ink, and a rose glow (`0 8px 22px rgba(255,143,177,.22)`); the other is **cool**: no container, word `--ink-2`, glyph `--ink-3`. No underline, no sliding thumb; on a switch the newly lit coin fades its fill in (220ms) and pops its glyph (`flame-pop`). The challenge list reuses the same pills without glyphs |
| inputs | 50px, `--surface-2`, radius 12, no border, focus 2px `--accent` ring; textarea 96 |
| `.switch` | pill track, `--grad` when on |
| `.sheet` | `--surface`, 24px top radius, a 3px `--grad` top edge, 36×4 `--line` handle, title Outfit 700 20, rows 52px with `--accent` icons |
| `.toast` | `--ink` pill on light, as today |
| skeletons | `--surface` → `--surface-2` shimmer, radius 12 |
| `.install` | `--surface` radius 18, the mark 44px as the icon |
| alerts | 1px `--accent` (info) / `--danger` (error) radius 12 |

## 8b. Round 7 surfaces: the camera, clips, the story card, moderation

- **The camera** (`views/camera.js`, `#/camera`): the viewfinder fills the screen with the masthead and the dock hidden,
  and shows **the whole frame the shutter keeps** — `object-fit: contain` on black, letterboxed the way a phone's own
  camera app shows a frame that is not the screen's shape, so the live view, the dashed guide and the preview all mean the
  same rectangle. Chrome is three 44px discs on translucent dark (close at the top start, timer and flip at the top end),
  a dashed 4:7 framing guide with the hint under it (fades after 3s, back on tap) — a miniature of the 9:16 frame, kept
  inside the painted rectangle and simply not drawn where a frame leaves no room for one (a landscape webcam), since the
  hint is the instruction —
  two mode pills (Photo / Clip) and **the shutter, which is the ring**: 76px, the gradient stroke, a white disc inside.
  Tap = photo (a 120ms white flash). In Clip mode a tap starts a hands-free clip and a press-and-hold rolls while it is
  held (the hold is clip-mode only: the photo stream carries no microphone to record with), and the disc becomes a red
  rounded square while the ring draws itself clockwise toward the 30-second cap, with a "Recording · Ns" pill and a
  blinking red dot at the top. The countdown is the display face at 168px. The preview after a capture is the same
  honest `object-fit: contain`, with Retake (secondary) and Use it (gradient).
- **Clips in cards** (`core.js` media helpers, `app.css`): a `<video>` in the same 4:5 box as a photo, poster = the picked
  frame, muted autoplay when 60% visible (never more than two at once), a `.clip-pill` (the clip glyph + CLIP, caps
  label style) at the top start corner of cards and grid tiles, and a 32px sound disc at the top end corner of cards
  (44px hit area, icon `mute`/`sound`). Double-tap still fires; the sound disc stops propagation.
- **The frame picker** (`views/check.js`): the clip paused in the photo box, one range slider under it in `--accent`,
  "Clip · Ns" as a tag with the clip glyph, and a text button to remove the clip.
- **The story card** (`app/sharecard.js`): 1080×1920, the stage with the lilac glow, the photo cover-fitted in a 936×1170
  block with 48px corners, the score ring at r=160 straddling the photo's bottom-end corner (numeral Outfit 800 160,
  "/10" under it), the headline Outfit 700 64 on two lines max, the intent as a caps pill, name + @handle, a hairline,
  the wordmark 52px tall at the start edge and "Checked on OREVOSH" at the end edge. Mirrored for RTL, and every
  wrapped block (the headline, the before/after change, the film's tip) is drawn with ONE base direction — the
  paragraph's, not the line's, the way `unicode-bidi: plaintext` reads it, since a canvas has no bidi of its own.
  Shown in a sheet with Share (when files can be shared) and Save image — named "Download to Files" on an iPhone, where
  a blob download lands in Files and never in Photos, and left out altogether inside the installed app, where Share can
  take the file and there is no download manager to show a download for.
- **Moderation** (`views/admin.js`): the queue as compact look cards (or the comment text with its author row), a meta line
  with the report count, the reasons as small chips, Hidden/Suspended tags, and an action row of outlined pills; Delete
  and Suspend confirm in a danger sheet, and a moderator's own account row and another moderator's carry no Suspend (the
  server refuses it). Reached from Settings, only for `isAdmin`.
- **The guidelines** (`views/pages.js`): h1, the intro, five numbered rules with bold titles, "What we keep", a version
  line. Linked from the agreement line under the signup button, with the terms and the privacy policy (§8c).

## 8c. Round 9 surfaces: guests, Pro, "Which one?", insights, Today, after the tip, the numbers, the legal pages

- **The guest banner and the cap line** (`views/check.js`): signed out, a `.notice` sits above the check form with a
  display-face title 20/800 ("Try it first"), a muted line and a 44px text button to join; nothing is walled. A guest's
  result shows a gradient button "Sign up to keep it and post it" (`#guest-keep`) where Post it would be, and Post it
  takes its place once the claim has run. Signed in, the cap line (`#checks-left`, hint voice) sits under the submit
  button with the private note: "2 of 3 checks left today", and "Go Pro for more" as a text button when none are left.
  The date field on signup (`.auth-dob`) is dressed like the text fields: `--surface-2`, radius 12, ltr numerals at the
  end edge in Hebrew, a dark picker.
- **"Which one?"** (`views/compare.js`, `#/compare`, linked as a text button under the check button): the intent chips
  and the occasion as on the check, then two 4:5 slots side by side (`.cmp-slots`: dashed `--line` on `--surface`, radius
  18, the letter A or B in a 28px translucent disc at the top start in Outfit 800 14, an image glyph in `--accent`, the
  outfit's name Outfit 700 18, a hint);
  with a photo in, the slot is the photo with a gradient "Replace" pill along its bottom. The verdict (`#/compare/<id>`):
  two figures with 44px score rings, the winner's frame a 3px gradient border (`.cmp-frame`, padding-box on `--grad`) with
  the rose glow and a WINNER caps pill at the top end, each side's name in caps (`--accent` for the winner) and its
  headline Outfit 700 17; "A wins" Outfit 800 32; the reason 16/1.55 `--ink-2`; the one tip as the same pull quote on a
  gradient bar (`.tip`); "Compare two more" as a ghost button. While the stylist looks, the breathing mark at 96px.
- **Insights** (`views/insights.js`, `#/insights`, a sparkle link on the own profile): three tiles (`--surface`, radius
  18, card shadow, 116 tall, caps label at the bottom): the count in Outfit 800 30, the average and the best score as
  58px score rings in flow; the sentences as rows with a 6px gradient dot and `--line-soft` hairlines between; a footer
  hint "From your last N checks" and a gradient "Check another look". Under three checks: the empty state with "N of 3
  checked so far".
- **The Pro page** (`views/pro.js`, `#/pro`, from Settings' plan row and the cap line): the mark at 68 on a `--bg` disc
  with the halo, h1 34 "The stylist, every day.", a lede; three benefit rows (a 44px `--accent-tint` disc with a lilac
  glyph: ring, flip, sparkle; title 17/700; hint 14 `--ink-2`) between hairlines; the price in Outfit 800 32 with
  "Billed monthly" under it; then either the gradient "Go Pro" with "Payment runs on Stripe" (billing live), a `.notice`
  saying Pro is switched on by hand (manual), or the PRO pill (`.tag.accent.pro-badge`) with "You're on Pro until…".
  A `role=status` alert thanks on return from Checkout.
- **The Today strip and page** (`views/today.js`): the strip is a card in the feed's rhythm (`--surface`, radius 18,
  `margin: 0 14px 20px`) under the chips on For you: kicker TODAY · date in caps `--accent`, the title Outfit 700 21
  (a link to `#/today`), the hint 14 `--ink-2`, the hashtag as an accent tag and "You're in" at the end, a scroll row
  of up to eight 72×90 prints with 26px rings, then "Post yours" (a 36px pill in a 44px row) and "All of today's looks"
  as a text link. The page is a front (title Outfit 800 28, hint 16, the tag and the intent), the CTA block with the
  entering hint, the count, and the grid.
- **After the tip** (`core.js` card, `app/after.js`): on the card, between the headline and the caption, a 44px strip
  (`.after-strip`): a 44px thumbnail of the earlier look and "After the tip · 6 → 7" in 13/600 `--ink-3`, `--ok` when
  the score went up, the whole strip a link to the earlier look. In the post sheet, under the caption: the label "After
  the tip", a scroll row of radio options, "Not a follow-up" first (a 120-wide `--surface-2` pill, `--accent-tint` when
  picked) then up to five 72×90 thumbnails with 28px rings, the picked one on a 2px `--accent` ring; a preview line
  with the two scores (`--ok` when up). Hidden until the looks arrive, gone when there are none.
- **The numbers page** (`views/dashboard.js`, `#/admin/metrics`, from the moderation page): the return rate as the hero
  (`--surface` card, caps label, Outfit 800 48, a sub line), stat tiles (`auto-fill minmax(140px)`, radius 12, Outfit
  700 24 with a small unit), the score distribution as a bar list of divs: rows of 22px with the score in tabular Outfit
  at the start, a 14px bar in **one hue** (`--accent`) with a 4px rounded data end on a `--line` baseline, the count at
  the tip, zero rows in `--ink-3`; the rubric averages and the community as tiles; two key → count lists in 44px rows;
  a footer with "as of" and a secondary Refresh pill. No chart library, nothing else drawn.
- **The legal pages** (`views/legal.js`, `#/terms`, `#/privacy`): the guidelines' shape: h1, a lede, ten numbered
  sections with the number in `--accent` Outfit 800 26 at the start, a 17/700 title and 15/1.55 `--ink-2` body,
  `--line-soft` hairlines; a footer with "Version 1 · date" and text links to the other page, the guidelines and back.
- **The verified brand mark** (`core.js` `brandMark`): the same BRAND pill (9px caps, `--accent-2` text and outline) with
  a 10px check glyph (stroke 3.5) after the word, inside the pill, in the mark's rose; 12px on the profile head; titled
  "Verified brand" for hover and screen readers. Only `--verify` puts it there.

## 8d. Round 10 surfaces: the pieces on a look, the weekly flames board

- **The item editor** (`app/items.js`, `#items-editor`: on the post sheet under the "after the tip" picker, and in the
  owner's "Edit items" sheet over the look's rows): the still at 220 wide in the 4:5 box (`#items-photo`, radius 12 on
  `--surface-2`, a crosshair cursor, a 2px `--accent` ring while a row waits for its tap, the hint "Tap the photo on
  running shoes…" under it), the dots over it, and one 52px row per piece (`li.items-row[data-source=Stylist|User]`):
  a 26px numbered ring (2px `--ink-3`; the gradient with `--accent-ink` once the piece has a dot), name · brand · model
  in 15 with a rose sparkle titled "Named by the stylist" on the stylist's rows, the "Place the dot" pill (caps 11;
  `--accent` text and outline once placed, `--accent-tint` while placing) and a 44px remove. A stylist brand guess is a
  line under the row head, "Looks like Nike?" in 600 with three 36px chips, Confirm / Edit / Not a brand; nothing goes
  out as a brand until one is tapped. The open row's fields are 46px inputs (name, the seven category chips, the brand
  with a listbox of the server's brands as 44px rows carrying the look count in 12.5 `--ink-3` and the BRAND mark,
  the model, the store link with its hint); "Add an item" is a 40px pill with the tag glyph in `--accent` and "Up to
  12 items on a look." beside it, disabled at twelve.
- **The dots** (`.item-dot`, on the editor's preview and on the look page): a 44px hit area around a 22px ring (2px
  `--ink` on a 72% `--bg` disc with a soft shadow) holding the piece's number in the display face at 11; the active one
  takes the gradient. Placed by physical `left`/`top` percentages of the 4:5 box, because a photo does not mirror in
  Hebrew; a drag moves one, the arrow keys nudge a focused one by 0.02, Enter on the photo places it at the centre.
- **The look page** (`views/post.js`): the dots sit over the photo behind **the tag toggle** (`#items-toggle`), a 44px
  target at the photo's physical bottom-left holding a 32px translucent pill with the tag glyph at 18 and the count in
  the display face at 13, `--accent` while the dots show; the score ring keeps the bottom-right in both directions. It
  appears only when a piece has a dot. Cards carry the same corner as a 22px `.item-count` pill (the glyph at 12, the
  count in Outfit 700 11) when a look has items; prints stay the ring alone. Under the card, **"The look"**
  (`#look-items`, a post section with the h2 and, for the owner, "Edit items" as a text button): the pieces as 52px rows
  with the same 26px ring, name · brand · model (the first letter capitalised by CSS; the server keeps names
  lower-case), the bag glyph in `--accent` when a store link waits, `--line-soft` hairlines between. A row or a dot
  opens **the item sheet** (`#item-sheet`): a caps-label / value list (`dl.item-kv`: the brand as an `--accent` link to
  its looks, the model, the category), "Named by the stylist" as a hint on a stylist row, the gradient `.btn` "Shop at
  nike.com" with the bag glyph (`#item-shop`, a new tab through the out door), centred under it the honest line
  "Leaves OREVOSH · This link may earn OREVOSH a commission." (`#item-leaves`) whenever a link exists, then "More looks
  with Nike" as a full-width text button.
- **The item pages** (`views/items.js`: `#/items/<brand>`, `#/items/<brand>/<category>`, `#/items?q=`): the top bar
  titled "Looks with Nike · Shoes" (the server's spelling), the Explore search rule as the first block (`#items-search`,
  submitting to `#/items?q=`), on a brand page the category chips as a scroll row with "All" first (the pressed one
  `--accent` on `--accent-ink`, `#items-cats`), the brand's own account as a 52px row between hairlines with its avatar,
  "More looks with Nike", the BRAND mark and a forward arrow (`#items-more`), then the print grid of §5 paging as you
  scroll (`#items-grid`) and the empty state "No looks with this yet." spanning its columns.
- **The board** (`views/board.js`, `#/board`, under Explore): the five boards as the feed's coins (§8), Looks / People /
  Rising / By intent / Stylist's picks, wrapping onto a second row (five never fit one phone row, and a hidden coin would
  be a hidden board). The head: WEEK OF … in caps `--accent`, "Hall of flame" with the trophy glyph at the end, "Closes
  in 2 days 5 hours" in Outfit 800 26 recomputed every minute (two units at most, in the locale's own words; "This week
  is closed" on an archive week), the sponsor as an outlined block (radius 12: PRESENTED BY in caps `--ink-3` with the
  name in `--accent-2`, the prize line in 15, the site's host with the link glyph), the rules line, and Previous /
  Next week as 40px pills. The reader's own place is a fire pill (`#board-me`: 44px, `--fire` on `--fire-tint`, Outfit
  700 16, the flame): "You're #2", "You finished #2 this week" once closed. On By intent, the feed's intent chips
  (`#board-intents`). Each place is a row (`.board-row[data-rank]`): **the medal** (`.rank-medal`, a 36px disc in
  Outfit 800 16, tabular, `direction: ltr`; the first three burn on the flame gradient, `--fire` → `--fire-2`, with
  ink `#1a0a04` and an orange glow, the rest sit on `--surface-2`), the fires that counted in `--fire` display 15 with
  the flame, "Score 9/10" as a tag on picks, then the compact look card of §5 or, on People, the person row with the
  looks posted and the fires at the end in 12.5 `--ink-3`. The empty and error states centre with the hall link under
  them.
- **The hall** (`#/board/hall`, `#hall`): one section per closed week, "Week of …" as the h2, its top three looks as
  three 4:5 prints (radius 12) with a 28px medal at the top-start corner (`.hall-tile`), the name in Outfit 700 14 and
  the fires under each; a look that is gone is a `--surface-2` tile saying so (`.hall-gone`); the week's top person as a
  row with the medal (`.hall-person`).
- **The Explore strip** (`#board-strip`, right after the search rule): a section head "This week's board" with "See the
  board" at the end, then the top three of the looks board as the same three prints with their medals and the fires
  under them; absent while the board is empty.
- **The reset card** (`#board-reset`, on For you above the Today strip, the first 24 hours of a week): a card in the
  feed's rhythm (`--surface`, radius 18, `margin: 0 14px 20px`, the card shadow), the flame at 20 in `--fire` and "The
  board reset. A new week starts now." in Outfit 700 18, two text links ("See the board", "See last week's winners"),
  and a close (`#board-reset-dismiss`) that remembers the dismissal for the week in `localStorage`.
- **The profile badge** (`#profile-badge`, inside the name next to the brand mark, and on Me): a 24px pill on the flame
  gradient with ink `#1a0a04`, the flame glyph at 12 and "#1 · LOOKS" in Heebo 700 11 caps, a link to the hall, titled
  "Finished #1 on Looks last week"; worn for the week after, then gone.
- Fire stays a reaction's colour: the medals and the badge burn because a place on the board *is* fire that counted.
  No motion was added for any of it (§9 stands as written).

## 8e. Round 12 surfaces: the shared video, the empty call

**The shared video** is the story card brought to life: 1080×1920, 12 seconds, silent, drawn on a canvas as a pure
function of time and encoded on the phone (WebCodecs), never on the server. Five scenes: the look full bleed with
the intent pill and a small wordmark (0–1.5 s); the ring drawing in the gradient while the number counts up and lands
with /10 (1.5–3.5); Fit · Color · Accessories snapping in as rows with gradient bars (3.5–6); the one tip on its card
with the accent bar, the ring shrunk to a badge on its top-end corner — the longest hold, because it is the product
(6–10); the end card with the mark drawn live, the wordmark, "Check the look." in the check's language, the @handle
when signed in and the site's host when the server publishes one (10–12). The content column is x 72–876 and nothing
readable sits below y 1576 or in the right 180 px: that is where TikTok and Instagram put their own controls. Type and
colour come from the same helpers the story card uses, so the two never drift. Hebrew and Arabic draw right-to-left;
the tip wraps by measurement down a size ladder rather than ever overflowing. The button is a full-width secondary
action on the result screen, busy while it renders, with a progress sheet (bar, percentage, cancel) and then the
video with Share (the phone's share sheet, when it accepts files) or Save.

**The empty call** replaces the blank walls a newcomer met on launch day. Home, Your circle, Explore, the board and a
fresh profile each keep the kit's empty line and add one gradient pill that names the single thing to do next — check a
look — and, on Your circle, a second, quieter one to find people and brands. One shared helper draws it so the five
read as one object; the pill is 52 px, never a small link.

## 9. Motion

Ring draw + flame pop on the check control; flame breathing on the loading screen; the score count-up on the result;
the double-tap burst; segment underline slides (transform, 200ms); sheets rise
220ms; the camera's ring draws while a clip records and its red dot blinks; the feed switch coin fades its fill in and pops
its glyph; everything off under `prefers-reduced-motion`. (Round 21 replaced the 120ms `scale(.985)` press with a 2px dip
on every control and added the rise-in entrances, the aurora's breath, the result ring's draw-on and overshoot, the lit
flame's glow and a look's settle under a press; "Motion and reduced motion" in the Round 21 section is the list now.)

## 10. Where each rule lives

`app.css` (the tokens and every shared surface, §1–§9 order), `index.html` (the font preloads and the link to
`fonts/fonts.css`, inlined wordmark and mark SVGs, the dock markup), `fonts/` (Round 21: the faces themselves, on this
origin), `app/core.js` (the score ring markup inside `.score-badge` and its `--score`, the piece chips, the `lit` class
on the check control, the flame burst), `app/views/check.js` (the loading mark, and since Round 21 the result's four
moments), `app/views/explore.js` and `profile.js` (structure unchanged from the previous system), `icons/` and
`manifest.webmanifest` (regenerated from `brand/mark.svg`).

**A screen's own CSS is not in `app.css`.** Anything only one view draws ships as a string inside that view's module
and is appended once, on first render, through its own `ensureStyle()` — twenty-four modules do it, from
`views/dashboard.js` and `views/board.js` to `views/wardrobe.js` and `app/taste.js`. `app.css` holds the tokens and what more than
one screen wears; a view that puts its own rules there makes every other screen pay for them on first paint, and
makes two builders share a file they do not share a feature in. The rule for a new surface: tokens from §1, shapes
from §8, and the rules themselves next to the code that draws them.

## Round 14 — the two questions, and the keep (appended)

The check screen asks twice now. **Where is it going?** is a row of six chips, one always pressed, and nothing can be
checked until one is. **Your style** is the row under it, starting with *No style* — a chip like any other, because
asking for nothing is an answer and should not look like a blank. Under those, one quiet line appears only when the
style pressed is not the one saved (*This check only.* · **Make this my style**), so the preference is visible without
ever being a settings trip. Same 40px chips, same pressed lilac, no new shapes.

On the result, `#asked-for` sits under the vibe: two tags, the occasion first, the style (or *No style*, outlined in
rose) after it — the reader should never have to remember what the score was measured against.

**A keep is the one place the result screen changes colour.** A tip that says *change nothing* gets its own heading
(*What to keep*), a *Change nothing* pill outlined in `--ok`, and the pull-quote's gradient bar swapped for the same
green — the colour the item list already uses for a piece that works. Nothing else about the block moves, and no swap
or replace verb goes anywhere near it.

Since Round 20 the result draws `#taste-reasons` and `#tried-action` under the tip and `taste.js`'s `mountResult` fills
them (the typed reasons, then the primary *Try the tip, then show me*, or the pair once the two checks are linked);
`#tip-feedback` and `#tried-it` are gone, and `#wardrobe-offer` stays beside the item list. `.mount:empty { display:
none }` keeps an unfilled one from leaving a gap.
## 11. Round 14 — the community round

**A look with no number.** Where the score ring sat there is now nothing: no blank ring, no dash, no grey circle. The
photo is simply a photo (`scoreBadge()` returns null), the "reads as … at 72%" row is not drawn, and the breakdown
section is not there. The one thing a reader gets instead is a single 13px line under the card, with the shield glyph
in lilac: "The score on this look is the author's alone." The author's own look carries the switch in its place — the
same glyph, the state in bold, the consequence in the meta grey, and a 44px outlined button at the end edge ("Keep it
to myself" / "Show the score").

**The openers.** A horizontally scrolling row of 44px chips between the comments and the sticky composer, in the meta
grey rather than the accent: they are an offer, not a call to action. A tap fills the box and puts the caret at the
end; the row does not move or close, so a second tap changes the opener and nothing is lost but the typing.

**The before/after card and film.** 1080×1920, the same stage, gradient, faces and colophon as the single look's. The
two photos sit side by side in 4:5 boxes with BEFORE and AFTER in caps under them and the gradient arrow between; the
before is on the start edge, so the pair reads the way the language does. What changed sits under them on the tip
card's surface with the lilac bar. The rings appear only if the person asked for them. The film is ten seconds — each
look full bleed with its pill, then the two together with the change, then the end card the twelve-second film already
ends on.

**A challenge's rule.** One inset block under the title, the check glyph in lilac at the start edge, "THE RULE" in
caps above the sentence, and a plain "Rule" tag beside the intent on the byline so a list reads at a glance. The
brand's form offers the three examples as chips that fill the field in, because a rule somebody edits is better than
one they pick.

## 12. Round 14 — the loop and the wardrobe, as surfaces (appended)

The three Round 14 screens the sections above never described. All of them draw their own rules through their own
`ensureStyle()`, and all of them use §1's tokens and §8's shapes and add no new ones.

- **The typed reasons** (`app/taste.js` `reasonRow`, drawn by `views/profile.js` on **`#/checks`** and, since Round 20, on the result
  screen in `#taste-reasons` through `mountResult`; the Round 13 yes/no `.useful` row is gone from the result, because
  the four answers already decide the server's yes). One `.loop` card —
  `--surface`, `var(--radius)`, the card shadow — with a 17px display heading and
  four chips below it in a two-up wrap, each one at least 44px and at least 46% of the row, so the four read as a
  square of answers rather than a line to scroll. *It worked* / *It didn't* / *Not my style* / *I don't own that*,
  and a quiet text skip below at the start edge. Once answered, the card collapses to one line of what was said
  with a text button to change it. The optional note is a row that appears only after a reason is chosen: the
  person's words are never the first thing asked for.
- **"I tried it"** (`app/taste.js` `triedBlock`, under the reasons on `#/checks` and, since Round 20, in `#tried-action` on the
  result screen, where `#tried-start` is the primary `btn` — *Try the tip, then show me* — a keep tip draws no block,
  and once a pair exists `#tried-pair` takes its place and `#share-pair` leads the share row). One 44px button while nothing is linked; a
  waiting line with a cancel while the second photo is owed; and, once both verdicts exist, the pair — the two
  scores, the two tips, and what changed — with the preference question under it. The second check went through the
  ordinary check screen, so nothing here is a second camera.
- **The taste card** (`app/taste.js`, mounted by `views/settings.js`): what the app has learned in a handful of
  lines the person would recognise, **the literal paragraph the stylist is sent** below them, then the learning
  switch and the clear. Nothing on this card is a secret from its subject; that is the whole design.
- **Your wardrobe** (`views/wardrobe.js`, `#/wardrobe`): a lede saying what it is for, the *Send these to the
  stylist* switch as a `--surface` card with a 44px native checkbox in `--accent`, then a plain hairline-separated
  list. Each row is a 34px `--surface-2` disc with the category glyph, the piece's name at 600/16 with
  `unicode-bidi: plaintext` (the names are the person's own words, in any script), and a meta line of how many looks
  it has been in; rename and remove sit behind the row's own sheet. There is **no photo per piece and no grid** —
  the wardrobe is a list of names, because a name is all the stylist needs and photographing a closet is the hour of
  work that would kill it. A free account on a server where the advice is Pro's sees the whole list and one plain
  line about what Pro adds, never a dead toggle.
- **The keep line** (`app/wardrobe.js`, `#wardrobe-keep`, appended by `views/check.js` right under the tip): one
  question for a piece this check named, and its answers — *Keep* (`#wardrobe-keep-yes`), a quiet *Not this one*
  (`#wardrobe-keep-skip`) and, while two or more pieces are left, a quiet *Keep all {n}* (`#wardrobe-keep-all`) that
  keeps exactly the pieces its number counts; the answers wrap on a narrow phone. One tap, no form, no category to
  pick: the category is the stylist's own word for it. A keep turns the row into one line — the piece is in your
  wardrobe, or after *Keep all* how many pieces from this look are — with *See it* (`#wardrobe-kept-link`), and the
  next piece is asked a moment later. Under the keep that ends the row, and only there, the Pro moment may sit: one
  sentence and Go Pro in a `.notice` (`#wardrobe-keep-moment`, `#wardrobe-keep-moment-go`), once per tab; a keep
  with a question still to come never draws it, since the question would take it off the screen. The row stays
  `hidden` until it has something to ask, so a screen with nothing to offer looks exactly as it did.
  (`#wardrobe-offer`, beside the item list, is a second mount `check.js` reserves and nothing fills.)
- **Keep from an older look** (`views/wardrobe.js`, `#wardrobe-unkept`): on `#/wardrobe`, after the list (before the
  "Check a look" button on an empty wardrobe), an `h2.rule`, one hint line saying how many looks it read, and the
  pieces those looks named that are not kept — each row the look's 44px photo, the name and its category with when
  it was worn, and a *Keep* (`.wardrobe-unkept-keep`). The screen's own Pro moment (`#wardrobe-moment`) takes the
  plain Pro notice's place for one paint, once per tab.

## Round 21 — the look (appended)

**The direction.** Warm depth, with a playful joy grafted on. The stage is a plum-black lit by an aurora — lilac at the
top-start, rose at the top-end, a breath of amber between, a faint rose glow at the foot — that breathes slowly. A look
card is glass with a 1px light edge, and a card that knows its occasion sits in that occasion's glow. The score ring
reads as a meter: its arc fills to the score, and on the result the numeral is the first thing read. The stylist's pieces
sit on the photo as name chips, each with a dot for the verdict: works (filled mint), neutral (hollow), weak (filled
fire) — on a posted look, for its author (everyone else reads the names alone). Each occasion has one pastel, used as a tint and never as a frame. The tip is the one warm panel on the result,
with the primary *Try the tip, then show me* inside it. Two more moments of colour earn their place: the keep row in
mint, Tomorrow's weather pill in amber. Empty states open under the mark, large and lit. Anything that is a button dips
2px when pressed, and a screen's first content rises in. What came over from the playful direction is its joy — the big
numeral, the chips, the pastels, the press, the mark on an empty screen — and not its grammar: no 2px ink outlines, no
hard shadows, no cream ground.

**Tokens, old → new** (`app.css` §1 as it is now).

| token | before | Round 21 | note |
|---|---|---|---|
| `--bg` | `#0b0b0f` | `#14101e` | warm plum-black, one step up from black so daylight never reads it as black |
| `--bg-2` | `#13121a` | `#1b1527` | the masthead, the sticky feed head, the composer |
| `--surface` | `#15151c` | `#211a2e` | solid surfaces only: the dock, sheets, the ring's inner disc |
| `--surface-2` | `#1e1e27` | `#2e2541` | chips at rest, action pills, fields, placeholders |
| `--line` | `#2b2b36` | `#3b3150` | |
| `--line-soft` | `rgba(255,255,255,.08)` | `rgba(255,255,255,.09)` | |
| `--ink` | `#f4f4f7` | `#f9f5ff` | a hair of lilac |
| `--ink-2` | `#b9b9c6` | `#d1c8e0` | 11.6:1 on the stage, 9.0:1 on `--surface-2` |
| `--ink-3` | `#7f7f8e` | `#a99ebd` | 7.4:1 on the stage, 6.6:1 on `--surface`, 5.7:1 on `--surface-2` |
| `--accent-tint` | `rgba(179,157,255,.14)` | `rgba(179,157,255,.16)` | |
| `--fire-tint` | `rgba(255,106,43,.12)` | `rgba(255,106,43,.14)` | |
| `--fire-ink` | — | `#ff8a5a` | new: fire as a word on its own wash (below) |
| `--ok` | `#5ee6a0` | `#6ff0ad` | the works dot, the keep row |
| `--ok-tint` | — | `rgba(111,240,173,.13)` | new: the keep row's ground, the works chip's wash |
| `--amber` / `--amber-tint` | — | `#ffb46b` / `rgba(255,180,107,.13)` | new: the aurora's warm breath, the weather pill, the Today kicker; a tint, never body text |
| `--glass` / `--glass-edge` / `--glass-hi` | — | `rgba(255,255,255,.055)` / `rgba(255,255,255,.1)` / `inset 0 1px 0 rgba(255,255,255,.09)` | new: a translucent card over the aurora (a flat fill, no backdrop-filter), its 1px edge, its top light |
| `--scrim` | — | `rgba(20,16,30,.64)` | new: a photo's vignette in the stage colour, not black |
| `--ring-track` | — | `rgba(255,255,255,.14)` | new: the unfilled rest of a meter |
| `--radius` / `--radius-sm` | 18 / 12 | 22 / 14 | |
| `--shadow-card` | `0 10px 30px rgba(0,0,0,.35)` | `inset 0 1px 0 rgba(255,255,255,.07), 0 14px 36px rgba(8,4,20,.55)` | lit from above, lifted on plum |
| `--shadow-dock` | `0 16px 40px rgba(0,0,0,.55)` | `inset 0 1px 0 rgba(255,255,255,.08), 0 18px 44px rgba(8,4,20,.65)` | |
| `--shadow-halo` | lilac `6px` ring + lilac lift | `0 0 0 7px rgba(179,157,255,.14), 0 12px 30px rgba(255,143,177,.4)` | the mark's halo, warmed to rose |
| `--shadow-primary` | `0 8px 24px rgba(255,143,177,.25)` | `inset 0 1px 0 rgba(255,255,255,.35), 0 10px 28px rgba(255,143,177,.32)` | |
| `--glow` | one lilac radial at the top centre | three radials: lilac at 18% (.30), rose at 84% (.26), amber at 52% (.14) | the aurora |
| `--glow-2` | — | `radial-gradient(70% 26% at 50% 108%, rgba(255,143,177,.1), transparent 60%)` | new: the stage's warm foot |
| `--tint-<occasion>` (and `-glow`, `-wash`) | — | the table below | new |
| `--tint` / `--tint-glow` / `--tint-wash` | — | `var(--accent)` / `rgba(179,157,255,.38)` / `var(--accent-tint)` | new: the cascade trio an element reads; lilac when no occasion is known |

Unchanged: `--accent` `#b39dff`, `--accent-2` `#ff8fb1`, `--accent-ink` `#150f2e`, `--grad`, `--fire` `#ff6a2b`,
`--fire-2`, `--danger`, both font stacks, `--caps`, `--pill`, `--tabbar`, `--col`, `color-scheme: dark`.

**One pastel per occasion.** Pastel on dark, each in three forms — a solid (a pressed chip's fill, a tag's text, the
Tomorrow kicker's pill), a glow (a coloured shadow behind a card or a photo) and a wash (a faint fill under a chip at
rest) — so no rule needs `color-mix()`.

| occasion | solid | glow | wash |
|---|---|---|---|
| Date | rose `#ffb1c8` | .42 | .12 |
| Office | sky `#a6c6ff` | .42 | .12 |
| Streetwear | lemon `#ffe082` | .34 | .11 |
| Casual and Everyday | mint `#8ef0c0` | .34 | .11 |
| Party | lilac `#d4b7ff` | .42 | .12 |
| Formal | ivory `#f2e8d2` | .30 | .10 |
| Sport | aqua `#7fe4ec` | .34 | .11 |
| OldMoney | sand `#e6cfa6` | .32 | .11 |

One map in §1 sets the trio from `data-occasion` (a chip on the check and on Tomorrow, the `#asked-for` tag and the card
head's tag, `#result`, `article.card`, `#tm-card`, the Tomorrow kicker's occasion) and from `data-intent` (the feed's
filter chips, the check's occasion and style chips). The keys are the app's own strings, matched exactly; any other
word — Minimal, Classic, the feed's All, No style — keeps the lilac fallback. Every pastel carries `--accent-ink` at
10.5:1 or better, and as tag text on its own wash reads 6.3–9.0:1.

**The components, by `app.css` section.**

- **Ground.** `html` is flat `--bg`. `body::before` is still the one fixed layer and paints `--glow, --glow-2` (a fixed
  layer, not `background-attachment: fixed`, which mobile Safari ignores); `body::after` adds an amber and a rose radial
  that fade in and out on opacity over nine seconds. `.sticky-tabs` repaints the same two images by hand, as before.
- **The label voice and §2.** A plain `h2` is now a heading: 700 17px in the display face, `--ink`. The caps and the
  hairline moved to `h2.rule`, for a head over a list of rows (Explore's sections, the board's strip and hall, the look's
  pieces and comments, the settings, admin and dashboard groups); a head that titles a card or a moment (the breakdown,
  the one tip, the check's two questions) is a plain `h2`. In Hebrew, h1, h3, the headlines, the tip's words, an empty
  state's title, the board's countdown, the Explore hero title, the Today title, the feed coins and the compare verdict
  take Heebo 800 with no tracking; an `h2` is Heebo 800 at 18px, the result headline 40px, the card headline 22px. Arabic
  headings take line-height 1.12 (Cairo's tall ascenders); Russian headlines lose the tight tracking. h1 tracks
  −0.02em, h3 −0.01em.
- **§3 the masthead.** `--bg-2` at 78% behind the 12px blur (still the app's only blur), a `--line-soft` foot, and an
  opaque `--bg-2` where the engine has no backdrop-filter. The wordmark is untouched.
- **§4 the dock.** The same geometry. Opaque `--surface` with a `--glass-edge` border and `--shadow-dock`, and a sheen
  across it — lilac to rose to amber at about a tenth — under the tabs. The mark sits on a `--bg` disc with the rose halo
  and a light along its top edge.
- **§8 controls.** `.btn` is the gradient with `--shadow-primary`. `.btn-secondary` is a 1px gradient outline at 70% over
  `--surface` (the `.featured` padding-box trick), lit along its top; pressed, the full gradient edge on `--surface-2`.
  `.chip` rests on `--surface-2` with a hairline and a faint top light; pressed, it is **the gradient** with `--accent-ink`
  and a rose glow (it was flat lilac). An occasion chip carries a 7px swatch of its pastel on a wash of it and, pressed,
  fills with the solid pastel, the swatch turning to ink. `.tag[data-occasion]` is the pastel as text on its wash; `.tag.rose`
  keeps its outline. Fields are raised (a top light and a hairline, which the 2px focus ring replaces); `.pill`,
  `.switch`, `.notice` and the install banner are glass. **The press:** `.btn, .chip, .action, .segment, .pill, .vote`
  dip 2px in 90ms (`translate: 0 2px` — the `translate` property, not `transform`, so it composes with a chip's `rotate`),
  and the primary's shadow shortens while it is down. It replaces the old `scale(.985)`.
- **§5 the look card.** Glass (`--glass`, a 1px `--glass-edge`, radius 22, no padding, `--shadow-card`).
  `.card[data-occasion]` trades the plain lift for its pastel's glow — `0 22px 60px var(--tint-glow), 0 2px 18px
  var(--tint-wash)`: rose behind a Date look, lemon behind Streetwear — a shadow, never a frame. The head and body carry
  14px inline padding, the avatar is 38px, the headline 22px. The photo runs edge to edge between them under a vignette in
  the stage colour (`--scrim` up from the foot, a faint one down from the top), so a dark selfie keeps a tonal foot and the
  chips have something to sit on. The actions are raised 42px pills on `--surface-2`; a lit fire is `--fire-tint` with a
  `--fire` edge, its glyph and count in `--fire-ink`, and a glow under it that breathes on opacity every three seconds; a
  saved look is `--accent-tint`. A look answers a press by settling to 99% (a transform on the picture alone).
- **Sheets, the toast, skeletons.** The sheet keeps its gradient top edge over `--surface` and gains a `--glass-edge`
  border and a plum shadow; the backdrop is a plum veil, not black; the toast gains a shadow; skeleton cards are glass.
- **Empty states.** `.empty` in `--ink-2` 16/1.5 under a 26px 800 title, and **`.empty-mark`**: a 136px clone of the
  mark (`logoMark(136)`) in a rose-lilac radial with a rose drop shadow, the flame lit (dimmed, it read as mud on the
  plum). `board.js` `emptyCall()` puts it on Home, Explore, the board and a fresh profile, and `tomorrow.js` on its three
  empty states; the empty call sits tighter under it so the way on is in the first viewport.
- **Over a photo.** The clip pill, the sound disc, the compare letter, the grid's private tag, the item dots, the tag
  toggle and the count moved from `rgba(11, 11, 15, …)` to the stage colour `rgba(20, 16, 30, …)` at the same alphas.

**The meter.** Every ring — the card's `.score-badge`, the grid's and the wall's, the breakdown's three, the checks
list's `.num` and the result's `.hero` — is one element: a solid disc in the padding box (`--surface` on a card, `--bg`
on the hero) and, in the border box, `conic-gradient(from 210deg, #b39dff 0deg, #ff8fb1 var(--arc), var(--ring-track) 0)`
with `--arc: calc(var(--score) * 36deg)`. The arc starts at the bottom-left and runs clockwise: a 7 is 252°, the gap sits
at the foot like a gauge's, a 10 closes the ring. `--score` is an inline style set by `core.js` `scoreStyle()` — clamped
to 0–10, and left off when the value is not a number, which leaves the ring full — from `scoreBadge()`, `breakdownRow()`,
the result's hero and `profile.js`'s checks list. A conic does not mirror in Hebrew and neither does a numeral: the ring
is `direction: ltr` and keeps the photo's bottom-right corner in both languages, as it always has. Sizes: a card's ring
48px with a 4px arc, the numeral at 25 and `/10` at 7, straddling the photo's bottom-right with a 4px stage collar and a
rose shadow; grid 32/16; the wall and the Explore hero 40/20; the breakdown 60/28, static; the checks list 44/20. **The
hero** is 156px with an 8px arc over `--bg` and the numeral at 82px 800 with a rose glow, `/10` at 12px under it — the
number is the first thing read — inside a 10px stage collar, a 1px light ring, the rose halo and a lilac bloom. It draws
its arc on over 900ms, which is why `--arc` is registered with `@property` (an animatable angle), and `.landed` —
added by `animateScore()` on the count-up's last frame, at once when nothing counts up — plays a 200ms 5% overshoot.
**The fallbacks:** an engine without `@property` (Safari before 16.4, Firefox before 128) cannot tween the angle, so the
draw-on jumps from nothing to the final arc halfway through its 900ms; the final ring is right everywhere. The public look
page keeps its own server-drawn SVG ring (recoloured to the stage, and since the review a meter too: the arc starts at
the bottom-left and leaves the gap at the foot), and the share card and film still draw the full gradient ring.

**The pieces chips, and where their verdicts come from.** `core.js` `pieceChip(item)` draws
`span.chip.piece[data-verdict] > span.dot.{works|neutral|weak} + span.item-name`; a piece with anything but the stylist's
three words, or none at all, is `span.chip.piece > span.item-name` with no dot and no `data-verdict`, because a hollow dot
would say "neutral", a verdict nobody gave (the result's own pieces always have one: `check.js` fills the neutral in). The shared rule is `app.css` §8: a 34px dark-glass pill (`rgba(22, 16, 34, .72)`, a
light edge, Heebo 600 13px in `--ink`: 12:1 over a mid-grey photo, 8.2:1 over a light wall), a `::before` that makes
the hit area 44px, an 8px dot — works filled mint with a mint glow, neutral a hollow `--ink-2` ring, weak filled fire with
a fire glow — and the name as the part that ellipsises, its first letter capitalised (the server keeps names lower-case).
**On a card** (`postCard()`) up to four sit at the photo's bottom-left, 32px, straight and quiet, `aria-hidden` like the
count they replace (the link names the look, the look page lists the pieces); the count is drawn only when nothing names
them. On the look page the tag toggle takes that corner once a piece has a dot, and the chips step aside for it.
**The data:** the feed used to carry only `itemCount`. `PostItemDto` gained `Verdict` — `works`, `neutral` or `weak` —
as its last, optional field. `PostReader` reads the stored feedback of the checks behind the looks whose verdicts this
viewer may read (one query over the page, skipped when there are none), keys the stylist's pieces by the name exactly as a look's row stores it
(`PostItems.NormalizeName`), and gives each row the verdict for its name — a row the person retyped keeps its piece's
verdict; a name the check never gave (a piece the person added in their own words) has none, and the field is left off
the wire. `PATCH /api/posts/{id}/items` answers with the same verdicts. **Who reads them** (settled in the review): the
look's author and a moderator in the queue, the people who may read the check; anyone else gets no verdict, public
number or private, and their card shows the names with no dots. The stylist's note on a piece never travels with the
look.

**The result in four moments** (`views/check.js`, in its own CSS). `#result` carries `data-occasion`, so its tag, its
chips and its glow take the occasion's pastel; every id stays inside `#result`.
1. **`.result-verdict`**: the judged still (`figure.result-photo`) edge to edge under the masthead, 4:5, its bottom
   corners rounded 28px so it sits on the stage as an object, under a stage-coloured scrim; up to four piece chips pinned
   at its bottom-left with a static tilt (−2°, 1.5°, −1°), the hero on its bottom-right corner, and behind it the
   occasion's glow, a radial in `--tint-glow` that the headline (38px, 40 in Hebrew), the vibe (17px, `--ink-2`) and
   `#asked-for` sit in. A past check with no still keeps the ring in flow above the headline.
2. **`.result-read`**: one glass panel for `#breakdown`, `#accessories` and the reads-as bar. The accessories' "add one"
   is a quiet lilac block with no bar, so the tip stays the one warm moment.
3. **`.result-pieces`**: the pieces again, as 40px chips on `--surface-2` with the verdict word beside the name (the works
   chip on a mint wash, the weak one on a fire wash with its word in `--fire-ink`); a chip whose piece has a note is a
   button, and the note opens under the row in `p.piece-note` (the first weak piece with a note starts open). Then what
   works, `#taste-win`, and **the tip as the warmest panel**: glass tinted lilac to rose to amber (17%, 15%, 17%), a 1px
   light edge, the card shadow and a rose bloom, the gradient bar inside it glowing 10px in from the start edge, the words
   at 23px, and `#tried-action` — the primary *Try the tip, then show me* with its hint, the waiting line, or the pair —
   inside `#tip`. The panel's rules name `#tip` itself, so the pair's two sides keep their own tips as the plain pull
   quote on its bar, not two more warm panels inside this one. `#taste-reasons` follows as its own glass panel, then
   `#wardrobe-keep`.
4. **`.result-doors`**: Post it (`#post-open`) and *Posted · See the look* (`#post-link`) are secondary now, `#guest-keep`
   stays the primary (it is a guest's only door), then the share row and Check another.

**The keep row** (`app/wardrobe.js`): the one green surface on the result — a mint wash over glass with a mint edge, the
bag on a mint disc with a soft glow — because keep is the works colour. The KEEP button stays the gradient.

**Tomorrow** (`views/tomorrow.js`). The day pills are 44px; the occasion row scrolls edge to edge under them with no
heading of its own (the group keeps its name for a screen reader, and the lede is read, not seen); the style row and the
forecast fold behind one 44px *More* line (`details.tm-more`, the words of `common.more` with the picked style as a hint
beside them), open by default only while there is an outfit to compose and the place is still a question, and left as the
person set it on a repaint. The closet strip is prints in the gradient ring, 68px in an 80px tile. The outfit is a glass
card lit in its occasion's glow (`#tm-card[data-occasion]`), its caps kicker holding the occasion as a solid pastel pill.
When every piece came from one check the photo is drawn once, large (`.tm-big`: up to 168px, 4:5, in its tint's glow with
its own vignette), beside `.tm-side`: a caps hint and the pieces as 40px name chips with the kind in `--ink-3`, one
`ul.pieces.chips` of `li[data-item]` inside `#tm-one-look`. The sentence is 18px (600 in Hebrew). The forecast is drawn
once, on the card, as **the amber pill** (`#weather-pill`: `--amber-tint`, an amber edge, a 10px sun that glows).

**Home's Today strip** (`views/today.js`). An amber-to-rose-to-lilac wash over glass with a 1px light edge and an amber
bloom under the card shadow; the kicker in `--amber` on one line; a 54×68 print of the first look posted to the prompt
(`.today-print`, tilted −4° like a photo left on a table, hidden from a screen reader, which has the title's link), first
in the head. The words keep at least 200px, so on a 390px phone the hashtag and *You're in* drop under them instead of a
title wrapping around a pill. The Post pill stays the gradient.

**The share card and the film** (`app/sharecard.js`, `app/sharevideo.js`) never read the stylesheet, so the palette is
copied into them by hand, `drawStage()` paints the same aurora from the same numbers for both, and every dark overlay in
the film is the stage colour at its old alpha. The chrome outside the page followed too: `theme-color`, the manifest's
colours, the offline page and the server-rendered public pages' own tokens.

**The fonts.** Outfit, Heebo and Cairo are served from this origin: `wwwroot/fonts/fonts.css` and ten woff2 files, which
are every subset Google served for the three families — Outfit latin and latin-ext; Heebo hebrew, latin, latin-ext, math
and symbols; Cairo arabic, latin and latin-ext — each a variable font covering its family's whole weight range, so there
is one `@font-face` per family and subset, with `font-display: swap` and a `unicode-range` so the browser fetches only
the subsets the text on screen needs (about 220 KB in all; an English screen fetches about 75 KB of it). `index.html`
preloads the three the first paint draws — Outfit latin, Heebo hebrew, Heebo latin — and links `fonts/fonts.css` before
`app.css`; the landing pages link `../fonts/fonts.css`. **The Google Fonts link was removed, not kept as a fallback:**
Chrome matches a single-weight `@font-face` declared by Google's sheet over a local face whose range covers the same
weight, so with both on the page the local files were never loaded. The service worker (shell `v8`) precaches
`/fonts/fonts.css` and keeps each woff2 the first time it is fetched, so an installed app draws its own type offline.
`fonts.js`, which flipped Google's sheet from print to all, is gone, and the security policy allows no font host:
`style-src 'self' 'unsafe-inline'`, `font-src 'self'`.

**Motion and reduced motion.** One keyframe, `rise-in` (from opacity 0 and 8px down, 320ms): a list's first page
(`core.js` `infiniteList()` puts `.enter` on it; the first six cards 80ms apart; a page appended on scroll simply
appears), a notice with its screen, the result's four moments (0, 80, 140, 200ms), the Tomorrow card when a new outfit
lands (`.tm-card.arrive`; a repaint of the same one, after a thumbs answer or the forecast, leaves it still) and the
Today strip when it arrives from the network into a Home that has none (`.today-strip.arrive`; drawn from its cache on
Back from a look, or refreshed over the one on screen, it is simply there, as the restored list is).
Then the hero's arc and its overshoot, the aurora's breath, the lit flame's glow, the 2px press and a look's settle under
a press. All of it is opacity and transform, and nothing waits on its end. Under `prefers-reduced-motion`, §9's global
collapse still applies (every duration to 0.01ms, so `animationend` still fires and `lit` still clears); on top of it
the aurora's breathing layer is hidden, the press and the settle are off, and **the entrances and the hero are off
outright**, each in the block that owns it (`app.css` §9, `check.js`, `tomorrow.js`, `today.js`): a collapsed rise-in
still painted its first frame at opacity 0, and the hero its arc at 0°, measured. So every screen is whole the instant it
appears.

**What did not change.** The mark and the wordmark SVGs (`#mark-template`, `#wordmark-template`), the check control's
ring-draw and flame-pop, the breathing loading mark, and the mark's left-to-right exception in Hebrew. **One
gradient:** lilac to rose is still the only solid gradient — the primary button, the ring, a pressed chip, the
secondary outline, the sheet's edge, the dock's dot, the closet strip's rings on Tomorrow; the aurora, the dock's
sheen, the tip panel and the Today strip are faint washes of the same lilac and rose with the amber (10–17%), and the
occasion pastels are never a gradient, a frame or an outline. **Fire** is still for reactions, the medals, the flame
and the weak dot; `--fire-ink` is the same fire, lighter, for a word on its own wash. **Every id and class** the app
and the browser test query is where it was; the new class names are additive (`.result-photo`, `.result-scrim`,
`.result-verdict`/`-read`/`-pieces`/`-doors`, `.pieces`, `.chip.piece`, `.piece-note`, `.empty-mark`, `.today-print`,
`.tm-side`, `.tm-more` on a `details`). Logical properties, 44px targets, the 16px gutter and 16px fields, and §10's
rule that a screen's own CSS lives in its own module. No route, setting or i18n key was added (the fold reuses
`common.more`).

**Where the build departed from the plan, and why** (`DECISIONS.md`, Round 21, has the full list).
- **The chips sit by physical left and right** (`left: 12px; right: 72px` on a card, `left: 16px; right: 176px` on the
  result), not `inset-inline`. The plan assumed the ring sits at the bottom-end; it is `direction: ltr` and stays
  bottom-right in both languages, and the first Hebrew render showed mirrored chips hidden under it. In Hebrew the row
  still flows from the ring's side leftward.
- **The Old money key is `OldMoney`**, the app's own string, not `Old money`.
- **The mounts are the app's:** `#tried-action` sits inside `#tip`, `#taste-reasons` after it as its own glass panel
  (inside, it would have made the warm panel tall), and a `#taste-win` slot above the tip, without which that line lands
  above the photo.
- **Tomorrow's pieces are one list:** the chips are the `ul` of `li[data-item]`, because the browser test counts three
  `li` in `#tm-one-look` and a hidden second list would have made it six.
- **`.tip p` became `.tip > p`**, so a door mounted inside the tip keeps the body face.
- **The keep row's rules live in `app/wardrobe.js`**, which draws it; `[dir=rtl] .tm-sentence` lives in `tomorrow.js`.
- **The result headline is 38px in English** on the result (40 elsewhere and in Hebrew).
- **The proofs corrected four things:** `--fire-ink` for the weak verdict word and a lit count (4.1:1 in `--fire`, 5.1:1
  now); the entrances and the hero off outright under reduced motion; the result's chip box eased to `right: 176px` and
  the chip to 10px padding and 13px, so three names take two rows at 390px; the Today strip's words to a 200px basis.
  And `occasionChips()` / `styleChips()` now load `check.js`'s rules wherever they are drawn, so Tomorrow's style row
  keeps its spacing in a browser that never opened the check.
- **The fonts are self-hosted with no Google fallback** (above), and the 60px card glow stayed: under a 6x CPU throttle
  on 14 cards the feed held a 17ms frame at the 95th percentile with one frame over 50ms in 352.

**Open questions, and what is left as it is.**
- **The arc in Hebrew.** The meter fills clockwise from the bottom-left in both languages. In the Hebrew renders it reads
  as a gauge filling up, and it was left for a Hebrew reader to judge on a real phone (`LAUNCH.md` 1.8, step 10); if it
  reads backwards, the change is `from 150deg` under `[dir=rtl] .hero`, and only there.
- **The landing pages** took the plum tokens in their own `<style>` and screens re-shot from the real Round 21 app
  (`5e0d8e2`); the review shot them again so another person's look carries no verdict dots, and
  `tools/brand/shoot/kit-shoot.js` takes them again after any change (`LAUNCH.md` 3.2).
- **A posted look's verdicts are its author's** (and a moderator's in the queue), settled in the review: the post
  sheet promised the stylist's notes on each piece stay with the person, and a public number never published them.
  Anyone else's card draws the names with no dot (`DECISIONS.md`, Round 21, review fixes).
- **Left as it is:** the share card and film draw the full ring, not the meter; a look with five or more pieces shows
  four chips and no count (the look page lists them all); on Tomorrow, English
  piece names in a Hebrew column wrap onto a second line; the Today strip is taller on a phone than the mock, with the
  tags under the words; the Today strip's and the "after the tip" picker's small rings keep their old black shadow; the
  old `.items li` list rules and a few overrides that undid the old caps `h2` are still in the files and do nothing.
