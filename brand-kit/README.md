# OREVOSH brand kit

Everything needed to show OREVOSH outside the app: the logos, the covers for social pages and the stores, the story
templates and the store screenshots, rendered from `tools/brand/templates/` by `tools/brand/render-kit.js` with the
real brand SVGs (`src/FitCheck.Api/wwwroot/brand/`), the tokens of `DESIGN.md` and the app screenshots the browser
test takes. The slogan is **Check the look.** / **בודקים את הלוק.** with the tagline *A stylist in your pocket, and a
community that lights it up.* / *סטייליסט בכיס, וקהילה שמדליקה.* (`MARKETING.md` has the runners-up; the copy lives
at the top of `render-kit.js`, so changing it and re-running regenerates every file).

## Regenerate

```bash
cd tools/brand
npm install                      # playwright, and sharp for smaller PNGs
npx playwright install chromium  # once; or CHROMIUM_PATH=/path/to/chromium
node render-kit.js               # everything; or: node render-kit.js stories store
```

The templates ask Google Fonts for Outfit and Heebo and fall back to the OFL copies in `templates/fonts/` when the
machine is offline, so a run anywhere gives the real faces. This folder is not under `wwwroot` and is not served; the
two files the app serves are copied out by the script: `/brand/og-1200x630.png` (`og-1200x630-he.png` for the Hebrew
landing page) for link previews, and `/landing/screens/*.jpg` for the landing page.

## Before the store listings

- **Replace the screens.** The screenshots come from the browser test (`tools/e2e`): the outfit photo is the test's
  synthetic image, the camera shows Chromium's fake device (green), and the check and camera screens exist in English
  only, so the Hebrew store set uses them for screens 1 and 5. Take real captures on a phone (1290×2796 on an iPhone
  with a 6.7" screen; any 9:16 Android phone), drop them into `tools/brand/templates/screens/`, point the `COPY` table
  in `render-kit.js` at them, and re-run. Apple rejects listings whose screenshots do not show the app as shipped.
- **Sizes.** Apple asks for 1284×2778 or 1290×2796 for the 6.7" slot (the files here are 1284×2778, accepted for 6.5"
  and 6.9" too); Google Play accepts 16:9 or 9:16 between 320 and 3840 px and wants the 1024×500 feature graphic.
- **Safe zones.** Facebook shows the cover at 820×312 on desktop and the middle 640 px on phones; LinkedIn puts the
  page logo over the bottom-left of the company cover; X puts the profile picture over the bottom-left of the header;
  Instagram covers the top and bottom 250 px of a story with its own chrome. The layouts keep the wordmark, the slogan
  and the phone inside those, but check once on a phone after uploading.

## logos/

| file | pixels | size | use |
|---|---|---|---|
| `mark-1024-dark.png` | 1024×1024 | 26 KB | The mark on the stage. App listings, press, anywhere the ground is dark |
| `mark-1024-dark-transparent.png` | 1024×1024 | 26 KB | The same mark with no ground, to place on dark photos and videos |
| `wordmark-2000-dark.png` | 2000×560 | 22 KB | The wordmark, 1840 px wide on a stage ground, for headers and decks |
| `lockup-2400x1200-dark.png` | 2400×1200 | 30 KB | Mark, wordmark, slogan and tagline together: press kits, the first slide, a printed card |
| `mark-1024-light.png` | 1024×1024 | 24 KB | The deeper pair on white, for light grounds (documents, light websites) |
| `mark-1024-light-transparent.png` | 1024×1024 | 25 KB | The light-ground pair with no ground, to place on light photos and paper |
| `wordmark-2000-light.png` | 2000×560 | 22 KB | The wordmark, 1840 px wide on a white ground, for headers and decks |
| `lockup-2400x1200-light.png` | 2400×1200 | 30 KB | Mark, wordmark, slogan and tagline together: press kits, the first slide, a printed card |
| `avatar-1024.png` | 1024×1024 | 16 KB | Profile picture for Instagram, TikTok, X, Facebook, LinkedIn: the mark centred on the stage, safe for a circular crop |
| `mark-mono-white.svg` | 512×512 (vector) | 1 KB | The mark in one colour (#ffffff) for print, embroidery, engraving, watermarks |
| `mark-mono-white.png` | 1024×1024 | 15 KB | The same, as a transparent PNG 1024 wide |
| `wordmark-mono-white.svg` | 1626×350 (vector) | 2 KB | The wordmark in one colour (#ffffff) for print, embroidery, engraving, watermarks |
| `wordmark-mono-white.png` | 2000×431 | 21 KB | The same, as a transparent PNG 2000 wide |
| `mark-mono-black.svg` | 512×512 (vector) | 1 KB | The mark in one colour (#000000) for print, embroidery, engraving, watermarks |
| `mark-mono-black.png` | 1024×1024 | 15 KB | The same, as a transparent PNG 1024 wide |
| `wordmark-mono-black.svg` | 1626×350 (vector) | 2 KB | The wordmark in one colour (#000000) for print, embroidery, engraving, watermarks |
| `wordmark-mono-black.png` | 2000×431 | 21 KB | The same, as a transparent PNG 2000 wide |

## covers/

| file | pixels | size | use |
|---|---|---|---|
| `facebook-linkedin-1500x500.png` | 1500×500 | 29 KB | Facebook page cover (shown 820×312 on desktop, the middle 640 px on phones) and a LinkedIn personal banner |
| `linkedin-1584x396.png` | 1584×396 | 24 KB | LinkedIn company page cover (keep the left 20% clear of anything vital: the logo sits there on phones) |
| `play-feature-1024x500.png` | 1024×500 | 30 KB | Google Play feature graphic (required for the listing; shown above the screenshots) |
| `x-header-1500x500.png` | 1500×500 | 29 KB | X / Twitter header (the profile picture covers the bottom-left corner) |
| `og-1200x630.png` | 1200×630 | 39 KB | Open Graph / Twitter card for links to the app; served at /brand/og-1200x630.png |
| `og-1200x630-he.png` | 1200×630 | 33 KB | The same in Hebrew, for the Hebrew landing page; served at /brand/og-1200x630-he.png |

## stories/

| file | pixels | size | use |
|---|---|---|---|
| `story-1-check-the-look-en.png` | 1080×1920 | 91 KB | The slogan with the check screen (English), Instagram / TikTok story or Reel cover |
| `story-2-the-one-tip-en.png` | 1080×1920 | 79 KB | The result screen and the one tip (English), Instagram / TikTok story or Reel cover |
| `story-3-brands-en.png` | 1080×1920 | 86 KB | A look with a brand tag (English), Instagram / TikTok story or Reel cover |
| `story-1-check-the-look-he.png` | 1080×1920 | 71 KB | The slogan with the check screen (Hebrew), Instagram / TikTok story or Reel cover |
| `story-2-the-one-tip-he.png` | 1080×1920 | 78 KB | The result screen and the one tip (Hebrew), Instagram / TikTok story or Reel cover |
| `story-3-brands-he.png` | 1080×1920 | 83 KB | A look with a brand tag (Hebrew), Instagram / TikTok story or Reel cover |

## store/

| file | pixels | size | use |
|---|---|---|---|
| `iphone-6.7-01-en.png` | 1284×2778 | 180 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 1: "Check the look in 10 seconds" |
| `iphone-6.7-02-en.png` | 1284×2778 | 144 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 2: "Fit, color, accessories" |
| `iphone-6.7-03-en.png` | 1284×2778 | 174 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 3: "Post it, light it up" |
| `iphone-6.7-04-en.png` | 1284×2778 | 140 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 4: "Brands feature the looks they love" |
| `iphone-6.7-05-en.png` | 1284×2778 | 59 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), English listing, screen 5: "Film it in the app" |
| `iphone-6.7-01-he.png` | 1284×2778 | 175 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 1: "בודקים את הלוק ב-10 שניות" |
| `iphone-6.7-02-he.png` | 1284×2778 | 129 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 2: "גזרה, צבע, אקססוריז" |
| `iphone-6.7-03-he.png` | 1284×2778 | 178 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 3: "מפרסמים, מדליקים" |
| `iphone-6.7-04-he.png` | 1284×2778 | 174 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 4: "מותגים מציגים את הלוקים שהם אוהבים" |
| `iphone-6.7-05-he.png` | 1284×2778 | 60 KB | App Store, iPhone 6.7" (also accepted for 6.5" and 6.9"), Hebrew listing, screen 5: "מצלמים בתוך האפליקציה" |
| `android-01-en.png` | 1080×1920 | 98 KB | Google Play phone screenshot (9:16), English listing, screen 1: "Check the look in 10 seconds" |
| `android-02-en.png` | 1080×1920 | 80 KB | Google Play phone screenshot (9:16), English listing, screen 2: "Fit, color, accessories" |
| `android-03-en.png` | 1080×1920 | 95 KB | Google Play phone screenshot (9:16), English listing, screen 3: "Post it, light it up" |
| `android-04-en.png` | 1080×1920 | 79 KB | Google Play phone screenshot (9:16), English listing, screen 4: "Brands feature the looks they love" |
| `android-05-en.png` | 1080×1920 | 37 KB | Google Play phone screenshot (9:16), English listing, screen 5: "Film it in the app" |
| `android-01-he.png` | 1080×1920 | 97 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 1: "בודקים את הלוק ב-10 שניות" |
| `android-02-he.png` | 1080×1920 | 73 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 2: "גזרה, צבע, אקססוריז" |
| `android-03-he.png` | 1080×1920 | 103 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 3: "מפרסמים, מדליקים" |
| `android-04-he.png` | 1080×1920 | 99 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 4: "מותגים מציגים את הלוקים שהם אוהבים" |
| `android-05-he.png` | 1080×1920 | 35 KB | Google Play phone screenshot (9:16), Hebrew listing, screen 5: "מצלמים בתוך האפליקציה" |

Total: 3.1 MB.
