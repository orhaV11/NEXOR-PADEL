# OREVOSH — Design system: Night Atelier

**Winner by the judges' totals:** Night Atelier 117.5 (39 + 39.5 + 39) · Paper Editorial 114.5 · The Wall 111.
No tie, so no creative-director override. Night Atelier is the system; the brief below grafts the "steal" ideas that
fit it and resolves every verdict against it. This is the system `app.css`, `index.html`, `core.js`, `explore.js` and `profile.js` implement; the judged renders of the
three candidate directions lived in the build session and are described in `DECISIONS.md`.

**The sentence the app must say:** a wall of mounted prints in a lamp-lit room where brands and the people who wear
them meet. Every photo is a print, every score is a brass stamp, the check control is the same stamp raised on the
dock, every label is a hang-tag. Young and energetic comes from big serif numerals, tilted stamps, a staggered wall,
a ranked index and fire orange — not from gradients, pills and glass.

**Grafted from Paper:** the Explore front (kicker · h1 · dateline · split hero from `topLooks[0]`), trending tags as a
numbered index with dotted leaders, captions (name over intent) under every Explore print, the serif "O" monogram on
the check stamp, the profile statline + colophon split, the own-profile index list, the unread count as a serif numeral.
**Grafted from Poster:** full-height tap blocks in the dock ("colour is the state"), 42px outlined action targets, the
follow control as the profile's one big label (44px), the brands strip as a full-bleed contrasting band, the dashed
"not yet" disabled button, the physical text-align pin for a Latin bio in Hebrew, `data-route` on `<html>`,
`_one` locale keys, and a brass edge on sheets.
**Refused:** the two-row sideways tag wall (hides half the tags), 46px plate numerals over the photo (two numbers per
print — the rank moves into the caption), a light theme (not shipped), the lilac accent (brass stays), colour-cycling
shadows, torn labels, tilted follow stickers, a bento that needs exactly 6 or 9 looks.
**Verdict fixes applied to the winner:** dock labels 10 → 11.5px/700 in tap blocks; the translucent dock becomes
opaque (nothing ghosts through); dock reserve 96 → 88px; follow label 34 → 44px; Hebrew bio/website no longer
zig-zag; chip cloud → index; no hero → hero; "1 LOOKS" / "1 COMMENTS" → singular keys; the sticky composer gets an
opaque ground; the fire-orange pill badge (the last TikTok leftover) becomes a serif numeral.

---

## 1. Tokens

**Dark only.** `color-scheme: dark`, `<meta name="theme-color" content="#171219">`. No light theme ships; the ground
is part of the identity (prints on a dark wall). Nothing in the CSS may assume a light ground.

### Colour (`:root`)

| token | value | role |
|---|---|---|
| `--bg` | `#171219` | aubergine charcoal ground |
| `--bg-2` | `#1d1620` | warm top of the lamp gradient; masthead, sticky feed head, composer |
| `--surface` | `#221a25` | mount board: cards, dock, sheets, install banner |
| `--surface-2` | `#2c2330` | raised board: the brands band, pressed states, photo placeholders, the second sheet behind the drop zone |
| `--line` | `#3b2f3f` | opaque hairline between blocks (masthead rule, activity rows) |
| `--frame` | `rgba(243,234,217,.42)` | the ivory hairline that frames prints, portraits, the drop zone, the follow label |
| `--frame-soft` | `rgba(243,234,217,.16)` | quieter hairline: card border, chips, action outlines, index rows, section rules, dock border |
| `--ink` | `#f3ead9` | ivory text (≈15:1 on bg) |
| `--ink-2` | `#cdbfae` | body copy, captions, bio (≈9.5:1) |
| `--ink-3` | `#9a8b95` | caps labels, meta, inactive tabs (≈5.6:1) |
| `--accent` | `#d8b163` | **brass** — the single accent: stamps, labels, links, #tags/@mentions, active marks (≈9:1 on bg) |
| `--accent-deep` | `#8a6a2c` | the stamp's edge ("thickness" shadow), button edge |
| `--accent-ink` | `#1a1408` | ink on brass (≈9:1 on brass) |
| `--accent-2` | `#e9d197` | pale brass: top stop of the brass gradient, hairlines on brass |
| `--accent-tint` | `rgba(216,177,99,.16)` | the active dock block, unread activity rows, alert tint |
| `--fire` | `#ff6b2d` | **reactions only**: fire count/label when pressed, the fire stat, the unread numeral, the burst flame, the "weak" verdict dot |
| `--fire-tint` | `rgba(255,107,45,.10)` | fill of a pressed fire action |
| `--ok` | `#7fd8a4` | "works" verdict dot, success alerts |
| `--danger` | `#ff7a6b` | report/delete rows, error alerts, `.btn-danger` |

Brass gradient (stamps, dock stamp, primary button): `linear-gradient(160deg, #e6c27a 0%, #d8b163 55%, #c69a4c 100%)`.

Ground: `html { background: #171219; background-image: radial-gradient(120% 55% at 50% -12%, rgba(216,177,99,.13), transparent 62%), linear-gradient(180deg, #1d1620 0, #171219 420px); background-attachment: fixed; }` (a lamp above the wall).
Grain: `body::before`, `position: fixed; inset: 0; z-index: -1; opacity: .07; pointer-events: none`, inline SVG
`feTurbulence type=fractalNoise baseFrequency=.9 numOctaves=2`, tiled at 180×180px (the data URI in the atelier
override). `body { background: transparent }` so the grain sits between ground and content.

### Shape

One signature shape everywhere: **a square with its top-end corner clipped round** — `border-radius: var(--radius); border-start-end-radius: <clip>`. Logical, so Hebrew mirrors it for free.

| token | value | used by |
|---|---|---|
| `--radius` | 3px | every square corner |
| `--radius-clip` | 16px | cards, portraits (lg avatar, brand portrait), sheets (24px), primary buttons, the drop zone, the challenge card |
| `--radius-clip-sm` | 10px | chips, labels, icon buttons, grid prints, card photo, avatars, the follow label |
| `--radius-clip-xs` | 8px | action outlines, tags, featured label, product links, the active dock block |
| stamps | 2px / 9px (card), 2 / 6 (grid), 3 / 13 (dock stamp) | |
| dock | 4px / 20px | |

No other radius exists. Nothing is a circle except the burst flame; nothing is a 999px pill.

### Spacing scale

`4 · 6 · 8 · 10 · 12 · 14 · 16 · 18 · 22 · 24 · 32`. Page gutter 16 (`.view`), cards inset 14 with 22 between them,
24 between Explore sections, 12 inside a section, 10 card padding, 8 between chips, 6 between chip rows, 10 grid gap,
14 between brand portraits, 26×16 wall gaps. Touch targets ≥ 44px everywhere (dock blocks 56, stamp 52, chips 38 in
44 rows, actions 42 in a 50 row, follow 44, index rows 46, sheet rows 52).

### Shadows

| name | value |
|---|---|
| mount (card) | `0 14px 34px rgba(0,0,0,.35)` |
| print (photo, portrait, grid) | `0 10px 26px rgba(0,0,0,.45)` (grid: `0 8px 20px rgba(0,0,0,.4)`) |
| stamp | `0 3px 0 var(--accent-deep), 0 8px 18px rgba(0,0,0,.4)` (grid: `0 2px 0 …, 0 6px 12px …`) |
| dock stamp | `0 4px 0 var(--accent-deep), 0 14px 26px rgba(216,177,99,.32), 0 10px 20px rgba(0,0,0,.45)`; pressed `0 1px 0 var(--accent-deep), 0 6px 14px rgba(216,177,99,.25)` |
| dock | `0 18px 44px rgba(0,0,0,.55), 0 2px 0 rgba(0,0,0,.35), inset 0 1px 0 rgba(255,255,255,.05)` |
| sheet | `0 -2px 0 var(--accent), 0 -20px 50px rgba(0,0,0,.5)` (the brass edge) |
| button | `0 2px 0 var(--accent-deep)`; pressed `0 1px 0` |

No blur anywhere except the masthead (`blur(14px)` over `--bg-2` at 92%). The dock and the composer are opaque.

---

## 2. Type

Google Fonts line for `index.html`:
`https://fonts.googleapis.com/css2?family=Instrument+Serif:ital@0;1&family=Heebo:wght@400;500;600;700&family=Frank+Ruhl+Libre:wght@400;500&display=swap`
(Syne is removed; Heebo 800 is removed.)

| role | family / weight | stack |
|---|---|---|
| `--font-display` — wordmark, h1, card headline, hero headline, brand name, profile name, segments, sheet titles, **every numeral that matters** (score, counts, stats, ranks, unread) | **Instrument Serif 400** (+ italic 400 for the dateline, the index counts, the colophon, the challenge hashtag) | `"Instrument Serif", "Frank Ruhl Libre", Georgia, "Times New Roman", serif` |
| Hebrew display | **Frank Ruhl Libre 400/500** (Instrument Serif has no Hebrew; it follows in the stack and sits on the same baseline at these sizes) | |
| `--font-body` — UI, body, captions, buttons, chips, meta | **Heebo 400 / 500 / 600 / 700** (Latin + Hebrew) | `"Heebo", system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans Hebrew", sans-serif` |
| `--caps` — the label voice: section heads, tags, dock labels, stat labels, tabs, action labels, meta | Heebo 600 11px, uppercase, `letter-spacing: var(--caps-track)` = .12em; `[dir=rtl]` → .04em and +1.5px (Hebrew has no capitals and reads small at Latin caps sizes) | |

Scale (px / line-height; display serif unless noted):

| element | size |
|---|---|
| wordmark | 25 / 1, tracking .18em, `direction: ltr`, a 6px brass square (clipped corner) after the H |
| h1 (Explore, Activity, sign-in, result states) | 42 / .98 |
| profile name | 32 / 1 |
| result headline | 36 / 1.04 |
| feed segments (For you / Following) | 27 / 1 |
| card headline | 23 / 1.12 |
| hero headline (Explore) | 22 / 1.12 |
| challenge title (card) / sheet title | 21 / 24 |
| brand name (portrait) | 20 / 1 |
| own-profile index rows | 17 |
| action count | 17 / 1 |
| caption (Heebo) | 15 / 1.5, `--ink-2` |
| body (Heebo) | 15–16 / 1.45 |
| meta: handle · time, dateline, followers (Heebo 12.5 / serif italic 14) | 12.5 |
| score numeral | 27 (card) · 20 (wall, hero) · 17 (grid); "/10" Heebo 700 9 / 8 / 7, tracking .08em |
| stat numerals (statline) | 23 / 1 |
| colophon | italic 13.5, numerals upright 400 `--ink-2` |
| index rank | 14, tabular, `direction: ltr` |
| caption rank on the wall | 13 brass |
| dock labels | Heebo 700 11.5 caps, tracking .08em (Hebrew 12.5, .02em); CHECK 10.5 |
| unread numeral | serif 14 `--fire` |
| caps labels | 11 (Hebrew 12.5) |

Numerals: score stamps, stat numerals, action counts, ranks and the unread count are `direction: ltr` so "7/10"
never reads "10/7" in Hebrew. Display serif is never bolded (weight 400 only). Placeholders are upright Heebo
`--ink-3` (Heebo has no italic Hebrew).

---

## 3. Navigation — the ledger dock with a raised O stamp

A floating opaque dock of four text destinations and one raised brass stamp for Check. All five destinations stay
under one thumb: the dock sits 10px above the safe area, every destination is a 56px-tall block, the stamp is the
biggest target on the screen.

**Geometry at 390pt (LTR; RTL mirrors through the grid and logical properties):**

- `nav.tabbar`: `position: fixed; inset-inline: 14px; inset-block-end: calc(10px + var(--safe-b)); z-index: 6;`
  width 362 (`max-inline-size: calc(var(--col) - 28px); margin-inline: auto` on wide screens); height 56;
  `background: var(--surface)` **opaque, no backdrop-filter**; `border: 1px solid var(--frame-soft)`;
  `border-radius: 4px; border-start-end-radius: 20px`; dock shadow. `padding-block-end: 0`.
- `.tabbar-inner`: `display: grid; grid-template-columns: 1fr 1fr 74px 1fr 1fr; block-size: 56px`.
- **Destination = a full-height tap block.** `.tab` is `position: relative; display: flex; align-items: center; justify-content: center;` with the label only (all four SVG icons are removed from `index.html`).
  Label: `span[data-i18n]`, Heebo 700 11.5px uppercase tracking .08em, `--ink-3`; `[dir=rtl]` 12.5px / .02em.
  Active (`aria-current="page"`, set by `renderShell()` today): the label turns `--ink` and a brass-tinted block appears behind it —
  `.tab[aria-current="page"]::before { content:""; position:absolute; inset: 5px 3px; background: var(--accent-tint); border-radius: 2px; border-start-end-radius: 8px; }`.
  Pressed (`:active`): the same block at `rgba(216,177,99,.28)`. No underline. Colour is the state.
- **Unread**: `#activity-badge` stops being a pill. The Activity tab lays out `flex-direction: row; align-items: baseline; gap: 5px`; the badge is `position: static; background: none; padding: 0; min-inline-size: 0; block-size: auto; font: 400 14px/1 var(--font-display); color: var(--fire); direction: ltr`.
  It reads **ACTIVITY 12** with the numeral in fire serif — a text bar gets a text badge, and it mirrors for free (in Hebrew the numeral follows the word at its end edge). "99+" stays.
- **The stamp** (`.tab.check .stamp`, renamed from `.plus`): 52×52, `position: absolute; inset-block-start: -22px; inset-inline-start: 50%; margin-inline-start: -26px` — it **protrudes 22px above the dock's top edge**; brass gradient; `border-radius: 3px; border-start-end-radius: 13px`; `transform: rotate(-6deg)` (`[dir=rtl]`: `rotate(6deg)` — it leans into the reading direction); inner frame `::after { inset: 4px; border: 1px solid rgba(26,20,8,.45); border-radius: 2px; border-start-end-radius: 10px }`; dock-stamp shadow.
  **Glyph: the serif "O"** — a text node `O` in `--font-display` 30px, `--accent-ink`, centred, `direction: ltr` (OREVOSH's initial; the wordmark's O, not a "confirm" tick and not a plus). The word **CHECK** (10.5px caps, `--ink-2`) sits at the dock's bottom edge, 6px up (`.tab.check { justify-content: flex-end; padding-block-end: 6px }`).
  Press: `transform: rotate(-6deg) translateY(3px)` and the thickness shadow drops to 1px (a stamp being pressed). Active on `#/check` and `#/result` (`aria-current` already lands on the check tab via `tabFor`): the CHECK word turns `--ink` and the stamp column gets the same tinted block; the stamp itself does not change.
- **Reserve**: `--tabbar: 88px` (56 dock + 10 lift + 22 overhang). `body { padding-block-end: calc(var(--tabbar) + var(--safe-b)) }`; `.toast` sits at `calc(var(--tabbar) + var(--safe-b) + 12px)`; `.composer` at `calc(var(--tabbar) + var(--safe-b))`. `body.no-tabbar` keeps 0.
- Safe area: the whole dock lifts with `env(safe-area-inset-bottom)`; the stamp's overhang never reaches the masthead on any screen because the dock is fixed to the bottom.
- Sheets set the dock `inert` (unchanged `sheet()` logic; the dock is still `nav.tabbar`).
- Wide screens (≥ 560px): the dock stays 492px max, centred; cards centre in the 520px column.

Why it is OREVOSH's: you press the O stamp to get stamped, and the same brass stamp appears on every print you scroll.

---

## 4. The look card — a mounted print

`article.card`, 362px wide at 390pt (`margin-inline: 14px; margin-block-end: 22px`), top to bottom:

1. **Mount**: `background: var(--surface); border: 1px solid var(--frame-soft); border-radius: 3px; border-start-end-radius: 16px; padding: 10px 10px 6px;` mount shadow. Cards no longer run edge to edge; the first card sits 4px under the sticky feed head.
2. **Head** (`.card-head`, `padding: 4px 4px 12px; gap: 10px`): avatar 36px in the signature shape (`border-radius: 3px; border-start-end-radius: 10px; outline: 1px solid var(--frame-soft); outline-offset: -1px`); name Heebo 700 15px `--ink` (+ the BRAND mark: 9px caps brass, 1px brass-60% outline, radius 2/6, `margin-inline-start: 8px`); `@handle · 9 minutes ago` 12.5px `--ink-3`; the **intent as a typographic label** — brass caps 11px with a 1px `rgba(216,177,99,.45)` bottom rule, no fill; the `…` icon button 44×44, `--ink-2`, clipped-corner press state.
3. **Print** (`a.card-photo`): 340×425 (4:5, unchanged), `border: 1px solid var(--frame); border-radius: 2px; border-start-end-radius: 10px; background: var(--surface-2); overflow: visible;` print shadow; an **inner mat line** `::before { inset: 5px; border: 1px solid rgba(243,234,217,.22); border-radius: 1px; border-start-end-radius: 7px }`; `img { border-radius: inherit; aspect-ratio: 4/5; object-fit: cover }`. Double-tap-to-fire and the burst flame (now `--fire`) are unchanged.
4. **Score stamp** (`.score-badge`, aria-hidden as today): `inset-block-end: -11px; inset-inline-end: 12px` — it **straddles the print's bottom edge**, clear of the head and collar of a 4:5 outfit shot. Brass gradient, `--accent-ink`, serif 27px numeral + Heebo 700 9px "/10"; `padding: 6px 9px 5px 8px; border-radius: 2px; border-start-end-radius: 9px; transform: rotate(-6deg)` (`[dir=rtl]` `rotate(6deg)`); inner frame `::after { inset: 3px; border: 1px solid rgba(26,20,8,.4) }`; stamp shadow; `direction: ltr; z-index: 2`.
5. **Body** (`.card-body`, `padding: 22px 4px 6px` — the 22 clears the stamp; children 8px apart):
   - hidden/under-review alert (outlined `--danger`, 6% tint) when `post.hidden`;
   - **headline** in the display serif 23/1.12 `--ink` — the print's caption card;
   - **caption** Heebo 15 `--ink-2`, `#tags` and `@mentions` in brass 500 (no underline);
   - **Featured by NEXOR** (`a.featured`): an outlined brass hang-tag — caps 11, sparkle icon 12px, 1px brass-50%, `padding: 4px 8px`, radius 2/7 — linking to the brand's Featured tab;
   - **READS AS DATE AT 72%** (`.match`): caps 11 `--ink-3` + a 2px brass fill on a 2px `--frame-soft` track (no rounded bar);
   - **challenge marker** (`a.challenge-link`, "In: Date night in black"): brass caps 11, underlined with brass-50%, offset 3px;
   - **product links** (`.shop a`): outlined hang-tags — 1px `--frame-soft`, radius 2/7, bag icon 16px, label 14px `--ink-2`, price in brass 600, 36px tall, 8px gaps.
6. **Actions** (`.actions`, a `--frame-soft` hairline above, `padding: 8px 0 2px; gap: 6px`, row height 50): every action is a **42px outlined target** (`border: 1px solid var(--frame-soft); border-radius: 2px; border-start-end-radius: 8px; background: transparent`).
   - **fire** (`.action.fire`): icon 19px + serif count 17 `--ink` + `.lbl` caps 11 `--ink-3` "FIRE"; `padding-inline: 10px 12px`. Pressed (`aria-pressed=true`): border, icon, count and label all `--fire`, fill `--fire-tint`.
   - **comments** (`a.action.comments`): icon + serif count + `.lbl` "COMMENTS" / "COMMENT" (singular key).
   - **save** and **share**: 42×42 icon-only (names stay in `aria-label`); saved = brass icon + brass border.
   - **more** lives in the head; the sheet it opens is §7.
   - In a challenge context the votes tag (`.tag.accent.end`) is a brass-filled label at the row's end.
   The row reads `🔥 3 FIRE · 💬 1 COMMENT · 🔖 · ↑` as an editorial caption line with real tap width. Mark `.lbl` `aria-hidden="true"` so screen readers do not hear the word twice (the `sr-only` spans still carry "On fire").

**Grid prints** (`.grid a`, profile, saved, search): 3 columns, gap 10, `padding-inline: 14px`; each print framed like the card's photo (1px `--frame`, radius 2/10, print shadow, `overflow: visible`) with a 17px stamp at `inset-block-end: -8px; inset-inline-end: 6px` (radius 2/6). The `.private` tag sits top-start on a `rgba(23,18,25,.8)` ground.

---

## 5. Explore — the atelier wall with a front page

`views/explore.js`, `.view` gutter 16, 24px between sections. Section heads (`h2`) are brass caps 11 with a
`--frame-soft` hairline running to the end of the line (`h2::after`); in `.section-head` the action ("ALL CHALLENGES")
is `--ink-2` caps with a brass underline. Order, top to bottom:

1. **Front** (`.x-front`): kicker **THIS WEEK** (brass caps 11) · `h1` **Explore** (serif 42/.98) · dateline *Issue · 5 September 2026* (serif italic 14 `--ink-3`, `fmtDate(now)`) · a 1px `--frame-soft` rule under the block. The date follows the locale.
2. **Search** (`form.search`): a rule, not a box — 48px tall, the search glyph 20px in brass at the start (`inset-inline-start: 2px`), input `padding-inline-start: 34px`, `border: 0; border-block-end: 1px solid var(--frame)`, Heebo 16 `--ink`, placeholder `--ink-3` upright; focus turns the rule brass and 2px (`box-shadow: 0 1px 0 var(--accent)`). 14px under the front.
3. **Hero** (`a.x-hero`, from `topLooks[0]`, the whole block is one link to the post): `display: grid; grid-template-columns: 58% 1fr; gap: 14px; align-items: stretch`.
   - `figure`: a framed 4:5 print (same frame, mat line and print shadow as the card photo, radius 2/16) with a **20px stamp** at bottom −10 / end 10.
   - `figcaption` (`display: flex; flex-direction: column; gap: 8px; min-inline-size: 0`): kicker **THIS WEEK'S LOOK** (brass caps 11) · the stylist headline (serif 22/1.12 `--ink`, `unicode-bidi: plaintext`) · `p.x-hero-by`: **Name** (Heebo 700 14 `--ink`, bdi) · intent (caps 11 `--ink-3`) · **READ THE LOOK →** (`span.x-hero-more`, caps 11 brass, `margin-block-start: auto` pins it to the bottom; the arrow is `::after { content: "→" }` and `[dir=rtl]` flips it with `scaleX(-1)`).
   The cover can be a brand's look or a person's — the product sentence in one component. If `topLooks` is empty the hero is skipped.
4. **Trending tags → a numbered index** (`ol.index`, replaces the chip cloud): rows ≥ 46px with `--frame-soft` hairlines (none after the last); each row is one link `a[href=#/tag/x]` laid out `display: flex; align-items: baseline; gap: 12px; padding-block: 12px 10px`:
   `span.num` "01" (serif 14, tabular, `direction: ltr; min-inline-size: 22px`, **brass for ranks 1–3**, `--ink-3` after) · `bdi.tag-name` "#allblack" (Heebo 500 16.5 `--ink`) · `span.lead` (`flex: 1; border-block-end: 1px dotted var(--frame-soft); transform: translateY(-5px); min-inline-size: 16px`, aria-hidden) · `span.count` *2 looks* (serif italic 13.5 `--ink-3`, `white-space: nowrap`, from `t('tag.looks', {n})` — "1 look" via the `_one` key).
   Numerals sit at the start edge and counts at the end edge in Hebrew for free. The same index renders on search results (`explore.tags`); tag pages keep their look list.
5. **Brands to follow → a full-bleed band** (`div.band`): `margin-inline: -16px; padding: 14px 16px 16px; background: var(--surface-2); border-block: 1px solid var(--frame-soft); display: flex; gap: 14px; overflow-x: auto; scroll-snap-type: x proximity; scrollbar-width: none`. Each `.brand-card` (128px, `scroll-snap-align: start`, no background, start-aligned): a **128×160 (4:5) portrait** — the brand avatar at `object-fit: cover` in the signature shape (radius 2/16, `outline: 1px solid var(--frame)`, print shadow; initials on the hue ground when there is no image) · the brand name in the serif 20/1 (`unicode-bidi: plaintext`) · followers in caps `--ink-3` · the **follow label** (36px tall, full card width, brass fill FOLLOW / outlined ✓ FOLLOWING). Brands sit in the exact frame the looks use. No API change: the portrait is the avatar.
6. **Top looks this week → the wall** (`postGrid(looks.slice(1, 7), { wall: true, captions: true })`, look #1 is the hero and is not repeated; the section is skipped when fewer than 2 looks): `grid-template-columns: 1fr 1fr; gap: 26px 16px; padding: 18px 16px 48px; counter-reset: look 1`. Each cell is `a > figure(img + stamp) + figcaption`; the **second column drops 34px** (`.grid.wall > a:nth-child(2n) { transform: translateY(34px) }`) so the prints hang like a wall while every photo stays 4:5 (171×214 at 390pt), framed with radius 2/16 and a **20px stamp** at bottom −10 / end 10. **No plate numeral on the photo.** The rank moves into the caption: `figcaption { padding-block-start: 12px }` → line one: `span.rank` (CSS counter `counter(look)`, serif 13 brass, `direction: ltr`, 6px gap) + `b` **Name** (serif 16 `--ink`, `unicode-bidi: plaintext`); line two: INTENT (caps 10.5 `--ink-3`; Hebrew 12). The stamp is the only number on a print; the rank and the name read as a gallery label.
7. **Hashtag challenges — the tail, a side feature**: `section-head` with ALL CHALLENGES at the end · intro (`p.muted` 14) · one **quiet outlined ticket** per open challenge (`a.x-challenge`: transparent, 1px `--frame-soft`, radius 3/16, `padding: 14px 16px`): line one `bdi.x-hash[dir=auto]` **#datenightinblack** (serif italic 15 brass — the mechanic leads) · title (serif 21) with the intent label at the end · meta **BY NEXOR · ENDS IN 5 DAYS** (caps 10.5 `--ink-3`). No fill, no colour block. When there are none: "No open challenges right now." in `--ink-3`.

Search results reuse: person rows (`.person`, 12px padding, `--frame-soft` hairlines, 40px avatar, name 700 15, `@handle · n followers` 12.5, follow label 36px at the end) and the tag index.

---

## 6. Profile — portrait, statline, colophon, a label to follow

- **Masthead**: title `@nexor` in the serif 21 (bidi-isolated, as today); back arrow at the start (flips in RTL); Settings gear on my own profile.
- **Head** (`.profile-head`, `gap: 16px; align-items: flex-start; padding-inline: 16px`):
  - portrait 84×84 in the signature shape (radius 3/16, `outline: 1px solid var(--frame)`);
  - `.who`: `h1.name` in the serif 32/1 with `unicode-bidi: isolate; text-align: start` (a Latin brand name sits at the start edge in Hebrew) + the BRAND mark (`vertical-align: 6px; margin-inline: 8px 0`); `@handle` 12.5 `--ink-3` 6px under; then, 10px under the handle, **the follow control** (`#follow`, `btn btn-sm`, kept inside `.who`): **44px tall, `min-inline-size: 150px`, start-aligned**, caps 11 tracking .12em, radius 3/10. Not following → brass fill, `--accent-ink`, **FOLLOW**, `box-shadow: 0 1px 0 var(--accent-deep)`. Following → transparent, 1px `--frame`, `--ink`, **✓ FOLLOWING** with the check drawn by the kit's `icon('check')` in brass (Heebo has no ✓). `aria-pressed` drives the state. It is the page's one big label and the thumb is already there.
- **Bio + website** leave `.who` and become `div.profile-bio` under the head, full width: bio Heebo 15 `--ink-2` (`dir="auto"`), website as an underlined brass link (`bdi dir=ltr`). `.profile-bio { unicode-bidi: plaintext; text-align: left } [dir=rtl] .profile-bio { text-align: right }` — a physical pin, so a Latin bio and website hug the same edge as the name and portrait in Hebrew; reading order stays plaintext. This ends the zig-zag.
- **Statline** (`p.statline`, between two `--frame-soft` rules, `padding: 14px 0 12px; display: flex; flex-wrap: wrap; gap: 4px 0; align-items: baseline`): **3 FOLLOWERS · 0 FOLLOWING · 3 FIRE**. Each item is `span > b + label`: numeral serif 23/1 `--ink` (`direction: ltr`), label caps 10.5 `--ink-3` (Hebrew 12.5, tracking 0), `white-space: nowrap` per item so it never wraps mid-item at 390pt in either language; separators are 4px brass squares (clipped corner) with 11px margins, `aria-hidden`. The fire numeral is `--fire`. Followers and following are figures, not links — the API has no list endpoints, so nothing pretends to open one; the follow affordance is the label above. Keep the `followers` `<b>` node reference so the follow control updates it live.
- **Colophon** (`p.colophon`, 10px under the statline): serif italic 13.5 `--ink-3`: *2 looks · Best score 7 · Day streak 1*, numerals upright serif 400 `--ink-2` (`direction: ltr; unicode-bidi: isolate`); a streak > 1 turns its numeral `--fire`. Built from `profile.posts_n` / `profile.best_n` / `profile.streak_n` with `_one` forms ("1 look", "יום אחד ברצף").
- **Own profile**: no follow control; between the colophon and the tabs, **Saved looks / My checks / Sign out as an index list** (`.links`): 52px rows, serif 17 `--ink`, `--frame-soft` hairlines (none after the last), the icon (bookmark / camera / none) 20px brass at the end.
- **Tabs** (`.profile-tabs`): caps 11 start-aligned with 6px gap (not thirds), 46px tall, `padding-inline: 14px`; active `--ink` with a 2px brass underline; a hairline under the strip.
- **Grid**: 3 framed prints per row with 17px stamps, `padding-block: 16px 8px`. Empty and error blocks span the row (existing rule).
- Head skeleton: an 84px clipped square, a 28px name line at 55%, a 12px handle line at 35%, a 44×150 control, then a 24px statline block and a 14px colophon line (replaces the six 58px tiles).

---

## 7. Buttons, chips, segments, inputs, sheets, toasts, skeletons

| component | shape and states |
|---|---|
| `.btn` primary | full width, 50px, brass gradient fill, `--accent-ink`, Heebo 700 15 tracking .02em, radius 3/16, `0 2px 0 var(--accent-deep)`; pressed: `translateY(1px)` + 1px edge. **Disabled ("not yet")**: `opacity: 1; background: var(--surface-2); color: var(--ink-3); border: 1px dashed var(--frame); box-shadow: none` — reads as "not yet", not as a faded block. |
| `.btn-secondary` | transparent, 1px `--frame`, `--ink`, no shadow |
| `.btn-ghost` | text `--ink-2`, brass underline offset 4px (Cancel, Check another) |
| `.btn-danger` | transparent, 1px `--danger`, `--danger` text |
| `.btn-sm` / follow label | caps 11, radius 3/10; 36px in brand cards and person rows, **44px on the profile**; brass fill (1px `--accent-deep` edge) when it invites; outlined `--frame` + brass ✓ (kit SVG) when `aria-pressed=true`; disabled while the request is in flight (60% opacity, no other change) |
| `.btn-text` | brass, underlined brass-60%, offset 4px, 500 (Delete, Report, Sign in to comment) |
| `.pill` (Join, language select) | outlined `--frame` caps label 36px, radius 3/10; `.pill.accent` brass fill |
| `.chip` (intent filters, Check intents) | 38px, 1px `--frame-soft`, radius 3/10, Heebo 500 14 `--ink-2`, `padding-inline: 13px`; pressed (`aria-pressed=true`): brass fill, `--accent-ink`, 700; disabled 35%. Chip rows keep 44px of tap height via padding. |
| `.tag` (intent) | brass caps 11 with a 1px brass-45% underline, no fill; `.tag.accent` (votes, posted) brass fill label radius 2/7; `.tag.rose` outlined brass; `.brand-mark` 9px caps brass outlined |
| `.segments` (For you / Following) | no box: serif 27/1, 22px apart, inactive `--ink-3`, active `--ink` with a 2px brass underline 4px under the baseline; 44px tall |
| `.profile-tabs` | caps 11, start-aligned, brass underline (§6) |
| inputs (`text`, `password`, `url`, `search`, `datetime-local`, `textarea`, `select`) | no box: `border: 0; border-block-end: 1px solid var(--frame)`, 48px, 16px `--ink`, `padding-inline: 2px`; label caps 11 `--ink-3`; focus: rule brass + `box-shadow: 0 1px 0 var(--accent)` (2px); placeholder `--ink-3`; `.hint` 13 `--ink-3` |
| `.switch` | outlined `--frame-soft` row radius 3/10; knob ivory; on = brass track |
| `.photo` (Check drop zone) | dashed `--frame` mount, radius 2/16, `--surface`, with a **second sheet behind** (`box-shadow: 10px 10px 0 -1px var(--surface-2), 10px 10px 0 0 var(--line)`; `[dir=rtl]` −10px); **empty state 4:3.1** so CHECK MY OUTFIT stays above the dock on 844pt, **4:5 once a photo is in**; "Add a photo" serif 26, camera 40px brass, hint 13; "Tap to replace" as a brass caps label bottom-start |
| `.sheet` | `--surface`, 1px `--frame-soft`, `border-start-start-radius: 4px; border-start-end-radius: 24px`, **a 2px brass top edge** (`box-shadow: 0 -2px 0 var(--accent), 0 -20px 50px rgba(0,0,0,.5)`), a 28×2 brass handle, serif 24 title, rows 52px with `--frame-soft` hairlines and brass 22px icons, danger rows `--danger`; backdrop `rgba(10,6,12,.6)`; swipe-down and focus trap unchanged |
| `.toast` | `--ink` on `--bg` text, 600 14, radius 2/10, above the dock |
| `.alert` | outlined brass with `--accent-tint`; `.alert.danger` outlined `--danger` with a 6% tint |
| `.notice` / `.install` | `--surface`, 1px `--frame-soft`, radius 3/16; the install mark is a brass 44px clipped square with a serif "O" |
| `.empty` | `b` in the serif 22 `--ink-2`, body 15 `--ink-3` |
| skeletons | `.skel`: `linear-gradient(90deg, var(--surface) 25%, var(--surface-2) 37%, var(--surface) 63%)`, shimmer 1.4s, radius 2 (clip 8 on blocks); `.skel-card` is a mount (margin-inline 14, `--surface`, `--frame-soft` border, radius 3/16, padding 10) with a 36px clipped avatar, a 12px line and a 4:5 placeholder (radius 2/10); grid skeleton = 6 framed 4:5 boxes; head skeleton per §6 |
| `.activity li` | 12px rows, `--frame-soft` hairlines, 40px clipped avatar, sentence 15, `.when` caps 10.5 `--ink-3`, thumb 40×50 framed (radius 2/6); unread rows `--accent-tint` |
| `.composer` | sticky above the dock, **opaque `--bg-2`**, 1px `--frame-soft` top rule, input as a bottom rule, Post button 46px brass |
| result screen | score in brass serif (existing clamp), headline serif 36, "the one tip" as a serif 24/1.25 pull quote with a 2px brass rule at the start, verdict dots in the signature shape (`works` brass, `weak` fire, `neutral` outlined) |

Focus ring: `2px solid var(--accent)`, offset 3px, radius 2. Selection: brass on `--accent-ink`.

---

## 8. Motion — short, mechanical, mostly still

What moves:
- **Press** = a stamp being pressed: dock stamp `translateY(3px)` with its thickness shadow collapsing 4 → 1px; primary button `translateY(1px)`, 2 → 1px; both `transition: transform 120ms ease, box-shadow 120ms ease`. Chips, tabs and actions change state instantly (no transition).
- **Sheets** rise 220ms `cubic-bezier(.2,.8,.2,1)`, sink 160ms; backdrop fades 160ms (existing).
- **Fire**: the burst flame 700ms and the spark 500ms on the action (existing, recoloured `--fire`).
- **Match bar** fill 600ms ease-out on first paint (existing).
- **Toast** 180ms opacity + 8px rise (existing). **Pull-to-refresh** indicator height 160ms (existing). **Skeleton** shimmer 1.4s.
- The result score count-up stays as implemented.

What stays still: the wall (no scroll-linked parallax, no entrance animations for prints), the stamps (rotation is
static), the dock (no hide-on-scroll; it is always reachable), the masthead, the hero. No hover effects (touch first).

`@media (prefers-reduced-motion: reduce)`: the existing global rule zeroes every animation and transition; `postCard`
already skips the burst flame via `reducedMotion()`; the sheet closes immediately; nothing else needs a branch.

---

## 9. Implementation plan

Implemented in the round recorded in `DECISIONS.md` (Phase 4). The plan below is kept as the map of where each rule lives.

### `index.html`
1. `<meta name="theme-color" content="#171219">`. `manifest.webmanifest`: `theme_color` and `background_color` `#171219`.
2. Fonts: replace the Heebo + Syne link with the line in §2 (Instrument Serif 400/italic, Heebo 400–700, Frank Ruhl Libre 400/500).
3. `nav.tabbar`: delete the four inline `<svg>`s in Home, Explore, Activity, Profile. Each is `<a class="tab" href data-tab><span data-i18n="nav.x"></span></a>`; Activity keeps `<span id="activity-badge" class="badge" hidden></span>` after its label.
4. Check tab: replace `<span class="plus"><svg…plus…></svg></span>` with `<span class="stamp" aria-hidden="true">O</span>` before the CHECK label span.
5. Nothing else: the grain is `body::before`, the masthead double rule is `.top::after`, the brass mark after the wordmark is `.wordmark::after`.

### `app.css`
Rewrite in place, section by section:
- **Tokens**: the §1 table (`--frame`, `--frame-soft`, `--accent-deep`, `--accent-tint`, `--fire-tint`, `--radius-clip`, `--radius-clip-sm`, `--radius-clip-xs`, `--caps`, `--caps-track`, `--tabbar: 88px`, `--top: 56px`); `[dir=rtl] { --caps-track: .04em }`. Fonts per §2. Ground + grain on `html` / `body::before`.
- **Label voice**: one shared rule for `h2, .tag, .tab > span[data-i18n], .statline > span, .brand-mark, .featured, .profile-tabs button, .match, .activity .when, .comments .when, .field label, .brand-card .sub, .x-challenge .x-meta, .challenge-meta, .action .lbl, .x-hero .kicker, .x-front .kicker` = `font: var(--caps); letter-spacing: var(--caps-track); text-transform: uppercase`, with `[dir=rtl]` size steps (11 → 12.5).
- **Shell**: masthead 56px, `--bg-2` at 92% + blur 14, `.top::after` brass second rule, serif wordmark with the brass square, `.top-title` serif 21, `.icon-btn` clipped corner.
- **Tab bar**: delete the whole current block (glass, 5-column, icons, `.plus`, pill badge) and write §3 (`.tabbar`, `.tabbar-inner`, `.tab`, `.tab::before` active block, `.tab[data-tab="activity"]` row layout, `.badge` serif numeral, `.tab.check`, `.stamp`, `[dir=rtl]` rotation).
- **Controls**: §7 (`.btn` + dashed disabled, `.btn-sm` 36/44 variants, `.chip` 38, `.segments` serif, inputs as rules, `.search`, `.switch`, `.alert`, `.pill`).
- **Avatars/people**: clipped-corner `.avatar` (sm/lg), `.brand-mark`, `.person` rows, `.band` (replaces `.people-scroll`/`.x-bleed`), `.brand-card` portrait.
- **Cards**: §4 (`.card` mount, `.card-head`, `.tag`, `.card-photo` + mat line, `.score-badge` stamp + `[dir=rtl]`, `.card-body`, `.headline` serif, `.featured`, `.match`/`.bar`, `.challenge-link`, `.shop a`, `.actions`, `.action` outlined 42px, `.action .count`, `.action .lbl`, pressed states).
- **Grids**: framed `.grid a`, small stamps, `.grid.wall` (two columns, stagger, `counter-reset: look 1`, figcaption with `span.rank::before { content: counter(look) }`), `.grid figcaption` typography. Delete `.grid.two`.
- **Explore**: `.x-front`, `.x-hero`, `ol.index`, `.band`, `.x-challenge` (+ `.x-hash`), and the rules moved out of `explore.js`'s `CSS` constant (`.x-section`, `.x-count`).
- **Challenges**: outlined `.challenge-card`, serif `.challenge-title` 28, `.prize`, `.lb-row`, `.vote` label — values from the override §10.
- **Check/result**: `.photo` stack + 4:3.1 empty / 4:5 loaded, `.photo-empty strong` serif 26, `.photo-replace` label, `.score`, `.result-headline` 36, `.tip` pull quote, `.dot` shapes, `.items li` hairlines.
- **Profile**: `.profile-head`, `.who .name` isolate/start, `.who > #follow` 44px, `.profile-bio` + `[dir=rtl]` pin, `.statline` + `.sep`, `.colophon`, `.links` index list, `.profile-tabs`. Delete `.stats`, `.stat`, `.stat.hot`.
- **Activity, comments, composer, sheet, toast, skeletons, install, offline**: §7 values; `.composer` opaque; `.sheet` brass edge.
- `@media (min-width: 560px)`: `.card { margin-inline: auto }`, `.challenge-card { margin-inline: 0 }`, `.activity li { padding-inline: 0 }`. Keep the reduced-motion rule.
- **Remove**: Syne, the lilac tokens (`--accent #b39dff`, `--accent-2 #ff8fb1`), every `border-radius: 999px`, the gradient wordmark, the glass tab bar and its 5 icons, the `.plus` circle, the pill badge, the blurred score badge, `.card` edge-to-edge borders, the `.stats` tiles, the boxed `.segments`, the boxed inputs, the boxed `.brand-card`, `.grid.two`, the lilac `::selection` and focus ring.

### `app/core.js`
1. `render()`: after `state.route = parseRoute()`, add `document.documentElement.dataset.route = state.route.name;` (CSS hooks per route; e.g. `[data-route="post"] .composer`).
2. `postCard`: give the comments link `class: 'action comments'`; after the fire count append `el('span', { class: 'lbl', 'aria-hidden': 'true', text: t('post.fire') })`; after the comments count append `el('span', { class: 'lbl', 'aria-hidden': 'true', text: t('post.comments_label', { n: post.commentCount }) })`. Keep the `sr-only` spans. Everything else in the card is CSS.
3. `paintFire`: unchanged (colour comes from `aria-pressed`).
4. `followButton(handle, following, onChange, opts)`: keep `btn btn-sm`; render `icon('check')` before the text when `following`, and re-render icon + text in the click handler (the CSS `::before` "✓" goes away). Add `btn-secondary` as today for the outlined state.
5. `postGrid(posts, opts)`: `opts.wall` → class `grid wall`; `opts.captions` → each cell is `a > figure(img, .score-badge, .private?) + figcaption(span.rank (empty, CSS counter), b(user.name), span(intentLabel))`. Drop `opts.two`. `profile.js` keeps calling it without opts.
6. `skeletonCards`: markup unchanged; the mount look is CSS.
7. `avatar`, `brandMark`, `userRow`, `iconButton`, `sheet`, `actionSheet`, `confirmSheet`, `setTopBar`, `defaultTopBar`, `renderShell`, `tabFor`, `toast`: unchanged. (`renderShell` already sets `aria-current` on the check tab for `#/check` and `#/result`.)

### `app/views/explore.js`
1. Delete the `CSS` constant and `ensureStyle()`; the rules live in `app.css`.
2. Explore route: replace `root.appendChild(el('h1', …))` with `el('div', { class: 'x-front' }, [el('p', { class: 'kicker', text: t('explore.kicker') }), el('h1', { text: t('explore.title') }), el('p', { class: 'dateline', text: t('explore.issue', { date: fmtDate(new Date().toISOString()) }) })])` (import `fmtDate`).
3. New `heroCard(post)`: `el('a', { class: 'x-hero', href: '#/post/' + post.id, 'aria-label': t('a11y.look_by', { intent, name }) }, [el('figure', {}, [img, scoreStamp]), el('figcaption', {}, [el('span', { class: 'kicker', text: t('explore.hero_kicker') }), el('p', { class: 'x-hero-title', text: post.headline }), el('p', { class: 'x-hero-by' }, [el('b', {}, [el('bdi', { text: post.user.name })]), ' · ', el('span', { text: intentLabel(post.intent) })]), el('span', { class: 'x-hero-more', text: t('explore.read_look') })])])`. Append it right after `searchForm` when `looks[0]` exists.
4. New `tagIndex(tags)` → `el('ol', { class: 'index' }, tags.map((item, i) => el('li', {}, [el('a', { href: '#/tag/' + encodeURIComponent(item.tag) }, [el('span', { class: 'num', text: String(i + 1).padStart(2, '0') }), el('bdi', { class: 'tag-name', text: '#' + item.tag }), el('span', { class: 'lead', 'aria-hidden': 'true' }), item.posts === undefined ? null : el('span', { class: 'count', text: t('tag.looks', { n: fmtCompact(item.posts) }) })])])))`. Use it for `explore.trending` and for `explore.tags` on search results; delete `tagChips`.
5. Brands: `section(t('explore.brands'), el('div', { class: 'band' }, brands.map(brandCard)))`. `brandCard` unchanged in structure; `avatar(user, 'lg')` becomes the portrait through CSS.
6. Top looks: `if (looks.length > 1) frag.appendChild(section(t('explore.top'), postGrid(looks.slice(1, 7), { wall: true, captions: true })))`.
7. `challengeCard`: prepend `c.tag ? el('bdi', { class: 'x-hash', dir: 'auto', text: '#' + c.tag }) : null` before the title row.
8. Tag route: unchanged.

### `app/views/profile.js`
1. Head: `.who` = `h1.name`, `.sub` handle, and — for other people's profiles — the follow control right there: `const follow = followButton(…); follow.id = 'follow'; who.appendChild(follow)` (keep `btn-sm`; delete `follow.classList.remove('btn-sm')` and the `actions` wrapper for that branch). Bio and website move out of `.who` into `el('div', { class: 'profile-bio' }, [bio, website])` appended after `.profile-head` (only when either exists).
2. Stats: replace the six-tile `stats` with `el('p', { class: 'statline' }, [item(followers, t('profile.followers')), sep(), item(following, t('profile.following')), sep(), item(fire, t('profile.fire'), 'hot')])` where `item = (valueNode, label, cls) => el('span', { class: cls }, [valueNode, el('span', { class: 'lbl', text: label })])` and `sep = () => el('span', { class: 'sep', 'aria-hidden': 'true' })`; keep `followers` as the `<b>` node the follow control updates. Then the colophon: `el('p', { class: 'colophon' }, [numeralise(t('profile.posts_n', { n: profile.posts })), ' · ', numeralise(t('profile.best_n', { n: best })), ' · ', numeralise(t('profile.streak_n', { n: profile.streak }), profile.streak > 1 && 'hot')])` where `numeralise(text, cls)` returns a `span` whose first run of digits (`/\d[\d.,]*/`) is wrapped in `<b class={cls}>` and the rest is plain text; a `_one` form with no digit ("לוק אחד") stays wholly italic. `best` is `profile.bestScore ?? '–'`. Delete `stat()`.
3. Own profile: the `.links` list stays and is appended after the colophon (order: head, profile-bio, statline, colophon, links, tabs).
4. `headSkeleton`: per §6 (portrait square, name line, handle line, control block, statline block, colophon line).
5. `tabStrip`, `gridList`, `checkRow`, saved/checks routes: unchanged.

### `app/views/feed.js`
No markup change. The segments, chips, install banner, pull indicator and cards are restyled through the classes they have. (The `explore.brands` link in the empty-following state is a `btn btn-secondary btn-sm` label — fine.)

### `app/views/check.js`, `post.js`, `activity.js`, `challenges.js`, `settings.js`, `auth.js`
No markup change. `check.js` keeps `.photo` / `.photo-empty` / `.photo-replace` (the 4:3.1 / 4:5 switch is `:not(.has-image)` in CSS); `post.js` inline styles may move to `app.css` at leisure; `challenges.js` keeps its small inline style block.

### `i18n/en.json` + `i18n/he.json` — new keys
| key | en | he |
|---|---|---|
| `explore.kicker` | This week | השבוע |
| `explore.issue` | Issue · {date} | גיליון · {date} |
| `explore.hero_kicker` | This week's look | הלוק של השבוע |
| `explore.read_look` | Read the look | ללוק המלא |
| `post.comments_label` / `_one` | Comments / Comment | תגובות / תגובה |
| `profile.posts_n` / `_one` | {n} looks / 1 look | {n} לוקים / לוק אחד |
| `profile.best_n` | Best score {n} | ציון שיא {n} |
| `profile.streak_n` / `_one` | Day streak {n} / Day streak 1 | {n} ימים ברצף / יום אחד ברצף |
Existing `tag.looks_one`, `profile.followers_n_one` already give the index and person rows their singulars.

### Verification (shoot at 390×844 @2x, `isMobile`, en + he)
Home fold (dock, stamp, first card with the stamp on the edge, action row with labels), Explore full page (front, rule, hero, index with brass 01–03, band, staggered wall with captions and no on-photo numerals, challenge ticket with the hashtag first), Profile (NEXOR: 44px FOLLOWING label under the handle, bio pinned to the same edge in Hebrew, statline on one line, colophon "1 look"), own profile (index list), Check empty (CTA above the dock, dashed disabled button) and loaded (4:5), Post (opaque composer, "1 COMMENT"), Activity (unread tint, ACTIVITY 12 in the dock), the "This look" sheet (brass edge and handle). RTL checks: stamp +6°, second sheet offset left, dock order בית · גילוי · [O בדיקה] · פעילות · פרופיל, index numerals at the right edge with counts at the left, hero arrow flipped, name and bio both on the right edge.
