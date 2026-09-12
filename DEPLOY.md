# Putting OREVOSH on the internet

Two ways to run the app for real people. Both start from this repository, build the same image from the same
`Dockerfile`, keep the database and the media folder on one volume, and use the same maintenance commands; they
differ in who looks after the machine.

| | Fly.io | Your own server (VPS + Docker) |
|---|---|---|
| Effort | About 15 minutes from a terminal; no server to manage | About an hour the first time; you look after a Linux box |
| Cost | About 5 USD a month: one small machine and a 3 GB volume | About 5 EUR a month for the VPS, plus a domain |
| Domain and HTTPS | `https://<name>.fly.dev` works at once; your own domain is one command | Your own domain from the start; Caddy fetches the certificate |
| Control | Fly's platform: `fly ssh console` for maintenance, daily volume snapshots included | Everything: the disk, cron, Docker, the proxy, where the data lives |
| Pick it when | You want the app up today and would rather not learn servers | You already run a server, or want full control of the data and the bill |

**Pick Fly.io unless you have a reason not to.** Moving later is possible either way: `--backup` on one side and
`tools/restore.sh` on the other carry everyone's data across (server path, step 10, "Moving your laptop pilot").

This page has the Fly.io path first, then the server path, then what both need before real people arrive: email for
account recovery, plans and billing (Free, Pro and Stripe), the checks that run on every push, and the go-live
checklist at the end. `looks.example.com` stands for your production origin throughout, and it also stands in the
code in a few places you replace before launch (the checklist says where).

To run the app on your own computer instead (Windows, Mac, Linux), see [Run it in 5 minutes](README.md#run-it-in-5-minutes)
in the README: that needs only the .NET SDK and a tunnel, no Docker, no domain.

## Fly.io in 15 minutes (no server to manage)

Fly runs the app's container on a small virtual machine in a region you pick, terminates HTTPS at its edge, gives the
app a `<name>.fly.dev` address, keeps a persistent volume for the database and the media, and snapshots that volume
daily. The repository's [`fly.toml`](fly.toml) describes all of it (one `shared-cpu-1x` machine with 512 MB, the volume
`data` at `/data`, port 8080, `/healthz` checked every 30 seconds, HTTPS forced, the machine never stopped for
idleness); you fill in a name and the secrets.

```
phone ── https://<name>.fly.dev ──▶ Fly's edge (certificate) ──▶ the app on one machine (port 8080)
                                                                        │
                                                                   /data volume: orevosh.db + storage/
```

You need: a Fly account (`fly auth signup`; it asks for a card, and a pilot stays at a few dollars a month), an
Anthropic API key from <https://console.anthropic.com/settings/keys> (set a monthly spend limit there), and the
repository on your computer. Neither Docker nor the .NET SDK is needed on your computer: Fly builds the image.

### 1. Install flyctl and sign in

```bash
curl -L https://fly.io/install.sh | sh                        # macOS and Linux
brew install flyctl                                           # or, on a Mac with Homebrew
pwsh -Command "iwr https://fly.io/install.ps1 -useb | iex"    # Windows PowerShell
fly auth signup                                               # or fly auth login if you have an account
```

### 2. Create the app from the repository's fly.toml

From the repository folder:

```bash
fly launch --no-deploy --copy-config --name <your-app-name> --region fra
```

`--copy-config` uses `fly.toml` as it is and only writes your app name into it; `--no-deploy` because the volume and
the secret come first. The name becomes the address (`https://<your-app-name>.fly.dev`), so it has to be unused across
Fly. `fra` is Frankfurt; `fly platform regions` lists the others. Pick the one nearest your users and use the same one
in the next step. If `fly launch` offers to add a Postgres or Redis database, or to tweak the settings, say no: the app
has its own SQLite file on the volume.

### 3. The volume

```bash
fly volumes create data --size 3 --region fra --yes
```

`data` is the name `fly.toml` mounts at `/data`. 3 GB holds a pilot's database, photos and clips (clips are up to
40 MB each); `fly volumes extend <volume id> --size 5` grows it later without moving anything (`fly volumes list`
shows the id). One volume, one machine: the app is a single process with one SQLite file (README, "Known limitations").

### 4. The secret

```bash
fly secrets set ANTHROPIC_API_KEY=sk-ant-...
```

Secrets are environment variables, exactly the ones the `.env` file carries on a server: any setting from the README's
configuration table goes in the same way (`fly secrets set Plans__FreeChecksPerDay=5`), and so do the push keys (step 7),
the email settings ("Email for account recovery", below), the plan and Stripe settings ("Plans and billing", below) and
the board and store-link settings ("The weekly board and store links", below). Every `fly secrets set` restarts the
machine with the new values; several in one command is one restart.

### 5. Deploy

```bash
fly deploy --ha=false
```

Fly builds the image from the `Dockerfile` on its own builders, starts one machine, attaches the volume and routes to
it. `--ha=false` matters: without it Fly starts two machines for high availability, and this app must run as one process
(the README's limitations; a second machine would also want a volume of its own). Then:

```bash
fly status                                        # the machine: started, its check passing
fly logs                                          # "Database /data/orevosh.db is new: creating the schema from the migrations."
curl https://<your-app-name>.fly.dev/healthz      # ok
```

Open `https://<your-app-name>.fly.dev` on your phone: HTTPS from the first second, so the camera, the share sheet, the
home-screen install and the Secure cookie all work. Send the address to your pilot users.

If `fly logs` says the app cannot write to `/data` (`permission denied`, `unable to open database file`): the container
runs as the non-root `app` user and the volume's root folder must belong to it. Fly gives a new volume the ownership of
the image's `/data` folder, which the Dockerfile hands to `app`; if that did not happen, once:

```bash
fly ssh console -C "chown -R app:app /data"
fly machine restart <machine id>                  # fly status shows the id
```

### 6. The first moderator

Moderation is a flag on an account that exists, so **sign up in the app first**, then:

```bash
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --admin <handle>"
```

No restart. `--unadmin <handle>` takes it away. Every maintenance command runs on Fly in this shape (`--vapid` in step
7, `--backup` in step 9, `--verify <handle>` for a brand you have confirmed, `--pro <handle> <months|off>` for a Pro
granted by hand; server path, step 7, lists them), as the `app` user so the files it writes belong to the app. `Admin__Handles__0=<handle>` as a
secret does the same at every start, with the ordering rules of step 7 of the server path; the command is simpler.

### 7. Push notifications

```bash
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --vapid"
fly secrets set Push__PublicKey=<the PUBLIC line> Push__PrivateKey=<the PRIVATE line> Push__Subject=mailto:you@example.com
```

One pair, generated once: a new pair drops every existing subscription (server path, step 8).

### 8. Your own domain (optional)

The `fly.dev` address is fine for a pilot. For `looks.example.com`:

```bash
fly certs add looks.example.com
```

It prints the DNS records to add: a `CNAME` from `looks` to `<your-app-name>.fly.dev` (or, for a bare domain, `A` and
`AAAA` records with the addresses from `fly ips list`). Once the name resolves, `fly certs check looks.example.com`
reports the certificate issued and both addresses work. Set `Email__PublicOrigin=https://looks.example.com` (below) so
the links in mails carry the domain (with mail on, the app builds links for no other host than localhost until it is
set), and `Billing__PublicOrigin` to the same value if Stripe is on, so Checkout comes back to it.

### 9. Backups

Fly snapshots the volume every day and keeps five days of snapshots: `fly volumes snapshots list <volume id>` shows
them, and `fly volumes create data --snapshot-id <snapshot id> --region fra` makes a new volume from one (Fly's docs
have the current recipe for attaching it to a machine; it changes). That is a backup on the same provider. For a copy of
your own, off Fly, the app's backup command works here too:

```bash
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --backup /data/backups"      # prints database: ... storage: ...
fly ssh console -u app -C "tar czf /data/backups/storage-<stamp>.tgz -C /data/backups storage-<stamp>"
fly sftp get /data/backups/orevosh-<stamp>.db ./orevosh-<stamp>.db
fly sftp get /data/backups/storage-<stamp>.tgz ./storage-<stamp>.tgz
fly ssh console -C "rm -rf /data/backups"        # the copies share the 3 GB volume with the live data
```

The files hold every photo and clip people gave the app; keep them as private on your computer as they are on the
volume (server path, step 9, says how). There is no cron on Fly's machine: run this weekly from your computer, or from
a scheduled GitHub Actions job with a `FLY_API_TOKEN` secret (`fly tokens create deploy`).

Putting a copy back is the one place a server is simpler, because the app is running while you swap the file. At a
quiet moment: `fly sftp shell`, then `put orevosh-<stamp>.db /data/incoming.db`, then

```bash
fly ssh console -C "sh -c 'cd /data && mv incoming.db orevosh.db && rm -f orevosh.db-wal orevosh.db-shm'"
fly machine restart <machine id>
```

Writes between the swap and the restart are lost. The media folder goes back the same way: `put` the archive, then
`tar xzf` it over `/data/storage` as `app`.

### 10. Updating

```bash
git pull
fly deploy --ha=false
```

Nobody's data is touched: it is on the volume, and the schema is versioned with migrations that run on start (server
path, step 10, explains what the log says). If a deploy goes wrong, `fly logs` says why, `git checkout <previous
commit> && fly deploy --ha=false` returns to the old code, and the snapshots are the way back for data. Fly can also
run the image the release workflow publishes (`fly deploy --image ghcr.io/<owner>/orevosh:latest --ha=false`) once the
package is public; see "Continuous checks".

### 11. What to watch

- `fly status` and `fly logs` (every 5xx is logged with the path; `fly logs --no-tail` for a snapshot).
- **Disk:** `fly ssh console -C "df -h /data"`; extend the volume before it is full.
- **Memory:** `fly machine status <machine id>`. 512 MB is comfortable for the app's ~150 MB; `fly scale memory 1024`
  is the fix if clips and transcoding push it.
- **The model bill:** the spend limit in the Anthropic console is the backstop; the plan caps (`Plans__FreeChecksPerDay`
  3, `Plans__ProChecksPerDay` 30, `Plans__GuestChecksPerDay` 1), the per-account ceiling `Limits__ChecksPerDay` and the
  global `Limits__ChecksPerDayGlobal` (1000) are the caps. Guest checks are calls nobody signed up for: `fly logs` shows
  `Guest sweep: …` once an hour with how many went unclaimed.
- **The board:** a few minutes after the week closes (Saturday midnight in `Board__TimeZone`), `fly logs` shows
  `Board: week 2026-09-06 closed, 38 rows`; a quiet week says `Board: week 2026-09-06 had no counted fires, nothing to
  close` once; `Board: the close failed; it runs again in five minutes` is a warning worth reading, the next run
  retries. The lines the closer, the moderator and the zone write are listed under "The weekly board and store links".
- **Health from outside:** an uptime checker (UptimeRobot, Better Stack) on `https://<your-app-name>.fly.dev/healthz`.
  Fly restarts a machine whose own check keeps failing.
- **Cost:** `fly dashboard` shows the month. A shared-cpu-1x with 512 MB is about 3 USD, the 3 GB volume about 0.45 USD,
  bandwidth for a pilot is inside the free allowance.

## Email for account recovery

An account has no email address until its owner adds one in Settings. With one, confirmed by a link the app sends, a
forgotten password is reset from the sign-in screen by another link; without an address, a forgotten password still
means a new account. The app sends those two mails itself over SMTP, to any provider. With the settings below unset,
mail is off: the client says recovery is not set up on this server and the links are written to the app's log instead
(`docker compose logs app` or `fly logs`), fine for a laptop pilot, not for people you cannot reach by hand.

Set them like every other setting: in `.env` on a server, with `fly secrets set` on Fly.

| Variable | What to put |
|---|---|
| `Email__Host` | The provider's SMTP server, e.g. `smtp.resend.com` |
| `Email__Port` | `587` (the default; STARTTLS) |
| `Email__User` | The SMTP login the provider gives you |
| `Email__Password` | The SMTP password or API key. A secret: environment only, never `appsettings.json` |
| `Email__From` | The sender, e.g. `OREVOSH <hello@looks.example.com>`. The domain must be one the provider verified for you, or the mail is refused or lands in spam |
| `Email__PublicOrigin` | `https://looks.example.com`: the origin the links in mails carry. **Required on a server.** A link is never built from the address a request came in on unless that is localhost (a `Host` header is the requester's to choose, and the token rides in the link), so with this empty the app warns at start (`Email is on but Email:PublicOrigin is not set`), answers 502 to an address change or a resend, and quietly mails nothing for a forgotten password |
| `Email__UseStartTls` | `true` by default, for port 587. Leave it |

Mail is on when `Email__Host` and `Email__From` are both set. Two brakes keep the app from being used to flood an
inbox, and neither needs a setting: per client address, five recovery requests an hour (`Limits__RecoveryPerHourPerIp`),
and per account, three confirmation links every ten minutes and ten a day, three reset links an hour. A reset link
works only while the address it went to is still the account's, and changing the address voids the open reset links.

Three providers that work with exactly these lines:

**Resend** (a free tier of 3,000 mails a month; verify your domain, then API Keys → Create API Key):

```bash
Email__Host=smtp.resend.com
Email__Port=587
Email__User=resend
Email__Password=re_xxxxxxxxxxxxxxxx
Email__From=OREVOSH <hello@looks.example.com>
```

**Postmark** (transactional mail; verify a sender signature or the domain; Servers → your server → API Tokens; the
token is both the user and the password):

```bash
Email__Host=smtp.postmarkapp.com
Email__Port=587
Email__User=<server API token>
Email__Password=<server API token>
Email__From=hello@looks.example.com
```

**Gmail with an app password** (a pilot only: Google caps it around 500 mails a day and may rewrite the sender. Google
Account → Security → 2-Step Verification on → App passwords):

```bash
Email__Host=smtp.gmail.com
Email__Port=587
Email__User=you@gmail.com
Email__Password=<the 16-character app password>
Email__From=you@gmail.com
```

To check it: `https://…/api/config` answers `"email": true` once the settings were read; then, in the app, Settings →
add your own address, and the confirmation mail should arrive within a minute. If it does not, the app's log has the
provider's answer (a refused sender, a wrong password), or the missing-origin line above.

## Plans and billing

Every check is a paid model call, so the plans are caps, not features. A visitor gets one free check as a guest, a
free account gets three a day, OREVOSH Pro gets thirty, and `Limits__ChecksPerDay` (30) is the ceiling no plan
exceeds; checks and "Which one?" comparisons share the allowance. All of it is settings, in `.env` on a server and
`fly secrets set` on Fly:

| Variable | What to put |
|---|---|
| `Plans__FreeChecksPerDay` | Checks a day on Free. `3` by default: a taste, not the habit |
| `Plans__ProChecksPerDay` | Checks a day on Pro, `30`. Clamped to `Limits__ChecksPerDay`: the clamped number is the effective Pro cap, what `/api/config` publishes and the Pro page promises, and the start log warns when the plan's number is above the ceiling |
| `Plans__GuestChecksPerDay` | Free checks for a visitor with no account, `1`, per guest cookie and per client address over a rolling day, counted from looks actually given (a refused photo, a model outage or a dropped connection spends nothing; the per-address count is in memory, so a restart forgets the day). `0` turns guests off and the check screen asks to sign in |
| `Plans__GuestAttemptsPerDay` | The brake on attempts at the check route from a visitor, `20` per client address per 24 hours whatever they come to (429 `error.too_fast`). Well above the guest cap on purpose, so a refused photo never locks a shared address out of its look |
| `Plans__ProPriceText` | What the Pro page shows as the price, e.g. `₪19 / month` or `$5 / month`. Text only; empty hides it |
| `Plans__CompareNeedsPro` | `false`. Set `true` to keep "Which one?" for Pro accounts |
| `Billing__Provider` | `manual` (the default) or `stripe` |

**With `manual`, Pro is a command.** The Pro page shows the benefits and a note that Pro is switched on by hand, and
you put an account on Pro yourself, for a month or a year, or take it back:

```bash
docker compose exec app dotnet FitCheck.Api.dll --pro <handle> 3      # 3 months (31 days each, from now; 1 to 120)
docker compose exec app dotnet FitCheck.Api.dll --pro <handle> off    # back to Free
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --pro <handle> 12"
```

That is the whole upgrade path for a pilot: people write to you, you run the command, the app shows the end date in
Settings and on the Pro page, and a lapsed period falls back to Free by itself. Nothing charges anyone.

**With `stripe`, Checkout and the webhook are live.** What to set up, in Stripe's dashboard (test mode first, keys
starting `sk_test_`):

1. A product "OREVOSH Pro" with one recurring monthly price; copy its id (`price_…`).
2. An API secret key (Developers → API keys).
3. A webhook endpoint at `https://looks.example.com/api/billing/webhook` subscribed to `checkout.session.completed`,
   `invoice.paid`, `customer.subscription.updated` and `customer.subscription.deleted`; copy its signing secret
   (`whsec_…`).

Then:

```bash
Billing__Provider=stripe
Billing__StripeSecretKey=sk_live_...          # secrets: environment only, never appsettings
Billing__StripePriceId=price_...
Billing__StripeWebhookSecret=whsec_...
Billing__PublicOrigin=https://looks.example.com   # where Checkout returns to; the request's origin when empty
Plans__ProPriceText=₪19 / month
```

Stripe is on only when the provider is `stripe` and all three keys are set (`/api/config` then says `"billing": true`
under `plans`, and the Pro page shows the button). What happens: "Go Pro" opens a Stripe-hosted Checkout page for a
subscription with the account id attached; Checkout returns to `/#/pro?checkout=success` (or `cancel`); Stripe posts
the events to `/api/billing/webhook`, which is the one write that needs no session and no `X-Requested-With` header,
because its `Stripe-Signature` header is the guard (a bad or old signature is answered 400 and shows in Stripe's
dashboard). `checkout.session.completed` puts the account on Pro for 35 days (a month plus slack for slow events) on
top of any period still running and stores the customer id; `invoice.paid` (except the first, `subscription_create`,
which the checkout already counted) moves the end to the invoice's period end plus three days, from the event and never
below the current end; `customer.subscription.updated` follows the status, `active` or `trialing` to the current period
end plus three days (never below the current end), `past_due`, `unpaid` or `paused` down to three days from now at most;
`customer.subscription.deleted` ends it now; a missed renewal simply lapses. No event ids are kept: a repeated
`checkout.session.completed` stacks one period, every other repeat names the same period and changes nothing or ends
what already ended. An account that is Pro already cannot open a second Checkout (409). Card details never reach the
app, and the secret key is redacted from the app's logs.

To try it before people pay: keep test keys, install the Stripe CLI and run `stripe listen --forward-to
localhost:5000/api/billing/webhook` (it prints a `whsec_` for the session; put that in `Billing__StripeWebhookSecret`),
pay with Stripe's test card `4242 4242 4242 4242`, and watch the app's log say `Account <handle> is Pro until …`.
**Nothing here has run against a live Stripe account yet** (README, "Known limitations"): the first real subscription,
renewal and cancellation are the proof, and Stripe's event log plus `docker compose logs app` are where to look when
one of them misbehaves. Cancelling is on Stripe's side (or `--pro <handle> off`); the app has no cancel button, and the
terms tell people to write to you.

Inside the store apps Apple and Google forbid this checkout (`STORE.md`, "Payments"): the wrapped app must hide the
purchase or use the stores' billing.

## The weekly board and store links

Two things from Round 10 are settings. **The weekly flames board** is five top tens (looks, people, rising, by intent,
the stylist's picks) for the week that is running, cut at local midnight in a time zone you pick, closed by the app
itself into a hall of flame, with rules that decide which fires count so friends cannot carry a look. **Store links**
on tagged pieces leave the app through one door, `/api/items/{id}/out`, which appends the parameters of an affiliate
programme when the store's host is one you listed, sends no referrer to the store, and counts the tap. Nothing earns
anything until you list a host. In `.env` on a server, `fly secrets set` on Fly:

| Variable | What to put |
|---|---|
| `Board__TimeZone` | The IANA zone the week is cut in, `Asia/Jerusalem` by default: the week opens at local midnight and closes seven days later, and the archive is labelled by that day. Set it to where your people live **before the first week runs**; a change later moves every week's edges. A zone the machine does not know falls back to UTC with `Board: the time zone … is not known here; the week runs in UTC` in the start log (add `tzdata` to the image's `apt-get install` line if you see it) |
| `Board__WeekStartsOn` | The week's first day, a day name, `Sunday` (Israel's week; the close is Saturday midnight) |
| `Board__MinChecksToCount` | A fire counts only from someone with at least this many `ok` checks by the week's end, `1`. `0` turns the rule off |
| `Board__MaxPerFirerPerAuthor` | The most fires from one person on one author's looks that count in a week, `3` (the first ones by time). `0` means unlimited |
| `Board__NewAccountDays` | A fire from an account younger than this at the moment of the fire does not count, `2` |
| `Board__Size` | Places on each board, `10` |
| `Board__RisingDays` | The rising board lists the fired looks of accounts younger than this at the week's end, `30` |
| `Board__Sponsor__Name`, `Board__Sponsor__Handle`, `Board__Sponsor__PrizeText`, `Board__Sponsor__Url` | The week's sponsor: while `Name` is set the board shows "Presented by <name>" (linked to the account when `Handle` names one, else to `Url`), the prize line and the site's host. There is no self-service: a brand that sponsors a week is one you agreed a prize with, verified with `--verify`, and put here by hand; unset it when the week is over |
| `Affiliate__Hosts__<host>` | One line per programme you joined: `Affiliate__Hosts__amazon.com=tag=orevosh-20` appends `?tag=orevosh-20` (or `&tag=…`, before any `#fragment`) to every store link that leaves for `amazon.com` or a subdomain of it. Nothing is stored on the link: the parameters are added at the door, so joining, changing or leaving a programme is one line for every link at once. Leave every line out until you have joined a programme; with none, every link is redirected as given |
| `Affiliate__Disclosure` | `true`. Bound, and nothing reads it yet: the item sheet shows "Leaves OREVOSH · This link may earn OREVOSH a commission." under every store link, listed host or not. Keep the line; programme terms and consumer law expect it |

The host is a configuration key with a dot in it (`Affiliate:Hosts:amazon.com`), which the app reads as it reads every
other key (a double underscore for each colon, the dot kept). A shell's `export` refuses a dot in a variable name, so
put the line in `.env` or in `fly secrets set 'Affiliate__Hosts__amazon.com=tag=orevosh-20'`; if your Compose version
refuses it too, set the host under `Affiliate:Hosts` in `src/FitCheck.Api/appsettings.json` before `docker compose
build`.

**What the app does on its own.** `BoardCloser` runs at start and every five minutes: every week that is over and has
no archive rows yet is computed under the rules above and written in one save, oldest first back to the week of the
earliest fire, so a server that was down over the weekend closes the missed week on its next start; the people on the
looks board of the week that just ended are told their place in the app and by push ("You finished #2 this week");
older weeks closed in a catch-up are silent. There is no route and no command that closes a week by hand. What the log
says, in `docker compose logs app` or `fly logs`:

- `Board: week 2026-09-06 closed, 38 rows`: the week is in the hall, the badges are on, the notifications went out.
- `Board: week 2026-09-06 had no counted fires, nothing to close`: once per week per process; nothing written.
- `Board: week 2026-09-06 was already closed by another run; nothing written`: two processes raced; the first one's
  rows stand. You should never see it with one instance.
- `Board: the close failed; it runs again in five minutes`, a warning with the exception: read it; the next run retries.
- `Board: <look id> excluded by <moderator id>: <reason>` and `Board: <look id> put back by <moderator id>`: a moderator
  pulled a look off the board through `POST /api/admin/board/exclude` (with `{ postId, reason }`) or put it back with
  `DELETE /api/admin/board/exclude/<postId>`. There is no screen for it yet; a moderator's session and the CSRF header
  do it from a terminal: `curl -X POST -b 'orevosh.session=…' -H 'X-Requested-With: Orevosh' -H 'Content-Type:
  application/json' -d '{"postId":"…","reason":"bought fires"}' https://looks.example.com/api/admin/board/exclude`.
- `Items: 3 on post <look id> by <user id>`: someone saved the pieces on their look.

**What to know before people rely on it.** The board reads `/api/board` from memory for 60 seconds per process (the
Explore strip and the reset card on Home read the same route, and every answered read counts as a `boardViews` in the
metrics), the out door allows sixty taps a minute per client address, and both windows live in the one `app` process.
A week's places, the hall and the badge appear a few minutes after midnight, not at the stroke of it. A moderator's
exclusion changes the weeks still open; a week already in the hall keeps its rows.

## Continuous checks

Two GitHub Actions workflows live in [`.github/workflows`](.github/workflows). They run on GitHub's machines when you
push; nothing to install.

- **`ci.yml` (CI)**, on every push and pull request on every branch: a Release build with warnings as errors, the
  xUnit suite (the `.trx` results are the `test-results` artifact of the run), and, in a second job, the browser test
  from `tools/e2e` in a phone-sized Chromium against the real API with only the Anthropic API stubbed. Its screenshots
  are the `browser-screenshots` artifact, kept also when the run fails (then with `failed-<person>.png` and `.html` of
  the page it stopped on). NuGet, npm and Chromium are cached, so a run takes a few minutes.
- **`release.yml` (Release image)**, on a push to `main` and on every `v*` tag: builds the Docker image from the
  repository's `Dockerfile` and publishes it as `ghcr.io/<owner>/orevosh` tagged `latest`, the commit sha and, for a
  tag, the tag name. The package is private to the repository's collaborators until you make it public (GitHub → the
  repository → Packages → orevosh → Package settings → Change visibility); a server pulls a public one with
  `docker pull ghcr.io/<owner>/orevosh:latest`, a private one after `docker login ghcr.io` with a token that has
  `read:packages`.

The badge in the README reads the CI workflow's latest result on the default branch. The workflow's file name is in
the address, so it keeps working through renames of the workflow's display name; a rename of the repository needs the
address updated:

```markdown
[![CI](https://github.com/<owner>/<repository>/actions/workflows/ci.yml/badge.svg)](https://github.com/<owner>/<repository>/actions/workflows/ci.yml)
```

A red badge means the last push broke a rule the tests hold; open the run from the badge, the failing test's name says
which.

## Your own server: a VPS with Docker and Caddy

This is the walkthrough for running the app on one small rented server (a 5 EUR/month VPS is enough for a pilot),
with your own domain, HTTPS, backups and updates that keep everyone's data. It assumes you can open a terminal and
copy commands; it does not assume you have done this before.

What you end up with:

```
phone ── https://looks.example.com ──▶ Caddy (certificate, port 443) ──▶ the app (port 8080, container only)
                                                                              │
                                                                         /data volume: orevosh.db + storage/
```

## 1. What you need

- **A domain name** (or a subdomain of one you already own, like `looks.example.com`). About 10 EUR/year.
- **A VPS** running Ubuntu 22.04 or 24.04 with at least 1 GB of RAM and 20 GB of disk. Hetzner, DigitalOcean,
  Scaleway, Contabo and OVH all have one for around 5 EUR/month. Pick a location near your users.
- **An Anthropic API key** from <https://console.anthropic.com/settings/keys>. It is the only secret the app
  needs. Every outfit check is one call to the model; set a monthly spend limit in the console.
- Your SSH key on the server (the VPS provider asks for it when you create the machine).

## 2. DNS

In your domain's DNS settings, add an **A record** for the name you want (`looks`, or `@` for the bare domain)
pointing at the server's public IPv4 address. If the server has an IPv6 address, add an **AAAA record** too.

Wait until `ping looks.example.com` on your computer answers from the server's address (usually minutes, sometimes
an hour). Caddy asks Let's Encrypt for the certificate the moment it starts, and that only works once the name
resolves to this machine.

## 3. The server

SSH in as root and install Docker (this is Docker's official installer; it also installs Docker Compose):

```bash
apt-get update && apt-get upgrade -y
curl -fsSL https://get.docker.com | sh
```

Open only what the app uses and turn the firewall on:

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable
```

Optional but recommended on a 1 GB machine, a little swap so a build never runs out of memory:

```bash
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

## 4. Get the code

```bash
apt-get install -y git
git clone https://github.com/<you>/<repository>.git /opt/orevosh
cd /opt/orevosh
```

Everything below is run from `/opt/orevosh`.

## 5. Settings: the `.env` file

```bash
cp .env.example .env
nano .env
```

Fill in:

| Variable | What to put |
|---|---|
| `DOMAIN` | The name from step 2, e.g. `looks.example.com`. No `https://`. |
| `ANTHROPIC_API_KEY` | Your key, `sk-ant-...`. |
| `Push__PublicKey`, `Push__PrivateKey`, `Push__Subject` | Leave the keys empty for now; step 8 fills them. `Subject` is a `mailto:` you can be reached at. |
| `Admin__Handles__0` | Leave it commented out for now. It names an account that already exists, so it comes in step 7, after you have signed up. |
| `Email__Host`, `Email__Port`, `Email__User`, `Email__Password`, `Email__From`, `Email__PublicOrigin` | Account recovery by mail. Leave them out until you have an SMTP provider; "Email for account recovery" above has the exact lines for Resend, Postmark and Gmail. `Email__PublicOrigin` is `https://` plus your domain and is required once mail is on: without it the app builds no links on a real host. |
| `Plans__FreeChecksPerDay`, `Plans__ProChecksPerDay`, `Plans__GuestChecksPerDay`, `Plans__GuestAttemptsPerDay`, `Plans__ProPriceText`, `Plans__CompareNeedsPro` | The caps (3, 30, 1), the brake on guest attempts (20) and the Pro page's price text. The defaults are fine for a pilot; "Plans and billing" above. |
| `Billing__Provider`, `Billing__StripeSecretKey`, `Billing__StripePriceId`, `Billing__StripeWebhookSecret`, `Billing__PublicOrigin` | Leave the provider at `manual` (Pro by the `--pro` command) until Stripe is set up and tested in test mode; "Plans and billing" above. The three Stripe keys are secrets. |
| `Board__TimeZone`, `Board__WeekStartsOn`, `Board__MinChecksToCount`, `Board__MaxPerFirerPerAuthor`, `Board__NewAccountDays`, `Board__Size`, `Board__RisingDays`, `Board__Sponsor__Name` (+ `Handle`, `PrizeText`, `Url`) | The weekly board: the zone and the day the week is cut on (`Asia/Jerusalem`, `Sunday`; set them before the first week runs), the rules that decide which fires count (1 check, 3 per pair, 2 days), the size (10), the rising window (30) and the week's sponsor, by hand. The defaults are the pilot's; "The weekly board and store links" above. |
| `Affiliate__Hosts__<host>` | One line per affiliate programme you have joined, e.g. `Affiliate__Hosts__amazon.com=tag=orevosh-20`: appended when a store link leaves for that host. Leave it out until you have joined one; with no line nothing is appended. The commission line under store links shows either way. |

Any setting from the README's configuration table can be added to `.env` in the same shape, for example
`Plans__FreeChecksPerDay=5` (a double underscore stands for the colon). `.env` is git-ignored and stays on the
server; never paste it anywhere.

## 6. First start

```bash
docker compose up -d --build
```

The first build downloads the .NET images and compiles the app (a few minutes on a small VPS; later builds are
faster). Then:

```bash
docker compose ps                 # both "app" and "caddy" should say running / healthy
docker compose logs -f app        # Ctrl-C to stop watching
```

The app's log shows what happened to the database on start, for example
`Database /data/orevosh.db is new: creating the schema from the migrations.`, and, since the image ships ffmpeg,
`Transcoding is on: ffmpeg version ...` (clips are re-encoded to H.264 MP4 in the background; README, "Clips"). Then open `https://looks.example.com`
on your phone. Caddy fetches the certificate on the first request (give it up to a minute). `https://looks.example.com/healthz`
answers `ok` when the app can reach its database.

If the certificate does not come: `docker compose logs caddy` says why, and it is almost always DNS not pointing at
this machine yet, or port 80/443 closed by the provider's own firewall (check the VPS control panel).

## 7. The first admin

Moderation is a flag on an account, and only an account that exists can carry it, so the order matters:

1. **Sign up in the app first**, with the handle you want to moderate with, like anyone else.
2. Make that account a moderator, either way:
   - add `Admin__Handles__0=<handle>` to `.env` and `docker compose up -d`: the app restarts and, at start, promotes
     the existing account with that handle (it never demotes anyone), or
   - without a restart: `docker compose exec app dotnet FitCheck.Api.dll --admin <handle>`.

Reload the app on the phone and the moderation queue (reported looks and comments, suspensions) is in that account's
menu.

A handle listed in `.env` cannot be registered by anyone, which is why the signup comes first: listed before you sign
up, the handle is blocked for you too and signup says it is taken. (If that happens: remove the line, `docker compose
up -d`, sign up, put the line back, `docker compose up -d`.)

A co-moderator is added the same way and in the same order: they sign up, then `Admin__Handles__1=theirhandle` and
`docker compose up -d`, or `--admin theirhandle`.

Revoking is `docker compose exec app dotnet FitCheck.Api.dll --unadmin <handle>`, and remove the handle from `.env` as
well, or the next restart promotes it again. A moderator cannot be suspended from the queue and cannot delete their own
account while the flag is on; both go through `--unadmin` first. `--admin` and `--unadmin` exit with code 1 when no
account has the handle.

The app has seven maintenance commands. None starts the server; all run from `/opt/orevosh`:

| Command | What it does |
|---|---|
| `docker compose run --rm --no-deps app dotnet FitCheck.Api.dll --vapid` | Prints a VAPID key pair for push (step 8). Needs no database, so it works before the first start |
| `docker compose exec app dotnet FitCheck.Api.dll --backup /data/backups` | A consistent copy of the database and the media folder into that folder on the volume (step 9; `tools/backup.sh` wraps it and brings the copies out) |
| `docker compose exec app dotnet FitCheck.Api.dll --admin <handle>` | Makes an existing account a moderator |
| `docker compose exec app dotnet FitCheck.Api.dll --unadmin <handle>` | Takes that away |
| `docker compose exec app dotnet FitCheck.Api.dll --verify <handle>` | Marks an existing brand account as verified: a check inside its BRAND mark everywhere it appears, from its next request. You are the process: run it for a brand once you know who is behind the account |
| `docker compose exec app dotnet FitCheck.Api.dll --unverify <handle>` | Takes that away |
| `docker compose exec app dotnet FitCheck.Api.dll --pro <handle> <months\|off>` | Puts an existing account on Pro for that many months (31 days each, from now; 1 to 120) or back on Free ("Plans and billing") |

`docker compose exec` needs the app running. The account commands exit with 1 when no account has the handle and 2 on
a usage error. On a laptop the same commands are `dotnet run -- --admin <handle>` and so on (the README, "Maintenance
commands").

## 8. Push notifications

Push needs a key pair (VAPID). Generate one with the app itself:

```bash
docker compose run --rm --no-deps app dotnet FitCheck.Api.dll --vapid
```

Paste the printed public and private key into `Push__PublicKey` and `Push__PrivateKey` in `.env`, then
`docker compose up -d`. From that moment the app offers "turn on notifications". Keep the private key private, and
generate the pair once: a new pair invalidates every existing subscription (the push services answer 401 or 403 to
it, the app drops the subscription on that answer, and people turn notifications on again in Settings).

## 9. Backups

`tools/backup.sh` makes a consistent copy of the database (SQLite's own online snapshot into one self-contained file,
no need to stop anything) and a copy of the photo and clip folder, and puts both in `backups/` next to the code:

```bash
tools/backup.sh
ls -l backups/
# -rw------- orevosh-20260905033000.db    drwx------ storage-20260905033000/
```

The copies hold every photo and clip people gave the app, so the script makes them readable by root only: it runs with
`umask 077`, makes `backups/` mode `700`, and `chmod -R go-rwx` after the copies come out of the container (`docker cp`
would otherwise keep the container's world-readable modes). Nothing stays on the data volume: the container's scratch
folder is removed whether the run succeeds or fails halfway.

Sizes: the database is small (megabytes for a pilot), a storage copy is the whole media folder. So the script keeps the
last `KEEP` (default 14) database copies but only the last `KEEP_STORAGE` (default 2) storage copies. At the moment a
backup runs the disk holds the live folder, the copy in flight on the volume, the two kept copies and the new one (the
oldest goes only once the new one is complete): budget about five times the media folder for that moment and three
times the rest of the time. `docker compose exec app du -sh /data/storage` says what the folder is today;
`KEEP_STORAGE=1` shrinks the kept part.

Every night at 03:30:

```bash
(crontab -l 2>/dev/null; echo '30 3 * * * cd /opt/orevosh && KEEP=14 KEEP_STORAGE=2 tools/backup.sh >> /var/log/orevosh-backup.log 2>&1') | crontab -
```

**A backup on the same disk is not a backup.** Copy `backups/` somewhere else at least weekly, and keep it as private
there as it is here: to another Linux machine with `rsync -a` or `scp -rp`, which keep the modes (from your laptop:
`scp -rp root@looks.example.com:/opt/orevosh/backups ~/orevosh-backups`), or encrypted to a cloud drive, which has no
file permissions of its own (`rclone` with a `crypt` remote, or `tar cz backups | gpg -c -o backups-$(date +%F).tgz.gpg`
before uploading).

To put a backup back (this replaces what is live, it asks first):

```bash
tools/restore.sh backups/orevosh-20260905033000.db backups/storage-20260905033000
```

Leave the second argument out to restore the database only.

## 10. Updating

```bash
cd /opt/orevosh
tools/backup.sh                       # belt and braces
git pull
docker compose build app
docker compose up -d
docker compose logs -f app            # watch it come up
```

Nobody's data is touched: the database is a file on the `data` volume, not in the image, and the schema is
versioned with EF Core migrations. On every start the app looks at the file and does one of three things, and says
which in the log:

- **New file**: creates every table (`is new: creating the schema from the migrations`).
- **File made by a previous version**: applies the migrations added since (`applying N pending migration(s)`), or
  nothing when there are none (`is at the current schema`).
- **A pilot file from before migrations existed** (a database created by `dotnet run` in an earlier round, on a
  laptop or a tunnel setup): first copies it to `orevosh.db.bak-<timestamp>` next to it, then adds every missing
  table, column, foreign key and index and alters a column whose nullability differs from the model (a table
  rebuild: the rows are copied into a fresh table), with foreign keys off during the batch and the whole batch rolled
  back, the `.bak` kept, if any table would come out with fewer rows; then it records the migrations as applied so the
  next update takes the normal path (`predates migrations: copied it to ... before upgrading` followed by
  `upgraded: ...`, which names the tables created, the columns added and altered and the foreign keys added). The copy
  stays on the volume; delete it once you are happy (`docker compose exec app rm /data/orevosh.db.bak-...`).

**Round 10 re-keys `PostItems`** (`20260912151344_Round10`). A database from Round 9 keyed the pieces on a look by
`(PostId, Name)`; the migration gives every existing row its own id, marks it as the stylist's (`Source = Stylist`,
nothing else could have written it) and numbers it in the order it was inserted, in a hand-written `UPDATE` that runs
before SQLite rebuilds the table around the new key (the generated migration alone would have copied one empty id into
every row and stopped at the second). Names and categories stay as they were, so the search by piece keeps finding the
same looks; the new columns (brand, model, link, dot, confirmation) start empty. The same migration adds
`BoardExclusions`, `WeeklyWinners` and `Counters`, and `Rank` on `Notifications`. It runs at the next start like every
migration, so take the backup first (`tools/backup.sh`, or `--backup`): the rebuild copies the table, and a copy is
what you want to have if the box loses power in the middle. A pilot file from before migrations existed has no
`PostItems` table at all (it came in Round 9), so the upgrade path above simply creates it in its Round 10 shape; the
tests cover both files.

### Moving your laptop pilot to the server

**Stop the local app first** (Ctrl-C in the window running `dotnet run`). The database runs in WAL mode: while the app
is open, its newest writes sit in `orevosh.db-wal` and `orevosh.db-shm` next to the file, and a copy of `orevosh.db`
taken alone at that moment is missing them. The safe way does not depend on that at all: the app's own backup command
writes one self-contained file whatever state the sidecars are in, and a copy of the media folder next to it:

```bash
cd src/FitCheck.Api
dotnet run -- --backup backups        # the same line in PowerShell; prints "database: ..." and "storage: ..."
```

Copy that `.db` file and the `storage-<stamp>` folder to the server (`scp -rp src/FitCheck.Api/backups
root@looks.example.com:~/pilot`), then restore them there:

```bash
tools/restore.sh ~/pilot/orevosh-<stamp>.db ~/pilot/storage-<stamp>
```

A pilot database from before migrations existed is upgraded on the next start as described above.

If an update goes wrong, `docker compose logs app` shows the reason; the `.bak` copy and the nightly backup are the
way back (`tools/restore.sh`), and `git checkout <previous commit> && docker compose build app && docker compose up -d`
returns to the old code.

For developers: after changing the model, add a migration from the repository root with
`dotnet ef migrations add <Name> --project src/FitCheck.Api --output-dir Data/Migrations` (`dotnet tool install -g
dotnet-ef` once) and commit the generated files; `DatabaseSetupTests` checks that the migrations produce exactly the
schema the model describes. From this commit on every schema change is a new migration: `InitialCreate` is never
regenerated again, because deployed databases carry its row in the migrations history and a regenerated one (a new
id) would be pending on all of them and fail on its first `CREATE TABLE`. The app also switches every file database
to WAL mode at start (persisted in the file; that is where the `-wal` and `-shm` sidecars come from).

## 11. What to watch

- **Disk.** Clips are up to 40 MB each as uploaded (the background re-encode to H.264 usually leaves a few MB, and the
  original is gone once it is done) and a storage backup is the whole media folder, two of them kept (step 9).
  `df -h /` weekly; `docker system prune -f` removes old build layers. When the disk is the problem, the answer is
  object storage (below).
- **Health.** `https://looks.example.com/healthz` returns `ok`; anything else, or no answer, is worth a look. A free
  uptime checker (UptimeRobot, Better Stack) can ping it every few minutes and email you. Docker also checks it
  itself: `docker compose ps` shows `(healthy)` or `(unhealthy)`.
- **Logs.** `docker compose logs --since 1h app` for the app (every 5xx is logged with the path), `docker compose logs
  caddy` for certificates and traffic. Logs are rotated by Docker.
- **The model bill.** The plan caps (`Plans__FreeChecksPerDay` 3, `Plans__ProChecksPerDay` 30, `Plans__GuestChecksPerDay`
  1), the ceiling `Limits__ChecksPerDay` and the global `Limits__ChecksPerDayGlobal` cap it; the spend limit in the
  Anthropic console is the backstop. `https://looks.example.com/#/admin/metrics` shows how much the pilot is used
  (signed in as a moderator; anyone else gets the refusal), and `docker compose logs app | grep "Guest sweep"` says how
  many guest checks went unclaimed each hour.
- **Memory.** A 1 GB server runs the app (about 150 MB) and Caddy comfortably; `docker stats` shows both.
- **The board.** `docker compose logs app | grep "Board:"` after the week closes (Saturday midnight in
  `Board__TimeZone`): `Board: week 2026-09-06 closed, 38 rows` is the normal line, `had no counted fires` a quiet
  week, and the warning `Board: the close failed; it runs again in five minutes` is the one to read. The moderator's
  exclusions log there too. "The weekly board and store links" lists every line.
- **Updates to the server itself.** `apt-get update && apt-get upgrade -y` monthly, `reboot` when it asks.

## 12. What the app does for security, and what it does not yet

In place: HTTPS with automatic renewal; HttpOnly, Secure, SameSite=Strict session cookies, and a guest cookie of the
same kind that lives a day; a CSRF header on every write, with the Stripe webhook the one exception and its signature
the guard there; passwords hashed with ASP.NET Core's hasher; rate limits on signup, login, checks (per plan; for guests per cookie and per
address, counted from looks given, with a brake on attempts per address), and per account on comments, reports and
recovery mail; recovery links built only from
`Email__PublicOrigin`, never from a request's `Host`; photos and clips never served by path; uploads checked by their
bytes, not their declared type; moderation, verification and the plan as flags on the account row, set only at start
from `Admin:Handles` and by the `--admin`, `--verify` and `--pro` commands (or Stripe's signed webhook for the plan),
never by anything a request carries; push
subscriptions only to public push-service names (a literal address, `localhost` or a single-label name is refused, so
the app cannot be pointed at its own network) and at most 10 per account; the app container runs as a non-root user
with nothing published except through Caddy; and on every response `Strict-Transport-Security: max-age=31536000`
(over https, for this host only: no `includeSubDomains`, so nothing else under your domain is forced onto HTTPS by
this app), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`
(`no-referrer` on the one route a store link leaves through, `/api/items/{id}/out`, which also answers
`Cache-Control: no-store`, refuses a hidden look and takes sixty taps a minute per address) and a `Permissions-Policy`
that keeps camera and microphone to the app itself. Store links themselves are stored only when they are `http(s)`
with a host and no user info, and are never the `href` a person taps.

Built, and waiting on a setting from you: account recovery works once `Email__*` points at a provider ("Email for
account recovery"); Stripe Checkout runs once `Billing__*` is set and tested ("Plans and billing"); clip transcoding runs
once ffmpeg is in the image (the checklist below); the pilot metrics answer only to a moderator's session.

Still missing before a public launch, in rough order of importance:

1. **Age assurance.** The date of birth at signup is self-declared. Integrate the Apple and Google age-signal APIs or a
   provider and gate signup on the result.
2. **Clips are transcoded inside the app container.** The image ships ffmpeg and the app re-encodes every uploaded
   clip that is not H.264 MP4 already into one in the background (`Storage:Transcode`, on by default; the start log
   says `Transcoding is on: ffmpeg version ...`), so a WebM from an Android phone plays on iPhones and the files are
   smaller than what was uploaded; the original serves until the re-encode is done. It is one ffmpeg at a time in the
   same process as the web server: fine for a pilot, and a separate worker or a video service is the answer at launch
   scale, together with object storage (next).
3. **Object storage.** Photos and clips sit on the server's disk behind `IImageStore`. An S3-compatible bucket
   (Hetzner, Backblaze, R2) makes the disk stop being the limit and the backups a bucket policy.
4. **A Content-Security-Policy header.** Not set yet: the client uses Google Fonts and inline styles, which need
   nonces or hashes before a strict policy can go in without breaking the app.
5. **Brand verification is by hand** (`--verify`, no form and no process behind it), and **one process only**: the
   checks-per-day reservation, the per-address guest count and the rate limiters' windows live in memory, so run one
   `app` container (one machine on Fly). Multiple instances need a shared store.
6. **The rate limiters trust `X-Forwarded-For`**, which is right behind Caddy on the private compose network and
   behind Fly's edge; never publish port 8080 on a host.

## Go-live checklist

Before the address leaves the team, in this order:

1. **A domain and HTTPS.** `fly certs add`, or steps 2 and 6 of the server path. `https://…/healthz` answers `ok`,
   the padlock is there on a phone, and the app installs to the home screen.
2. **The production origin in the code.** `https://looks.example.com` is a placeholder in a handful of files; replace it
   with your domain and redeploy: the Open Graph and Twitter tags in `src/FitCheck.Api/wwwroot/index.html` (`og:url`,
   `og:image`, `twitter:image`), the four absolute URLs at the top of `wwwroot/landing/index.html` and
   `index.he.html` (canonical, hreflang, `og:url`, `og:image`), `mobile/capacitor.config.json` (`server.url`,
   `allowNavigation`) with `mobile/README.md`'s `WKAppBoundDomains` note, and the URLs table in `STORE.md`. Then paste a
   link into a chat app and check the card shows the 1200×630 image.
3. **The Anthropic key with a spend limit.** Set a monthly limit in the console; the plan caps (`Plans__FreeChecksPerDay`
   3, `Plans__ProChecksPerDay` 30, `Plans__GuestChecksPerDay` 1), the ceiling `Limits__ChecksPerDay` (30) and
   `Limits__ChecksPerDayGlobal` (1000) cap the volume from the app's side. A thousand checks a day is a real bill: do
   the arithmetic for your model and your pilot's size before raising any of them, and remember that guest checks are
   calls made by people who never signed up (`Plans__GuestChecksPerDay=0` closes that door).
4. **Plans and billing decided.** For a pilot leave `Billing__Provider=manual` and grant Pro with `--pro`; set
   `Plans__ProPriceText` only when there is a price. To charge, set up Stripe in test mode, run the Stripe CLI against
   the webhook, pay once with the test card, and only then switch to live keys ("Plans and billing"). Never switch the
   provider to `stripe` with keys you have not tested.
5. **The first moderator**, signed up and then promoted with `--admin` (Fly step 6, server step 7). Two is better than
   one: someone has to look at the queue every day.
6. **The first brands verified.** Each brand account that you know is the brand (you spoke to them, the handle is on
   their site) gets `--verify <handle>` and the check inside its BRAND mark; anyone else stays a self-declared brand.
   `--unverify` when that changes. `MARKETING.md`'s "week 0" is when this happens.
7. **VAPID keys** set once (`--vapid`) and never regenerated.
8. **Email** pointed at a real provider, `Email__PublicOrigin` set to the domain, and tested with your own address.
   Without a provider a forgotten password means a new account; without the origin, no link goes out on a server.
9. **Backups running and copied off the box.** On a server: the nightly cron of step 9 and a weekly copy elsewhere.
   On Fly: the daily snapshots are on, plus a weekly `--backup` and `fly sftp get` of your own. Restore one once,
   before you need to.
10. **The legal pages reviewed by a lawyer, and the guidelines by you.** `#/terms` and `#/privacy` (version 2, dated
    2026-09-12, in both languages) describe what the code does in plain words and are not legal advice: the
    governing-law line is a placeholder, the contact address `hello@orevosh.app` must be a mailbox someone reads, and the
    stores want both pages at a public URL. The guidelines page (linked from signup with the two) says what gets
    reported and what happens to a report, what deletion removes, and that the photos are the person's own. Change
    anything you would not stand behind; the version line at the bottom moves with the text (`views/legal.js`).
11. **Age is self-declared, and the limits say so.** A date of birth typed at signup is not age assurance; the
    README's "Known limitations" is the honest list. Read it, decide who you invite, and plan the age-signal
    integration before a public launch.
12. **Watch the disk.** Clips are up to 40 MB each and a media backup is the whole folder: `df -h` weekly on a server,
    `fly ssh console -C "df -h /data"` on Fly, and grow the volume before it fills. A full disk stops uploads and, worse,
    writes.
13. **Transcoding needs ffmpeg in the image and CPU to spare.** With `ffmpeg` on the machine the app re-encodes clips to
    H.264 MP4 in the background (`Storage__Transcode`, on by default; `Storage__FfmpegPath` when it is not on the PATH),
    so an Android WebM plays on iPhones. Check `https://…/api/config`: `"transcoding": true` means it is running;
    `false` means the image has no ffmpeg (add `ffmpeg` to the `apt-get install` line of the Dockerfile's runtime
    stage and redeploy) and clips play only where the sender's codec does. A 30-second clip takes on the order of a
    minute of a shared CPU; on Fly, `fly scale vm shared-cpu-2x` if the app gets sluggish while a clip converts.
14. **Rate limits that fit the launch.** Signups per address (50 an hour), logins (30 per quarter hour), comments (30
    an hour per account) and reports (20 an hour per account) are pilot numbers; a launch party on one Wi-Fi needs
    `Limits__SignupsPerHourPerIp` raised for the evening, and the per-account ones (`Limits__CommentsPerHour`,
    `Limits__ReportsPerHour`) are meant to stay where nobody meets them by hand. The guest cap is per address too:
    one free check per address a day means a whole café shares one, which is the intended side; it counts looks given,
    so a refused photo does not spend the café's look, and `Plans__GuestAttemptsPerDay` (20) brakes attempts on top.
15. **Real screenshots in the store kit.** The files in `brand-kit/store/` and `wwwroot/landing/screens/` show the
    browser test's synthetic outfit and a fake camera; take real captures on a phone and re-run
    `tools/brand/render-kit.js` before any store submission (`brand-kit/README.md`, `STORE.md`).
16. **The board's week is your users' week.** `Board__TimeZone` and `Board__WeekStartsOn` (`Asia/Jerusalem`, `Sunday`)
    cut the week and label the archive; set them to where your people live before the first week runs, because a
    change later moves every week's edges. The morning after the first close, `grep "Board:"` in the log should show
    `closed, N rows`, and `#/board/hall` the week ("The weekly board and store links").
17. **Decide the sponsor.** `Board__Sponsor__Name` (with `Handle`, `PrizeText`, `Url`) puts "Presented by" on the board
    with the prize; leave it unset until a brand has agreed to a prize with you, and unset it again when the week is
    over. There is no self-service, so this is your word on the board.
18. **Affiliate hosts only for programmes you joined, and keep the disclosure on.** One `Affiliate__Hosts__<host>` line
    per programme whose terms you accepted; with none, nothing is appended and nobody earns anything. The commission
    line shows under every store link (`Affiliate__Disclosure` is bound and not read yet): leave it, the programmes'
    terms and consumer law expect it. The store gets no referrer from the app.
19. **`--verify` the brands that tag products.** A brand account whose looks carry store links, and any brand that
    sponsors a week, is one you have spoken to; the check inside its mark says so. Anyone else's brand and model on a
    piece is their own word (README, "Known limitations"), and the queue is the answer when it is abused.
