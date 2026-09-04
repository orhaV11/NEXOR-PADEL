# Browser smoke test

Drives the real client in a phone-sized Chromium against the real API. Only the Anthropic Messages API is
replaced, by `stub_anthropic.py`, which validates the request shape FitCheck sends (headers, forced tool
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
open page. The run has three people in separate browser contexts: Noa (a person, English), NEXOR (a brand,
English) and Dan (a person who mostly browses, Hebrew). It covers, in order: browsing signed out in Hebrew
and RTL, signup validation, a check with the client-side downscale, the result screen, posting to the feed,
feed tabs and the intent filter, reading a post signed out, a brand opening a challenge, fire, save, a
comment and a follow, the activity list and its badge, entering the challenge from its card with the intent
locked, voting in Hebrew, the "following" and "top" feeds, a brand post with product links, the challenge
ending (the database clock is moved) and the winner being fixed with both sides notified, photo privacy by
URL, deleting a post, the pilot metrics, deleting an account, and signing out and back in.
