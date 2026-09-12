# Browser smoke test

Drives the real OREVOSH client in a phone-sized Chromium against the real API. Only the Anthropic Messages API
is replaced, by `stub_anthropic.py`, which validates the request shape the server sends (headers, forced tool
call, base64 image block), answers 529 once to exercise the retry, and returns feedback in the requested
language.

```bash
dotnet build                      # from the repository root
cd tools/e2e
npm install
npx playwright install chromium   # once
node e2e.js
```

If a Chromium is already installed somewhere else, skip `playwright install` and point the script at it with
`CHROMIUM_PATH=/path/to/chromium node e2e.js`.

Screenshots land in `shots/`; on a failure the script also writes `failed-<person>.png` and `.html` for each
open page. Three people take part in separate browser contexts: Noa (a person, English), NEXOR (a brand,
English) and Dan (mostly browsing, Hebrew). The run covers, in order: the manifest, service worker and icons;
browsing signed out in Hebrew and RTL; a guest's check before any signup (below); signup with handle, password and a
date of birth; the welcome screen (styles,
brands to follow); settings (brand mode, display name, avatar upload and the avatar route's cache header); a
check with the client-side downscale; posting with `#tags` and `@mentions`; the post page with its tagged
accounts; the mention notification; a brand featuring the look and its Community and Featured tabs; the
creator's notification and Featured tab; Explore (trending tag, brand card, top look), search and the tag page;
signing up in Hebrew and following a brand from the welcome screen; the For you feed and double-tap to fire;
the Following feed; a challenge from brief to winner reached through Explore, with the winning entry featured
through the challenge route; photo privacy by URL; the pilot metrics; the in-app camera against Chromium's fake
device (a photo, then a clip recorded in clip mode, its frame picked with the slider, the still judged, the story
card drawn, the clip posted, streamed with Range and shown in the feed with its pill); the moderation queue (a
report with a reason picked from the list, hide, show again, suspend and lift; Noa is promoted with the `--admin`
command after she signs up, the way a real owner is); the guidelines page; the push switch on a server without
VAPID keys; `/healthz`, `/api/config` and the security headers; deleting a look and an account; signing out and
back in. The service worker is blocked in the test contexts so it never masks a live request; the browser is
launched with `--use-fake-device-for-media-stream` so `getUserMedia` and `MediaRecorder` run for real. The Round 8 steps add:
the rubric v2 rings and the accessories read on the result and the look page (the stub answers 7/8/4, "Nothing on",
"A thin black leather belt."); the wait for the clip the browser recorded as WebM to come back as H.264 MP4 (ffmpeg
must be on the machine; `/api/config` says `transcoding: true`); account recovery with `Email__Host=log`, so the
confirmation and reset links are read from `data/api.log`; and the pilot metrics read through a moderator's session.

The Round 9 steps, in the order they run: Dan, signed out and in Hebrew, gets the guest banner on the check screen,
checks a look as a guest, sees "Sign up to keep it and post it" instead of Post it, and a second guest check from the
same browser is refused with 429 (one free look per guest); Noa's signup fails without a date of birth ("Add your date
of birth.") and the agreement line carries the terms and privacy links; the server runs with `Plans__FreeChecksPerDay=5`
so the cap line reads "5 of 5 checks left today" and `/api/config` reports the plan; when Dan signs up his guest check
is claimed and shows in his history, and the storage folder holds it under his account; the post sheet offers Noa's
earlier look under "After the tip" and the card shows the strip; "Which one?" with two photos, the stub picking B and
the winner frame on side B; the insights page from Noa's profile; a search by piece (`#/search/running`) finding looks
by the stylist's item names; the Pro page in its manual state, `--pro noa 1` run the way an owner runs it, `me` saying
`pro`, the plan row in Settings and the current-plan card; the Today strip on For you, "Post yours" prefilling the tag,
and the `#/today` page; `--verify nexor` putting the check inside the brand mark on the profile and on cards and
`--unverify` taking it away; the numbers page for the moderator and the refusal for a person; and the terms and privacy
pages with ten sections each, in Hebrew and in English. A clip load that Chromium aborts when a card is redrawn is not
counted as a failed request.

**Round 10 is not in the script yet.** The stub already answers rubric v3: every item carries `brand_seen`, null
everywhere except `"Nike"` on the English running shoes, and a request whose item schema lacks `brand_seen` is refused
the way one without the v2 fields is. The builders verified the surfaces by hand in Chromium (against the stub, and
against `page.route` mocks where the other half had not landed); nothing in `e2e.js` drives them. What a Round 10 run
has to add, in the order the features sit, with the hooks the views expose:

- **The items editor** on Noa's post sheet after the check (`#items-editor`): the stylist's three rows as `#items-list >
  li.items-row[data-source=Stylist]`, the running shoes carrying `.items-suggest[data-brand="Nike"]` with
  `button[data-action=confirm|edit|dismiss]`; Confirm, then "Place the dot" (`.items-place`), a tap on `#items-photo
  .items-photo-tap` and a drag of `.item-dot[data-key]`; `#items-add` for a fourth row with a name, a category chip
  (`.chip[data-category]`), a brand (the listbox `ul.items-brands` answers from `GET /api/items/brands?q=`), a model and
  a store link; then the `POST /api/posts` body carrying `items` with `brand: "Nike"`, `confirmed: true` and `x`/`y` on
  the shoes and `brand: null` on the rest. "Not a brand" sends no brand at all.
- **The look items and the store door**: on the look page `#look-items` with a `li[data-item]` per piece and the feed
  card's `.item-count` pill; `#items-toggle` (only when a piece has a dot) showing `.item-dot[data-item]`; a dot or a
  row opening `#item-sheet` with `#item-shop[href="/api/items/<id>/out"][target=_blank][rel=noopener]` reading "Shop at
  <host>" and `#item-leaves` under it; the owner's `#items-edit` opening `#items-sheet` with the editor over the
  existing rows, `#items-save` sending `PATCH /api/posts/{id}/items` with the ids kept. The door: the stub cannot serve
  the store, so never follow it; assert the 302 with `page.request.get(url, { maxRedirects: 0 })` (or a `fetch` with
  `redirect: 'manual'`): `Location` is the stored link plus `?tag=…` when the API runs with
  `Affiliate__Hosts__<host>=tag=…`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store`, and `social.itemOuts`
  in the metrics counts the tap.
- **The item pages**: `#/items/nike/shoes` titled "Looks with Nike · Shoes" with `#items-cats
  a.chip[data-category=shoes][aria-pressed=true]`, `#items-grid`, and `#items-more` when a brand account of that name
  exists; the search rule `#items-search` to `#/items?q=boots` and the empty state.
- **The board**: `#/board` with `#board-tabs .segment[data-tab=looks|people|rising|intent|picks]`, `#board-panel`,
  `#board-intents .chip[data-intent]`, `#board-closes`, `#board-me` when placed, `.board-row[data-rank]` with
  `.rank-medal.top` on the first three and `.board-fires`, `#board-previous` for the week before (`?week=`). A fire
  counts only from someone with an `ok` check and an account `Board__NewAccountDays` old, and the test's accounts are
  minutes old: start the API with `Board__NewAccountDays=0` for the run, and fire from Dan (his guest check is his once
  claimed). The server caches a computed week for 60 seconds, so read the board once after the fires, or exclude and
  re-include a look to drop the cache. The closer has no HTTP trigger, so a closed week, the hall (`#hall` with
  `.hall-week`, `.hall-tile`, `.hall-gone`, `.hall-person`), the badge (`#profile-badge` inside `h1.name`) and the
  `board_rank` line in Activity need either a `WeeklyWinners` row seeded by hand for last week or a test host calling
  `BoardCloser.CloseDueWeeksAsync` with the clock moved; the moderator's exclusion (`POST /api/admin/board/exclude`,
  `DELETE …/exclude/{postId}`) is Noa's, through the API with the CSRF header.
- **The Explore strip and the reset card**: `#board-strip` right after the search form with three `.board-strip-row a`
  once the board has three looks, absent while it is empty; `#board-reset` with `#board-reset-dismiss` on For you only
  in the first 24 hours of a week, and gone for the week once dismissed.

Two things about timing: the sheet kit keeps a closing panel for 170 ms after Escape, so wait for `.sheet.closing` to
go before asserting on the page behind it; and `GET /api/items/brands` is called while a brand field is typed in (a
failure there is silent in the UI).
