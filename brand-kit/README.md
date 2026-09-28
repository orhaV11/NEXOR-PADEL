# OREVOSH brand kit

Everything needed to show OREVOSH outside the app: the logos, the covers for social pages and the stores, the story
templates and the store screenshots, rendered from `tools/brand/templates/` by `tools/brand/render-kit.js` with the
real brand SVGs (`src/FitCheck.Api/wwwroot/brand/`), the tokens of `DESIGN.md` (the plum stage of Round 21) and ten
screenshots of the real app with the three real looks (`templates/photos/`, the owner's, used with permission)
checked, posted and featured in it. The slogan is **Check the look.** / **בודקים את הלוק.** with the tagline
*A stylist in your pocket, and a community that lights it up.* / *סטייליסט בכיס, וקהילה שמדליקה.*
(`MARKETING.md` has the runners-up; the copy lives at the top of `render-kit.js`, so changing it and re-running
regenerates every file).

## Regenerate

```bash
cd tools/brand
npm install                      # playwright, and sharp for smaller PNGs (never the ones with a photo in them)
npx playwright install chromium  # once; or CHROMIUM_PATH=/path/to/chromium
node render-kit.js               # everything; or: node render-kit.js stories store
```

The videos are not part of `render-kit.js`. They have their own script, run **from the repository root**:

```bash
node tools/brand/render-episode.js brand-kit/episodes/001-camel.json   # one episode, about half a minute
node tools/brand/render-episode.js 001-camel.json --preview            # a 10-second low-res pass to check timing and copy first
node tools/brand/render-episode.js --all brand-kit/episodes            # every episode in that folder (not its subfolders)
node tools/brand/render-episode.js --all brand-kit/episodes/week-1     # the first week's five posts
node tools/brand/render-episode.js 001-camel.json --cover-only         # just the thumbnail, seconds
node tools/brand/render-episode.js --list-checks orevosh-or-20260927.json   # the scored checks in a Settings export, with ids
```

`episodes/` is the account's weekly material (`CONTENT.md` is the manual): a 1080x1920 MP4 per JSON file, in five
variants — `verdict`, `versus`, `board`, `overlay` (the same beats on a flat chroma green so a filmed clip can be
keyed in behind it), and `before-after`, which takes two of the app's own checks — the export from Settings, or two
saved `/api/checks/<id>` answers — and never a retyped number. Every end card carries the address, `orevosh.com`.
`teaser/` holds the two launch films, which are cut by hand and not regenerated from a JSON (they still say *coming*
*soon*; a follow-up). `node render-kit.js` rewrites this README from its file list, so this paragraph lives in
`writeReadme` in the script.

The templates ask Google Fonts for Outfit and Heebo and fall back to the OFL copies in `templates/fonts/` when the
machine is offline, so a run anywhere gives the real faces. This folder is not under `wwwroot` and is not served; the
two files the app serves are copied out by the script: `/brand/og-1200x630.png` (`og-1200x630-he.png` for the Hebrew
landing page) for link previews, and `/landing/screens/*.jpg` for the landing page.

## Before the store listings

- **Replace the screens.** The ten screenshots are the real app (Round 21) at the phone viewport, taken in a browser
  against a local server: the people, the brand, the counts and the timestamps are a test run's, the stylist is the
  test stub answering each of the three looks with a verdict written for that photo, and the camera is Chromium's
  fake device playing a still of the pink look. The check and camera screens exist in English only, so the Hebrew
  store set uses them for screens 1 and 5. Take real captures on a
  phone (1290×2796 on an iPhone with a 6.7" screen; any 9:16 Android phone), drop them into
  `tools/brand/templates/screens/`, point the `COPY` table in `render-kit.js` at them, and re-run. Apple rejects
  listings whose screenshots do not show the app as shipped.
- **The looks.** `look-2-camel` is the check, its result (9/10), the Hebrew tag page and Explore's look of the week;
  `look-1-streetwear` is the look's page and the Hebrew feed (8/10); `look-3-pink` is the look NEXOR featured, the
  Hebrew guest result (8/10) and the camera. The app crops an outfit to 4:5 from the centre, so each gives something
  up on a card: the camel look its face and the boots at the hem, the grey one its sneakers, the pink one the top of
  the fedora and the shoes.
- **Sizes.** Apple asks for 1284×2778 or 1290×2796 for the 6.7" slot (the files here are 1284×2778, accepted for 6.5"
  and 6.9" too); Google Play accepts 16:9 or 9:16 between 320 and 3840 px and wants the 1024×500 feature graphic.
- **Safe zones.** Facebook shows the cover at 820×312 on desktop and the middle 640 px on phones; LinkedIn puts the
  page logo over the bottom-left of the company cover; X puts the profile picture over the bottom-left of the header;
  Instagram covers the top and bottom 250 px of a story with its own chrome. The layouts keep the wordmark, the slogan
  and the phone inside those, but check once on a phone after uploading.

## logos/

| file | pixels | size | use |
|---|---|---|---|
| `mark-1024-dark.png` | 1024×1024 | 69 KB | The mark on the stage. App listings, press, anywhere the ground is dark |
| `mark-1024-dark-transparent.png` | 1024×1024 | 102 KB | The same mark with no ground, to place on dark photos and videos |
| `wordmark-2000-dark.png` | 2000×560 | 60 KB | The wordmark, 1840 px wide on a stage ground, for headers and decks |
| `lockup-2400x1200-dark.png` | 2400×1200 | 84 KB | Mark, wordmark, slogan and tagline together: press kits, the first slide, a printed card |
| `mark-1024-light.png` | 1024×1024 | 56 KB | The deeper pair on white, for light grounds (documents, light websites) |
| `mark-1024-light-transparent.png` | 1024×1024 | 79 KB | The light-ground pair with no ground, to place on light photos and paper |
| `wordmark-2000-light.png` | 2000×560 | 54 KB | The wordmark, 1840 px wide on a white ground, for headers and decks |
| `lockup-2400x1200-light.png` | 2400×1200 | 79 KB | Mark, wordmark, slogan and tagline together: press kits, the first slide, a printed card |
| `avatar-1024.png` | 1024×1024 | 42 KB | Profile picture for Instagram, TikTok, X, Facebook, LinkedIn: the mark centred on the stage, safe for a circular crop |
| `mark-mono-white.svg` | 512×512 (vector) | 1 KB | The mark in one colour (#ffffff) for print, embroidery, engraving, watermarks |
| `mark-mono-white.png` | 1024×1024 | 27 KB | The same, as a transparent PNG 1024 wide |
| `wordmark-mono-white.svg` | 1626×350 (vector) | 2 KB | The wordmark in one colour (#ffffff) for print, embroidery, engraving, watermarks |
| `wordmark-mono-white.png` | 2000×431 | 35 KB | The same, as a transparent PNG 2000 wide |
| `mark-mono-black.svg` | 512×512 (vector) | 1 KB | The mark in one colour (#000000) for print, embroidery, engraving, watermarks |
| `mark-mono-black.png` | 1024×1024 | 24 KB | The same, as a transparent PNG 1024 wide |
| `wordmark-mono-black.svg` | 1626×350 (vector) | 2 KB | The wordmark in one colour (#000000) for print, embroidery, engraving, watermarks |
| `wordmark-mono-black.png` | 2000×431 | 31 KB | The same, as a transparent PNG 2000 wide |

## covers/

| file | pixels | size | use |
|---|---|---|---|
| `facebook-linkedin-1500x500.png` | 1500×500 | 212 KB | Facebook page cover (shown 820×312 on desktop, the middle 640 px on phones) and a LinkedIn personal banner |
| `linkedin-1584x396.png` | 1584×396 | 157 KB | LinkedIn company page cover (keep the left 20% clear of anything vital: the logo sits there on phones) |
| `play-feature-1024x500.png` | 1024×500 | 196 KB | Google Play feature graphic (required for the listing; shown above the screenshots) |
| `x-header-1500x500.png` | 1500×500 | 212 KB | X / Twitter header (the profile picture covers the bottom-left corner) |
| `og-1200x630.png` | 1200×630 | 274 KB | Open Graph / Twitter card for links to the app; served at /brand/og-1200x630.png |
| `og-1200x630-he.png` | 1200×630 | 270 KB | The same in Hebrew, for the Hebrew landing page; served at /brand/og-1200x630-he.png |

## stories/

| file | pixels | size | use |
|---|---|---|---|
| `story-1-check-the-look-en.png` | 1080×1920 | 635 KB | The slogan with the check screen (English), Instagram / TikTok story or Reel cover |
| `story-2-the-one-tip-en.png` | 1080×1920 | 683 KB | The result screen and the one tip (English), Instagram / TikTok story or Reel cover |
| `story-3-brands-en.png` | 1080×1920 | 834 KB | A look a brand has just featured (English), Instagram / TikTok story or Reel cover |
| `story-1-check-the-look-he.png` | 1080×1920 | 563 KB | The slogan with the feed (Hebrew), Instagram / TikTok story or Reel cover |
| `story-2-the-one-tip-he.png` | 1080×1920 | 732 KB | The result screen and the one tip (Hebrew), Instagram / TikTok story or Reel cover |
| `story-3-brands-he.png` | 1080×1920 | 734 KB | A look with a brand tag (Hebrew), Instagram / TikTok story or Reel cover |

## store/

| file | pixels | size | use |
|---|---|---|---|
| `iphone-6.7-01-en.png` | 1284×2778 | 1216 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 1: "Check the look in 10 seconds" |
| `iphone-6.7-02-en.png` | 1284×2778 | 1260 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 2: "Fit, color, accessories" |
| `iphone-6.7-03-en.png` | 1284×2778 | 1336 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 3: "Post it, light it up" |
| `iphone-6.7-04-en.png` | 1284×2778 | 792 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 4: "Brands feature the looks they love" |
| `iphone-6.7-05-en.png` | 1284×2778 | 1826 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 5: "Film it in the app" |
| `iphone-6.7-01-he.png` | 1284×2778 | 1204 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 1: "בודקים את הלוק ב-10 שניות" |
| `iphone-6.7-02-he.png` | 1284×2778 | 1323 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 2: "גזרה, צבע, אקססוריז" |
| `iphone-6.7-03-he.png` | 1284×2778 | 1076 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 3: "מפרסמים, מדליקים" |
| `iphone-6.7-04-he.png` | 1284×2778 | 1454 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 4: "מותגים מציגים את הלוקים שהם אוהבים" |
| `iphone-6.7-05-he.png` | 1284×2778 | 1831 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 5: "מצלמים בתוך האפליקציה" |
| `android-01-en.png` | 1080×1920 | 689 KB | Google Play phone screenshot (9:16), English listing, screen 1: "Check the look in 10 seconds" |
| `android-02-en.png` | 1080×1920 | 860 KB | Google Play phone screenshot (9:16), English listing, screen 2: "Fit, color, accessories" |
| `android-03-en.png` | 1080×1920 | 909 KB | Google Play phone screenshot (9:16), English listing, screen 3: "Post it, light it up" |
| `android-04-en.png` | 1080×1920 | 530 KB | Google Play phone screenshot (9:16), English listing, screen 4: "Brands feature the looks they love" |
| `android-05-en.png` | 1080×1920 | 1262 KB | Google Play phone screenshot (9:16), English listing, screen 5: "Film it in the app" |
| `android-01-he.png` | 1080×1920 | 681 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 1: "בודקים את הלוק ב-10 שניות" |
| `android-02-he.png` | 1080×1920 | 921 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 2: "גזרה, צבע, אקססוריז" |
| `android-03-he.png` | 1080×1920 | 583 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 3: "מפרסמים, מדליקים" |
| `android-04-he.png` | 1080×1920 | 1015 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 4: "מותגים מציגים את הלוקים שהם אוהבים" |
| `android-05-he.png` | 1080×1920 | 1266 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 5: "מצלמים בתוך האפליקציה" |

## social/

The pack for the two accounts, English first: the account is international and the host never speaks, so every
word is on-screen text in English and the Hebrew files are the Israeli launch track (`SOCIAL` at the top of the
social block in `render-kit.js`, same layouts, RTL).

- **The profile picture.** `instagram-avatar-1080.png` and `tiktok-avatar-1080.png` are the same file, candidate
  C (`AVATAR_PICK` in `render-kit.js`). Both apps crop to a circle, so the mark is sized and hung by its own ink
  circle — measured at (304.25, 234.5) r 251.184 in the 512 box, up and right of the box's centre because the flame
  flies out of the ring's top-right — not by the square it is drawn in. `avatar-test.png` is the sheet the pick was
  made on: the three candidates circle-cropped at 150 / 56 / 40 / 32 px on a dark row and on white, and the 32 px
  crop magnified 4× under them. `avatar-a/b/c.png` are kept so the choice can be re-made by eye.

- **The grid opener.** `grid-1.png` … `grid-9.png` are one 3240×3240 composition cut into nine.
  **Upload `grid-9` first, then 8, 7 … down to `grid-1`**: a profile stacks newest first, so uploaded in that order
  they land 1 2 3 across the top row and the wordmark reads straight across the middle. `grid-preview.png` shows
  the assembly. Every tile also stands alone — the corners carry the mark, a score ring, the tagline and the line
  about fire — so none of the nine is a wasted post.

- **TikTok's safe area.** TikTok covers the bottom 320 px with the caption, the handle, the sound bar and the
  buttons, and the right 180 px with the action rail. The covers, the link story and the face-reveal card keep every
  word and the logo inside a 700 px column centred on the canvas (x 190 → 890) with 210 px of air on top and 392 px
  at the bottom, in physical padding, so the Hebrew files clear the same strips as the English ones.
  `tiktok-safe-area.png` draws those two strips over a real cover — it is a guide, not a post.

- **The face reveal.** The follower target is one constant, `FACE_REVEAL_TARGET`, at the top of
  `templates/social.html`. Change that line and re-run `node render-kit.js social` to re-cut both cards at a
  different number. Announce the number up front and keep to it.

- Instagram posts are 1080×1350, the tallest it shows whole in the feed; a square crop of one of them loses the CTA,
  so post them as they are.

| file | pixels | size | use |
|---|---|---|---|
| `avatar-a.png` | 1080×1080 | 180 KB | Candidate A "tight": the mark in colour on the stage, its ink circle at 78% of the tile, optically centred — 119 px of margin inside the circular crop |
| `avatar-b.png` | 1080×1080 | 89 KB | Candidate B "gradient ground": the mark in white on the deeper lilac→rose pair, at A's size, so the pair only tests the ground — a light tile in a dark feed |
| `avatar-c.png` | 1080×1080 | 184 KB | Candidate C "ring bleed": the same mark on the stage at 88%, the ring's stroke 65 px from the crop's edge |
| `avatar-test.png` | 1440×1016 | 167 KB | The test the pick was made on: the three files circle-cropped at 150 / 56 / 40 / 32 px on a dark row and on white, and the 32 px crop magnified 4× underneath. Not for posting |
| `instagram-avatar-1080.png` | 1080×1080 | 184 KB | The Instagram profile picture: candidate C (AVATAR_PICK in render-kit.js). Upload at 1080; Instagram stores 320 and shows 150 / 56 / 32 |
| `tiktok-avatar-1080.png` | 1080×1080 | 184 KB | The TikTok profile picture: the same file (TikTok wants at least 200×200 and crops it to a circle too) |
| `grid-1.png` | 1080×1080 | 118 KB | Grid opener 1 of 9, top row start: the mark, small |
| `grid-2.png` | 1080×1080 | 129 KB | Grid opener 2 of 9, top row middle: the three things the stylist reads |
| `grid-3.png` | 1080×1080 | 123 KB | Grid opener 3 of 9, top row end: a score ring |
| `grid-4.png` | 1080×1080 | 201 KB | Grid opener 4 of 9, middle row start: the wordmark's own ring and flame |
| `grid-5.png` | 1080×1080 | 43 KB | Grid opener 5 of 9, middle row middle: the wordmark and the slogan |
| `grid-6.png` | 1080×1080 | 23 KB | Grid opener 6 of 9, middle row end: the end of the wordmark |
| `grid-7.png` | 1080×1080 | 32 KB | Grid opener 7 of 9, bottom row start: the tagline |
| `grid-8.png` | 1080×1080 | 39 KB | Grid opener 8 of 9, bottom row middle: the CTA |
| `grid-9.png` | 1080×1080 | 16 KB | Grid opener 9 of 9, bottom row end: the house line on reactions |
| `grid-preview.png` | 1080×1200 | 154 KB | The nine tiles assembled, with the upload order under them. Not for posting |
| `post-1-what-it-is-en.png` | 1080×1350 | 382 KB | First post 1 (English): what the app is |
| `post-2-the-one-tip-en.png` | 1080×1350 | 408 KB | First post 2 (English): the result and the one tip |
| `post-3-brands-en.png` | 1080×1350 | 435 KB | First post 3 (English): the brands |
| `post-1-what-it-is-he.png` | 1080×1350 | 351 KB | First post 1 (Hebrew): what the app is |
| `post-2-the-one-tip-he.png` | 1080×1350 | 408 KB | First post 2 (Hebrew): the result and the one tip |
| `post-3-brands-he.png` | 1080×1350 | 412 KB | First post 3 (Hebrew): the brands |
| `tiktok-cover-1-en.png` | 1080×1920 | 386 KB | TikTok cover 1 (English): the slogan over the check screen. Everything readable is out of the bottom 320 px and the right 180 px |
| `tiktok-cover-2-en.png` | 1080×1920 | 424 KB | TikTok cover 2 (English): the verdict. Everything readable is out of the bottom 320 px and the right 180 px |
| `tiktok-cover-1-he.png` | 1080×1920 | 357 KB | TikTok cover 1 (Hebrew): the slogan over the feed. Everything readable is out of the bottom 320 px and the right 180 px |
| `tiktok-cover-2-he.png` | 1080×1920 | 416 KB | TikTok cover 2 (Hebrew): the verdict. Everything readable is out of the bottom 320 px and the right 180 px |
| `tiktok-safe-area.png` | 1080×1920 | 428 KB | The same cover with TikTok's two unsafe strips drawn on it: what to keep clear when filming and when making a new cover. Not for posting |
| `story-link-en.png` | 1080×1920 | 194 KB | "Link in bio" story (English): a big arrow up at the profile chip, which sits at the start edge in that direction |
| `face-reveal-en.png` | 1080×1920 | 262 KB | The face-reveal promise (English): the target is the biggest thing on the card. Change FACE_REVEAL_TARGET in templates/social.html to move it |
| `story-link-he.png` | 1080×1920 | 182 KB | "Link in bio" story (Hebrew): a big arrow up at the profile chip, which sits at the start edge in that direction |
| `face-reveal-he.png` | 1080×1920 | 256 KB | The face-reveal promise (Hebrew): the target is the biggest thing on the card. Change FACE_REVEAL_TARGET in templates/social.html to move it |

Total: 34.6 MB.
