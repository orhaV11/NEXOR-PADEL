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
browsing signed out in Hebrew and RTL; signup with only handle, password and 16+; the welcome screen (styles,
brands to follow); settings (brand mode, display name, avatar upload and the avatar route's cache header); a
check with the client-side downscale; posting with `#tags` and `@mentions`; the post page with its tagged
accounts; the mention notification; a brand featuring the look and its Community and Featured tabs; the
creator's notification and Featured tab; Explore (trending tag, brand card, top look), search and the tag page;
signing up in Hebrew and following a brand from the welcome screen; the For you feed and double-tap to fire;
the Following feed; a challenge from brief to winner reached through Explore, with the winning entry featured
through the challenge route; photo privacy by URL; the pilot metrics; deleting a look and an account; signing
out and back in. The service worker is blocked in the test contexts so it never masks a live request.
