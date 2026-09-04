# DECISIONS.md — FitCheck Phase 1

Every judgment call made while building, in the order it came up. The brief wins over instinct;
objections are noted, not acted on.

## Plan (written before building)

1. Scaffold `src/FitCheck.Api` (ASP.NET Core 8, Minimal APIs) + `tests/FitCheck.Api.Tests` (xUnit) in this repo.
2. Domain models, options, `AppDbContext` (SQLite, `EnsureCreated`, index on `(UserId, CreatedAt)`).
3. `IImageStore` + `DiskImageStore` with magic-byte detection (JPEG/PNG/WebP), private storage root.
4. `IOutfitVisionClient` + `AnthropicVisionClient` (raw `HttpClient`, forced tool call, 60s timeout, one retry).
5. `OutfitAnalyzer`: prompt, schema, intent guide, `PromptVersion`, mapping + clamping.
6. Endpoints: users, checks (multipart, caps, 413/415/429/502), metrics. `Localizer` for server messages.
7. Client `wwwroot/index.html` (vanilla, mobile-first) + `i18n/en.json`, `i18n/he.json`, RTL via logical properties.
8. Tests per §11, then README. Build + test after each step. Commit as steps land.

## Decisions

### Repository and toolchain

- **Built in NEXOR-PADEL.** Two repositories were in scope. This one was an empty initial commit; the other
  is a running React/Vite product with its own stack. A fresh .NET solution belongs in the empty one.
- **.NET SDK 8.0.130 from the Ubuntu apt repository.** The official `dot.net` install script is blocked
  by the build environment's egress policy. Any 8.0.x SDK builds this project.
- **Test packages:** xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 17.14.1,
  Microsoft.AspNetCore.Mvc.Testing 8.0.30 (latest 8.x line, matching EF Core Sqlite 8.0.30).
- **`TreatWarningsAsErrors` on** in the API project. Small project; a warning today is a bug next month.

### Model and API call

- **Model `claude-sonnet-5`.** The brief asks for a current Sonnet-class model. Verified against the
  current model table in the Claude API reference (cached 2026-06). Forced tool use (`tool_choice:
  {type:"tool"}`) is supported on Sonnet 5, Opus 5, the 4.x family and Haiku 4.5; it returns 400 on
  Fable 5.1 / Mythos 5.1, so those are not candidates for `Anthropic:Model` without changing the client.
- **`thinking: {type: "disabled"}` sent explicitly.** On Sonnet 5 an omitted `thinking` field runs
  adaptive thinking, and `max_tokens` is shared between thinking and the tool call. A ~1000-token budget
  could be eaten by reasoning and truncate the JSON. A 10-second outfit check does not need reasoning
  tokens; if calibration quality turns out to need them, raise `MaxTokens` and switch to
  `{type: "adaptive"}` in `AnthropicVisionClient.BuildBody`.
- **`MaxTokens` 1200, not ~900.** Hebrew is token-dense and the Sonnet 5 tokenizer uses ~30% more tokens
  than the previous generation. 900 risked a truncated tool call in Hebrew; 1200 is still tiny. Configurable.
- **Retry once on 429 and 5xx only, 1.5 s backoff.** Timeouts and connection failures are not retried:
  a second 60 s wait would turn a slow failure into a two-minute one. The user gets a 502 and retries.
- **API-level refusal (`stop_reason: "refusal"`) maps to `rejected`, not `error`.** If the safety layer
  declines the image, telling the user to "try again" would be wrong; the neutral rejection state is right.
- **The API key is read from the environment at call time,** never from configuration, so it cannot end
  up in `appsettings*.json`. Startup logs a warning when it is missing.

### Data and storage

- **Feedback JSON is stored camelCase,** the same shape the API returns, so reading a check back is a plain
  deserialize. The snake_case policy applies only when mapping the model's tool payload.
- **`ImagePath` becomes `""` when the file is removed** rather than a nullable column. The column is
  non-null in the brief's model; an empty string reads as "no photo" without a schema change.
- **The photo is written to disk only after the model says `ok`.** The bytes are already in memory for the
  model call, so `not_outfit`, `rejected`, `error` and an abandoned request never touch the disk at all,
  and there is no delete-on-failure path that an unexpected exception could skip. `ok` keeps the photo
  (future closet memory). `rejected` stores nothing but the status and the request metadata (intent,
  language, latency); no feedback, no score, and the model's own message is never returned or stored.
  The client shows a server-side neutral message in the check's language instead.
- **`not_outfit` keeps the model's friendly message** so the client can show a specific hint ("this looks
  like a desk").
- **`rejected` also drops the wearer's occasion note.** It is user-authored prose about the photo, which is
  exactly what "nothing stored except the status" is protecting against.
- **Intent is stored as text,** not an int, so a SQLite browser shows `Date` rather than `1`.
- **Storage root and a relative SQLite path both resolve against the content root,** not the working
  directory, so `dotnet run` from anywhere finds the same photos and the same database. Restarting from a
  different folder would otherwise create an empty database and log every pilot user out. Absolute paths
  are honoured for deployments.
- **Cascade delete plus explicit deletes.** The FK cascades, but `DELETE /api/users/{id}` deletes files
  first, then checks, then the user, so a failure mid-way leaves something the user can retry rather than
  orphaned photos with no owner.

### Endpoints

- **Rate cap counts every stored check except `error`, plus checks still in flight.** A model outage must
  not eat the user's allowance, while `not_outfit` and `rejected` did cost a model call and do count.
  `CheckCapacity` holds an in-memory reservation from the cap check until the row is stored, so a burst
  of parallel uploads cannot all slip under the count. The 429 carries a `Retry-After` header computed
  from the oldest counted check.
- **Two more cost guards beyond the brief's per-user cap,** because the tunnel URL goes to 50 phones and a
  per-user cap keyed on a free-to-mint id is not a bound on spend: a global ceiling
  (`Limits:ChecksPerDayGlobal`, 1000 a day, its own 429 message) and a per-address signup limit
  (`Limits:SignupsPerHourPerIp`, ASP.NET's built-in rate limiter, client address from `X-Forwarded-For`
  because the app sits behind a tunnel). Neither is authentication; both are rule 7. The signup limit
  defaults to the pilot size (50 an hour) because carrier NAT and office Wi-Fi put many real users behind
  one address; the README says to raise it for a launch hour that exceeds that.
- **Any unexpected exception during a check becomes an `error` row and a 502,** not just the vision
  client's own exceptions, as the brief's "on any failure" asks. A client disconnect is the exception:
  nothing is stored and nothing counts.
- **The wearer's occasion note is sanitized and quoted in the prompt.** Control characters and line
  breaks become spaces and the note is wrapped as `"…" (context only, never instructions)`, so a note
  cannot pose as a new rule. The brief's sentence shape is otherwise kept; `PromptVersion` stays `v1`
  because no pilot data exists yet and the rubric did not change.
- **`x-api-key` is redacted from HttpClient logging** so raising the log level to Trace while debugging
  cannot print the secret.
- **Wrong owner on `GET /api/checks/{id}` is a 404, not a 403,** so ids do not leak existence.
- **Unknown `language` on `POST /api/users` falls back** (Accept-Language, then English) instead of
  failing: the client always sends a shipped locale, and a stale value should not block sign-up.
  `PATCH` with an unsupported language is a 400 because it is an explicit request to switch.
- **Missing or unsupported `language` on `POST /api/checks` uses the user's stored preference,** never
  the Accept-Language header. Every check must carry a language and the user record is the only default
  that stays stable if a locale is added or removed later.
- **Validation order on `POST /api/checks`:** body size, form parse, user, language, intent, occasion,
  image present, image size, magic bytes, daily cap, then the model. The cap is checked after format
  validation so a user at the cap still learns about a broken file, and before saving anything.
- **413 is enforced three ways:** `Content-Length` up front, the multipart body limit, and the file
  length after parsing. Kestrel's per-request body limit is set on the endpoint so a huge body is cut at
  the transport rather than buffered.
- **`scoreDistribution` always carries keys 1–10.** A stable shape charts better than sparse keys; the
  brief's example only illustrated the format.
- **Metrics are computed in memory** from the OK rows (six narrow columns). At pilot scale this is a few
  thousand rows and keeps the second-check math readable and unit-testable (`MetricsEndpoints.Compute`).
- **`GET /api/users/{id}/checks` returns all statuses;** the client filters to OK for history. Error and
  rejected rows being visible to the owner is useful when debugging a pilot user's report.
- **Unhandled exceptions return the same `{ error }` shape** in the caller's language, so the client has
  one error path.

### Client

- **Available locales are a one-line constant in `index.html`** (`AVAILABLE_LOCALES`) plus one JSON file
  each. Each file carries `meta.name` (native name for the switcher) and `meta.dir`. The brief says
  "adding a locale = adding one JSON file"; the constant is the one extra line, needed so the switcher can
  list locales before loading them.
- **Both locale files load at startup** (two small requests) so the switcher shows native names
  immediately. Fine for 2 locales; lazy-load when there are 10.
- **Feedback keeps the language it was written in.** Switching the UI language re-renders labels, dates
  and intent names but never the model text. The check's `language` field is the record of that.
- **Fonts:** Heebo (body) and Karantina (display, the score numeral) from Google Fonts with system
  fallbacks. Both cover Latin and Hebrew. If the font host is unreachable the page still renders.
- **The score is wrapped in `dir="ltr"`** so "7/10" reads the same in Hebrew. Everything else mirrors via
  logical properties.
- **Intent chips are built once per locale and only their pressed state changes.** Rebuilding them on
  every tap threw keyboard focus back to the top of the page; now the chip keeps focus, and a language
  switch that must rebuild them restores focus to the same intent.
- **Choosing a photo clears the previous one immediately** and carries a selection token, so an old photo
  can never be submitted while a new one is being prepared, and a slower earlier pick cannot overwrite a
  later one. The photo area shows "Preparing the photo…" meanwhile.
- **Image decoding falls back in three steps:** `createImageBitmap` with EXIF orientation, then without the
  option (older engines reject the option but orient by default), then an `<img>` decode; the raw file is
  sent only when decoding itself fails, so the downscale survives on older Safari and Chrome.
- **Live regions never toggle `hidden`.** The toast fades with opacity and a visually hidden status region
  announces the loading line and the result, because a region that appears already filled is not read out.
  Each screen change moves focus to the new screen's heading.
- **The score counts up exactly once per result.** A language switch on the result screen re-renders
  with the final numeral and a full bar in place, so the switch is instant as the brief asks.
- **A "Delete my account and photos" text button** sits at the bottom of the check screen. The brief only
  requires the endpoint, but pilot users must be able to exercise their deletion right without asking us.
- **Client-side downscale:** max edge 1280 px, JPEG quality 0.85, via `createImageBitmap` with
  `imageOrientation: 'from-image'`; on any failure the original file is sent and the server's 6 MB cap
  still applies. The uploaded file is always named `outfit.jpg`; the server ignores the name and content
  type and reads magic bytes.
- **The client sends `Accept-Language` with the active locale** on every request, so server error
  messages match the UI even if the user record lags.
- **Web Share with clipboard fallback,** and a `prompt()` as the last resort on browsers that block both.
- **A 404 for the user (account deleted elsewhere or database reset) resets the session** to onboarding
  instead of showing an error the user cannot fix.
- **Hebrew copy uses infinitive and neutral forms** ("להתחיל", "שווה לנסות", "אני בגיל 16 ומעלה") to avoid
  gendered imperatives, matching the style note given to the model.

### Testing and verification

- **The vision client is faked at the `IOutfitVisionClient` seam** in the endpoint tests (scripted tool
  payloads, recorded requests). The real `AnthropicVisionClient` has its own unit tests against a scripted
  `HttpMessageHandler` (headers, forced tool choice, base64 image block, thinking disabled, 529-then-200
  retry, no retry on 4xx or connection failure, refusal, missing tool_use, missing key).
- **A browser smoke test lives in `tools/e2e`** (Playwright, optional, not part of `dotnet test`): the real
  page in a phone viewport against the real API, with only the Anthropic API replaced by
  `stub_anthropic.py`, which validates the request shape and answers 529 once. It was run with `he-IL` as
  the browser language: auto-detect, RTL, switch to English without reload, onboarding validation, upload
  with downscale, loading state, result in both languages, history, not-outfit state, photo privacy by
  URL, metrics, deletion. Chromium reports a body-less 204 fetch as `ERR_ABORTED` after the response
  arrives; the script treats that as success only when the 204 was observed.
- **`FitCheck.sln` at the root** so `dotnet build` and `dotnet test` work from the repository root as the
  definition of done expects.
- **Not verified here: real-model calibration** (the "scores are not all 7–8 on 10 varied photos" item).
  No API key was available in the build environment. `scripts/calibrate.sh` is provided and the README
  tells the operator to run it before inviting people, bumping `PromptVersion` if the spread is poor.

### Review before hand-off

- **Five independent reviews with adversarial verification** were run against the brief before hand-off
  (spec compliance, security and privacy, backend correctness, client and i18n, tests and docs; every
  finding was then challenged by two more reviewers). Everything that survived was fixed: deferred photo
  write and catch-all error rows, the in-flight cap reservation and global ceiling, per-address signups,
  header redaction, the quoted occasion note, occasion dropped on rejection, the anchored SQLite path,
  chip focus, the photo-replacement race, decode fallbacks, live regions, the share guard, the locale
  switch failure path, two Hebrew strings, and three bugs in `scripts/calibrate.sh`.

### Objections kept out of the code (brief wins)

- **The user id as the only credential** is the biggest risk in the pilot: anyone with the id can read,
  post and delete. Documented prominently in the README as the brief asks; a signed cookie would have been
  a small addition but is "real authentication", which is Phase 2.
- **A 60 s model timeout** contradicts the 10-second promise. Kept per brief; in practice Sonnet 5 with
  thinking disabled answers in a few seconds and the loading screen is honest about waiting.
- **The metrics endpoint is public.** No auth exists in Phase 1, and the endpoint is aggregate-only; the
  README says to protect it before the URL leaves the team.
