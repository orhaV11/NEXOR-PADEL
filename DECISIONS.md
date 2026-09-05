# DECISIONS.md — OREVOSH (built as FitCheck: Phase 1, Phase 2, Phase 3)

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

- **122 xUnit tests** (up from 98): signup and login rules, the CSRF header, posting, fire, comments,
  saves, follows, feed tabs, reports, challenges and votes, winner resolution, notifications, deletion
  cascades and counters, social metrics. The Phase 1 tests were adapted to cookie sessions and all still
  pass.
- **The browser test was rewritten for three people in three browser contexts** (a person in English, a
  brand, and a Hebrew browser that mostly reads): 15 steps from signup to account deletion, including the
  challenge ending (the database clock is moved) and photo privacy by URL. It found two real bugs before
  hand-off: the feed double-load race above, and the activity list showing handles where names belong.
- **Not verified here, again: the real model.** The calibration script now signs up through the cookie
  flow; run it before inviting people.

### Review before hand-off

- **Two independent reviews, backend and client, ran against the finished build.** Neither found an
  authorization hole. What they found was fixed: a hidden comment leaving the count twice when deleted,
  double taps on follow, vote and report surfacing as 500s instead of the idempotent answer, account
  deletion running outside a transaction, activity rows and the challenge winner still pointing at a
  deleted post, a brand able to enter and vote in its own challenge, report counts written from memory
  instead of rows, fire and comment counters moving in a second statement, product labels and
  timestamps without an offset taken at face value, photos locked against deletion on Windows while
  being streamed; on the client, private state surviving sign-out on a shared phone, a bad percent
  escape in the URL crashing the router, a wrong password on the login page signing the person out, a
  check failure losing its message after navigating away, the challenge dropped on "try another photo",
  the toast under the iPhone home bar, `@handle` rendered backwards in Hebrew, and a dozen smaller
  focus, fallback and copy issues. Three tests cover the backend fixes; the browser test covers the
  client ones.

### Objections kept out of the code (owner wins)

- **Building the feed before `returnRate` said so.** The owner decided the product is a social app; the
  metric is still there for the pilot readout.
- **Brand accounts are self-declared.** Anyone can tick the box. Fine for an invited pilot; verification
  belongs with age assurance in the launch checklist.

# Phase 3 — OREVOSH, the real thing

The owner tried Phase 2 and set the direction: this is a social app first, challenges are a nice way in for
brands rather than the point, the neon accent had to go, the app is called OREVOSH, it has to feel like a
phone app, and the goal is a place where fashion brands and the people who wear them meet. `PHASE3.md` is the
plan written before building; what follows are the calls made while building it.

## Decisions

### Name, look, feel

- **OREVOSH everywhere a person looks; the .NET project keeps `FitCheck.Api` for now.** Cookie, CSRF header
  value, preferences key, copy, manifest, icons and docs say OREVOSH. Renaming the solution, folders and
  namespaces is a mechanical change that also changes the run instructions the owner just learned, so it waits
  for the repository's own rename.
- **The accent is one lilac (`#b39dff`) with a rose companion (`#ff8fb1`) and fire orange for reactions.**
  The owner asked for something nicer than the neon yellow; lilac on the dark base reads young and chic, rose
  marks brands and featured looks, and fire stays the colour of a reaction. All of it is CSS tokens, so a
  different accent is a one-line change.
- **Syne for the wordmark and display numerals, Heebo for text.** Syne has no Hebrew, so Hebrew headings fall
  back to Heebo 800 through the font stack; the wordmark stays Latin and left-to-right in both languages.
- **Phone-native by construction, not by media query.** Edge-to-edge look cards, a raised check button in the
  bottom tab bar, bottom sheets for every menu and form that used to be inline, skeleton loaders, infinite
  scroll, pull to refresh, double-tap to fire with a flame burst, 44px targets, safe-area insets, and a top
  bar that turns into a back-arrow-plus-title on inner pages. Desktop simply gets the same column centred.
- **Installable.** A manifest with generated icons and a service worker that caches the app shell
  (network first so a deploy shows up, cache as the fallback) and never touches `/api` or photos. Chrome shows
  its own install prompt and the app keeps it for a one-time banner; iPhone gets the "Share → Add to Home
  Screen" hint instead, because Safari offers nothing else. The browser test blocks the service worker so it
  never masks a live request.

### Information architecture

- **Five tabs: Home, Explore, Check, Activity, Profile.** Challenges moved from a tab to a section of Explore,
  with their own routes intact. Explore is where discovery lives: search, trending tags, brands to follow, the
  week's top looks, open challenges.
- **Signup asks three things: handle, password, 16+.** The brand question is gone from signup. Brand mode is a
  switch in settings with a one-line explanation, switchable both ways; the display name moved to settings
  too. A welcome screen follows signup: pick the styles you wear and follow a few brands, both skippable.
- **Home has two feeds, For you and Following, plus intent chips.** "Fresh" and "Top" survive as API tabs
  (Top feeds Explore's "Top looks this week").

### Brands and people meeting

- **`@brand` mentions and `#tags` are parsed on the server at posting time**, not trusted from the client:
  only existing handles become mentions, the author cannot mention themselves, five of each at most, tags are
  lower-cased once. Mentioned accounts get one notification. This is the mechanic the owner described: a person
  wears a brand, says so, and the look lands on the brand's Community wall.
- **"Featured by" is the collaboration primitive, and it is earned.** A brand can feature a look only when the
  look mentions the brand or entered one of its challenges; one brand per look, first come; the creator is
  told; only the featuring brand can undo it. No money moves, no review step: the brand's taste is the curation.
  A person's profile gains a Featured tab the first time a brand features them.
- **Avatars use the same private store and the same magic-byte rules as check photos**, capped at 2 MB, downscaled
  and centre-cropped to 320px on the phone, served through one versioned route with a public one-day cache. The
  version in the URL is what lets the cache be that long.
- **The For you feed is a documented formula, not a recommender.** Fire and comments (log-scaled), a bonus for
  people you follow, for intents you said you wear, for intents you have checked lately, and for featured looks,
  minus a slow time decay; ties by recency. Deterministic, testable, explainable to a pilot user, and ranked in
  memory over the newest 400 posts of the last 30 days. When the feed has ten thousand posts a day this becomes
  a job with a table; not before.
- **Search is a prefix match on SQLite.** Handles by prefix, display names by substring, tags by prefix, brands
  first. An index and a proper tokenizer are a scale problem the pilot does not have.

### How it was built

- **The spec came first, then three backend agents in parallel worktrees, then nine view agents in parallel
  on distinct files, then reviewers.** The shared contract (`PHASE3.md`), the schema, the DTOs, the localizer
  keys, the batched reader and the client core were written by hand up front so that parallel work had fixed
  edges to build against; the feed handler moved to its own file for the same reason. Every backend agent
  shipped its own tests; every view agent smoke-tested its screen in Chromium against the running API.
- **The client became modules.** One core (`core.js`) and one file per screen replace the Phase 2 monolith.
  Same vanilla approach, no build step, but nine people or nine agents can now work without touching each
  other's files.
- **All UI strings were written up front in both languages**, so the view work could not drift into
  untranslated copy; the browser test still fails on any missing key.

### Review before hand-off

- **Six review lenses, then three adversarial refuters per finding, 141 agents in all.** Forty-five findings
  stood; the refuters rated most of them low. What was fixed: the service worker and the static files now
  revalidate on every load (`Cache-Control: no-cache`, `cache: 'no-cache'`), so a deploy can no longer leave an
  old core next to a new screen; the feed loader no longer sticks on skeletons when a refresh lands during a
  page load; cards are deduplicated across pages because the For you ranking moves under the reader; Back from
  a look returns to the same place in the feed (the list and the scroll position are kept for ten minutes and
  dropped when a look is posted or deleted); guards and signed-in redirects replace the history entry instead of
  pushing one, so Back never loops; bottom sheets trap focus, make the page behind them inert, dismiss on a
  swipe only from their own chrome, and open a confirmation on Cancel rather than on the destructive button;
  a cancelled photo picker no longer swallows the next pick; a deleted account takes every activity row keyed by
  its handle with it, and avatar versions are wall-clock based, so a handle taken again inherits neither;
  display-name search compares in .NET, so Hebrew and accented capitals match; un-vote, delete-look and
  delete-comment are set-based and answer the same to a racing second request; two simultaneous third reports
  on a comment move the count once; activity rows show the actor's photo; only mentions that resolved become
  links, with the same token rules as the server; fire and comment counts are readable by screen readers;
  form errors take focus; touch targets are 44px; the unread badge has contrast; people's text is
  bidi-isolated inside either UI; Hebrew activity lines are gender-neutral; singular forms exist for counts.
- **Left as documented limitations:** the challenge list loads entry rows to count them (fine at pilot scale),
  the language switch rebuilds a form in progress, profile tabs lack the full keyboard tab pattern, and the
  install banner cannot be exercised in headless Chromium.

### Objections kept out of the code (owner wins)

- **Brand mode is a switch anyone can flip.** Verification belongs in the launch checklist with age assurance;
  for an invited pilot the switch is the honest amount of process.
- **Featured looks have no review.** A brand featuring a look is a brand endorsing it; if that is ever abused,
  reports still hide the look.
- **The project folder still says FitCheck.** See above.

# Phase 4 — the hashtag, and a look of our own

The owner's notes after using Phase 3: the challenge picker on the post sheet is noise (challenges are a side idea
for brands, not the product); the brand switch should not be explained through challenges; and the visuals felt like
TikTok. "Let's find and create OREVOSH's own look, especially Explore and followers/following."

## Decisions

### Challenges are a hashtag

- **A challenge is a hashtag a brand promotes.** The brand picks it (the form suggests one from the title; it is made
  unique among open challenges with a numeric suffix rather than refused), and a look posted with that hashtag while
  the challenge is open is an entry: once per person, any intent, never the brand's own look. The picker is gone from
  the post sheet; the challenge page's button pre-fills the hashtag in the caption. `challengeId` is still accepted by
  the API for older clients. The intent on a challenge is its theme, not a gate: the crowd decides what fits.
- **Brand copy leads with community, featuring and products.** A hashtag challenge with a prize is one sentence at the
  end, marked optional.

### The look: Night Atelier

- **Three directions were built and rendered on the real app with seeded content, then judged.** Paper Editorial (a
  light magazine: paper, ink, serif, a text folio with an O seal), Night Atelier (a warm dark wall of mounted prints,
  brass stamps, a floating text dock with a raised stamp), and The Wall (poster graphic: cream, colour blocks, stickers).
  Three judges with different lenses (a creative director, a mobile product designer, a young Israeli TikTok user) scored
  distinctiveness, chic, usability, RTL and feasibility. Atelier won on totals (117.5 / 114.5 / 111); each judge had a
  different favourite, which is the point of the panel.
- **What the system is:** aubergine-charcoal ground under a lamp gradient and a faint grain, ivory ink, brass as the one
  accent and fire orange for reactions only; Instrument Serif (Frank Ruhl Libre for Hebrew) for the wordmark, headings and
  every numeral that matters, Heebo for everything else; one signature shape everywhere, a square with its top-end corner
  clipped round, which mirrors for free in Hebrew. Navigation is a floating opaque dock of four text destinations with the
  check control as a raised, tilted brass stamp carrying the serif "O". Look cards are mounted prints with the score as a
  stamp straddling the photo's bottom edge. Explore has a front page (kicker, title, dateline), a hero from the week's top
  look, trending tags as a numbered index with dotted leaders, brands as a band of framed portraits, the top looks as a
  staggered wall with gallery captions, and hashtag challenges as quiet tickets at the tail. Profile stats are a statline
  and a colophon in serif numerals, not tiles; the follow control is the page's one big label under the handle. No pill
  shapes, no glass, no gradient plus button, no 999px radius anywhere.
- **Grafted from the runners-up:** Paper's Explore front, index and captions and the serif O monogram; Poster's full-height
  tap blocks in the dock, outlined 42px action targets and the dashed "not yet" disabled button.
- **Refused:** a light theme (the dark ground is the identity), the lilac accent (brass replaced it), tilted stickers and
  colour-cycling shadows, and plate numerals over photos (two numbers per print).
- **`DESIGN.md` is the system's reference**, kept in the repository so the next screen is designed inside it.

### Objections kept out of the code (owner wins)

- **Instrument Serif has no Hebrew.** Frank Ruhl Libre follows in the stack and sits on the same baseline at these
  sizes; the wordmark stays Latin in both languages.

# Phase 5 — the logo, and Ring of Fire

The owner's verdict on Night Atelier: more unique, yes, but the earlier vibe was better, younger and more "lit". And a
request: a logo for OREVOSH. The logo came first and the system followed it.

## Decisions

- **Four logo concepts were drawn as pure geometry (no fonts), rendered on presentation sheets, and shown side by side:**
  Ring of Fire (the O as a bold ring with one lick of flame breaking out), The Mirror (the O as a full-length mirror with a
  spark), Volt (the V as a bolt that is also a check, ink + acid lime), and The Score Stamp (a tilted sticker holding a 10
  whose 0 is the O). The owner chose **Ring of Fire** ("קטלני").
- **Why it works as the mark:** it is the first letter, not a badge beside the name; the ring is the frame you step into
  and the score lands inside it; the flame is the app's one currency, the fire reaction, so the logo is the moment a look
  catches fire. It reads at 16px as ring + spark. The gradient runs cool-to-warm across the ring and the flame sits on the
  mauve midpoint so orange against lilac stays crisp at favicon size. Sources live in `wwwroot/brand/`.
- **The system is rebuilt around the logo, not the other way round.** Palette back to the Phase 3 stage (near-black,
  lilac, rose, fire), with one rule added from the logo: the lilac-to-rose gradient is the only gradient and appears where
  the ring appears (primary buttons, the score ring, the follow label, brand portrait rings, the sheet's edge); fire is
  reserved for what caught fire. Type goes rounded geometric (Outfit) to match the lettering, Heebo for body and Hebrew.
  Shapes are round again (pills are the lettering's own shape). The structure that made the previous round ours stays:
  the floating text dock with the check control raised in the middle (now the mark itself, which draws its ring and pops
  its flame when tapped), the Explore front page with a hero and a numbered index, the profile statline, hashtag
  challenges at the tail. The gallery mood goes: no brass, serif, grain, frames or stamps.
- **The score is a ring.** Every look's score sits in a small gradient ring at the photo's corner, the logo's own shape
  with the number inside it, which is the sentence the concept was built on.
- **`DESIGN.md` now describes Ring of Fire**; the Night Atelier brief is superseded and kept only in git history.

### Objections kept out of the code (owner wins)

- **Outfit has no Hebrew.** Heebo 800 carries Hebrew headings; the wordmark is a logo and stays Latin.

## Round 6 — the feed switch (2026-09-05)

The owner liked Ring of Fire and asked for one more thing: "For you / Following" at the top of Home looked like every
other app, find a way to present it that is ours.

### Decisions

- **The switch is built from the two halves of the mark.** The flame is *For you* (what is catching fire, the ranked
  feed) and the ring is *Your circle* (the people you follow). The current feed is **lit**: a gradient pill with the
  word on it, the flame in fire, the ring in ink, and a rose glow. The other waits **cool** beside it: a gray glyph and
  the word, no container. There is no underline (Instagram, TikTok, Threads) and no sliding thumb (the iOS segmented
  control); switching re-renders Home and the newly lit coin fades its fill in and pops its glyph, the same pop as the
  check control. The challenge list's Open/Ended pills reuse the same styles without glyphs.
- **"Following" is now "Your circle".** The ring in the logo is your circle, so the feed is named after it, in both
  languages ("המעגל שלך"). Follower counts, the follow label and the empty states keep their words; only the feed's
  name changed. The route (`#/feed/following`) and the API tab (`following`) are unchanged.

## Round 7 — the real thing: clips, the camera, push, moderation, the deploy kit (2026-09-05)

The owner asked two things: is the idea good, and take the app to "the real thing": clips, creating photos and clips
inside the app, and whatever else separates a pilot from a product. The answer to the first question is in the chat
log and summarised under "Objections"; this section records what was built and why.

### Decisions

- **A clip is a look with a cover, not a new kind of post.** The stylist still judges one still (the frame the person
  picks, which is also the clip's poster); the clip rides along on the same check and the same post. So every rule
  about photos holds for clips unchanged: private until posted, stored only when the check is ok, one delete removes
  it, never reachable by path, served only through `/api/posts/{id}/video` with Range support. Clips are capped at
  40 MB (`Storage:MaxVideoBytes`) and 30 seconds (`Storage:MaxVideoSeconds`, enforced by the client; the server has no
  ffmpeg to measure duration and does not pretend to). No server-side transcoding in this round: iPhones record H.264
  MP4 which plays everywhere; Android Chrome records WebM, which older iPhones cannot play. That is the honest limit of
  a no-ffmpeg pilot and the first thing to add for a public launch (DEPLOY.md lists it).
- **The camera is the ring.** The in-app camera's shutter is the mark's ring: tap for a photo, hold and the ring draws
  itself clockwise as the clip records toward the cap, the same motion as the check control. A framing guide asks for
  the whole look, shoes included. No filters, on purpose: a filter that warms or fades the colors changes what the
  stylist judges, and the score has to be about the clothes as they are.
- **Frame picking is the person's call.** For a clip, a slider picks the judged frame (40% in by default, where people
  are usually posed). It makes the check honest and gives the person control over their cover.
- **Push is Web Push, no vendor.** VAPID keys the owner generates (`dotnet run -- --vapid`), subscriptions per browser,
  a background sender that never blocks a request, in the person's language, deleted on 404/410. iPhone needs the app
  on the home screen (iOS 16.4+); the switch in Settings says so instead of failing quietly.
- **Moderation is a queue, not a dashboard.** Reported looks and comments with the reasons people gave, hide/show/
  delete, and suspension. Admins are handles in `Admin:Handles`; a suspended account cannot sign in, its profile reads
  as missing and its looks are hidden; lifting the suspension un-hides what the crowd had not hidden on its own. Unhiding
  a look clears its reports so the same people can report it again if it recurs.
- **Migrations from here on.** The schema is versioned with EF Core migrations and applied at start. A pilot database
  made by `EnsureCreated` in earlier rounds is upgraded in place (a `.bak-<stamp>` copy first, missing tables and
  columns added, then the history row), so the owner's test data survives this update and every update after it.
- **The deploy kit is one `docker compose up`.** App container plus Caddy for automatic HTTPS on the owner's domain,
  one data volume for the database and the media, `/healthz` for the proxy, `--backup` for a nightly copy, security
  headers, and DEPLOY.md written for someone who has never run a server.
- **The share card is the growth loop.** A story-sized image of the look with its score ring and the wordmark, saved
  or shared from the result screen and the post menu. The people who will fill the feed are on Instagram and TikTok
  today; the card goes where they are and carries the mark back.
- **Community guidelines are in the app** and linked from signup: judge clothes not people, 16+, your own photos,
  brands play fair, reports are acted on, what we keep and what deletion removes.

### Objections kept out of the code (owner wins)

- **On the idea.** The outfit check is the strong part: useful alone, on day one, with an empty feed. "A new social
  network" is the hard part: attention lives on Instagram and TikTok, brands follow the audience, and an empty feed
  kills a social app faster than any bug. The recommended path is stated in the chat: launch as the outfit-check app
  with share cards for distribution, build density in one narrow community first, bring brands once there is an
  audience to sell them, and measure retention before features. Clips multiply cost (storage, bandwidth, moderation) and
  are kept short for that reason.
- **Left out on purpose, named so nobody assumes it is there:** password reset (needs an email provider; wire one and it
  is an afternoon), payments or checkout (product links stay links), native app store builds (the PWA installs today;
  a Capacitor wrap is the next step), server-side transcoding (ffmpeg), object storage (the store interface is ready),
  direct messages (moderation load), and real age assurance (self-declaration is a pilot measure).

### After review

The review that followed the hand-off changed a few rules. What changed and why, most important first:

- **A moderator is a flag on the account, not a handle in a list.** `Admin:Handles` used to be the whole rule: whoever
  held a listed handle held the queue. A handle is not an identity. Delete the account and anyone could sign up with the
  freed handle and inherit the queue; list a handle before its owner signs up and whoever got there first would be the
  moderator. Now `AppUser.IsAdmin` is on the row. `Admin:Handles` promotes existing accounts at start and never demotes;
  a listed handle can no longer be signed up, so the owner signs up first, then lists the handle and restarts;
  `--admin <handle>` and `--unadmin <handle>` set and clear the flag at any time without a restart. A moderator cannot be
  suspended from the queue (moderators are not for each other to switch off; that is `--unadmin` on the box) and cannot
  delete the account until un-admined (the freed handle, still in the list, would be promoted again on the next restart).
- **Suspending a brand closes its open challenges**, with no winner and no notifications: the hashtag stops taking
  entries, and nobody is crowned by an account that is locked out. Lifting the suspension does not reopen them; the brand
  opens a new one.
- **Push, hardened.** At most 10 subscriptions per account (the oldest make room for a new browser); an endpoint must be
  a public push-service name, because a literal address, `localhost` or a single-label name would make this server post
  signed requests at its own network; and a push service answering 401 or 403 (the subscription was made against other
  VAPID keys) drops that subscription instead of failing at every notification.
- **Clip codecs, corrected.** The camera asks the recorder for H.264 MP4 with explicit codecs and takes WebM only when
  the device cannot, so Chrome records MP4 where it can, not only Safari. "Android Chrome records WebM" above was too
  flat; the honest line is that a clip from an Android phone may be WebM, WebM does not play on older iPhones, and
  transcoding is still the launch item.
- **Backups are private.** They hold every photo and clip, and they landed world-readable. `tools/backup.sh` now writes
  them readable by root only, keeps 14 copies of the database but only two of the media folder (a storage copy is the
  whole folder), and leaves nothing on the data volume on any exit path. An off-site copy keeps the modes or is
  encrypted; DEPLOY.md says how.
- **WAL.** Every file database is switched to WAL mode at start: readers never wait on a writer, the push worker and a
  request share the file, and `--backup` snapshots it while the app runs, into a single file in rollback mode so the
  copy never grows sidecars of its own. The corollary for a laptop pilot: stop the app or use `--backup` before copying
  the file, or the `-wal` sidecar's writes are left behind. Schema changes from here on are new migrations;
  `InitialCreate` is never regenerated again.
- **Report reasons are a list.** Not an outfit, nudity or sexual content, comments on the person rather than the
  clothes, spam or a scam, something else: picked in a sheet instead of typed, so the same thing is reported with the
  same word. The API still takes free text, kept to 200 characters.
- **HSTS for this host only.** The header no longer carries `includeSubDomains`: the app decides HTTPS for its own name,
  not for everything else the owner runs under the domain.
