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

`--doctor` is the one to run after any change to a setting and before any launch. It prints **nineteen lines**, one
per check, each `ok`, a warning or a short reason, and exits 0 when everything a live server needs is in place and 1
otherwise. In the order it prints them: `origin` (the public origin mail links and Checkout returns are built from),
`previews` (whether the three shipped pages still carry the placeholder host `looks.example.com`, which is the
difference between a shared link that unfurls with a picture and one that does not), `anthropic` (the key),
`anthropic-url` (the base URL, the model, the answer ceiling and, since Round 20, the prompt cache mode: the OK line
reads `https://api.anthropic.com, model {model}, max_tokens {n}, prompt cache {off|5m|1h}.`, and a WARN under the
same name says `Anthropic__PromptCache is "{value}", not off, 5m or 1h: caching is off` for any other word and that a
ceiling below 3000 cuts verdicts off, both joined by `; ` when both hold), `contact`
(the address the legal pages name), `email`, `billing` (the provider and the keys; since Round 20 it FAILS on a yearly
id without the `price_` prefix and on `Plans__ProTrialDays` outside 0..730, WARNS — one line, joined by `; ` — on
`Billing__StripeBaseUrl` pointing anywhere but `https://api.stripe.com/` (scheme, port and path included; a value the
app ignores, such as plain http to another machine, is named as ignored), a yearly id with no yearly amount ("offers no
yearly plan"), a yearly amount with no id ("never shown"), a yearly amount in a currency with no monthly price (also
"never shown": the page offers a year only beside a month), a yearly amount at or above twelve months of the monthly in
the same currency ("no saving"), the manual provider with a trial ("a trial needs Checkout") and a test key over https,
and reads `stripe (sk_live_…), price <id>, yearly <id|none>, trial <n> days|off, webhook secret set.` when all is
well), `plans` (the caps against the ceiling, ending `morning push 07:30 Asia/Jerusalem` or `morning push off`, and
warning when `Plans__TomorrowMorningPush` is on without VAPID keys or with `Plans__TomorrowEnabled` off, and when
`Plans__TomorrowMorningHour` is not `HH:mm`, flag on or off), `push` (the VAPID keys), `admin` (the moderator list, and the accounts `--admin`
promoted), `board` (the time zone), `affiliate`, `storage` (the photo folder, actually written to and the file removed
again), `database` (the file and what it still has to migrate), `ffmpeg`, `disk` (the free space where the data lives),
`spend` (what a model call is priced at here and the day's ceiling), `weather` (the forecast behind Tomorrow, and
whether its keyless licence is being leaned on by a server that takes payments) and `alerts` (whether anything at all
would shout). The last two lines are the tally —
`doctor: 9 ok, 7 warnings, 1 failure`, with whatever numbers your own run comes to — and the verdict, `Ready.`, `Ready, with warnings to read.` or `Not ready: fix
the failures above and run it again.` **Only a failure changes the exit code**; a warning is the operator's call.
`--doctor --live` adds the calls that cost something or leave the machine:
one small Anthropic call with the configured key and model (a fraction of a cent), when the provider is `stripe` two
reads from Stripe — three with a yearly price set, all against `Billing:StripeBaseUrl` — one forecast from Open-Meteo (`weather-live`, a warning and never a failure) and, when an alert
channel is set, one test alert (`alerts-live`). **It never dials the mail server**: `--doctor` reads the `Email__*` settings and says whether
they could work, and nothing in this program opens an SMTP connection or logs in. The only thing that tests the sender
is sending: ask for a password reset from the app (or sign up) with your own address and watch the mail arrive, and
read the log line if it does not. `--stripe-check` is the Stripe half on its own, five lines — `billing`, `stripe-live` (the
monthly price), `stripe-price` (its table against `Plans:ProPrices`), `stripe-yearly` (the yearly price read and its
table in one line; a skip, `not called: Billing__StripeYearlyPriceId is empty (monthly only).`, until one is set) and
`stripe-webhook` — and it writes nothing and charges nobody. `/readyz` answers the machine-side half of `--doctor` over HTTP,
for a deploy or an uptime checker to wait on.

The account commands exit with code 1 when no account has the handle (sign up first, then run it again) and 2 on a
usage error; a leading `@` on the handle is fine. `--verify` and `--pro` stay as the terminal fallback for the verified
flag and the plan; since Round 20 the same actions are buttons on `#/admin` (the Accounts section), with one difference:
the screen refuses a Pro grant or removal on an account that pays through Stripe (its plan is changed in Stripe), the
command does not. The flag and the plan are still written by hand only — by the command, or by a moderator on that
screen, never by a request from the person themselves — and with `Billing:Provider` left at `manual` those two doors
are the whole upgrade path. On a server the same commands run inside the container, `docker compose exec app dotnet
FitCheck.Api.dll --admin yourhandle` (`DEPLOY.md`, step 7). Round 10 added no command: the weekly board closes itself
in the background (`Services/BoardCloser.cs`, the log says when), a moderator pulls a look off it through the API, and
affiliate programmes are settings (`Affiliate:Hosts`). Round 11 added the three above, for going live and staying
live. Blocking, the billing portal and the data export add none: they are things a person does in the app. Round 20
adds none either: the account actions moved onto `#/admin` as buttons and the commands stay.

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
happened at most 7 days after their first ÷ users with at least one OK check; guest checks are left out). Since Round
20 it also carries `p95LatencyMs`: the nearest-rank 95th percentile over the same all-time OK checks by accounts that
`avgLatencyMs` averages (0 when empty), so the two tiles can never disagree about the population. The
`social` block counts users, brands, posts, fires, follows, comments, open and ended challenges, votes, mentions,
featured looks, clips, push subscriptions, people active in the last 7 days, and, since Round 10, `itemsTagged`
(item rows a person touched: typed by them, or carrying a brand or a store link; the stylist's bare names are not
tagging), `itemOuts` (store-link taps that left through `/api/items/{id}/out`) and `boardViews` (answered reads of
`/api/board`), the last two read from the `Counters` table the routes increment. Round 14 appended five more to the
same block: `privateScores`, `commentOpeners`, `beforeAfterShares`, `beforeAfterSharesPlain` and
`constraintChallenges`.

Six blocks sit beside it, each with its own DTO and nothing shared with the tiles above:

| Block | What it answers |
|---|---|
| `stylist` | **Did the tip land?** Yes / no / unanswered over every OK check by an account, overall, by intent and by language, plus how many photos the stylist called "no outfit" and how many it refused. The only number that says whether the stylist is any good. **Round 20** adds `triedPairs` (pairs linked, the `CheckLinks` count), `triedPer100Ok` (pairs × 100 ÷ OK checks by accounts, two decimals, absent while there is no OK check), `tryTipNudges` (notifications of type `try_tip`) and `nudgedThenTried` (pairs whose before check carries one) |
| `spend` | **What the model cost today**, at the owner's configured prices, the day's ceiling, whether the app is resting on it, and fourteen days of it. Always an estimate, never an invoice. **Round 20:** `promptCache` is the effective mode (`off`, `5m` or `1h`); `today.cacheReadTokens` / `cacheWriteTokens` are drawn as two tiles ("Cache reads (tokens)", "Cache writes (tokens)") with the prices hint ending "Prompt cache: off / 5 minutes / 1 hour."; the estimate prices cache reads at 0.1× and cache writes at 1.25× (off or `5m`) or 2× (`1h`) of the input price, and writes are re-priced by the mode in force when the page is read, so flip the mode at midnight UTC or accept a few cents of drift |
| `funnel` | **The growth loop**: fourteen days of landing views, guest checks, signups, first posts and public-page arrivals, today's conversion between those steps, and the invites — sent, accepted, and who is inviting. **Round 20:** each `days[]` row gains `proFromCompare` and `proFromWardrobe` (the Pro page opened from a refused compare or from the wardrobe line) and `standalone` (launches from the home screen), and `sources` lists `{ source, arrivals, guestChecks, signups, firstPosts }` per allowlisted source in `Funnel:Sources` order over the fourteen-day window, zero rows included: arrivals off `funnel:src:<source>:<day>`, guest checks off `Checks.Source` (guest or claimed, not `error`), signups off `Users.Source`, first posts off each poster's `Users.Source`. An unclaimed guest check is swept a day after it was made, so the sweep leaves `funnel:guest:swept:<day>` and `funnel:guest:swept:<source>:<day>` behind (by the day it was made, in the delete's transaction) and both the day's `guestChecks` and the source's add them to the rows still there |
| `wardrobe` | **Round 15.** The two numbers `MARKETING.md` watches. `keepRate` is `keepers` (accounts with at least one kept piece) ÷ `checkedUsers` (accounts with at least one OK check — the same number the hero tile reads, so the page cannot say two different things about who has checked), with `items` as the raw row count behind it; `dontOwnRate` is `dontOwn` ÷ `reasons`, the "I do not own that" answer over every typed answer to the tip, and it is watched **falling**, because a tip that draws it is exactly the tip a wardrobe should have prevented. `toStylistOff` counts the accounts that turned the sending off, which is what keeps a flat `dontOwnRate` readable: a wardrobe nobody sends cannot prevent anything. **A rate with nothing to divide by is absent from the JSON, not `0`.** **Round 20** adds `keepAll` (keep-all requests that wrote at least one row), `momentShown` and `momentGo` (the Pro moment, counted only while it was true for that account), `momentGoRate` (go ÷ shown, absent while nothing was shown) and `piecesPerActiveMedian` (the median of kept pieces over the seven-day active set — checks, fires, comments, votes — zeros included, even counts averaged to two decimals; absent while nobody is active) |
| `breakdownAverages` | The mean of each rubric sub-score over the checks that carry one; absent while no check does |
| `tomorrow` | **Round 19.** Planned outfits, the worn and reuse rates, refs the model returned that were not in its list, sentences the template replaced, the reasons. **Round 20** adds `pushesSent` (morning push receipts with `SentAt` in the last 30 days), `pushesOpened` (of those, with `OpenedAt` set) and `openRate` (opened ÷ sent, four decimals, absent while nothing was sent). **Review fixes:** `planners`, the distinct accounts behind those outfits over the same 30 days ("People planning"), the number the morning push's go/no-go rule in `LAUNCH.md` reads beside the worn rate |

The route answers only through a moderator's session (below), and moderators see the same numbers drawn as a page at
`#/admin/metrics` (one hero figure, the return rate; tiles; the score distribution as bars; the stylist, money and
funnel sections), linked from the moderation page. Round 20 adds a "Latency (p95)" tile beside the average, the two
cache tiles among the money tiles (six), three wardrobe tiles ("Keep-all taps", "Pro moment → Go Pro" with "{shown}
shown, {go} tapped" under it, "Pieces per active person (median)", an en dash while null; eight in the block), two
funnel columns and the per-source table ("Where people come from"), and "Morning pushes" and "Opened" in the Tomorrow
block, whose hint ends "Read the three together: pushed, opened, worn." (eight tiles since the review fixes added
"People planning", the people behind the outfits).

### Run the tests

```bash
dotnet test        # from the repository root (FitCheck.sln)
```

1168 tests, and the number is meant to be read off the run, not trusted from here: magic-byte detection for photos
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
wardrobe's numbers (`WardrobeMetricsTests`) and the wardrobe reaching a comparison (`WardrobeComparisonTests`). Round
20 added the webhook replay, the yearly Checkout and the no-card trial to `BillingTests` (every event factory now stamps
a fresh `evt_` id, and a test that means a replay passes the same one), `RenewalRecapTests`, `StylistBackTests`,
`TryTipNudgeTests`, `TomorrowMorningTests`, `DistributionTests` (the `/go` routes, the crawler skip, the invite
passthrough, the stamped source, the launch header, the attributed walk, a swept guest check still counted,
`Funnel:Sources` as a setting, the redirect's own security headers), the two compare questions and the close call in `CompareTests` and `OutfitComparerTests`,
keep-all, the unkept list and the Pro moment in `WardrobeTests` with the wedge numbers in `MetricsTests`, the account
actions, the audit lines and the sponsor reader in `AdminTests` (with `BoardTests` for the excluded account and
`RateLimitTests` for the per-moderator brake), the prompt-cache breakpoints in `AnthropicVisionClientTests` and
`SpendMeterTests`, the p95 in `MetricsComputeTests`, `Round20SkeletonTests` for the seams the moves share, and the
renderer's `tools/brand/test/before-after.test.js` under `node --test`. Round 21 added the verdict on a posted look's
pieces to `ItemsTests` (a piece the stylist named carries its word on the new post, on the look as its author reads it
and on the answer to a later tagging, matched by name in any case or spacing; a name the stylist never gave carries
none; and, settled in the review, a guest and another account read no verdict on the look, the feed or the profile,
public number or private, while a moderator reads them in the queue) and the policy with no font host to `SecurityHeaderSetTests` in `SecurityTests` (`style-src 'self'
'unsafe-inline';` and `font-src 'self';`, with neither `googleapis` nor `gstatic` anywhere in it).
Two of them are policy rather than behaviour and are the reason a careless change fails the build:
`IdorEnumerationTests.Rules` in `SecurityTests` makes every route with an `{id}` or a `{handle}` declare, in writing,
what stops a stranger enumerating it, and `LanguagesTests` keeps the four locale files at key parity.

There is also a browser test in [`tools/e2e`](tools/e2e/README.md): Playwright drives the real client in a
phone viewport against the real API with the Anthropic API stubbed (the same stub stands in for the forecast, and for
Stripe in the money step), around three people (a person in English, a brand, and a person browsing in Hebrew, with more joining in
later rounds' steps), from a guest's check and signup with a birth date and the welcome
screen through posting with tags and mentions, featuring, Explore, a challenge and its winner, the in-app camera
with a fake device (a photo, then a clip with its frame picked), the story card, a comparison, the insights, a
search by piece, the Pro page, Today's look and a follow-up look, verified brands, the moderation queue and a
suspension, the numbers page, the guidelines, the terms and the privacy policy, the health and config routes, to
deleting an account. **Round 10 is in the script**: the item editor on the post sheet (the stylist's rows, the Nike guess confirmed, a dot placed),
the look page's items, dots and item sheet, the item pages and the brands list, the board with its first-place medal and
tabs, the empty hall and the Explore strip. The run starts the API with `Board__NewAccountDays=0`, `Board__MinChecksToCount=1`
and `Board__CacheSeconds=0` because its accounts are minutes old and its fires must show at once. Not driven: a closed
week (the closer has no HTTP trigger), so the hall's weeks, the badge and the `board_rank` line are covered by
`BoardTests` only; the moderator's exclusion is covered by `BoardTests` and `Round10SkeletonTests`. **Round 20 is in the
script too**: the run sets `Anthropic__PromptCache=5m` (every check and comparison is asserted to carry the breakpoint
and no compose), `Plans__WardrobeNamesToStylist=2` and a sponsor in the server environment; the wedge step tries the tip
from the result screen, links the pair, shares it and reads the public pair; the compare step asks the two questions
with slot A from the library and slot B through the in-app camera and meets the Pro nudge; a stubbed 4.5-second answer
shows the staged wait; keep-all and the Pro moment run on a free Hebrew account; the morning step lands on
`#/tomorrow?from=push` twice, opens on Today without saving it over the pill pressed last and composes nothing (and the
next ordinary visit opens on that saved pill), then finds the Settings switch drawn, locked and captioned "Turn on
notifications above first." (this browser has no push subscription, so the switch is never flipped here; its route is
covered by `TomorrowMorningTests`), while a second window of Noa's with a stand-in `push.js` turns notifications on and
off and watches the morning switch unlock and lock in place; a fourth person, Maya, on a Hebrew phone, follows `/go/tt` and the
per-source table attributes her guest check and signup to TikTok (after a refused first try, and a reload does not bring
the spent source back), and then, signed in, keeps no entry-link word, takes the Hebrew offer and reads the Today prompt
in Hebrew, crosses the free wardrobe slice with questions still to come and meets the Pro moment on `#/wardrobe` rather
than under that keep, and signs out of a Home that showed her own verdict dots onto one with none; an Instagram webview gets the one-time note instead
of any install advice, and the installed app's first answered call of the day counts one launch (the first, with no
signal, spends nothing), and a launch whose first call never answers is drawn anyway once the boot's twelve seconds are up; step 11b grants Pro and takes it back, verifies and unverifies, keeps off the
board and puts back from `#/admin`, and watches the sponsor card's warning clear; a second API on the next port with `Limits__SpendPerDayUsd=0.001`
plays the guest at the ceiling; the money step restarts the API with Stripe on against `tools/e2e/stub_anthropic.py`,
which answers Stripe's session POSTs, `/v1/prices/{id}` and `/v1/webhook_endpoints`, where a fifth person buys the year on a
seven-day no-card trial and the signed webhook, posted twice, is ignored the second time by its id; and the before/after
converter (`tools/brand/lib/before-after.js`) reads the run's own JSON (the two checks, the pair and the export) into the
scores, the tip and the change line the episode would show; the episode itself is laid out only by CI's cover render of
the sample (`006-before-after-sample.json`). Not driven: a real push (service
workers are blocked, so the senders are covered by their unit tests), the service worker's `/go/` passthrough (a pin in
`DistributionTests`), and the full Checkout → webhook → `#/compare?ready=1` chain (the landing is exercised directly).
**Round 21 changed no query in the script**: every id and class it reads survived the redesign. Its filter of failed
requests no longer excuses the two Google font hosts (nothing may ask them for anything now), and lets a face under
`/fonts/` be cut off when a page moves on, as a photo may.

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
`tools/eval/README.md` explains the numbers and what a pass costs (photos × runs = model calls). From a Windows
machine against the live server, `tools\eval\calibrate.ps1 -Photos <folder> -Occasion date -Language he` runs the
same pass (Windows PowerShell 5.1, no `pwsh` needed), echoes every line, files a dated report under
`tools/eval/reports/` (ignored by git: it carries the handle and the tips) and ends with one Hebrew verdict line,
green or red; `-DryRun` prints the command without signing in. A pass longer than a Pro day (photos × runs above 30,
so more than three photos at 8 runs) stops before signing in unless `-Force` says the server's caps were raised. **No real-model numbers have been taken in this
repository**: the sandbox this was written in has no route to Anthropic, so the harness has only ever run against a
local stand-in built to move its scores on purpose; the wrapper is how the first real pass is run.

## Configuration

`src/FitCheck.Api/appsettings.json`:

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=orevosh.db` | SQLite file, switched to WAL mode on start. Migrations run on start (`Data/DatabaseSetup.cs`); a pre-migration pilot file is backed up and upgraded in place. A relative path resolves against the project folder |
| `Anthropic:Model` | `claude-sonnet-5` | Must support forced tool use: Sonnet 5, Opus 5, the 4.x family, Haiku 4.5 |
| `Anthropic:MaxTokens` | `3000` | The ceiling on ONE answer, not a charge — the bill is the tokens the model actually writes, so headroom is free and a ceiling that is too low is not. A typical verdict is 300–700 output tokens; the ceiling is far above that on purpose, because a cut lands the early fields (status, score, headline, vibe) and silently drops the whole verdict after them. An answer that hits it is refused with the reason named, never stored as half a screen |
| `Anthropic:BaseUrl` | `https://api.anthropic.com` | Override to point at a stub in tests |
| `Anthropic:PromptCache` | `off` | **Round 20.** `off`, `5m` or `1h`. On, the shared rubric of a check or a comparison (the tool schema and the system prompt, identical for every call in a language) is written to the provider's prompt cache once and read back at a tenth of the input price for five minutes or an hour after the last read; a write costs 1.25× (`5m`) or 2× (`1h`) of the input price. Tomorrow and the recap never carry a breakpoint (their prompts are not shared). Any other value counts as off and the doctor warns. The rubric-plus-schema prefix must clear the model's minimum cacheable length or the API silently writes nothing, so the two cache tiles on the numbers page are the check; turning it on lowers the day's estimate, so re-read `Limits:SpendPerDayUsd` afterwards |
| `Storage:Root` | `storage` | Private photo folder (checks and avatars). Relative paths resolve against the content root, never `wwwroot` |
| `Storage:MaxImageBytes` | `6291456` | Upload limit for the still of a check (6 MB). Avatars are capped at 2 MB. The client downscales first |
| `Storage:MaxVideoBytes` | `41943040` | Upload limit for a look clip (40 MB) |
| `Storage:MaxVideoSeconds` | `30` | Advisory clip length: the camera stops there and the picker refuses longer library clips. Returned by `/api/config`. The transcoder cuts a longer clip there |
| `Storage:Transcode` | `true` | Re-encode clips to H.264 MP4 in the background with ffmpeg ("Clips" below). Nothing runs when ffmpeg is not found |
| `Storage:FfmpegPath` | empty | The ffmpeg binary, with ffprobe next to it. Empty means `ffmpeg` on `PATH` |
| `Push:PublicKey` / `Push:PrivateKey` | empty | VAPID keys for Web Push, generated once with `dotnet run -- --vapid`. Environment only (`Push__PublicKey`, `Push__PrivateKey`), never in appsettings. Push is off until both are set; a new pair drops every existing subscription (the push service answers 401/403 and the app deletes it) |
| `Push:Subject` | `mailto:hello@orevosh.app` | Contact the push services see |
| `Push:TryTipNudge` | `true` | **Round 20.** Whether the day-after nudge runs at all: one push per check, one per person a day, to push-subscribed accounts whose change tip nobody answered on a check that is in no pair, an "I tried it" link or a posted look marked as the after of another (`TryTipNudgeService`, hourly) |
| `Push:TryTipAfterHours` / `TryTipWindowHours` | `24` / `24` | Hours after the verdict before a check may be nudged, and how long after that it still may be; older is never nudged, so a server that was down for a week does not nudge last week |
| `Push:TryTipDayStart` / `TryTipDayEnd` | `9` / `21` | The local hours in `Board:TimeZone` between which nudges go out (`[start, end)`); a check due in the night waits for the morning's run. The stylist-back note keeps the same hours: the ceiling reopens at UTC midnight, and those notes wait for the morning too. The quiet hours are the server's, not the person's |
| `Email:Host` / `Port` / `User` / `Password` / `From` / `UseStartTls` | empty | SMTP for confirmation and reset links (`Email__Host` etc.; the password is environment only). Mail is on when Host and From are set; `Email__Host=log` writes the links to the log instead of sending |
| `Legal:ContactEmail` | empty | The address the terms of use and the privacy policy tell a reader to write to — a privacy question, deleting their data, reporting an account belonging to someone under 16. Empty falls back to the address in `Email:From`; with neither, those pages leave the contact section out rather than name nobody, and `--doctor` says so. Public, not a secret |
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
| `Plans:ProYearlyPriceAmount` | `0` | **Round 20.** The yearly Pro price in `Plans:ProPriceCurrency`; `0` is none. A year is sold only when this (or an entry in the table below) AND `Billing:StripeYearlyPriceId` are both set; the Pro page computes "save N%" from this and the monthly number and offers the year only when that is above 0 (a year at or above twelve months is not offered) |
| `Plans:ProYearlyPrices` | `{}` | The yearly price per currency (ISO code to amount, `Plans__ProYearlyPrices__USD=79.99`), built into a table exactly like `Plans:ProPrices`: prices, never conversions, and each one a currency the yearly Stripe price carries (`--stripe-check` says) |
| `Plans:ProTrialDays` | `0` | Days of Pro before the first charge, with no card asked for (Stripe Checkout's trial; 1..730 is Stripe's range and the doctor fails outside it). Offered once per account, to one with no `BillingCustomerId`, so a deleted-and-recreated account can trial again and a `--pro` grant does not disqualify; a trial that ends with no card simply ends. A trial is Checkout's and means nothing while `Billing:Provider` is `manual`, which the doctor warns about |
| `Plans:WardrobeUnkeptChecks` | `20` | **Round 20.** How many of the person's latest OK checks the wardrobe screen looks back through for pieces the stylist named that were never kept ("Keep from an older look"). `0` hides the section. Not published on `/api/config` |
| `Plans:TomorrowMorningPush` | `false` | **Round 20.** The server flag for the morning push. Off: nothing is sent, `/api/config` publishes `plans.tomorrowMorningPush=false`, Settings draws no switch and the dashboard's two tiles read zero. On: once a day at the hour below, one push to each account with its own switch on, a push subscription, a wardrobe of two kinds and something left to spend — nothing is composed until the tap. The founder flips it once the worn rate says planned outfits get worn |
| `Plans:TomorrowMorningHour` | `07:30` | `HH:mm`, local to `Board:TimeZone`, the same clock for everybody. Anything not `HH:mm` falls back to 07:30 and the doctor warns. Constants beside it, not settings: the send window is three hours (a push after 10:30 local is skipped for the day), the sender ticks every fifteen minutes with a first pass at start, a receipt may be opened for 24 hours, and the push service holds the push only for what is left of the window (its time to live), so a phone that comes back online after the morning is never told about "today's" outfit |
| `Billing:Provider` | `manual` | `manual`: Pro is granted with `--pro`, and the Pro page shows a note instead of a checkout button. `stripe`: Checkout and the webhook are live once the three keys below are set; until they are, the routes answer 400 `error.billing_disabled` |
| `Billing:StripeSecretKey` / `StripePriceId` / `StripeWebhookSecret` | empty | Environment only (`Billing__StripeSecretKey`, `Billing__StripePriceId`, `Billing__StripeWebhookSecret`): the API secret key (`sk_test_…` works against Stripe's test mode), the recurring Pro price (`price_…`), and the signing secret of the webhook endpoint (`whsec_…`). Read in `Services/StripeClient.cs` and `Endpoints/BillingEndpoints.cs`; the secret key is redacted from HttpClient logging |
| `Billing:PublicOrigin` | empty | Where Checkout returns to (`/#/pro?checkout=success` or `cancel`); the request's origin when empty |
| `Billing:StripeYearlyPriceId` | empty | **Round 20.** The yearly recurring price (`price_…`), optional, environment only like the other keys. A year is sold only with this AND a yearly amount; with the id set the doctor also demands that the monthly price recur every month |
| `Billing:StripeBaseUrl` | `https://api.stripe.com/` | Where Stripe is, read by the named Stripe client and by both doctor reads; blank, not an absolute URL, or anything but `https` (plain `http` only to this machine, since every request carries the secret key) falls back to the default, and a trailing slash is always added so relative paths keep their prefix. It exists so the browser test can point the app at its stub; the doctor warns on any other address, and it stays unset on a server |
| `Limits:ChecksPerDay` | `30` | The ceiling per account over a rolling 24 hours, whatever the plan says: `Plans:ProChecksPerDay` cannot exceed it. Counted including checks still in flight; failed calls do not count |
| `Limits:ChecksPerDayGlobal` | `1000` | Ceiling across all users and guests over a rolling 24 hours: checks and comparisons on both routes (`Services/Spend.cs`), calls in flight counted, failed calls left out (429 `error.rate_limited_global`) |
| `Limits:SignupsPerHourPerIp` | `50` | New accounts per client address per hour (address taken from `X-Forwarded-For` behind the tunnel) |
| `Limits:LoginsPerQuarterHourPerIp` | `30` | Login attempts per client address per 15 minutes |
| `Limits:CommentsPerHour` / `Limits:ReportsPerHour` | `30` / `20` | Per signed-in account, fixed one-hour windows in memory (429 `error.too_fast` with `Retry-After`) |
| `Limits:ReportsToHide` | `3` | Reports from distinct people after which a post or comment is hidden |
| `Limits:AdminActionsPerHour` | `120` | **Round 20.** The per-moderator cap on the eight account actions on `#/admin` (verify, unverify, grant and remove Pro, exclude from and put back on the board, suspend, unsuspend): a fixed one-hour window partitioned by account, 429 `error.too_fast` with `Retry-After`. A brake on a stolen moderator cookie or a script, not a product limit; the reads (the queue, the users, the sponsor) are never limited |
| `Board:WeekStartsOn` | `Sunday` | The first day of the board's week (a `DayOfWeek` name), in `Board:TimeZone` |
| `Board:TimeZone` | `Asia/Jerusalem` | The IANA zone the week is cut in: it opens at local midnight on `WeekStartsOn` and closes seven days later (Saturday midnight in Israel); `weekStart` and `weekEnd` on the board are those instants in UTC. A zone this machine does not know falls back to UTC with a warning at start (`Board: the time zone … is not known here`) |
| `Board:MinChecksToCount` | `1` | A fire counts only when the firer has made at least this many `ok` checks by the week's end (a check later in the week makes their earlier fires count). `0` turns the rule off |
| `Board:MaxPerFirerPerAuthor` | `3` | The most fires from one person on one author's looks that count in a week; the first ones by time count, the rest do not. `0` or less means unlimited |
| `Board:NewAccountDays` | `2` | A fire from an account younger than this at the moment of the fire does not count |
| `Board:Size` | `10` | Places on each board |
| `Board:RisingDays` | `30` | The rising board lists the fired looks of accounts younger than this at the week's end |
| `Board:CacheSeconds` | `60` | How long a computed week is served from memory, real time, per process. Only the running week and, until the closer has written it, the one before are kept (two entries at most; an exclusion drops them); every other week, the archive browsed back or next week, is computed on each read. `0` turns the cache off (the browser test runs so) |
| `Board:Sponsor:Name` / `Handle` / `PrizeText` / `Url` | empty | The week's sponsor, on the board only while `Name` is set: the name (linked to the account when `Handle` names one — a leading `@` is dropped, the same way on the board and on the admin card — else to `Url`), the prize line and the site's host. `Url` must be an `http(s)` link with a host and no user info; a bare host (`nexor.example`, `www.nexor.example/drop`) is read as `https://`; anything else (`javascript:`, `ftp:`, `mailto:`, `user:pw@host`) is dropped at start with the warning `Board: the sponsor link {Url} is not an http(s) URL; the board shows the sponsor without a link`, and the page checks the link again before it becomes an `href`. Settings, not a form: there is no sponsor self-service |
| `Affiliate:Disclosure` | `true` | Whether the item sheet shows "This link may earn OREVOSH a commission." under a store link. Published by `/api/config` as `affiliate.disclosure` and read by the sheet: while `true`, the line follows "Leaves OREVOSH" under every store link, listed host or not; `false` leaves "Leaves OREVOSH" on its own. Keep it on wherever a programme is joined; the hosts and their parameters are never published |
| `Affiliate:Hosts` | `{}` | Host → the query string `GET /api/items/{id}/out` appends when a link goes there, e.g. `"amazon.com": "tag=orevosh-20"` (`Affiliate__Hosts__amazon.com=tag=orevosh-20` as an environment variable); a listed host matches case-insensitively with its subdomains (`www.amazon.com` and `smile.amazon.com`, not `notamazon.com`). Empty by default: nothing is appended and no link earns anything until you list a programme you joined. The parameters are added at the door, never stored, so a change here changes every link at once |
| `Anthropic:PriceInPerMillion` / `PriceOutPerMillion` | `2.00` / `10.00` | **Round 13 — money.** USD per million tokens, the owner's own contract prices. Every dollar on the numbers page and the daily ceiling is built on these: they are settings, never Anthropic's invoice, and `appsettings.json` says so in a `_prices` note. The doctor prints them |
| `Limits:SpendPerDayUsd` | `0` | The day's ceiling in **estimated** USD (a UTC day). At the ceiling every route that would ask the model answers 503 and spends nobody's allowance. `0` is off, and `--doctor` warns about that; 5 is a sane pilot number |
| `Alerts:Webhook` / `Alerts:Email` | empty | **Round 13 — money.** Where a readiness flip, the spend ceiling, a run of model failures, a full disk or a failed backup shouts. `Alerts__Webhook` is an https incoming-webhook URL that takes `{ text }` and is a secret, so it belongs in the environment and never in `appsettings.json`; `Alerts__Email` is one address, sent through the app's own mail, so it sends nothing at all unless `Email__Host` and `Email__From` are set too. With neither channel reachable, every alert is only a log line nobody is watching, and both the startup log and the doctor say so |
| `Alerts:ModelFailuresIn10Min` / `Alerts:DiskFreeMb` | `5` / `512` | The two thresholds that shout on their own: model failures inside ten minutes, and the free space where the data lives |
| `Languages:Enabled` | `["en","he"]` | The languages this server actually offers. A language ships when a native reader has read it, not when the file exists: `ar` and `ru` are translated and shipped in the repository but left out of this list, and a check asking for one falls back to English. `/api/config` publishes the list |
| `Digest:Enabled` / `Digest:Hour` | `true` / `9` | The Sunday mail: the week a person had, sent only to a confirmed address and only to an account something happened to. The hour is local to `Board:TimeZone`. Nothing goes out at all without mail configured and a public origin |
| `Logging:Requests` | `false` | One log line per request — the method, the path, the status and how long it took — on top of the usual lines. Off by default: a pilot's log is worth reading, and this is a lot of lines. Turn it on (`Logging__Requests=true`) for the first days of a launch and while chasing something, then off again. It never logs a body, a cookie, a header or a query string's values, so nothing a person typed and no session lands in the log; the client address is already there for the rate limiters |
| `Funnel:Sources` | `tt, ig, wa, campus, yt, fb, x, qr, story, dm` | **Round 20.** The entry links this server answers, `/go/<source>`. Entries are trimmed, lower-cased, must match `[a-z0-9]{1,16}` and are kept once each in order; a long name is kept as the short code it spells (`tiktok` is the `tt` row, as on the link); an empty or all-invalid setting is the default list. The setting REPLACES the list (`Funnel__Sources__0=campus` alone answers `/go/campus` and nothing else): the property starts empty so the configuration binder does not append to the ten defaults |

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
| `POST /api/auth/signup` | `{ handle, password, birthDate, today?, language, displayName?, notifyStylistBack?, source? }` | `201` me. Handle: 2–40 letters, digits, dots or underscores, unique case-insensitively; password 8–200; `birthDate` is `yyyy-MM-dd` (what a date input sends), 16 years or more before today, not before 1900 and not in the future: 400 `birthdate_required`, `birthdate_invalid` or `underage` in that order after the handle and password rules. `today` is the client's own calendar date (`yyyy-MM-dd`): the sixteen rule and the not-in-the-future check are measured on it when it is within one day of the server's UTC date, otherwise on the UTC date, so nobody is stopped on their birthday east of Greenwich. The date is stored and never returned by any route. `confirmed16Plus` from older clients is ignored. 409 taken (a handle listed in `Admin:Handles` counts as taken), 429 too many signups from one address. **Round 20** adds two optional body fields, neither ever a reason to refuse: `notifyStylistBack` (bool, default false) — when true AND the spend ceiling is closed at that moment, a `Counter` row `stylist_back:{userId}` is written after the account is created, which `StylistBackService` turns into one note once the stylist is back (the client sends it from `#/signup?back=stylist`, where the check screen's `#resting-offer` points after a guest's 503 `error.stylist_resting`) — and `source`, the entry link's word, normalised against `Funnel:Sources` and stored on `AppUser.Source` (at most 16 characters; anything else is stored as null) |
| `POST /api/auth/login` | `{ handle, password }` | `200` me. 401 for a wrong handle or password (same message for both), 429 too many attempts |
| `POST /api/auth/logout` 🔒 | — | 204 |
| `GET /api/auth/me` 🔒 | — | `{ id, handle, name, accountType, language, bio, website, streak, unreadNotifications, avatarUrl, interests, isAdmin, email, emailVerified, plan, proUntil, verified, checksToday, checksPerDay, badge? }`. `isAdmin` is the account's persisted moderator flag, set at start from `Admin:Handles` or by `--admin`, never by a request. `plan` is `free` or `pro` (`pro` only while `proUntil` is in the future or open), `verified` is the `--verify` flag, `checksToday` counts the account's checks in the rolling 24 hours (failed ones excluded) and `checksPerDay` is its cap; on a FREE account comparisons are counted in the same number, and on a PRO account they are not — Round 14 gives Pro a second allowance for comparisons alone (`Plans:ProComparesPerDay`, enforced on `POST /api/compare`), so `checksToday` on Pro is checks only. `badge` is last week's place in the top three of the looks board, `{ board: "looks", rank, weekStart }`, worn for this week only and absent otherwise. `stylistBackAsked` (every answer that carries me) is true while the account holds the stylist-back ask its signup recorded, until the pass keeps it; the welcome screen promises the note only on it. A suspended account gets 403 and is signed out |
| `POST /api/auth/forgot` | `{ handleOrEmail }` | `202` always, same body whether or not the account exists; mails a reset link when the account has a confirmed email (5 per hour per address) |
| `POST /api/auth/reset` | `{ token, password }` | `200` me, signed in. 400 for a used, expired or unknown link (the link survives a too-short password) |
| `POST /api/auth/verify-email` | `{ token }` | `200` me. Confirms the address the link was sent to, and only while that is still the account's address; works signed out, signs nobody in |
| `POST /api/users/me/email/resend` 🔒 | — | 204, a new confirmation link (5 per hour per address, and per account three per ten minutes or ten a day: 429) |
| `GET /api/config` | — | `{ maxImageBytes, maxVideoBytes, maxVideoSeconds, pushPublicKey?, email, transcoding, plans: { freeChecksPerDay, proChecksPerDay, guestChecksPerDay, proPriceText, compareNeedsPro, billing }, affiliate: { disclosure }, publicOrigin? }`. `plans.proChecksPerDay` is what a Pro account really gets (`Plans:ProChecksPerDay` clamped to `Limits:ChecksPerDay`); `billing` is true only when Stripe Checkout is live; `affiliate.disclosure` is `Affiliate:Disclosure`, whether the item sheet shows the commission line under a store link (the hosts and their parameters stay on the server); `publicOrigin` is `Email:PublicOrigin`, else `Billing:PublicOrigin`, trimmed, absent when neither is set, and the shared video's end card names its host. No secrets. **Round 20:** `plans` also carries `proYearlyPriceAmount`, `proYearlyPrices` (the yearly table), `yearly` (true only while Stripe is live and `Billing:StripeYearlyPriceId` is set), `proTrialDays` (`Plans:ProTrialDays` clamped to 0..730), `tomorrowMorningPush` (`Plans:TomorrowMorningPush` and `Plans:TomorrowEnabled` both on) and `tomorrowMorningHour` (`HH:mm`); the yearly saving is not published, the Pro page computes it from the two numbers. A request carrying `X-Orevosh-Launch: standalone` counts one home-screen launch here (`funnel:standalone:yyyyMMdd`) and nowhere else — the header is ignored on every other path; the installed client sends it once per device-day (`prefs.standaloneDay` in `localStorage`, marked once this call has answered, so a launch with no signal does not spend the day), never a cookie |
| `GET /healthz` | — | `ok` when the database answers, 503 otherwise. For the proxy and uptime checks |
| `GET /readyz` | — | `{ ok, checks }`: is this machine ready to serve? Public, `Cache-Control: no-store`. `checks` maps a name to `"ok"` or a short reason: `db` (a query answers and the schema is at the current migration), `storage` (`Storage:Root` exists and a file can be written and removed there) and, only while `Storage:Transcode` is on, `ffmpeg` (the binary is found). 200 with `ok` true when every check passes; 503 with `ok` false and the failing checks named. A reason never carries a path, a version or a secret, so the line is safe to leave public. `/healthz` is untouched and stays what the proxy, the Docker health check and `fly.toml` poll |
| `GET /go/{source}` | — | **Round 20, the entry links.** No session, no CSRF header, not under `/api`. `302` to `/?src=<source>[&via=<handle>]#/check` when `<source>` (trimmed, lower-cased; the aliases `tiktok`, `instagram`, `whatsapp`, `youtube`, `facebook` and `twitter` resolve to `tt`, `ig`, `wa`, `yt`, `fb` and `x`) is on `Funnel:Sources`, one arrival counted on `funnel:src:<source>:yyyyMMdd`; `302` to `/landing/` when it is not, nothing counted; 404 for `/go/` with no word. Every answer carries `Cache-Control: no-store` and the security header set, and no cookie is set. A user agent matching `bot\|crawl\|spider\|facebookexternalhit\|whatsapp\|telegrambot\|twitterbot\|slackbot\|discordbot` is redirected but not counted: a messenger unfurling a pasted link is a paste, not a person. A handle-shaped `?via=` (never `share`) is counted as one invite arrival on the hop itself (not for the crawlers above) and rides onto the redirect for the client to keep; the address it lands on carries `src`, which the funnel middleware reads as already counted, so a followed link is one invite and a word off the list none. The hop is where it is counted because the landing page may never reach the server: a phone that has opened OREVOSH before answers that navigation from its service worker, as `/index.html` without the query (second look at Round 21). Nothing here refuses, so there is no error key. The service worker passes `/go/` navigations to the network and `robots.txt` disallows `/go/` |
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
| `POST /api/checks` | multipart: `occasion`, `style?`, `note?`, `language`, `image`, `video?`, `source?` | `201 { id, intent, occasion, style, language, createdAt, latencyMs, status, score, feedback, postId }`. **Round 14 split the one question in two** ("Round 14 — the stylist" below has the refusals and the one-word rule): `occasion` is where the outfit is going, `style` is optional and empty means none, and the wearer's own free line is now `note`. A client from before the split that still sends `intent` is understood — the one word is split into the pair and its `occasion` field is read as the free line. **No session needed:** signed out, the call is a guest's, named by the `orevosh.guest` cookie (minted on the first one, sent back with the answer), allowed `Plans:GuestChecksPerDay` times per cookie and per client address over a rolling day, counted from stored checks (429 `error.guest_limit` with `Retry-After`, only ever for a look actually given: a refused upload, a 502 or a dropped connection spends nothing; 401 `error.sign_in_required` when that setting is 0; beyond `Plans:GuestAttemptsPerDay` attempts from one address in 24 hours, 429 `error.too_fast`); a guest's photo is stored in a shared folder until claimed or swept. Signed in, the account's plan cap applies, with `Retry-After`: at the cap a free account hears `error.plan_limit` (which names the Pro number), a Pro account `error.rate_limited` (with its cap), as on the compare route; at `Limits:ChecksPerDayGlobal` everyone hears `error.rate_limited_global`. `Retry-After` on both routes is when a permit actually frees up: the expiry of the (count − cap + 1)th oldest counted call, not the oldest. `video` is an optional MP4/MOV/WebM clip of the same look (≤ `Storage:MaxVideoBytes`); the stylist judges only `image`, the frame the person picked, and the clip is stored with the check when the status is `ok` (a guest's clip is transcoded only after the claim). 413 too large (still or clip), 415 not JPEG/PNG/WebP (or not MP4/WebM for the clip), 429 over a cap, 502 model failure. **Round 20:** the optional `source` field (the entry link's word, normalised against `Funnel:Sources`) is stored on `OutfitCheck.Source` for a guest's check, anything else as null and never refused (since the second look at Round 21 a signed-in check stores none: the numbers page reads it off guest checks only); and the free day's 429 `error.plan_limit` for a signed-in free account carries `code: "plan_limit"` in its body — `ErrorDto` is `{ error, code? }`, that is the only `code` any body has, and the guest's `error.guest_limit`, a Pro account's `error.rate_limited`, the month's and the global refusal all carry none |
| `POST /api/checks/claim` 🔒 | — | `{ claimed }`: every check and comparison carrying the caller's guest cookie becomes the account's (owner set, token cleared, `claimedAt` stamped, the files moved into the account's folder, a claimed clip queued for the transcoder) and the cookie is dropped. All or nothing: a file that cannot be copied (a full disk, a file missing from the store) answers 500 `error.server`, the rows stay the guest's and the cookie stays, and the client claims again on its next load. `{ claimed: 0 }` when there was nothing, so the client calls it blind after signup, after login and at every signed-in boot |
| `GET /api/checks/{id}` | — | The check, for its owner or for the guest whose cookie made it (404 to anyone else, the same as a missing id) |
| `POST /api/checks/{id}/shared-video` | — | `204`. One tally (`videos_made`, on the numbers page as "Share videos made") after the person saved or shared the check's video. The video itself is drawn and encoded **on the phone** (a 12-second 1080×1920 file: H.264 MP4 where the browser can, else VP9/VP8 WebM, else the story card PNG) and never touches the server. Owner or guest-cookie only, 404 to anyone else like the GET; 30 per hour per account or address (429 `error.too_fast`) |
| `POST /api/checks/{id}/tried/shared` 🔒 | `{ withScores }` | **Round 20.** 204: the before/after card or film of the pair left the phone, counted on `before_after_shares` (`withScores` true) or `before_after_shares_plain` — the same two rows `POST /api/posts/{id}/shared-after` moves, because a pair share is a pair share whether the look was posted or not. Either side's id works; 404 `error.check_not_found` when the check is not the caller's or is in no pair; 401 without a session, 403 without the header; the `useful` policy (60 an hour). `POST /api/checks/{id}/tried` (201) and `/tried/prefer` (200) now carry `postId` on both sides of the `TriedPairDto`, as `GET /api/users/me/tried` always did (absent while a side is not posted) |
| `POST /api/compare` 🔒 | multipart: `occasion`, `style?`, `note?`, `language`, `imageA`, `imageB` (legacy: `intent`, with the free line as `occasion`) | `201 { id, intent, occasion, occasionKind, style?, language, createdAt, latencyMs, status, feedback: { status, winner, scoreA, scoreB, headlineA, headlineB, reason, oneTip, close, message? }, imageUrlA, imageUrlB }`: both photos go to the stylist in one call (`PromptVersion` `cmp-v3`, the analyzer's rules and calibration) and one wins, `a` or `b`. **Since Round 20 the form asks the check's two questions:** `occasion` is one of `Everyday, Date, Office, Party, Formal, Sport` (case-insensitive), `style` one of `Streetwear, OldMoney, Minimal, Classic` or empty for none, and `note` is the free line (≤ 120). A client from before the split that still sends `intent` is understood: the one word is split with `StyleIntents.Split` and its `occasion` field is read as the note. On the answer `intent` is the one word (`StyleIntents.Legacy` of the pair), `occasion` is the free NOTE under its old name, `occasionKind` is always present and `style` is omitted when none was asked for; `feedback.close` is derived on the server, true only on an `ok` verdict whose two scores are within one point and both 5 or more (`OutfitComparer.WorksFrom`, the foot of the calibration's "fine" band; false on every row stored before Round 20, and lowered on read for a row that said close over a lower score) — the prompt says a close call is a real answer, that both outfits then work, and that the occasion decides it, never the style, never a coin flip; when both score 4 or less the reason says that neither works for the occasion yet, and the verdict line is the plain win. Round 14: on a FREE account this counts against the same daily allowance as a check; on a PRO account against `Plans:ProComparesPerDay`, an allowance of its own, so comparing two looks never spends a check (`Limits:ChecksPerDayGlobal` and `Limits:SpendPerDayUsd` still count every stored call, whatever the plan). Statuses and keys, in gate order: 401; 403 `error.pro_required` (`Plans:CompareNeedsPro`); 400 `error.invalid_request` (not a form); 413 `error.image_too_large`; 400 `error.intent_invalid` (a bad legacy word) or 400 `error.occasion_invalid` / `error.style_invalid`; 400 `error.occasion_too_long`; 400 `error.compare_two_photos`; 413/415 per still; 503 `error.stylist_resting` (a Pro account passes); 429 `error.month_limit`; 429 `error.plan_limit` with `code: "plan_limit"` for a free account, 429 `error.rate_limited` (no code) for Pro, 429 `error.rate_limited_global`; 502 `error.model_failed`. `not_outfit` says which photo to replace; `rejected` keeps nothing but the status. Private and never postable |
| `GET /api/compare/{id}` 🔒 | — | The comparison, owner only (404 otherwise) |
| `GET /api/compare/{id}/image/a` and `/b` 🔒 | — | The two photos, owner only, `Cache-Control: private`. The only route that serves them; 404 for a comparison that kept none |
| `GET /api/wardrobe` 🔒 | — | As in "Round 14 — Pro worth paying for" below, plus since Round 20 `proMoment` (true while `Plans:WardrobeNeedsPro` is on, the account is free, `Plans:WardrobeNamesToStylist` is above 0 and the wardrobe holds strictly more pieces than it — the server's fact, `Plans.WardrobeProMoment`) and `proSees` (how many of these pieces Pro's stylist would see: what Pro's prompt would carry of this wardrobe — clothes only, nothing that names a person, at most the larger of `WardrobeNamesToStylist` and `WardrobeNamesToStylistPro`, 40 by default — counted by `Wardrobe.SeenBy`; `proMoment` is false when that is 0), so the client says "all of them" only while it is every piece |
| `POST /api/wardrobe/keep-all` 🔒 | `{ checkId, names? }` | **Round 20.** `200 { kept, added, skipped, full, count, max, items[] }` always on success (a batch has no single 201; `added` says how many were new; `kept` is how many of this look's pieces are now in the wardrobe, never its size, which is `count`): every piece the check named goes into the wardrobe in one request — known pieces gain the look and are never refused by the cap, new ones are added in the stylist's order until `Plans:WardrobeMaxItems` and the rest skipped (`full: true`). A check that named nothing (`not_outfit`, `rejected`) answers 200 with `kept: 0` and writes nothing. 400 `error.invalid_request` without a `checkId`; 404 `error.check_not_found` for a missing check, a guest's or another account's; 409 `error.wardrobe_full` only when nothing at all could be written and the cap kept something out. The server's own list of names is the only list: `names` (review of Rounds 20 and 21; the keep row sends the pieces it still offers, so one refused with "Not this one" stays out) can only take names off it, a name the check did not give is ignored, and with no `names` it is every piece; there is no path parameter, so the IDOR sweep is untouched and the stranger's 404 is pinned in `WardrobeTests`. Counted once per request that wrote a row — a new piece, a new look, a later last-worn date (`wardrobe_keep_all`), so the same request twice counts once |
| `GET /api/wardrobe/unkept` 🔒 | — | **Round 20.** `200 { pieces: [{ name, category, checkId, wornAt, postId? }], checks }` always: the pieces named on the account's last `Plans:WardrobeUnkeptChecks` OK checks that are not in its wardrobe (under their name, or under the stylist's name a renamed piece was kept as), newest look first, each once and carrying the newest check that named it, at most 30 (`Wardrobe.UnkeptMaxPieces`); `checks` is how many checks were looked at; `postId` only where that look is a visible post. Empty with `checks: 0` when the setting is 0 or the account has no OK check. Keeping one uses the existing `POST /api/wardrobe { checkId, name }`; nothing records a refusal, so a piece passed over resurfaces |
| `POST /api/wardrobe/moment` 🔒 | `{ step: "shown" \| "go" }` | **Round 20.** 204 either way; increments `wardrobe_moment_shown` / `wardrobe_moment_go` only while `Plans.WardrobeProMoment` is true for the caller right now, so a script cannot inflate a rate the moment never earned, and since the review of Rounds 20 and 21 it has the tallies' hourly brake (the `useful` policy, 60 an hour per account, 429 `error.too_fast`); 400 `error.invalid_request` for any other step. The client owns only once-per-tab (`sessionStorage`) |
| `POST /api/posts` 🔒 | `{ checkId, caption?, challengeId?, products?, beforePostId?, items? }` | `201` post. The check must be yours, `ok`, and not yet posted; caption up to 140 characters, its `#tags` (first 5) and `@mentions` of existing handles (first 5) are stored and mentioned accounts are notified; a caption carrying an open challenge's hashtag enters that challenge (once per person; `challengeId` is still accepted); `products` (brands only, up to 3) are `{ label, url, price? }` with https URLs; `beforePostId` names one of your own visible looks this one improves on ("after the tip": 400 `error.before_invalid` for anyone else's look, a hidden one, or the look of this very check). Without `items`, the stylist's item names are copied onto the look, lower-cased (up to 8, 60 characters each, with their category, source `Stylist`, never a brand), so `/api/search` finds it by piece; the item notes stay private (each piece's one-word verdict travels with the look since Round 21: `GET /api/posts/{id}` below). With `items` (the post sheet's list, the same shape and rules as `PATCH /api/posts/{id}/items` below), that list is the whole list: an input without an id whose name, normalised, equals a stylist row's name (the stylist's uncut name from the check and the stored name's first forty characters count as the same) keeps that row as the stylist's, anything else is the person's own row, and an invalid list refuses the post with the same 400s before anything is written, so the check stays postable |
| `GET /api/posts/{id}` | — | The post: `user, intent, score, intentMatch, headline, caption, challengeId, challengeTitle, fireCount, commentCount, fired, saved, isMine, hidden, votes, products, imageUrl, videoUrl?, breakdown?, before?, createdAt, tags, mentions, featuredBy, items, itemCount`. `breakdown` is `{ fit, color, accessories }` for a rubric-v2 check; `before` is `{ postId, score, imageUrl }` of the earlier look (left off while that look is under review; the link is cleared when it is deleted). `items` (on every card, in every feed, in position order, `[]` when there are none) are `{ id, name, category, brand?, model?, url?, host?, source, x?, y?, confirmed, verdict? }`: `name` lower-cased, `category` one of `top, bottom, dress, outerwear, shoes, accessory, other`, `source` `Stylist` or `User`, `host` the link's host without `www.` for "Shop at {host}", `x`/`y` the dot on the photo as fractions of its width and height (absent when the piece is listed and not placed), `confirmed` true only for a stylist suggestion the person accepted; **`verdict`** (Round 21) the stylist's `works`, `neutral` or `weak` for the piece of that name, read from the check's stored feedback, so a card can draw the dot beside the name — **only for the look's author and for a moderator in the queue** (the people who may read the check; settled in the review, public number or private), and absent for everyone else and for a name the check never gave (the client then draws the name with no dot; the stylist's note on the piece never travels); `url` is the raw link and the client never sends anyone to it (the out door does). Hidden posts are visible to their author and to moderators only; a suspended author's posts are hidden |
| `GET /api/posts/{id}/image` | — | The photo (`Cache-Control: private`). The only route that serves a check photo, and only for a visible post |
| `GET /api/posts/{id}/video` | — | The clip (`video/mp4` or `video/webm`, `Cache-Control: private`, Range requests honoured so players can seek). 404 for a look without a clip. The only route that serves a clip. A WebM becomes `video/mp4` at the same URL once the background transcode is done (Configuration, "Clips") |
| `DELETE /api/posts/{id}` 🔒 | — | 204, author only. The photo becomes private again with the check; the clip is deleted, and so are the look's items (their search rows and store links with them) |
| `PATCH /api/posts/{id}/items` 🔒 | `{ items: [{ id?, name?, category?, brand?, model?, url?, x?, y?, confirmed? }] }` | `200` the look's items, in the order sent, each with its `verdict` as `GET /api/posts/{id}` carries it (Round 21; one read of the post's check). Owner only: another person's look and a hidden one answer 404 `error.post_not_found` alike. The list is the whole list, at most 12 (400 `error.items_too_many`, "Up to 12 items on a look."): a row not in it is removed, `items: []` clears the look, and a missing body or a body without the list (`{}`, `{ "items": null }`) is 400 `error.item_invalid` with nothing changed: only an explicit `[]` clears. An input with `id` keeps that row (its name and category may be left out to keep them); one without an id whose name equals a not-yet-named stylist row's name re-attaches to it; anything else is a new row with source `User`. A stylist row stays the stylist's while only its brand, model, link, dot or confirmation change and becomes the person's once its name or category does. Rules, each a 400 with nothing written: a typed name 1–40 characters after normalisation (lower-cased, one space between words; a stylist name sent back unchanged may be up to 60, and unchanged means the stored name, the stylist's uncut name from the check, or the stored name's first forty characters, none of which re-attributes the row), `category` one of the seven (a new row without one is `other`), `brand` ≤ 40, `model` ≤ 60 (`error.item_invalid`, also for a duplicate or unknown `id`); `url` absolute `http(s)`, ≤ 500, with a host and no user info, so `javascript:`, `data:`, `ftp:`, a relative path or `nike.com@evil.example` are refused (`error.item_url_invalid`); `x` and `y` both in 0..1 or both absent (`error.item_position_invalid`); `confirmed` is stored true only with a brand on a row that is still the stylist's. The stylist's `brandSeen` is never copied by the server: the client shows it as "Looks like Nike?" and sends it back as `brand` with `confirmed: true` when the person confirms, another brand with `confirmed: false` on Edit, and no brand on "Not a brand". Logs `Items: {Count} on post {PostId} by {UserId}` |
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
| `GET /api/notifications` 🔒 | — | `{ items: [{ type, actorHandle, actorName, postId, challengeId, createdAt, read, rank?, checkId? }], unread }`. Types: `fire, comment, follow, vote, entry, ended, won, mention, featured, board_rank, try_tip, stylist_back`. `board_rank` is written by the board's closer for everyone on the looks board of the week that just closed (one per person, their best place in `rank`, `actorHandle` their own handle, `postId` the look), shown as "You finished #{rank} this week" and pushed with the same line, the tap landing on the week that closed for the push and the activity line alike: `#/board?week=<instant>`, the instant six and a half days before the line was written (UTC, `yyyy-MM-ddTHH:mm:ssZ`; the row carries no week, and the closer writes it after the close, so that instant is mid-week inside the closed week whether the week ran 167, 168 or 169 hours), which `?week=` takes as an instant. Older weeks closed in a catch-up are silent. **Round 20** adds two types in which the person is their own actor (`postId` and `challengeId` null): `try_tip`, the day-after nudge written by `TryTipNudgeService`, the only type that carries `checkId`, its tap landing on `#/checks?try=<checkId>`; and `stylist_back`, written by `StylistBackService` once a closed day is open again, its tap landing on `#/check`. Both are pushed with the same line (`push.try_tip`, `push.stylist_back`) |
| `POST /api/notifications/read` 🔒 | — | 204, marks everything read |
| `GET /api/push/state` 🔒 | — | `{ enabled, subscribed }`: whether the server has VAPID keys, and whether this account has at least one subscription |
| `POST /api/push/subscriptions` 🔒 | `{ endpoint, p256dh, auth }` (from `PushSubscription.toJSON()`) | `200` state. Upserts this browser's subscription for the account, at most 10 per account (the oldest make room); 400 when push is off, the subscription is malformed, or the endpoint is not a public push-service name (a literal address, `localhost` or a single-label host is refused). A subscription the push service answers 404/410 (gone) or 401/403 (made against other VAPID keys) to is deleted |
| `DELETE /api/push/subscriptions` 🔒 | `{ endpoint }` | 204 |
| `POST /api/push/test` 🔒 | — | 202, sends a test notification to the caller's own browsers |
| `GET /api/push/morning` 🔒 | — | **Round 20.** `{ on, offered, hour }`: the person's own switch on the morning push (`AppUser.TomorrowPushOn`, on by default), whether this server offers it at all (`Plans:TomorrowMorningPush` and `Plans:TomorrowEnabled` on and VAPID keys set) and the hour as `HH:mm`. 401 signed out |
| `POST /api/push/morning` 🔒 | `{ on }` | `200` the same state. 400 `error.invalid_request` when `on` is missing, 400 `error.tomorrow_push_off` when the server does not offer it, 401 signed out, 403 without the header |
| `GET /api/tomorrow?from=push` 🔒 | — | The same answer and statuses as `GET /api/tomorrow` ("Round 19" below); the query stamps this account's newest unopened morning push receipt from the last 24 hours as opened (`TomorrowPushes.OpenedAt`, once per push). Never a model call, and a read with no push behind it counts nothing |
| `GET /api/billing/state` 🔒 | — | `{ plan, proUntil, billing, proPriceText, yearly, trialDays }`: the effective plan, whether Stripe Checkout is live, the price text, whether a year can be bought (Stripe live and `Billing:StripeYearlyPriceId` set) and how many free days Checkout would open for this account — `Plans:ProTrialDays` clamped to 0..730 while Stripe is live and the account has no `BillingCustomerId`, 0 otherwise, always 0 on the manual provider |
| `POST /api/billing/checkout` 🔒 | `?currency=&interval=month\|year&return=compare` | `{ url }` of a Stripe Checkout Session (subscription, the Pro price, the account id as `client_reference_id` and `metadata.userId`, the confirmed email prefilled, an existing customer reused) that returns to `/#/pro?checkout=success` or `cancel`. **Round 20:** `interval` (default `month`) buys the yearly price where the page could have offered it — `Billing:StripeYearlyPriceId` set and a yearly amount in the quoted currency; a yearly Checkout never swaps to the fallback currency the way the monthly leg does, because the page never showed a year in that currency. The session carries `metadata[interval]` and, for an eligible account (`Plans:ProTrialDays` above 0, no `BillingCustomerId`), `subscription_data[trial_period_days]`, `payment_method_collection=if_required`, `subscription_data[trial_settings][end_behavior][missing_payment_method]=cancel` and `metadata[trialDays]`. `return=compare` is an allowlist of one (anything else is dropped silently): the two return URLs then carry `&return=compare`, and the Pro page, having arrived as `#/pro?from=compare`, sends the person on to `#/compare?ready=1` once `me` says pro. Gate order: 401; 400 `error.billing_disabled` while the provider is `manual` or a key is missing; 409 `error.already_pro` for an account that is Pro (one subscription per account; a stale tab re-reads `me`); 400 `error.billing_interval` for an interval that is not `month` or `year`, or `year` while no yearly price is set or the quoted currency has none; 502 `error.billing_failed` when Stripe did not answer with a page |
| `POST /api/billing/portal` 🔒 | — | `{ url }` of a Stripe Billing Portal session for the account's customer (`customer` and `return_url` = the origin plus `/#/settings`, posted form-encoded to `v1/billing_portal/sessions` through the same named client as Checkout); the client sends the person there in the same tab, and that is where a subscription is changed or cancelled. 404 `error.portal_unavailable` ("Manage your plan by writing to us.") while the provider is `manual` or the account has no customer id — a Pro granted by `--pro` has nothing on Stripe's side, and the Pro page and the settings row show that sentence instead of the button. 502 `error.portal_failed` when Stripe answers with no url. Nothing is written here: what the person does on the portal comes back through the webhook |
| `POST /api/billing/webhook` | Stripe's event, raw | `200 { received: true }`. No session, no CSRF header: the `Stripe-Signature` header (`t=…,v1=…`, HMAC-SHA256 over `t.body` with `Billing:StripeWebhookSecret`, within five minutes of now) is the guard, 400 `error.billing_signature` otherwise. `checkout.session.completed` puts the account on Pro for 35 days on top of any period still running and stores the customer id; `invoice.paid` (except the first, `subscription_create`) moves the end to the invoice's period end plus three days, never below the current end (an invoice naming no period is worth 35 days from now); `customer.subscription.updated` follows the status (`active`/`trialing`: the current period end plus three days, never below the current end; `past_due`/`unpaid`/`paused`: three days from now at most); `customer.subscription.deleted` ends Pro now (the plan field keeps saying a subscription existed); every other event, and an event naming no account, is answered 200 so Stripe stops sending it. **Round 20 keeps the event ids.** After the signature and JSON checks the event's `id` (cut to 64 characters) is looked up in `StripeEvents`: a known id answers `200 { received: true, replayed: true }` and does nothing; otherwise the event is handled and then recorded (`Id`, `Type`, `ReceivedAt`) — record-after, so a handler that threw answers 500, leaves no row, and Stripe's retry is handled rather than ignored. An event with no id is handled every time. The lookup, the handler and the record run under one process-wide gate (`BillingEndpoints.WebhookGate`), so a parallel burst of one id is handled once; rows older than 30 days are pruned by `RenewalRecapService`. `checkout.session.completed` now reads what the session sold, always on top of any period still running: 368 days for `metadata.interval=year` (`PaidYear`), the trial's days (capped at 730) plus the three-day slack for a session with `payment_status=no_payment_required` and `metadata.trialDays` above 0 (a no-payment session without `trialDays`, a full coupon say, is granted like a paid one), 35 days for everything else |
| `POST /api/funnel/pro-opened` 🔒 | `{ from: "compare" \| "wardrobe" }` | **Round 20.** 204 and one increment of the day's counter (`funnel:pro:compare:yyyyMMdd` or `funnel:pro:wardrobe:yyyyMMdd`), which the numbers page reads as `proFromCompare` / `proFromWardrobe`; 400 `error.invalid_request` for any other value or an empty body; 401 signed out or a cookie whose account is gone, 403 `error.suspended` for a suspended account (both sign the cookie out and count nothing: the account is loaded as on every signed-in door). A hash route never reaches the funnel middleware, so the Pro page says so itself, once, on arriving as `#/pro?from=compare` or `from=wardrobe`. A client-driven tally by an account, like the board views: a moderators' number, never a decision. No route parameter, so no IDOR row |
| `GET /api/admin/queue` 🔒 | — | Moderators (accounts with the `isAdmin` flag) only, 403 otherwise: reported looks and comments with counts, reasons and the author's state, plus counters |
| `GET /api/admin/users?q=` 🔒 | — | Accounts by handle prefix (empty `q` lists suspended accounts). Since Round 20 each row is an `AdminUserDto` `{ user, suspended, posts, reports, createdAt, verified, plan, proUntil?, boardExcluded, isAdmin }`: `plan` is `free` or `pro` read through the clock (an expired grant reads `free`), `proUntil` only while the plan is `pro`, and every account action below answers with the same row |
| `POST /api/admin/posts/{id}/hide` / `unhide` 🔒 | — | Hide a look, or show it again (which also clears its reports) |
| `DELETE /api/admin/posts/{id}` 🔒 | — | 204, removes the look, its check, photo and clip |
| `POST /api/admin/comments/{id}/hide` / `unhide`, `DELETE /api/admin/comments/{id}` 🔒 | — | The same for comments |
| `POST /api/admin/users/{handle}/suspend` / `unsuspend` 🔒 | — | A suspended account cannot sign in, reads as missing, and its looks and comments are hidden; a suspended brand's open challenges are closed with no winner (lifting does not reopen them); lifting restores what the crowd had not hidden on its own. A moderator cannot be suspended (400): that is `--unadmin` on the box. Round 20: both share the account actions' per-moderator brake below (429 `error.too_fast` with `Retry-After` over `Limits:AdminActionsPerHour`) and write one audit line, `Admin: {Handle} suspended by {Moderator}` / `Admin: {Handle} suspension lifted by {Moderator}` |
| `POST /api/admin/users/{handle}/verify` / `unverify` 🔒 | — | **Round 20, the Accounts section on `#/admin`.** `200` the `AdminUserDto`, the verified flag set or cleared; idempotent (a write that changes nothing writes no audit line and still answers 200 with the row), allowed on moderators and on yourself. Every account action is behind the `/api/admin` gate — 401 nobody, 429 `error.too_fast` with `Retry-After` over `Limits:AdminActionsPerHour` (a fixed hour per moderator; the limiter runs before the gate), 403 for a suspended account or `error.admin_only`, then 404 `error.user_not_found` for an unknown handle — and writes one audit line at Information under the category `FitCheck.Api.Endpoints.AdminEndpoints`, naming the target and the moderator by handle, never an email or a cookie: `Admin: {Handle} verified by {Moderator}`, `Admin: {Handle} verification removed by {Moderator}` |
| `POST /api/admin/users/{handle}/pro` 🔒 | `{ months }` | `200` the row, the account on Pro for `months` × 31 days from now (1 to 120; 400 `error.pro_months` otherwise, nothing written); 400 `error.pro_billing` when the account has a Stripe subscription (`BillingSubscriptionId` set) — its plan is changed in Stripe, and the row keeps saying `pro` so the moderator sees the state. Logs `Admin: {Handle} on Pro until {yyyy-MM-dd} by {Moderator} ({Months} months)` |
| `DELETE /api/admin/users/{handle}/pro` 🔒 | — | `200` the row, back on Free (200 even when it already was, and then no audit line); 400 `error.pro_billing` under the same guard. Logs `Admin: {Handle} back on Free by {Moderator}` |
| `POST /api/admin/users/{handle}/board-exclusion` 🔒 | `{ reason? }` | `200` the row, the account off every board computed from now on (`AppUser.BoardExcludedAt`, one column: its looks and the fires on them, looks posted later included; the account's own fires on other people's looks still count; a closed week keeps its rows); 409 `error.board_account_excluded` when it is already off. `reason` is trimmed, cut at 200 and kept in the audit line only, `Admin: {Handle} excluded from the board by {Moderator}: {Reason}`, never in a column |
| `DELETE /api/admin/users/{handle}/board-exclusion` 🔒 | — | `200` the row, back on the board; 404 `error.board_account_included` when it was not off. Logs `Admin: {Handle} back on the board by {Moderator}` |
| `GET /api/admin/sponsor` 🔒 | — | `AdminSponsorDto` `{ configured, name?, handle?, prizeText?, url?, urlDropped, handleExists?, handleVerified?, handleIsBrand? }`: what `Board:Sponsor:*` came to, read-only and not rate-limited — the handle as the board links it (a leading `@` dropped), the link as the board would show it, whether it was dropped for not being `http(s)`, and whether the handle is an account here (`handleExists`), a brand account (`handleIsBrand`, absent without an account) and a verified brand (`handleVerified`, never true for a person, verified or not). The Verify button one section up on `#/admin` is the fix for an unverified brand; a personal account is the wrong handle or an account that has not turned on "Brand account", and the card says so. `{ configured: false, urlDropped: false }` while no sponsor is set |
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
  Services/OutfitComparer.cs      ← "Which one?": two photos in one call, the same rules and calibration, PromptVersion cmp-v3
                                  (Round 20: a close call within one point is a real answer, and the occasion decides it;
                                  since the review only when both score 5 or more)
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
  Services/PostReader.cs          posts → DTOs with tags, mentions, featured-by, the before look and the viewer's state, in batches;
                                  since Round 21 each piece's verdict, read from the check's stored feedback by name,
                                  for the look's author and a moderator only
  Services/Localizer.cs           server messages in all four languages (en/he/ar/ru) and Accept-Language matching
  Services/Wardrobe.cs            the pieces a check named, the rows kept from them, and the names that travel to the stylist
                                  (on a check and, since Round 15, on a comparison)
  Services/Taste.cs               the typed reasons, the profile they build and the advisory the stylist is sent; the learning switch
  Services/Funnel.cs              the fourteen-day funnel, the invites and the arrival tallies behind the numbers page
  Services/SpendMeter.cs          what a day of model calls is estimated to cost, the ceiling and the fourteen-day series
  Services/Alerter.cs             the one place a readiness flip, the ceiling, a run of failures or a failed backup shouts
  Services/Doctor.cs              --doctor, --doctor --live and --stripe-check: the nineteen lines, no secret printed
  Services/Readiness.cs           the machine-side half of the doctor, over HTTP, at /readyz
  Services/Transcoder.cs          the background ffmpeg pass that turns a WebM clip into H.264 MP4
  Services/Digest.cs              the Sunday mail: the week a person had, only to a confirmed address
  Services/RenewalRecap.cs        Round 20: the mail three days before a Stripe renewal, and the prune of old webhook event ids
  Services/StylistBack.cs         Round 20: the one note to a guest who signed up while the stylist was resting, once it is back
  Services/TryTipNudge.cs         Round 20: "Did you try the tip?", the day after an unanswered change tip, push only
  Services/TomorrowMorning.cs     Round 20: the morning push that never composes, one receipt row per person per day
  Services/Security/              the headers and the CSP every response is served under (since Round 21 nothing off this
                                  origin: style-src 'self' 'unsafe-inline', font-src 'self'), the Exif strip on every stored
                                  photo and clip, the per-account brake, session revocation
  Endpoints/                      auth, users, checks (+ claim), feedback (the typed reason, "I tried it", the taste card),
                                  compare, wardrobe (+ keep-all, the unkept list, the Pro moment), posts (+ comments), items
                                  (tagging, the item search, the brands list, the out door), board (the week, the hall, the
                                  moderator's exclusion), feed, explore (+ search, tags), challenges, notifications, insights,
                                  today, billing, push (+ the morning switch), blocks, export, admin (+ the account actions and
                                  the sponsor card), metrics, funnel (the Pro-page tally), the entry links (/go/{source}), the
                                  server-rendered public pages (/look, /u, /digest), health
  wwwroot/index.html, app.css     the shell (with the Open Graph and Twitter tags) and the design system: Ring of Fire, see
                                  DESIGN.md (logical properties for RTL; Round 21's plum stage, glass and meter)
  wwwroot/fonts/                  Round 21: the three faces on this origin — fonts.css and ten woff2 subsets of Outfit, Heebo
                                  and Cairo — preloaded by index.html, linked by the landing pages, kept by the service
                                  worker; no font host anywhere
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
  wwwroot/sw.js, wwwroot/icons/   /landing/ navigations through to the network; the shell is orevosh-shell-v8 since Round 21,
                                  with /fonts/fonts.css precached and each face kept the first time it is fetched
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
  private. Since Round 21 each piece the stylist named shows its one-word verdict (works, neutral, weak) as the dot on
  its chip, to the look's author and a moderator only: like the notes, the verdicts are part of the check, and they
  stay with it for everyone else whether the number is public or private (`DECISIONS.md`, review fixes). Deleting the
  post makes the photo private again.
- **Photos are never served by path.** Check photos and avatars live under `Storage:Root`, outside `wwwroot`.
  The post image route (visible posts only), the avatar route and, since Round 19, `GET /api/checks/{id}/image` — a
  private check's own photo, to the person who may read the check (its owner, or the guest whose cookie made it), and
  a 404 to everyone else — are the only doors. `SecurityTests` lists that route as private and asserts nothing else
  under `/api/checks` serves a file.
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
- **Cost control.** Every check is a paid model call, so the caps are the product, and every plan has two: a month
  and a day. Free is 2 a day and 20 a month, Pro is 150 a month with 30 a day as a burst brake (the month is what Pro
  is sold on and what the app quotes; the day is never advertised), 1 as
  a guest, checks and comparisons counted together over a rolling 24 hours and a rolling 30 days (429 with a friendly message and a
  `Retry-After` that names when a permit really frees up: a Free account is told what Pro gives, a Pro account at
  its ceiling just hears the number), `Limits:ChecksPerDay` as the ceiling no plan exceeds, a global ceiling of 1000
  a day across both routes, a 6 MB upload cap, and the client downscales to 1280px JPEG (avatars to 320px) before
  uploading. In-flight calls count; failed model calls do not.
- **Pro is a cap on a real cost, not a feature wall.** Pro raises the daily cap; comparisons and insights stay free
  by default (`Plans:CompareNeedsPro`, and the Pro page names them as benefits only when that is on). The check
  screen says how many checks are left today and offers "Go Pro for more" only to a Free account at its cap. Pro is a flag plus an end date on the account, written by Stripe's webhook,
  by `--pro` or, since Round 20, by a moderator on `#/admin`, never by a request from the person; a lapsed period falls
  back to Free by itself. Stripe stays behind
  `Billing:Provider`: with `manual`, the Pro page shows a note and nothing pretends to charge.
- **Comparisons are private and never postable.** "Which one?" keeps both photos only when the stylist confirmed
  two outfits, serves them to the owner alone, and has no post route.
- **Items are indexed from the stylist's words or the person's, never from captions.** At posting, the item names of
  the check are copied onto the look (lower-cased, up to 8) unless the person tagged the pieces on the post sheet; a
  caption cannot put a look under "black boots". A row keeps saying whether the stylist or the person named it
  (`source`), and the notes stay private with the tip, as the one-word verdict does for everyone but the author and a
  moderator (above).
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
  did it. Since Round 20 an account can be kept off too (`POST /api/admin/users/{handle}/board-exclusion`, one column,
  looks posted later included), with the reason in the audit line only. A week already closed keeps its archive.
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
- **Verification is the owner's hand.** `--verify <handle>` on the box sets the flag, and since Round 20 so does a
  moderator's Verify button on `#/admin`; there is no form and no request a brand can make for it. The check sits
  inside the BRAND mark wherever the account appears.
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
  only a brand the owner ran `--verify` for, or a moderator verified on `#/admin`, carries the check. There is no form
  to ask for it and no process behind it beyond the owner knowing who is behind the account.
- **Stripe has not run against a live account.** Checkout, the webhook and its seven events were built and tested
  against a recording stand-in and signed test events; the first real subscription, renewal, trial and cancellation
  are the proof. Run it in test mode (`sk_test_…`, the Stripe CLI forwarding to `/api/billing/webhook`) before the
  provider is switched to `stripe` on a server people pay on. Since Round 20 event ids are kept for 30 days and a
  replay is answered `replayed: true` and ignored; the dedup is per process (one SQLite file, one instance, so a
  second process on the same database would only be stopped by the primary key on the insert, after doing the work
  twice).
- **The pilot upgrade path is a command *while `Billing:Provider` is `manual`*.** Then the Pro page says Pro is
  switched on by hand, the owner runs `--pro <handle> <months>`, there is no in-app request or cancel, and a person
  ends Pro by writing to the owner (the terms say so). With `stripe`, the Pro page and Settings carry "Manage
  subscription" — Stripe's Billing Portal (`POST /api/billing/portal`) — and that is where a subscription is changed
  or cancelled.
- **The legal pages need a lawyer.** `#/terms` and `#/privacy` (version 6, dated 2026-09-28) are written from what
  the code actually does, in each language, and are not legal advice; the governing-law line is a placeholder
  ("the place where the owner is based") and the contact address `hello@orevosh.app` must exist before the pages go
  live. Have a lawyer review both before launch.
- **Translations need a native review.** Every language after English (Hebrew, Arabic and Russian) was written by the
  builders, the terms and the privacy policy included; a native speaker should read
  each before it reaches people.
- **The screenshots in the brand kit are test fixtures.** The store screenshots and the landing screens show the three
  real looks in a test run of the app (`tools/brand/shoot/kit-shoot.js`: its people, its counts, the test stylist's
  words) and Chromium's fake camera. Replace them with real captures before any store submission (`brand-kit/README.md`); Apple rejects listings whose screenshots do not show the app as shipped.
- **Password recovery needs a mail provider.** An account can carry an email (optional, confirmed by a link) and a
  forgotten password is reset by a link that lives an hour; without `Email__*` settings the app says recovery is off
  and writes the links to its log instead. A reset does not end sessions that are already signed in.
- **Moderation is a queue, not a team.** Moderators (accounts flagged at start from `Admin:Handles`, or with
  `--admin`) see reported looks and comments and can hide, delete and suspend, and since Round 20 verify, grant Pro
  and keep an account off the board from the same screen. All moderators are equal: any of them can grant Pro (money)
  and verify (trust), on themselves included; the audit line and `Limits:AdminActionsPerHour` bound the damage of a
  stolen cookie, and the policy itself is the founder's question. Featured looks are the brand's call with no review
  step.
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
  `scripts/calibrate.py` on real photos before judging scores, and for the steadiness question (does the same photo
  get the same score twice) `tools\eval\calibrate.ps1 -Photos <folder> -Occasion date -Language he` from a Windows
  machine against the live server, which files a dated report under `tools/eval/reports/`.
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
  screenshots; its README lists every file. Change `COPY` at the top of the script and re-run to regenerate. The
  episodes (`brand-kit/episodes/`, `tools/brand/render-episode.js`) come in five variants; the fifth,
  `before-after`, is fed by the app's own exported JSON — two checks and their pair — through
  `tools/brand/lib/before-after.js`, and every end card carries the address `orevosh.com`.
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

**The last polish.** `offline.html` is precached (it came with `orevosh-shell-v6`; the shell is `orevosh-shell-v8`
since Round 21) and answers a navigation with no network when there is no cached shell to fall back on, and every
`/landing/` navigation that fails: the mark, one line, *Try again*;
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
tokens (it does once `Anthropic:PromptCache` is `5m` or `1h`, Round 20; off, the default, they stay at zero). `AnthropicVisionClient` reads `usage.input_tokens`,
`usage.output_tokens`, `usage.cache_read_input_tokens` and `usage.cache_creation_input_tokens` off every answer and
hands them to `Services/SpendMeter.cs`. A call that FAILED at the API (a 4xx or 5xx whose body still carried usage, or a
timeout) is counted too, with whatever is known — somebody billed it. A call that never reached the API (no connection,
no key) counts nothing. The meter writes in its own DI scope, and never throws into the request: a check that already
happened must not fail because a tally did.

**The estimate is an estimate.** `Anthropic:PriceInPerMillion` (2.00) and `Anthropic:PriceOutPerMillion` (10.00) are USD
per million tokens, marked in `appsettings.json` as the owner's to set from their contract; the defaults are the
published list prices for the default model at the time of writing. Cache tokens were priced at the input price until
Round 20, which prices a cache read at a tenth of it and a write at 1.25× (`5m`) or 2× (`1h`), the ratios the provider
bills; the write factor follows the mode in force when the page is read. Nothing here is an invoice, and the page says
so.

**The ceiling.** `Limits:SpendPerDayUsd` (default `0` = off, which the doctor warns about and `LAUNCH.md` tells the
owner to set — 5 USD for the pilot). Once today's estimate reaches it, `POST /api/checks` and `POST /api/compare` answer
**503 `error.stylist_resting`** ("The stylist is resting until tomorrow. Your look is not spent.", in all four
languages, with `code: "stylist_resting"`, which the check screen offers the stylist-back note on) *before* the model is asked: no allowance spent, no guest free look spent, no row stored, no photo written.
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
requires them since Round 17 (see `DEPLOY.md`, "Round 13 — Money"): green used to be printable on an endpoint that
would never be told about a chargeback. A refund only ends Pro when the charge was refunded **in full** — a partial
refund leaves Pro alone and alerts the owner, because a refund does not cancel the subscription and Stripe keeps
billing, so the person would have gone on paying for something they had lost.

| Key | Default | Meaning |
|---|---|---|
| `Anthropic:PriceInPerMillion` | `2.00` | USD per million input tokens, for the estimate only. Set it to your contract's price |
| `Anthropic:PriceOutPerMillion` | `10.00` | USD per million output tokens, same |
| `Limits:SpendPerDayUsd` | `0` | The day's estimated-USD ceiling (UTC day). `0` is off; above it every route that would ask the model answers 503 |
| `Alerts:Webhook` | `""` | https URL taking `{ "text": "…" }` — Slack as-is, and a Discord webhook URL as pasted (`Alerter.SlackShaped` appends Discord's `/slack` endpoint). Environment only: it is a secret |
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
no script — the photo, the score ring as inline SVG, the handle and the intent, the stylist's headline (never the tip),
*Check yours* into the app and *Open in OREVOSH* to `/#/post/{id}`. `GET /u/{handle}` is the same for a person: their
visible looks as a grid. Both carry the Open Graph and Twitter tags a paste into WhatsApp, Telegram, Facebook or X
unfurls: the headline (or *@handle's look on OREVOSH*) as the title, the score and the intent as the description (never
the tip: since the second look at Round 21 the page keeps the post sheet's promise that the tip stays with its author,
for a public grade and a private one alike) and `og:image` pointing at `GET /look/{id}/image`, the one public door
to a look's photo: a post that exists, is not hidden and whose author is not suspended, and 404 for everything else
(`/api/posts/{id}/image` stays the app's own route, cached `private`). The page runs in the look's own language and
direction, not the reader's, and asks nothing off this origin: the brand's faces are named and the system's stack
stands behind them, so nothing is fetched from a font host. `robots.txt` indexes `/look/` and `/u/` and disallows the
API, the client modules, the locale files and the app shell itself (a hash route has nothing for a crawler).
Every arrival is a day tally: `arrivals:look:yyyyMMdd`, `arrivals:look:share:yyyyMMdd` for one that carried
`?via=share`, `arrivals:profile:yyyyMMdd`.

**The share lands there.** The story card's colophon and the share video's end card name the look's public address
(`orevosh.app/look/…`, the host from `/api/config` `publicOrigin`) once the check has been posted; with no public
origin configured they fall back to the address this browser reached the app at, but only when that address is a real
`https` one somebody else could open — behind a quick tunnel that is the only address there is. A `localhost`, a LAN
name (`*.local`, `*.internal`, a name with no dot) or a bare IP prints nothing at all, as before: a card travels, and
such a line on somebody's story would be a lie. On a look, *Copy link* opens the share sheet (or the clipboard) with
that address, carrying the sharer's own `?via` when they are signed in, so a look that travels is also an invite.

**And the invite survives the link.** A share carries `/look/{id}?via=<handle>`, and both public pages hang that same
`?via` back on every way into the app — *Check yours*, *Open in OREVOSH* and the wordmark — with the query before the
hash, since the app reads `?via` off its own address and these pages set no cookie. The value is validated as a handle
first (`?via=share` is the share loop's own marker, never a person) and escaped once. Every one of those arrivals is a
day tally of its own, on the page and again on the hop into the app.

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
  has no word of its own in that list and lands on `Party` (Formal got its own word later in this round: see "The
  check that was interrupted, the camera's microphone, and Formal's own word"). Looks, boards, challenges, the feed
  filter, search and the interests list are untouched: they all still speak the one word.

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/checks` | multipart: `occasion`, `style?`, `note?`, `language`, `image`, `video?` | As before, plus `occasion` and `style` on the answer. `occasion` is one of the six names, case-insensitive (400 `error.occasion_invalid`); `style` is one of the four, or empty for none (400 `error.style_invalid`); `note` is the wearer's free line, ≤ 120 characters (400 `error.occasion_too_long`). **A client from before the split is still understood:** when the form carries `intent`, that one word is split into the pair and its `occasion` field is read as the free line, exactly as it was sent. The presence of `intent` is what tells the two shapes apart |

`GET /api/checks/{id}`, `GET /api/users/me/checks` and the answer to `POST /api/checks` carry `occasion` and `style`
(absent when no style was asked for) beside `intent`. **The wearer's free line is now `note`, not `occasion`** — the
chip took that name. The export's `checks[]` carries `note`, `occasion`, `style` and `tipKind`. `POST /api/compare` kept
the one word through Round 14; since Round 20 the compare form asks the check's two questions and the one word is only a
legacy shape (the API table above).

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
what Streetwear, OldMoney and Minimal used to mean. On the result, `#asked-for` names both. The three empty containers
this round left for later modules (`#tip-feedback`, `#tried-it`, `#wardrobe-offer`) are gone: since Round 20 the
result screen draws `#taste-reasons` and `#tried-action` and `taste.js` fills them (Round 14 — the loop, below).

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
calls `mountResult(container, result, { onStart, onPair, afterUrl })` once after the tip, since Round 20, and this
module fills `#taste-reasons` (the typed answers) and `#tried-action` (`#tried-start`, the primary `btn` "Try the tip,
then show me" with `tried.try_tip_hint`, or `#tried-pair` once the two checks are linked); the Round 13 yes/no `#useful`
row no longer exists on the result screen, because the four typed answers already decide `Useful` on the server. Once a
pair exists, `#share-pair` leads the share row. i18n under `taste.` and `tried.` in all four files.

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
`wwwroot/app/wardrobe.js`, mounted by `views/check.js` right after the `#taste-reasons` and `#tried-action` mounts). A keep offers the next
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
| `GET /api/wardrobe` 🔒 | — | `{ items: [{ id, name, category, keptAt, lastSeenAt, looks: [{ checkId, wornAt, postId? }], keptAs? }], max, toStylist, stylistAvailable }`. The account's pieces, most recently worn first, each with the looks it appeared in (newest first; `postId` only where that check is a visible look of the caller's; `keptAs`, since the review of Rounds 20 and 21, only on a piece renamed since it was kept: the stylist's name it was kept under, lower-cased, so the keep row does not offer it again). `max` is `Plans:WardrobeMaxItems`, `toStylist` is this account's own switch and `stylistAvailable` is whether its plan honours it. A free account sees its whole wardrobe |
| `POST /api/wardrobe` 🔒 | `{ checkId, name }` | `201` the piece, or `200` when it was already kept and this check was added to it (one row per piece per account, matched case-insensitively). `name` must be one the check actually named — the stylist's items, then the accessories it saw. 400 `wardrobe_name_invalid` for an empty name, 400 `wardrobe_unknown_piece` for anything else, 404 `check_not_found` for another account's check or a guest's, 409 `wardrobe_full` at `Plans:WardrobeMaxItems` (never for a piece already kept) |
| `PATCH /api/wardrobe/{id}` 🔒 | `{ name }` | `200` the piece, renamed, keeping its looks and the stylist's name it was kept under, so a later check naming it adds a look to this row and the unkept list never offers it back (review of Rounds 20 and 21). 400 `wardrobe_name_invalid`, 404 `wardrobe_not_found` (which is also what another account's piece answers), 409 `wardrobe_full` when the new name is already another of this account's pieces |
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

## The check that was interrupted, the camera's microphone, and Formal's own word

**A check in flight survives the phone.** The stylist's minute is the longest wait in the app, and a phone can take the
screen away in the middle of it: iOS discards a backgrounded tab, a locked phone suspends the page, an app is swiped
away, a lift eats the connection while the answer is on the wire. The check itself was never in danger — the row is
written and it is the person's — but the answer was travelling to a page that no longer exists, and **a guest's lost
check was their one free look.**

On submit the client writes a marker to `localStorage` (`orevosh.check.pending`): when the wait started, the occasion,
the style, and who was waiting. **Never the photo.** Every read and write is behind try/catch, so private mode and
blocked site data simply have no recovery and the app is otherwise exactly itself. The marker goes the moment the answer
is in hand, either way — the verdict, or a refusal the server actually sent (429, 413, 502). A connection that died
keeps it, because the check may have landed after the page stopped listening.

The next boot, with a marker younger than ten minutes, opens on `#/result` instead of the feed — only from the addresses
an interrupted person lands on (the app's own start_url, a reopened home screen, the check screen a restored tab comes
back to), never over a link somebody tapped — and asks for the check. Three ends: the verdict, drawn as though the
answer had arrived; a check that never landed, which puts the two answers back on the chips with one line, so only the
photo has to come again; or no answer at all, which keeps the marker for the next open. The marker is also dropped when
the person starts another check, and it is never recovered by anyone but the account (or guest) that wrote it.

| Route | What it is |
|---|---|
| `GET /api/checks/latest` | The caller's own newest check, with **no id in the address**, because the client that was holding the id is the thing that died. The rule is `GET /api/checks/{id}`'s, unchanged: the owner's row, or — signed out — the row this browser's own guest cookie made; 404 to everyone else, the same as a missing id, so it says nothing about what exists. `?withinSeconds=` is **how long the caller has been waiting, by the caller's own clock**, and the answer must be younger than that: two durations, each measured by the side that measures it, so a phone whose clock is days out gets the honest 404 instead of this morning's verdict. Clamped to `CheckEndpoints.MaxWithinSeconds` (900). A row the model failed on (`error`) is never the answer — it cost the person nothing (`Spend`) and the screen it would draw talks about the photo, which is not what went wrong. A read: no CSRF header, and nothing for the script policy to allow |

**The camera asks for the microphone only when a clip is what it is about.** `#/camera` opened photo mode with
`getUserMedia({ audio: true })`, which is a second prompt on iOS and, on Android, one site-level camera-and-microphone
question — where a person who blocks the microphone has blocked the camera too, for good, on every later visit. Photo
mode now asks for video alone, and choosing **Clip** re-opens the camera for the microphone, where "and microphone"
is the obvious half of the question; a device with no microphone, or one that was refused, keeps the silent stream and
is never asked again. Because photo mode's stream has no sound in it, the press-and-hold shortcut is a clip-mode
gesture now: a hold in photo mode takes the photo on release rather than recording a silent clip.

**The viewfinder shows the frame the shutter keeps.** The stage painted the stream `cover` over a full-bleed box while
`takePhoto` keeps the whole camera track, so on any phone taller than the camera's frame the person framed one photo and
got a wider one — the dotted guide was marking a rectangle that was not the one being kept. The stage is `contain` now:
the frame sits on black the way a phone's own camera app shows one, live and preview agree, and the 4:7 guide is a
miniature of the frame rather than a third aspect. The capture is untouched — the 4:5 crops downstream and the share
video need that width.

**A wedding has a word of its own.** Round 14 added the Formal *occasion* and had no one-word value for it, so
`StyleIntents.Legacy` folded it onto `Party`: a wedding look said PARTY on its card, its board, its share card and its
share video. `StyleIntent.Formal` is that word — appended to the enum (`Checks.Intent` is TEXT, so nothing renumbers),
with `intent.Formal` in the four locale files and `public.intent.Formal` in the four server dictionaries for the shared
look page. Boards, search, challenges and comparisons read the enum and learned it for nothing; a database
written before the split cannot hold the word, so no migration changes. One number moved with it: the interests ceiling
(`UserEndpoints.MaxInterests`) was the literal 8 and is now however many words there are, because "pick every style" has
to stay possible and the list is deduplicated anyway — `error.interests_invalid` carries the number (`{0}`) in all four
server dictionaries instead of spelling it. The client's own `INTENTS` list in `app/core.js` names all nine, so
Formal has its chip in the feed filter, the interests list, a new challenge and the compare screen, and a Formal
winner's board badge reads the word (`intentLabel`) rather than the raw value.

Tests: `CheckRecoveryTests` (the signed-in and guest recoveries, a guest who cannot read another guest's, a wait that is
over, a window nobody can widen, a check that never landed, an error row that is not a verdict, and the read that needs
no header), `LatestCheckRuleTests` in `SecurityTests` (the one private route with no id, and why its rule is not in
`IdorEnumerationTests.Rules`), and `IntentSplitTests` for the word.
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

## Round 19 — Tomorrow: an outfit from the wardrobe that built itself

"What should I wear?", answered from the pieces a person already kept from their checks, each one shown as the photo of
them wearing it. Nothing is photographed for it: the wardrobe filled itself one keep at a time (Round 14), and Tomorrow
is what it was for. The screen is `#/tomorrow`, reached from the profile's links, the wardrobe's header and the keep row
after a check once two kinds of piece are in.

**What keeps it honest.** The model is handed a closed list of refs (P1..Pn) over the wardrobe rows — the plan's slice
of them, 12 on Free and 40 on Pro, most recently worn first — and a tool whose enum is exactly those refs (and, for the
one kind of piece the wardrobe lacks, exactly the kinds they do not own). Everything it answers is checked again in code
by id: an unknown ref is dropped and counted, one of each kind is kept and at most two accessories, a dress sends the top
and the bottom home, and a sentence that names a body, a number the figures never gave, or an offered piece it did not
pick is replaced by a template built from the rows. What reaches the person is the wardrobe row's own name, never a
string the model wrote. The photo per piece is chosen in code too: the newest photographed check the piece was kept
from, a different check per piece where the wardrobe allows it, and one photo drawn once when every piece came from the
same look.

**What keeps it cheap.** A planned outfit is one stylist call and is counted exactly where a check is: a free account's
one shared day (and `Plans:FreeSuggestionsPerDay`, `1`, of it may be outfits), a Pro account's own third bucket
(`Plans:ProSuggestionsPerDay`, `10`), the same month for both, and the global ceiling. A stored answer to the same
question on the same day is handed back for twelve hours instead of a new call — unless the person asked for another
idea or thumbed the last one down — and a wardrobe that changed since is shown as stale with a Refresh, never composed
again on its own. Below two pieces of two kinds nothing is asked at all. The brake sits inside the same in-flight
reservation a check uses, so parallel taps cannot slip under it. A failed call is stored and counts for nothing.

**The weather.** With the person's leave — one tap on the Tomorrow screen, the browser's own prompt — the phone sends its
place rounded to about a kilometre with the request, the server asks Open-Meteo for the day's high, low, chance of rain
and sky, and the figures go to the model as words the code chose. No coordinate is ever stored or logged. The forecast is
optional and the outfit is never delayed for want of it. Open-Meteo's keyless service is for non-commercial use;
`Weather__ApiKey` moves the calls to its paid host, and `--doctor` says so while Stripe is on and the key is empty.

**The loop.** The thumbs on an outfit use the check's own vocabulary (`worked`, `didnt_work`, `not_my_style`,
`dont_own`) and are written onto the row's four columns, so the taste profile reads one shape from two tables: the
counts grow, a turned-down outfit's sentence and note join the advisory under the same two-reason rule, and the outfits
themselves are named as combinations — "Outfits they said yes to", "Outfits they turned down" — on the next outfit and
on the next check alike. "I don't have one of these any more" removes the piece from the wardrobe at the source.
"Wearing it? Check it" carries the outfit into a check, and the stored check links back to it and records the yes.

| Method & path | Body | Returns |
|---|---|---|
| `GET /api/tomorrow` 🔒 | — | The screen's first paint, never a model call: `available`, `stylistOn`, `needsPro`, `have`/`haveKinds` against `needs`/`needsKinds`, `offered` (how many pieces the model would be shown), `strip` (up to 8 pieces with `photoUrl`), `recent` (the last 5 outfits with `stale`), `leftToday`/`capToday`/`leftMonth`/`capMonth`, `defaultOccasion`/`defaultStyle` |
| `POST /api/tomorrow` 🔒 | `{ occasion, style?, when: "today"\|"tomorrow", today: "yyyy-MM-dd", fresh?, lat?, lon? }` | `201` a `SuggestionDto` (`pieces[]` each with `itemId`, `name`, `category`, `photoCheckId`, `photoUrl`, `worn`; `sentence`; `weather?`; `gap?`; `seq`; `reused`; `stale`; `counted`; the four left/cap numbers), or `200` with `reused: true` when a stored answer serves, or `200` `{ have, haveKinds, needs, needsKinds }` when the wardrobe is too thin. 400 `error.intent_invalid` (unknown `occasion`) or `error.invalid_request` (unknown `style` or `when`); 403 `error.pro_required` where `Plans:TomorrowNeedsPro` is on; 404 `error.tomorrow_not_found` where `Plans:TomorrowEnabled` is off; 409 `error.tomorrow_stylist_off`; 429 `error.tomorrow_limit`/`error.tomorrow_free_limit`/`error.plan_limit`/`error.month_limit`, or `error.rate_limited_global` at everybody's day ceiling; 502 `error.tomorrow_failed` (nothing spent); 503 `error.stylist_resting` for a free account at the ceiling |
| `POST /api/tomorrow/{id}/useful` 🔒 | `{ reason, note? }` | The check's own thumbs body and guards (`FeedbackEndpoints.ParseUseful`); 404 `error.tomorrow_not_found` for anyone but the owner |
| `GET /api/checks/{id}/image` | — | The photo of a private check, to whoever may read the check (its owner, or the guest whose cookie made it), `Cache-Control: private`; 404 to everyone else and for a check with no file |

Every `/api/tomorrow` route answers 404 while `Plans:TomorrowEnabled` is off, and `/api/config` publishes the plan
numbers as enforced (`plans.tomorrow`, `plans.tomorrowNeedsPro`, `plans.proSuggestionsPerDay`, `plans.freeSuggestionsPerDay`,
`plans.suggestionMinPieces`, `plans.suggestionMinCategories`). The numbers page gains a Tomorrow block (outfits, worn
rate, reuse rate, refs the model returned that were not in its list, sentences the template replaced, the reasons), and
the wardrobe block's "I do not own that" now counts outfits too. Migration `Round19Tomorrow` adds `Suggestions` and
`SuggestionPieces` and a nullable `Checks.SuggestionId`; the server strings are in `Localizer.cs` and the screen's in the
four `i18n` files.

Tests: `TomorrowTests` (a hallucinated garment never reaches the outfit; the tenth tap is refused exactly where the tenth
check would be; the ref and gap enums are exactly the wardrobe; a body word or an invented number replaces the sentence;
too few pieces or kinds make no call; the wardrobe switch and the guest; the kill switch and the wall; five taps pay once
and another idea pays again; a thumbs-down breaks the cache and a new piece marks it stale; a failure spends nothing and
a refusal counts; free's brake inside its shared day and Pro's own bucket; the month; the in-flight reservation; the
weather reaching the model only when the server fetched it, rounded and never stored or logged by the server; the photo per piece; the thumbs;
the loop closing from a check; the advisory and the numbers page), `WeatherTests`, and the doctor, plan, security and
database tests that grew with it.

## Round 20 — the wedge

The round is one sentence from the judges about beating the competitor: an honest, steady number on the real outfit in
fifteen seconds with no account, and then the real before/after as the thing people share. Everything below serves one
of the two halves — the first look faster, more honest and reachable from a link in a bio, and the loop that turns a
tip into a second photo, a pair, a share and a morning habit — or pays for it, or lets the founder run it without a
terminal. Nine builders shipped nine moves on one skeleton (`b8cbadb`: the migration `Round20Wedge`, the DTO fields,
the counters and the i18n keys every move shares), in this order: billing, the wait, compare, keep, tried,
distribution, the morning loop, owner tooling, and the content builder's, which has its own section below. The API
table, the Configuration table and the metrics paragraph above carry every route, setting and number; this section says
what each move is and how it fits.

**Billing: replays ignored by id, a yearly price, a no-card trial, the renewal recap** (`814af1e`). `POST
/api/billing/webhook` keeps the event ids it has handled (`StripeEvents`, pruned after 30 days) and answers a replay
`200 { received: true, replayed: true }` without touching the account; the id is recorded in the handler's own save
(since the review; it used to be a second save after it) and under a process-wide gate, so a handler that threw before
saving is retried by Stripe, a grant never stands without its id, and a parallel burst of one id is handled once. `POST
/api/billing/checkout?interval=year` sells the yearly price where the page could have offered it (`Billing:StripeYearlyPriceId`
and a yearly amount in the reader's currency: `Plans:ProYearlyPriceAmount`, `Plans:ProYearlyPrices`; 400
`error.billing_interval` otherwise), and `checkout.session.completed` grants 368 days for it (`PaidYear`) where a month
is 35. `Plans:ProTrialDays` opens Stripe's own trial, once per account, to one that never went through Checkout here
(no `BillingCustomerId`): the session asks for no card, a trial that ends with no card simply ends
(`customer.subscription.deleted` arrives, which is why the webhook endpoint needs all seven events), and the webhook
grants the trial's days plus the three-day slack, never a paid month. `GET /api/billing/state` says `yearly` and
`trialDays`, and `/api/config` publishes `proYearlyPriceAmount`, `proYearlyPrices`, `yearly` and `proTrialDays`. The Pro
page draws a month/year toggle (`#pro-interval`, `#pro-interval-month`, `#pro-interval-year`, `aria-pressed`) only when
Stripe is live and a monthly price and a yearly amount both exist in the reader's currency; the year shows "{price} a
year", "Billed once a year. Cancel any time." and "Save {pct}% against paying monthly" (`#pro-saving`) when the saving
computed from the two numbers is above zero, and Go posts `&interval=`. The trial line ("{days} days free, no card
needed", the `timer` icon) is drawn only when `plans.billing`, `plans.proTrialDays > 0` and the account is eligible
(signed out, or `GET /api/billing/state` says `trialDays > 0`), and the button then reads "Start {days} free days".
Since the review the toggle is drawn only when the year also saves something (a yearly amount at or above twelve months
is not offered), and the trial line's hint (`#pro-trial-hint`, "Then {price}. Cancel before the trial ends and nothing
is charged.") names the price on the interval the toggle stands on — "$290 a year" once Yearly is pressed, since
Checkout then sells the year — and is left out when no price is published.
`Services/RenewalRecap.cs` (hosted `RenewalRecapService`, hourly, first pass at start) mails a Pro account three days
before Stripe charges it — the charge date, the comparisons it decided, the outfits it planned and of them wore, the
tips that named something it owned, in its language (the date too), with a link to `#/settings` — once per period
(`AppUser.RenewalRecapUntil`), only with a `BillingSubscriptionId` that will really charge, a confirmed address and mail
configured; it is transactional, so it ignores `DigestOn` and carries no unsubscribe link. Since the review the charge
is the period Stripe bills (`AppUser.BillingPeriodEnd`: a Checkout's month, year or trial from then, then whatever
`customer.subscription.*` and `invoice.paid` name), not the end date, and `AppUser.BillingRenews` keeps the mail from a
subscription set to cancel at the period end, a trial with no card and a declined renewal; an account from before
those columns is read as Round 20 read it until Stripe's next event. Since the second look both are taken in the order
Stripe created the events (`AppUser.BillingNotedAt`, the last one read): a late or out-of-order subscription event older
than that moves neither, and a Checkout's first charge counts from when it completed, not from a late delivery. The doctor's `billing` and `stripe-yearly`
lines and `Billing:StripeBaseUrl` are described with the doctor above.

**The wait and the viral day** (`b53b996`). The line under the flame on a check or a comparison now changes with the
elapsed time: "Looking at the look" / "Looking at both looks" for the first three seconds, "Reading the pieces" until
eight, "Weighing the occasion" until fifteen, then "Still with you - a long answer takes a moment"; with a clip the
stages start after the "sending" swap, and Tomorrow keeps its skeleton. The numbers page gains `p95LatencyMs` beside the
average, over the same population. `Anthropic:PromptCache` (`off` by default, `5m` or `1h`) puts a cache breakpoint on
the shared rubric block of a check or a comparison — the system prompt is an array of text blocks on every call, on or
off, so there is one wire shape; the taste advisory is its own uncached block after it, and Tomorrow and the recap never
carry one — and `Services/SpendMeter.cs` prices a cache read at a tenth of the input price and a write at 1.25× or 2×;
the doctor's `anthropic-url` line names the mode and the money tiles show the reads and writes. A guest refused at the
day's ceiling (503 `error.stylist_resting`) is now offered a signup that promises exactly what the code does
(`#resting-offer`, `#/signup?back=stylist`, `notifyStylistBack` on the body): while the ceiling is really closed a
`Counter` row `stylist_back:{userId}` is written, and `StylistBackService` (every five minutes, first pass at start)
turns it, once the ceiling is open and inside the `Push:TryTipDayStart`–`TryTipDayEnd` local hours, into one in-app line
of type `stylist_back` (pushed to a subscribed browser), one mail only to a confirmed address when mail and a public origin
are configured, and deletes the row, in a save of its own before the mail, so a failed save or a shutdown never mails
anyone twice; the welcome screen offers the push step when me says `stylistBackAsked` and the browser can take one, and
says the app's usual pings come with it. The offer names the mail only where the server can confirm an address and
(since the second look) the phone only where it has push keys, and appears only on the ceiling's own 503
(`code: "stylist_resting"`).

**Compare: the two questions, the close call, the Pro nudge** (`d00260c`). `POST /api/compare` asks the check's two
questions (`occasion`, `style`, the free line as `note`) and keeps the one word as a legacy shape; `ComparisonDto` appends
`occasionKind` and `style` beside `occasion` (the note, under its old name) and `feedback.close` says a close call,
derived on the server from the two scores. `OutfitComparer` moved to `cmp-v2`: a close call is a real answer, both
outfits work within a point, the occasion decides it, never the style, never a coin flip, and the schema is unchanged.
The free day's 429 on both stylist routes carries `code: "plan_limit"` — the only `code` any error body has — so the
compare screen can offer Pro there and nowhere else: `#cmp-pro-nudge` with `#cmp-go-pro` (`#/pro?from=compare`), drawn
only for a non-Pro account on that code while `plans.proComparesPerDay > 0`. The Pro page counts where it was opened
from (`POST /api/funnel/pro-opened`, `from` `compare` or `wardrobe`, once per arrival, signed in), appends
`&return=compare` to Checkout when it came from a compare, and lands the paid person on `#/compare?ready=1`, which toasts
"Pro is on. Two photos, one answer." and opens slot A's media sheet on the camera row. Each slot has its own sheet
(`#cmp-media-camera`, `#cmp-media-library`, no clip); the camera is borrowed through `check.js`'s
`cameraReturn.handoff` and cleared on every exit, so a stale handoff can never feed a check's photo into a compare slot.
`#cmp-asked` names the occasion and the style, and "Both work. {outfit} edges it" replaces the verdict line when
`feedback.close`. The numbers page gains `proFromCompare` and `proFromWardrobe` as funnel columns. Since the review
(`cmp-v3`) a close call also needs both scores at 5 or more, so two outfits that both fail the occasion are a plain win
with a reason that says neither works yet; a Checkout cancelled on the way from a compare keeps `&return=compare` on the
next try from the same page; and the funnel route loads the account, so a suspended one is refused and counts nothing.

**Keep: fill the closet faster without photographing it** (`a518457`). The keep row after a check offers "Keep all {n}"
(`#wardrobe-keep-all`) as a third answer whenever more than one piece is left; `POST /api/wardrobe/keep-all` keeps every
piece the check named in one request (200 always on success, 409 `error.wardrobe_full` only when nothing at all could be
written), and the row then reads "{n} pieces from this look are in your wardrobe." with "Your wardrobe is full at {max}
pieces, so the rest stayed out." (`#wardrobe-keep-all-full`) when the cap kept some out, then "See it" and the payoff. `#/wardrobe`
gains "Keep from an older look" (`#wardrobe-unkept`, `.wardrobe-unkept-keep`), fed by `GET /api/wardrobe/unkept` over the
last `Plans:WardrobeUnkeptChecks` (20) OK checks — after the list, or before the "Check a look" button on an empty
wardrobe, and a failed read hides the section. The Pro moment is the server's fact (`WardrobeDto.proMoment`,
`Plans.WardrobeProMoment`: `Plans:WardrobeNeedsPro` on, a free account, strictly more pieces than
`Plans:WardrobeNamesToStylist`): one true line with Go Pro (`#/pro?from=wardrobe`), drawn under the keep that ends the
row (`#wardrobe-keep-moment`, `#wardrobe-keep-moment-go`) and in place of the plain Pro notice on `#/wardrobe`
(`#wardrobe-moment`, `#wardrobe-moment-go`), once per tab (`sessionStorage` key `orevosh.wardrobe.moment`), and it says
"all of them" only while the count fits `proSees`. `POST /api/wardrobe/moment` tallies shown and go only while the
moment is true for the caller. The wardrobe block on the numbers page gains `keepAll`, `momentShown`, `momentGo`,
`momentGoRate` and `piecesPerActiveMedian`, eight tiles in all.

**Tried: the loop as the primary action, the pair as the first share, the day-after nudge, the public pair**
(`8769645`). The result screen calls `taste.js` `mountResult(container, result, { onStart, onPair, afterUrl })` after the
tip and draws `#taste-reasons` and `#tried-action`: `#tried-start` is the primary `btn`, "Try the tip, then show me",
with "Change the one thing, take a new photo, and see both side by side. The second look is scored on its own." under
it; a tap arms the attempt and takes the retake path into the media sheet, the second check is an ordinary check, and
the pair is written afterwards. The Round 13 yes/no `#useful` row is gone from the result screen, because the four
typed answers already decide `Useful` on the server (`TipReason.Landed`), so the Round 13 rate keeps its meaning. A keep
tip draws no tried block on either screen. Once a pair exists `#share-pair` (the primary `btn`) leads the share row and
the pair block carries a secondary `.pair-share` button; the share is tallied by `POST /api/checks/{id}/tried/shared`
on the two `before_after_shares` rows, and `/tried` and `/tried/prefer` now return `postId` on both sides so the post
sheet can preselect the before. `GET /look/{id}` renders `<section class="pair">` after the meta when the post has a
`BeforePostId` that is the same author's, not hidden, with its photo: two figures captioned "Before" and "After" under
a "One change" heading, the two numbers (`<b class="pair-n">`) only when neither post keeps its score private, and no
section at all (the page still 200) when the before is hidden or deleted. `Services/TryTipNudge.cs` (hosted
`TryTipNudgeService`, hourly, first pass at start, log line `TryTip: {N} nudged`) writes, inside `Push:TryTipDayStart`
to `Push:TryTipDayEnd` local hours, one `try_tip` notification and push per check whose change tip nobody answered and
that is in no pair (an "I tried it" link, or a post whose `BeforePostId` names this check's post or that this check's post
carries), `Push:TryTipAfterHours` after the verdict and for `Push:TryTipWindowHours` after that, to an
account that is not suspended and has a push subscription at query time, at most one per person a day (the newest
unanswered check carries it); the row is stamped with the scheduler's clock so the once-a-day rule reads it back against
the same clock. Its tap lands on `#/checks?try=<checkId>`, which scrolls to that check and focuses its `.loop-start`,
and the settings push copy names the nudge. The stylist block gains `triedPairs`, `triedPer100Ok`, `tryTipNudges` and
`nudgedThenTried`.

**Distribution that can be counted** (`0cf2811`). `GET /go/{source}` is the short address for a bio, a group or a
poster's QR code: one arrival counted per allowlisted source (`Funnel:Sources`, ten by default, `tiktok` and the other
long names resolving to their short codes), a 302 into the check screen with `?src=` on the query, an unknown word sent
to the landing page uncounted, crawlers redirected but not counted, and never a cookie. The client keeps the word in
`localStorage` (`orevosh.source`, `invite.js`), sends it with every guest check while kept and spends it on the signup;
`POST /api/checks` (a guest's) and `POST /api/auth/signup` store it on `OutfitCheck.Source` and `AppUser.Source`. Since
the second look at Round 21 a browser somebody is signed in on forgets it (and a kept invite) as soon as `/me` answers,
which is what the privacy page says: kept until a signup. The installed app
sends `X-Orevosh-Launch: standalone` once per device-day on its first call, counted on `/api/config` only. The numbers
page's funnel gains `standalone` per day and the per-source table ("Where people come from": arrivals, guest checks,
signups, first posts). Copy link on a look page copies the share sentence above the address and the share sheet sends
the same sentence (the Hebrew reads as a challenge, ending in a question mark); the result screen's Share sends the
sentence plus the look URL once the look is posted. Inside another app's browser (Instagram, Facebook, TikTok) the app
draws the one-time `#inapp-hint` — "You're in another app's browser", "Open OREVOSH in Safari or Chrome to keep it on
your home screen." — instead of any install advice, and a TikTok webview no longer gets Add to Home Screen. `sw.js`
passes `/go/` navigations to the network and `robots.txt` disallows `/go/`.

**The morning loop behind a switch** (`e1390d8`). `Services/TomorrowMorning.cs` (hosted `TomorrowMorningService`, every
fifteen minutes, first pass at start) sends, while `Plans:TomorrowMorningPush` is on, one push a day at
`Plans:TomorrowMorningHour` in `Board:TimeZone` (a three-hour send window) to each account whose own switch
(`AppUser.TomorrowPushOn`, `GET`/`POST /api/push/morning`) is on, that has a push subscription, a wardrobe of two kinds,
nothing composed today already and something left to spend, in the compose route's own gate order so a tap never lands
on a foreseeable refusal; it never composes and never asks the model. The push (`tomorrow_morning`, "Your outfit for
today is one tap away" in the recipient's language, tag `tomorrow_morning:<handle>` so yesterday's undismissed ping is
replaced) has no activity row; its receipt is a `TomorrowPushes` row (unique per person and day, saved before the push
goes, so a crash costs one morning and never a double), and the tap on `#/tomorrow?from=push` stamps it opened once.
Settings draws the switch (`#morning-section`) only when `/api/config` says `plans.tomorrowMorningPush`, checked only
when the server offers it and this browser is subscribed, otherwise unchecked and disabled with "Turn on notifications
above first." (since the review fixes it follows the notifications switch above as it changes, so turning that on
unlocks it without leaving Settings). A tap on the push opens the screen on Today, whatever pill was pressed last, and
does not save that choice. The Tomorrow block gains `pushesSent`, `pushesOpened` and `openRate`, and the doctor's
`plans` line ends with the morning push. The flag is off by default and stays off until the worn rate says planned
outfits get worn (`DEPLOY.md`).

**Owner tooling without a terminal** (`9a61599`). `#/admin` gains an Accounts section: search a handle and act on the
row — Verify / Unverify, Grant Pro (a sheet with 1, 3, 6 or 12 months; 2 since the review fixes, the founding members'
gift) / Remove Pro, Exclude from board / Put back, Suspend / Lift (no Suspend on another moderator's row, which the
server refuses) — through six new routes and the two old ones, every one behind the moderator gate, one audit line each
under `FitCheck.Api.Endpoints.AdminEndpoints`, and one shared per-moderator brake (`Limits:AdminActionsPerHour`, 120).
The screen refuses a Pro grant or removal on an account that pays through Stripe (400 `error.pro_billing`); the
command does not. The account exclusion is one column (`AppUser.BoardExcludedAt`) that `Board.ComputeAsync` honours for
every open week, so looks posted later stay off too. `AdminUserDto` carries `verified`, `plan`, `proUntil`,
`boardExcluded` and `isAdmin`, and every action answers with the row. `GET /api/admin/sponsor` feeds a read-only
"Sponsor of the week" card that says what `Board:Sponsor:*` came to, warns when the link was dropped or the handle is
a personal account or not yet a verified brand, and reloads with every action, so a Verify tap on the sponsor's handle
is seen to clear the warning. The commands stay as the terminal fallback and the browser test proves both doors.

**Background services.** Four hosted services joined `DigestService`, each a worker class beside a `BackgroundService`
wrapper and each idempotent by a row rather than by its schedule. `TryTipNudgeService` wakes hourly and, inside the
server's quiet hours, writes the day-after nudges described above; `StylistBackService` wakes every five minutes, does
nothing outside the try-tip nudge's local hours, reads the `stylist_back:` rows before it asks whether the ceiling is open
(so a pass with nothing to do never touches the meter), and keeps each promise once, committing each row's removal
before its mail; `RenewalRecapService` wakes hourly, prunes webhook event ids older than 30 days
whether or not mail is on, and, with mail and a public origin configured, sends the pre-renewal recap once per period
(`RenewalRecap: run at …, N recaps sent, M failed, K old Stripe events pruned`; a failed send is tried again next hour);
`TomorrowMorningService` ticks every fifteen minutes, logs its global reasons for doing nothing at Debug (the flag is off
on most servers) and its run at Information once a morning (`TomorrowMorning: run at … for {Day}, {Sent} sent, {Skipped} skipped (due …)`:
the day's first pass inside the window, and a later pass only when it pushed someone; the other quarter-hour passes are Debug).
None of the four ever asks the model.

**Tests.** `BillingTests` (the replay ignored by id, a parallel burst handled once, the yearly session and its refusal,
the trial offered once and granted as days), `RenewalRecapTests`, `StylistBackTests`, `TryTipNudgeTests` (the window, one
a day, the quiet hours, the newest check carrying it), `DistributionTests`, `TomorrowMorningTests` (seven: the right
accounts and never a compose, the flag, the window and the switches, the row before the push and the second process
giving way, the open marker, the recipient's language, DST and the hour parsing; an eighth since the review fixes, the
run line once a morning, and the push's time to live is asserted with the right accounts), the compare questions and the close
call in `CompareTests` and `OutfitComparerTests`, keep-all, the unkept list and the Pro moment in `WardrobeTests` with
the wedge numbers in `MetricsTests`, the tried share and the `postId` in `TriedTests`, the account actions, the audit
lines and the sponsor reader in `AdminTests`, `Round20SkeletonTests`, and `tools/brand/test/before-after.test.js`; the
browser test's Round 20 steps are listed under "Run the tests" above.

**Known limits.** The webhook dedup is per process: two API processes on one database would both pass the lookup and
only the primary key would stop the second record, after the work ran twice (one SQLite file, one instance, as
everywhere else on this page). The Pro moment's once-per-tab memory is `sessionStorage`, so a second tab shows and
counts it again (the rate stays honest, "go" is counted the same way), and the wardrobe records no refusal, so a piece
passed over resurfaces in the unkept list. Two parallel keep-all requests can both pass the cap count, the same exposure
as the single keep route, held off from one tab by the client's busy flag. The nudge's quiet hours and the morning
push's hour are server-wide (`Board:TimeZone`), not per person; a pilot user abroad may be pinged at an odd hour, and the
person's switch is the remedy. The morning sender does not check the money ceiling or the in-flight reservation, on
purpose: at the ceiling a free tap answers 503 unless a stored answer serves. All moderators are equal, so any of them
can grant Pro and verify, on themselves included; the audit line and the per-moderator cap bound a stolen cookie, the
policy is the founder's. `tools/eval/calibrate.ps1` was verified under `pwsh` 7 (a dry run, an empty folder, a run
against a closed port) and not yet under Windows PowerShell 5.1 itself, whose choices (BOM, CRLF, the masked password)
follow `tools/deploy/fly-deploy.ps1`; since the review the password reaches node in `OREVOSH_EVAL_PASSWORD` rather than
as an argument, which 5.1 would not have quoted. The social PNGs in `brand-kit/social` were regenerated with the local fallback
fonts because the font host was unreachable from the sandbox, so a re-run on a connected machine may shift them by a
pixel.

## Round 20 — the wedge: content and documents

The episode renderer gains a fifth variant, `before-after` (17 s): the same look twice — the score, the one tip, the
new score with a delta chip, and *what changed* — fed only by the app's own JSON (the export from Settings, a saved
`GET /api/checks/{id}`, the pair from `GET /api/users/me/tried`) through the pure module `tools/brand/lib/before-after.js`,
which mirrors `Taste.Changes`, reads the intent's word from the app's `i18n`, and refuses what `POST /api/checks/{id}/tried`
refuses; `--list-checks <export.json>` prints the scored checks with their ids. The end card of every episode now
carries the address `orevosh.com` instead of *coming soon* (all shipped files re-rendered once; the two hand-cut
teasers are a follow-up), `CONTENT.md` and `MARKETING.md` point every CTA at the address and the `/go/<source>` links
(`tiktok`, `instagram`, `story`, `dm`), and `CONTENT.md` gains Format 6. `tools/eval/calibrate.ps1` runs the
steadiness pass from Windows PowerShell 5.1 against the live server with a Hebrew verdict line and a dated report
(`stylist.js --report <file>` keeps the rows beside the tables). Tests: `tools/brand/test/before-after.test.js`
(`node --test`), `ExportTests.Export_check_carries_what_the_renderer_reads`, the CI steps *Calibration wrapper parses*
and *Render the before/after cover*, and the browser test's `before-after` step against the real API.

## Round 21 — the look

A visual redesign, and nothing else: the plum stage and its aurora, glass cards lit in their occasion's glow, the score
ring as a meter with the numeral read first, the stylist's pieces as name chips on the photo with their works / neutral
/ weak dot, one pastel per occasion, the result in four moments with the tip as its one warm panel and the primary door
inside it, the keep row in mint, Tomorrow as a lit card with the forecast as one amber pill, a warm Today strip, empty
states under the mark, a 2px press and screens that rise in. **`DESIGN.md`, "Round 21 — the look", is the whole of it** —
the tokens old to new, every component by stylesheet section, the meter, the fonts, the motion, what did not change and
what is still open — and `DECISIONS.md`, Round 21, says why each choice went the way it did. Six commits: `c38f879` (the
stage and the type), `98fceed` (the glass, the meter, the press), `ca3cf73` (the pieces and the result), `9e454ac`
(Tomorrow, Today, the empty states), `c884748` (the motion, the fonts, the shell) and `901a1d4` (the proofs). No route,
setting, migration or i18n key was added.

**What changed outside the stylesheet.** `PostItemDto` gained `verdict` (the API table above: `GET /api/posts/{id}`, and
every feed card, which reads the same shape), read by `PostReader` from the check's stored feedback — since the review,
only for the looks whose author is reading, or for a moderator in the queue, so nobody else's feedback is parsed. The fonts are served from `/fonts` on this origin; the Google Fonts link and `fonts.js`
are gone from the app and the landing pages. The security policy allows no host off this origin: `style-src 'self'
'unsafe-inline'`, `font-src 'self'`. The service worker is `orevosh-shell-v8`, so an installed app takes the new shell the
next time it is opened. `theme-color`, the manifest's colours, the offline page and the server-rendered public pages take
the stage colour, and the share card and the film paint the same aurora.

**The proofs made before the last commit.**
- `dotnet test`: 1168 passed, none failed.
- The browser test ran end to end (`E2E OK`) with its queries untouched.
- Renders on a 390×844 phone screen at twice the pixels: the result, the feed, Tomorrow, the check, a look, Explore, the
  profile, Pro and Today in English; the result, the feed, the check, Tomorrow with its fold open, Tomorrow's empty
  state and an empty Home in Hebrew; the check, the result and Tomorrow in Arabic; the result with its photo darkened
  like a selfie under a weak bulb; the result with every font blocked, in the system face. On the result in each
  language, a report of the faces: only files under `/fonts` were asked for, each once, Cairo only in Arabic; and the
  landing pages asked nothing of any other host.
- Contrast, every text pair composited over its real ground, all at 4.5:1 or better: `--ink-3` 7.4:1 on the stage, 6.6
  on a surface, 5.7 on a raised one; `--accent-ink` 10.5–15.1:1 on the eight pastels; a pastel as tag text on its own
  wash 6.3–9.0:1; the weak verdict word and a lit fire count 5.1:1 in `--fire-ink`, which exists because `--fire` read
  4.1:1 there.
- The feed scrolled to its foot under a 6x CPU throttle with 14 cards in their 60px glow: 17ms a frame at the median and
  at the 95th percentile, one frame over 50ms in 352. The glow stayed.
- Reduced motion, measured the instant each screen appeared: every result section and every card at full opacity, the
  ring's arc already at its score. Earlier runs caught the cards at opacity 0 and the arc at 0° on that first frame, and
  that is why the entrances are off outright there.
- The three piece chips on the result's photo fit two rows at 390px in Heebo.

**Known limits.** The landing pages and the brand kit are on the new stage with screens re-shot from this app
(`tools/brand/shoot/kit-shoot.js`, `LAUNCH.md` 3.2), but they are a test run's captures, not a phone's; the share card
and the film draw the full ring, not the meter (the public look page's SVG ring
became one in the review); a look with five or more pieces shows four chips and no count on its card;
whether the arc should fill from the other side in Hebrew is left for a Hebrew reader (`LAUNCH.md` 1.8, step 10). A
posted look's per-piece verdicts reach its author and a moderator only, and anyone else's card draws the names with no
dots: the review settled it that way (`DECISIONS.md`, Round 21, review fixes).
