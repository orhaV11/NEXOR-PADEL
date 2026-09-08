# OREVOSH

A social app for looks. Pick where the outfit is going (date, office, streetwear…), add a photo or film a
short clip with the in-app camera, and a stylist scores the look **relative to that intent**, lists what you
are wearing, what works, and **the one tip**. The check is private. Post it and it joins a feed where people
react with fire, comment, save and follow; clips play in the feed, and a story card carries the score to
Instagram and TikTok.
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
media folder (`storage/`) are created next to the project on first run; both are git-ignored. The database runs
in WAL mode, so `orevosh.db-wal` and `orevosh.db-shm` sit next to it while the app is open and hold its newest
writes: stop the app before copying the file, or use `--backup` (below), never the `.db` alone. The schema is
versioned with EF Core migrations and applied on start; a database from an earlier round (made before
migrations existed) is upgraded in place after a `.bak-<stamp>` copy is written next to it. A `fitcheck.db`
left over from an earlier build is simply unused and can be deleted.

To put it on a server with your own domain, HTTPS, push notifications, backups and updates that keep
everyone's data, follow [`DEPLOY.md`](DEPLOY.md).

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

### Maintenance commands

The same program runs four maintenance commands; each does its job and exits without starting the server. From
`src/FitCheck.Api`, the lines are the same in bash and in PowerShell:

```bash
dotnet run -- --vapid                 # print a VAPID key pair for Web Push; set Push__PublicKey and Push__PrivateKey, restart
dotnet run -- --backup backups        # a consistent copy of the database and the media folder into ./backups (git-ignored)
dotnet run -- --admin yourhandle      # make an existing account a moderator: sign up with the handle first
dotnet run -- --unadmin yourhandle    # take that away
```

`--admin` and `--unadmin` exit with code 1 when no account has the handle (sign up first, then run it again). On a
server the same commands run inside the container, `docker compose exec app dotnet FitCheck.Api.dll --admin yourhandle`
(`DEPLOY.md`, step 7).

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

256 tests: magic-byte detection for photos and clips, the disk store, analyzer mapping and clamping, locale
matching, the Anthropic client against a scripted HTTP handler, and endpoint tests against the real app with a
scripted vision client: signup and login rules, the CSRF header, uploads and 413/415/429/502, the daily and
global caps, clips (storage, Range streaming, deletion, limits), posting, fire, comments, saves, follows, the
feed tabs and the For you ranking, reports hiding content, the moderation queue and suspensions, challenges
with votes and winner resolution, notifications and Web Push (against a recording push service), tags and
mentions, featured looks, avatars, account-type switches, interests, Explore and search, account deletion
removing files and fixing other people's counters, the migrations and the pilot-database upgrade, backups,
and the metrics math on a seeded dataset.

There is also a browser test in [`tools/e2e`](tools/e2e/README.md): Playwright drives the real client in a
phone viewport against the real API with only the Anthropic API stubbed, as three people (a person in English,
a brand, and a person browsing in Hebrew), from signup and the welcome screen through posting with tags and
mentions, featuring, Explore, a challenge and its winner, the in-app camera with a fake device (a photo, then
a clip with its frame picked), the story card, the moderation queue and a suspension, the guidelines, the
health and config routes, to deleting an account.

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
| `ConnectionStrings:Default` | `Data Source=orevosh.db` | SQLite file, switched to WAL mode on start. Migrations run on start (`Data/DatabaseSetup.cs`); a pre-migration pilot file is backed up and upgraded in place. A relative path resolves against the project folder |
| `Anthropic:Model` | `claude-sonnet-5` | Must support forced tool use: Sonnet 5, Opus 5, the 4.x family, Haiku 4.5 |
| `Anthropic:MaxTokens` | `1200` | Output budget for the tool call (Hebrew is token-heavy) |
| `Anthropic:BaseUrl` | `https://api.anthropic.com` | Override to point at a stub in tests |
| `Storage:Root` | `storage` | Private photo folder (checks and avatars). Relative paths resolve against the content root, never `wwwroot` |
| `Storage:MaxImageBytes` | `6291456` | Upload limit for the still of a check (6 MB). Avatars are capped at 2 MB. The client downscales first |
| `Storage:MaxVideoBytes` | `41943040` | Upload limit for a look clip (40 MB) |
| `Storage:MaxVideoSeconds` | `30` | Advisory clip length: the camera stops there and the picker refuses longer library clips. Returned by `/api/config`. The transcoder cuts a longer clip there |
| `Storage:Transcode` | `true` | Re-encode clips to H.264 MP4 in the background with ffmpeg ("Clips" below). Nothing runs when ffmpeg is not found |
| `Storage:FfmpegPath` | empty | The ffmpeg binary, with ffprobe next to it. Empty means `ffmpeg` on `PATH` |
| `Push:PublicKey` / `Push:PrivateKey` | empty | VAPID keys for Web Push, generated once with `dotnet run -- --vapid`. Environment only (`Push__PublicKey`, `Push__PrivateKey`), never in appsettings. Push is off until both are set; a new pair drops every existing subscription (the push service answers 401/403 and the app deletes it) |
| `Push:Subject` | `mailto:hello@orevosh.app` | Contact the push services see |
| `Admin:Handles` | empty | Handles promoted to moderator at start (`Admin__Handles__0=yourhandle`, `__1` for more): only an account that already exists is promoted, so sign up first, then list the handle and restart. The list never demotes (`--unadmin` does) and a listed handle can no longer be signed up. `--admin <handle>` does the same at any time without a restart |
| `Limits:ChecksPerDay` | `20` | Per-user cap over a rolling 24 hours |
| `Limits:ChecksPerDayGlobal` | `1000` | Ceiling across all users over a rolling 24 hours |
| `Limits:SignupsPerHourPerIp` | `50` | New accounts per client address per hour (address taken from `X-Forwarded-For` behind the tunnel) |
| `Limits:LoginsPerQuarterHourPerIp` | `30` | Login attempts per client address per 15 minutes |
| `Limits:ReportsToHide` | `3` | Reports from distinct people after which a post or comment is hidden |

Any key can be overridden with an environment variable, e.g. `Limits__ChecksPerDay=5`. `ANTHROPIC_API_KEY`
is read from the environment only.

### Clips

A clip is stored as uploaded (MP4 or WebM, whatever the phone recorded) and served from `/api/posts/{id}/video` at
once. When `Storage:Transcode` is on and ffmpeg is found (`Storage:FfmpegPath`, else `ffmpeg` on `PATH`; the Docker
image ships it), one background worker re-encodes every new clip that is not already H.264 in an MP4 into one: at
most 1080 px wide, cut at `Storage:MaxVideoSeconds`, `libx264` veryfast CRF 26, yuv420p, faststart, AAC 96k when
the clip has sound. The MP4 takes the original's place next to the still (`<userId>/<checkId>.mp4`, written whole
before the old file goes) and the check points at it; until then, and whenever ffmpeg fails on a clip, the original
serves as before. One ffmpeg at a time, two minutes each at most, and every start re-queues up to 200 clips that are
not MP4 yet (oldest first), so a server that gets ffmpeg later catches up. The start log says which it is,
`Transcoding is on: ffmpeg version ...` or `ffmpeg not found`, each clip logs one line with sizes and time, and
`/api/config` reports `transcoding`.

## API

All responses are JSON (camelCase, null properties omitted). Errors are `{ "error": "<message in the caller's
language>" }`. Language for messages: an explicit `language` field wins, then `Accept-Language`, then English.

Sessions are an HttpOnly, SameSite=Strict cookie (`orevosh.session`) set by signup and login. **Every non-GET
call under `/api` must carry the header `X-Requested-With: Orevosh`** or it is refused with 403; that header
is the CSRF guard. Endpoints marked 🔒 need a session. Ids are GUIDs. `intent` is one of `Casual, Date,
Streetwear, OldMoney, Minimal, Office, Party, Sport`.

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/auth/signup` | `{ handle, password, confirmed16Plus, language, displayName? }` | `201` me. Handle: 2–40 letters, digits, dots or underscores, unique case-insensitively; password 8–200. 400 invalid, 409 taken (a handle listed in `Admin:Handles` counts as taken), 429 too many signups from one address |
| `POST /api/auth/login` | `{ handle, password }` | `200` me. 401 for a wrong handle or password (same message for both), 429 too many attempts |
| `POST /api/auth/logout` 🔒 | — | 204 |
| `GET /api/auth/me` 🔒 | — | `{ id, handle, name, accountType, language, bio, website, streak, unreadNotifications, avatarUrl, interests, isAdmin }`. `isAdmin` is the account's persisted moderator flag, set at start from `Admin:Handles` or by `--admin`, never by a request. A suspended account gets 403 and is signed out |
| `GET /api/config` | — | `{ maxImageBytes, maxVideoBytes, maxVideoSeconds, pushPublicKey? }`. No secrets |
| `GET /healthz` | — | `ok` when the database answers, 503 otherwise. For the proxy and uptime checks |
| `PATCH /api/users/me` 🔒 | `{ language?, displayName?, bio?, website?, accountType?, interests? }` | Updated me. `accountType` is `Person` or `Brand`; `interests` is a list of intents (≤ 8); website must be https |
| `POST /api/users/me/avatar` 🔒 | multipart `image` (JPEG/PNG/WebP ≤ 2 MB) | `200` me with a versioned `avatarUrl` |
| `DELETE /api/users/me/avatar` 🔒 | — | `200` me |
| `GET /api/users/{handle}/avatar?v=` | — | The photo, `Cache-Control: public, max-age=86400` |
| `DELETE /api/users/me` 🔒 | — | 204. Deletes the account, every check, photo, avatar, post, tag, mention, comment, fire, save, vote, follow and notification, and fixes other people's counters and featured marks. 403 for a moderator: `--unadmin` first, or the freed handle would be promoted again on the next restart |
| `GET /api/users/me/checks` 🔒 | — | Last 50 checks, newest first, each with `postId` when posted |
| `GET /api/users/me/saved` 🔒 | `?offset&limit` | Saved posts, newest first |
| `GET /api/users/{handle}` | — | Public profile: counts, best score, streak, `avatarUrl`, `featured`, `community`, `viewer.following` |
| `GET /api/users/{handle}/posts` | `?offset&limit` | That person's public posts |
| `GET /api/users/{handle}/community` | `?offset&limit` | Public posts that mention this account |
| `GET /api/users/{handle}/featured` | `?offset&limit` | Brand: posts it featured. Person: their posts that were featured |
| `POST` / `DELETE /api/users/{handle}/follow` 🔒 | — | `{ followers, following }`. 400 when following yourself |
| `POST /api/checks` 🔒 | multipart: `intent`, `occasion?`, `language`, `image`, `video?` | `201 { id, intent, occasion, language, createdAt, latencyMs, status, score, feedback, postId }`. `video` is an optional MP4/MOV/WebM clip of the same look (≤ `Storage:MaxVideoBytes`); the stylist judges only `image`, the frame the person picked, and the clip is stored with the check when the status is `ok`. 413 too large (still or clip), 415 not JPEG/PNG/WebP (or not MP4/WebM for the clip), 429 over a cap (with `Retry-After`), 502 model failure |
| `GET /api/checks/{id}` 🔒 | — | The check, owner only (404 otherwise) |
| `POST /api/posts` 🔒 | `{ checkId, caption?, challengeId?, products? }` | `201` post. The check must be yours, `ok`, and not yet posted; caption up to 140 characters, its `#tags` (first 5) and `@mentions` of existing handles (first 5) are stored and mentioned accounts are notified; a caption carrying an open challenge's hashtag enters that challenge (once per person; `challengeId` is still accepted); `products` (brands only, up to 3) are `{ label, url, price? }` with https URLs |
| `GET /api/posts/{id}` | — | The post: `user, intent, score, intentMatch, headline, caption, challengeId, challengeTitle, fireCount, commentCount, fired, saved, isMine, hidden, votes, products, imageUrl, videoUrl?, createdAt, tags, mentions, featuredBy`. Hidden posts are visible to their author and to moderators only; a suspended author's posts are hidden |
| `GET /api/posts/{id}/image` | — | The photo (`Cache-Control: private`). The only route that serves a check photo, and only for a visible post |
| `GET /api/posts/{id}/video` | — | The clip (`video/mp4` or `video/webm`, `Cache-Control: private`, Range requests honoured so players can seek). 404 for a look without a clip. The only route that serves a clip. A WebM becomes `video/mp4` at the same URL once the background transcode is done (Configuration, "Clips") |
| `DELETE /api/posts/{id}` 🔒 | — | 204, author only. The photo becomes private again with the check; the clip is deleted |
| `POST` / `DELETE /api/posts/{id}/fire` 🔒 | — | `{ fireCount, fired }`. One per person; idempotent |
| `POST` / `DELETE /api/posts/{id}/save` 🔒 | — | `{ saved }` |
| `POST` / `DELETE /api/posts/{id}/feature` 🔒 | — | `{ featuredBy }`. Brands only; the post must mention the brand or be an entry in one of its challenges; one brand per post (409 when another brand was first); the author is notified |
| `POST /api/posts/{id}/report` 🔒 | `{ reason? }` | 204. `reason` is one of `not_outfit`, `nudity`, `person`, `spam`, `other` (what the app's picker sends) or free text, kept to 200 characters. One report per person per post; at `Limits:ReportsToHide` the post is hidden |
| `GET /api/posts/{id}/comments` | — | Comments, oldest first, hidden ones excluded |
| `POST /api/posts/{id}/comments` 🔒 | `{ text }` | `201` comment (1–200 characters) |
| `DELETE /api/comments/{id}` 🔒 | — | 204, by the comment's author or the post's author |
| `POST /api/comments/{id}/report` 🔒 | `{ reason? }` | 204, same rule and reasons as posts |
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
| `GET /api/push/state` 🔒 | — | `{ enabled, subscribed }`: whether the server has VAPID keys, and whether this account has at least one subscription |
| `POST /api/push/subscriptions` 🔒 | `{ endpoint, p256dh, auth }` (from `PushSubscription.toJSON()`) | `200` state. Upserts this browser's subscription for the account, at most 10 per account (the oldest make room); 400 when push is off, the subscription is malformed, or the endpoint is not a public push-service name (a literal address, `localhost` or a single-label host is refused). A subscription the push service answers 404/410 (gone) or 401/403 (made against other VAPID keys) to is deleted |
| `DELETE /api/push/subscriptions` 🔒 | `{ endpoint }` | 204 |
| `POST /api/push/test` 🔒 | — | 202, sends a test notification to the caller's own browsers |
| `GET /api/admin/queue` 🔒 | — | Moderators (accounts with the `isAdmin` flag) only, 403 otherwise: reported looks and comments with counts, reasons and the author's state, plus counters |
| `GET /api/admin/users?q=` 🔒 | — | Accounts by handle prefix (empty `q` lists suspended accounts) |
| `POST /api/admin/posts/{id}/hide` / `unhide` 🔒 | — | Hide a look, or show it again (which also clears its reports) |
| `DELETE /api/admin/posts/{id}` 🔒 | — | 204, removes the look, its check, photo and clip |
| `POST /api/admin/comments/{id}/hide` / `unhide`, `DELETE /api/admin/comments/{id}` 🔒 | — | The same for comments |
| `POST /api/admin/users/{handle}/suspend` / `unsuspend` 🔒 | — | A suspended account cannot sign in, reads as missing, and its looks and comments are hidden; a suspended brand's open challenges are closed with no winner (lifting does not reopen them); lifting restores what the crowd had not hidden on its own. A moderator cannot be suspended (400): that is `--unadmin` on the box |
| `GET /api/metrics/pilot` | — | See above |

## How it is built

```
FitCheck.sln
src/FitCheck.Api/
  Program.cs                      wiring, migrations on start, cookie auth, rate limiter, CSRF header check,
                                  security headers, static files, /api/config, /healthz, --vapid, --backup, --admin, --unadmin
  Data/DatabaseSetup.cs           Migrate(), WAL, the pilot-database upgrade, the backup command
  Data/AdminSync.cs               the Admin:Handles sync at start and the --admin / --unadmin commands
  Data/Migrations/                the EF Core migrations (dotnet ef migrations add <Name> for the next one)
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
- **Moderators are a flag on the account, not a handle.** `Admin:Handles` promotes existing accounts at start and
  never demotes; `--admin` and `--unadmin` set and clear the flag at any time; a listed handle cannot be signed
  up; a moderator cannot be suspended, and cannot delete the account until un-admined.
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
- **Moderation is a queue, not a team.** Moderators (accounts flagged at start from `Admin:Handles`, or with
  `--admin`) see reported looks and comments and can hide, delete and suspend. Featured looks are the brand's
  call with no review step.
- **Clips are transcoded on the box, one at a time.** iPhones record MP4 (H.264), which plays everywhere; Chrome
  records H.264 MP4 only when the device can and WebM otherwise, which older iPhones cannot play. With ffmpeg
  present (`Storage:Transcode`, on by default; the Docker image has it) the app re-encodes every clip to H.264 MP4
  in the background, and the WebM serves until that is done, seconds on a small VPS; without ffmpeg clips stay as
  uploaded. It is the web process running one ffmpeg at a time: fine for a pilot; a separate worker or a video
  service, probably with object storage for the files, is the launch-scale answer.
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
- **Photos stay on disk until the look or the account is deleted; clips go with the look.** There is no
  retention job yet, and clips are large: watch the disk (`DEPLOY.md`, "What to watch").
- **Push needs an installed app on iPhone** (iOS 16.4+, added to the home screen). Android and desktop
  Chrome work in the tab.
- **The .NET project is still called `FitCheck.Api`.** A mechanical rename for when the repository gets its
  final name; nothing a user sees says FitCheck.

## Not in this version

Direct messages, payments or prize fulfilment inside the app, native wrappers (the PWA installs; a
Capacitor wrap is the next step), sign-in with Apple or Google, password reset by email (needs an email
provider), closet memory, a blob store behind `IImageStore`, and real age assurance. None of it is scaffolded on
purpose.

## Decisions

Every judgment call made while building, and every place the brief and instinct disagreed, is in
[`DECISIONS.md`](DECISIONS.md). The plans each phase was built from are in [`PHASE2.md`](PHASE2.md) and
[`PHASE3.md`](PHASE3.md); the visual system is [`DESIGN.md`](DESIGN.md).
