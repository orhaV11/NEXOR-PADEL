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
code in a few places that one command rewrites before the first build:

```bash
node tools/brand/set-origin.js https://looks.example.com
```

It looks at eleven files, writes your origin into the ones that still carry the placeholder, and prints each with a
count. Four of them are shipped to a browser or a store and are the reason this runs before the build:
`src/FitCheck.Api/wwwroot/index.html` (`og:image`, `twitter:image` — two), both landing pages
(`wwwroot/landing/index.html` and `index.he.html`: canonical, both `hreflang` links, `og:image`, `twitter:image` —
five each) and `mobile/capacitor.config.json` (`server.url`, `allowNavigation` — two). **`og:url` is not on that
list any more**: it was deleted from all three pages, because every crawler falls back to the URL it actually
fetched, so the address unfurls right on a tunnel, a staging name or the real domain with nothing to configure.
`og:image` cannot do the same — the spec wants it absolute — which is why these two remain.
The other seven are the documents that quote the origin, rewritten so the commands you paste from them are already
yours: `mobile/README.md`, `STORE.md`, `MARKETING.md`, `brand-kit/README.md`, this file, `README.md` and
`.env.example`. So run it **before** `fly deploy` or `docker compose build`, commit the result, and read the diff —
the sample email addresses it rewrote in `.env.example` and `STORE.md` now say `hello@` your domain, which is a guess
at your mailbox, not a fact. `node tools/brand/set-origin.js --check` exits 1 while a placeholder is left in any of the
eleven, which is the line for CI and for the last look before a build. Step 5 of the go-live checklist says the same
thing in its place.

**If you have never deployed anything, read [`LAUNCH.md`](LAUNCH.md) instead and come back here for detail.** It is the
same ground as one runbook, in English and in Hebrew, from buying a domain to the first day and what to do when
something breaks.

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
iwr https://fly.io/install.ps1 -useb | iex                   # Windows PowerShell, in the window itself
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
curl -s https://<your-app-name>.fly.dev/readyz    # {"ok":true,"checks":{"db":"ok","storage":"ok","ffmpeg":"ok"}}
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --doctor"    # the settings, from inside
```

From your laptop, `scripts/smoke.sh https://<your-app-name>.fly.dev` runs the two curl lines above and six more (both
landing pages with the slogan, `/api/config`, the link-preview tags in the shell, the security headers, the manifest),
one `OK`/`FAIL` line each, and exits 1 on any failure; `--check` adds one guest check, which spends a real stylist call
and the address's free look for the day. It needs only `curl`. Run it after every deploy (`LAUNCH.md`, 1.6).

**`/healthz` and `/readyz` answer different questions.** `/healthz` is liveness — `ok` when the database answers — and
it is what `fly.toml`'s check, the Dockerfile's health check and an uptime monitor poll; it is untouched. `/readyz` is
readiness: public, `Cache-Control: no-store`, and a small JSON document `{ ok, checks }` where each check is `"ok"` or
a short reason. It checks `db` (a query answers and the schema is at the current migration), `storage` (`Storage:Root`
exists and a file can be written and removed there) and, only while `Storage:Transcode` is on, `ffmpeg` (the binary is
found). Every check ok is a 200; anything failing is a **503** naming the failing checks. A reason never carries a
path, a version or a secret, so the line is safe to leave public and to point a monitor at. Wait on `/readyz` after a
deploy; keep polling `/healthz`.

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
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --backup /data/backups/manual --keep 7"   # prints database: ... storage: ...
fly ssh console -u app -C "tar czf /data/backups/manual/storage-<stamp>.tgz -C /data/backups/manual storage-<stamp>"
fly ssh console -u app -C "tar czf /data/backups/manual/keys-<stamp>.tgz -C /data keys"           # the session keys; see below
fly sftp get /data/backups/manual/orevosh-<stamp>.db ./orevosh-<stamp>.db
fly sftp get /data/backups/manual/storage-<stamp>.tgz ./storage-<stamp>.tgz
fly sftp get /data/backups/manual/keys-<stamp>.tgz ./keys-<stamp>.tgz
fly ssh console -u app -C "rm -rf /data/backups/manual"   # the copies share the 3 GB volume with the live data
```

`--keep <n>` prunes the folder to the `n` newest database copies and the `n` newest storage copies once the new one is
complete, so a weekly run cannot
fill the volume even if the last line is forgotten; without it nothing is pruned. The files hold every photo and clip
people gave the app; keep them as private on your computer as they are on the volume (server path, step 9, says how).
There is no cron on Fly's machine: run this weekly from your computer, or from a scheduled GitHub Actions job with a
`FLY_API_TOKEN` secret (`fly tokens create deploy`).

**`--backup` does not take `/data/keys`, and that is why the third line is there.** `/data/keys` is the Data
Protection key ring: the keys that encrypt every session cookie. The app persists them there deliberately — beside
the database, **outside** `Storage:Root`, so a media backup never carries the keys off with the photos — and a
Fly volume snapshot holds them because a snapshot is the whole volume. A copy you take by hand does not, unless you
take it. **What a missing key ring costs:** nothing in the database, and everybody signed out. A restore onto a
volume with no `/data/keys` mints a fresh one, and every phone in the pilot is signed out at once — with mail
unconfigured, "forgot password" cannot bring them back either. They are about as secret as the photos are private:
a stolen key ring forges sessions, so keep the `.tgz` where you keep the rest and no looser.

Putting a copy back is the one place a server is simpler, because the app is running while you swap the file. At a
quiet moment: `fly sftp shell`, then `put orevosh-<stamp>.db /data/incoming.db`, then

```bash
fly ssh console -C "sh -c 'cd /data && mv incoming.db orevosh.db && rm -f orevosh.db-wal orevosh.db-shm && chown app:app orevosh.db'"
fly machine restart <machine id>
```

**The `chown` is not optional.** `fly sftp` and a bare `fly ssh console` are root, the app runs as the image's `app`
user (the `USER app` line in the `Dockerfile`), and a database file owned by root is one the app cannot write: the
restart comes up with `unable to open database file` and the site stays down. Check it afterwards with
`fly ssh console -C "ls -l /data/orevosh.db"` — it should say `app app`.

Writes between the swap and the restart are lost. The media folder goes back the same way: `put` the archive, `tar
xzf` it over `/data/storage`, and hand that back too — `fly ssh console -C "chown -R app:app /data/storage"`.

**Swapping the database alone does not sign anyone out**, because `/data/keys` is still sitting on the volume beside
it — the sessions keep working over a database that has just been rolled back, which is what you want. The one case
that needs the key ring back is a restore onto a **new** volume (a Fly snapshot carries it; a copy you took by hand
does not):

```bash
fly sftp shell                                          # put keys-<stamp>.tgz /data/keys.tgz
fly ssh console -C "sh -c 'cd /data && rm -rf keys && tar xzf keys.tgz && rm keys.tgz && chown -R app:app keys && chmod 700 keys'"
fly machine restart <machine id>
```

Put it back **before** the first start on the new volume if you can: the app mints a fresh key the moment it starts
without one, and every session issued against that fresh key dies when the old ring replaces it — so people who
signed in during the gap are signed out a second time.

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
  2 and `Plans__FreeCallsPerMonth` 20, `Plans__ProChecksPerDay` 30 and `Plans__ProCallsPerMonth` 150,
  `Plans__GuestChecksPerDay` 1), the per-account ceiling `Limits__ChecksPerDay` and the
  global `Limits__ChecksPerDayGlobal` (1000) are the caps. Guest checks are calls nobody signed up for: `fly logs` shows
  `Guest sweep: …` once an hour with how many went unclaimed.
- **The board:** a few minutes after the week closes (Saturday midnight in `Board__TimeZone`), `fly logs` shows
  `Board: week 2026-09-06 closed, 38 rows`; a quiet week says `Board: week 2026-09-06 had no counted fires, nothing to
  close` once; `Board: the close failed; it runs again in five minutes` is a warning worth reading, the next run
  retries. The lines the closer, the moderator and the zone write are listed under "The weekly board and store links".
- **Health from outside:** an uptime checker (UptimeRobot, Better Stack) on `https://<your-app-name>.fly.dev/healthz`.
  Fly restarts a machine whose own check keeps failing. Point a second, quieter check at `/readyz` if you want to hear
  about a full disk or a missing ffmpeg before a person does: it answers 503 with the failing check named.
- **The settings, when something is off:** `fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --doctor"` reads
  the configuration and this machine and prints one line per check; `--doctor --live` adds the calls that leave it
  (one Anthropic call, and Stripe when it is on; the mail server is read but never dialled). `--stripe-check` is the Stripe half alone.
- **Cost:** `fly dashboard` shows the month. A shared-cpu-1x with 512 MB is about 3 USD, the 3 GB volume about 0.45 USD,
  bandwidth for a pilot is inside the free allowance.

## Email for account recovery

An account has no email address until its owner adds one in Settings. With one, confirmed by a link the app sends, a
forgotten password is reset from the sign-in screen by another link; without an address, a forgotten password still
means a new account. The app sends those two mails itself over SMTP, to any provider. With the settings below unset,
mail is off: the client says recovery is not set up on this server and the links are written to the app's log instead
(`docker compose logs app` or `fly logs`), fine for a laptop pilot, not for people you cannot reach by hand.

Set them like every other setting: in `.env` on a server, with `fly secrets set` on Fly. The blocks below are
written as `.env` lines. On Fly they are the same names and values, but the shell reads the line first, so quote
any value with a space or an angle bracket in it: `fly secrets set "Email__From=OREVOSH <hello@looks.example.com>"`
(unquoted, `<` is a redirect and the shell answers `No such file or directory`). `LAUNCH.md` 1.5 has the whole
block in its Fly shape.

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

Every check is a paid model call, so the plans are caps, not features. **Each plan has two caps: a month and a
day.** The month is the allowance the plan is really sold on and the one the bill is built from; the day is a burst
brake, there so one person cannot spend a month of calls in an afternoon. A visitor gets one free check as a guest, a
free account gets two a day and twenty a month, OREVOSH Pro gets 150 a month with 30 a day as its brake, and
`Limits__ChecksPerDay` (30) is the ceiling no plan's day exceeds; checks and "Which one?" comparisons share both
allowances. All of it is settings, in `.env` on a server and `fly secrets set` on Fly:

| Variable | What to put |
|---|---|
| `Plans__FreeChecksPerDay` | Checks a day on Free. `2` by default: a taste, not the habit. Two and not one because a free account is the app's content and the audience a brand pays to reach — an app that feels like it is chasing money on the first screen dies before the money arrives |
| `Plans__FreeCallsPerMonth` | Checks a **month** on Free, `20`. The backstop behind the day: two a day for thirty days would be sixty paid calls given away, and this is the number that actually bounds it. `0` removes the monthly bound |
| `Plans__ProChecksPerDay` | Checks a day on Pro, `30`. Clamped to `Limits__ChecksPerDay`. This is Pro's **burst brake, not its promise** — it is deliberately not advertised anywhere in the app, because "30 a day" reads as a boast to somebody who checks twice — and the start log warns when the plan's number is above the ceiling |
| `Plans__ProCallsPerMonth` | Checks a **month** on Pro, `150`. This is the number Pro is sold on, the one the Pro page and the check screen quote, and the one the price is built from. Raise it and you raise the model bill for every subscriber directly: at the measured ~$0.02 a call, 150 is about $3.06 of model cost a month. `0` removes the monthly bound and falls the Pro page back to quoting the day |
| `Plans__GuestChecksPerDay` | Free checks for a visitor with no account, `1`, per guest cookie over a rolling day, counted from looks actually given (a refused photo, a model outage or a dropped connection spends nothing). `0` turns guests off and the check screen asks to sign in |
| `Plans__GuestChecksPerAddressPerDay` | The same per client **address**, `10`, over a rolling day and in memory (a restart forgets it). Far above the per-cookie cap on purpose: one address is a household, an office or a whole carrier, so equal numbers meant the second person you showed the app to was refused before taking a photo. Refuses with `error.too_fast`, not `error.guest_limit` |
| `Plans__GuestAttemptsPerDay` | The brake on attempts at the check route from a visitor, `20` per client address per 24 hours whatever they come to (429 `error.too_fast`). Well above the guest cap on purpose, so a refused photo never locks a shared address out of its look |
| `Plans__ProPriceText` | What the Pro page shows as the price, e.g. `₪19 / month` or `$5 / month`. Text only; empty hides it |
| `Plans__CompareNeedsPro` | `false`. Set `true` to keep "Which one?" for Pro accounts |
| `Plans__NoOutfitForgivenPerDay` | `3`. How many "that is not an outfit" answers a day do not count against a person's own allowance. The model call was still made and the global ceiling still counts it; this is about not punishing somebody for a photo the stylist could not read |
| `Plans__ProComparesPerDay` | **Round 14**, `30`. A Pro account's OWN rolling-day allowance for "Which one?", counted apart from its checks, so deciding between two outfits never spends a check. Never above `Limits__ChecksPerDay`; a free account keeps one allowance for both. Thirty is a guess with the same shape as the check cap — move it once real Pro accounts exist and the numbers page's `spend` block says what they cost |
| `Plans__WardrobeMaxItems` | **Round 14**, `200`. The most pieces one account may keep. A brake on a script, not a product limit, and the same for free and Pro |
| `Plans__WardrobeNamesToStylist` | **Round 14**, `12`. How many of the wearer's own piece names travel with a check and with a comparison, most recently worn first, so a tip can name something they already own. `0` keeps the wardrobe and never sends it, and `--doctor` says so on its `plans` line |
| `Plans__WardrobeNeedsPro` | **Round 14**, `true`. Whether the wardrobe **reaching the stylist** is Pro's. The list itself is everyone's on every server — it cannot fill itself behind a wall — so this gates only the advice from it |
| `Plans__TasteProfile` | **Round 14**, `true`. Whether this server has the taste profile built (`Services/Taste.cs`). It is, so it is on. Turning it off takes the benefit off the Pro page in the same breath as it stops the advisory being built. **`.env.example` still shows this commented out as `false` with a note to leave it off — that comment predates the feature landing; the shipped default is `true` and the example line is the one to ignore** |
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
3. A webhook endpoint at `https://looks.example.com/api/billing/webhook` subscribed to the five events the app reads:
   `checkout.session.completed`, `customer.subscription.created`, `invoice.paid`, `customer.subscription.updated` and
   `customer.subscription.deleted`; copy its signing secret (`whsec_…`). `--stripe-check` names any you missed.
4. Settings → Billing → **Customer portal**: open it once and save a configuration (what a customer may do there —
   cancel, change the payment method). Stripe creates no portal session until that configuration exists, and the app's
   "Manage subscription" is a portal session, so without this it answers 502 `error.portal_failed`.

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
top of any period still running and stores the customer id and the subscription id (`customer.subscription.created`
is the same record for a subscription that started on Stripe's own side, and grants nothing); `invoice.paid` (except
the first, `subscription_create`, which the checkout already counted) moves the end to the invoice's period end plus
three days, from the event and never below the current end; `customer.subscription.updated` follows the status,
`active` or `trialing` to the current period end plus three days (never below the current end), `past_due`, `unpaid`
or `paused` down to three days from now at most;
`customer.subscription.deleted` ends it now; a missed renewal simply lapses. No event ids are kept: a repeated
`checkout.session.completed` stacks one period, every other repeat names the same period and changes nothing or ends
what already ended. An account that is Pro already cannot open a second Checkout (409). Card details never reach the
app, and the secret key is redacted from the app's logs.

**Before a test purchase, ask Stripe whether what you set is real:**

```bash
docker compose exec app dotnet FitCheck.Api.dll --stripe-check
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --stripe-check"
```

It prints three lines. `billing` is read from the settings alone: the provider, the three keys and their prefixes.
`stripe-live` is a `GET /v1/prices/<your price>` with your secret key: the key is accepted, the price exists in the
same mode as the key (a live key cannot see a test price), and it is a **recurring** price that is not archived —
Checkout opens in subscription mode and refuses a one-time price, which is otherwise discovered by the first person
to press Go Pro. `stripe-webhook` is a `GET /v1/webhook_endpoints`: one endpoint is registered for
`Billing__PublicOrigin` + `/api/billing/webhook`, it is enabled, and its events cover the five above (`*` counts). A
missing event is named, so "registered, but not for `customer.subscription.deleted`" tells you cancellations would
never end Pro. An endpoint on the app's own route under a different name — the `fly.dev` address beside your domain —
is a warning naming it, not a failure: the webhook only has to reach the route.

Two GETs, nothing written, nobody charged, and no secret printed, so run it after every change to a Stripe value and
again when test keys become live ones. `--doctor --live` includes both lines.

**Cancelling and changing a plan happen on Stripe's page, not here.** `POST /api/billing/portal` opens a Billing
Portal session for the account's customer and returns to `/#/settings`; the app shows it as "Manage subscription" in
Settings and on the Pro page, and it appears only for a Pro account that went through Checkout. An account whose Pro
came from `--pro` has no customer on Stripe's side, so it sees "To change or cancel, write to us." instead — which is
also what the whole app says while the provider is `manual`, and the reason the support mailbox has to be real.

To try it before people pay: keep test keys, install the Stripe CLI and run `stripe listen --forward-to
localhost:5000/api/billing/webhook` (it prints a `whsec_` for the session; put that in `Billing__StripeWebhookSecret`),
pay with Stripe's test card `4242 4242 4242 4242`, and watch the app's log say `Account <handle> is Pro until …`. Then
press "Manage subscription" and cancel from the portal, and watch Pro end where the webhook says it does.
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
| `Board__CacheSeconds` | How long the running week and the one before it are served from memory, `60` seconds, real time, per process (two entries at most; every other week is computed on each read). `0` turns the cache off |
| `Board__Sponsor__Name`, `Board__Sponsor__Handle`, `Board__Sponsor__PrizeText`, `Board__Sponsor__Url` | The week's sponsor: while `Name` is set the board shows "Presented by <name>" (linked to the account when `Handle` names one, else to `Url`), the prize line and the site's host. `Url` must be an `http(s)` link with a host and no user info; a bare host such as `nexor.example` is read as `https://nexor.example`; anything else is dropped at start with `Board: the sponsor link <url> is not an http(s) URL; the board shows the sponsor without a link` in the log, and the name shows without a link. There is no self-service: a brand that sponsors a week is one you agreed a prize with, verified with `--verify`, and put here by hand; unset it when the week is over |
| `Affiliate__Hosts__<host>` | One line per programme you joined: `Affiliate__Hosts__amazon.com=tag=orevosh-20` appends `?tag=orevosh-20` (or `&tag=…`, before any `#fragment`) to every store link that leaves for `amazon.com` or a subdomain of it. Nothing is stored on the link: the parameters are added at the door, so joining, changing or leaving a programme is one line for every link at once. Leave every line out until you have joined a programme; with none, every link is redirected as given |
| `Affiliate__Disclosure` | `true`. Whether the item sheet shows "This link may earn OREVOSH a commission." under a store link, listed host or not: `/api/config` publishes it as `affiliate.disclosure` and the sheet reads it ("Leaves OREVOSH" shows under every store link either way). Keep it on; programme terms and consumer law expect the line |

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
- `Board: the sponsor link <url> is not an http(s) URL; the board shows the sponsor without a link`, a warning at start:
  `Board__Sponsor__Url` is not `http(s)` (a bare host is read as `https://`; `javascript:`, `ftp:`, `mailto:` and a
  link with user info are dropped). The name and the prize still show; fix the line and restart.
- `Board: <look id> excluded by <moderator id>: <reason>` and `Board: <look id> put back by <moderator id>`: a moderator
  pulled a look off the board through `POST /api/admin/board/exclude` (with `{ postId, reason }`) or put it back with
  `DELETE /api/admin/board/exclude/<postId>`. There is no screen for it yet; a moderator's session and the CSRF header
  do it from a terminal: `curl -X POST -b 'orevosh.session=…' -H 'X-Requested-With: Orevosh' -H 'Content-Type:
  application/json' -d '{"postId":"…","reason":"bought fires"}' https://looks.example.com/api/admin/board/exclude`.
- `Items: 3 on post <look id> by <user id>`: someone saved the pieces on their look.

**What to know before people rely on it.** The board serves the running week and the one before it from memory for
`Board__CacheSeconds` (60 seconds, real time) per process, two weeks at most, and computes every other week on each read
(the Explore strip and the reset card on Home read the same route, and every answered read counts as a `boardViews` in
the metrics); `?week=` answers from the week of the first look through next week and refuses the rest with a 400; the
out door allows sixty taps a minute per client address, and both windows live in the one `app` process.
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
| `Plans__FreeChecksPerDay`, `Plans__FreeCallsPerMonth`, `Plans__ProChecksPerDay`, `Plans__ProCallsPerMonth`, `Plans__GuestChecksPerDay`, `Plans__GuestChecksPerAddressPerDay`, `Plans__GuestAttemptsPerDay`, `Plans__ProPriceAmount`, `Plans__ProPriceCurrency`, `Plans__ProPriceText`, `Plans__CompareNeedsPro`, and the Round 14 four (`Plans__ProComparesPerDay`, `Plans__WardrobeMaxItems`, `Plans__WardrobeNamesToStylist`, `Plans__WardrobeNeedsPro`, `Plans__TasteProfile`) | The day caps (2, 30, 1) and the month caps (20 Free, 150 Pro — the ones the plans are really sold on), the per-address guest number (10), the brake on guest attempts (20), the price and its currency (the browser formats it per the reader's region) with `ProPriceText` as the override for a price no format covers, and what Pro actually sells. The defaults are fine for a pilot; "Plans and billing" above. |
| `Billing__Provider`, `Billing__StripeSecretKey`, `Billing__StripePriceId`, `Billing__StripeWebhookSecret`, `Billing__PublicOrigin` | Leave the provider at `manual` (Pro by the `--pro` command) until Stripe is set up and tested in test mode; "Plans and billing" above. The three Stripe keys are secrets. |
| `Board__TimeZone`, `Board__WeekStartsOn`, `Board__MinChecksToCount`, `Board__MaxPerFirerPerAuthor`, `Board__NewAccountDays`, `Board__Size`, `Board__RisingDays`, `Board__CacheSeconds`, `Board__Sponsor__Name` (+ `Handle`, `PrizeText`, `Url`) | The weekly board: the zone and the day the week is cut on (`Asia/Jerusalem`, `Sunday`; set them before the first week runs), the rules that decide which fires count (1 check, 3 per pair, 2 days), the size (10), the rising window (30), the memory cache (60 seconds, two weeks at most) and the week's sponsor, by hand (its `Url` an `http(s)` link, or it is dropped with a warning). The defaults are the pilot's; "The weekly board and store links" above. |
| `Affiliate__Hosts__<host>` | One line per affiliate programme you have joined, e.g. `Affiliate__Hosts__amazon.com=tag=orevosh-20`: appended when a store link leaves for that host. Leave it out until you have joined one; with no line nothing is appended. The commission line under store links shows while `Affiliate__Disclosure` is `true`, the default. |
| `Logging__Requests` | `true` for the first days of a launch: one extra log line per request, with the method, the path, the status and how long it took. Off by default, because it is a lot of lines; no body, cookie, header or query value is ever logged. Turn it off again once things are quiet. |

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
answers `ok` when the app can reach its database, and `https://looks.example.com/readyz` answers
`{"ok":true,"checks":{…}}` when the database, the media folder and (while `Storage:Transcode` is on) ffmpeg are all
good — a 503 there names what is not (the Fly path, step 5, explains the two lines). `docker compose exec app dotnet
FitCheck.Api.dll --doctor` is the same question asked of the settings, and `scripts/smoke.sh https://looks.example.com`
from your laptop is the whole read-only smoke in one go (the Fly path, step 5).

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

The app has ten maintenance commands. None starts the server; all run from `/opt/orevosh`:

| Command | What it does |
|---|---|
| `docker compose run --rm --no-deps app dotnet FitCheck.Api.dll --vapid` | Prints a VAPID key pair for push (step 8). Needs no database, so it works before the first start |
| `docker compose exec app dotnet FitCheck.Api.dll --doctor` | Reads the configuration and this machine and prints one line per check — the database, the storage folder, ffmpeg, the Anthropic key and its model, the mail settings and their public origin, the push keys, the billing settings, the plan caps, the board's time zone, the moderator list (`Admin__Handles`, and the accounts `--admin` promoted, counted in the database), the affiliate hosts, the free disk space, whether the shipped pages still carry the placeholder host in their link previews, what a model call is priced at here with the day's spend ceiling, and whether any alert channel is set at all — **seventeen lines**, each `ok`, a warning, or a short reason, then the tally (`doctor: 9 ok, 7 warnings, 1 failure`, with your own run's numbers) and the verdict. Exit 0 when everything a live server needs is in place, 1 otherwise, so a deploy script can gate on it; only a failure changes the exit code, a warning is your call. Run it after every settings change |
| `docker compose exec app dotnet FitCheck.Api.dll --doctor --live` | The same, plus the checks that leave the machine: one small call to Anthropic with the configured key and model (a few hundred tokens, a fraction of a cent), and two reads from Stripe when the provider is `stripe`. **The mail server is never dialled** — no command in this app opens an SMTP connection; ask the app for a password reset with your own address to test the sender. This is the one that tells you whether a broken check is you or the provider |
| `docker compose exec app dotnet FitCheck.Api.dll --stripe-check` | The Stripe half of `--doctor --live` on its own: whether the secret key works, whether the price id exists, is recurring and is not archived, and whether an enabled webhook endpoint is registered for `Billing__PublicOrigin` + `/api/billing/webhook` with the five events the app reads. Two GETs; it writes nothing, charges nobody and prints no secret ("Plans and billing") |
| `docker compose exec app dotnet FitCheck.Api.dll --backup /data/backups/nightly` | A consistent copy of the database and the media folder into that folder on the volume (step 9; `tools/backup.sh` wraps it and brings the copies out). `--keep <n>` after the folder also prunes it to the `n` newest database and storage copies once the new one is complete — it prunes whatever it finds there, so give each writer a folder of its own. `scripts/backup.sh` is the cron-able wrapper that leaves the copies on the volume, and `/data/backups/nightly` is the folder it defaults to |
| `docker compose exec app dotnet FitCheck.Api.dll --admin <handle>` | Makes an existing account a moderator |
| `docker compose exec app dotnet FitCheck.Api.dll --unadmin <handle>` | Takes that away |
| `docker compose exec app dotnet FitCheck.Api.dll --verify <handle>` | Marks an existing brand account as verified: a check inside its BRAND mark everywhere it appears, from its next request. You are the process: run it for a brand once you know who is behind the account |
| `docker compose exec app dotnet FitCheck.Api.dll --unverify <handle>` | Takes that away |
| `docker compose exec app dotnet FitCheck.Api.dll --pro <handle> <months\|off>` | Puts an existing account on Pro for that many months (31 days each, from now; 1 to 120) or back on Free ("Plans and billing") |

`docker compose exec` needs the app running. The account commands exit with 1 when no account has the handle and 2 on
a usage error. On a laptop the same commands are `dotnet run -- --admin <handle>` and so on (the README, "Maintenance
commands"). `node tools/brand/set-origin.js https://looks.example.com` is not one of them: it edits files in the repository
before a build, not the running app (the top of this page).

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
would otherwise keep the container's world-readable modes). Nothing stays on the data volume: the scratch folder it
makes inside the container (`mktemp -d /data/backup-scratch.XXXXXXXX`, a fresh name every run) is removed whether the
run succeeds or fails halfway.

**Neither script takes the session keys, and one line a week fixes that.** `/data/keys` is the Data Protection key
ring — the keys that encrypt every session cookie. The app keeps it beside the database and **outside**
`Storage:Root` on purpose, so a media copy never carries the keys off with the photos, which also means the two
backup scripts do not see it. It is tiny and it almost never changes, so take it once and again after anything that
touches the volume:

```bash
docker compose cp app:/data/keys "backups/keys-$(date -u +%Y%m%d%H%M%S)"
chmod -R go-rwx backups
```

**What a missing key ring costs:** nothing in the database, and everybody signed out. Restore the database and the
photos onto a machine with no `/data/keys` and the app mints a fresh ring on first start; every phone in the pilot is
signed out at once, and with mail unconfigured "forgot password" cannot bring them back. Putting one back is the same
idiom `tools/restore.sh` uses — one throwaway container on the same volume, as root, with the copy mounted in:

```bash
docker compose stop app
docker compose run --rm --no-deps --user root \
  -v "$(realpath backups/keys-<stamp>):/restore/keys:ro" --entrypoint sh app -c \
  'rm -rf /data/keys && cp -r /restore/keys /data/keys && chown -R app:app /data/keys && chmod 700 /data/keys'
docker compose start app
```

Do it **before** the first start on a fresh volume if you can: the app mints a key the moment it starts without one,
and every session issued against that key dies when the old ring replaces it — so anyone who signed in during the gap
is signed out a second time. Keep the copy as private as the photos: a stolen key ring forges sessions.

**The two backup scripts never share a folder, and that is the point.** `tools/backup.sh` empties the scratch folder
it made on every exit path; `scripts/backup.sh`, the other half, leaves its copies **on** the volume in
`/data/backups/nightly` and lets the app prune them. Nothing either one removes is anything the other wrote, so a
nightly cron and a manual copy taken five minutes later cannot cost you the history. If you point either at a folder
of your own, give it one nobody else writes to: the app's `--keep` prunes every `orevosh-*.db` and `storage-*` in the
folder it is given, whoever put them there.

Sizes: the database is small (megabytes for a pilot), a storage copy is the whole media folder. So the script keeps the
last `KEEP` (default 14) database copies but only the last `KEEP_STORAGE` (default 2) storage copies. (The app's own
`--backup <dir> --keep <n>` prunes the same way inside the container, which is what a copy taken by hand or on Fly
needs; `tools/backup.sh` prunes on the host, where the copies you keep actually live.) At the moment a
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

Leave the second argument out to restore the database only. `tools/restore.sh` never touches `/data/keys`, so a
restore on the machine the backup came from keeps everyone signed in: the key ring is still there beside the database.
Only a restore onto a **fresh** volume needs the key copy above.

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

Copy the whole `backups` folder to the server, keeping the modes, then restore from it there:

```bash
scp -rp backups root@looks.example.com:~      # still in src/FitCheck.Api; lands in ~/backups on the server
ssh root@looks.example.com
cd /opt/orevosh
tools/restore.sh ~/backups/orevosh-<stamp>.db ~/backups/storage-<stamp>
```

The first line runs from `src/FitCheck.Api`, where the block above left you. `scp -r <folder>` copies the folder and
not its contents, so the files land at `~/backups/…` and not `~/…`; `<stamp>` is the one the `--backup` line printed.
`tools/restore.sh` asks before it replaces anything, so run it where you can answer it.

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
- **Readiness.** `https://looks.example.com/readyz` is the fuller answer: `{ ok, checks }` with `db`, `storage` and,
  while transcoding is on, `ffmpeg`, each `"ok"` or a short reason, 503 when one fails. It is where a full disk or a
  missing ffmpeg shows up first. `docker compose exec app dotnet FitCheck.Api.dll --doctor` covers the settings that
  `/readyz` cannot see, and `--doctor --live` adds Anthropic and Stripe. It does not add mail: nothing here dials an
  SMTP server, so a password reset you ask for yourself is the test of the sender.
- **Logs.** `docker compose logs --since 1h app` for the app (every 5xx is logged with the path), `docker compose logs
  caddy` for certificates and traffic. Logs are rotated by Docker.
- **The model bill.** The plan caps (`Plans__FreeChecksPerDay` 2 / `Plans__FreeCallsPerMonth` 20,
  `Plans__ProChecksPerDay` 30 / `Plans__ProCallsPerMonth` 150, `Plans__GuestChecksPerDay`
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
`Cache-Control: no-store`, refuses a hidden look and takes sixty taps a minute per address), a `Permissions-Policy`
that keeps camera and microphone to the app itself, and a **`Content-Security-Policy`** with `script-src 'self'` (no
`unsafe-inline`, no nonce) — see item 4 below for the two openings that remain. Store links themselves are stored
only when they are `http(s)` with a host and no user info, and are never the `href` a person taps. The session
cookie's encryption keys are persisted on the data volume beside the database, at `/data/keys`, and deliberately
outside `Storage:Root`: without that they would live in the container and a deploy would sign everyone out. They are
a secret — "Backups" says how to take a copy and how private to keep it.

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
4. **The fonts are still off-origin.** The Content-Security-Policy is set — that item used to say it was not, and it
   has been since Round 13 (`Services/Security/SecurityHeaders.cs`, asserted by `SecurityTests` on `/`, `/landing/`,
   an API answer and an error): `default-src 'self'`, **`script-src 'self'` with no `unsafe-inline` and no nonce**,
   `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'`, `img-src` and `media-src`
   allowing `blob:` for the share card and the share video. Two deliberate openings remain. `style-src` keeps
   `'unsafe-inline'` because the design system sets `style` attributes from code and appends one `<style>` per view
   — a nonce cannot cover an attribute and a hash cannot cover a computed width, and CSS injection is not code
   execution while every user string reaches the page as text. And `style-src`/`font-src` still name
   `fonts.googleapis.com` and `fonts.gstatic.com`, which is a third party on every cold start. Self-hosting woff2
   subsets under `/fonts` (Latin, Hebrew, Arabic) closes that one and lets both hosts leave the policy.
5. **Brand verification is by hand** (`--verify`, no form and no process behind it), and **one process only**: the
   checks-per-day reservation, the per-address guest count and the rate limiters' windows live in memory, so run one
   `app` container (one machine on Fly). Multiple instances need a shared store.
6. **The rate limiters trust `X-Forwarded-For`**, which is right behind Caddy on the private compose network and
   behind Fly's edge; never publish port 8080 on a host.

## Go-live checklist

Before the address leaves the team. The order is [`LAUNCH.md`](LAUNCH.md)'s, so the two pages can be worked through
side by side: everything under **Before you start** is that runbook's section 0, **Deploy and prove it** is its
sections 1 and 2, and so on. Each item says where in this page the detail is.

### Before you start (LAUNCH.md, section 0)

1. **The Anthropic key, with a spend limit.** Set a monthly limit in the console; it is the only backstop that is not
   the app's. The plan caps (`Plans__FreeChecksPerDay` 2 / `Plans__FreeCallsPerMonth` 20, `Plans__ProChecksPerDay` 30 /
   `Plans__ProCallsPerMonth` 150, `Plans__GuestChecksPerDay` 1),
   the ceiling `Limits__ChecksPerDay` (30) and `Limits__ChecksPerDayGlobal` (1000) cap the volume from the app's side.
   A thousand checks a day is a real bill: do the arithmetic for your model and your pilot's size before raising any of
   them, and remember that guest checks are calls made by people who never signed up (`Plans__GuestChecksPerDay=0`
   closes that door). `LAUNCH.md` 0.3 has the per-check estimate and where to read the real number.
2. **A support mailbox that someone reads.** `hello@<your domain>`, on the store listing, in the legal pages, and the
   address the app tells people to write to while `Billing__Provider` is `manual` ("To change or cancel, write to
   us."). The pages ship with `hello@orevosh.app` in them; change it.
3. **The legal pages reviewed by a lawyer, and the guidelines by you.** `#/terms` and `#/privacy` (version 2, dated
   2026-09-12, in both languages) describe what the code does in plain words and are not legal advice: the
   governing-law line is a placeholder, the contact address must be a mailbox someone reads, and the stores want both
   pages at a public URL. The guidelines page (linked from signup with the two) says what gets reported and what
   happens to a report, what deletion removes, and that the photos are the person's own. Change anything you would not
   stand behind; the version line at the bottom moves with the text (`views/legal.js`).
4. **Age is self-declared, and the limits say so.** A date of birth typed at signup is not age assurance; the README's
   "Known limitations" is the honest list. Read it, decide who you invite, and plan the age-signal integration before
   a public launch.

### Deploy and prove it (LAUNCH.md, sections 1 and 2)

5. **The production origin in the code, before the build.** `node tools/brand/set-origin.js https://looks.example.com`
   writes your domain into eleven files it knows by name: the Open Graph and Twitter tags in
   `src/FitCheck.Api/wwwroot/index.html` (`og:image`, `twitter:image`; `og:url` was deleted on purpose, so a shared
   link unfurls with the address it was actually fetched from), the absolute URLs at the top of
   `wwwroot/landing/index.html` and
   `index.he.html` (canonical, both `hreflang` links, `og:image`, `twitter:image`),
   `mobile/capacitor.config.json` (`server.url`, `allowNavigation`), and then the documents that quote the origin —
   `mobile/README.md` (including its `WKAppBoundDomains` note), `STORE.md` (including the URL table), `MARKETING.md`,
   `brand-kit/README.md`, `DEPLOY.md`, `README.md` and `.env.example`. The first four are static files inside the
   image, so this runs **before** `fly deploy` or `docker compose build`. Nothing here is left for you to hand-edit;
   read the diff anyway, because the sample email addresses it rewrote are a guess at your mailbox.
   `node tools/brand/set-origin.js --check` exits 1 while any placeholder is left. Then paste a link into a chat app
   and check the card shows the 1200×630 image.
6. **A domain and HTTPS.** `fly certs add`, or steps 2 and 6 of the server path. `https://…/healthz` answers `ok`,
   the padlock is there on a phone, and the app installs to the home screen.
7. **Green before anyone arrives.** `curl -s https://…/readyz` answers `{"ok":true,…}` — `db`, `storage` and, while
   `Storage:Transcode` is on, `ffmpeg`, each `"ok"`; a 503 names what is not. Then `--doctor` for the settings
   `/readyz` cannot see, and `--doctor --live` once for the things outside the machine (Anthropic, and Stripe when it
   is on — never the mail server, which no command dials; item 11 says how to test that). Keep the uptime checker on
   `/healthz`; `/readyz` is the one you read when something is wrong. `scripts/smoke.sh https://…` from your laptop
   asks the read-only half of this in one go, eight `OK` lines and exit 0, and is the line to run after every deploy.
8. **The first moderator**, signed up and then promoted with `--admin` (Fly step 6, server step 7). Two is better than
   one: someone has to look at the queue every day.
9. **The first brands verified.** Each brand account that you know is the brand (you spoke to them, the handle is on
   their site) gets `--verify <handle>` and the check inside its BRAND mark; anyone else stays a self-declared brand.
   `--unverify` when that changes. `MARKETING.md`'s "week 0" is when this happens.
10. **VAPID keys** set once (`--vapid`) and never regenerated.
11. **Email** pointed at a real provider, `Email__PublicOrigin` set to the domain, SPF and DKIM verified with the
    provider, and tested with your own address — which means asking the app for a password reset and watching the mail
    arrive, because `--doctor` reads the settings and never dials the server. Without a provider a forgotten password
    means a new account; without the origin, no link goes out on a server.

### The settings that must be right before the first week

12. **The board's week is your users' week.** `Board__TimeZone` and `Board__WeekStartsOn` (`Asia/Jerusalem`, `Sunday`)
    cut the week and label the archive; set them to where your people live before the first week runs, because a
    change later moves every week's edges. The morning after the first close, `grep "Board:"` in the log should show
    `closed, N rows`, and `#/board/hall` the week ("The weekly board and store links").
13. **Decide the sponsor.** `Board__Sponsor__Name` (with `Handle`, `PrizeText`, `Url`) puts "Presented by" on the board
    with the prize; leave it unset until a brand has agreed to a prize with you, and unset it again when the week is
    over. `Url` is an `http(s)` link (a bare host is read as `https://`); anything else is dropped with a warning in
    the start log and the name shows without a link. There is no self-service, so this is your word on the board.
14. **Affiliate hosts only for programmes you joined, and keep the disclosure on.** One `Affiliate__Hosts__<host>` line
    per programme whose terms you accepted; with none, nothing is appended and nobody earns anything. The commission
    line shows under every store link while `Affiliate__Disclosure` is `true`, the default: leave it on, the programmes'
    terms and consumer law expect it. The store gets no referrer from the app.
15. **`--verify` the brands that tag products.** A brand account whose looks carry store links, and any brand that
    sponsors a week, is one you have spoken to; the check inside its mark says so. Anyone else's brand and model on a
    piece is their own word (README, "Known limitations"), and the queue is the answer when it is abused.
16. **Rate limits that fit the launch.** Signups per address (50 an hour), logins (30 per quarter hour), comments (30
    an hour per account) and reports (20 an hour per account) are pilot numbers; a launch party on one Wi-Fi needs
    `Limits__SignupsPerHourPerIp` raised for the evening, and the per-account ones (`Limits__CommentsPerHour`,
    `Limits__ReportsPerHour`) are meant to stay where nobody meets them by hand. The guest cap is per address too:
    one free check per address a day means a whole café shares one, which is the intended side; it counts looks given,
    so a refused photo does not spend the café's look, and `Plans__GuestAttemptsPerDay` (20) brakes attempts on top.
17. **Transcoding needs ffmpeg in the image and CPU to spare.** With `ffmpeg` on the machine the app re-encodes clips to
    H.264 MP4 in the background (`Storage__Transcode`, on by default; `Storage__FfmpegPath` when it is not on the PATH),
    so an Android WebM plays on iPhones. Check `https://…/api/config`: `"transcoding": true` means it is running;
    `false` means the image has no ffmpeg (add `ffmpeg` to the `apt-get install` line of the Dockerfile's runtime
    stage and redeploy) and clips play only where the sender's codec does — `/readyz` reports the same thing as a
    failing `ffmpeg` check. A 30-second clip takes on the order of a minute of a shared CPU; on Fly,
    `fly scale vm shared-cpu-2x` if the app gets sluggish while a clip converts.
18. **Watch the disk.** Clips are up to 40 MB each and a media backup is the whole folder: `df -h` weekly on a server,
    `fly ssh console -C "df -h /data"` on Fly, and grow the volume before it fills. A full disk stops uploads and, worse,
    writes.

### The first day (LAUNCH.md, section 3)

19. **Real screenshots in the store kit.** The files in `brand-kit/store/` and `wwwroot/landing/screens/` show the
    browser test's synthetic outfit and a fake camera; take real captures on a phone and re-run
    `tools/brand/render-kit.js` before any store submission (`brand-kit/README.md`, `STORE.md`).
20. **Backups running and copied off the box.** On a server: the nightly cron of step 9 and a weekly copy elsewhere.
    On Fly: the daily snapshots are on, plus a weekly `--backup /data/backups/manual --keep 7` and `fly sftp get` of your own.
    Restore one once, before you need it.
21. **Turn the request log on for the first days.** `Logging__Requests=true` adds one line per request — the method,
    the path, the status and how long it took — which is how you see what people actually do and what fails. Turn it
    off once the launch is quiet; it is a lot of lines.

### Money, and later (LAUNCH.md, sections 4 and 5)

22. **Plans and billing decided.** For a pilot leave `Billing__Provider=manual` and grant Pro with `--pro`; set
    `Plans__ProPriceText` only when there is a price. To charge, set up Stripe in test mode, save a Customer portal
    configuration in Stripe's dashboard, run `--stripe-check`, run the Stripe CLI against the webhook, pay once with
    the test card, press "Manage subscription" and cancel from the portal, and only then switch to live keys ("Plans
    and billing"). Never switch the provider to `stripe` with keys you have not tested.
23. **Before a store submission**, and not before you need it: real screenshots (19), the block-a-person feature
    (in the app since Round 11: the look's menu, the profile's menu, and Settings → Blocked accounts — Apple's
    guideline 1.2 requires it), the in-app data export and account deletion for the privacy forms, the support URL and
    the privacy policy URL, and the payments rule for the wrapped app. `STORE.md` and `mobile/README.md`.

### Round 13 — the verdict's own verdict, the honest no-outfit answer, languages shipped only when real (appended)

24. **Languages: enable only what a native reader has reviewed.** `Languages__Enabled__0=en` and `__1=he` are the
    default (English is always on). Arabic and Russian ship in the image, unreviewed: add `__2=ar` or `__3=ru` only
    after someone who reads the language has gone through `wwwroot/i18n/<code>.json` and the block in
    `Services/Localizer.cs`, then restart. `curl -s https://…/api/config | jq .languages` shows what is live; a browser
    in a language that is not gets English, and the stylist is never asked for one that is not.
25. **Watch the tip-landed rate.** `/api/metrics/pilot` (or `#/admin/metrics`, *The stylist*) now carries `stylist.useful.rate`,
    yes over yes and no; it is the one number that says whether the stylist is any good, and it is the number to read
    before touching the rubric. Under 60% for an intent is a calibration problem for that intent; under 60% for a
    language is a translation problem. The unanswered count says how many people the row never reached.
26. **No-outfit answers are forgiven three times a day (`Plans__NoOutfitForgivenPerDay`)**: they spend no allowance and
    no guest look, and the global ceiling counts them all the same. Leave it at 3; set it to 0 if a script is found
    feeding the door non-outfit photos (the attempts brake and the ceiling bound the damage either way).
27. **`/offline.html` is in the shell** (`orevosh-shell-v6`): a phone with no network that navigates to `/landing/` or
    opens the app before its shell was cached sees the brand and *Try again* instead of the browser's error page.
    `curl -sI https://…/offline.html` is a 200 like the rest of `wwwroot`.

### Round 13 — Money: the spend meter, the daily ceiling and the alerts (appended)

28. **Set a daily spend ceiling before you invite anyone.** `Limits__SpendPerDayUsd=5` for a pilot. Above it every
    route that would ask the model answers 503 "the stylist is resting until tomorrow", *before* the call: nobody's
    allowance is spent and nothing is stored. It opens again at the next UTC midnight. `0` (the default) means no
    ceiling at all, and `--doctor` warns about it on every run. `Limits__ChecksPerDayGlobal` stays as the count-based
    brake beside it.
29. **Put your real prices in.** `Anthropic__PriceInPerMillion` and `Anthropic__PriceOutPerMillion` are USD per million
    tokens and they are what every dollar on the numbers page and the ceiling above is computed from. The shipped
    defaults are the published list prices for the default model at the time this was written — if you are on a
    different model or a volume agreement they are wrong. `--doctor` prints the two in use; check them after any model
    change. These are estimates, never Anthropic's invoice: reconcile against the console monthly.
30. **Set at least one alert channel.** `Alerts__Webhook` is any https URL that takes `{ "text": "…" }` — a Slack
    incoming webhook as-is, or a Discord webhook URL exactly as Discord hands it to you (the app appends Discord's own
    `/slack` compatibility suffix itself, so you do not have to know about it). `Alerts__Email` is one address and goes
    through the same mail server as the recovery links, **so on its own it sends nothing**: without `Email__Host` and
    `Email__From` the address sits there and no alert ever reaches it, which the startup log and `--doctor` both say.
    The webhook is the channel that needs no mail server, which makes it the one to set first. **The webhook URL is a
    secret**: `.env` only, never `appsettings.json`, and never in a screenshot. You get: the app starting (with the
    version), readiness flipping to failing and back, the spend ceiling, more than `Alerts__ModelFailuresIn10Min`
    (default 5) failed model calls in ten minutes, free space under `Alerts__DiskFreeMb` (default 512), a failed
    `--backup`, and a refunded or disputed Stripe charge. At most one of each kind per hour. Prove it once:
    `docker compose exec app dotnet FitCheck.Api.dll --doctor --live` sends a test alert down every channel you set.
31. **Read the money block weekly.** `#/admin/metrics` → *Model spend*: today's estimate, the calls and tokens behind
    it, the ceiling, and the last 14 days as bars. A day that jumps without a matching jump in checks means retries,
    and the `model.failing` alert should have told you first.

**Stripe: what the dashboard has to do, and what it does not.**

- **Receipts.** The app sends none and never will — it holds no payment data. Stripe Checkout can email its own
  receipts, but only once you switch them on: Stripe Dashboard → Settings → **Customer emails** → "Successful payments"
  (and "Refunds"), per mode. Test mode and live mode are separate switches; turning it on in test does nothing for live.
  Until you do, a paying person gets nothing from anyone, which is the most common complaint of a first pilot.
- **Refunds and disputes.** Add `charge.refunded` and `charge.dispute.created` to your webhook endpoint's events
  (Developers → Webhooks → your endpoint → "Update details"). The app now handles both: Pro ends on that customer's
  account, a warning is logged, and an alert goes out. **`--stripe-check` does not yet require these two** — it checks
  only the five subscription-lifecycle events — so if you do not add them by hand, a chargeback silently leaves the
  person Pro. A dispute also has a response deadline and a fee; the alert exists to get you into the dashboard in time.
- **`past_due`.** Already handled before this round and unchanged: `customer.subscription.updated` with `past_due`,
  `unpaid` or `paused` leaves the person three days of slack and then Pro lapses. Stripe's own dunning settings
  (Settings → **Subscriptions and emails** → retries) decide how long it tries the card first; the app only reacts.
- **VAT and invoicing in Israel — get an accountant, not this file.** What is true and checkable: Stripe Tax can
  calculate and collect tax on Checkout if you enable it and register the jurisdictions, and Stripe can produce invoice
  documents. What Stripe does **not** do for an Israeli seller: it does not register you for VAT, it does not decide
  whether you owe Israeli VAT on a sale (that depends on your own status — עוסק פטור, עוסק מורשה, חברה — and on where
  the customer is), it does not issue an Israeli-compliant tax invoice (חשבונית מס) or receipt (קבלה), and it does not
  file anything. Israel has also been phasing in an invoice-allocation-number requirement for invoices above a
  threshold, which is a Tax Authority process no payment processor performs for you. Practically, sellers here run a
  local invoicing service alongside Stripe. **Do not take the paragraph above as advice**: before you take the first
  live shekel, ask a רואה חשבון or יועץ מס which of these apply to you, and what you must issue and when. This file is
  written by the people who built the app, and tax law is not something to infer from a payments API.
## Round 13 — the growth loop (appended; the lead folds it into the numbered list above)

28. **A posted look has a public page at `https://$DOMAIN/look/<id>` and a person at `/u/<handle>`.** Server-rendered,
    no session, no client script, nothing fetched from a font host. Nothing to configure — but two things decide how
    it looks to the world:
    - **`Email__PublicOrigin` is what the tags name.** It is already required for mail; with it set, `og:url`,
      `og:image` and the canonical link are absolute on the real domain. Without it they fall back to the request's
      own scheme and host, which is right on a laptop and wrong behind a proxy that rewrites Host.
    - **`robots.txt` now allows `/look/` and `/u/` and disallows `/api/`, `/app/`, `/i18n/`, `/vendor/`, `/digest/`
      and the app shell itself.** If you put a CDN or a WAF in front, let `GET /look/*` and `GET /u/*` through
      unauthenticated, and let `/look/<id>/image` be cached (it answers `Cache-Control: public, max-age=3600`).
    Check it the way a crawler does, from your laptop, before you announce anything:
    ```bash
    curl -sA "WhatsApp/2.2319.9 A" https://$DOMAIN/look/<id> | grep -E 'og:|twitter:'
    curl -sI https://$DOMAIN/look/<id>/image | head -3      # 200, image/jpeg, public max-age=3600
    curl -s  https://$DOMAIN/robots.txt
    ```
    A look that is hidden, never posted, or whose account is suspended answers 404 with `noindex` — try one and see.

29. **The weekly mail (`Digest__*`) goes out only when mail is on and `Email__PublicOrigin` is set.** Every line of
    both messages is a link, so with no origin the hourly run logs one warning and sends nothing — that warning is the
    thing to grep for if nobody is getting mail:
    ```bash
    docker compose logs --since 24h app | grep 'Digest:'
    # Digest: run at 2026-09-20T06:00:00Z, 12 digests, 1 welcomes, 30 with nothing to say
    ```
    One line per run, every hour, whatever happened. Set **`Digest__Secret`** (any string; `openssl rand -base64 32`)
    before the first send and never change it: it keys the one-tap unsubscribe links. While it is empty the links are
    keyed by the account's stored password hash — they work, and a password reset voids that person's open links.
    `Digest__Hour` (default 9) is local to `Board__TimeZone`; the digest is sent within a day of that moment and never
    outside it, so a deploy in the middle of the week mails nobody. Turn the whole thing off with
    `Digest__Enabled=false` without touching the mail settings.

30. **Invites cost you checks.** An accepted invite gives *both* accounts one more check that day, which is one more
    paid model call each. `Limits__ChecksPerDayGlobal` still bounds the bill and the bonus never touches it. Watch
    *Invites → accepted* on `#/admin/metrics` next to the day's checks; if someone farms accounts, the signup brake
    (`Limits__SignupsPerHourPerIp`) and the ceiling are what stop it, and a suspended account can no longer invite.

31. **The funnel on `#/admin/metrics` is this server's own counting.** Landing views and invite arrivals are `Counter`
    rows written by a middleware that sets no cookie and stores no address; guest checks, signups and first posts are
    counted off the tables. There is no third party to configure and nothing to consent to. Fourteen days, today's
    conversion between the steps, moderators only. If a day reads zero landing views while the page is clearly being
    visited, something in front of the app is serving `/landing/` itself — the count is the app's, not the proxy's.
