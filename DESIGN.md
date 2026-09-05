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

| token | value | role |
|---|---|---|
| `--bg` | `#0b0b0f` | the stage |
| `--bg-2` | `#13121a` | top of the page gradient, masthead, sticky feed head, composer |
| `--surface` | `#15151c` | cards, dock, sheets |
| `--surface-2` | `#1e1e27` | raised: chips at rest, the brands band, placeholders, pressed rows |
| `--line` | `#2b2b36` | hairlines |
| `--line-soft` | `rgba(255,255,255,.08)` | quieter hairlines (action outlines, index rows) |
| `--ink` | `#f4f4f7` | text |
| `--ink-2` | `#b9b9c6` | body, captions |
| `--ink-3` | `#7f7f8e` | labels, meta |
| `--accent` | `#b39dff` | lilac: links, #tags, @mentions, active marks, ranks 1–3, pressed chips |
| `--accent-2` | `#ff8fb1` | rose: the warm end of the gradient, brand marks |
| `--accent-ink` | `#150f2e` | ink on the gradient |
| `--grad` | `linear-gradient(135deg, #b39dff 0%, #ff8fb1 100%)` | **the only gradient**: primary buttons, the score ring, the follow label, brand portrait rings, the sheet's top edge, the active dock label's dot |
| `--accent-tint` | `rgba(179,157,255,.14)` | active dock block, unread rows, selected states |
| `--fire` | `#ff6a2b` | **reactions only**: pressed fire, fire counts, the burst, the flame in the mark, the "weak" verdict dot |
| `--fire-2` | `#ffa83a` | the flame's tip |
| `--fire-tint` | `rgba(255,106,43,.12)` | fill of a pressed fire action |
| `--ok` | `#5ee6a0` | "works" dot, success |
| `--danger` | `#ff5d7a` | report, delete, errors |

Ground: `html { background: #0b0b0f; background-image: radial-gradient(90% 38% at 50% -8%, rgba(179,157,255,.16), transparent 62%); }`
(a lilac glow at the top of every screen, nothing else). No grain, no blur except the masthead (`--bg-2` at 92% + `blur(12px)`).

Shape: rounded everywhere. `--radius: 18px` (cards, tickets, the hero figure), `--radius-sm: 12px` (photos inside grids,
inputs, the stamp of the app icon), `--pill: 999px` (chips, segments, buttons, the dock, the follow label, product links,
the score ring). Sheets 24px on top. Avatars are circles.

Spacing: 4 · 8 · 12 · 16 · 20 · 24 · 32. Gutter 16, cards 14 from the edge with 20 between, sections 24 apart, 12 inside.
Touch targets ≥ 44px (dock blocks 56, the mark 60, chips 40 in 44 rows, actions 42 in a 50 row, index rows 46, follow 44).

Shadows: card `0 10px 30px rgba(0,0,0,.35)`; dock `0 16px 40px rgba(0,0,0,.55)`; the mark's halo
`0 0 0 6px rgba(179,157,255,.12), 0 10px 26px rgba(179,157,255,.35)`; primary button `0 8px 24px rgba(255,143,177,.25)`.

## 2. Type

Google Fonts: `https://fonts.googleapis.com/css2?family=Outfit:wght@500;700;800&family=Heebo:wght@400;500;600;700;800&display=swap`

| role | stack |
|---|---|
| `--font-display` — h1, card headline, hero headline, profile name, segments, every numeral (score, counts, stats, ranks, unread) | `"Outfit", "Heebo", system-ui, sans-serif` — Outfit 700/800; Hebrew falls to Heebo 800 |
| `--font-body` — UI, body, captions, buttons, chips, meta | `"Heebo", system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans Hebrew", sans-serif` |
| labels (section heads, dock, tags, stat labels, tabs, action labels) | Heebo 700 11px uppercase, tracking .08em, `--ink-3`; `[dir=rtl]` 12.5px tracking .02em |

Scale: wordmark 22px tall in the masthead (the SVG, never text) · h1 36/1.02 (Explore, Activity, states; 40 on the result
headline) · profile name 28/1 · card headline 21/1.15 · hero headline 22/1.12 · segments 24/1 · action count 16 · caption
Heebo 15/1.5 `--ink-2` · meta 12.5 · score ring numeral 22 (card) / 18 (hero, wall) / 15 (grid) · statline numerals 22 ·
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

- **The camera** (`views/camera.js`, `#/camera`): a full-bleed viewfinder with the masthead and the dock hidden. Chrome is
  three 44px discs on translucent dark (close at the top start, timer and flip at the top end), a dashed 4:7 framing guide
  with the hint under it (fades after 3s, back on tap), two mode pills (Photo / Clip) and **the shutter, which is the
  ring**: 76px, the gradient stroke, a white disc inside. Tap = photo (a 120ms white flash). Hold, or tap in Clip mode,
  and the disc becomes a red rounded square while the ring draws itself clockwise toward the 30-second cap, with a
  "Recording · Ns" pill and a blinking red dot at the top. The countdown is the display face at 168px. The preview
  after a capture is honest (`object-fit: contain`) with Retake (secondary) and Use it (gradient).
- **Clips in cards** (`core.js` media helpers, `app.css`): a `<video>` in the same 4:5 box as a photo, poster = the picked
  frame, muted autoplay when 60% visible (never more than two at once), a `.clip-pill` (the clip glyph + CLIP, caps
  label style) at the top start corner of cards and grid tiles, and a 32px sound disc at the top end corner of cards
  (44px hit area, icon `mute`/`sound`). Double-tap still fires; the sound disc stops propagation.
- **The frame picker** (`views/check.js`): the clip paused in the photo box, one range slider under it in `--accent`,
  "Clip · Ns" as a tag with the clip glyph, and a text button to remove the clip.
- **The story card** (`app/sharecard.js`): 1080×1920, the stage with the lilac glow, the photo cover-fitted in a 936×1170
  block with 48px corners, the score ring at r=160 straddling the photo's bottom-end corner (numeral Outfit 800 160,
  "/10" under it), the headline Outfit 700 64 on two lines max, the intent as a caps pill, name + @handle, a hairline,
  the wordmark 52px tall at the start edge and "Checked on OREVOSH" at the end edge. Mirrored for RTL. Shown in a sheet
  with Share (when files can be shared) and Save image.
- **Moderation** (`views/admin.js`): the queue as compact look cards (or the comment text with its author row), a meta line
  with the report count, the reasons as small chips, Hidden/Suspended tags, and an action row of outlined pills; Delete
  and Suspend confirm in a danger sheet. Reached from Settings, only for `isAdmin`.
- **The guidelines** (`views/pages.js`): h1, the intro, five numbered rules with bold titles, "What we keep", a version
  line. Linked under the 16+ checkbox on signup.

## 9. Motion

Ring draw + flame pop on the check control; flame breathing on the loading screen; the score count-up on the result;
the double-tap burst; a 120ms `scale(.985)` press on buttons; segment underline slides (transform, 200ms); sheets rise
220ms; the camera's ring draws while a clip records and its red dot blinks; the feed switch coin fades its fill in and pops
its glyph; everything off under `prefers-reduced-motion`. Nothing else moves.

## 10. Where each rule lives

`app.css` (all styles, §1–§9 order), `index.html` (fonts, inlined wordmark and mark SVGs, the dock markup), `app/core.js`
(the score ring markup inside `.score-badge`, the `lit` class on the check control, the flame burst), `app/views/check.js`
(the loading mark), `app/views/explore.js` and `profile.js` (structure unchanged from the previous system), `icons/` and
`manifest.webmanifest` (regenerated from `brand/mark.svg`).
