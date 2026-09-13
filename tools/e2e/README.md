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

**Round 10 in the script.** The stub answers rubric v3: every item carries `brand_seen`, null everywhere except
`"Nike"` on the English running shoes, and a request whose item schema lacks `brand_seen` is refused. The run starts the
API with `Board__NewAccountDays=0`, `Board__MinChecksToCount=1` and `Board__CacheSeconds=0` (its accounts are minutes
old, and a fire must show on the board at once). What the script drives, in the order the features sit:

- **The items editor** on Noa's first post sheet (`#items-editor`): the stylist's rows `#items-list > li.items-row[data-source=Stylist]`,
  the running shoes carrying `.items-suggest[data-brand="Nike"]`; Confirm (`button[data-action=confirm]`), then
  "Place the dot" (`button.items-place` on the row found by its `data-key`) and a tap on `#items-photo`; the row gains
  `.placed` (screenshot 35). The post body then carries `items` with `brand: "Nike"`, `confirmed: true` and `x`/`y`.
- **The look items and the store door**: on the look page `#look-items li[data-item]` with the confirmed brand in the
  placed row's `.txt`; `#items-toggle` showing `#item-dots .item-dot[data-item]`; a dot opening `#item-sheet` naming the
  brand (screenshot 36); `GET /api/items?brand=nike` finding the look and `GET /api/items/brands?q=ni` listing Nike;
  the `#/items/Nike` page (screenshot 37). The store door itself (`/api/items/{id}/out`) is covered by `ItemOutTests`.
- **The board**: after Dan fires the featured look, `#/board` shows `.board-row[data-rank]` with `.rank-medal.top` on
  first place (screenshot 38), the People and Stylist's picks tabs render, `#board-hall-link` opens the hall in its
  empty state (no week has closed in the run), and Explore carries `#board-strip .board-strip-row a` (screenshot 39).
- **Hooks the review added**, not yet driven by the script: in an open editor row the brand field is
  `input[role=combobox]` with `aria-expanded` and `aria-controls`, and once ArrowDown has moved into the list
  `input[role=combobox][aria-activedescendant]` names the active `li[role=option]` (Enter picks it, Escape closes);
  on the item sheet `#item-disclosure` sits inside `#item-leaves` only while `/api/config` says `affiliate.disclosure`
  is true (the default; `#item-leaves` itself shows whenever the item has a link); `#items-search` carries
  `maxlength="40"` (the server's cap), and a term the server refuses draws `#items-results .alert[role=alert]` with its
  message instead of the empty state; `#board-previous`, the way to the week before in `.board-nav`, is 44px tall.
- **Not driven**: a closed week (the closer has no HTTP trigger), so the hall's weeks, `#profile-badge` and the
  `board_rank` line in Activity are covered by `BoardTests` only; the moderator's exclusion by `BoardTests` and
  `Round10SkeletonTests`; the reset card `#board-reset` shows only in the first 24 hours of a week.

Two things about timing: the sheet kit keeps a closing panel for 170 ms after Escape, so wait for `.sheet.closing` to
go before asserting on the page behind it; and `GET /api/items/brands` is called while a brand field is typed in (a
failure there is silent in the UI).
