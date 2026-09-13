# OREVOSH

A social app for looks. Pick where the outfit is going (date, office, streetwear…), add a photo or film a
short clip with the in-app camera, and a stylist scores the look **relative to that intent**, breaks the score
into fit, color and accessories, lists what you are wearing, what works, **the one tip**, and the one accessory
that would finish the look. The check is private. Post it and it joins a feed where people
react with fire, comment, save and follow; clips play in the feed, and a story card carries the score to
Instagram and TikTok.
Tag the brands you wear with `@brand`, add `#tags`, and brands feature the community looks they love, open
challenges with a prize, and tag products on their own looks. **Tag the pieces** on your own look, the brand, the
model, a store link and a dot on the photo (the stylist suggests a brand only when its mark is visible, and only you
publish it), so a look can be found by brand and shopped from its item sheet; every week the looks that caught the
most fire land on the **weekly flames board**, five top tens that close at Saturday midnight, Israel time, into a
hall of flame. Browsing needs no account, and neither does the
first check: a visitor gets one free look as a guest and keeps it by signing up. A free account gets a few checks
a day; **OREVOSH Pro** raises that to 30. Two outfits for the same evening? **"Which one?"** scores both and
picks. **Insights** read your checks over time, looks are searchable by the pieces in them, **Today's look** is a
daily prompt that runs on a hashtag, and a look can be posted as the follow-up to an earlier one, with both
scores on the card.

English is the default language; Hebrew (RTL), Arabic (RTL) and Russian ship alongside it, and the stylist writes its
feedback in the user's language. The web app installs to the home screen on iPhone and Android and behaves like a native app
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
migrations existed) is upgraded in place after a `.bak-<stamp>` copy is written next to it: missing tables,
columns, foreign keys and indexes are added, and a column whose nullability differs from the model is altered (a
table rebuild), with foreign keys off during the batch and a rollback (keeping the `.bak`) if any table would lose
rows; the log line names the altered columns and the added foreign keys. A `fitcheck.db`
left over from an earlier build is simply unused and can be deleted.

To put it on the internet, follow [`DEPLOY.md`](DEPLOY.md): Fly.io in 15 minutes with no server to manage
(`fly launch`, `fly deploy`, about 5 USD a month; the repository's `fly.toml` does the rest), or your own server with
Docker and Caddy for full control. Both give you a domain with HTTPS, push notifications, email for account recovery,
backups, and updates that keep everyone's data. Every push runs the build, the tests and the browser test on GitHub
Actions: [![CI](https://github.com/orhaV11/NEXOR-PADEL/actions/workflows/ci.yml/badge.svg)](https://github.com/orhaV11/NEXOR-PADEL/actions/workflows/ci.yml)

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

The same program runs the maintenance commands; each does its job and exits without starting the server. From
`src/FitCheck.Api`, the lines are the same in bash and in PowerShell:

```bash
dotnet run -- --vapid                 # print a VAPID key pair for Web Push; set Push__PublicKey and Push__PrivateKey, restart
dotnet run -- --backup backups        # a consistent copy of the database and the media folder into ./backups (git-ignored)
dotnet run -- --admin yourhandle      # make an existing account a moderator: sign up with the handle first
dotnet run -- --unadmin yourhandle    # take that away
dotnet run -- --verify nexor          # mark an existing brand account as verified: a check inside its BRAND mark, everywhere it appears
dotnet run -- --unverify nexor        # take that away
dotnet run -- --pro noa 3             # put an existing account on Pro for 3 months (31 days each, from now; 1 to 120)
dotnet run -- --pro noa off           # back to Free
```

The account commands exit with code 1 when no account has the handle (sign up first, then run it again) and 2 on a
usage error; a leading `@` on the handle is fine. `--verify` and `--pro` are the only things that write the verified
flag and the plan by hand: no request can, and with `Billing:Provider` left at `manual` the `--pro` command is the
whole upgrade path. On a server the same commands run inside the container, `docker compose exec app dotnet
FitCheck.Api.dll --admin yourhandle` (`DEPLOY.md`, step 7). Round 10 adds no command: the weekly board closes itself
in the background (`Services/BoardCloser.cs`, the log says when), a moderator pulls a look off it through the API, and
affiliate programmes are settings (`Affiliate:Hosts`).

### Read the pilot metrics

```bash
curl -s -b 'orevosh.session=<a moderator’s cookie>' http://localhost:5000/api/metrics/pilot | jq
# or, signed in as a moderator in the app: #/admin/metrics
```

The first block covers signed-in people's checks with `status = "ok"` (`returnRate` = users whose second OK check
happened at most 7 days after their first ÷ users with at least one OK check; guest checks are left out). The
`social` block counts users, brands, posts, fires, follows, comments, open and ended challenges, votes, mentions,
featured looks, clips, push subscriptions, people active in the last 7 days, and, since Round 10, `itemsTagged`
(item rows a person touched: typed by them, or carrying a brand or a store link; the stylist's bare names are not
tagging), `itemOuts` (store-link taps that left through `/api/items/{id}/out`) and `boardViews` (answered reads of
`/api/board`), the last two read from the `Counters` table the routes increment. The route answers only through a
moderator's session (below), and moderators see the same numbers drawn as a page at `#/admin/metrics` (one hero
figure, the return rate; tiles; the score distribution as bars), linked from the moderation page.

### Run the tests

```bash
dotnet test        # from the repository root (FitCheck.sln)
```

573 tests: magic-byte detection for photos and clips, the disk store, analyzer mapping and clamping, locale
matching, the Anthropic client against a scripted HTTP handler, and endpoint tests against the real app with a
scripted vision client: signup and login rules (the date of birth and the phone's own day among them), the CSRF
header, uploads and 413/415/429/502, the plan caps, the ceiling and the global cap, what the allowances count and
the `Retry-After` they name, guest checks (the cookie, one per cookie and per address counted from looks given, the
brake on attempts, the claim at signup and login and a claim that cannot copy a file, the sweep), clips (storage,
Range streaming, deletion, limits), posting
(with the before/after link), fire, comments, saves, follows, the feed tabs and the For you ranking, reports
hiding content, the moderation queue and suspensions, challenges with votes and winner resolution, the daily
prompt across the year turn, notifications and Web Push (against a recording push service), tags and mentions, featured looks,
verified brands, avatars, account-type switches, interests, Explore, search by name, tag and item, "Which one?"
comparisons (the comparer's prompt and mapping, the routes, the shared allowance), the insights math on a seeded
list, billing (Checkout against a recording Stripe and its refusal for a Pro account, the signed webhook and its
four events, the clamped Pro cap on `/api/config`, `--pro`), the insights gate, account recovery (the link origin,
the per-account brakes, the address binding), account deletion removing files and fixing other people's counters,
the migrations and the pilot-database upgrade (nullability and foreign keys included), the Round 10 migration over a
Round 9 file (every item keeps its name and gets an id in the upper-case text the SQLite provider binds a `Guid` as, so
a key lookup finds it, the stylist as its source and its order; the app starts on that file, the search by piece still
finds the looks, and a migrated row can be tagged by its id and leaves through the out door), items on a look (the stylist's rows at posting and never a
brand while the check carries `brandSeen`, the tagging rules as a validation matrix with nothing written on a
refusal, owner-only and hidden looks, the list on `POST /api/posts`, the item search and its paging, the brands list,
the out door with the affiliate parameters and its brake (a link pasted with Hebrew, an accent or a host in its own
script leaving in its ASCII form), the disclosure on `/api/config`, the metrics), rubric v3 (`brand_seen` mapped, the words
"null" and "none" read as no brand), the weekly board (each eligibility rule, the pair cap, the tie order, the intent
boards, the picks board, rising, exclusions and their lift (and one outliving its moderator), the week cut at midnight
in Asia/Jerusalem and across a DST change, `?week=` by date and by instant, the span it answers and the calendar's
edges, the counted reads, the two-week 60-second cache, a check by the week's end, the sponsor and `me` and the sponsor
link's validation, the closer's idempotence, its two-process race, catch-up and silence for old weeks, the rank tap
landing on the closed week, the badge and the hall), backups, and the metrics math on a
seeded dataset.

There is also a browser test in [`tools/e2e`](tools/e2e/README.md): Playwright drives the real client in a
phone viewport against the real API with only the Anthropic API stubbed, as three people (a person in English,
a brand, and a person browsing in Hebrew), from a guest's check and signup with a birth date and the welcome
screen through posting with tags and mentions, featuring, Explore, a challenge and its winner, the in-app camera
with a fake device (a photo, then a clip with its frame picked), the story card, a comparison, the insights, a
search by piece, the Pro page, Today's look and a follow-up look, verified brands, the moderation queue and a
suspension, the numbers page, the guidelines, the terms and the privacy policy, the health and config routes, to
deleting an account. **Round 10 is in the script**: the item editor on the post sheet (the stylist's rows, the Nike guess confirmed, a dot placed),
the look page's items, dots and item sheet, the item pages and the brands list, the board with its first-place medal and
tabs, the empty hall and the Explore strip. The run starts the API with `Board__NewAccountDays=0`, `Board__MinChecksToCount=1`
and `Board__CacheSeconds=0` because its accounts are minutes old and its fires must show at once. Not driven: a closed
week (the closer has no HTTP trigger), so the hall's weeks, the badge and the `board_rank` line are covered by
`BoardTests` only; the moderator's exclusion is covered by `BoardTests` and `Round10SkeletonTests`.

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
| `Email:Host` / `Port` / `User` / `Password` / `From` / `UseStartTls` | empty | SMTP for confirmation and reset links (`Email__Host` etc.; the password is environment only). Mail is on when Host and From are set; `Email__Host=log` writes the links to the log instead of sending |
| `Email:PublicOrigin` | empty | The https origin the links in mails carry, e.g. `https://looks.example.com`. **Required on any host that is not localhost**: a link is never built from the request's `Host` header (a stranger could choose it), so with mail on and this empty only a laptop run gets links, the start log warns, and a request for one on a real host is logged as an error and answered 502 (`error.email_send_failed`) or, for a forgotten password, silently not sent |
| `Limits:RecoveryPerHourPerIp` | `5` | Forgot-password and resend requests per client address per hour. On top of it, per account: three confirmation links per ten minutes and ten a day, three reset links an hour (429 `error.recovery_limited` on the resend and the address change; a forgotten password is always 202 and simply not mailed) |
| `Admin:Handles` | empty | Handles promoted to moderator at start (`Admin__Handles__0=yourhandle`, `__1` for more): only an account that already exists is promoted, so sign up first, then list the handle and restart. The list never demotes (`--unadmin` does) and a listed handle can no longer be signed up. `--admin <handle>` does the same at any time without a restart |
| `Plans:FreeChecksPerDay` | `3` | Checks and comparisons together, per free account, over a rolling 24 hours (429 `error.plan_limit` with `Retry-After`, which names the Pro number) |
| `Plans:ProChecksPerDay` | `30` | The same for a Pro account. Clamped to `Limits:ChecksPerDay`: the clamped number is the effective Pro cap, the one `/api/config` publishes (`plans.proChecksPerDay`), `me.checksPerDay` carries, and the Pro page and the check screen's cap line quote; a value above the ceiling is warned about at startup. The 429 `error.plan_limit` message quotes the same clamped number |
| `Plans:GuestChecksPerDay` | `1` | Free checks for a visitor with no account, per guest cookie (`orevosh.guest`, one day) and per client address, over a rolling 24 hours (429 `error.guest_limit` with `Retry-After` either way). What is counted is a stored check, `ok`, `not_outfit` or `rejected` (the ones that cost a model call): a refused upload (400/413/415), a 502 or a dropped connection never spends it, and `error.guest_limit` is only ever sent for a look actually given. The per-cookie count is read from the rows; the per-address count is in memory (`GuestAddressCounter`), so a restart forgets the day. `0` turns guests off: `POST /api/checks` answers 401 signed out, and the check screen shows the sign-in prompt with the submit disabled; with guests on, its hint carries this number |
| `Plans:GuestAttemptsPerDay` | `20` | The brake on attempts: the `guest` rate-limit policy on `POST /api/checks`, a fixed 24-hour window per client address in memory for signed-out calls, whatever they come to, answering 429 `error.too_fast` with `Retry-After` beyond it. Well above `Plans:GuestChecksPerDay` on purpose, so a refused photo or a model outage never locks a shared address out of its look; a signed-in call passes through it unlimited |
| `Plans:ProPriceText` | empty | Shown on the Pro page as the price, e.g. `₪19 / month`; empty hides it. Text only: the price itself is the Stripe price |
| `Plans:CompareNeedsPro` | `false` | Whether "Which one?" and your insights need Pro (403 `error.pro_required` and a Pro card on `#/compare` and `#/insights` otherwise; the Pro page lists them as benefits only then). Off by default: Pro is a cap on a real cost, not a feature wall |
| `Billing:Provider` | `manual` | `manual`: Pro is granted with `--pro`, and the Pro page shows a note instead of a checkout button. `stripe`: Checkout and the webhook are live once the three keys below are set; until they are, the routes answer 400 `error.billing_disabled` |
| `Billing:StripeSecretKey` / `StripePriceId` / `StripeWebhookSecret` | empty | Environment only (`Billing__StripeSecretKey`, `Billing__StripePriceId`, `Billing__StripeWebhookSecret`): the API secret key (`sk_test_…` works against Stripe's test mode), the recurring Pro price (`price_…`), and the signing secret of the webhook endpoint (`whsec_…`). Read in `Services/StripeClient.cs` and `Endpoints/BillingEndpoints.cs`; the secret key is redacted from HttpClient logging |
| `Billing:PublicOrigin` | empty | Where Checkout returns to (`/#/pro?checkout=success` or `cancel`); the request's origin when empty |
| `Limits:ChecksPerDay` | `30` | The ceiling per account over a rolling 24 hours, whatever the plan says: `Plans:ProChecksPerDay` cannot exceed it. Counted including checks still in flight; failed calls do not count |
| `Limits:ChecksPerDayGlobal` | `1000` | Ceiling across all users and guests over a rolling 24 hours: checks and comparisons on both routes (`Services/Spend.cs`), calls in flight counted, failed calls left out (429 `error.rate_limited_global`) |
| `Limits:SignupsPerHourPerIp` | `50` | New accounts per client address per hour (address taken from `X-Forwarded-For` behind the tunnel) |
| `Limits:LoginsPerQuarterHourPerIp` | `30` | Login attempts per client address per 15 minutes |
| `Limits:CommentsPerHour` / `Limits:ReportsPerHour` | `30` / `20` | Per signed-in account, fixed one-hour windows in memory (429 `error.too_fast` with `Retry-After`) |
| `Limits:ReportsToHide` | `3` | Reports from distinct people after which a post or comment is hidden |
| `Board:WeekStartsOn` | `Sunday` | The first day of the board's week (a `DayOfWeek` name), in `Board:TimeZone` |
| `Board:TimeZone` | `Asia/Jerusalem` | The IANA zone the week is cut in: it opens at local midnight on `WeekStartsOn` and closes seven days later (Saturday midnight in Israel); `weekStart` and `weekEnd` on the board are those instants in UTC. A zone this machine does not know falls back to UTC with a warning at start (`Board: the time zone … is not known here`) |
| `Board:MinChecksToCount` | `1` | A fire counts only when the firer has made at least this many `ok` checks by the week's end (a check later in the week makes their earlier fires count). `0` turns the rule off |
| `Board:MaxPerFirerPerAuthor` | `3` | The most fires from one person on one author's looks that count in a week; the first ones by time count, the rest do not. `0` or less means unlimited |
| `Board:NewAccountDays` | `2` | A fire from an account younger than this at the moment of the fire does not count |
| `Board:Size` | `10` | Places on each board |
| `Board:RisingDays` | `30` | The rising board lists the fired looks of accounts younger than this at the week's end |
| `Board:CacheSeconds` | `60` | How long a computed week is served from memory, real time, per process. Only the running week and, until the closer has written it, the one before are kept (two entries at most; an exclusion drops them); every other week, the archive browsed back or next week, is computed on each read. `0` turns the cache off (the browser test runs so) |
| `Board:Sponsor:Name` / `Handle` / `PrizeText` / `Url` | empty | The week's sponsor, on the board only while `Name` is set: the name (linked to the account when `Handle` names one, else to `Url`), the prize line and the site's host. `Url` must be an `http(s)` link with a host and no user info; a bare host (`nexor.example`, `www.nexor.example/drop`) is read as `https://`; anything else (`javascript:`, `ftp:`, `mailto:`, `user:pw@host`) is dropped at start with the warning `Board: the sponsor link {Url} is not an http(s) URL; the board shows the sponsor without a link`, and the page checks the link again before it becomes an `href`. Settings, not a form: there is no sponsor self-service |
| `Affiliate:Disclosure` | `true` | Whether the item sheet shows "This link may earn OREVOSH a commission." under a store link. Published by `/api/config` as `affiliate.disclosure` and read by the sheet: while `true`, the line follows "Leaves OREVOSH" under every store link, listed host or not; `false` leaves "Leaves OREVOSH" on its own. Keep it on wherever a programme is joined; the hosts and their parameters are never published |
| `Affiliate:Hosts` | `{}` | Host → the query string `GET /api/items/{id}/out` appends when a link goes there, e.g. `"amazon.com": "tag=orevosh-20"` (`Affiliate__Hosts__amazon.com=tag=orevosh-20` as an environment variable); a listed host matches case-insensitively with its subdomains (`www.amazon.com` and `smile.amazon.com`, not `notamazon.com`). Empty by default: nothing is appended and no link earns anything until you list a programme you joined. The parameters are added at the door, never stored, so a change here changes every link at once |

Any key can be overridden with an environment variable, e.g. `Plans__FreeChecksPerDay=5` (a double underscore stands
for each colon; a key with a dot in it, `Affiliate__Hosts__amazon.com`, keeps the dot). `ANTHROPIC_API_KEY`
is read from the environment only, and so should be every other secret (`Push__PrivateKey`, `Email__Password`,
the three `Billing__Stripe*` keys).

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

Sessions are an HttpOnly, SameSite=Strict cookie (`orevosh.session`) set by signup and login. A visitor's first
check sets a second cookie of the same kind, `orevosh.guest` (a random token, one day), which names the guest's
checks until an account claims them. **Every non-GET call under `/api` must carry the header
`X-Requested-With: Orevosh`** or it is refused with 403; that header is the CSRF guard, and `POST
/api/billing/webhook` (signed by Stripe instead) is the one write exempt from it. Endpoints marked 🔒 need a
session. Ids are GUIDs. `intent` is one of `Casual, Date, Streetwear, OldMoney, Minimal, Office, Party, Sport`.
Wherever a person appears in a response (`user`, `mentions`, `featuredBy`, `brand`, the cards), the shape is
`{ handle, name, accountType, avatarUrl?, verified }`.

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/auth/signup` | `{ handle, password, birthDate, today?, language, displayName? }` | `201` me. Handle: 2–40 letters, digits, dots or underscores, unique case-insensitively; password 8–200; `birthDate` is `yyyy-MM-dd` (what a date input sends), 16 years or more before today, not before 1900 and not in the future: 400 `birthdate_required`, `birthdate_invalid` or `underage` in that order after the handle and password rules. `today` is the client's own calendar date (`yyyy-MM-dd`): the sixteen rule and the not-in-the-future check are measured on it when it is within one day of the server's UTC date, otherwise on the UTC date, so nobody is stopped on their birthday east of Greenwich. The date is stored and never returned by any route. `confirmed16Plus` from older clients is ignored. 409 taken (a handle listed in `Admin:Handles` counts as taken), 429 too many signups from one address |
| `POST /api/auth/login` | `{ handle, password }` | `200` me. 401 for a wrong handle or password (same message for both), 429 too many attempts |
| `POST /api/auth/logout` 🔒 | — | 204 |
| `GET /api/auth/me` 🔒 | — | `{ id, handle, name, accountType, language, bio, website, streak, unreadNotifications, avatarUrl, interests, isAdmin, email, emailVerified, plan, proUntil, verified, checksToday, checksPerDay, badge? }`. `isAdmin` is the account's persisted moderator flag, set at start from `Admin:Handles` or by `--admin`, never by a request. `plan` is `free` or `pro` (`pro` only while `proUntil` is in the future or open), `verified` is the `--verify` flag, `checksToday` counts the account's checks and comparisons in the rolling 24 hours (failed ones excluded) and `checksPerDay` is its cap. `badge` is last week's place in the top three of the looks board, `{ board: "looks", rank, weekStart }`, worn for this week only and absent otherwise. A suspended account gets 403 and is signed out |
| `POST /api/auth/forgot` | `{ handleOrEmail }` | `202` always, same body whether or not the account exists; mails a reset link when the account has a confirmed email (5 per hour per address) |
| `POST /api/auth/reset` | `{ token, password }` | `200` me, signed in. 400 for a used, expired or unknown link (the link survives a too-short password) |
| `POST /api/auth/verify-email` | `{ token }` | `200` me. Confirms the address the link was sent to, and only while that is still the account's address; works signed out, signs nobody in |
| `POST /api/users/me/email/resend` 🔒 | — | 204, a new confirmation link (5 per hour per address, and per account three per ten minutes or ten a day: 429) |
| `GET /api/config` | — | `{ maxImageBytes, maxVideoBytes, maxVideoSeconds, pushPublicKey?, email, transcoding, plans: { freeChecksPerDay, proChecksPerDay, guestChecksPerDay, proPriceText, compareNeedsPro, billing }, affiliate: { disclosure } }`. `plans.proChecksPerDay` is what a Pro account really gets (`Plans:ProChecksPerDay` clamped to `Limits:ChecksPerDay`); `billing` is true only when Stripe Checkout is live; `affiliate.disclosure` is `Affiliate:Disclosure`, whether the item sheet shows the commission line under a store link (the hosts and their parameters stay on the server). No secrets |
| `GET /healthz` | — | `ok` when the database answers, 503 otherwise. For the proxy and uptime checks |
| `PATCH /api/users/me` 🔒 | `{ language?, displayName?, bio?, website?, accountType?, interests?, email? }` | Updated me. `accountType` is `Person` or `Brand`; `interests` is a list of intents (≤ 8); website must be https; `email` null leaves it, `""` clears it, a value stores it lower-cased (unique, never shown to others) and sends a confirmation link (400 when mail is off on this server, 409 when another account has it) |
| `POST /api/users/me/avatar` 🔒 | multipart `image` (JPEG/PNG/WebP ≤ 2 MB) | `200` me with a versioned `avatarUrl` |
| `DELETE /api/users/me/avatar` 🔒 | — | `200` me |
| `GET /api/users/{handle}/avatar?v=` | — | The photo, `Cache-Control: public, max-age=86400` |
| `DELETE /api/users/me` 🔒 | — | 204. Deletes the account, every check and comparison, their photos and clips (by path, `ImagePath`/`VideoPath` and `ImagePathA`/`B`, as well as the account's folder), the avatar, every post, tag, mention, comment, fire, save, vote, follow and notification, the items on its looks, the board exclusions on its looks and its places in the hall (`WeeklyWinners`), and fixes other people's counters and featured marks. The looks it pulled off the board while it was a moderator stay off: each exclusion stands with its reason and loses its signature (`byUserId` null). 403 for a moderator: `--unadmin` first, or the freed handle would be promoted again on the next restart |
| `GET /api/users/me/checks` 🔒 | — | Last 50 checks, newest first, each with `postId` when posted |
| `GET /api/users/me/saved` 🔒 | `?offset&limit` | Saved posts, newest first |
| `GET /api/users/me/insights` 🔒 | — | `{ checks, avgScore, bestScore, bestIntent, weakestCategory, weakestShare, accessoriesMissingShare, streak, lines }` over the last 200 OK checks: the average (one decimal) and best score, the intent with the highest average among those checked at least twice (else the most checked), the item category most often called weak and its share of the looks that had items, the share of rubric-v2 checks with no accessories on, and two to four ready sentences in the caller's language. Under 3 checks only `checks` and `streak` are filled and `lines` is empty. 403 `error.pro_required` when `Plans:CompareNeedsPro` is on and the account is not Pro (the client shows the Pro card on `#/insights`). Private, like the checks |
| `GET /api/users/me/comparisons` 🔒 | — | The caller's last 20 comparisons, newest first (the shape of `GET /api/compare/{id}`) |
| `GET /api/users/{handle}` | — | Public profile: counts, best score, streak, `avatarUrl`, `verified`, `featured`, `community`, `viewer.following`, and `badge` (the same shape as on `me`: last week's top-three place on the looks board, this week only) |
| `GET /api/users/{handle}/posts` | `?offset&limit` | That person's public posts |
| `GET /api/users/{handle}/community` | `?offset&limit` | Public posts that mention this account |
| `GET /api/users/{handle}/featured` | `?offset&limit` | Brand: posts it featured. Person: their posts that were featured |
| `POST` / `DELETE /api/users/{handle}/follow` 🔒 | — | `{ followers, following }`. 400 when following yourself |
| `POST /api/checks` | multipart: `intent`, `occasion?`, `language`, `image`, `video?` | `201 { id, intent, occasion, language, createdAt, latencyMs, status, score, feedback, postId }`. **No session needed:** signed out, the call is a guest's, named by the `orevosh.guest` cookie (minted on the first one, sent back with the answer), allowed `Plans:GuestChecksPerDay` times per cookie and per client address over a rolling day, counted from stored checks (429 `error.guest_limit` with `Retry-After`, only ever for a look actually given: a refused upload, a 502 or a dropped connection spends nothing; 401 `error.sign_in_required` when that setting is 0; beyond `Plans:GuestAttemptsPerDay` attempts from one address in 24 hours, 429 `error.too_fast`); a guest's photo is stored in a shared folder until claimed or swept. Signed in, the account's plan cap applies, with `Retry-After`: at the cap a free account hears `error.plan_limit` (which names the Pro number), a Pro account `error.rate_limited` (with its cap), as on the compare route; at `Limits:ChecksPerDayGlobal` everyone hears `error.rate_limited_global`. `Retry-After` on both routes is when a permit actually frees up: the expiry of the (count − cap + 1)th oldest counted call, not the oldest. `video` is an optional MP4/MOV/WebM clip of the same look (≤ `Storage:MaxVideoBytes`); the stylist judges only `image`, the frame the person picked, and the clip is stored with the check when the status is `ok` (a guest's clip is transcoded only after the claim). 413 too large (still or clip), 415 not JPEG/PNG/WebP (or not MP4/WebM for the clip), 429 over a cap, 502 model failure |
| `POST /api/checks/claim` 🔒 | — | `{ claimed }`: every check and comparison carrying the caller's guest cookie becomes the account's (owner set, token cleared, `claimedAt` stamped, the files moved into the account's folder, a claimed clip queued for the transcoder) and the cookie is dropped. All or nothing: a file that cannot be copied (a full disk, a file missing from the store) answers 500 `error.server`, the rows stay the guest's and the cookie stays, and the client claims again on its next load. `{ claimed: 0 }` when there was nothing, so the client calls it blind after signup, after login and at every signed-in boot |
| `GET /api/checks/{id}` | — | The check, for its owner or for the guest whose cookie made it (404 to anyone else, the same as a missing id) |
| `POST /api/compare` 🔒 | multipart: `intent`, `occasion?`, `language`, `imageA`, `imageB` | `201 { id, intent, occasion, language, createdAt, latencyMs, status, feedback: { status, winner, scoreA, scoreB, headlineA, headlineB, reason, oneTip, message? }, imageUrlA, imageUrlB }`: both photos go to the stylist in one call (`PromptVersion` `cmp-v1`, the analyzer's rules and calibration) and one wins, `a` or `b`. Counts against the same daily allowance as a check; 400 `compare_two_photos` without both, 403 `pro_required` when `Plans:CompareNeedsPro` is on and the account is not Pro, and otherwise the same 413/415/429/502 as a check (at the cap a free account hears `error.plan_limit`, a Pro account `error.rate_limited`). `not_outfit` says which photo to replace; `rejected` keeps nothing but the status. Private and never postable |
| `GET /api/compare/{id}` 🔒 | — | The comparison, owner only (404 otherwise) |
| `GET /api/compare/{id}/image/a` and `/b` 🔒 | — | The two photos, owner only, `Cache-Control: private`. The only route that serves them; 404 for a comparison that kept none |
| `POST /api/posts` 🔒 | `{ checkId, caption?, challengeId?, products?, beforePostId?, items? }` | `201` post. The check must be yours, `ok`, and not yet posted; caption up to 140 characters, its `#tags` (first 5) and `@mentions` of existing handles (first 5) are stored and mentioned accounts are notified; a caption carrying an open challenge's hashtag enters that challenge (once per person; `challengeId` is still accepted); `products` (brands only, up to 3) are `{ label, url, price? }` with https URLs; `beforePostId` names one of your own visible looks this one improves on ("after the tip": 400 `error.before_invalid` for anyone else's look, a hidden one, or the look of this very check). Without `items`, the stylist's item names are copied onto the look, lower-cased (up to 8, 60 characters each, with their category, source `Stylist`, never a brand), so `/api/search` finds it by piece; the item verdicts and notes stay private. With `items` (the post sheet's list, the same shape and rules as `PATCH /api/posts/{id}/items` below), that list is the whole list: an input without an id whose name, normalised, equals a stylist row's name (the stylist's uncut name from the check and the stored name's first forty characters count as the same) keeps that row as the stylist's, anything else is the person's own row, and an invalid list refuses the post with the same 400s before anything is written, so the check stays postable |
| `GET /api/posts/{id}` | — | The post: `user, intent, score, intentMatch, headline, caption, challengeId, challengeTitle, fireCount, commentCount, fired, saved, isMine, hidden, votes, products, imageUrl, videoUrl?, breakdown?, before?, createdAt, tags, mentions, featuredBy, items, itemCount`. `breakdown` is `{ fit, color, accessories }` for a rubric-v2 check; `before` is `{ postId, score, imageUrl }` of the earlier look (left off while that look is under review; the link is cleared when it is deleted). `items` (on every card, in every feed, in position order, `[]` when there are none) are `{ id, name, category, brand?, model?, url?, host?, source, x?, y?, confirmed }`: `name` lower-cased, `category` one of `top, bottom, dress, outerwear, shoes, accessory, other`, `source` `Stylist` or `User`, `host` the link's host without `www.` for "Shop at {host}", `x`/`y` the dot on the photo as fractions of its width and height (absent when the piece is listed and not placed), `confirmed` true only for a stylist suggestion the person accepted; `url` is the raw link and the client never sends anyone to it (the out door does). Hidden posts are visible to their author and to moderators only; a suspended author's posts are hidden |
| `GET /api/posts/{id}/image` | — | The photo (`Cache-Control: private`). The only route that serves a check photo, and only for a visible post |
| `GET /api/posts/{id}/video` | — | The clip (`video/mp4` or `video/webm`, `Cache-Control: private`, Range requests honoured so players can seek). 404 for a look without a clip. The only route that serves a clip. A WebM becomes `video/mp4` at the same URL once the background transcode is done (Configuration, "Clips") |
| `DELETE /api/posts/{id}` 🔒 | — | 204, author only. The photo becomes private again with the check; the clip is deleted, and so are the look's items (their search rows and store links with them) |
| `PATCH /api/posts/{id}/items` 🔒 | `{ items: [{ id?, name?, category?, brand?, model?, url?, x?, y?, confirmed? }] }` | `200` the look's items, in the order sent. Owner only: another person's look and a hidden one answer 404 `error.post_not_found` alike. The list is the whole list, at most 12 (400 `error.items_too_many`, "Up to 12 items on a look."): a row not in it is removed, `items: []` clears the look, and a missing body or a body without the list (`{}`, `{ "items": null }`) is 400 `error.item_invalid` with nothing changed: only an explicit `[]` clears. An input with `id` keeps that row (its name and category may be left out to keep them); one without an id whose name equals a not-yet-named stylist row's name re-attaches to it; anything else is a new row with source `User`. A stylist row stays the stylist's while only its brand, model, link, dot or confirmation change and becomes the person's once its name or category does. Rules, each a 400 with nothing written: a typed name 1–40 characters after normalisation (lower-cased, one space between words; a stylist name sent back unchanged may be up to 60, and unchanged means the stored name, the stylist's uncut name from the check, or the stored name's first forty characters, none of which re-attributes the row), `category` one of the seven (a new row without one is `other`), `brand` ≤ 40, `model` ≤ 60 (`error.item_invalid`, also for a duplicate or unknown `id`); `url` absolute `http(s)`, ≤ 500, with a host and no user info, so `javascript:`, `data:`, `ftp:`, a relative path or `nike.com@evil.example` are refused (`error.item_url_invalid`); `x` and `y` both in 0..1 or both absent (`error.item_position_invalid`); `confirmed` is stored true only with a brand on a row that is still the stylist's. The stylist's `brandSeen` is never copied by the server: the client shows it as "Looks like Nike?" and sends it back as `brand` with `confirmed: true` when the person confirms, another brand with `confirmed: false` on Edit, and no brand on "Not a brand". Logs `Items: {Count} on post {PostId} by {UserId}` |
| `GET /api/items` | `?brand&category&q&offset&limit` | `{ brand?, category?, q?, posts, nextOffset? }`: visible looks (not hidden, author not suspended) carrying **one item row that matches every filter given** (`brand=nike&category=bottom` is a Nike bottom, not a Nike top on a look with pants), newest first, paged like a feed (`limit` 1–30, 20 by default). `brand` matches case-insensitively and comes back in the spelling most rows carry ("Nike" for `?brand=nike`); `category` is exact and one of the seven (an unknown one is an empty page); `q` matches anywhere in the name, the brand or the model, `%` and `_` literal. No filter at all is an empty page, never everything; `brand` or `q` over 40 characters is 400 `error.search_invalid`. The client shows this under `#/items/<brand>`, `#/items/<brand>/<category>` and `#/items?q=` |
| `GET /api/items/brands` | `?q` | `{ items: [{ name, looks, account? }] }` for the brand autocomplete: brands already on visible looks, merged across case in .NET (so "Nike" and "nike" are one, in any script) with the count of distinct looks, plus brand accounts (not suspended) whose handle starts with `q` or whose name contains it, a brand account of the same name riding on the tagged brand as its `account` (a user ref, `verified` included); by looks, then accounts first, then verified, then name; at most 20. Empty `q` is the top brands plus every brand account; over 40 characters is 400 `error.search_invalid` |
| `GET /api/items/{id}/out` | — | **The one door a store link leaves through**: `302` to the item's `url`, as stored when it is ASCII (host case kept) and in its ASCII form when it was pasted with characters outside ASCII (a Hebrew query, an accented path, a host in its own script: the host as punycode, the path, query and fragment percent-encoded, the scheme and port as they were, since a `Location` header carries printable ASCII only), with the parameters `Affiliate:Hosts` names for its host appended to that form after the link's own query and before its `#fragment` (`?tag=…` or `&tag=…`), and `Referrer-Policy: no-referrer` and `Cache-Control: no-store` on the answer, so the store learns nothing about the look or the person and every tap is counted (`itemOuts` in the metrics, incremented once the `Location` header is set, so only taps that were redirected count). 404 `error.item_not_found` ("We couldn't find this item.") for a missing item, one without a link, or one on a hidden look; a moderator reviewing a hidden look reads the raw `url` on the DTO instead. Rate limited by the `out` policy: 60 a minute per client address, a fixed window in memory, 429 `error.too_fast` with `Retry-After` beyond it. The client opens it as `<a href target="_blank" rel="noopener">` ("Shop at nike.com") with "Leaves OREVOSH" under it whenever a link exists, followed by "· This link may earn OREVOSH a commission." while `Affiliate:Disclosure` is true (the default; `/api/config` says) |
| `POST` / `DELETE /api/posts/{id}/fire` 🔒 | — | `{ fireCount, fired }`. One per person; idempotent |
| `POST` / `DELETE /api/posts/{id}/save` 🔒 | — | `{ saved }` |
| `POST` / `DELETE /api/posts/{id}/feature` 🔒 | — | `{ featuredBy }`. Brands only; the post must mention the brand or be an entry in one of its challenges; one brand per post (409 when another brand was first); the author is notified |
| `POST /api/posts/{id}/report` 🔒 | `{ reason? }` | 204. `reason` is one of `not_outfit`, `nudity`, `person`, `spam`, `other` (what the app's picker sends) or free text, kept to 200 characters. One report per person per post; at `Limits:ReportsToHide` the post is hidden |
| `GET /api/posts/{id}/comments` | — | Comments, oldest first, hidden ones excluded; each comment's `user` ref carries `verified` like every other ref |
| `POST /api/posts/{id}/comments` 🔒 | `{ text }` | `201` comment (1–200 characters) |
| `DELETE /api/comments/{id}` 🔒 | — | 204, by the comment's author or the post's author |
| `POST /api/comments/{id}/report` 🔒 | `{ reason? }` | 204, same rule and reasons as posts |
| `GET /api/feed` | `?tab=foryou\|following\|top\|fresh&intent&offset&limit` | `{ items, nextOffset }`. `foryou` (default) ranks the last 30 days by fire, comments, people you follow, your interests and recency; `following` (🔒) is newest from people you follow; `top` is the most fire in 7 days; `fresh` is newest. `intent` filters. Limit 1–30 |
| `GET /api/explore` | — | `{ trendingTags, brands, topLooks, challenges }`: tags from the last 7 days, brands by followers, top 6 looks by fire in 7 days, up to 5 open challenges |
| `GET /api/search?q=` | — | `{ users, tags, posts }` for a 1–40 character query: handle prefix or display-name substring (brands first), tag prefix, and up to 12 visible looks whose stylist-named items contain the term ("black boots"), newest first. The client shows the three under `#/search/<term>` |
| `GET /api/tags/{tag}/posts` | `?offset&limit` | Public posts carrying the tag, newest first |
| `GET /api/today` | — | `{ tag, title, hint, intent?, date, posts, posted }`: the day's prompt (one of 30 in `Services/DailyPrompts.cs`, picked by the count of UTC days since a fixed day, 31 December 2025, modulo thirty, so it turns over at midnight UTC, everyone sees the same one, and the cycle runs on across the year turn instead of restarting on 1 January), its title and hint in the caller's language, up to 60 visible looks posted today with its hashtag (newest first), and whether the caller posted one (a look under review still counts). Public |
| `GET /api/board` | `?week` | `{ weekStart, weekEnd, closesIn, closed, looks, people, rising, intents, picks, sponsor?, me? }`: **the weekly flames board**, public. Without `week`, the week running now in `Board:TimeZone`; `week=yyyy-MM-dd` is a date in that zone and answers the week containing it (any day of the week names it), and a full ISO instant with a `T` (the `weekStart` a board answered, or the instant a `board_rank` tap carries) is taken as an instant, so a client can pass it back unchanged. The weeks that answer run from the week of the first look (the first archived week when that is earlier; last week at the latest, so a fresh board's way back always answers) through next week, which is an empty board; a week beyond next week or before that floor, a date at the calendar's edges (`0001-01-01`, `9999-12-31`, as a date or an instant) and anything unparsable are 400 `error.board_week_invalid`, not computed. `weekStart` and `weekEnd` are the UTC instants of local midnight (Sunday 6 September 2026 in Israel is `2026-09-05T21:00:00Z`); `closesIn` is seconds until `weekEnd`, 0 once `closed`. Every row is `{ rank, fires, post?, user, looks?, score? }` with `fires` the fires that counted, not the look's `fireCount`: `looks` is the fired looks by counted fires (an earlier look first on a tie); `people` is the sum over each author's fired looks (ties to the older account; `looks` on a people row is how many they posted that week); `rising` is the looks board restricted to authors whose account is younger than `Board:RisingDays` at the week's end; `intents` is keyed by intent name (`"Date"`), only intents with a counted fire; `picks` is the looks **posted** this week by the stylist's `score`, counted fires and then age only breaking ties, `score` on the row. Ten places each (`Board:Size`). `post` is left off a row whose look is gone or under review; the place stays with the person. `sponsor` `{ name, handle?, prizeText?, url? }` only while `Board:Sponsor:Name` is set (`url` only when `Board:Sponsor:Url` is an `http(s)` link, a bare host read as `https://`); `me` `{ looks?, people?, rising?, intent?, picks? }` is the caller's best place on each board, absent signed out or when on none (`intent` is the best across the intent boards, without saying which). A week that is over and closed answers from the archive (`closed: true`, `closesIn: 0`); the running week and the one before it are computed and served from memory for `Board:CacheSeconds` (60, real time) per process, two entries at most; every other week is computed on each read. Every answered read counts in `boardViews` (a 400 does not, and the Explore strip and the reset card read this route too) |
| `GET /api/board/hall` | — | `{ weeks: [{ weekStart, weekEnd, winners: [{ board, rank, user, postId?, imageUrl?, fires, score? }] }] }`: **the hall of flame**, the closed weeks newest first, twelve at most, every place the closer wrote (`board` is `looks`, `people`, `rising`, `intent:<Intent>` or `picks`, in that order, then by rank). `imageUrl` (`/api/posts/{id}/image`) only while the look still exists and is not hidden; a deleted look's place stays with `postId` cleared. Not counted in `boardViews` |
| `POST /api/admin/board/exclude` 🔒 | `{ postId, reason? }` | Moderators only (the same gate as the queue): `201 { postId, reason, by, createdAt }`, the look off every board of every computed week from now on and the board's cache dropped; `reason` trimmed and cut at 200. 400 `error.invalid_request` without a `postId`, 404 `error.post_not_found`, 409 `error.board_excluded` when it is already off. A hidden look can be excluded too. Logs `Board: {PostId} excluded by {UserId}: {Reason}`. Already-closed weeks are not rewritten |
| `DELETE /api/admin/board/exclude/{postId}` 🔒 | — | 204, the look back on the board (cache dropped); 404 `error.board_not_excluded` when it was not off. Logs `Board: {PostId} put back by {UserId}` |
| `GET /api/challenges` | `?state=open\|ended` | Challenges with entry and vote counts, the top three entries and `viewer` (`isBrand, hasEntered, votedPostId, myEntryId`) |
| `POST /api/challenges` 🔒 | `{ title, brief, intent, prize, prizeUrl?, endsAt, tag? }` | `201`, brand accounts only. Ends between 1 hour and 60 days from now. `tag` is the entry hashtag (derived from the title when missing, made unique among open challenges) |
| `GET /api/challenges/{id}` | — | `{ challenge, entriesByVotes, winner }`. Reading an ended challenge fixes its winner if that has not happened yet |
| `POST` / `DELETE /api/challenges/{id}/vote` 🔒 | `{ postId }` / — | `{ votedPostId, votes }`. One vote per person per challenge, movable while open; not for your own entry, and not by the brand that opened it |
| `GET /api/notifications` 🔒 | — | `{ items: [{ type, actorHandle, actorName, postId, challengeId, createdAt, read, rank? }], unread }`. Types: `fire, comment, follow, vote, entry, ended, won, mention, featured, board_rank`. `board_rank` is written by the board's closer for everyone on the looks board of the week that just closed (one per person, their best place in `rank`, `actorHandle` their own handle, `postId` the look), shown as "You finished #{rank} this week" and pushed with the same line, the tap landing on the week that closed for the push and the activity line alike: `#/board?week=<instant>`, the instant six and a half days before the line was written (UTC, `yyyy-MM-ddTHH:mm:ssZ`; the row carries no week, and the closer writes it after the close, so that instant is mid-week inside the closed week whether the week ran 167, 168 or 169 hours), which `?week=` takes as an instant. Older weeks closed in a catch-up are silent |
| `POST /api/notifications/read` 🔒 | — | 204, marks everything read |
| `GET /api/push/state` 🔒 | — | `{ enabled, subscribed }`: whether the server has VAPID keys, and whether this account has at least one subscription |
| `POST /api/push/subscriptions` 🔒 | `{ endpoint, p256dh, auth }` (from `PushSubscription.toJSON()`) | `200` state. Upserts this browser's subscription for the account, at most 10 per account (the oldest make room); 400 when push is off, the subscription is malformed, or the endpoint is not a public push-service name (a literal address, `localhost` or a single-label host is refused). A subscription the push service answers 404/410 (gone) or 401/403 (made against other VAPID keys) to is deleted |
| `DELETE /api/push/subscriptions` 🔒 | `{ endpoint }` | 204 |
| `POST /api/push/test` 🔒 | — | 202, sends a test notification to the caller's own browsers |
| `GET /api/billing/state` 🔒 | — | `{ plan, proUntil, billing, proPriceText }`: the effective plan, whether Stripe Checkout is live, and the price text |
| `POST /api/billing/checkout` 🔒 | — | `{ url }` of a Stripe Checkout Session (subscription, the Pro price, the account id as `client_reference_id` and `metadata.userId`, the confirmed email prefilled, an existing customer reused) that returns to `/#/pro?checkout=success` or `cancel`. 400 `error.billing_disabled` while the provider is `manual` or a key is missing, 409 `error.already_pro` for an account that is Pro (one subscription per account; a stale tab re-reads `me`), 502 `error.billing_failed` when Stripe did not answer with a page |
| `POST /api/billing/webhook` | Stripe's event, raw | `200 { received: true }`. No session, no CSRF header: the `Stripe-Signature` header (`t=…,v1=…`, HMAC-SHA256 over `t.body` with `Billing:StripeWebhookSecret`, within five minutes of now) is the guard, 400 `error.billing_signature` otherwise. `checkout.session.completed` puts the account on Pro for 35 days on top of any period still running and stores the customer id; `invoice.paid` (except the first, `subscription_create`) moves the end to the invoice's period end plus three days, never below the current end (an invoice naming no period is worth 35 days from now); `customer.subscription.updated` follows the status (`active`/`trialing`: the current period end plus three days, never below the current end; `past_due`/`unpaid`/`paused`: three days from now at most); `customer.subscription.deleted` ends Pro now (the plan field keeps saying a subscription existed); every other event, and an event naming no account, is answered 200 so Stripe stops sending it. No event ids are kept: a repeated `checkout.session.completed` stacks one period, every other repeat names the same period and changes nothing or ends what already ended |
| `GET /api/admin/queue` 🔒 | — | Moderators (accounts with the `isAdmin` flag) only, 403 otherwise: reported looks and comments with counts, reasons and the author's state, plus counters |
| `GET /api/admin/users?q=` 🔒 | — | Accounts by handle prefix (empty `q` lists suspended accounts) |
| `POST /api/admin/posts/{id}/hide` / `unhide` 🔒 | — | Hide a look, or show it again (which also clears its reports) |
| `DELETE /api/admin/posts/{id}` 🔒 | — | 204, removes the look, its check, photo and clip |
| `POST /api/admin/comments/{id}/hide` / `unhide`, `DELETE /api/admin/comments/{id}` 🔒 | — | The same for comments |
| `POST /api/admin/users/{handle}/suspend` / `unsuspend` 🔒 | — | A suspended account cannot sign in, reads as missing, and its looks and comments are hidden; a suspended brand's open challenges are closed with no winner (lifting does not reopen them); lifting restores what the crowd had not hidden on its own. A moderator cannot be suspended (400): that is `--unadmin` on the box |
| `GET /api/metrics/pilot` 🔒 | — | See above; moderators only (403 otherwise) |

## How it is built

```
FitCheck.sln
src/FitCheck.Api/
  Program.cs                      wiring, migrations on start, cookie auth, rate limiter (incl. the "guest" attempts brake and the
                                  "out" door's minute), CSRF header check (the Stripe webhook exempt), security headers (a route
                                  may set Referrer-Policy first), static files, /api/config, /healthz, --vapid, --backup,
                                  --admin, --unadmin, --verify, --unverify, --pro
  Data/DatabaseSetup.cs           Migrate(), WAL, the pilot-database upgrade, the backup command
  Data/AdminSync.cs               the Admin:Handles sync at start and the --admin/--unadmin, --verify/--unverify, --pro commands
  Data/Migrations/                the EF Core migrations (dotnet ef migrations add <Name> for the next one)
  Domain/                         StyleIntent, AppUser (plan, proUntil, birthDate, verified), OutfitCheck (guestToken,
                                  claimedAt), OutfitFeedback (+ ComparisonFeedback; items carry brandSeen from rubric v3),
                                  Social.cs (posts with beforePostId, tags, mentions, comments, fire, saves, follows,
                                  challenges, votes, notifications with a rank, reports, auth tokens, post items with brand,
                                  model, url, source and the dot, board exclusions, weekly winners, counters, comparisons),
                                  options (Plans, Billing, Email, Limits, Board, Affiliate…)
  Data/AppDbContext.cs            SQLite via EF Core; unique indexes carry the one-per-person rules
  Services/OutfitAnalyzer.cs      ← the stylist: system prompt, intent guide, tool schema, PromptVersion, mapping
  Services/OutfitComparer.cs      ← "Which one?": two photos in one call, the same rules and calibration, PromptVersion cmp-v1
  Services/AnthropicVisionClient  Messages API over HttpClient: base64 images + forced tool call, 60s timeout, one retry
  Services/Plans.cs               IsPro (the flag and the end date), CapFor (the plan's cap, never above Limits:ChecksPerDay) and
                                  ProCap (the clamped Pro number /api/config publishes)
  Services/Spend.cs               what the allowances count: stored checks and comparisons over the rolling day, per account, per
                                  guest cookie and globally, me.checksToday, and the Retry-After that names the call whose expiry
                                  frees a permit
  Services/CheckCapacity.cs       the in-flight reservations that close the cap race, and GuestAddressCounter (the per-address
                                  guest count, in memory)
  Services/GuestChecks.cs         the guest cookie, the claim (rows and files move to the account, all or nothing), GuestCheckSweeper
                                  (hourly)
  Services/StripeClient.cs        one form POST that opens a Checkout Session, and the webhook signature check; no SDK
  Services/PostItems.cs           the stylist's item names copied onto a look at posting, for the search by piece, and Apply: the
                                  one validation of the list a person tags (names, brand, model, the store link, the dot)
  Services/Board.cs               the weekly board: the week's window in Board:TimeZone, which fires count, the five boards, the
                                  two-week 60-second cache, the span ?week= answers, the sponsor link, the DTOs, the badge
  Services/BoardCloser.cs         the hosted service that closes ended weeks into WeeklyWinners once and tells the looks board
  Services/Counters.cs            the item_outs and board_views tallies, an upsert per hit
  Services/Clock.cs               IClock, the one clock the board reads, so a test can move the week
  Services/DailyPrompts.cs        the 30 "Today's look" prompts, one a day by the count of UTC days since 31 Dec 2025, modulo 30
  Services/RecoveryTokens.cs      one-time links (hashed), the link origin rule, the per-account mail brakes
  Services/CaptionParser.cs       #tags and @mentions out of a caption
  Services/FeedRanker.cs          the For you score, a pure function
  Services/DiskImageStore.cs      storage/<userId>/<checkId>.<ext> and storage/<userId>/avatar.<ext>, behind IImageStore
  Services/Sessions.cs            cookie sign-in and the current user id
  Services/Notifier.cs            activity rows, deduplicated per actor and target
  Services/ChallengeResolver.cs   fixes the winner exactly once when a challenge has ended
  Services/PostReader.cs          posts → DTOs with tags, mentions, featured-by, the before look and the viewer's state, in batches
  Services/Localizer.cs           server messages (en/he) and Accept-Language matching
  Endpoints/                      auth, users, checks (+ claim), compare, posts (+ comments), items (tagging, the item search, the
                                  brands list, the out door), board (the week, the hall, the moderator's exclusion), feed,
                                  explore (+ search, tags), challenges, notifications, insights, today, billing, push, admin, metrics
  wwwroot/index.html, app.css     the shell (with the Open Graph and Twitter tags) and the design system: Ring of Fire, see
                                  DESIGN.md (logical properties for RTL)
  wwwroot/app/core.js             state, i18n, API, router, bottom sheets, gestures, look cards, the claim call, the brand mark
  wwwroot/app/views/*.js          one module per screen: feed, post, explore, challenges, check, camera, compare, pro, insights,
                                  today, activity, profile, auth, settings, admin, dashboard (the numbers), pages, legal,
                                  items (the brand and search pages), board (the board, the hall, the Explore strip, the
                                  reset card, the profile badge)
  wwwroot/app/after.js            the "after the tip" picker for the post sheet
  wwwroot/app/items.js            the tagging editor of the post sheet and of "Edit items": the rows, the brand suggestion, the dot
  wwwroot/manifest.webmanifest,   the installable app; the service worker caches the shell only, never the API, and lets
  wwwroot/sw.js, wwwroot/icons/   /landing/ navigations through to the network
  wwwroot/i18n/en.json, he.json   UI strings (the terms and the privacy policy among them); add a locale by adding a file
  wwwroot/landing/                the static landing pages (index.html, index.he.html) and their screens
  wwwroot/brand/                  the mark and wordmark SVGs, the concept notes, the two OG cards
tests/FitCheck.Api.Tests/         xUnit
tools/e2e/                        optional browser test (Playwright + a stub of the Anthropic API)
tools/brand/                      render-kit.js and its templates: regenerates brand-kit/, the OG cards and the landing screens
brand-kit/                        logos, covers, story templates, store screenshots (README lists every file)
mobile/                           the Capacitor wrap for the stores: config and instructions, nothing installed
scripts/calibrate.py              calibration run against the real model: score spread, latency, rule 1 scan
STORE.md, MARKETING.md            the store listings and the launch plan
```

The model is forced to call a tool (`tool_choice: {type: "tool"}`) whose input schema is our feedback shape,
so the answer is always JSON we can validate. Scores are clamped to 1–10, intent match to 0–100, and anything
descriptive is dropped when the status is not `ok`.

### Rules the code enforces

- **Clothes, never the person.** The prompt forbids any reference to body, face, skin, age or gender, and the
  UI copy follows the same rule.
- **Checks are private; posting is a separate choice.** Posting publishes the photo, the intent, the score,
  the headline, your caption and the three sub-scores (fit, color, accessories), and indexes the stylist's item
  names so people can find the look in search. The tip, the notes on each item and the accessories read stay
  private. Deleting the post makes the photo private again.
- **Photos are never served by path.** Check photos and avatars live under `Storage:Root`, outside `wwwroot`.
  The post image route (visible posts only) and the avatar route are the only doors.
- **16+ by date of birth, self-declared.** Signup needs a birth date (16 on the day; nothing before 1900 or in the
  future), stored on the account and never returned by any route. A checkbox is not an age rule; a date is at
  least a rule. Still not age assurance: see the limitations below.
- **A guest gets one check, and keeps it by signing up.** The first wow comes before the account: signed out,
  `POST /api/checks` works once per guest cookie and once per address over a rolling day, counted from looks
  actually given (a refused upload, a model outage or a dropped connection spends nothing; attempts are braked
  separately, twenty a day per address), the result offers "Sign up to keep it and post it", and the claim at signup
  or login makes the check an ordinary one (posting, history, deletion), all or nothing: a claim that cannot copy a
  file leaves the rows the guest's and runs again on the next load. Unclaimed guest checks and their photos are
  swept after a day (the log says `Guest sweep: …`). Guests cannot post, compare, or read anyone else's check.
- **Cost control.** Every check is a paid model call, so the caps are the product: 3 a day on Free, 30 on Pro, 1 as
  a guest, checks and comparisons counted together over a rolling 24 hours (429 with a friendly message and a
  `Retry-After` that names when a permit really frees up: a Free account is told what Pro gives, a Pro account at
  its ceiling just hears the number), `Limits:ChecksPerDay` as the ceiling no plan exceeds, a global ceiling of 1000
  a day across both routes, a 6 MB upload cap, and the client downscales to 1280px JPEG (avatars to 320px) before
  uploading. In-flight calls count; failed model calls do not.
- **Pro is a cap on a real cost, not a feature wall.** Pro raises the daily cap; comparisons and insights stay free
  by default (`Plans:CompareNeedsPro`, and the Pro page names them as benefits only when that is on). The check
  screen says how many checks are left today and offers "Go Pro for more" only to a Free account at its cap. Pro is a flag plus an end date on the account, written by Stripe's webhook
  or by `--pro`, never by a request; a lapsed period falls back to Free by itself. Stripe stays behind
  `Billing:Provider`: with `manual`, the Pro page shows a note and nothing pretends to charge.
- **Comparisons are private and never postable.** "Which one?" keeps both photos only when the stylist confirmed
  two outfits, serves them to the owner alone, and has no post route.
- **Items are indexed from the stylist's words or the person's, never from captions.** At posting, the item names of
  the check are copied onto the look (lower-cased, up to 8) unless the person tagged the pieces on the post sheet; a
  caption cannot put a look under "black boots". A row keeps saying whether the stylist or the person named it
  (`source`), and the verdicts and notes stay private with the tip.
- **The stylist never publishes a brand; the person does.** Rubric v3 asks for `brand_seen` on every piece and only
  for a mark, logo or unmistakable signature that is visible (null otherwise, never a guess from style, cut or
  price); the server never copies it onto a look. The post sheet shows it as "Looks like Nike?" with Confirm, Edit
  and Not a brand, and a brand reaches a row only through the person's own list: confirmed (a stylist row, a brand,
  `confirmed: true`) or typed, in which case `confirmed` is false whatever was sent. A brand or a model on a look is
  the person's word, never verified.
- **Store links leave through one door.** A link is stored as given (`http(s)`, a host, no user info, 500 characters,
  Hebrew or an accent in it allowed) and is never the `href` a person taps: `GET /api/items/{id}/out` answers a 302 to
  the link in a form a header can carry (punycode host, percent-encoded path, query and fragment when it was pasted
  with characters outside ASCII) with the affiliate parameters for its host from `Affiliate:Hosts` appended at the
  door, `Referrer-Policy: no-referrer` and `Cache-Control: no-store`, counts the tap once the header is set, and
  refuses a hidden look. So a programme can be joined, changed or revoked in one setting for every link at once, the
  store learns nothing about the look or the person, and the item sheet says "Leaves OREVOSH" under every store link,
  with "This link may earn OREVOSH a commission." after it while `Affiliate:Disclosure` is on (the default), whether
  or not the host earns anything today. Sixty taps a minute per address, then 429.
- **A fire counts on the board only from someone who uses the app, and only a few per pair.** The week's fires are
  read in time order and each counts only when the firer has made at least one `ok` check by the week's end
  (`Board:MinChecksToCount`), when their account was at least two days old at the fire (`Board:NewAccountDays`),
  when the look is not their own, and while it is within the first three fires from that person on that author's
  looks in the week (`Board:MaxPerFirerPerAuthor`); the row's `fires` is that count, not `fireCount`. Hidden looks
  and looks a moderator excluded are on no board. Friends cannot carry a look, and a fresh batch of accounts carries
  nothing.
- **The picks board cannot be gamed.** It ranks the looks posted that week by the stylist's score; counted fires and
  then age only break ties, so no amount of fire moves a look past a better score.
- **Moderators pull looks off the board; they cannot put anyone on it.** `POST /api/admin/board/exclude` takes a look
  off every board that is computed from then on, with a reason, and the lift puts it back; both are logged with who
  did it. A week already closed keeps its archive.
- **A week closes once, by the closer, into the hall.** `BoardCloser` runs at start and every five minutes, finds
  every week that is over and has no `WeeklyWinners` rows (back to the week of the earliest fire, so downtime is
  caught up oldest first), and writes every board and rank of a week in one save; the unique index on
  `(WeekStart, Board, Rank)` makes a second close of the same week a no-op, a week with no counted fires writes
  nothing, and only the most recent ended week tells the looks board its places (`board_rank`, in the app and by
  push). There is no HTTP route that closes a week. The badge the week after is read from those rows: the top
  three of the looks board, on `me` and the profile, for one week.
- **A follow-up is a strip on the card, not a post type.** `beforePostId` must be one of your own visible looks and
  not this check's own; the card shows "After the tip · 6 → 7" with a link to the earlier look, and the feed stays
  one kind of thing.
- **Verification is the owner's hand.** `--verify <handle>` on the box sets the flag; no form, no request. The check
  sits inside the BRAND mark wherever the account appears.
- **Recovery links never carry a stranger's host.** Links are built from `Email:PublicOrigin` or, with none, only
  for a loopback host; a reset link is refused once the address it went to left the account, an address change
  voids open reset links, and each account gets three confirmation links per ten minutes (ten a day) and three
  reset links an hour.
- **Bad input is refused.** Non-outfit photos get a friendly state. Nudity, sexual content or an apparent
  minor gets a neutral rejection: the photo is deleted immediately, nothing but the status is stored, and the
  model's own words are never shown. Only `ok` checks can be posted.
- **One endpoint deletes everything.** Account, checks, comparisons, photos and clips (by path as well as by
  folder), avatar, posts, tags, mentions, comments, fire, saves, votes, follows and notifications, with other
  people's counters and featured marks corrected.
- **No hallucinated brands or items.** Instruction in the prompt; the model may only name what is visible, and a
  brand only when its mark is (rubric v3, above).
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
## Known limitations (read before inviting anyone)

- **Age is self-declared.** A date of birth typed at signup is a rule, not age assurance: anyone can type a
  date. Before any public launch, integrate the Apple and Google age-signal APIs (or an equivalent provider) and
  gate account creation on the result.
- **Guest checks cost money.** A visitor's free look is a real model call with no account behind it. The guard is
  the cap per guest cookie and per client address (`Plans:GuestChecksPerDay`, one a day each, counted from looks
  actually given; the per-address count lives in memory and a restart forgets the day), the brake on attempts per
  address (`Plans:GuestAttemptsPerDay`, twenty a day) and the global ceiling; a patient script that rotates addresses
  gets one check per address. Set `Plans__GuestChecksPerDay=0` to close the door, and watch the model bill either way.
- **Brand accounts are self-declared; verification is by hand.** Anyone can switch to brand mode in settings, and
  only a brand the owner ran `--verify` for carries the check. There is no form to ask for it and no process
  behind it beyond the owner knowing who is behind the account.
- **Stripe has not run against a live account.** Checkout, the webhook and its four events were built and tested
  against a recording stand-in and signed test events; the first real subscription, renewal and cancellation are
  the proof. Run it in test mode (`sk_test_…`, the Stripe CLI forwarding to `/api/billing/webhook`) before the
  provider is switched to `stripe` on a server people pay on. No event ids are stored, so a replayed
  `checkout.session.completed` stacks one period (renewals are read from the event and repeat harmlessly);
  acceptable for a pilot, not for scale.
- **The pilot upgrade path is a command.** With `Billing:Provider` at `manual` the Pro page says Pro is switched on
  by hand, and the owner runs `--pro <handle> <months>`; there is no in-app request or cancel, and a person ends
  Pro by writing to the owner (the terms say so).
- **The legal pages need a lawyer.** `#/terms` and `#/privacy` (version 2, dated 2026-09-12) are written from what
  the code actually does, in each language, and are not legal advice; the governing-law line is a placeholder
  ("the place where the owner is based") and the contact address `hello@orevosh.app` must exist before the pages go
  live. Have a lawyer review both before launch.
- **Translations need a native review.** Every language after English (Hebrew, Arabic and Russian) was written by the
  builders, the terms and the privacy policy included; a native speaker should read
  each before it reaches people.
- **The screenshots in the brand kit are test fixtures.** The store screenshots and the landing screens show the
  browser test's synthetic outfit and Chromium's fake camera. Replace them with real captures before any store
  submission (`brand-kit/README.md`); Apple rejects listings whose screenshots do not show the app as shipped.
- **Password recovery needs a mail provider.** An account can carry an email (optional, confirmed by a link) and a
  forgotten password is reset by a link that lives an hour; without `Email__*` settings the app says recovery is off
  and writes the links to its log instead. A reset does not end sessions that are already signed in.
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
- **Metrics are for moderators.** `/api/metrics/pilot` answers only through a moderator's session (403 otherwise),
  so the URL can leave the team; it still returns aggregates only, and guest checks are left out of them.
- **Single process, single SQLite file.** The in-flight reservation that closes the cap race, the per-address
  guest count and the brake on guest attempts live in memory; run one instance.
- **The limiters trust `X-Forwarded-For`.** Right behind the tunnel; if Kestrel is exposed directly, the
  global daily ceiling bounds the damage. Raise `Limits__SignupsPerHourPerIp` for a launch hour on a shared
  network.
- **Comments and reports are rate limited per account.** 30 comments and 20 reports an hour
  (`Limits__CommentsPerHour`, `Limits__ReportsPerHour`), answered with 429, "Slow down a little. Try again in a
  bit." and a `Retry-After`. Fixed one-hour windows counted in memory: they reset when the app restarts and are per
  process, one more reason to run one instance. A brake, not a spam filter: a patient flood stays under them, and the
  queue and suspensions are the answer to that.
- **Cookies are Secure only over HTTPS.** Plain `http://localhost` works for development; anything users reach
  must be behind HTTPS (the tunnel).
- **Calibration is unverified until you run it.** The build was tested against a stubbed model; run
  `scripts/calibrate.py` on real photos before judging scores.
- **Photos stay on disk until the look or the account is deleted; clips go with the look.** There is no
  retention job yet, and clips are large: watch the disk (`DEPLOY.md`, "What to watch").
- **Push needs an installed app on iPhone** (iOS 16.4+, added to the home screen). Android and desktop
  Chrome work in the tab.
- **Brands and models on items are the person's word, or a confirmed guess; never verified.** A brand on a piece is
  what its owner typed or confirmed, and the stylist's suggestion is a mark it saw in a photo, not a catalogue
  lookup. The brand pages group looks by that word; a misspelt or made-up brand is its own page, and nothing checks
  that a store link leads to the piece it is on. Reports and the queue are the answer to abuse, as for captions.
- **Affiliate hosts are empty by default.** No link earns anything until `Affiliate:Hosts` lists a programme you
  joined; the item sheet shows the commission line under every store link regardless of the host while
  `Affiliate:Disclosure` is `true`, the default (`/api/config` carries it). Fill the list only with programmes whose
  terms you accepted; the app knows nothing about them.
- **The board's per-address protection is nothing.** The eligibility rules are the guard (a check made, an account
  two days old, three fires per pair); they raise the cost of gaming, they do not make it impossible, and the
  moderator's exclusion is the answer when it happens. Only the out door has an address limit.
- **The closer has no HTTP trigger.** A week closes when `BoardCloser` next runs (at start, then every five minutes),
  so a week's places, the hall and the badge appear a few minutes after midnight; there is no route to close one by
  hand. A week already in the hall keeps its rows if a moderator later un-hides or re-includes a look that would have
  placed, and a week the closer found empty is remembered as empty for the life of the process, so a late change to a
  past week is closed only after a restart.
- **SQLite folds ASCII only.** The item search matches a brand with `COLLATE NOCASE` and the name or model with
  `LIKE`, both of which fold Latin letters only: a Cyrillic or Hebrew brand typed in another case is a separate brand
  on `#/items/<brand>`, while the brands list groups in .NET and merges them. Fine at pilot scale; a collation later.
- **The board cache is per process.** The running week and the one before it are served from memory for
  `Board:CacheSeconds` (60, real time), two entries at most and dropped by an exclusion; every other week is computed
  on each read; the out-door limiter is a memory window too. One more reason to run one instance.
- **A past check posted from "Your checks" gets the editor without a photo box.** The post sheet hands the item editor
  the preview only while it is the still this result was judged on; a check has no photo route, so an older check
  opened from your history gets the rows and no dots at posting. "Edit items" on the look, which has the photo route,
  adds them.
- **The .NET project is still called `FitCheck.Api`.** A mechanical rename for when the repository gets its
  final name; nothing a user sees says FitCheck.

## Not in this version

- **No in-app cancel for Pro.** Stripe renews until the subscription is cancelled on Stripe's side (or by the owner);
  the app shows the end date and the terms say to write to us. A customer-portal link is the obvious next step.
- **No block-user, still.** Reports and moderation exist; a person cannot mute or block another. Apple's UGC checklist
  expects it before a store submission (`STORE.md`).
- **No marketplace.** A tagged piece links out to a store through the out door; nothing is sold, carted or paid for
  in the app, and no catalogue, price or stock is kept.
- **No product catalogue search and no image search.** Looks are found by the brand, category, name and model
  people typed (`GET /api/items`), not by a product database, a barcode or the photo itself; the stylist's brand
  suggestion is a mark it saw, not a lookup.
- **No sponsor self-service.** The week's sponsor is `Board:Sponsor:*` in the settings, set by the owner; a brand
  cannot buy or book a week from the app.
- **No native push.** Web Push works in the browser install; inside the Capacitor shells nothing arrives (APNs and
  FCM need a sender that does not exist yet, `mobile/README.md`).
- **Sounds on posts, deliberately not built.** Music on looks would bring licensing, break the muted feed and pull
  the loop away from the check; see `DECISIONS.md`, Round 9.
- Still out, as before: direct messages, prize fulfilment inside the app, sign-in with Apple or Google, closet
  memory, a blob store behind `IImageStore`, and real age assurance. The store apps are described in `mobile/`
  but nothing is installed there. None of it is scaffolded on purpose.

## Launch files

- **The slogan** is *Check the look.* / *בודקים את הלוק.* (`app.slogan`) with the tagline *A stylist in your pocket,
  and a community that lights it up.* (`app.tagline`); the runners-up are kept in `MARKETING.md`.
- **Link previews.** `index.html` carries Open Graph and Twitter tags with the card at `/brand/og-1200x630.png`
  (`og-1200x630-he.png` for Hebrew); the manifest's description is the tagline. The absolute URLs say
  `https://looks.example.com`: replace that with the production origin before launch (`DEPLOY.md`, go-live).
- **The landing pages** are static, `/landing/` and `/landing/index.he.html`: the stage, the wordmark, the slogan,
  three phones, three feature blocks, the CTA, the home-screen note and the legal links; no app JS, and the service
  worker lets `/landing/` navigations through to the network instead of answering with the app shell. Their four
  absolute URLs carry the same placeholder origin.
- **The brand kit** in `brand-kit/` (logos on dark, light and nothing, monochrome SVG+PNG, the lockup, the social
  avatar, five covers plus the OG cards, three story templates in both languages, twenty store screenshots) is
  rendered by `tools/brand/render-kit.js` from HTML templates with the real brand SVGs and the browser test's
  screenshots; its README lists every file. Change `COPY` at the top of the script and re-run to regenerate.
- **The store listings** (`STORE.md`: names, descriptions, keywords, the 16+ rating, the privacy labels, the URLs,
  the payments rule for the wrapped app, the review notes), **the launch plan** (`MARKETING.md`: positioning, the
  voice, the first ten posts, four weeks in one community, the numbers to watch) and **the store shells**
  (`mobile/`: a Capacitor wrap pointed at the production URL, nothing installed).

## Decisions

Every judgment call made while building, and every place the brief and instinct disagreed, is in
[`DECISIONS.md`](DECISIONS.md). The plans each phase was built from are in [`PHASE2.md`](PHASE2.md) and
[`PHASE3.md`](PHASE3.md); the visual system is [`DESIGN.md`](DESIGN.md).
