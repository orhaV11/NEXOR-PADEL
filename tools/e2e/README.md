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
