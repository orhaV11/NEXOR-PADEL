# OREVOSH

A social app for looks. Say **where the outfit is going** (everyday, date, office, party, formal, sport) and, if you
want, **how you want it to read** (streetwear, old money, minimal, classic — or nothing at all, which is a real
answer), add a photo or film a short clip with the in-app camera, and a stylist scores the look **relative to that
intent**, breaks the score into fit, color and accessories, lists what you are wearing, what works, **the one tip**
— which may be *change nothing*, when the look is already right — and the one accessory that would finish the look.
Where the two questions disagree the occasion wins. The check is private, and it **remembers**: say what happened to
the tip (it worked, it did not, not my style, I do not own that), post the second photo as **"I tried it"** and both
verdicts sit on one card, **keep** the pieces the stylist named and your own wardrobe starts naming the tips.
Post it and it joins a feed where people
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

**Going live?** [`LAUNCH.md`](LAUNCH.md) is the runbook, in English and in Hebrew: what to have ready (a domain, a
host, an Anthropic key with a spend limit, an email sender, a support mailbox, the legal review), the Fly.io path and
the server path command by command, the first day, money, the store apps, and what to do when something breaks. An
evening, start to finish. [`DEPLOY.md`](DEPLOY.md) is the reference behind it.

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

Open the printed `https://…` URL on the phone, in the browser.

**Do not add a quick tunnel to the home screen.** A quick tunnel's hostname is new every time it starts, and an
installed web app is bound to the origin it was installed from: the next day the icon opens a Cloudflare error page
with no browser chrome around it, and the new URL is a different site with no session, no saved language and no
push. Installing, and anything you ask a pilot user to install, wants an origin that stays — the Fly URL, a named
Cloudflare tunnel on a domain you own, or ngrok's static domain. A quick tunnel is for looking at the app in a
browser tab, which is most of what a first phone test is.

On that stable origin: **iPhone** → Share → "Add to Home Screen", in **Safari** (an in-app browser — WhatsApp,
Instagram, Telegram — has no such menu, and neither does Chrome or Firefox on iOS). **Android Chrome** does *not*
pop its own install bar: the app asks for `beforeinstallprompt` first and calls `preventDefault()` on it, because
Chrome's mini-infobar pins itself to the bottom of the viewport, exactly over the tab bar and the Check control. So
install from the app's own **Install** card on Home, or from Chrome's ⋮ menu → *Install app*.

**On an iPhone, the installed app has its own cookie jar.** It does not inherit the Safari session, the guest
check, the saved language or anything else: sign in once inside the new icon, and a link tapped in WhatsApp still
opens in Safari as a different visitor. Install after signing up, not before.

### Maintenance commands

The same program runs the maintenance commands; each does its job and exits without starting the server. From
`src/FitCheck.Api`, the lines are the same in bash and in PowerShell:

```bash
dotnet run -- --vapid                 # print a VAPID key pair for Web Push; set Push__PublicKey and Push__PrivateKey, restart
dotnet run -- --backup backups        # a consistent copy of the database and the media folder into ./backups (git-ignored)
dotnet run -- --backup backups --keep 14   # the same, then prune to the 14 newest database and storage copies
dotnet run -- --doctor                # read the configuration and this machine and print one line per check
dotnet run -- --doctor --live         # the same, plus what leaves the machine: Anthropic and Stripe, never the mail server
dotnet run -- --stripe-check          # ask Stripe whether the key, the price and the webhook the app was given are real
dotnet run -- --admin yourhandle      # make an existing account a moderator: sign up with the handle first
dotnet run -- --unadmin yourhandle    # take that away
dotnet run -- --verify nexor          # mark an existing brand account as verified: a check inside its BRAND mark, everywhere it appears
dotnet run -- --unverify nexor        # take that away
dotnet run -- --pro noa 3             # put an existing account on Pro for 3 months (31 days each, from now; 1 to 120)
dotnet run -- --pro noa off           # back to Free
```

`--doctor` is the one to run after any change to a setting and before any launch. It prints **seventeen lines**, one
per check, each `ok`, a warning or a short reason, and exits 0 when everything a live server needs is in place and 1
otherwise. In the order it prints them: `origin` (the public origin mail links and Checkout returns are built from),
`previews` (whether the three shipped pages still carry the placeholder host `looks.example.com`, which is the
difference between a shared link that unfurls with a picture and one that does not), `anthropic` (the key),
`anthropic-url` (the base URL and the model), `email`, `billing`, `plans` (the caps against the ceiling), `push` (the
VAPID keys), `admin` (the moderator list, and the accounts `--admin` promoted), `board` (the time zone), `affiliate`,
`storage` (the photo folder, actually written to and the file removed again), `database` (the file and what it still
has to migrate), `ffmpeg`, `disk` (the free space where the data lives), `spend` (what a model call is priced at here
and the day's ceiling) and `alerts` (whether anything at all would shout). The last two lines are the tally —
`doctor: 9 ok, 7 warnings, 1 failure` — and the verdict, `Ready.`, `Ready, with warnings to read.` or `Not ready: fix
the failures above and run it again.` **Only a failure changes the exit code**; a warning is the operator's call.
`--doctor --live` adds the calls that cost something or leave the machine:
one small Anthropic call with the configured key and model (a fraction of a cent), and, when the provider is `stripe`,
two reads from Stripe. **It never dials the mail server**: `--doctor` reads the `Email__*` settings and says whether
they could work, and nothing in this program opens an SMTP connection or logs in. The only thing that tests the sender
is sending: ask for a password reset from the app (or sign up) with your own address and watch the mail arrive, and
read the log line if it does not. `--stripe-check` is the Stripe half on its own — the key, the price and the webhook
endpoint — and it writes nothing and charges nobody. `/readyz` answers the machine-side half of `--doctor` over HTTP,
for a deploy or an uptime checker to wait on.

The account commands exit with code 1 when no account has the handle (sign up first, then run it again) and 2 on a
usage error; a leading `@` on the handle is fine. `--verify` and `--pro` are the only things that write the verified
flag and the plan by hand: no request can, and with `Billing:Provider` left at `manual` the `--pro` command is the
whole upgrade path. On a server the same commands run inside the container, `docker compose exec app dotnet
FitCheck.Api.dll --admin yourhandle` (`DEPLOY.md`, step 7). Round 10 added no command: the weekly board closes itself
in the background (`Services/BoardCloser.cs`, the log says when), a moderator pulls a look off it through the API, and
affiliate programmes are settings (`Affiliate:Hosts`). Round 11 added the three above, for going live and staying
live. Blocking, the billing portal and the data export add none: they are things a person does in the app.

One more, and it is not a maintenance command but a one-off before the first build:

```bash
node tools/brand/set-origin.js https://looks.example.com   # write the production origin into the static files
```

The link-preview tags, the two landing pages and the store shell carry `https://looks.example.com` as a placeholder,
and they are static files inside the image, so this runs **before** `fly deploy` or `docker compose build`
(`LAUNCH.md`, 1.2). It looks at eleven files in all — those four, plus `mobile/README.md`, `STORE.md`,
`MARKETING.md`, `brand-kit/README.md`, `DEPLOY.md`, this file and `.env.example` — rewrites the ones that still carry
the placeholder, and prints each with a count. `node tools/brand/set-origin.js --check` exits 1 while a placeholder is
left in any of them, which is the line for CI and for the last look before a build; `grep -rl looks.example.com .` is
the same question asked by hand.

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
`/api/board`), the last two read from the `Counters` table the routes increment. Round 14 appended five more to the
same block: `privateScores`, `commentOpeners`, `beforeAfterShares`, `beforeAfterSharesPlain` and
`constraintChallenges`.

Five blocks sit beside it, each with its own DTO and nothing shared with the tiles above:

| Block | What it answers |
|---|---|
| `stylist` | **Did the tip land?** Yes / no / unanswered over every OK check by an account, overall, by intent and by language, plus how many photos the stylist called "no outfit" and how many it refused. The only number that says whether the stylist is any good |
| `spend` | **What the model cost today**, at the owner's configured prices, the day's ceiling, whether the app is resting on it, and fourteen days of it. Always an estimate, never an invoice |
| `funnel` | **The growth loop**: fourteen days of landing views, guest checks, signups, first posts and public-page arrivals, today's conversion between those steps, and the invites — sent, accepted, and who is inviting |
| `wardrobe` | **Round 15.** The two numbers `MARKETING.md` watches. `keepRate` is `keepers` (accounts with at least one kept piece) ÷ `checkedUsers` (accounts with at least one OK check — the same number the hero tile reads, so the page cannot say two different things about who has checked), with `items` as the raw row count behind it; `dontOwnRate` is `dontOwn` ÷ `reasons`, the "I do not own that" answer over every typed answer to the tip, and it is watched **falling**, because a tip that draws it is exactly the tip a wardrobe should have prevented. `toStylistOff` counts the accounts that turned the sending off, which is what keeps a flat `dontOwnRate` readable: a wardrobe nobody sends cannot prevent anything. **A rate with nothing to divide by is absent from the JSON, not `0`** |
| `breakdownAverages` | The mean of each rubric sub-score over the checks that carry one; absent while no check does |

The route answers only through a moderator's session (below), and moderators see the same numbers drawn as a page at
`#/admin/metrics` (one hero figure, the return rate; tiles; the score distribution as bars; the stylist, money and
funnel sections), linked from the moderation page.

### Run the tests

```bash
dotnet test        # from the repository root (FitCheck.sln)
```

892 tests, and the number is meant to be read off the run, not trusted from here: magic-byte detection for photos
and clips, the disk store, analyzer mapping and clamping, locale
matching, the Anthropic client against a scripted HTTP handler, and endpoint tests against the real app with a
scripted vision client: signup and login rules (the date of birth and the phone's own day among them), the CSRF
header, uploads and 413/415/429/502, the plan caps, the ceiling and the global cap, what the allowances count and
the `Retry-After` they name, guest checks (the cookie, one per cookie and ten per address, counted from looks given, the
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
seeded dataset. Round 13 added the verdict's own verdict and the honest no-outfit answer, the languages a server may
actually offer, the spend meter and the day's ceiling, the alerts, the doctor's own lines, and the growth loop (the
public look and profile pages, the invites, the funnel, the Sunday digest). Round 14 added the occasion/style split
and the keep verdict (`IntentSplitTests`), the typed reasons and the taste profile (`TasteTests`), "I tried it"
(`TriedTests`), the wardrobe (`WardrobeTests`), Pro's own comparison allowance (`PlansTests`), and the community
round (`ScorePrivacyTests`, the openers, the before/after shares, constraint challenges). Round 15 added the
wardrobe's numbers (`WardrobeMetricsTests`) and the wardrobe reaching a comparison (`WardrobeComparisonTests`).
Two of them are policy rather than behaviour and are the reason a careless change fails the build:
`IdorEnumerationTests.Rules` in `SecurityTests` makes every route with an `{id}` or a `{handle}` declare, in writing,
what stops a stranger enumerating it, and `LanguagesTests` keeps the four locale files at key parity.

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
calibration text in `Services/OutfitAnalyzer.cs`, bump `PromptVersion`, and compare the two reports. It still speaks
the one-word `--intent`, which `POST /api/checks` still understands and splits into the pair.

The other half of the question is **how much the same photo's score moves between runs**, which matters more since
Round 14 anchored the scale band by band (the model removed `temperature`, so there is no knob to pin it with):

```bash
node tools/eval/stylist.js --photo outfit.jpg --occasion date --style minimal --runs 8 \
  --base http://127.0.0.1:5000 --handle yourhandle --password ...
```

It sends the same photo `--runs` times and prints the spread — min, max, mean, standard deviation — how often the
breakdown moved, how often the tip was a *keep*, and the tips side by side to be read; repeat `--photo` for a table
per photo, and it exits non-zero when a spread is wider than `--max-spread` (2 by default).
`tools/eval/README.md` explains the numbers and what a pass costs (photos × runs = model calls). **No real-model
numbers have been taken yet**: the sandbox this was written in has no route to Anthropic, so the harness has only
ever run against a local stand-in built to move its scores on purpose.

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
| `Plans:GuestChecksPerDay` | `1` | Free checks for a visitor with no account, per guest cookie (`orevosh.guest`, one day), over a rolling 24 hours (429 `error.guest_limit` with `Retry-After`). What is counted is a stored check, `ok`, `not_outfit` or `rejected` (the ones that cost a model call): a refused upload (400/413/415), a 502 or a dropped connection never spends it, and `error.guest_limit` is only ever sent for a look actually given. The count is read from the rows. `0` turns guests off: `POST /api/checks` answers 401 signed out, and the check screen shows the sign-in prompt with the submit disabled; with guests on, its hint carries this number |
| `Plans:GuestChecksPerAddressPerDay` | `10` | The same, per client **address** rather than per cookie, over a rolling 24 hours (429 `error.too_fast` with `Retry-After` — never `error.guest_limit`, since that request's own cookie may have spent nothing). Deliberately far above the per-cookie cap: behind one router, office, café or carrier NAT everybody shares an address, so holding the two at the same number meant the third friend you showed the app to was refused before taking a photo. Kept in memory (`GuestAddressCounter`), so a restart forgets the day, and never below `Plans:GuestChecksPerDay` whatever is configured |
| `Plans:GuestAttemptsPerDay` | `20` | The brake on attempts: the `guest` rate-limit policy on `POST /api/checks`, a fixed 24-hour window per client address in memory for signed-out calls, whatever they come to, answering 429 `error.too_fast` with `Retry-After` beyond it. Well above `Plans:GuestChecksPerDay` on purpose, so a refused photo or a model outage never locks a shared address out of its look; a signed-in call passes through it unlimited |
| `Plans:ProPriceText` | empty | Shown on the Pro page as the price, e.g. `₪19 / month`; empty hides it. Text only: the price itself is the Stripe price |
| `Plans:CompareNeedsPro` | `false` | Whether "Which one?" and your insights need Pro (403 `error.pro_required` and a Pro card on `#/compare` and `#/insights` otherwise; the Pro page lists them as benefits only then). Off by default: Pro is a cap on a real cost, not a feature wall |
| `Plans:ProComparesPerDay` | `30` | **Round 14.** A Pro account's own rolling-day allowance for comparisons, counted apart from its checks, never above `Limits:ChecksPerDay`. What Pro sells: deciding between two outfits never spends a check. A free account is unchanged — one allowance for checks and comparisons together |
| `Plans:WardrobeMaxItems` | `200` | The most pieces one account may keep. A brake on a script, not a product limit, and the same for free and Pro |
| `Plans:WardrobeNamesToStylist` | `12` | How many of the wearer's own piece names travel with a check, most recently worn first. `0` keeps the wardrobe but never sends it, and the doctor says so |
| `Plans:WardrobeNeedsPro` | `true` | Whether the wardrobe REACHING THE STYLIST is Pro's. The wardrobe itself is everyone's on every server — it cannot build itself behind a wall — and this gates only the advice from it (`POST /api/wardrobe/stylist` answers 403 `error.pro_required` to a free account) |
| `Plans:TasteProfile` | `true` | Whether this server has the taste profile built (the memory of what a person liked and turned down, `Services/Taste.cs`). It landed with the loop, so it is on; turning it off takes the benefit off the Pro page and stops the advisory being built. The Pro page lists it as a benefit only when this is on: nothing on that page may promise a thing this server cannot do (`PlansTests`) |
| `Plans:NoOutfitForgivenPerDay` | `3` | How many "that is not an outfit" answers a day do not count against a person's own allowance. The model call was still made and the global ceiling still counts it — this is about not punishing somebody for a photo the stylist could not read. `0` makes every one count |
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
| `Anthropic:PriceInPerMillion` / `PriceOutPerMillion` | `2.00` / `10.00` | **Round 13 — money.** USD per million tokens, the owner's own contract prices. Every dollar on the numbers page and the daily ceiling is built on these: they are settings, never Anthropic's invoice, and `appsettings.json` says so in a `_prices` note. The doctor prints them |
| `Limits:SpendPerDayUsd` | `0` | The day's ceiling in **estimated** USD (a UTC day). At the ceiling every route that would ask the model answers 503 and spends nobody's allowance. `0` is off, and `--doctor` warns about that; 5 is a sane pilot number |
| `Alerts:Webhook` / `Alerts:Email` | empty | **Round 13 — money.** Where a readiness flip, the spend ceiling, a run of model failures, a full disk or a failed backup shouts. `Alerts__Webhook` is an https incoming-webhook URL that takes `{ text }` and is a secret, so it belongs in the environment and never in `appsettings.json`; `Alerts__Email` is one address, sent through the app's own mail. With neither set, every alert is only a log line nobody is watching, and the doctor says so |
| `Alerts:ModelFailuresIn10Min` / `Alerts:DiskFreeMb` | `5` / `512` | The two thresholds that shout on their own: model failures inside ten minutes, and the free space where the data lives |
| `Languages:Enabled` | `["en","he"]` | The languages this server actually offers. A language ships when a native reader has read it, not when the file exists: `ar` and `ru` are translated and shipped in the repository but left out of this list, and a check asking for one falls back to English. `/api/config` publishes the list |
| `Digest:Enabled` / `Digest:Hour` | `true` / `9` | The Sunday mail: the week a person had, sent only to a confirmed address and only to an account something happened to. The hour is local to `Board:TimeZone`. Nothing goes out at all without mail configured and a public origin |
| `Logging:Requests` | `false` | One log line per request — the method, the path, the status and how long it took — on top of the usual lines. Off by default: a pilot's log is worth reading, and this is a lot of lines. Turn it on (`Logging__Requests=true`) for the first days of a launch and while chasing something, then off again. It never logs a body, a cookie, a header or a query string's values, so nothing a person typed and no session lands in the log; the client address is already there for the rate limiters |

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
| `GET /api/auth/me` 🔒 | — | `{ id, handle, name, accountType, language, bio, website, streak, unreadNotifications, avatarUrl, interests, isAdmin, email, emailVerified, plan, proUntil, verified, checksToday, checksPerDay, badge? }`. `isAdmin` is the account's persisted moderator flag, set at start from `Admin:Handles` or by `--admin`, never by a request. `plan` is `free` or `pro` (`pro` only while `proUntil` is in the future or open), `verified` is the `--verify` flag, `checksToday` counts the account's checks in the rolling 24 hours (failed ones excluded) and `checksPerDay` is its cap; on a FREE account comparisons are counted in the same number, and on a PRO account they are not — Round 14 gives Pro a second allowance for comparisons alone (`Plans:ProComparesPerDay`, enforced on `POST /api/compare`), so `checksToday` on Pro is checks only. `badge` is last week's place in the top three of the looks board, `{ board: "looks", rank, weekStart }`, worn for this week only and absent otherwise. A suspended account gets 403 and is signed out |
| `POST /api/auth/forgot` | `{ handleOrEmail }` | `202` always, same body whether or not the account exists; mails a reset link when the account has a confirmed email (5 per hour per address) |
| `POST /api/auth/reset` | `{ token, password }` | `200` me, signed in. 400 for a used, expired or unknown link (the link survives a too-short password) |
| `POST /api/auth/verify-email` | `{ token }` | `200` me. Confirms the address the link was sent to, and only while that is still the account's address; works signed out, signs nobody in |
| `POST /api/users/me/email/resend` 🔒 | — | 204, a new confirmation link (5 per hour per address, and per account three per ten minutes or ten a day: 429) |
| `GET /api/config` | — | `{ maxImageBytes, maxVideoBytes, maxVideoSeconds, pushPublicKey?, email, transcoding, plans: { freeChecksPerDay, proChecksPerDay, guestChecksPerDay, proPriceText, compareNeedsPro, billing }, affiliate: { disclosure }, publicOrigin? }`. `plans.proChecksPerDay` is what a Pro account really gets (`Plans:ProChecksPerDay` clamped to `Limits:ChecksPerDay`); `billing` is true only when Stripe Checkout is live; `affiliate.disclosure` is `Affiliate:Disclosure`, whether the item sheet shows the commission line under a store link (the hosts and their parameters stay on the server); `publicOrigin` is `Email:PublicOrigin`, else `Billing:PublicOrigin`, trimmed, absent when neither is set, and the shared video's end card names its host. No secrets |
| `GET /healthz` | — | `ok` when the database answers, 503 otherwise. For the proxy and uptime checks |
| `GET /readyz` | — | `{ ok, checks }`: is this machine ready to serve? Public, `Cache-Control: no-store`. `checks` maps a name to `"ok"` or a short reason: `db` (a query answers and the schema is at the current migration), `storage` (`Storage:Root` exists and a file can be written and removed there) and, only while `Storage:Transcode` is on, `ffmpeg` (the binary is found). 200 with `ok` true when every check passes; 503 with `ok` false and the failing checks named. A reason never carries a path, a version or a secret, so the line is safe to leave public. `/healthz` is untouched and stays what the proxy, the Docker health check and `fly.toml` poll |
| `PATCH /api/users/me` 🔒 | `{ language?, displayName?, bio?, website?, accountType?, interests?, email? }` | Updated me. `accountType` is `Person` or `Brand`; `interests` is a list of intents (≤ 8); website must be https; `email` null leaves it, `""` clears it, a value stores it lower-cased (unique, never shown to others) and sends a confirmation link (400 when mail is off on this server, 409 when another account has it) |
| `POST /api/users/me/avatar` 🔒 | multipart `image` (JPEG/PNG/WebP ≤ 2 MB) | `200` me with a versioned `avatarUrl` |
| `DELETE /api/users/me/avatar` 🔒 | — | `200` me |
| `GET /api/users/{handle}/avatar?v=` | — | The photo, `Cache-Control: public, max-age=86400` |
| `DELETE /api/users/me` 🔒 | — | 204. Deletes the account, every check and comparison, their photos and clips (by path, `ImagePath`/`VideoPath` and `ImagePathA`/`B`, as well as the account's folder), the avatar, every post, tag, mention, comment, fire, save, vote, follow and notification, the items on its looks, the board exclusions on its looks and its places in the hall (`WeeklyWinners`), and fixes other people's counters and featured marks. The looks it pulled off the board while it was a moderator stay off: each exclusion stands with its reason and loses its signature (`byUserId` null). 403 for a moderator: `--unadmin` first, or the freed handle would be promoted again on the next restart |
| `GET /api/users/me/checks` 🔒 | — | Last 50 checks, newest first, each with `postId` when posted |
| `GET /api/users/me/saved` 🔒 | `?offset&limit` | Saved posts, newest first |
| `GET /api/users/me/insights` 🔒 | — | `{ checks, avgScore, bestScore, bestIntent, weakestCategory, weakestShare, accessoriesMissingShare, streak, lines }` over the last 200 OK checks: the average (one decimal) and best score, the intent with the highest average among those checked at least twice (else the most checked), the item category most often called weak and its share of the looks that had items, the share of rubric-v2 checks with no accessories on, and two to four ready sentences in the caller's language. Under 3 checks only `checks` and `streak` are filled and `lines` is empty. 403 `error.pro_required` when `Plans:CompareNeedsPro` is on and the account is not Pro (the client shows the Pro card on `#/insights`). Private, like the checks |
| `GET /api/users/me/comparisons` 🔒 | — | The caller's last 20 comparisons, newest first (the shape of `GET /api/compare/{id}`) |
| `GET /api/users/me/export` 🔒 | — | Everything the account wrote, as one file: `{ exportedAt, account, checks[], posts[], comments[], follows[], followers[], comparisons[], blocks[], notifications[] }`, one read per table, the account's own rows only, newest first, hidden looks and hidden comments included (they are the person's words). `Content-Disposition: attachment; filename="orevosh-<handle>-<yyyyMMdd>.json"` — an ASCII stand-in plus RFC 5987 `filename*` when the handle has letters outside ASCII, since a header value is ASCII — and `Cache-Control: no-store`, so the browser saves it and no cache keeps it. Comments carry only the text the person wrote, follows, followers and blocks only handles, notifications only a type and a time: nothing in the file is another person's. **No photo, no clip, no birth date, no password hash, no billing id** (no route returns a birth date, and an export is a route). 3 an hour per account through the `export` policy (429 `error.too_fast`), 500 `error.export_failed` when a read fails half-way — nothing is written either way, so a retry is safe. A suspended account still exports. Logged as `Export: {userId} took their data`. Settings → "Download your data" fetches it and hands the blob to the browser |
| `GET /api/users/{handle}` | — | Public profile: counts, best score, streak, `avatarUrl`, `verified`, `featured`, `community`, `viewer.following`, and `badge` (the same shape as on `me`: last week's top-three place on the looks board, this week only) |
| `GET /api/users/{handle}/posts` | `?offset&limit` | That person's public posts |
| `GET /api/users/{handle}/community` | `?offset&limit` | Public posts that mention this account |
| `GET /api/users/{handle}/featured` | `?offset&limit` | Brand: posts it featured. Person: their posts that were featured |
| `POST` / `DELETE /api/users/{handle}/follow` 🔒 | — | `{ followers, following }`. 400 when following yourself |
| `POST /api/users/{handle}/block` 🔒 | — | `200` `{ user, createdAt }`. Writes the block and ends the follow in both directions, silently: no notification, nothing pushed. 404 `error.user_not_found` (a suspended account answers like a missing one, as the profile does), 400 `error.cannot_block_self`, 409 `error.already_blocked`. What it does everywhere else is one predicate over the pair in either direction (`Services/Blocks.cs`): the two accounts' looks leave each other's feeds, Explore, search, the tag pages, the saved list and the profile grids; the other's look, photo, clip and comment list read as missing (a moderator still opens them for the queue); each side's comments leave the other's lists, rows kept; a fire, save, comment, follow, mention or feature that targets the other is refused 403 `error.blocked`; and no notification crosses the pair, old ones included, until an unblock. The blocker's own profile view of the other carries `viewer.blocked` so the menu can say Unblock. **Nothing tells the blocked person**: there is no `blockedBy` field anywhere, `error.blocked` is the same sentence whichever side acted, and a blocked person sees what a quiet account looks like. The public board keeps every look. Logged as `Block: {blockerId} blocked {blockedId}` |
| `DELETE /api/users/{handle}/block` 🔒 | — | 204, the row gone. 404 `error.not_blocked` when there was none. Unblocking restores nothing: the follows stay ended and the old notifications stay gone |
| `GET /api/users/me/blocks` 🔒 | — | `{ items: [{ user, createdAt }] }`: the accounts the caller blocked, newest first, whole (a person blocks a handful, not a feed). The client draws it at `#/settings/blocked` |
| `POST /api/checks` | multipart: `occasion`, `style?`, `note?`, `language`, `image`, `video?` | `201 { id, intent, occasion, style, language, createdAt, latencyMs, status, score, feedback, postId }`. **Round 14 split the one question in two** ("Round 14 — the stylist" below has the refusals and the one-word rule): `occasion` is where the outfit is going, `style` is optional and empty means none, and the wearer's own free line is now `note`. A client from before the split that still sends `intent` is understood — the one word is split into the pair and its `occasion` field is read as the free line. **No session needed:** signed out, the call is a guest's, named by the `orevosh.guest` cookie (minted on the first one, sent back with the answer), allowed `Plans:GuestChecksPerDay` times per cookie and per client address over a rolling day, counted from stored checks (429 `error.guest_limit` with `Retry-After`, only ever for a look actually given: a refused upload, a 502 or a dropped connection spends nothing; 401 `error.sign_in_required` when that setting is 0; beyond `Plans:GuestAttemptsPerDay` attempts from one address in 24 hours, 429 `error.too_fast`); a guest's photo is stored in a shared folder until claimed or swept. Signed in, the account's plan cap applies, with `Retry-After`: at the cap a free account hears `error.plan_limit` (which names the Pro number), a Pro account `error.rate_limited` (with its cap), as on the compare route; at `Limits:ChecksPerDayGlobal` everyone hears `error.rate_limited_global`. `Retry-After` on both routes is when a permit actually frees up: the expiry of the (count − cap + 1)th oldest counted call, not the oldest. `video` is an optional MP4/MOV/WebM clip of the same look (≤ `Storage:MaxVideoBytes`); the stylist judges only `image`, the frame the person picked, and the clip is stored with the check when the status is `ok` (a guest's clip is transcoded only after the claim). 413 too large (still or clip), 415 not JPEG/PNG/WebP (or not MP4/WebM for the clip), 429 over a cap, 502 model failure |
| `POST /api/checks/claim` 🔒 | — | `{ claimed }`: every check and comparison carrying the caller's guest cookie becomes the account's (owner set, token cleared, `claimedAt` stamped, the files moved into the account's folder, a claimed clip queued for the transcoder) and the cookie is dropped. All or nothing: a file that cannot be copied (a full disk, a file missing from the store) answers 500 `error.server`, the rows stay the guest's and the cookie stays, and the client claims again on its next load. `{ claimed: 0 }` when there was nothing, so the client calls it blind after signup, after login and at every signed-in boot |
| `GET /api/checks/{id}` | — | The check, for its owner or for the guest whose cookie made it (404 to anyone else, the same as a missing id) |
| `POST /api/checks/{id}/shared-video` | — | `204`. One tally (`videos_made`, on the numbers page as "Share videos made") after the person saved or shared the check's video. The video itself is drawn and encoded **on the phone** (a 12-second 1080×1920 file: H.264 MP4 where the browser can, else VP9/VP8 WebM, else the story card PNG) and never touches the server. Owner or guest-cookie only, 404 to anyone else like the GET; 30 per hour per account or address (429 `error.too_fast`) |
| `POST /api/compare` 🔒 | multipart: `intent`, `occasion?`, `language`, `imageA`, `imageB` | `201 { id, intent, occasion, language, createdAt, latencyMs, status, feedback: { status, winner, scoreA, scoreB, headlineA, headlineB, reason, oneTip, message? }, imageUrlA, imageUrlB }`: both photos go to the stylist in one call (`PromptVersion` `cmp-v1`, the analyzer's rules and calibration) and one wins, `a` or `b`. Round 14: on a FREE account this counts against the same daily allowance as a check, as before; on a PRO account it counts against `Plans:ProComparesPerDay`, an allowance of its own, so comparing two looks never spends a check (`Limits:ChecksPerDayGlobal` and `Limits:SpendPerDayUsd` still count every stored call, whatever the plan). 400 `compare_two_photos` without both, 403 `pro_required` when `Plans:CompareNeedsPro` is on and the account is not Pro, and otherwise the same 413/415/429/502 as a check (at the cap a free account hears `error.plan_limit`, a Pro account `error.rate_limited`). `not_outfit` says which photo to replace; `rejected` keeps nothing but the status. Private and never postable |
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
| `POST /api/billing/portal` 🔒 | — | `{ url }` of a Stripe Billing Portal session for the account's customer (`customer` and `return_url` = the origin plus `/#/settings`, posted form-encoded to `v1/billing_portal/sessions` through the same named client as Checkout); the client sends the person there in the same tab, and that is where a subscription is changed or cancelled. 404 `error.portal_unavailable` ("Manage your plan by writing to us.") while the provider is `manual` or the account has no customer id — a Pro granted by `--pro` has nothing on Stripe's side, and the Pro page and the settings row show that sentence instead of the button. 502 `error.portal_failed` when Stripe answers with no url. Nothing is written here: what the person does on the portal comes back through the webhook |
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
  Program.cs                      wiring, migrations on start, cookie auth, the Data Protection key ring persisted beside the
                                  database, response compression (Brotli then gzip, text types only), rate limiter (incl. the
                                  "guest" attempts brake and the "out" door's minute), CSRF header check (the Stripe webhook
                                  exempt), security headers (a route may set Referrer-Policy first), static files, /api/config,
                                  /healthz, /readyz, --vapid, --backup, --doctor, --stripe-check, --admin, --unadmin, --verify,
                                  --unverify, --pro
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
  Services/Localizer.cs           server messages in all four languages (en/he/ar/ru) and Accept-Language matching
  Services/Wardrobe.cs            the pieces a check named, the rows kept from them, and the names that travel to the stylist
                                  (on a check and, since Round 15, on a comparison)
  Services/Taste.cs               the typed reasons, the profile they build and the advisory the stylist is sent; the learning switch
  Services/Funnel.cs              the fourteen-day funnel, the invites and the arrival tallies behind the numbers page
  Services/SpendMeter.cs          what a day of model calls is estimated to cost, the ceiling and the fourteen-day series
  Services/Alerter.cs             the one place a readiness flip, the ceiling, a run of failures or a failed backup shouts
  Services/Doctor.cs              --doctor, --doctor --live and --stripe-check: the seventeen lines, no secret printed
  Services/Readiness.cs           the machine-side half of the doctor, over HTTP, at /readyz
  Services/Transcoder.cs          the background ffmpeg pass that turns a WebM clip into H.264 MP4
  Services/Digest.cs              the Sunday mail: the week a person had, only to a confirmed address
  Services/Security/              the headers and the CSP every response is served under, the Exif strip on every stored
                                  photo and clip, the per-account brake, session revocation
  Endpoints/                      auth, users, checks (+ claim), feedback (the typed reason, "I tried it", the taste card),
                                  compare, wardrobe, posts (+ comments), items (tagging, the item search, the brands list, the
                                  out door), board (the week, the hall, the moderator's exclusion), feed, explore (+ search,
                                  tags), challenges, notifications, insights, today, billing, push, blocks, export, admin,
                                  metrics, the server-rendered public pages (/look, /u, /digest), health
  wwwroot/index.html, app.css     the shell (with the Open Graph and Twitter tags) and the design system: Ring of Fire, see
                                  DESIGN.md (logical properties for RTL)
  wwwroot/app/core.js             state, i18n, API, router, bottom sheets, gestures, look cards, the claim call, the brand mark
  wwwroot/app/views/*.js          one module per screen: feed, post, explore, challenges, check, camera, compare, pro, insights,
                                  today, activity, profile, auth, settings, admin, dashboard (the numbers), pages, legal,
                                  items (the brand and search pages), board (the board, the hall, the Explore strip, the
                                  reset card, the profile badge), wardrobe (the pieces you kept), blocked
  wwwroot/app/after.js            the "after the tip" picker for the post sheet, and the before/after share
  wwwroot/app/sharecard.js        the story card drawn on a canvas: the look, the before/after pair, the colophon
  wwwroot/app/sharevideo.js       "Share as video": the story card as a 12-second vertical video, encoded on the device
  wwwroot/vendor/                 mp4-muxer and webm-muxer (MIT, local modules, no CDN)
  wwwroot/app/items.js            the tagging editor of the post sheet and of "Edit items": the rows, the brand suggestion, the dot
  wwwroot/app/wardrobe.js         the keep line on the result screen (#wardrobe-keep) and the one cached read of /api/wardrobe
  wwwroot/app/taste.js            the typed reasons under the tip, "I tried it", and the taste card in settings
  wwwroot/app/invite.js           ?via, the stored handle, the invite link and the share sheet behind it
  wwwroot/manifest.webmanifest,   the installable app; the service worker caches the shell only, never the API, and lets
  wwwroot/sw.js, wwwroot/icons/   /landing/ navigations through to the network
  wwwroot/offline.html            what a navigation gets when the network is gone
  wwwroot/i18n/*.json             UI strings in all four languages — en, he, ar, ru (the terms and the privacy policy among
                                  them), kept at key parity by a test; add a locale by adding a file
  wwwroot/landing/                the static landing pages (index.html, index.he.html) and their screens
  wwwroot/brand/                  the mark and wordmark SVGs, the concept notes, the two OG cards
tests/FitCheck.Api.Tests/         xUnit
tools/e2e/                        optional browser test (Playwright + a stub of the Anthropic API)
tools/brand/                      render-kit.js and its templates: regenerates brand-kit/, the OG cards and the landing screens
brand-kit/                        logos, covers, story templates, store screenshots (README lists every file)
mobile/                           the Capacitor wrap for the stores: config and instructions, nothing installed
tools/eval/stylist.js             how far the same photo's score moves between runs, with the tips side by side
scripts/calibrate.py              calibration run against the real model: score spread, latency, rule 1 scan
STORE.md, MARKETING.md            the store listings and the launch plan
```

The model is forced to call a tool (`tool_choice: {type: "tool"}`) whose input schema is our feedback shape,
so the answer is always JSON we can validate. Scores are clamped to 1–10, intent match to 0–100, and anything
descriptive is dropped when the status is not `ok`.

**Two things in `Program.cs` that are easy to miss and expensive to lose.**

- **Text is compressed on the way out.** Fly's edge does not do it, and the shell is about 710 KB of JavaScript, CSS
  and JSON — several seconds on a phone's connection before the first screen, and every byte of it squeezes to
  roughly a fifth. `UseResponseCompression()` sits **before** the static files, so it covers them and every JSON
  answer below; Brotli first, gzip for a client that cannot take it. Only text types are listed: a photo, a clip and
  a share video are already compressed formats, and running them through again spends CPU to make them slightly
  bigger. There is nothing to configure and nothing to turn on.
- **The Data Protection key ring is persisted beside the database**, in a `keys/` folder next to the `.db` file
  (`/data/keys` on Fly and on the server), with a fixed application name. This is what encrypts the session cookie.
  Without it ASP.NET writes the keys to `$HOME/.aspnet` inside the container, which a deploy throws away: **everyone
  signed in on a phone is silently signed out by the next deploy**, and with mail unconfigured "forgot password"
  cannot bring them back. It is outside `Storage:Root` on purpose, so a media backup does not copy the keys with the
  photos — which also means **a backup has to take the folder deliberately** (`DEPLOY.md`, "Backups"). The
  application name is a literal rather than the default (the content root path), so moving `WORKDIR` cannot quietly
  rotate every key. Treat the folder as a secret: it is as private as the photos.

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
- **Guest checks cost money.** A visitor's free look is a real model call with no account behind it. The guard is three
  numbers, counted from looks actually given: one per guest cookie (`Plans:GuestChecksPerDay`), ten per client address
  (`Plans:GuestChecksPerAddressPerDay`, in memory, so a restart forgets the day) and twenty attempts per address
  whatever they come to (`Plans:GuestAttemptsPerDay`), under the global ceiling. The address number is ten and not one
  because an address is a household, an office or a whole carrier, not a person — but that is also the honest size of
  the hole: a patient script that rotates addresses gets ten checks per address. Set `Plans__GuestChecksPerDay=0` to
  close the door, set `Limits__SpendPerDayUsd`, and watch the model bill either way.
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
- **No mute.** Blocking is built (`POST /api/users/{handle}/block`, `#/settings/blocked`, and the predicate that takes
  the two accounts out of each other's feeds, lists and notifications), which is what Apple's UGC checklist asks for;
  the softer thing — quieting someone without cutting them off — is not.
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
- **No photo of a kept piece.** The wardrobe is a list of names (Round 14): a name is all the stylist needs to say
  "the brown ones you wore on the 4th", and photographing a closet is the hour of work that kills these products
  before the first minute of value. A piece carries the checks it appeared in, and those have the photos.
- Still out, as before: direct messages, prize fulfilment inside the app, sign-in with Apple or Google, a blob store
  behind `IImageStore`, and real age assurance. The store apps are described in `mobile/` but nothing is installed
  there. None of it is scaffolded on purpose. **Closet memory is no longer on this list**: Round 14 built it, as a
  wardrobe that fills itself one tap at a time from the pieces a check named.

## Launch files

- **The slogan** is *Check the look.* / *בודקים את הלוק.* (`app.slogan`) with the tagline *A stylist in your pocket,
  and a community that lights it up.* (`app.tagline`); the runners-up are kept in `MARKETING.md`.
- **Link previews.** `index.html` carries Open Graph and Twitter tags with the card at `/brand/og-1200x630.png`
  (`og-1200x630-he.png` for Hebrew); the manifest's description is the tagline. **`og:url` is deliberately absent**
  from all three shipped pages: every crawler falls back to the URL it actually fetched, so the address is right on
  any host — a tunnel, a staging name, the real domain — with nothing to configure and nothing to get wrong. Only
  `og:image` and `twitter:image` still need an absolute URL (the spec wants one, and relative resolution is
  unreliable in exactly the unfurlers this is for), so those two say `https://looks.example.com`: replace that with
  the production origin before launch (`DEPLOY.md`, go-live), and `--doctor`'s `previews` line warns while any of the
  three pages still carries the placeholder.
- **The landing pages** are static, `/landing/` and `/landing/index.he.html`: the stage, the wordmark, the slogan,
  three phones, three feature blocks, the CTA, the home-screen note and the legal links; no app JS, and the service
  worker lets `/landing/` navigations through to the network instead of answering with the app shell. Their five
  absolute URLs each (canonical, both `hreflang` links, `og:image`, `twitter:image`) carry the same placeholder
  origin, and `tools/brand/set-origin.js` rewrites them. `node tools/brand/set-origin.js --check` counts what is
  left: two in `index.html`, five in each landing page.
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

## Round 13 — the product made whole: the verdict's own verdict, the honest no-outfit answer, languages shipped only when real, the last polish

*(One builder's section, appended for the lead to fold into the parts above.)*

**The verdict's own verdict.** After the one tip, the result screen asks one quiet question, *Did the tip land?*, with two
44px answers; a tap stores the answer at once, an optional one-line note follows (120 characters, Send or Skip), then
thanks. It is the only number that says whether the stylist is any good, and it is on the numbers page.

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/checks/{id}/useful` | `{ useful: bool, note?: string }` | `200 { id, useful, usefulAt, note }`. The owner, or the guest whose cookie made the check; anyone else, and a call with neither a session nor a guest cookie, gets the 404 of `GET /api/checks/{id}` (`error.check_not_found`). Only a check the stylist scored can be rated (400 `error.useful_not_scored`); `useful` is required (400 `error.useful_invalid`); `note` is trimmed, line breaks and control characters folded, at most 120 characters (400 `error.useful_note_too_long`), stored as null when empty. May be changed: every call overwrites `Useful`, `UsefulAt` (the app's clock) and `UsefulNote`. 60 an hour per account or address through the `useful` policy (429 `error.too_fast`). Logged as `Useful: check {CheckId} yes|no`, never the note |

`GET /api/checks/{id}` and `GET /api/users/me/checks` carry `useful`, `usefulAt` and `usefulNote` once the person has
answered (absent before). The export's `checks[]` carries the same three. `POST /api/checks` alone answers with one more
field, `counted`: whether this check counted against the day's allowance (below). `/api/metrics/pilot` gains `stylist`:
`{ useful: { yes, no, unanswered, rate }, byIntent: { Date: {…}, … }, byLanguage: { en: {…}, … }, notOutfit, rejected }`
over every `ok` check by an account (guest rows are left out until claimed, as everywhere on that page); `rate` is yes ÷
(yes + no), four decimals, absent while nobody has answered. The numbers page draws it as *The stylist*: the rate as a
hero figure, the three tiles, the door's two counts, and the split by intent and by language.

**A photo with no outfit.** The rubric (now `v4`) names the cases the model answers `not_outfit` to: no clothes clearly
visible; a landscape, a room, a street, an animal, food, an object, a product on its own; a screenshot, a drawing, a
meme, text; clothes laid flat or on a hanger with nobody in them; a crop too tight to read the outfit; a crowd or a
group where no one outfit is clearly the wearer's; and two people, which is a *Which one?* question, not a check.
Nudity, sexual content or an apparent child stay `rejected`. The one-line reason the model gives must be about the
photo, never about a person, and the server holds it to that: `OutfitAnalyzer.SafeNoOutfitMessage` folds it to one line,
cuts it at 200 characters, and drops it entirely when it contains a body, face, skin, hair, weight, age, gender or looks
word in English, Hebrew, Arabic or Russian; a comparison's `not_outfit` reason goes through the same filter. With the
reason dropped, `feedback.message` is absent and the client shows its own line. The result screen for `not_outfit`: *We
couldn't find an outfit in this photo*, the guidance (one outfit, full length, good light), the stylist's line when it
survived, *This one didn't count as a check* when the server said so, and *Take another photo*, which goes back to the
check screen with the photo button focused and the media sheet open. No score, no share, no post.

**Whether a no-outfit answer spends the allowance: it does not, up to a point.** The person got nothing for it, so the
first `Plans:NoOutfitForgivenPerDay` (3) no-outfit answers in a rolling day, oldest first, checks and comparisons alike,
are left out of the plan cap, the guest's free look (cookie and address) and `me.checksToday`; from the fourth on they
count like any stored check, so a stream of non-outfit photos still meets a cap. The global ceiling
(`Limits:ChecksPerDayGlobal`) counts every one of them, because each was a model call and the ceiling is about the bill;
the spend meter counts them too. `0` makes every no-outfit answer count, as before this round. The guest's brake on
attempts (`Plans:GuestAttemptsPerDay`) stands in front as before.

**Languages shipped only when real.** `Languages:Enabled` (default `["en", "he"]`; `Languages__Enabled__0=en`,
`__1=he` as environment variables) is the list of UI languages that are live. `/api/config` publishes it as `languages`
(English always first). The client offers only these in the switcher (with *More languages are on the way, once native
speakers have reviewed them* under the list while some are not), auto-detects only among these (a browser in Arabic gets
English), ignores a saved preference outside them, and fetches only their files; an account whose stored preference is
not live reads the app in the language it sees and its preference follows quietly. The stylist is asked to answer only
in a live language: a check or a comparison asked for in another one is written in English, the row says `en`, and the
call's messages are English too. The account preference itself (`PATCH /api/users/me`), the server's own strings and
the four locale files keep working in every language the app knows, so enabling Arabic or Russian is one setting after a
native reader has reviewed `wwwroot/i18n/<code>.json` and the block in `Services/Localizer.cs`. The service worker
precaches all four files regardless (they are small, and enabling one needs no new shell). The landing pages stay as
they are: English and Hebrew, the two that are live.

**The last polish.** `offline.html` is precached (`orevosh-shell-v6`) and answers a navigation with no network when
there is no cached shell to fall back on, and every `/landing/` navigation that fails: the mark, one line, *Try again*;
it reads the saved language and takes its three lines from the cached locale file, so it speaks Hebrew to someone who
used the app in Hebrew, and only in a language the app last saw as live (`core.js` leaves that list in `localStorage` at
boot, since the page cannot ask `/api/config`); an Arabic browser with no saved language gets the English page. On iOS Safari (not an in-app browser, not the installed app) the result screen shows once per
device, under the share row, *Keep OREVOSH on your home screen* with the Share → Add to Home Screen line and *Got it*;
the flag lives in `localStorage` behind try/catch. Focus along check → result → post: the feedback row's answers are a
labelled group, the note gets focus after a tap and thanks after Send or Skip, *Change* on an answered check returns focus
to the first choice, and *Take another photo* lands on the photo button so closing the media sheet returns there.

| Key | Default | Meaning |
|---|---|---|
| `Plans:NoOutfitForgivenPerDay` | `3` | How many no-outfit answers a person (an account, or a guest cookie) gets back in a rolling day: they spend neither the plan cap nor the guest's look; the ones after count. The global ceiling counts all of them. `0` counts every one |
| `Languages:Enabled` | `["en", "he"]` | The UI languages that are live: offered, detected, asked of the stylist and published on `/api/config`. English is always in the list. Add `ar` or `ru` once a native reader has reviewed its files |

Tests: `FeedbackTests` (the route's rules, the owner and the guest cookie, the 404, the brake, the export and the numbers
page, the rule-1 filter over sixteen lines in four languages, the generic line, the forgiveness and the ceiling) and
`LanguagesTests` (the list, `/api/config`, the stylist's language for a check, a comparison and an Arabic guest, one
setting enabling Russian, and key parity of the four locale files with the Round 13 prefixes present). Three earlier
tests that pinned `v3` now pin `v4`, and `GuestCheckTests` expects the forgiven no-outfit answer instead of the spent look.

## Round 13 — Money: the spend meter, the daily ceiling and the alerts (appended)

**The meter.** Every model call the API answers adds to `Counter` rows named for its UTC day: `spend:calls:yyyyMMdd`,
`spend:in:yyyyMMdd`, `spend:out:yyyyMMdd`, and `spend:cache_read:` / `spend:cache_write:` when the API reports cache
tokens (it does not today — the app uses no prompt caching). `AnthropicVisionClient` reads `usage.input_tokens`,
`usage.output_tokens`, `usage.cache_read_input_tokens` and `usage.cache_creation_input_tokens` off every answer and
hands them to `Services/SpendMeter.cs`. A call that FAILED at the API (a 4xx or 5xx whose body still carried usage, or a
timeout) is counted too, with whatever is known — somebody billed it. A call that never reached the API (no connection,
no key) counts nothing. The meter writes in its own DI scope, and never throws into the request: a check that already
happened must not fail because a tally did.

**The estimate is an estimate.** `Anthropic:PriceInPerMillion` (2.00) and `Anthropic:PriceOutPerMillion` (10.00) are USD
per million tokens, marked in `appsettings.json` as the owner's to set from their contract; the defaults are the
published list prices for the default model at the time of writing. Cache tokens are priced at the input price, which
overstates cache reads on purpose — a ceiling that guesses low is a ceiling that lets a real bill through. Nothing here
is an invoice, and the page says so.

**The ceiling.** `Limits:SpendPerDayUsd` (default `0` = off, which the doctor warns about and `LAUNCH.md` tells the
owner to set — 5 USD for the pilot). Once today's estimate reaches it, `POST /api/checks` and `POST /api/compare` answer
**503 `error.stylist_resting`** ("The stylist is resting until tomorrow. Your look is not spent.", in all four
languages) *before* the model is asked: no allowance spent, no guest free look spent, no row stored, no photo written.
One log line and one alert the first time it closes on a given day, never one per refused request. It opens again at the
next UTC midnight, because the rows are per UTC day. `Limits:ChecksPerDayGlobal` stays beside it as the count-based
brake: one caps how many calls are made, the other caps what they are estimated to cost. `GET /api/users/me/insights`
has no gate — it asks the model nothing.

**The numbers page.** `/api/metrics/pilot` carries a `spend` block (`SpendMetricsDto`): `today` (calls, input/output/cache
tokens, `estimatedUsd`), `ceilingUsd`, `resting`, the two prices, a 14-day `series` oldest-first with empty days as
zeroes, and `alertWebhook` / `alertEmail` — whether a channel is set, never its value. `#/admin/metrics` draws it as
*Model spend*: the hero estimate, four tiles, the 14-day bars, the prices line, and an *Alerts* row.

**The alerts.** `Services/Alerter.cs` sends one sentence to `Alerts:Webhook` (any https URL that takes Slack-shaped
`{ "text": "…" }`; a Discord webhook URL with `/slack` appended takes the same shape) and/or `Alerts:Email` (one
address, through the app's own `IEmailSender`, so it needs mail configured), and always to the log — with neither set,
the log line is the whole alert. **At most one alert per kind per hour**, in memory over one process, so a flapping
check cannot spam; "readiness went down" and "readiness came back" are different kinds, so a recovery is never
swallowed. The kinds: `started` (once at boot, with the version), `readiness.failing` / `readiness.ok`,
`spend.ceiling`, `model.failing` (more than `Alerts:ModelFailuresIn10Min`, default 5, failed calls inside ten minutes),
`disk.low` (free space under `Alerts:DiskFreeMb`, default 512, checked every five minutes by `AlertWatchdog`),
`backup.failed` (the `--backup` command now catches, alerts and exits 1), `billing.reversed` and `test`.
**Never a secret in an alert**: a name, a number and at most a setting's NAME. The webhook URL is itself a secret and
lives in the environment only.

**The doctor.** Two new lines. `spend` prints the prices in use and the ceiling, WARNs when no ceiling is set (naming 5
USD for a pilot) and when a price is 0 (an estimate of nothing can never reach a ceiling). `alerts` says whether either
channel is set, without printing either value, and WARNs when neither is; `--doctor --live` adds `alerts-live`, which
sends one real test alert down every configured channel, or skips when there is none.

**Stripe, honestly.** The webhook now reads `charge.refunded` and `charge.dispute.created`, which it used to ignore:
either ends Pro on the matching customer's account (the end date moves to now, as for a deleted subscription), logs a
warning and raises `billing.reversed`. **The endpoint must be subscribed to those two events in Stripe** — `--stripe-check`
does not yet require them (see `DEPLOY.md`, "Round 13 — Money").

| Key | Default | Meaning |
|---|---|---|
| `Anthropic:PriceInPerMillion` | `2.00` | USD per million input tokens, for the estimate only. Set it to your contract's price |
| `Anthropic:PriceOutPerMillion` | `10.00` | USD per million output tokens, same |
| `Limits:SpendPerDayUsd` | `0` | The day's estimated-USD ceiling (UTC day). `0` is off; above it every route that would ask the model answers 503 |
| `Alerts:Webhook` | `""` | https URL taking `{ "text": "…" }`. Environment only: it is a secret |
| `Alerts:Email` | `""` | One address, through the app's mail. Does nothing while mail is off |
| `Alerts:ModelFailuresIn10Min` | `5` | More than this many failed model calls in ten minutes raises the model alert. `0` turns it off |
| `Alerts:DiskFreeMb` | `512` | Free space under this on the data volume raises the disk alert. `0` turns it off |

Tests: `SpendTests` (`SpendMeterTests`: the estimate accumulating through a vision client that reports usage, the gate
closing at the ceiling and opening after midnight UTC on the fake clock, a guest's free look surviving a 503, a ceiling
of 0 being none, and the real `AnthropicVisionClient` against a scripted handler — the usage read, a billed failure
counted, a call that never reached the API not) and `AlertTests` (both channels and no secret in either, the hour-long
throttle, a readiness flip and back, one ceiling alert however many refusals, the ten-minute model window, the boot
line with the version, the disk floor, a dead channel not taking the app with it, the host-less `SendOnceAsync` path,
and the doctor's `spend`, `alerts` and `alerts-live` lines).

## Round 13 — the growth loop: a look has an address, a friend can be invited, the week comes back by mail

**A look has a public address.** `GET /look/{id}` is a posted look as a page: server-rendered HTML with no session and
no script — the photo, the score ring as inline SVG, the handle and the intent, the stylist's headline and the one tip,
*Check yours* into the app and *Open in OREVOSH* to `/#/post/{id}`. `GET /u/{handle}` is the same for a person: their
visible looks as a grid. Both carry the Open Graph and Twitter tags a paste into WhatsApp, Telegram, Facebook or X
unfurls: the headline (or *@handle's look on OREVOSH*) as the title, the score, the intent and the tip as the
description — the tip is what makes people tap — and `og:image` pointing at `GET /look/{id}/image`, the one public door
to a look's photo: a post that exists, is not hidden and whose author is not suspended, and 404 for everything else
(`/api/posts/{id}/image` stays the app's own route, cached `private`). The page runs in the look's own language and
direction, not the reader's, and asks nothing off this origin: the brand's faces are named and the system's stack
stands behind them, so nothing is fetched from a font host. `robots.txt` indexes `/look/` and `/u/` and disallows the
API, the client modules, the locale files and the app shell itself (a hash route has nothing for a crawler).
Every arrival is a day tally: `arrivals:look:yyyyMMdd`, `arrivals:look:share:yyyyMMdd` for one that carried
`?via=share`, `arrivals:profile:yyyyMMdd`.

**The share lands there.** The story card's colophon and the share video's end card name the look's public address
(`orevosh.app/look/…`, the host from `/api/config` `publicOrigin`) once the check has been posted; with no public
origin configured they stay as they were — the wordmark alone, never a guess from the page's own address. On a look,
*Copy link* opens the share sheet (or the clipboard) with that address, carrying the sharer's own `?via` when they are
signed in, so a look that travels is also an invite.

**Invite a friend.** Settings carries *Invite friends*: the person's link (`/?via=<handle>`), a copy button and the
share sheet. The shell and the landing pages read `?via` and keep the handle in `localStorage` (try/catch: private mode
simply has no memory); the signup body sends it once as `invitedBy` and then forgets it. The server validates the
handle — it must exist, not be suspended and not be the account being created — stores it as `AppUser.InvitedByUserId`
and gives **both** accounts one more check that day (`bonus:{userId:N}:yyyyMMdd`, which `Services/Spend.cs` takes off
the front of the day's counted calls, so the check route, the comparison route and `me.checksToday` honour it with no
line of their own). A handle that does not resolve is quietly not an invite: nobody is refused an account over a link
they did not choose.

**The week comes back by mail.** With mail on and `Email:PublicOrigin` set, `Services/Digest.cs` wakes hourly and sends
two plain-text messages, each three or four lines with one thing to tap and a signed unsubscribe: a welcome, once per
account, as soon as it has an address the person confirmed; and, on Sunday morning at `Digest:Hour` in
`Board:TimeZone`, the week — *your looks got N fires and M comments this week*, the week's top look on the board with
its public link, and *check a look* — to accounts with the toggle on that had at least one look or one check that week.
An account with nothing to say gets nothing. `AppUser.LastDigestAt` is stamped as each message goes, so a restart in
the middle of a run finishes the rest and never writes to one inbox twice, and a run outside the Sunday window sends no
digest at all. `GET /digest/off/{token}` — an HMAC of the account id, keyed by `Digest:Secret` or, while that is empty,
by the account's stored password hash — flips the toggle off with no login and says so on a small page. Settings has
the switch (`GET`/`POST /api/users/me/digest`), which says which of the two things is missing when the mail could not
go out anyway.

**The funnel.** One small middleware (`Services/Funnel.cs`) counts a landing-page view and an arrival carrying an
invite, per day, in the app's own `Counter` rows: no cookie, no address, nothing third-party. The numbers page shows
fourteen days — landing views, guest checks, signups, first posts, look pages, the ones from a share, invite links —
today's conversion between the steps, and the invites: links followed, accepted, and the handles doing the inviting.
Moderators only, like the rest of that page.

| Route | What it is |
|---|---|
| `GET /look/{id}` | A posted look as a page, with the link-preview tags. 404 (and `noindex`) for hidden, unposted, suspended or missing |
| `GET /look/{id}/image` | That look's photo, public, `Cache-Control: public, max-age=3600`. The only public door to a photo |
| `GET /u/{handle}` | A person's visible looks as a grid, with the same tags |
| `GET /digest/off/{token}` | The weekly mail's one-tap unsubscribe. No session |
| `GET`/`POST /api/users/me/digest` | The same switch inside the app: `{ on, canSend }` |

| Key | Default | Meaning |
|---|---|---|
| `Digest:Enabled` | `true` | The weekly mail and the welcome. Off turns both off without touching the mail settings |
| `Digest:Hour` | `9` | The hour of Sunday morning, local to `Board:TimeZone`, the digest goes out at |
| `Digest:Secret` | *(empty)* | Environment only (`Digest__Secret`). Keys the unsubscribe links; while empty they are keyed by the account's password hash, which works and voids open links on a password reset |

Tests: `PublicPageTests` (the tags a share unfurls, three real crawler user agents, the public image route and the four
ways a photo must not leave, the look's own language and direction, the profile grid, the arrival tallies, robots),
`InviteTests` (stored, both bonuses, case, the handles that cannot invite, the extra check the allowance really gives
back, the numbers page's invite block), `DigestTests` (the Sunday send and what it says, the guard, the weekday, an
account with nothing to say, the signed unsubscribe, a forged and a swapped token, a configured secret, the welcome
once, an unconfirmed address, no public origin, mail off) and `FunnelTests` (what the middleware counts and what it
leaves alone, the fourteen days, today's conversion, the moderator gate, a claimed guest check).


## Round 14 — the stylist: the occasion and the style are two questions, and a tip may be "change nothing"

*(One builder's section, appended for the lead to fold into the parts above.)*

**The category error, corrected.** `StyleIntent` was one list of eight — Casual, Date, Streetwear, OldMoney, Minimal,
Office, Party, Sport — and five of those are OCCASIONS while three are STYLES. A person who wanted streetwear for a
date, or minimal for a party, had no way to say so. A check now carries both:

- **The occasion** — `Everyday`, `Date`, `Office`, `Party`, `Formal`, `Sport`. Where the outfit is going. Chosen on
  every check; the score is always relative to it. `Formal` is new: a wedding or a ceremony is the one occasion with a
  real dress code, and the old list had no word for it.
- **The style** — `Streetwear`, `OldMoney`, `Minimal`, `Classic`, or **none**. How the wearer wants the look to read.
  A preference, kept on the device (`prefs.style`), shown on the check screen and changeable for one check without
  changing it. **None is a first-class answer**, not a blank: the stylist is told in words that no style was asked for
  and judges the look on its own terms, which is exactly what Date, Office, Party and Sport always did.
- **The one word** — `intent` on the check row and in every answer, derived from the pair (`StyleIntents.Legacy`): the
  style's own name for a style worn everyday, the OCCASION anywhere else. A streetwear look for a date reads *Date* on
  a card, a board, the share card and the share video, because that is what a stranger needs to know first. `Formal`
  has no word of its own in that list and lands on `Party`. Looks, boards, challenges, the feed filter, search and the
  interests list are untouched: they all still speak the one word.

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/checks` | multipart: `occasion`, `style?`, `note?`, `language`, `image`, `video?` | As before, plus `occasion` and `style` on the answer. `occasion` is one of the six names, case-insensitive (400 `error.occasion_invalid`); `style` is one of the four, or empty for none (400 `error.style_invalid`); `note` is the wearer's free line, ≤ 120 characters (400 `error.occasion_too_long`). **A client from before the split is still understood:** when the form carries `intent`, that one word is split into the pair and its `occasion` field is read as the free line, exactly as it was sent. The presence of `intent` is what tells the two shapes apart |

`GET /api/checks/{id}`, `GET /api/users/me/checks` and the answer to `POST /api/checks` carry `occasion` and `style`
(absent when no style was asked for) beside `intent`. **The wearer's free line is now `note`, not `occasion`** — the
chip took that name. The export's `checks[]` carries `note`, `occasion`, `style` and `tipKind`. `POST /api/compare` is
unchanged: the "which one?" screen still asks one question, and the comparer splits it on the way in.

**Storage.** `Checks.OccasionKind` and `Checks.Style` are new text columns (`Round14Check`), backfilled from `Intent`
row by row. The wearer's line stays in the column it has been in since the first migration — `Occasion`, now mapped to
`OutfitCheck.Note` — because a pilot database made before migrations is upgraded by matching the model's columns
against the file's, and renaming it would leave an orphan column or make 120 characters of someone's words have to
parse as an enum. Nothing is copied, nothing is renamed, and `Down()` leaves every row as it was found.

**"Change nothing" is now an answer.** `tip_kind` sits beside `one_tip` in the tool schema, both required — the promise
is still one tip, and a stylist structurally incapable of approval is one nobody believes twice. `change` is the tip as
it always was. `keep` means the look is already right for what was asked, and `one_tip` then names **what to keep and
why it works**, never an invented improvement. The rubric's test for a keep is explicit: nothing you could name would
meaningfully raise the score for this occasion and this style, and the weakest element is still fine — in practice an 8
or above with no weak item — and it says out loud that a keep is RARE, because a keep on a mediocre look is the same
dishonesty facing the other way. Anything the model sends that is not the word `keep` is read as a change, so a keep is
never shown by accident, and every check made before v5 reads as a change, which is what all of them were. The result
screen renders a keep with its own heading (*What to keep*), its own label (*Change nothing*) and the green accent of a
piece that works, and with no swap or replace verb anywhere near it.

**An anchored scale (rubric v5).** The model OREVOSH runs on (claude-sonnet-5) removed `temperature`, `top_p` and
`top_k` from the API and answers 400 to a request carrying one, so there is no knob to pin the scores with — **do not
add one**. Instead the rubric now anchors every scale band by band, one concrete sentence about garments each: the
overall score, fit, colour, accessories, and `intent_match`, which has two dimensions to match now and says what to do
when only one was asked for. The rubric also states the rule the split creates: the occasion and the style can
disagree ("this is a fine streetwear look and a weak one for a wedding"), **the occasion wins**, and a look that nails
the style while being wrong for the occasion scores 5 at most.

**Measuring it.** `tools/eval/stylist.js` points at a running OREVOSH with a real key, sends the same photo N times
(default 8) through `POST /api/checks`, and prints the score spread — min, max, mean, standard deviation — how often
the breakdown moved, how often the tip was a keep, and the tips side by side to be read. `--photo` repeats for a
per-photo table, `--json` gives the raw rows, and it exits non-zero when a spread is wider than `--max-spread`
(default 2). `tools/eval/README.md` explains the numbers to a non-developer and the arithmetic of what a pass costs
(photos × runs = model calls). **No real-model numbers have ever been taken**: the sandbox this was written in has no
route to Anthropic, so the harness was only exercised against a local stand-in made to move its scores on purpose.

**The check screen.** Two chip rows — `#occasions` (`.chip[data-occasion]`) and `#styles` (`.chip[data-style]`, with
`data-style=""` for *No style*) — then the preference line `#style-default` when the pick differs from what is saved,
then the free line (still `#occasion`, now labelled as a note). Every chip also carries `data-intent` with the one word
it contributes. Asking for a style before saying where lights `Everyday`, because a style with nowhere to go is exactly
what Streetwear, OldMoney and Minimal used to mean. On the result, `#asked-for` names both, and three empty containers
wait for the modules other rounds are adding: `#tip-feedback` (under the tip), `#tried-it` (under that) and
`#wardrobe-offer` (with the item list). Each renders nothing while its module is absent.

Tests: `IntentSplitTests` (the round trip, an older client, the refusals by name, the two shapes mapping onto each
other, a database written before the split, the keep from the model's word to the export, the anchors asserted on the
request, and rule 1 over every string the split added in all four languages), plus the Round 14 cases in
`OutfitAnalyzerTests` and `StylistV2Tests`.
## Round 14 — the loop that makes the tenth check better than the first

The thing a competitor cannot copy, because they do not hold this person's history. Three pieces: what happened with
the tip, said in words that mean something; a second photo of the same look after the change, judged on its own merits;
and a short profile of the clothes this person wears, which picks the *kind* of tip they get and never the number.

**Four answers, not two.** Round 13 asked *Did the tip land?* and took a yes or a no. "I tried it and it worked", "I
tried it and it did not", "that is not my style" and "I do not own that" are four different facts — success, failure,
taste, availability — and only the typed one can teach anything. Under the tip there is now a row of four 44px taps and
a Skip; a tap stores the answer at once, the optional one-line note follows, then thanks. `OutfitCheck.UsefulReason`
holds one of `worked | didnt_work | not_my_style | dont_own` beside the Round 13 columns; only `worked` is a `Useful`
yes, so the tip-landed rate on the numbers page keeps meaning exactly what it meant. *I do not own that* is the most
valuable of the four: it means the tip should have used something already in the wardrobe, and it is the one signal that
changes what the stylist is told next time.

**"I tried it", and the integrity rule.** One action on a result remembers that check in the phone's `sessionStorage`,
the person photographs the look again through the ordinary check flow, and the app shows the two results together:
both scores, both tips, what changed in the combination, and *which do you prefer?*. The rule that makes it worth
anything: **the second check is a real check.** It goes out through `POST /api/checks` with nothing of the first in it,
the stylist is never told that a photo is an attempt at its own tip, and the pair is written only afterwards, once both
verdicts exist. A score can never rise because somebody obeyed. `TriedTests` proves it on the recorded request: none of
the first check's id, headline, vibe or tip is in the second's prompt, its user message is byte-for-byte the message a
first-ever check makes, and a `beforeId` smuggled into the check form changes nothing, because the route has no door for
it.

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/checks/{id}/useful` | `{ useful?: bool, reason?: string, note?: string }` | `200 { id, useful, usefulAt, note, reason }`. As Round 13, plus `reason`: one of the four (400 `error.reason_invalid` otherwise), which decides `useful` on its own. One of `reason` and `useful` is required (400 `error.useful_invalid`). Logged as `Useful: check {CheckId} yes\|no {reason}`, never the note |
| `POST /api/checks/{id}/tried` | `{ beforeId }` | `201` `TriedPairDto`. Both checks must be the caller's own and `ok`. 404 `error.check_not_found` (either check, a missing `beforeId`, anyone else's), 400 `error.tried_same_check`, 400 `error.tried_order`, 400 `error.tried_not_scored`, 409 `error.tried_already` |
| `POST /api/checks/{id}/tried/prefer` | `{ prefer: "before" \| "after" }` | `200` the pair. 400 `error.prefer_invalid`; the check's 404 when the pair is not theirs. Changes no score, ever |
| `GET /api/users/me/tried` | — | `{ items: TriedPairDto[] }`, the caller's own pairs, newest first, at most 20 |

Every write above, and the two taste writes below, share the Round 13 `useful` rate-limit policy: 60 an hour per account
or address across all of them together (429 `error.too_fast`). The two reads are not limited.

`TriedPairDto` is `{ id, before, after, changed[], preferred, preferredAt, createdAt }`; each side is
`{ id, intent, createdAt, score, intentMatch, headline, oneTip, reason, postId }`, and `changed[]` is
`{ category, from, to }` per item category, computed here from the two stored verdicts — no model call, no opinion, and
nothing about anybody. A check is at most one pair's *before* and at most one pair's *after* (two unique indexes).

**The taste profile.** Built from the account's **own rows only**: which occasions it picks, the colours the stylist
named on its own checks, the pieces and categories on its own posted looks, and the reasons it gave. Never another
account's anything, never a hidden look, never a photo, and never a word about a body — every string is dropped when the
rule‑1 filter (`OutfitAnalyzer.MentionsPerson`, all four languages) sees one, so the profile is about clothes and only
clothes. It is a handful of short lines and a few counts, not an embedding.

It reaches the stylist as one clearly-marked section appended **after** the rubric, capped at 900 characters, which says
in its own words that it informs *which* tip is chosen and never the score:

```
WEARER'S TASTE (their own past checks, context only):
- Most often dressing for: Casual.
- Colours seen on their looks: white.
- Answered 'I do not own that' 1 time(s): prefer pieces from the list above.
- Tips they turned down: "Swap the running shoes for plain white leather sneakers.".
- Their own words: "no white sneakers here".
This section informs WHICH tip you choose, never the score. …
```

A tip's own text, and the note typed beside it, reach that section **only** for the two answers given without trying the
tip — *not my style* and *I do not own that* — because those are the ones that say "stop giving me this kind of tip".
What the person said about a tip they *tried* stays in its row, so an attempt can never ride in. Every quoted string is
folded to one line, its quotes turned to single ones, cut (80 characters for a note, 60 for a tip, 40 for a piece) and
labelled as context, never instructions. When the learning switch is off, or the profile is empty, or the caller is a
guest, **nothing is sent** and the request is byte-for-byte the one this app sent before Round 14.

**Nothing in it is a secret from its subject.** The card — *What OREVOSH has learned about your taste* — is on Settings
and at the top of `#/checks`, and it shows the facts **and the literal advisory text**, word for word. Next to it, a
switch that stops the learning and a button that clears it; both are honoured at once. Clearing draws a line at now:
nothing from before it is ever read again, so the profile is empty on the next breath and the app learns again from what
comes after. The checks and the looks themselves are the person's own history and are never touched.

| Method & path | Body | Returns |
|---|---|---|
| `GET /api/users/me/taste` | — | `TasteCardDto`: `{ learning, clearedAt, empty, facts, advisory, lastWin }` |
| `PATCH /api/users/me/taste` | `{ learning: bool }` | the card. 400 `error.taste_invalid` without the switch |
| `DELETE /api/users/me/taste` | — | the card, cleared |

**A reason to come back.** `lastWin` is the most recent check the person marked *worked*, within 30 days and with no
more than two of their checks since — rare enough to stay meaningful. The app shows it as one line: *Last time you
swapped the shoes — and you said it worked.* Only when true, only from their own rows.

Client: `app/taste.js` holds all of it. `views/profile.js` mounts the card, the reason row, "I tried it" and the pair on
`#/checks`; `views/settings.js` mounts the card with the switch and the clear. The result screen (`views/check.js`)
calls `mountResult(container, check)` once after the tip and this module fills whichever of `#taste-win`,
`#taste-reasons` and `#tried-action` it finds inside that container, appending its own nodes in that order when it finds
none. i18n under `taste.` and `tried.` in all four files.

Tests: `TasteTests` (each reason stored on its own and reaching the profile, an unknown reason refused and the Round 13
body still working, the profile as the person's own rows with a blocked account's and a hidden look's excluded, an empty
profile and a guest sending nothing, the request changed only in its own section, the switch and the clear both honoured,
a hostile note that cannot escape its quotes or blow the prompt, the last win's window, and the string, colour and cap
units) and `TriedTests` (no trace of the first in the second, the pair linked only after both verdicts, the score left
alone, the rules, the preference, the list, and what changed).
## Round 14 — Pro worth paying for, and a wardrobe that builds itself

Pro raised a cap from a few checks a day to thirty. Someone who dresses twice a day never touched either number, so Pro
sold nothing. Round 14 makes Pro about what the person GETS, and gives the app a memory of the clothes they own.

**The wardrobe builds itself.** Nobody photographs a closet: an hour of work before the first minute of value is how
these features die. Instead the stylist already names the pieces it can see on every check, and the result screen shows
one quiet line under the tip — *"Keep the White tee in your wardrobe?"* — with one tap and no form (`#wardrobe-keep`,
`wwwroot/app/wardrobe.js`, mounted by `views/check.js` right after the "did the tip land?" row). A keep offers the next
piece a moment later, so a wardrobe fills over a few checks. A piece can only be kept from a check that NAMED it, which
is what keeps the list a list of clothes somebody was photographed wearing rather than a free-text store.

**What it is for.** With those names in front of it, a tip can say *"swap the black tights for the brown ones you wore
on the 4th"* instead of *"buy sheer brown tights"*. That is the difference between advice and shopping, and it is what
makes the advice worth paying for. `Services/Wardrobe.cs` builds the list that travels: at most
`Plans:WardrobeNamesToStylist` names, most recently worn first, clothes only (a row the stylist itself could not place
stays home), each one cleaned and capped like any other stored string, and never one that names a body, a face, an age
or a gender — a renamed piece is free text the person typed, and rule 1 holds there too. `OutfitAnalyzer.WardrobeRule`
is the paragraph that goes with them; it says in its own words that the wardrobe informs the CHOICE of tip and is never
a reason for a higher or lower score. An empty wardrobe adds nothing to the request: the call is byte for byte the one
it always was.

**What Pro is now**, in the order the Pro page says it:

1. **"Which one?" whenever you are deciding.** A Pro account's comparisons have their own rolling-day allowance
   (`Plans:ProComparesPerDay`), counted apart from its checks, so deciding between two outfits never spends a check.
   A free account keeps the single allowance it always had.
2. **The taste profile**, where this server has it (`Plans:TasteProfile`, off until it does).
3. **Tips from your own wardrobe** (`Plans:WardrobeNeedsPro`): the list is everyone's, the advice from it is Pro's.
4. **Your insights**, where they are Pro's (`Plans:CompareNeedsPro`, as before).

The cap is named **once, last**, as one fair-use line naming both allowances. `PlansTests` reads
`wwwroot/app/views/pro.js` itself, pulls out every benefit it can draw and matches each against a table that says what
in the server makes it true: a benefit added to that page without a row there **fails the build**, and so does a cap
sold as a benefit or a promise left in the copy after the code stopped drawing it.

| Method & path | Body | Returns |
|---|---|---|
| `GET /api/wardrobe` 🔒 | — | `{ items: [{ id, name, category, keptAt, lastSeenAt, looks: [{ checkId, wornAt, postId? }] }], max, toStylist, stylistAvailable }`. The account's pieces, most recently worn first, each with the looks it appeared in (newest first; `postId` only where that check is a visible look of the caller's). `max` is `Plans:WardrobeMaxItems`, `toStylist` is this account's own switch and `stylistAvailable` is whether its plan honours it. A free account sees its whole wardrobe |
| `POST /api/wardrobe` 🔒 | `{ checkId, name }` | `201` the piece, or `200` when it was already kept and this check was added to it (one row per piece per account, matched case-insensitively). `name` must be one the check actually named — the stylist's items, then the accessories it saw. 400 `wardrobe_name_invalid` for an empty name, 400 `wardrobe_unknown_piece` for anything else, 404 `check_not_found` for another account's check or a guest's, 409 `wardrobe_full` at `Plans:WardrobeMaxItems` (never for a piece already kept) |
| `PATCH /api/wardrobe/{id}` 🔒 | `{ name }` | `200` the piece, renamed, keeping its looks. 400 `wardrobe_name_invalid`, 404 `wardrobe_not_found` (which is also what another account's piece answers), 409 `wardrobe_full` when the new name is already another of this account's pieces |
| `DELETE /api/wardrobe/{id}` 🔒 | — | `204`. Its appearances go with it; the checks do not. Anything that is not a piece of the caller's answers `204` too, existing or not: a delete that said "not yours" for a real id and "gone" for a fake one would tell a stranger what other accounts keep |
| `POST /api/wardrobe/stylist` 🔒 | `{ on }` | `200` the wardrobe. Whether this account's piece names travel with its checks; on by default. 403 `error.pro_required` to a free account while `Plans:WardrobeNeedsPro` is on |

The wardrobe is the account's alone: `SecurityTests` declares the rule for both `{id}` routes. It travels in the data
export (`wardrobe`: the name, the category, when it was kept, when it was last worn and the checks it appeared in) and
it goes with the account on delete, appearances and setting and all. Migration `Round14Wardrobe` adds `WardrobeItems`
(unique on owner + lower-cased name), `WardrobeAppearances` (item + check) and `WardrobeSettings` (one row per account).
The legal pages moved to version 3: the wardrobe is named in "what we store", "what we send to the model provider",
"who sees what" and "deleting", and "Free and Pro" describes Pro by what it gives.

Tests: `WardrobeTests` (kept only from a check that named it, one row per piece with its looks, the post behind a
published look, another account's check and another account's piece, rename and delete, the fair-use cap, a free
account's list with the Pro-only switch refused in four languages, the switch on and off, the names in the prompt only
when the plan, the switch and the setting all say so, a renamed piece that names a person never travelling, the
handful and the order, `0` turning it off, the export and the account delete, and the cleaning of a name) and
`PlansTests` (the promise table over the Pro page's own source, the cap once and last, the published allowances,
the two buckets, and Pro's comparison never coming out of the day's checks).
## Round 14 — a community that says more than "fire"

Four things, in the order they matter. Everything else on this page still holds; these add to it.

**Post the look, keep the grade.** Posting used to put the number into public with the photo, and there was no way to
have one without the other. Now the author chooses — in the post sheet, and afterwards from their own look — and the
choice is one column, `Posts.ScorePrivate`. A private grade hides the score, the three sub-scores and the intent match
from everyone but the author and a moderator; the look, the caption, the pieces, the fires and the comments work
exactly as before, and the card shows no number rather than a blank where one was.

The question is asked in one place, `Services/PostReader.cs`, which every route that returns a look already goes
through, so the answer cannot drift between the feed, a profile, a tag page, Explore, the searches, the saved list, a
challenge board, the weekly board, the look itself, or the public `/look/{id}` page and its unfurl (`Endpoints/
PublicPageEndpoints.cs` answers the same way, ring and all). `PostDto.Score`, `IntentMatch` and `Breakdown` are simply
absent when the reader may not read them, and `BeforeDto.Score` follows the earlier look's own choice.

The tallies and the boards keep the rule blocks set in Round 11: **hide the row, never move the number**. A look whose
grade is private keeps its fires and its place on the looks, people, rising and intent boards, which rank by fires; only
its card's number goes. The stylist's picks board is the exception, and it is one on purpose: it ranks *by* the number,
so a row on it says, through the rows above and below, roughly what the card refuses to say. A private look is
therefore not on the picks board at all — live, from the archive, or in the hall of winners — the same way a look a
moderator excluded is not, and nothing else moves but the ranks closing over the gap. Showing the grade again puts it
back: the archive row was hidden, never rewritten.

**The comment box has a direction.** The flame stays as it is: it is appreciation and it is the brand. Under the box
there are now three openers a tap fills in — "the piece doing the most work here is…", "I would try swapping…",
"where is the … from?" — in all four languages. They are starting points a person edits, they post nothing by
themselves, and they are quiet enough to ignore. One tally counts comments begun from any of them; which one was
tapped is not counted and nothing about it is stored on the comment.

**Before and after, one change.** A look that names an earlier one ("after the tip") can be shared as a pair: a story
card and a ten-second film, both drawn on the phone like the ones that were already there, showing the two looks, what
changed (the person's own words — never the tip) and, if the person wants them, the two verdicts. The version with no
numbers at all is always offered and is the one a private grade opens on. Two tallies say which version people use.

**Challenges that state a rule.** A challenge may now carry one sentence of constraint ("the same piece in three
looks", "two colours only", "something you have not worn in a month"), shown plainly on its card and its page. Entry is
the same hashtag as ever and **nothing enforces the rule**: a person's word is enough and the community sees the looks.

| Route | What it is |
|---|---|
| `PATCH /api/posts/{id}/score-privacy` | `{ scorePrivate }` → the look's grade goes private or public. The author's own look only (404 otherwise) |
| `POST /api/posts/{id}/shared-after` | `{ withScores }` → one tally for a before/after card or film that was shared or saved. Author only |
| `POST /api/posts` | Also takes `scorePrivate` (default false, which is what posting always did) |
| `POST /api/posts/{id}/comments` | Also takes `opener` (`piece` \| `swap` \| `where`), counted once for all three and stored nowhere |
| `POST /api/challenges` | Also takes `constraint`, ≤ 140 characters, null for the open hashtag challenge |

The numbers page gains five: looks with a private grade, comments begun from an opener, before/after shares with the
scores and without them, and challenges that state a rule.

Tests: `ScorePrivacyTests` (the sweep over every route that returns a look, the author and the moderator, the posting
choice, the stranger's 404, the before/after tally, the openers, the four locale files), `ScorePrivacyBoardTests` (the
fires boards keep the place, the picks board does not carry it, live and archived and in the hall) and
`ChallengeTests.A_constraint_challenge_is_opened_entered_and_ended_like_any_other`.

## Round 15 — the wardrobe is counted, and it reaches the comparison

*(One builder's section, appended for the lead to fold into the parts above.)*

**The wardrobe had no metric.** Round 14 built a wardrobe that fills itself from the pieces a person keeps, and
nothing counted it; `MARKETING.md` named the two numbers to watch and told the owner to count `WardrobeItems` by
owner by hand "until a metric exists". They are on `/api/metrics/pilot` now, in a block of their own.

| Field | What it is |
|---|---|
| `wardrobe.items` | Kept pieces across the pilot, all accounts |
| `wardrobe.keepers` | Accounts with at least one kept piece |
| `wardrobe.checkedUsers` | Accounts with at least one OK check — **the same number as `usersWithAtLeastOneCheck`**, passed in rather than counted again, so the page cannot say two different things about who has checked |
| `wardrobe.keepRate` | `keepers ÷ checkedUsers`, four decimals. `MARKETING.md`'s "wardrobe kept": 40% by week 4, and below 15% the keep line is in the wrong place or says the wrong thing |
| `wardrobe.dontOwn` / `wardrobe.reasons` | Checks answered `dont_own`, and checks answered with any typed reason |
| `wardrobe.dontOwnRate` | `dontOwn ÷ reasons`. Watched **falling**: a tip that draws "I do not own that" is exactly the tip a wardrobe should have prevented, so its fall is the wardrobe's worth measured |
| `wardrobe.toStylistOff` | Accounts that turned the sending off. Without it a flat `dontOwnRate` has two different explanations — the tips are not using the wardrobe, or the wardrobe is not reaching the stylist |

**A rate with nothing to divide by is absent from the JSON, not `0`.** Both rates are nullable and `AppJson` drops a
null, so an empty pilot has no `keepRate` at all. Nought per cent is a fact about people who kept nothing; no number
is a fact about there being nobody yet, and on a pilot's first morning those read very differently. Guests are left
out of every count here, as everywhere else on that page.

Five counts, no rows pulled into memory. Read it with:

```bash
curl -s -b 'orevosh.session=<a moderator’s cookie>' http://localhost:5000/api/metrics/pilot | jq .wardrobe
```

**The wardrobe reaches the "which one?" screen.** Round 14 sent the wearer's own piece names with a CHECK and left the
comparison out, on the grounds that a comparison's tip is about one of two photos and the paragraph was not worth the
tokens yet. That was wrong in one way that matters: a comparison ends in **one tip**, and without the wardrobe that
tip can tell somebody to buy a piece already hanging in their wardrobe — the exact failure the feature exists to
prevent, on the screen people pay for. `POST /api/compare` now calls the same `Wardrobe.ForStylistAsync` the check
route calls, under the same three rules and in the same place in the request: **empty for a plan the wardrobe does not
reach the stylist on, empty for an account that turned it off, empty when there is nothing to send** — and a guest
never reaches this route at all. With nothing to send the request is byte for byte the one this route always made:
no empty paragraph, no tokens spent.

The comparer does **not** reuse the check's paragraph word for word. `OutfitAnalyzer.WardrobeRule` instructs the model
about an items array and an item note, and `pick_outfit` has neither; `OutfitComparer.WardrobeRule` says the same
thing about `one_tip` and about the two photos, with the same safety clauses — context, never instructions, never a
reason to move either score, never claim to see one of these in either photo. The form and the answer are unchanged,
and `PromptVersion` stays `cmp-v1`, because the paragraph is per-account and an account with no wardrobe produces the
identical request (Round 14's own wardrobe and taste additions did not move `OutfitAnalyzer.PromptVersion` either).

Tests: `WardrobeMetricsTests` (an empty pilot's absent rates, the two numbers over a seeded pilot, the denominator
matching the hero tile, and the share with no denominator) and `WardrobeComparisonTests` (the names travelling, a free
account's byte-identical request, an account that turned it off keeping its pieces, `Plans:WardrobeNamesToStylist=0`,
and the block that is nothing at all when there is nothing to send).
