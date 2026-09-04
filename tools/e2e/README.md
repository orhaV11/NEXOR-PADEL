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

Screenshots land in `shots/`. The run covers: Hebrew auto-detect and RTL, switching to English without a
reload, onboarding validation, upload with client-side downscale, the loading state, results in both
languages, history, the not-an-outfit state, photos being unreachable by URL, metrics, and deletion.
