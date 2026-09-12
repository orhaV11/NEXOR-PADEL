# OREVOSH

A social app for looks. Pick where the outfit is going (date, office, streetwear…), add a photo or film a
short clip with the in-app camera, and a stylist scores the look **relative to that intent**, breaks the score
into fit, color and accessories, lists what you are wearing, what works, **the one tip**, and the one accessory
that would finish the look. The check is private. Post it and it joins a feed where people
react with fire, comment, save and follow; clips play in the feed, and a story card carries the score to
Instagram and TikTok.
Tag the brands you wear with `@brand`, add `#tags`, and brands feature the community looks they love, open
challenges with a prize, and tag products on their own looks. Browsing needs no account, and neither does the
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
migrations existed) is upgraded in place after a `.bak-<stamp>` copy is written next to it. A `fitcheck.db`
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
FitCheck.Api.dll --admin yourhandle` (`DEPLOY.md`, step 7).

### Read the pilot metrics

```bash
curl -s -b 'orevosh.session=<a moderator’s cookie>' http://localhost:5000/api/metrics/pilot | jq
# or, signed in as a moderator in the app: #/admin/metrics
```

The first block covers signed-in people's checks with `status = "ok"` (`returnRate` = users whose second OK check
happened at most 7 days after their first ÷ users with at least one OK check; guest checks are left out). The
`social` block counts users, brands, posts, fires, follows, comments, open and ended challenges, votes, mentions,
featured looks, clips, push subscriptions, and people active in the last 7 days. The route answers only through a
moderator's session (below), and moderators see the same numbers drawn as a page at `#/admin/metrics` (one hero
figure, the return rate; tiles; the score distribution as bars), linked from the moderation page.

### Run the tests

```bash
dotnet test        # from the repository root (FitCheck.sln)
```

422 tests: magic-byte detection for photos and clips, the disk store, analyzer mapping and clamping, locale
matching, the Anthropic client against a scripted HTTP handler, and endpoint tests against the real app with a
scripted vision client: signup and login rules (the date of birth among them), the CSRF header, uploads and
413/415/429/502, the plan caps, the ceiling and the global cap, guest checks (the cookie, one per cookie and per
address, the claim at signup and login, the sweep), clips (storage, Range streaming, deletion, limits), posting
(with the before/after link), fire, comments, saves, follows, the feed tabs and the For you ranking, reports
hiding content, the moderation queue and suspensions, challenges with votes and winner resolution, the daily
prompt, notifications and Web Push (against a recording push service), tags and mentions, featured looks,
verified brands, avatars, account-type switches, interests, Explore, search by name, tag and item, "Which one?"
comparisons (the comparer's prompt and mapping, the routes, the shared allowance), the insights math on a seeded
list, billing (Checkout against a recording Stripe, the signed webhook and its three events, `--pro`), account
recovery (the link origin, the per-account brakes, the address binding), account deletion removing files and
fixing other people's counters, the migrations and the pilot-database upgrade, backups, and the metrics math on
a seeded dataset.

There is also a browser test in [`tools/e2e`](tools/e2e/README.md): Playwright drives the real client in a
phone viewport against the real API with only the Anthropic API stubbed, as three people (a person in English,
a brand, and a person browsing in Hebrew), from a guest's check and signup with a birth date and the welcome
screen through posting with tags and mentions, featuring, Explore, a challenge and its winner, the in-app camera
with a fake device (a photo, then a clip with its frame picked), the story card, a comparison, the insights, a
search by piece, the Pro page, Today's look and a follow-up look, verified brands, the moderation queue and a
suspension, the numbers page, the guidelines, the terms and the privacy policy, the health and config routes, to
deleting an account.

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
| `Plans:FreeChecksPerDay` | `3` | Checks and comparisons together, per free account, over a rolling 24 hours (429 `error.plan_limit`, which names the Pro number) |
| `Plans:ProChecksPerDay` | `30` | The same for a Pro account. Clamped to `Limits:ChecksPerDay` |
| `Plans:GuestChecksPerDay` | `1` | Free checks for a visitor with no account, per guest cookie (`orevosh.guest`, one day) and, through the `guest` rate-limit policy, per client address per day (429 `error.guest_limit` either way). `0` turns guests off: `POST /api/checks` answers 401 signed out |
| `Plans:ProPriceText` | empty | Shown on the Pro page as the price, e.g. `₪19 / month`; empty hides it. Text only: the price itself is the Stripe price |
| `Plans:CompareNeedsPro` | `false` | Whether "Which one?" needs Pro (403 `error.pro_required` and a Pro card on `#/compare` otherwise). Off by default: Pro is a cap on a real cost, not a feature wall |
| `Billing:Provider` | `manual` | `manual`: Pro is granted with `--pro`, and the Pro page shows a note instead of a checkout button. `stripe`: Checkout and the webhook are live once the three keys below are set; until they are, the routes answer 400 `error.billing_disabled` |
| `Billing:StripeSecretKey` / `StripePriceId` / `StripeWebhookSecret` | empty | Environment only (`Billing__StripeSecretKey`, `Billing__StripePriceId`, `Billing__StripeWebhookSecret`): the API secret key (`sk_test_…` works against Stripe's test mode), the recurring Pro price (`price_…`), and the signing secret of the webhook endpoint (`whsec_…`). Read in `Services/StripeClient.cs` and `Endpoints/BillingEndpoints.cs`; the secret key is redacted from HttpClient logging |
| `Billing:PublicOrigin` | empty | Where Checkout returns to (`/#/pro?checkout=success` or `cancel`); the request's origin when empty |
| `Limits:ChecksPerDay` | `30` | The ceiling per account over a rolling 24 hours, whatever the plan says: `Plans:ProChecksPerDay` cannot exceed it. Counted including checks still in flight; failed calls do not count |
| `Limits:ChecksPerDayGlobal` | `1000` | Ceiling across all users and guests over a rolling 24 hours |
| `Limits:SignupsPerHourPerIp` | `50` | New accounts per client address per hour (address taken from `X-Forwarded-For` behind the tunnel) |
| `Limits:LoginsPerQuarterHourPerIp` | `30` | Login attempts per client address per 15 minutes |
| `Limits:CommentsPerHour` / `Limits:ReportsPerHour` | `30` / `20` | Per signed-in account, fixed one-hour windows in memory (429 `error.too_fast` with `Retry-After`) |
| `Limits:ReportsToHide` | `3` | Reports from distinct people after which a post or comment is hidden |

Any key can be overridden with an environment variable, e.g. `Plans__FreeChecksPerDay=5`. `ANTHROPIC_API_KEY`
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
| `POST /api/auth/signup` | `{ handle, password, birthDate, language, displayName? }` | `201` me. Handle: 2–40 letters, digits, dots or underscores, unique case-insensitively; password 8–200; `birthDate` is `yyyy-MM-dd` (what a date input sends), 16 years or more before today, not before 1900 and not in the future: 400 `birthdate_required`, `birthdate_invalid` or `underage` in that order after the handle and password rules. The date is stored and never returned by any route. `confirmed16Plus` from older clients is ignored. 409 taken (a handle listed in `Admin:Handles` counts as taken), 429 too many signups from one address |
| `POST /api/auth/login` | `{ handle, password }` | `200` me. 401 for a wrong handle or password (same message for both), 429 too many attempts |
| `POST /api/auth/logout` 🔒 | — | 204 |
| `GET /api/auth/me` 🔒 | — | `{ id, handle, name, accountType, language, bio, website, streak, unreadNotifications, avatarUrl, interests, isAdmin, email, emailVerified, plan, proUntil, verified, checksToday, checksPerDay }`. `isAdmin` is the account's persisted moderator flag, set at start from `Admin:Handles` or by `--admin`, never by a request. `plan` is `free` or `pro` (`pro` only while `proUntil` is in the future or open), `verified` is the `--verify` flag, `checksToday` counts the account's checks and comparisons in the rolling 24 hours (failed ones excluded) and `checksPerDay` is its cap. A suspended account gets 403 and is signed out |
| `POST /api/auth/forgot` | `{ handleOrEmail }` | `202` always, same body whether or not the account exists; mails a reset link when the account has a confirmed email (5 per hour per address) |
| `POST /api/auth/reset` | `{ token, password }` | `200` me, signed in. 400 for a used, expired or unknown link (the link survives a too-short password) |
| `POST /api/auth/verify-email` | `{ token }` | `200` me. Confirms the address the link was sent to, and only while that is still the account's address; works signed out, signs nobody in |
| `POST /api/users/me/email/resend` 🔒 | — | 204, a new confirmation link (5 per hour per address, and per account three per ten minutes or ten a day: 429) |
| `GET /api/config` | — | `{ maxImageBytes, maxVideoBytes, maxVideoSeconds, pushPublicKey?, email, transcoding, plans: { freeChecksPerDay, proChecksPerDay, guestChecksPerDay, proPriceText, compareNeedsPro, billing } }`. `billing` is true only when Stripe Checkout is live. No secrets |
| `GET /healthz` | — | `ok` when the database answers, 503 otherwise. For the proxy and uptime checks |
| `PATCH /api/users/me` 🔒 | `{ language?, displayName?, bio?, website?, accountType?, interests?, email? }` | Updated me. `accountType` is `Person` or `Brand`; `interests` is a list of intents (≤ 8); website must be https; `email` null leaves it, `""` clears it, a value stores it lower-cased (unique, never shown to others) and sends a confirmation link (400 when mail is off on this server, 409 when another account has it) |
| `POST /api/users/me/avatar` 🔒 | multipart `image` (JPEG/PNG/WebP ≤ 2 MB) | `200` me with a versioned `avatarUrl` |
| `DELETE /api/users/me/avatar` 🔒 | — | `200` me |
| `GET /api/users/{handle}/avatar?v=` | — | The photo, `Cache-Control: public, max-age=86400` |
| `DELETE /api/users/me` 🔒 | — | 204. Deletes the account, every check, photo, avatar, post, tag, mention, comment, fire, save, vote, follow and notification, and fixes other people's counters and featured marks. 403 for a moderator: `--unadmin` first, or the freed handle would be promoted again on the next restart |
| `GET /api/users/me/checks` 🔒 | — | Last 50 checks, newest first, each with `postId` when posted |
| `GET /api/users/me/saved` 🔒 | `?offset&limit` | Saved posts, newest first |
| `GET /api/users/me/insights` 🔒 | — | `{ checks, avgScore, bestScore, bestIntent, weakestCategory, weakestShare, accessoriesMissingShare, streak, lines }` over the last 200 OK checks: the average (one decimal) and best score, the intent with the highest average among those checked at least twice (else the most checked), the item category most often called weak and its share of the looks that had items, the share of rubric-v2 checks with no accessories on, and two to four ready sentences in the caller's language. Under 3 checks only `checks` and `streak` are filled and `lines` is empty. Private, like the checks |
| `GET /api/users/me/comparisons` 🔒 | — | The caller's last 20 comparisons, newest first (the shape of `GET /api/compare/{id}`) |
| `GET /api/users/{handle}` | — | Public profile: counts, best score, streak, `avatarUrl`, `verified`, `featured`, `community`, `viewer.following` |
| `GET /api/users/{handle}/posts` | `?offset&limit` | That person's public posts |
| `GET /api/users/{handle}/community` | `?offset&limit` | Public posts that mention this account |
| `GET /api/users/{handle}/featured` | `?offset&limit` | Brand: posts it featured. Person: their posts that were featured |
| `POST` / `DELETE /api/users/{handle}/follow` 🔒 | — | `{ followers, following }`. 400 when following yourself |
| `POST /api/checks` | multipart: `intent`, `occasion?`, `language`, `image`, `video?` | `201 { id, intent, occasion, language, createdAt, latencyMs, status, score, feedback, postId }`. **No session needed:** signed out, the call is a guest's, named by the `orevosh.guest` cookie (minted on the first one, sent back with the answer), allowed `Plans:GuestChecksPerDay` times per cookie and per client address per day (429 `error.guest_limit`; 401 `error.sign_in_required` when that setting is 0); a guest's photo is stored in a shared folder until claimed or swept. Signed in, the account's plan cap applies (429 `error.plan_limit`, which names the Pro number, with `Retry-After`). `video` is an optional MP4/MOV/WebM clip of the same look (≤ `Storage:MaxVideoBytes`); the stylist judges only `image`, the frame the person picked, and the clip is stored with the check when the status is `ok`. 413 too large (still or clip), 415 not JPEG/PNG/WebP (or not MP4/WebM for the clip), 429 over a cap, 502 model failure |
| `POST /api/checks/claim` 🔒 | — | `{ claimed }`: every check and comparison carrying the caller's guest cookie becomes the account's (owner set, token cleared, `claimedAt` stamped, the files moved into the account's folder) and the cookie is dropped. `{ claimed: 0 }` when there was nothing, so the client calls it blind after signup, after login and at every signed-in boot |
| `GET /api/checks/{id}` | — | The check, for its owner or for the guest whose cookie made it (404 to anyone else, the same as a missing id) |
| `POST /api/compare` 🔒 | multipart: `intent`, `occasion?`, `language`, `imageA`, `imageB` | `201 { id, intent, occasion, language, createdAt, latencyMs, status, feedback: { status, winner, scoreA, scoreB, headlineA, headlineB, reason, oneTip, message? }, imageUrlA, imageUrlB }`: both photos go to the stylist in one call (`PromptVersion` `cmp-v1`, the analyzer's rules and calibration) and one wins, `a` or `b`. Counts against the same daily allowance as a check; 400 `compare_two_photos` without both, 403 `pro_required` when `Plans:CompareNeedsPro` is on and the account is not Pro, and otherwise the same 413/415/429/502 as a check (at the cap a free account hears `error.plan_limit`, a Pro account `error.rate_limited`). `not_outfit` says which photo to replace; `rejected` keeps nothing but the status. Private and never postable |
| `GET /api/compare/{id}` 🔒 | — | The comparison, owner only (404 otherwise) |
| `GET /api/compare/{id}/image/a` and `/b` 🔒 | — | The two photos, owner only, `Cache-Control: private`. The only route that serves them; 404 for a comparison that kept none |
| `POST /api/posts` 🔒 | `{ checkId, caption?, challengeId?, products?, beforePostId? }` | `201` post. The check must be yours, `ok`, and not yet posted; caption up to 140 characters, its `#tags` (first 5) and `@mentions` of existing handles (first 5) are stored and mentioned accounts are notified; a caption carrying an open challenge's hashtag enters that challenge (once per person; `challengeId` is still accepted); `products` (brands only, up to 3) are `{ label, url, price? }` with https URLs; `beforePostId` names one of your own visible looks this one improves on ("after the tip": 400 `error.before_invalid` for anyone else's look, a hidden one, or the look of this very check). At posting the stylist's item names are copied onto the look, lower-cased (up to 8, 60 characters each, with their category), so `/api/search` finds it by piece; the item verdicts and notes stay private |
| `GET /api/posts/{id}` | — | The post: `user, intent, score, intentMatch, headline, caption, challengeId, challengeTitle, fireCount, commentCount, fired, saved, isMine, hidden, votes, products, imageUrl, videoUrl?, breakdown?, before?, createdAt, tags, mentions, featuredBy`. `breakdown` is `{ fit, color, accessories }` for a rubric-v2 check; `before` is `{ postId, score, imageUrl }` of the earlier look (left off while that look is under review; the link is cleared when it is deleted). Hidden posts are visible to their author and to moderators only; a suspended author's posts are hidden |
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
| `GET /api/search?q=` | — | `{ users, tags, posts }` for a 1–40 character query: handle prefix or display-name substring (brands first), tag prefix, and up to 12 visible looks whose stylist-named items contain the term ("black boots"), newest first. The client shows the three under `#/search/<term>` |
| `GET /api/tags/{tag}/posts` | `?offset&limit` | Public posts carrying the tag, newest first |
| `GET /api/today` | — | `{ tag, title, hint, intent?, date, posts, posted }`: the day's prompt (one of 30 in `Services/DailyPrompts.cs`, picked by the UTC day of the year, so it turns over at midnight UTC and everyone sees the same one), its title and hint in the caller's language, up to 60 visible looks posted today with its hashtag (newest first), and whether the caller posted one (a look under review still counts). Public |
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
| `GET /api/billing/state` 🔒 | — | `{ plan, proUntil, billing, proPriceText }`: the effective plan, whether Stripe Checkout is live, and the price text |
| `POST /api/billing/checkout` 🔒 | — | `{ url }` of a Stripe Checkout Session (subscription, the Pro price, the account id as `client_reference_id` and `metadata.userId`, the confirmed email prefilled, an existing customer reused) that returns to `/#/pro?checkout=success` or `cancel`. 400 `error.billing_disabled` while the provider is `manual` or a key is missing, 502 `error.billing_failed` when Stripe did not answer with a page |
| `POST /api/billing/webhook` | Stripe's event, raw | `200 { received: true }`. No session, no CSRF header: the `Stripe-Signature` header (`t=…,v1=…`, HMAC-SHA256 over `t.body` with `Billing:StripeWebhookSecret`, within five minutes of now) is the guard, 400 `error.billing_signature` otherwise. `checkout.session.completed` puts the account on Pro for 35 days from now and stores the customer id; `invoice.paid` (except the first, `subscription_create`) extends Pro by 35 days from the later of now and the current end; `customer.subscription.deleted` ends Pro now (the plan field keeps saying a subscription existed); every other event, and an event naming no account, is answered 200 so Stripe stops sending it. No event ids are kept: repeats re-stamp, over-extend by one period at worst, or end what already ended |
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
  Program.cs                      wiring, migrations on start, cookie auth, rate limiter (incl. the "guest" policy), CSRF
                                  header check (the Stripe webhook exempt), security headers, static files, /api/config,
                                  /healthz, --vapid, --backup, --admin, --unadmin, --verify, --unverify, --pro
  Data/DatabaseSetup.cs           Migrate(), WAL, the pilot-database upgrade, the backup command
  Data/AdminSync.cs               the Admin:Handles sync at start and the --admin/--unadmin, --verify/--unverify, --pro commands
  Data/Migrations/                the EF Core migrations (dotnet ef migrations add <Name> for the next one)
  Domain/                         StyleIntent, AppUser (plan, proUntil, birthDate, verified), OutfitCheck (guestToken,
                                  claimedAt), OutfitFeedback (+ ComparisonFeedback), Social.cs (posts with beforePostId,
                                  tags, mentions, comments, fire, saves, follows, challenges, votes, notifications, reports,
                                  auth tokens, post items, comparisons), options (Plans, Billing, Email, Limits…)
  Data/AppDbContext.cs            SQLite via EF Core; unique indexes carry the one-per-person rules
  Services/OutfitAnalyzer.cs      ← the stylist: system prompt, intent guide, tool schema, PromptVersion, mapping
  Services/OutfitComparer.cs      ← "Which one?": two photos in one call, the same rules and calibration, PromptVersion cmp-v1
  Services/AnthropicVisionClient  Messages API over HttpClient: base64 images + forced tool call, 60s timeout, one retry
  Services/Plans.cs               IsPro (the flag and the end date) and CapFor (the plan's cap, never above Limits:ChecksPerDay)
  Services/GuestChecks.cs         the guest cookie, the claim (rows and files move to the account), GuestCheckSweeper (hourly)
  Services/StripeClient.cs        one form POST that opens a Checkout Session, and the webhook signature check; no SDK
  Services/PostItems.cs           the stylist's item names copied onto a look at posting, for the search by piece
  Services/DailyPrompts.cs        the 30 "Today's look" prompts, one a day by the UTC day of the year
  Services/RecoveryTokens.cs      one-time links (hashed), the link origin rule, the per-account mail brakes
  Services/CaptionParser.cs       #tags and @mentions out of a caption
  Services/FeedRanker.cs          the For you score, a pure function
  Services/DiskImageStore.cs      storage/<userId>/<checkId>.<ext> and storage/<userId>/avatar.<ext>, behind IImageStore
  Services/Sessions.cs            cookie sign-in and the current user id
  Services/Notifier.cs            activity rows, deduplicated per actor and target
  Services/ChallengeResolver.cs   fixes the winner exactly once when a challenge has ended
  Services/PostReader.cs          posts → DTOs with tags, mentions, featured-by, the before look and the viewer's state, in batches
  Services/Localizer.cs           server messages (en/he) and Accept-Language matching
  Endpoints/                      auth, users, checks (+ claim), compare, posts (+ comments), feed, explore (+ search, tags),
                                  challenges, notifications, insights, today, billing, push, admin, metrics
  wwwroot/index.html, app.css     the shell (with the Open Graph and Twitter tags) and the design system: Ring of Fire, see
                                  DESIGN.md (logical properties for RTL)
  wwwroot/app/core.js             state, i18n, API, router, bottom sheets, gestures, look cards, the claim call, the brand mark
  wwwroot/app/views/*.js          one module per screen: feed, post, explore, challenges, check, camera, compare, pro, insights,
                                  today, activity, profile, auth, settings, admin, dashboard (the numbers), pages, legal
  wwwroot/app/after.js            the "after the tip" picker for the post sheet
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
  the headline, your caption and the three sub-scores (fit, color, accessories). The tip, the items and the
  accessories read never go public. Deleting the post makes the photo private again.
- **Photos are never served by path.** Check photos and avatars live under `Storage:Root`, outside `wwwroot`.
  The post image route (visible posts only) and the avatar route are the only doors.
- **16+ by date of birth, self-declared.** Signup needs a birth date (16 on the day; nothing before 1900 or in the
  future), stored on the account and never returned by any route. A checkbox is not an age rule; a date is at
  least a rule. Still not age assurance: see the limitations below.
- **A guest gets one check, and keeps it by signing up.** The first wow comes before the account: signed out,
  `POST /api/checks` works once per guest cookie and once per address a day, the result offers "Sign up to keep it
  and post it", and the claim at signup or login makes the check an ordinary one (posting, history, deletion).
  Unclaimed guest checks and their photos are swept after a day (the log says `Guest sweep: …`). Guests cannot
  post, compare, or read anyone else's check.
- **Cost control.** Every check is a paid model call, so the caps are the product: 3 a day on Free, 30 on Pro, 1 as
  a guest, checks and comparisons counted together over a rolling 24 hours (429 with a friendly message and
  `Retry-After`), `Limits:ChecksPerDay` as the ceiling no plan exceeds, a global ceiling of 1000 a day, a 6 MB
  upload cap, and the client downscales to 1280px JPEG (avatars to 320px) before uploading. In-flight calls count;
  failed model calls do not.
- **Pro is a cap on a real cost, not a feature wall.** Pro raises the daily cap; comparisons and insights stay free
  by default (`Plans:CompareNeedsPro`). Pro is a flag plus an end date on the account, written by Stripe's webhook
  or by `--pro`, never by a request; a lapsed period falls back to Free by itself. Stripe stays behind
  `Billing:Provider`: with `manual`, the Pro page shows a note and nothing pretends to charge.
- **Comparisons are private and never postable.** "Which one?" keeps both photos only when the stylist confirmed
  two outfits, serves them to the owner alone, and has no post route.
- **Items are indexed from the stylist's words, never from captions.** At posting, the item names of the check are
  copied onto the look (lower-cased, up to 8); a caption cannot put a look under "black boots". The verdicts and
  notes stay private with the tip.
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
## Known limitations (read before inviting anyone)

- **Age is self-declared.** A date of birth typed at signup is a rule, not age assurance: anyone can type a
  date. Before any public launch, integrate the Apple and Google age-signal APIs (or an equivalent provider) and
  gate account creation on the result.
- **Guest checks cost money.** A visitor's free look is a real model call with no account behind it. The guard is
  the cap per guest cookie and the brake per client address (`Plans:GuestChecksPerDay`, one a day each) plus the
  global ceiling; a patient script that rotates addresses gets one check per address. Set `Plans__GuestChecksPerDay=0`
  to close the door, and watch the model bill either way.
- **Brand accounts are self-declared; verification is by hand.** Anyone can switch to brand mode in settings, and
  only a brand the owner ran `--verify` for carries the check. There is no form to ask for it and no process
  behind it beyond the owner knowing who is behind the account.
- **Stripe has not run against a live account.** Checkout, the webhook and its three events were built and tested
  against a recording stand-in and signed test events; the first real subscription, renewal and cancellation are
  the proof. Run it in test mode (`sk_test_…`, the Stripe CLI forwarding to `/api/billing/webhook`) before the
  provider is switched to `stripe` on a server people pay on. No event ids are stored, so a replayed
  `invoice.paid` over-extends by one period; acceptable for a pilot, not for scale.
- **The pilot upgrade path is a command.** With `Billing:Provider` at `manual` the Pro page says Pro is switched on
  by hand, and the owner runs `--pro <handle> <months>`; there is no in-app request or cancel, and a person ends
  Pro by writing to the owner (the terms say so).
- **The legal pages need a lawyer.** `#/terms` and `#/privacy` (version 1, dated 2026-09-08) are written from what
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
- **Single process, single SQLite file.** The in-flight reservation that closes the cap race lives in memory;
  run one instance.
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
- **The .NET project is still called `FitCheck.Api`.** A mechanical rename for when the repository gets its
  final name; nothing a user sees says FitCheck.

## Not in this version

- **No in-app cancel for Pro.** Stripe renews until the subscription is cancelled on Stripe's side (or by the owner);
  the app shows the end date and the terms say to write to us. A customer-portal link is the obvious next step.
- **No block-user.** Reports and moderation exist; a person cannot mute or block another. Apple's UGC checklist
  expects it before a store submission (`STORE.md`).
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
