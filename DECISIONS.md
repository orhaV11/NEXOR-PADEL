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
  from the oldest counted check (since Round 9's review: from the call whose expiry actually frees a permit, the
  (count − cap + 1)th oldest; the two are the same only while the count equals the cap).
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
  columns added, then the history row; Round 9's review added foreign keys, nullability and a rollback to it), so the
  owner's test data survives this update and every update after it.
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

## Round 8 — accessories, recovery, clips that play everywhere, the internet (2026-09-08)

The owner asked for the pieces Round 7 named as "left out on purpose", and for the app to be reachable by anyone with
one command: a way back into an account, clips that play on every phone, a deploy that needs no server, checks on every
push, and the pilot's numbers off the open internet. Four builders worked in parallel from one skeleton (the domain
fields, options, DTOs, stubs, routes and strings); this section is completed by the lead after the merge.

### Decisions

- **Accessories are a dimension of the score, not a footnote.** The stylist now returns three sub-scores behind the
  overall one (fit, color, accessories: `ScoreBreakdown`) and reads the accessories on their own (`AccessoriesFeedback`:
  what it saw, a verdict of adds / neutral / missing / clashes, a note, and the one accessory that would finish the look
  for this intent, doable with common pieces). The sub-scores are copied onto the post when it is published, so the feed
  can show them; the accessory advice stays private with the tip, like the rest of the breakdown. Checks made before the
  rubric changed have no breakdown and the client simply does not draw one; the prompt version is bumped so calibration
  reports before and after can be compared.
- **Recovery is by email, and email is optional.** An account can carry one address (lower-cased, unique among accounts
  that have one, never shown to others), confirmed by a link; a forgotten password is reset by a link that lives an
  hour. Both are single-use tokens in one table, and a request for recovery is rate limited and answered the same way
  whether or not the handle exists. Mail goes over plain SMTP (`Email__*`, any provider: Resend, Postmark, a Gmail app
  password for a pilot); with no provider set the app says so in the client and writes the links to its log, so a
  laptop pilot keeps working and nothing pretends to send.
- **Clips are transcoded to H.264 MP4 in the background** when ffmpeg is on the machine (`Storage:Transcode`, on by
  default; `Storage:FfmpegPath` when it is not on the PATH), so a WebM from an Android phone plays on iPhones. The
  upload is stored and served as it came, and replaced by the MP4 when the worker is done; `/api/config` says whether
  transcoding is on, so the client can say what to expect. Without ffmpeg nothing changes, and DEPLOY.md's checklist
  says how to add it to the image.
- **Deploying is one command and needs no server.** `fly launch` with the repository's `fly.toml` (one small machine in
  Frankfurt, one 3 GB volume at `/data`, HTTPS at the edge, `/healthz` as the check, never stopped for idleness), `fly
  secrets set` for the key, `fly deploy --ha=false`; about 5 USD a month. The VPS path with Docker and Caddy stays as
  the option for full control; both run the same image from the same Dockerfile, and DEPLOY.md opens with which to pick.
- **Every push is checked.** GitHub Actions builds in Release with warnings as errors, runs the xUnit suite and runs the
  browser test in Chromium with the Anthropic API stubbed, on every branch and pull request; a push to `main` or a `v*`
  tag publishes the image to `ghcr.io/<owner>/orevosh`. The README carries the badge, so a red build is visible before
  anyone deploys it.
- **Comments and reports are rate limited per account.** 30 comments and 20 reports an hour, a 429 with "Slow down a
  little. Try again in a bit." and a `Retry-After`. Per account, not per address: the cookie names the person, so two
  people behind one router never share a bucket; the limiter runs after authentication for that reason, and an unsigned
  call is a 401 that spends nobody's permits. Fixed windows in memory, like the signup and login brakes: enough against
  a script, not a spam filter; the queue and suspensions remain the answer to a patient flood.
- **Metrics are behind the moderator flag.** `/api/metrics/pilot` answers through a moderator's session and 403 to
  anyone else, so the URL can leave the team without the numbers leaving with it; the browser test reads it that way.

### Objections and notes kept out of the code (owner wins)

- **On the rubric.** A partial breakdown from the model (two numbers of three) is dropped whole rather than shown with an
  invented ring; the accessories list is cut to six pieces of forty characters; a verdict outside the four words reads
  as neutral; and "add one" is kept even when the verdict is "adds", because a stylist may still name a piece. The
  rule that a look with nothing on rarely earns above 7 outside Minimal and Sport is a rubric choice, not a fact; run
  `scripts/calibrate.py` on real photos (it prints the sub-scores and the verdict spread) before trusting it.
- **A password reset does not end other sessions.** Sessions are stateless cookies; a per-account stamp checked on
  every request would close that and is a small follow-up. Until then, someone with an old cookie keeps it for up to
  90 days after a reset.
- **Nothing here has run on GitHub, Fly or a mail server yet.** The workflows, `fly.toml` and the SMTP sender were
  built and tested against stand-ins (a recording mail sender, a fake Docker); the first push, the first `fly deploy`
  and the first real email are the proof, and DEPLOY.md says where to look when one of them misbehaves.
- **Clips from before this round** that were stored as WebM are re-encoded by the sweep at the next start; the
  original serves until then.

## Round 9 — the first wow before the signup, plans, "Which one?", trust, the launch kit (2026-09-08 → 2026-09-12)

The owner asked for what separates a pilot from a thing people can find, try, come back to and pay for: a check
before the account, a plan that pays for the model, a second stylist call ("Which one?"), reasons to come back
(insights, a daily prompt, a follow-up look), the trust pieces a store and a lawyer ask about (a real age rule, verified
brands, terms and a privacy policy, the pilot's numbers on a page), and everything a launch needs outside the app: a
slogan, a brand kit, landing pages, store texts, a marketing plan and the store shells. Builders worked in parallel
from one skeleton (`c480be5`: the schema and its migration, the options, the DTOs, the localizer keys, the empty views
and routes), in two waves, and the lead merged and wired; the recovery hardening came from a review between the two.
This section is completed by the docs builder after the merge, from the code.

### Decisions

- **The guest check comes first, because the first wow must come before the signup.** A visitor's first tap on the
  mark gets a real verdict with no account: `POST /api/checks` needs no session, the answer sets `orevosh.guest`
  (HttpOnly, SameSite=Strict, a random token, one day), the row keeps the token where an owner would be, and the result
  screen offers "Sign up to keep it and post it". Signing up or in claims everything the cookie names (`POST
  /api/checks/claim`: owner set, token cleared, the photo and clip moved into the account's folder, all or nothing; the
  client calls it after signup, after login and at every signed-in boot, and a `0` is the usual answer). What nobody claims expires
  with the cookie: `GuestCheckSweeper` removes day-old guest rows and their files, hourly and at start, and says so in
  the log (`Guest sweep: …`). It is **capped per cookie and per address because it costs money**:
  `Plans:GuestChecksPerDay` (1) per token from the rows and per client address from an in-memory count
  (`GuestAddressCounter`), both counting looks actually given (a refused upload, a model outage or a dropped connection
  spends nothing, and `error.guest_limit` is only sent for a look given); attempts are braked separately by the `guest`
  rate-limit policy (`Plans:GuestAttemptsPerDay`, twenty a day per address, 429 `error.too_fast`), on top of the global
  ceiling, and `0` closes the door (the check screen asks to sign in and keeps the submit disabled). A guest can read their own
  check and nothing else: no posting, no comparison, no history. The pilot metrics leave guest checks out, so
  `returnRate` still means what it meant.
- **Pro is a cap on a real cost, not a feature wall.** Every check is a model call, so the plan is the daily number:
  three on Free, thirty on Pro, `Limits:ChecksPerDay` as the ceiling no plan exceeds (the clamped number is what
  `/api/config` publishes and the Pro page promises), checks and comparisons counted together over the same rolling
  day on both routes, failed calls left out. Comparisons and insights stay free by default
  (`Plans:CompareNeedsPro` is off): a cap is honest about what Pro pays for, a wall around a feature would be theatre.
  Pro is two fields on the account (`Plan`, `ProUntil`), written by Stripe's webhook or by `--pro`, never by a
  request; a lapsed period reads as Free by itself, and `MeDto` carries `plan`, `proUntil`, `checksToday` and
  `checksPerDay` so the check screen can say "2 of 3 checks left today" and, for a Free account at its cap, where Pro is.
- **Stripe stays behind config so nothing pretends to charge.** `Billing:Provider` is `manual` until the owner has a
  Stripe account, a price and a tested webhook; with `manual`, the Pro page shows the benefits and a note that Pro is
  switched on by hand, `--pro <handle> <months|off>` is the upgrade path, and Checkout answers 400. With `stripe` and
  the three keys (secret, price, webhook secret, environment only) "Go Pro" opens a hosted Checkout Session (one
  form-encoded POST, no SDK: two calls do not earn a dependency) that returns to `#/pro?checkout=…`, and the webhook,
  the one write exempt from the CSRF header because Stripe cannot send it, is guarded by the `Stripe-Signature` HMAC
  with a five-minute tolerance. A paid period is 35 days, not a month, so a slow renewal event does not drop a paying
  person to Free, and a completed checkout stacks it on a period still running; the first `invoice.paid` is skipped
  because the checkout already counted it. Ends come from Stripe's events, not from the previous end: `invoice.paid`
  and `customer.subscription.updated` name the period (its end plus three days, never below what is there; `past_due`,
  `unpaid` or `paused` trims to three days from now at most), `customer.subscription.deleted` ends Pro now, and an
  account that is Pro cannot open a second Checkout (409). No event ids are stored: only a replayed
  `checkout.session.completed` stacks, every other repeat names the same period, and the README says so.
- **"Which one?" is one stylist call, two photos, one winner.** The comparer sends both images in one message with the
  analyzer's hard rules and the same calibration (an 8 here means what an 8 means on a check), a schema with a winner,
  two scores, two headlines, the reason and one tip that says which outfit it is for, and `PromptVersion` `cmp-v1`.
  **Comparisons are private and not postable**: both photos are stored only when the stylist confirmed two outfits,
  under the owner's folder with ids derived from the comparison's so an account deletion takes them with the folder,
  served to the owner alone through `/api/compare/{id}/image/a|b`, and there is no post route. A comparison counts
  against the day like a check because it costs like one (more, in fact).
- **Items are indexed at posting from the stylist's own words, never from captions.** When a look is posted, the
  item names from its check are copied to `PostItems` (lower-cased, one space between words, up to eight, sixty
  characters, with the category), and `/api/search` finds looks whose items contain the term, under the people and the
  tags. A caption cannot put a look under "black boots"; the stylist saw the boots or it did not. The verdicts and notes
  stay private with the tip, as before.
- **Insights are a pure function over the last 200 OK checks, and under three checks they are an invitation.** The
  average and the best score, the intent that scores highest among those checked at least twice (else the most
  checked; ties are stable), the item category most often called weak and in what share of the looks, how often
  nothing was on, and two to four sentences written by the server in the person's language. One check says nothing
  about a person, so the page shows "N of 3 checked so far" until then. Behind Pro exactly when comparisons are
  (`Plans:CompareNeedsPro`): 403 `error.pro_required` and the same Pro card as `#/compare`.
- **Today's look is a hashtag, not ephemeral content.** Thirty prompts in code (`Services/DailyPrompts.cs`, a tag, a
  title and a hint in each language, an optional intent), one a day by the count of UTC days since 31 December 2025
  modulo thirty (through 2026 that is the day of the year, so the pilot's calendar holds), so everyone sees the same
  one, a prompt comes back every thirty days, and the cycle runs on across the year turn instead of restarting on
  1 January. Nothing is stored: `GET /api/today` reads the
  visible looks posted today with that hashtag, and "Post yours" enters the check with the tag prefilled exactly the
  way a challenge does. The strip sits at the top of For you (up to eight thumbnails) and `#/today` is the day's grid.
- **Before/after is a strip on the card, not a separate post type, so the feed stays one kind of thing.** A post may
  name `beforePostId`, one of the author's own visible looks and never the look of this very check (400 otherwise);
  `PostDto.before` carries the earlier look's score and photo from one batched query, left off while the earlier look
  is under review, and the foreign key clears the link when the earlier look is deleted. The card shows "After the tip
  · 6 → 7" between the headline and the caption, green when the score went up, the whole strip a link to the earlier
  look; the post sheet offers the last five looks as a picker with "Not a follow-up" first and picked by default.
- **The birth date replaces the checkbox, because a checkbox is not an age rule.** Signup requires `birthDate`
  (`yyyy-MM-dd`, what a date input sends in every locale), sixteen on the day (the phone's own calendar day when the
  client sends `today` within a day of UTC, else the UTC day), not before 1900 and not in the future,
  checked after the handle and the password so the person fixes the top field first; the errors are
  `birthdate_required`, `birthdate_invalid` and `underage`. The date is stored as a UTC date on the account and appears
  on no DTO; the checkbox older clients still send is accepted and ignored. Still self-declared, still not age
  assurance; the README says so in the same breath.
- **Verification is the owner's hand (`--verify`), not a form.** `AppUser.Verified` is written by `--verify` and
  `--unverify` on the box and by nothing else; it travels on every user ref, the profile and `me`, and the client draws
  a small check inside the BRAND mark. A verification form would need a process behind it that a one-person pilot does
  not have; a command is the honest amount of process, and the go-live checklist says to run it for the first brands.
- **The numbers page is one hero figure, tiles and one-hue bars.** `#/admin/metrics` draws `/api/metrics/pilot` for
  moderators: the return rate as the hero (it is the kill switch), stat tiles, the score distribution as a bar list
  made of divs in one hue (lilac is the data; fire stays a reaction), the rubric averages, the community tiles and two
  small lists. No chart library, no second colour, and the server stays the gate (403 to anyone else).
- **The legal pages are written from the code's facts.** `#/terms` and `#/privacy` are ten headed sections each, in
  the i18n files and written in each language rather than translated, saying what the app actually stores, sends to
  the model provider (the photo, the occasion, the note and the language; never a name, handle, email or birth date),
  shows to whom, keeps for how long, and deletes; version 2, dated 2026-09-12 (the second version corrected, after
  review, what a posted look carries and how the stylist's item names are used in search), the contact
  `hello@orevosh.app`, and a
  governing-law line that is a placeholder ("the place where the owner is based"). They are linked from the agreement
  line under the signup button with the guidelines. The file says it in capitals: have a lawyer review them before
  launch.
- **The recovery hardening, after review** (`6897cdc`): a mailed link carries the token, so it must never be built
  from a `Host` header a stranger chose; links are built from `Email:PublicOrigin` or, with none, only for a loopback
  host, and the app warns at start when mail is on without the origin. Per-account brakes on top of the per-address
  one: three verification links per ten minutes and ten a day, three reset links an hour. A reset link is refused
  once the address it went to is no longer the account's, and changing the address voids the open reset links. CI now
  installs ffmpeg for the tests job so the transcoder tests run there instead of skipping.
- **The slogan is "Check the look." / "בודקים את הלוק."** with the tagline "A stylist in your pocket, and a community
  that lights it up." It is the app's own verb (CHECK sits under the mark in the dock), it reads as an imperative and as
  a description, and the Hebrew is the idiom people use. Chosen over "Every look, checked." (calmer, more product than
  gesture) and "Wear it. Check it. Light it up." (the three beats, but a video line, not a header); both are kept in
  `MARKETING.md` so the owner can swap by changing `COPY` in `tools/brand/render-kit.js` and the two i18n keys
  (`app.slogan`, `app.tagline`).
- **The brand kit is rendered, not drawn.** `tools/brand/render-kit.js` builds every file in `brand-kit/` (the marks on
  dark, light and nothing, monochrome SVG+PNG, the wordmark, the lockup, the avatar, five covers, the two OG cards, three
  story templates in both languages, twenty store screenshots), the OG card the app serves and the landing screens,
  from HTML templates with the real brand SVGs, the tokens of `DESIGN.md` and the browser test's screenshots, with the
  fonts from Google or the OFL copies offline. A slogan change or a real screenshot is a re-run, and the README in the
  folder is written by the script from what is there.
- **The landing pages are static and the production origin is a placeholder.** `/landing/` and `/landing/index.he.html`
  need no app JS and hold at 390 and 1280 px; `index.html` carries the Open Graph and Twitter tags with the 1200×630
  card; the manifest's description is the tagline; `mobile/` is a Capacitor shell pointed at the site with nothing
  installed. All of them say `https://looks.example.com` where the domain goes, and the go-live checklist lists the
  places to replace it. The service worker lets `/landing/` navigations through to the network, so a landing link never
  answers with the app shell.
- **468 tests** (up from 314; 422 before the review's fixes), and the browser test now starts with a guest's check in
  Hebrew before anyone signs up.

### Landed late in this round

- **Arabic and Russian**, everywhere Hebrew is: `ar.json` and `ru.json` for the client (all 629 keys, the same
  placeholders and `_one` plurals, Western digits kept through `intlLocale()`), the server messages in `Localizer.cs`, the
  locale lists, a Cairo face for Arabic and the system stack for Cyrillic, and the stub's answers in both languages. The
  Arabic is Modern Standard in a light register that avoids gendered forms; the Russian is the informal "ты" throughout.
  Both were written by the builders and need a native review before they reach people, the terms and the privacy policy
  included. The daily prompts, the comparer's style notes and the service worker's precache follow in the same round.

### After review

The review between the merge and the hand-off (2026-09-12) read the round against the code. It confirmed the shape: the
guest check before the signup, Pro as a cap, comparisons private and unpostable, items indexed from the stylist's words,
Today's look as a hashtag, the strip for a follow-up, the birth date, `--verify`, the numbers page and the legal pages
written from the code all stand as decided. What it changed, most important first:

- **The guest cap counts looks given, and attempts are braked separately.** The per-address half of
  `Plans:GuestChecksPerDay` lived in a fixed-window rate-limit policy, which spent its one permit on a refused upload, a
  502 or a dropped connection and then said "that was your free look" for a look never given. Now the handler counts
  the address in memory (`GuestAddressCounter`: a slot reserved for the check in flight, counted only once a row is
  stored with a status other than error), exactly as the per-cookie count reads the rows, and `error.guest_limit` is
  only ever sent for a look given. The `guest` policy stays as the brake on attempts (`Plans:GuestAttemptsPerDay`,
  twenty a day per address, 429 `error.too_fast`), well above the cap so a bad photo never locks a shared address out
  of its look. A restart forgets the address count; the single-process limitation already said as much.
- **One place counts the day.** `Services/Spend.cs` is what the allowances mean: stored checks and comparisons over the
  rolling 24 hours, failed calls left out, for the account, the guest cookie, `me.checksToday` and the global ceiling.
  The check route's global ceiling now counts comparisons as the compare route's did, so `Limits:ChecksPerDayGlobal`
  means the same thing on both.
- **`Retry-After` names the call whose expiry frees a permit.** The (count − cap + 1)th oldest, not the oldest: the
  two are the same only while the count equals the cap, and a lapsed Pro or a lowered cap leaves more calls in the
  window than the cap allows.
- **A Pro account at its ceiling hears the number.** `error.rate_limited` with the cap on the check route, as on the
  compare route, not "go Pro"; the client offers "Go Pro for more" only to a Free account at its cap. `/api/config`
  publishes `Plans:ProChecksPerDay` clamped to `Limits:ChecksPerDay` (what a Pro account really gets, what the Pro
  page promises), and the start log warns when the plan's number is above the ceiling. The `error.plan_limit` message
  on both routes quotes the same clamped number.
- **A claim that cannot copy a file aborts.** A full disk or a file missing from the store used to save an owned row
  pointing at the guest folder, which account deletion and the sweeper would then miss. Now the fresh copies are
  removed, the rows and the cookie stay the guest's, the route answers 500 with a clear log line, and the client claims
  again on its next load; the transcoder leaves a guest's clip alone until the claim queues it, so the two never move
  the same file. Account deletion deletes the rows' files by path as well as by folder.
- **The pilot upgrade rebuilds tables.** Round 9 made `Checks.UserId` nullable and gave `Posts.BeforePostId` its
  `ON DELETE SET NULL`; adding missing tables, columns and indexes covered neither. The upgrade now compares column
  nullability and foreign keys with the model and emits the alter and add operations (a table rebuild in SQLite), with
  foreign-key enforcement off around the batch as `Migrate()` does, and rolls the whole batch back, the `.bak` kept, if
  any table would come out with fewer rows. The log line names the altered columns and the added foreign keys, and the
  test builds the old file from the migrations up to Round 8 with the history table dropped.
- **One subscription per account, ends from Stripe's events.** `POST /api/billing/checkout` answers 409
  `error.already_pro` for an account that is Pro, so a stale tab cannot open a second subscription on the same
  customer. `invoice.paid` used to add 35 days to the previous end, which ran ahead by the difference every cycle; it
  now sets the end from the invoice's period plus three days of slack, never below what is there, and
  `customer.subscription.updated` is handled (`active` or `trialing` to the period end plus slack; `past_due`,
  `unpaid` or `paused` down to three days from now at most). A completed checkout stacks its 35 days on a period still
  running instead of re-stamping it.
- **Insights are behind Pro exactly when comparisons are.** `GET /api/users/me/insights` answers 403 when
  `Plans:CompareNeedsPro` is on and the account is not Pro, `#/insights` shows the same Pro card as `#/compare`, and
  the Pro page lists comparisons and insights as benefits only when the server keeps them for Pro: the page sells what
  this server actually gives.
- **The check screen honours guests off.** With `Plans:GuestChecksPerDay` at 0 a signed-out visitor gets the sign-in
  prompt and a disabled submit instead of a free-check banner and a wasted upload; with guests on, the banner's hint
  carries the server's number.
- **The claim toast belongs to the claim.** "Saved to your account." is announced by the call that actually moved the
  rows, with the count, and `me` is read again after it so the cap line counts the claimed check; the claim on the
  result screen is one promise kept on the result, so a tab and Back during it no longer leaves Post it disabled.
- **The privacy wording matched the code.** A posted look carries the score's breakdown, and the stylist's item names
  are used to find the look in search; the tip, the notes on each item and the accessories read stay private. The
  privacy policy, the post sheet and the guidelines still said the items never go public, which stopped being true with
  the search by piece; the pages are version 2, dated 2026-09-12, in four languages.
- **A comment's author ref carries `verified`**, like every other ref; the comment list had been built before the flag.
- **The sixteen rule is measured on the phone's day.** Signup takes an optional `today`; when it is within a day of
  the UTC date it is the day the rule and the not-in-the-future check use, so nobody is stopped on their birthday east
  of Greenwich or let in the evening before it west. Anything else falls back to UTC; a self-declared date already
  rests on the person's word, so a day of slack around midnight hands nobody anything the rule did not.
- **The prompt calendar has an epoch.** `DailyPrompts.For` counts whole UTC days since 31 December 2025 modulo thirty
  (through 2026 that equals the day of the year, so the pilot's calendar is unchanged), so late December's prompts do
  not come back within the week of 1 January.

What the review raised and the round kept as it was:

- **No in-app cancel yet.** Still Stripe's side or `--pro off`, and the terms say to write to us; a customer-portal
  link is the next step, not a fix.
- **Webhook idempotency, accepted.** With the ends read from the events, only a replayed `checkout.session.completed`
  can stack a period, and Stripe replays only what was not answered 2xx; an events table waits for money that is real.
- **The comparison verdict is the model's call.** The winner is the one the stylist named; the server normalises the
  word and falls back to the higher score only when the word is unusable (A on a tie). Deciding it from the two scores
  ourselves would be a rule dressed as taste.
- **The insights' lines are a subset on purpose.** The best intent always gets a line; the weak category, the missing
  accessories and the streak get one only when they say something (nothing at 0%, no streak under two). A line for
  every number would be the tiles read aloud.
- **The Today cache edge.** The strip draws what it has and refetches in place once the cached prompt is older than
  the feed's ten minutes, so across midnight UTC yesterday's prompt can show for those minutes until a refetch or a
  pull to refresh. A daily hashtag can carry that; a clock-aligned refetch is not worth its own timer.
- **`--verify` on a person.** The command sets the flag on the account with the handle, whatever its type, and the
  client draws the check only inside the BRAND mark, so on a person it shows nowhere. The owner runs it by hand for
  accounts they know; a type check would be a rule for a case the owner already controls.
- **The 24px pill.** `.tag` (the intent label on a card, the PRO badge, "clip ready") stays at 24px: it is a label,
  not a control, and the 44px rule in `DESIGN.md` is for touch targets.

### Objections kept out of the code (owner wins)

- **A guest check is a model call with nobody behind it.** Kept because the first wow must come before the signup;
  the cost is bounded per cookie and per address (looks given, not attempts; attempts are braked separately) and
  globally, `Plans:GuestChecksPerDay=0` exists, and the README's limitations say a script that rotates addresses gets
  one check per address.
- **The webhook keeps no event ids.** A replayed `checkout.session.completed` stacks one period; a replayed
  `invoice.paid` or `customer.subscription.updated` names the same period and changes nothing. Acceptable for a pilot,
  named in the README; an events table is the fix when money is real.
- **Stripe has not run against a live account.** Checkout and the webhook were built against a recording stand-in and
  signed test events; DEPLOY.md says to run test mode and the Stripe CLI before switching the provider.
- **No in-app cancel for Pro.** Cancelling is on Stripe's side or `--pro off`, and the terms say to write to us. A
  customer-portal link is the next step, not this round's.
- **The birth date is still self-declared**, and **verification has no process**. Both are the honest amount for a
  pilot and both sit in the launch checklist with age assurance.
- **No block-user.** Apple's UGC checklist expects one before a store submission (`STORE.md` says so); reports and the
  queue are what exists.
- **No native push in the store shells.** Web Push does not run inside the WebViews; the shells ship without it or a
  sender for APNs and FCM comes first (`mobile/README.md`).
- **The screenshots in the kit are test fixtures**: the browser test's synthetic outfit and Chromium's fake camera.
  Every README in the path says to replace them before a store submission.
- **The legal pages are not legal advice.** Written from the code, in each language, with a placeholder for the
  governing law; a lawyer reads them before launch.
- **Sounds on posts, rejected.** Music on looks would bring licensing the app cannot carry, would break the muted feed
  that clips were designed for (autoplay muted, the sound disc opt-in), and would pull the loop away from the check
  toward a video app the world already has. Named here so nobody assumes it is planned.

## Round 10 — items on a look, the weekly flames board (2026-09-12 → )

The owner asked for items on a post ("tap the pants: Nike pants, the model, the store link"; typed by the person, or
suggested by the stylist when a mark is visible) and a weekly flames board (a top ten that is a competition and grows
from there); sounds on posts stay rejected (Round 9). Builders work in parallel from one skeleton; the lead rewrites
this section after the merge, from the code.

### Round 10 skeleton (the lead, before the builders)

The shared contract. Everything here compiles, migrates and is tested; nothing here is the feature.

- **Schema** (`Data/Migrations/20260912151344_Round10.cs`, applied at start like the others). `PostItems` is keyed on a
  new `Id` (GUID) instead of `(PostId, Name)` and gains `Brand` (≤ 40), `Model` (≤ 60), `Url` (≤ 500, http(s), stored as
  given), `Source` (`Stylist` | `User`), `X`/`Y` (0..1, the dot; null = listed, not placed), `Position`, `Confirmed`;
  `Name` and `Category` stay as Round 9 wrote them and `IX_PostItems_Name` stays, so `/api/search` by piece works
  unchanged over old and new rows. The migration is hand-edited: existing rows get a random id, `Source = Stylist` and
  their position in insertion order *before* SQLite rebuilds the table around the new key (the generated migration alone
  would copy one zero id into every row and fail on the second). New tables: `BoardExclusions { PostId (PK), ByUserId,
  Reason ≤ 200, CreatedAt }`, `WeeklyWinners { Id, WeekStart (UTC date), Board, Rank, PostId?, UserId, Fires, Score? }`
  with a unique index on `(WeekStart, Board, Rank)` (the closer's idempotence guard) and indexes on `(UserId, WeekStart)`
  and `PostId`, and `Counters { Name (PK), Value }` for the two tallies the metrics read. `Notifications.Rank` (int?) for
  `board_rank`. Indexes `PostItems(Brand)`, `PostItems(Category)`, `PostItems(PostId, Position)`, `Fires(CreatedAt)`.
  Cascades: exclusions and winners go with the account; a deleted look sets `WeeklyWinner.PostId` null and the place
  stays; `DELETE /api/users/me` deletes the new rows explicitly like every other table.
- **Options.** `Board` → `BoardOptions { WeekStartsOn = Sunday, TimeZone = "Asia/Jerusalem", MinChecksToCount = 1,
  MaxPerFirerPerAuthor = 3, NewAccountDays = 2, Size = 10, RisingDays = 30, Sponsor? { Name, Handle, PrizeText, Url } }`
  (`Sponsor` is null until `Board:Sponsor:Name` is set). `Affiliate` → `AffiliateOptions { Disclosure = true, Hosts = {} }`;
  `Hosts` maps a host to the query string `/api/items/{id}/out` appends (`"amazon.com": "tag=orevosh-20"`), matched
  case-insensitively with subdomains (`AffiliateOptions.ParametersFor`); nothing is appended by default, on purpose.
  Both are in `appsettings.json` with their defaults.
- **Rubric v3.** `OutfitAnalyzer.PromptVersion = "v3"`; every `items[]` entry has `brand_seen` (string or null, required)
  with the rule in the schema and a BRANDS section in the prompt: only a brand whose mark, logo or unmistakable signature
  is visible, null otherwise, never a guess from style. `OutfitItem.BrandSeen` (≤ 40, the words "null"/"none"/"unknown"
  read as null) rides in the stored feedback and on `GET /api/checks/{id}` as `brandSeen`. `PostItems.AddFrom` never
  copies it: the brand reaches a row only through the person (`PATCH /api/posts/{id}/items`). The e2e stub answers
  `brand_seen` (null everywhere, `"Nike"` on the English running shoes) and rejects a schema without it.
- **DTOs** (`Endpoints/Dtos.cs`). `PostItemDto { id, name, category, brand?, model?, url?, host?, source, x?, y?,
  confirmed }`; `PostItemInput` + `UpdateItemsRequest { items[] }`; `ItemsDto`, `BrandDto`, `BrandsDto`; `PostDto` gains
  `items?` and `itemCount` (null and 0 until items-server reads them in `PostReader`); `BoardRowDto`, `BoardSponsorDto`,
  `BoardMeDto`, `BoardDto { weekStart, weekEnd, closesIn (seconds), closed, looks, people, rising, intents{Intent: [..]},
  picks, sponsor?, me? }`; `WeeklyWinnerDto`, `HallWeekDto`, `HallDto`; `BadgeDto { board, rank, weekStart }` on `MeDto`
  and `ProfileDto` (null in the skeleton); `ExcludeRequest`, `BoardExclusionDto`; `NotificationDto.rank?`;
  `SocialMetricsDto` gains `itemsTagged` (items with a brand, a link or a person as source), `itemOuts` and `boardViews`
  (the two counters).
- **Routes, answering 501** with `error.not_built` in the caller's language until filled: `PATCH /api/posts/{id}/items`
  (session required), `GET /api/items?brand=&category=&q=`, `GET /api/items/brands?q=`, `GET /api/items/{id}/out`,
  `GET /api/board?week=`, `GET /api/board/hall`, `POST /api/admin/board/exclude { postId, reason }` and
  `DELETE /api/admin/board/exclude/{postId}` (behind the moderator gate, now `AdminEndpoints.GateAsync`, public).
  `Endpoints/ItemEndpoints.cs` and `Endpoints/BoardEndpoints.cs` carry the contract in their doc comments; `Stubs.cs`
  goes with the last stub.
- **Server strings** in all four dictionaries: `error.not_built`, `error.item_not_found`, `error.item_invalid`,
  `error.item_url_invalid`, `error.items_too_many {0}`, `error.item_position_invalid`, `error.board_week_invalid`,
  `error.board_excluded`, `error.board_not_excluded`, `push.board_rank {0}` ("You finished #{0} this week"; the push
  worker passes the rank there instead of the actor). The Arabic and Russian lines are plain copy by the lead and need
  the same native review as the rest.
- **Notification kind `board_rank`**: `NotificationType.BoardRank`, `Notification.Rank`, `Notifier.Add(..., rank:)`,
  `PushJob.Rank`, the push line and its tap target (`/#/board`), the activity line `activity.board_rank` ("You finished
  #{rank} this week") and its target on the client. No sender yet: the closer is the board-server builder's.
- **Client.** Routes `#/board`, `#/board/hall` (`board-hall`), `#/items/<brand>/<category>` and `#/items?q=` (`items`;
  the query belongs to the view) in `core.js`, under the Explore tab; stub views `views/board.js` and `views/items.js`
  that draw the title and a "coming in this round" line; `ICONS.tag` next to the existing `ICONS.trophy`; 78 keys under
  `items.*`, `board.*`, `hall.*`, `badge.*`, `affiliate.*` plus `activity.board_rank` in all four i18n files (707 keys
  each, parity checked: same key set and placeholders as English; the `_one` forms carry no `{n}` by design).
- **Seams for the builders.** `IClock` (`Services/Clock.cs`, `SystemClock` registered; the board's window math and the
  closer read it, nothing else does) and `TestApp.Clock` (a `FakeClock` with a settable `Now`); `TestApp.Settings`
  (any `"Section:Key"` override, e.g. `Board:TimeZone`, `Affiliate:Hosts:amazon.com`, `Board:Sponsor:Name`);
  `Counters.IncrementAsync/ReadAsync` (an upsert, safe under concurrent requests); `PostItems.IsStoreUrl/HostOf` and
  the length constants (`TypedNameMaxLength` 40, `BrandMaxLength` 40, `ModelMaxLength` 60, `UrlMaxLength` 500,
  `MaxTagged` 12); `BoardName` (`looks | people | rising | picks | intent:<Intent>`); `AdminEndpoints.Admin(context)`
  for the moderator's row inside the gate.
- **File ownership, as the lead understands it** (the plan's five builders): *items-server* owns
  `Endpoints/ItemEndpoints.cs`, `Services/PostItems.cs` (validation of the typed list), the items on cards and the post
  in `Services/PostReader.cs`, the `itemsTagged` definition in `MetricsEndpoints.cs`, and `tests/ItemTests.cs`;
  *items-client* owns `views/items.js`, the post sheet's tagging in `views/check.js` (chips pre-filled from the
  stylist's items with "Looks like Nike? Confirm / Edit / Not a brand", brand autocomplete from `/api/items/brands`,
  the dot on the preview), "The look" list, the dots behind the tag toggle and the item sheet in `views/post.js`, the
  item-search reuse in `views/explore.js`, the items' CSS in `app.css`, and the `items.*`/`affiliate.*` copy it needs
  beyond this set in `en`/`he`; *board-server* owns `Endpoints/BoardEndpoints.cs`, new `Services/Board*.cs` (the window
  math in the configured zone, the eligibility rules, the five boards, the 60-second cache, the closer as a hosted
  service registered in `Program.cs`), the badge on `MeDto`/`ProfileDto` in `UserEndpoints.cs`, and
  `tests/BoardTests.cs`; *board-client* owns `views/board.js`, the "This week" strip in `views/explore.js`, the
  reset-day card in `views/feed.js`, the badge in `views/profile.js`, the board's CSS, and the `board.*`/`hall.*`/`badge.*`
  copy beyond this set in `en`/`he`; *phase 2* owns the `ar`/`ru` lines of whatever the four add, README/DEPLOY/this
  section, the e2e, and the review. Shared files (`Dtos.cs`, `Social.cs`, `AppDbContext.cs`, `Localizer.cs`, `core.js`,
  the migration) change through the lead.
- **Not built on purpose, so nobody assumes it is:** `PostReader` reads no items (`items` null, `itemCount` 0); no
  window math, no closer, no cache, no badge; `/out` redirects nothing; `itemOuts` and `boardViews` stay 0 until the
  routes increment them.
- **Tests: 440 → 470**, all green: the migration over a Round 9 file keeps its items and mints ids, the app starts on
  such a file and the item search finds the migrated looks, every stub answers 501 behind its gate, the options bind
  with the plan's defaults and take overrides, the clock can be set, counters persist and reach the metrics, posting
  writes stylist rows with ids and order and never a brand while the check carries `brandSeen`, account and look
  deletion take the board rows with them, the unique index refuses a second place, and `board_rank` carries its rank
  through the row, the DTO and the push line.

### Objections to keep out of the code (from the plan; the lead confirms after the merge)

- The AI never publishes a brand: it suggests only when a mark is visible; the person confirms. Wrong attribution is the
  one mistake a fashion app cannot afford.
- Links leave through one door (`/out`) so the app can decorate, count and later revoke; the disclosure is always shown
  when a link earns anything.
- The board counts fires from people who use the app (a check), capped per pair, so friends cannot carry a look; the
  stylist's picks board cannot be gamed at all.
- Sounds on posts: not built (licensing, the muted feed, the core loop).

