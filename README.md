# OREVOSH

A social app for looks. Pick where the outfit is going (date, office, streetwear…), add a photo, and a
stylist scores it **relative to that intent**, lists what you are wearing, what works, and **the one tip**.
The check is private. Post it and it joins a feed where people react with fire, comment, save and follow.
Tag the brands you wear with `@brand`, add `#tags`, and brands feature the community looks they love, open
challenges with a prize, and tag products on their own looks. Browsing needs no account.

English is the default language, Hebrew (RTL) ships alongside it, and the stylist writes its feedback in the
user's language. The web app installs to the home screen on iPhone and Android and behaves like a native app
(full screen, bottom tabs, pull to refresh, double-tap to fire, bottom sheets).

## Run it in 5 minutes

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and an Anthropic API key.

```bash
export ANTHROPIC_API_KEY=sk-ant-...        # the only secret; never put it in appsettings
cd src/FitCheck.Api                        # the .NET project keeps its original name for now
dotnet run
```

On Windows PowerShell the first line is `$env:ANTHROPIC_API_KEY="sk-ant-..."` (quotes included), and the
variable lives only in that window.

Open http://localhost:5000 (the port is printed on start). The SQLite database (`orevosh.db`) and the private
photo folder (`storage/`) are created next to the project on first run; both are git-ignored. A `fitcheck.db`
left over from an earlier build is simply unused and can be deleted.

### On a phone

Phones need HTTPS for the camera, the share sheet, the home-screen install and the Secure session cookie, so
put a tunnel in front of the local server:

```bash
# Cloudflare Tunnel (no account needed for a quick tunnel)
cloudflared tunnel --url http://localhost:5000

# or ngrok
ngrok http 5000
```

Send the printed `https://…` URL to your pilot users. On iPhone: Share → "Add to Home Screen". On Android,
Chrome offers "Install" by itself and the app shows a one-time hint.

### Read the pilot metrics

```bash
curl -s http://localhost:5000/api/metrics/pilot | jq
```

The first block covers checks with `status = "ok"` (`returnRate` = users whose second OK check happened at most
7 days after their first ÷ users with at least one OK check). The `social` block counts users, brands, posts,
fires, follows, comments, open and ended challenges, votes, mentions, featured looks, and people active in the
last 7 days.

### Run the tests

```bash
dotnet test        # from the repository root (FitCheck.sln)
```

181 tests: magic-byte detection, the disk image store, analyzer mapping and clamping, locale
matching, the Anthropic client against a scripted HTTP handler, and endpoint tests against the real app with a
scripted vision client: signup and login rules, the CSRF header, uploads and 413/415/429/502, the daily and
global caps, posting, fire, comments, saves, follows, the feed tabs and the For you ranking, reports hiding
content, challenges with votes and winner resolution, notifications, tags and mentions, featured looks,
avatars, account-type switches, interests, Explore and search, account deletion removing files and fixing
other people's counters, and the metrics math on a seeded dataset.

There is also a browser test in [`tools/e2e`](tools/e2e/README.md): Playwright drives the real client in a
phone viewport against the real API with only the Anthropic API stubbed, as three people (a person in English,
a brand, and a person browsing in Hebrew), from signup and the welcome screen through posting with tags and
mentions, featuring, Explore, a challenge and its winner, to deleting an account.

### Check the calibration before inviting people

With the server running and a folder of 10 varied outfit photos:

```bash
python3 scripts/calibrate.py ./sample-photos --intent Casual --intent Date --language en --language he
```

It signs up a throwaway account, checks every photo for each intent and language, prints a table, the score
distribution per group, latency, and a scan of every feedback text for body, face, age or gender words (rule
1), then deletes the account and writes a JSON report. If most scores land on 7–8 it says so: tighten the
calibration text in `Services/OutfitAnalyzer.cs`, bump `PromptVersion`, and compare the two reports.

## Configuration

`src/FitCheck.Api/appsettings.json`:

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=orevosh.db` | SQLite file, created on first run (`EnsureCreated`, no migrations). A relative path resolves against the project folder |
| `Anthropic:Model` | `claude-sonnet-5` | Must support forced tool use: Sonnet 5, Opus 5, the 4.x family, Haiku 4.5 |
| `Anthropic:MaxTokens` | `1200` | Output budget for the tool call (Hebrew is token-heavy) |
| `Anthropic:BaseUrl` | `https://api.anthropic.com` | Override to point at a stub in tests |
| `Storage:Root` | `storage` | Private photo folder (checks and avatars). Relative paths resolve against the content root, never `wwwroot` |
| `Storage:MaxImageBytes` | `6291456` | Upload limit for checks (6 MB). Avatars are capped at 2 MB. The client downscales first |
| `Limits:ChecksPerDay` | `20` | Per-user cap over a rolling 24 hours |
| `Limits:ChecksPerDayGlobal` | `1000` | Ceiling across all users over a rolling 24 hours |
| `Limits:SignupsPerHourPerIp` | `50` | New accounts per client address per hour (address taken from `X-Forwarded-For` behind the tunnel) |
| `Limits:LoginsPerQuarterHourPerIp` | `30` | Login attempts per client address per 15 minutes |
| `Limits:ReportsToHide` | `3` | Reports from distinct people after which a post or comment is hidden |

Any key can be overridden with an environment variable, e.g. `Limits__ChecksPerDay=5`. `ANTHROPIC_API_KEY`
is read from the environment only.

## API

All responses are JSON (camelCase, null properties omitted). Errors are `{ "error": "<message in the caller's
language>" }`. Language for messages: an explicit `language` field wins, then `Accept-Language`, then English.

Sessions are an HttpOnly, SameSite=Strict cookie (`orevosh.session`) set by signup and login. **Every non-GET
call under `/api` must carry the header `X-Requested-With: Orevosh`** or it is refused with 403; that header
is the CSRF guard. Endpoints marked 🔒 need a session. Ids are GUIDs. `intent` is one of `Casual, Date,
Streetwear, OldMoney, Minimal, Office, Party, Sport`.

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/auth/signup` | `{ handle, password, confirmed16Plus, language, displayName? }` | `201` me. Handle: 2–40 letters, digits, dots or underscores, unique case-insensitively; password 8–200. 400 invalid, 409 taken, 429 too many signups from one address |
| `POST /api/auth/login` | `{ handle, password }` | `200` me. 401 for a wrong handle or password (same message for both), 429 too many attempts |
| `POST /api/auth/logout` 🔒 | — | 204 |
| `GET /api/auth/me` 🔒 | — | `{ id, handle, name, accountType, language, bio, website, streak, unreadNotifications, avatarUrl, interests }` |
| `PATCH /api/users/me` 🔒 | `{ language?, displayName?, bio?, website?, accountType?, interests? }` | Updated me. `accountType` is `Person` or `Brand`; `interests` is a list of intents (≤ 8); website must be https |
| `POST /api/users/me/avatar` 🔒 | multipart `image` (JPEG/PNG/WebP ≤ 2 MB) | `200` me with a versioned `avatarUrl` |
| `DELETE /api/users/me/avatar` 🔒 | — | `200` me |
| `GET /api/users/{handle}/avatar?v=` | — | The photo, `Cache-Control: public, max-age=86400` |
| `DELETE /api/users/me` 🔒 | — | 204. Deletes the account, every check, photo, avatar, post, tag, mention, comment, fire, save, vote, follow and notification, and fixes other people's counters and featured marks |
| `GET /api/users/me/checks` 🔒 | — | Last 50 checks, newest first, each with `postId` when posted |
| `GET /api/users/me/saved` 🔒 | `?offset&limit` | Saved posts, newest first |
| `GET /api/users/{handle}` | — | Public profile: counts, best score, streak, `avatarUrl`, `featured`, `community`, `viewer.following` |
| `GET /api/users/{handle}/posts` | `?offset&limit` | That person's public posts |
| `GET /api/users/{handle}/community` | `?offset&limit` | Public posts that mention this account |
| `GET /api/users/{handle}/featured` | `?offset&limit` | Brand: posts it featured. Person: their posts that were featured |
| `POST` / `DELETE /api/users/{handle}/follow` 🔒 | — | `{ followers, following }`. 400 when following yourself |
| `POST /api/checks` 🔒 | multipart: `intent`, `occasion?`, `language`, `image` | `201 { id, intent, occasion, language, createdAt, latencyMs, status, score, feedback, postId }`. 413 too large, 415 not JPEG/PNG/WebP, 429 over a cap (with `Retry-After`), 502 model failure |
| `GET /api/checks/{id}` 🔒 | — | The check, owner only (404 otherwise) |
| `POST /api/posts` 🔒 | `{ checkId, caption?, challengeId?, products? }` | `201` post. The check must be yours, `ok`, and not yet posted; caption up to 140 characters, its `#tags` (first 5) and `@mentions` of existing handles (first 5) are stored and mentioned accounts are notified; a caption carrying an open challenge's hashtag enters that challenge (once per person; `challengeId` is still accepted); `products` (brands only, up to 3) are `{ label, url, price? }` with https URLs |
| `GET /api/posts/{id}` | — | The post: `user, intent, score, intentMatch, headline, caption, challengeId, challengeTitle, fireCount, commentCount, fired, saved, isMine, hidden, votes, products, imageUrl, createdAt, tags, mentions, featuredBy`. Hidden posts are visible to their author only |
| `GET /api/posts/{id}/image` | — | The photo (`Cache-Control: private`). The only route that serves a check photo, and only for a visible post |
| `DELETE /api/posts/{id}` 🔒 | — | 204, author only. The photo becomes private again |
| `POST` / `DELETE /api/posts/{id}/fire` 🔒 | — | `{ fireCount, fired }`. One per person; idempotent |
| `POST` / `DELETE /api/posts/{id}/save` 🔒 | — | `{ saved }` |
| `POST` / `DELETE /api/posts/{id}/feature` 🔒 | — | `{ featuredBy }`. Brands only; the post must mention the brand or be an entry in one of its challenges; one brand per post (409 when another brand was first); the author is notified |
| `POST /api/posts/{id}/report` 🔒 | `{ reason? }` | 204. One report per person per post; at `Limits:ReportsToHide` the post is hidden |
| `GET /api/posts/{id}/comments` | — | Comments, oldest first, hidden ones excluded |
| `POST /api/posts/{id}/comments` 🔒 | `{ text }` | `201` comment (1–200 characters) |
| `DELETE /api/comments/{id}` 🔒 | — | 204, by the comment's author or the post's author |
| `POST /api/comments/{id}/report` 🔒 | `{ reason? }` | 204, same rule as posts |
| `GET /api/feed` | `?tab=foryou\|following\|top\|fresh&intent&offset&limit` | `{ items, nextOffset }`. `foryou` (default) ranks the last 30 days by fire, comments, people you follow, your interests and recency; `following` (🔒) is newest from people you follow; `top` is the most fire in 7 days; `fresh` is newest. `intent` filters. Limit 1–30 |
| `GET /api/explore` | — | `{ trendingTags, brands, topLooks, challenges }`: tags from the last 7 days, brands by followers, top 6 looks by fire in 7 days, up to 5 open challenges |
| `GET /api/search?q=` | — | `{ users, tags }` for a 1–40 character query: handle prefix or display-name substring (brands first), tag prefix |
| `GET /api/tags/{tag}/posts` | `?offset&limit` | Public posts carrying the tag, newest first |
| `GET /api/challenges` | `?state=open\|ended` | Challenges with entry and vote counts, the top three entries and `viewer` (`isBrand, hasEntered, votedPostId, myEntryId`) |
| `POST /api/challenges` 🔒 | `{ title, brief, intent, prize, prizeUrl?, endsAt, tag? }` | `201`, brand accounts only. Ends between 1 hour and 60 days from now. `tag` is the entry hashtag (derived from the title when missing, made unique among open challenges) |
| `GET /api/challenges/{id}` | — | `{ challenge, entriesByVotes, winner }`. Reading an ended challenge fixes its winner if that has not happened yet |
| `POST` / `DELETE /api/challenges/{id}/vote` 🔒 | `{ postId }` / — | `{ votedPostId, votes }`. One vote per person per challenge, movable while open; not for your own entry, and not by the brand that opened it |
| `GET /api/notifications` 🔒 | — | `{ items: [{ type, actorHandle, actorName, postId, challengeId, createdAt, read }], unread }`. Types: `fire, comment, follow, vote, entry, ended, won, mention, featured` |
| `POST /api/notifications/read` 🔒 | — | 204, marks everything read |
| `GET /api/metrics/pilot` | — | See above |

## How it is built

```
FitCheck.sln
src/FitCheck.Api/
  Program.cs                      wiring, EnsureCreated, cookie auth, rate limiter, CSRF header check, static files
  Domain/                         StyleIntent, AppUser, OutfitCheck, OutfitFeedback, Social.cs (posts, tags, mentions,
                                  comments, fire, saves, follows, challenges, votes, notifications, reports), options
  Data/AppDbContext.cs            SQLite via EF Core; unique indexes carry the one-per-person rules
  Services/OutfitAnalyzer.cs      ← the stylist: system prompt, intent guide, tool schema, PromptVersion, mapping
  Services/AnthropicVisionClient  Messages API over HttpClient: base64 image + forced tool call, 60s timeout, one retry
  Services/CaptionParser.cs       #tags and @mentions out of a caption
  Services/FeedRanker.cs          the For you score, a pure function
  Services/DiskImageStore.cs      storage/<userId>/<checkId>.<ext> and storage/<userId>/avatar.<ext>, behind IImageStore
  Services/Sessions.cs            cookie sign-in and the current user id
  Services/Notifier.cs            activity rows, deduplicated per actor and target
  Services/ChallengeResolver.cs   fixes the winner exactly once when a challenge has ended
  Services/PostReader.cs          posts → DTOs with tags, mentions, featured-by and the viewer's state, in batches
  Services/Localizer.cs           server messages (en/he) and Accept-Language matching
  Endpoints/                      auth, users, checks, posts (+ comments), feed, explore (+ search, tags), challenges,
                                  notifications, metrics
  wwwroot/index.html, app.css     the shell and the design system: Night Atelier, see DESIGN.md (logical properties for RTL)
  wwwroot/app/core.js             state, i18n, API, DOM kit, router, bottom sheets, gestures, look cards
  wwwroot/app/views/*.js          one module per screen: feed, post, explore, challenges, check, activity, profile,
                                  auth, settings
  wwwroot/manifest.webmanifest,   the installable app; the service worker caches the shell only, never the API
  wwwroot/sw.js, wwwroot/icons/
  wwwroot/i18n/en.json, he.json   UI strings; add a locale by adding a file
tests/FitCheck.Api.Tests/         xUnit
tools/e2e/                        optional browser test (Playwright + a stub of the Anthropic API)
scripts/calibrate.py              calibration run against the real model: score spread, latency, rule 1 scan
```

The model is forced to call a tool (`tool_choice: {type: "tool"}`) whose input schema is our feedback shape,
so the answer is always JSON we can validate. Scores are clamped to 1–10, intent match to 0–100, and anything
descriptive is dropped when the status is not `ok`.

### Rules the code enforces

- **Clothes, never the person.** The prompt forbids any reference to body, face, skin, age or gender, and the
  UI copy follows the same rule.
- **Checks are private; posting is a separate choice.** Posting publishes the photo, the intent, the score,
  the headline and your caption. The tip and the item breakdown never go public. Deleting the post makes the
  photo private again.
- **Photos are never served by path.** Check photos and avatars live under `Storage:Root`, outside `wwwroot`.
  The post image route (visible posts only) and the avatar route are the only doors.
- **16+ only, self-declared.** Signup fails without the checkbox. See the limitations below.
- **Bad input is refused.** Non-outfit photos get a friendly state. Nudity, sexual content or an apparent
  minor gets a neutral rejection: the photo is deleted immediately, nothing but the status is stored, and the
  model's own words are never shown. Only `ok` checks can be posted.
- **One endpoint deletes everything.** Account, checks, photos, avatar, posts, tags, mentions, comments, fire,
  saves, votes, follows and notifications, with other people's counters and featured marks corrected.
- **No hallucinated brands or items.** Instruction in the prompt; the model may only name what is visible.
- **One of each per person.** Fire, save, follow, report and challenge vote are unique per person and target
  at the database level; repeating is idempotent, never a 500. You cannot follow yourself, report your own
  look, or vote for your own entry.
- **Tags and mentions are parsed on the server**, so a caption cannot carry a mention the client invented,
  unknown handles are dropped, and every mentioned account is told exactly once.
- **Featuring is earned.** A brand can feature a look only when the look mentions the brand or entered one of
  its challenges, and one brand per look. The creator is told. Undoing is the featuring brand's alone.
- **Challenges are a hashtag.** A brand picks a hashtag and a prize; a look posted with the hashtag while the
  challenge is open is an entry, one per person, any intent. A brand neither enters nor votes in its own; the
  winner is the entry with the most votes (ties go to the earlier entry), fixed once and notified once. The
  prize changes hands between the brand and the winner; the app takes no payments.
- **Reports hide, people decide.** Three reports from different people hide a post or a comment from everyone
  but its author, who sees an "under review" badge.
- **Sessions are cookies, writes need a header.** HttpOnly, SameSite=Strict, Secure over HTTPS, 90 days
  sliding. Passwords are hashed with ASP.NET Core's `PasswordHasher`. Login and signup are rate limited per
  client address.
- **Cost control.** 20 checks per user per rolling 24 hours (429 with a friendly message), counted including
  checks still in flight; a global ceiling of 1000 checks a day; a 6 MB upload cap; the client downscales to
  1280px JPEG (avatars to 320px) before uploading. Failed model calls do not count toward the caps.

## Known limitations (read before inviting anyone)

- **Age is self-declared.** A checkbox is not age assurance. Before any public launch, integrate the Apple
  and Google age-signal APIs (or an equivalent provider) and gate account creation on the result.
- **Brand accounts are self-declared.** Anyone can switch to brand mode in settings. Fine for an invited
  pilot; verification belongs in the launch checklist with age assurance.
- **No password recovery.** Accounts have no email address, so a forgotten password means a new account.
- **No moderation screen.** Hidden posts and comments stay hidden until someone clears `Hidden` and
  `ReportCount` in the database. Featured looks are the brand's call with no review step.
- **The For you feed is a formula, not a recommender.** It ranks by fire, comments, follows, interests and
  recency; good enough for a pilot, and documented in `PHASE3.md`. Search is a prefix match on SQLite, fine at
  pilot scale.
- **Metrics are unauthenticated.** `/api/metrics/pilot` only returns aggregates, but put it behind a
  password or an allow-list before the URL leaves the team.
- **Single process, single SQLite file.** The in-flight reservation that closes the cap race lives in memory;
  run one instance.
- **The limiters trust `X-Forwarded-For`.** Right behind the tunnel; if Kestrel is exposed directly, the
  global daily ceiling bounds the damage. Raise `Limits__SignupsPerHourPerIp` for a launch hour on a shared
  network.
- **Cookies are Secure only over HTTPS.** Plain `http://localhost` works for development; anything users reach
  must be behind HTTPS (the tunnel).
- **Calibration is unverified until you run it.** The build was tested against a stubbed model; run
  `scripts/calibrate.py` on real photos before judging scores.
- **Photos stay on disk until the look or the account is deleted.** There is no retention job yet.
- **The .NET project is still called `FitCheck.Api`.** A mechanical rename for when the repository gets its
  final name; nothing a user sees says FitCheck.

## Not in this version

Direct messages, push notifications, share images, payments or prize fulfilment inside the app, native
wrappers, sign-in with Apple or Google, closet memory, a blob store behind `IImageStore`, and a moderation
dashboard. None of it is scaffolded on purpose.

## Decisions

Every judgment call made while building, and every place the brief and instinct disagreed, is in
[`DECISIONS.md`](DECISIONS.md). The plans each phase was built from are in [`PHASE2.md`](PHASE2.md) and
[`PHASE3.md`](PHASE3.md); the visual system is [`DESIGN.md`](DESIGN.md).
