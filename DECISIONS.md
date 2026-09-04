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
  No API key was available in the build environment. `scripts/calibrate.py` is provided (Python 3
  standard library, so it runs on a Mac, Linux or Windows laptop) and the README tells the operator to
  run it before inviting people. Beyond the score spread it scans every feedback text for body, face,
  age and gender words in both languages, because rule 1 can only be checked on real model output.

### Review before hand-off

- **Five independent reviews with adversarial verification** were run against the brief before hand-off
  (spec compliance, security and privacy, backend correctness, client and i18n, tests and docs; every
  finding was then challenged by two more reviewers). Everything that survived was fixed: deferred photo
  write and catch-all error rows, the in-flight cap reservation and global ceiling, per-address signups,
  header redaction, the quoted occasion note, occasion dropped on rejection, the anchored SQLite path,
  chip focus, the photo-replacement race, decode fallbacks, live regions, the share guard, the locale
  switch failure path, two Hebrew strings, and the calibration script, which was then rewritten in Python to add the rule 1 scan and a JSON report.

### Objections kept out of the code (brief wins)

- **The user id as the only credential** is the biggest risk in the pilot: anyone with the id can read,
  post and delete. Documented prominently in the README as the brief asks; a signed cookie would have been
  a small addition but is "real authentication", which is Phase 2.
- **A 60 s model timeout** contradicts the 10-second promise. Kept per brief; in practice Sonnet 5 with
  thinking disabled answers in a few seconds and the loading screen is honest about waiting.
- **The metrics endpoint is public.** No auth exists in Phase 1, and the endpoint is aggregate-only; the
  README says to protect it before the URL leaves the team.

# Phase 2 — the feed

Added after the owner tried the Phase 1 build and asked for the social layer now rather than after the
`returnRate` gate: fire and followers, brands selling and running challenges the crowd decides, and a
young, energetic feel. Then refined: it is a social app first, not a contest; nobody has to post to use
it; the feed is for scrolling, reacting, commenting and taking inspiration. `PHASE2.md` is the plan that
was written before building; what follows are the calls made while building it.

## Decisions

### Scope and gate

- **The Phase 1 gate is overridden by the owner, and the Phase 1 metrics stay intact.** `returnRate` and
  the whole first block of `/api/metrics/pilot` are computed exactly as before; a `social` block is added
  next to them. The pilot question can still be answered.
- **Comments are in.** The plan had left them out to keep moderation small; the owner asked for
  "להגיב" explicitly. They are short (200 characters), never contain links that render as links, are
  reportable, and the post's author can delete any comment under their look.
- **No payments, no prize fulfilment, no DMs.** A challenge stores the prize as text plus an optional
  https link; the brand and the winner sort out delivery between themselves. The app records who won.
- **Still not in:** push, share images, native wrappers, sign-in with Apple or Google, closet features.

### Accounts

- **Cookie sessions replace the user id credential.** Signup and login set an HttpOnly, SameSite=Strict
  cookie (Secure whenever the request was HTTPS, so the tunnel gets a Secure cookie and localhost still
  works), 90 days sliding. Passwords go through ASP.NET Core's `PasswordHasher`. The Phase 1 "anyone with
  the id can act" limitation is gone.
- **CSRF is a required header, not an antiforgery token.** Every non-GET call under `/api` must carry
  `X-Requested-With: FitCheck` or is refused before routing. A cross-site form cannot set that header, and
  SameSite=Strict already keeps the cookie off cross-site requests; the header is the belt to that pair
  of braces and costs one line in the client.
- **Handles are Unicode letters, digits, dots and underscores, 2–40, unique case-insensitively**
  (`HandleLower` column with a unique index), so a Hebrew handle works and "Noa" and "noa" cannot both
  exist. A short reserved list (`me`, `admin`, `fitcheck`, `feed`, …) keeps handles out of route names.
- **A wrong handle and a wrong password get the same 401 message**, and login is rate limited per client
  address, so the login form cannot be used to enumerate accounts.
- **No email address, so no password recovery.** Adding email meant adding delivery, verification and a
  second secret; for a private pilot a lost password is a new account. Documented as a limitation.
- **Brand is an account type chosen at signup**, not a verified status. Brands can open challenges and
  attach product links to their own posts; people cannot. Nothing else differs. Verification is a later
  problem.
- **Streak = consecutive days with an OK check**, stored on the user and updated when a check succeeds.
  It is a small daily hook that costs nothing and needs no job.

### What goes public

- **A check stays private; a post is a separate, explicit act.** The post sheet says what will be shown:
  the photo, the intent, the score and the headline, plus a caption. The tip and the item breakdown are
  the user's and never leave the check. Only `ok` checks can be posted, once each.
- **Photos are served by exactly one route, `GET /api/posts/{id}/image`, and only for a visible post.**
  The file stays under the private storage root; the route streams it with `Cache-Control: private`.
  Deleting the post closes the door again. The Phase 1 "no plausible URL serves a photo" test still holds.
- **Reads are public, writes need a session.** The feed, posts, comments, profiles and challenge boards
  work signed out, so a link shared on WhatsApp opens for anyone. Everything that changes state asks to
  sign in, and the client says so with one card instead of failing.
- **Hidden posts remain visible to their author with an "under review" badge**, so a person whose look
  was reported is not left guessing why nobody reacts.

### Reactions and counters

- **"Fire", not "like".** One per person per post, enforced by a unique index; repeating is idempotent.
  Firing your own post is allowed (it is harmless and a plain refusal would be a strange first
  experience), but it produces no notification.
- **Fire and comment counts are denormalized and moved with conditional `ExecuteUpdate`**, so two
  requests cannot double-count and a delete cannot go below zero; follower counts are cheap enough to
  count live. Account deletion decrements the counters it touched on other people's posts.
- **Notifications deduplicate per actor and target.** Unfire and fire again does not spam; a vote moved
  back and forth notifies the entrant once. Actor names are looked up at read time, so a rename shows
  through and a deleted account falls back to its handle.

### Feed

- **Three tabs, one query.** `fresh` is newest first; `top` is the most fire in the last 7 days, so an
  early post cannot sit at the top forever; `following` is newest from people you follow and needs a
  session. An intent filter sits on top of all three, because "show me date looks" is the inspiration use
  case.
- **Offset paging, page of 10, `nextOffset` in the response.** Cursor paging is the right thing at scale;
  at pilot size offsets are simpler and let the client keep one number.

### Challenges

- **Brands only, 1 hour to 60 days, intent fixed at creation.** Entering locks the check's intent to the
  challenge's, so the stylist scores the look against the brief the brand set.
- **One vote per person per challenge, movable while open, never for your own entry.** Reads as a single
  row per voter (unique index), so the count is always the number of people.
- **The winner is fixed lazily, on the first read after `endsAt`, with a conditional claim.** A
  `ResolvedAt is null` update guarded by the row's own state means two simultaneous reads produce one
  winner and one set of notifications; there is no background job to run or forget. Most votes wins; a
  tie goes to the earlier entry, which rewards showing up first.
- **A challenge with no entries ends with no winner** and tells the brand so.

### Safety

- **Three reports from different people hide a post or a comment.** The threshold is configuration.
  Reports keep the reporter's reason for a later human look; there is no admin screen yet, which the
  README says plainly.
- **Captions, comments, bios and challenge text go through the same sanitizer as the occasion**
  (control characters stripped, length capped), and the client never puts user text through `innerHTML`.
  Product, prize and website links must be `https://` so `javascript:` and `data:` URLs cannot be posted.
- **Account deletion is one request and removes everything**: checks and photo files, posts, comments,
  fire, saves, votes, follows, notifications received, and reports made. Notifications the person caused
  for others stay as plain text under their handle; the challenge winner pointer is nulled if it pointed
  at a deleted post, and their entries leave the leaderboard.

### Client

- **One hash-routed page (`app.js`) replaces the screen file.** Routes for the feed, a post, challenges,
  a challenge, check, result, activity, profiles, saved, my checks, settings, login and signup. Every
  render carries a token so a slow response cannot paint over a newer screen.
- **Dark, high-contrast theme with one lime accent and one fire orange**, big display numerals for the
  score, a bottom tab bar with the check button raised in the middle, a count-up score and a burst on
  fire; all of it off under `prefers-reduced-motion`.
- **Tapping the active tab refreshes it**, as phone feeds do. The feed loader carries a sequence number
  so a refresh during a load cannot leave the list empty (found by the browser test: two loads used to
  fight and the second gave up).
- **The post sheet is on the result screen**, one tap from the score, with the challenge picker limited to
  open challenges of the same intent and the product rows shown to brands only. A check can also be
  posted later from "My checks".
- **Names everywhere, handles underneath.** Display names are optional; the handle is the fallback and
  the route (`#/u/handle`).
- **Hebrew copy keeps the neutral forms** of Phase 1 ("מתחברים כדי להגיב", "שווה להיות הראשונים") and the
  RTL layout mirrors through logical properties; scores and percentages come from `Intl` in the active
  locale.

### Testing and verification

- **119 xUnit tests** (up from 98): signup and login rules, the CSRF header, posting, fire, comments,
  saves, follows, feed tabs, reports, challenges and votes, winner resolution, notifications, deletion
  cascades and counters, social metrics. The Phase 1 tests were adapted to cookie sessions and all still
  pass.
- **The browser test was rewritten for three people in three browser contexts** (a person in English, a
  brand, and a Hebrew browser that mostly reads): 15 steps from signup to account deletion, including the
  challenge ending (the database clock is moved) and photo privacy by URL. It found two real bugs before
  hand-off: the feed double-load race above, and the activity list showing handles where names belong.
- **Not verified here, again: the real model.** The calibration script now signs up through the cookie
  flow; run it before inviting people.

### Objections kept out of the code (owner wins)

- **Building the feed before `returnRate` said so.** The owner decided the product is a social app; the
  metric is still there for the pilot readout.
- **Brand accounts are self-declared.** Anyone can tick the box. Fine for an invited pilot; verification
  belongs with age assurance in the launch checklist.
