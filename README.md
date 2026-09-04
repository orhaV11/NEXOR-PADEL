# FitCheck

A 10-second outfit check that turns into a feed. Pick where the outfit is going (date, office,
streetwear…), add a photo, and a stylist scores it **relative to that intent**, lists what you are wearing,
what works, and **the one tip**. The check is private. If you like the look, post it: the feed shows the
photo, the intent, the score and the headline, and people react with fire, comment, save it, and follow
you. Brands open challenges with a prize, the crowd votes, and the winner is fixed automatically when the
challenge ends.

You do not have to post to use it. Browsing, the challenge boards and profiles are open to everyone;
checking, fire, comments, saves, follows and votes need an account.

English is the default language, Hebrew (RTL) ships alongside it, and the stylist writes its feedback in the
user's language.

## Run it in 5 minutes

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and an Anthropic API key.

```bash
export ANTHROPIC_API_KEY=sk-ant-...        # the only secret; never put it in appsettings
cd src/FitCheck.Api
dotnet run
```

On Windows PowerShell the first line is `$env:ANTHROPIC_API_KEY="sk-ant-..."` (quotes included).

Open http://localhost:5000 (the port is printed on start). The SQLite database (`fitcheck.db`) and the
private photo folder (`storage/`) are created next to the project on first run, wherever you start it
from; both are git-ignored.

**Upgrading from the Phase 1 build:** the database schema changed and there are no migrations. Delete
`fitcheck.db` (and `storage/`, whose photos belong to the old user ids) before starting this version.

### On a phone

Phones need HTTPS for the camera and the share sheet, and the session cookie is marked Secure over HTTPS,
so put a tunnel in front of the local server:

```bash
# Cloudflare Tunnel (no account needed for a quick tunnel)
cloudflared tunnel --url http://localhost:5000

# or ngrok
ngrok http 5000
```

Send the printed `https://…` URL to your pilot users. The client is static files served from `wwwroot`,
same origin as the API, so nothing else needs configuring.

### Read the pilot metrics

```bash
curl -s http://localhost:5000/api/metrics/pilot | jq
```

```json
{
  "totalChecks": 0,
  "usersWithAtLeastOneCheck": 0,
  "usersWithSecondCheckWithin7Days": 0,
  "returnRate": 0.0,
  "avgLatencyMs": 0,
  "scoreDistribution": { "1": 0, "2": 0, "3": 0, "4": 0, "5": 0, "6": 0, "7": 0, "8": 0, "9": 0, "10": 0 },
  "byLanguage": {},
  "byPromptVersion": {},
  "social": {
    "users": 0, "brands": 0, "posts": 0, "fires": 0, "follows": 0, "comments": 0,
    "challengesOpen": 0, "challengesEnded": 0, "votes": 0, "activeUsers7d": 0
  }
}
```

The first block covers checks with `status = "ok"`. `returnRate` = users whose second OK check happened at
most 7 days after their first ÷ users with at least one OK check. `activeUsers7d` counts people who
checked, fired, commented or voted in the last 7 days.

### Run the tests

```bash
dotnet test        # from the repository root (FitCheck.sln)
```

119 tests: magic-byte detection, the disk image store, analyzer mapping and clamping, locale matching, the
Anthropic client against a scripted HTTP handler (request shape, single retry, refusal handling), and
endpoint tests against the real app with a scripted vision client: signup and login rules, the CSRF header,
uploads and 413/415/429/502, the daily and global caps, posting, fire, comments, saves, follows, the three
feed tabs, reports hiding content, challenges with votes and winner resolution, notifications, account
deletion removing files and fixing other people's counters, and the metrics math on a seeded dataset.

There is also a browser test in [`tools/e2e`](tools/e2e/README.md): Playwright drives the real client in a
phone viewport against the real API with only the Anthropic API stubbed, as three people (a person in
English, a brand, and a person browsing in Hebrew), from signup through posting, reacting, a challenge, its
winner, and deleting an account.

### Check the calibration before inviting people

The score calibration lives in the prompt and only a real sample tells you whether it holds. With the
server running and a folder of 10 varied outfit photos:

```bash
python3 scripts/calibrate.py ./sample-photos --intent Casual --intent Date --language en --language he
```

It signs up a throwaway account, checks every photo for each intent and language, prints a table, the
score distribution per group, latency, and a scan of every feedback text for body, face, age or gender
words (rule 1), then deletes the account and writes a JSON report. If most scores land on 7–8 it says so:
tighten the calibration text in `Services/OutfitAnalyzer.cs`, bump `PromptVersion`, and compare the two
reports.

## Configuration

`src/FitCheck.Api/appsettings.json`:

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=fitcheck.db` | SQLite file, created on first run (`EnsureCreated`, no migrations). A relative path resolves against the project folder |
| `Anthropic:Model` | `claude-sonnet-5` | Must support forced tool use: Sonnet 5, Opus 5, the 4.x family, Haiku 4.5 |
| `Anthropic:MaxTokens` | `1200` | Output budget for the tool call (Hebrew is token-heavy) |
| `Anthropic:BaseUrl` | `https://api.anthropic.com` | Override to point at a stub in tests |
| `Storage:Root` | `storage` | Private photo folder. Relative paths resolve against the content root, never `wwwroot` |
| `Storage:MaxImageBytes` | `6291456` | Upload limit (6 MB). The client downscales to 1280px JPEG first |
| `Limits:ChecksPerDay` | `20` | Per-user cap over a rolling 24 hours |
| `Limits:ChecksPerDayGlobal` | `1000` | Ceiling across all users over a rolling 24 hours, so a leaked URL cannot run up an unbounded bill |
| `Limits:SignupsPerHourPerIp` | `50` | New accounts per client address per hour (address taken from `X-Forwarded-For` behind the tunnel) |
| `Limits:LoginsPerQuarterHourPerIp` | `30` | Login attempts per client address per 15 minutes |
| `Limits:ReportsToHide` | `3` | Reports from distinct people after which a post or comment is hidden |

Any key can be overridden with an environment variable, e.g. `Limits__ChecksPerDay=5`.
`ANTHROPIC_API_KEY` is read from the environment only.

## API

All responses are JSON (camelCase, null properties omitted). Errors are
`{ "error": "<message in the caller's language>" }`. Language for messages: an explicit `language` field
wins, then `Accept-Language`, then English.

Sessions are an HttpOnly, SameSite=Strict cookie set by signup and login. **Every non-GET call under
`/api` must carry the header `X-Requested-With: FitCheck`** or it is refused with 403; that header is the
CSRF guard. Endpoints marked 🔒 need a session. Ids are GUIDs.

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/auth/signup` | `{ handle, password, confirmed16Plus, language, accountType?, displayName? }` | `201` me. Handle: 2–40 letters, digits, dots or underscores, unique case-insensitively; password 8–200; `accountType` is `Person` (default) or `Brand`. 400 invalid, 409 taken, 429 too many signups from one address |
| `POST /api/auth/login` | `{ handle, password }` | `200` me. 401 for a wrong handle or password (same message for both), 429 too many attempts |
| `POST /api/auth/logout` 🔒 | — | 204 |
| `GET /api/auth/me` 🔒 | — | `{ id, handle, name, accountType, language, bio, website, streak, unreadNotifications }` |
| `PATCH /api/users/me` 🔒 | `{ language?, displayName?, bio?, website? }` | Updated me. Website must be https |
| `DELETE /api/users/me` 🔒 | — | 204. Deletes the account, every check, photo, post, comment, fire, save, vote, follow and notification, and fixes other people's counters |
| `GET /api/users/me/checks` 🔒 | — | Last 50 checks, newest first, each with `postId` when posted |
| `GET /api/users/me/saved` 🔒 | `?offset&limit` | Saved posts, newest first |
| `GET /api/users/{handle}` | — | Public profile: counts, best score, streak, `viewer.following` |
| `GET /api/users/{handle}/posts` | `?offset&limit` | That person's public posts |
| `POST` / `DELETE /api/users/{handle}/follow` 🔒 | — | `{ followers, following }`. 400 when following yourself |
| `POST /api/checks` 🔒 | multipart: `intent`, `occasion?`, `language`, `image` | `201 { id, intent, occasion, language, createdAt, latencyMs, status, score, feedback, postId }`. 413 too large, 415 not JPEG/PNG/WebP, 429 over a cap (with `Retry-After`), 502 model failure |
| `GET /api/checks/{id}` 🔒 | — | The check, owner only (404 otherwise) |
| `POST /api/posts` 🔒 | `{ checkId, caption?, challengeId?, products? }` | `201` post. The check must be yours, `ok`, and not yet posted; caption up to 140 characters; `challengeId` must be open and match the check's intent; `products` (brands only, up to 3) are `{ label, url, price? }` with https URLs |
| `GET /api/posts/{id}` | — | The post: `user, intent, score, intentMatch, headline, caption, challengeId, challengeTitle, fireCount, commentCount, fired, saved, isMine, hidden, votes, products, imageUrl, createdAt`. Hidden posts are visible to their author only |
| `GET /api/posts/{id}/image` | — | The photo (JPEG/PNG/WebP, `Cache-Control: private`). This is the only route that serves a photo, and only for a visible post |
| `DELETE /api/posts/{id}` 🔒 | — | 204, author only. The photo becomes private again |
| `POST` / `DELETE /api/posts/{id}/fire` 🔒 | — | `{ fireCount, fired }`. One per person; idempotent |
| `POST` / `DELETE /api/posts/{id}/save` 🔒 | — | `{ saved }` |
| `POST /api/posts/{id}/report` 🔒 | `{ reason? }` | 204. One report per person per post; at `Limits:ReportsToHide` the post is hidden |
| `GET /api/posts/{id}/comments` | — | Comments, oldest first, hidden ones excluded |
| `POST /api/posts/{id}/comments` 🔒 | `{ text }` | `201` comment (1–200 characters) |
| `DELETE /api/comments/{id}` 🔒 | — | 204, by the comment's author or the post's author |
| `POST /api/comments/{id}/report` 🔒 | `{ reason? }` | 204, same rule as posts |
| `GET /api/feed` | `?tab=fresh|top|following&intent&offset&limit` | `{ items, nextOffset }`. `fresh` is newest first, `top` is the most fire in the last 7 days, `following` (🔒) is newest from people you follow; `intent` filters. Limit 1–50 |
| `GET /api/challenges` | `?state=open|ended` | Challenges with entry and vote counts, the top three entries and `viewer` (`isBrand, hasEntered, votedPostId, myEntryId`) |
| `POST /api/challenges` 🔒 | `{ title, brief, intent, prize, prizeUrl?, endsAt }` | `201`, brand accounts only. Ends between 1 hour and 60 days from now |
| `GET /api/challenges/{id}` | — | `{ challenge, entriesByVotes, winner }`. Reading an ended challenge fixes its winner if that has not happened yet |
| `POST /api/challenges/{id}/vote` 🔒 | `{ postId }` | `{ votedPostId, votes }`. One vote per person per challenge, movable while open; not for your own entry |
| `DELETE /api/challenges/{id}/vote` 🔒 | — | `{ votedPostId: null, votes }` |
| `GET /api/notifications` 🔒 | — | `{ items: [{ type, actorHandle, actorName, postId, challengeId, createdAt, read }], unread }`. Types: `fire, comment, follow, vote, entry, ended, won` |
| `POST /api/notifications/read` 🔒 | — | 204, marks everything read |
| `GET /api/metrics/pilot` | — | See above |

`intent` is one of `Casual, Date, Streetwear, OldMoney, Minimal, Office, Party, Sport`.
`feedback.status` is `ok`, `not_outfit` or `rejected`; only `ok` carries items, working and oneTip, and
only `ok` checks can be posted.

## How it is built

```
FitCheck.sln
src/FitCheck.Api/
  Program.cs                      wiring, EnsureCreated, cookie auth, rate limiter, CSRF header check, static files
  appsettings.json
  Domain/                         StyleIntent, AppUser, OutfitCheck, OutfitFeedback, Social.cs (posts, comments, fire,
                                  saves, follows, challenges, votes, notifications, reports), options
  Data/AppDbContext.cs            SQLite via EF Core; unique indexes carry the one-per-person rules
  Services/OutfitAnalyzer.cs      ← the product: system prompt, intent guide, tool schema, PromptVersion, mapping
  Services/AnthropicVisionClient  Messages API over HttpClient: base64 image + forced tool call, 60s timeout, one retry
  Services/DiskImageStore.cs      storage/<userId>/<checkId>.<ext>, behind IImageStore for a later blob store
  Services/Sessions.cs            cookie sign-in and the current user id
  Services/Notifier.cs            activity rows, deduplicated per actor and target
  Services/ChallengeResolver.cs   fixes the winner exactly once when a challenge has ended
  Services/PostReader.cs          posts → DTOs with the viewer's fire/save state, in batches
  Services/Localizer.cs           server messages (en/he) and Accept-Language matching
  Endpoints/                      auth, users, checks, posts (+ comments, feed), challenges, notifications, metrics
  wwwroot/index.html, app.js,     the whole client: one hash-routed page, vanilla HTML/CSS/JS, no build step
  wwwroot/app.css
  wwwroot/i18n/en.json, he.json   UI strings; add a locale by adding a file
tests/FitCheck.Api.Tests/         xUnit
tools/e2e/                        optional browser test (Playwright + a stub of the Anthropic API)
scripts/calibrate.py              calibration run against the real model: score spread, latency, rule 1 scan
```

The model is forced to call a tool (`tool_choice: {type: "tool"}`) whose input schema is our feedback
shape, so the answer is always JSON we can validate. Scores are clamped to 1–10, intent match to 0–100,
and anything descriptive is dropped when the status is not `ok`.

### Rules the code enforces

- **Clothes, never the person.** The prompt forbids any reference to body, face, skin, age or gender, and
  the UI copy follows the same rule. Comment and caption boxes ask for "kind and specific".
- **Checks are private; posting is a separate choice.** A check is stored under its owner and nobody else
  can read it. Posting publishes the photo, the intent, the score, the headline and your caption. The tip
  and the item breakdown never go public. Deleting the post makes the photo private again.
- **Photos are never served by path.** They live under `Storage:Root`, outside `wwwroot`. The only route
  that returns a photo is the post's image route, for a visible post. A test asserts that every stored
  photo returns 404 on every plausible URL.
- **16+ only, self-declared.** Signup fails without the checkbox. See the limitations below.
- **Bad input is refused.** Non-outfit photos get a friendly state. Nudity, sexual content or an apparent
  minor gets a neutral rejection: the photo is deleted immediately, nothing but the status is stored, and
  the model's own words are never shown. Only `ok` checks can be posted.
- **One endpoint deletes everything.** Account, checks, photo files, posts, comments, fire, saves, votes,
  follows and notifications, with other people's counters corrected.
- **No hallucinated brands or items.** Instruction in the prompt; the model may only name what is visible.
- **One of each per person.** Fire, save, follow, report and challenge vote are unique per person and
  target at the database level; repeating is idempotent, never a 500. You cannot follow yourself, report
  your own post, or vote for your own entry.
- **Challenges are honest.** Only brand accounts open them; entries must match the challenge's intent;
  votes are open until `endsAt`; the winner is the entry with the most votes (ties go to the earlier
  entry), fixed once and notified once, even when two requests read the ended challenge at the same
  moment. The prize itself changes hands between the brand and the winner; the app takes no payments.
- **Reports hide, people decide.** Three reports from different people hide a post or a comment from
  everyone but its author, who sees an "under review" badge.
- **Sessions are cookies, writes need a header.** HttpOnly, SameSite=Strict, Secure over HTTPS, 90 days
  sliding. Passwords are hashed with ASP.NET Core's `PasswordHasher`. A missing `X-Requested-With:
  FitCheck` header on any non-GET `/api` call is a 403, so a cross-site form cannot act as a signed-in
  user. Login and signup are rate limited per client address.
- **Cost control.** 20 checks per user per rolling 24 hours (429 with a friendly message), counted including
  checks still in flight so a parallel burst cannot slip past; a global ceiling of 1000 checks a day across
  everyone; a 6 MB upload cap; and the client downscales to 1280px JPEG before uploading. Failed model
  calls do not count toward the caps.

## Known limitations (read before inviting anyone)

- **Age is self-declared.** A checkbox is not age assurance. Before any public launch, integrate the Apple
  and Google age-signal APIs (or an equivalent provider) and gate account creation on the result.
- **No password recovery.** Accounts have no email address, so a forgotten password means a new account.
  Add an email or phone step before a public launch.
- **No moderation screen.** Hidden posts and comments stay hidden until someone clears `Hidden` and
  `ReportCount` in the database. Reports carry the reporter's reason for that review.
- **Metrics are unauthenticated.** `/api/metrics/pilot` only returns aggregates, but put it behind a
  password or an allow-list before the URL leaves the team.
- **Single process, single SQLite file.** The in-flight reservation that closes the cap race lives in
  memory, so running two instances would reopen it. One instance is all the pilot needs.
- **The limiters trust `X-Forwarded-For`.** That is right behind the tunnel and spoofable if Kestrel is
  exposed directly; the global daily ceiling bounds the damage either way. Shared addresses (carrier NAT,
  office Wi-Fi) share one bucket, so if the invite goes out to more than 50 people on one network in the
  same hour, raise `Limits__SignupsPerHourPerIp` for the launch hour.
- **Cookies are Secure only over HTTPS.** Plain `http://localhost` works for development; anything users
  reach must be behind HTTPS (the tunnel) or the session cookie travels in the clear.
- **Calibration is unverified until you run it.** The build was tested against a stubbed model; run
  `scripts/calibrate.py` on real photos before judging scores.
- **Photos stay on disk until the post or the account is deleted.** There is no retention job yet.

## Not in this version

Direct messages, push notifications, share images, payments or prize fulfilment inside the app, native
wrappers, sign-in with Apple or Google, closet memory, and a blob store behind `IImageStore`. None of it is
scaffolded on purpose.

## Decisions

Every judgment call made while building, and every place the brief and instinct disagreed, is in
[`DECISIONS.md`](DECISIONS.md). The Phase 2 plan the social layer was built from is in
[`PHASE2.md`](PHASE2.md).
