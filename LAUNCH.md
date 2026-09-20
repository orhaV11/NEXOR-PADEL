# Taking OREVOSH live

This is the runbook. It assumes you have never deployed anything: every command is here to be copied, every value is
explained, and nothing is left as "configure your infrastructure". An evening is enough — about two hours if the
domain's DNS is quick and you have a card for the two accounts that need one.

Read section 0 first and collect the six things it lists. Then pick **one** of section 1 (Fly.io, recommended) or
section 2 (your own server) — not both. Sections 3 to 6 are the same whichever path you took: the first day, money,
the store apps, and what to do when something breaks.

`looks.example.com` stands for your domain everywhere below, and `<handle>` for an account's handle without the `@`.
Those two, and anything else in angle brackets, are yours to replace; nothing else is. `set-origin` (1.2) puts your
domain into the code and the other documents, but not into this page: this one keeps the stand-in so it still reads as
a runbook for the next person.

- **Section 0** — what you need before you start
- **Section 1** — Fly.io, from nothing to a live address
- **Section 2** — the other path: one rented server with Docker
- **Section 3** — the first day
- **Section 4** — money
- **Section 5** — the store apps, later
- **Section 6** — when something breaks

The reference pages behind this one: [`DEPLOY.md`](DEPLOY.md) has every deployment detail and the settings tables,
[`README.md`](README.md) the configuration and the API, [`MARKETING.md`](MARKETING.md) the launch plan,
[`STORE.md`](STORE.md) the store listings, [`DECISIONS.md`](DECISIONS.md) why the app is the way it is.

---

## 0. What you need before you start

Six things. None of them takes long on its own; the waiting is DNS and a lawyer.

### 0.1 A domain

Buy a domain — Cloudflare Registrar, Porkbun and Namecheap all sell one for about **10 EUR a year**; any registrar
works. A subdomain of a domain you already own (`looks.example.com`) is just as good and costs nothing.

You will add **one DNS record**, and which one depends on the path you pick:

- **Fly.io, on a subdomain** (`looks.example.com`): a `CNAME` record, name `looks`, value `<your-app-name>.fly.dev`.
- **Fly.io, on the bare domain** (`example.com`): an `A` record and an `AAAA` record with the addresses `fly ips list`
  prints. A bare domain cannot take a `CNAME`.
- **Your own server**: an `A` record pointing at the server's public IPv4 address, and an `AAAA` record for its IPv6
  address if it has one.

Do not add the record yet on the Fly path — `fly certs add` prints exactly what to add in section 1. On the server
path the record comes first, because Caddy asks for the certificate the moment it starts.

Whichever you add, the record has to have propagated before HTTPS works. `ping looks.example.com` answering from the
right address is the signal; it is usually minutes and occasionally an hour.

### 0.2 A hosting account

**Fly.io is the recommendation.** Fly runs the app's container on a small virtual machine, terminates HTTPS at its
edge, keeps a persistent volume for the database and the photos, and snapshots that volume daily. There is no server
to patch, no firewall to configure and no certificate to renew. Sign up at <https://fly.io> (`fly auth signup` does it
from the terminal); it asks for a card.

What it costs, at Fly's list prices, for the machine [`fly.toml`](fly.toml) describes:

- one `shared-cpu-1x` machine with 512 MB of memory: about **3 USD a month**;
- a 3 GB volume for the database, the photos and the clips: about **0.45 USD a month**;
- bandwidth for a pilot: inside the free allowance.

So about **3.50 to 5 USD a month**, and up to 10 USD if you grow the machine to 1 GB or the volume to 10 GB. Those are
estimates from Fly's published prices; `fly dashboard` shows the month you are actually having.

**The alternative is one rented server**, a VPS at Hetzner, DigitalOcean, Scaleway, Contabo or OVH: about **5 EUR
(roughly 6 USD) a month** for 1 GB of memory and 20 GB of disk, plus the domain. You run Docker and Caddy on it, you
own the disk and the backups, and you patch the machine yourself. Pick this if you already run a server or if you want
the data on a machine you rent directly. Section 2 is the walkthrough.

Either way the app is **one process with one SQLite file**, so it runs on exactly one machine. Do not start two.

### 0.3 An Anthropic API key

Every outfit check is one call to a model, and that call is the app's only per-use cost.

1. Sign in at <https://console.anthropic.com>.
2. **Plans & Billing** → add a payment method and buy some credit.
3. **Set a monthly spend limit** in the same billing area (usage limits). This is the backstop that means a mistake or
   a bad week costs you a number you chose. Set it to what you are willing to lose, not to what you expect to spend.
4. **Settings → API keys → Create key**. Copy it once (`sk-ant-…`); the console will not show it again.

**What a check costs.** The model is `Anthropic:Model` in `src/FitCheck.Api/appsettings.json`, `claude-sonnet-5`,
which lists at **2 USD per million input tokens and 10 USD per million output tokens**. One check sends the stylist's
rubric (about 1,900 tokens), the tool schema the answer must fit (about 600), the intent line, and one photo that the
phone has already downscaled to 1280 px on its long side before upload (about 1,600 tokens for an image that size) —
call it 4,000 to 4,500 input tokens. The answer is a single tool call with reasoning switched off
(`AnthropicVisionClient` sends `thinking: disabled`) and `Anthropic:MaxTokens` is 1200, so 300 to 700 output tokens in
practice and 1,200 at the absolute most.

That is about **1.5 US cents a check**, and about 2.1 cents in the worst case (0.0045 × 2 + 0.0012 × 10). A "Which
one?" comparison sends two photos, so about **2 cents**. **Treat these as estimates**: they come from the model's
list price and a token count of the prompt, not from a bill. The console's usage page is the real number, and the end
of your first week is when to look at it.

The app caps the volume from its side as well: `Plans__GuestChecksPerDay` (1 free check for a visitor with no
account), `Plans__FreeChecksPerDay` (3), `Plans__ProChecksPerDay` (30), the per-account ceiling
`Limits__ChecksPerDay` (30) and the global `Limits__ChecksPerDayGlobal` (1000 a day across everyone). A thousand
checks a day at 1.5 cents is about 15 USD a day: do that arithmetic for your own numbers before you raise any of them.

### 0.4 An email sender

Without one, a person who forgets their password has to make a new account. With one, the app sends a confirmation
link and a reset link over SMTP. Two providers work with the app's settings exactly as they are:

**Resend** (<https://resend.com>, a free tier of 3,000 mails a month). Add your domain, then API Keys → Create API Key:

```
Email__Host=smtp.resend.com
Email__Port=587
Email__User=resend
Email__Password=re_xxxxxxxxxxxxxxxx
Email__From=OREVOSH <hello@looks.example.com>
Email__PublicOrigin=https://looks.example.com
```

**Postmark** (<https://postmarkapp.com>). Verify a sender signature or the domain, then Servers → your server → API
Tokens; the token is both the user and the password:

```
Email__Host=smtp.postmarkapp.com
Email__Port=587
Email__User=<server API token>
Email__Password=<server API token>
Email__From=hello@looks.example.com
Email__PublicOrigin=https://looks.example.com
```

**The from-address must be on your domain** (`hello@looks.example.com`), and the domain must be verified with the
provider, or the mail is refused or lands in spam.

**SPF and DKIM.** When you add your domain, the provider gives you two or three DNS records to paste into the same
DNS panel as section 0.1: a `TXT` record for SPF (`v=spf1 include:…`), one or two `CNAME` records for DKIM, and
usually a `TXT` record for DMARC. Add them and wait for the provider's dashboard to say the domain is verified before
you send anything real. Skipping this is the single most common reason a reset link never arrives.

**`Email__PublicOrigin` is not optional on a server.** The links in the mails are built from it and never from the
address a request arrived on, because a `Host` header is the requester's to choose and a reset token rides in the
link. With mail on and this unset the app warns at start (`Email is on but Email:PublicOrigin is not set`), answers
502 to an address change, and quietly mails nothing for a forgotten password.

### 0.5 Stripe — optional at launch

**You do not need Stripe to go live.** Leave `Billing__Provider` at `manual` and grant Pro by hand:

```bash
# Fly
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --pro <handle> 3"
# a server
docker compose exec app dotnet FitCheck.Api.dll --pro <handle> 3
```

Three months, 31 days each, from now (1 to 120 months); `--pro <handle> off` puts the account back on Free. The Pro
page shows a note instead of a purchase button, Settings shows the end date, and a lapsed period falls back to Free by
itself. For the first weeks this is the better path: people who want Pro write to you, and you learn what they say.

When you are ready to charge, in Stripe's dashboard **in test mode first**:

1. A product **OREVOSH Pro** with one recurring monthly price. Copy the price id (`price_…`).
2. Developers → API keys → the secret key (`sk_test_…` in test mode, `sk_live_…` later).
3. Developers → Webhooks → add an endpoint at **`https://looks.example.com/api/billing/webhook`**, subscribed to the
   five events the app reads: `checkout.session.completed`, `customer.subscription.created`, `invoice.paid`,
   `customer.subscription.updated` and `customer.subscription.deleted`. Copy its signing secret (`whsec_…`).
   `--stripe-check` (4.2) names any you missed, so you do not have to count them here.
4. Settings → Billing → **Customer portal**: open it once and save a configuration, choosing what a customer may do
   there (cancel, change payment method). Stripe will not create a portal session until that configuration exists, and
   the app's "Manage subscription" button is a portal session.

Section 4 turns those four values on and tests them. Note for later: inside the iPhone and Android store apps this
checkout is forbidden and must be hidden (`STORE.md`, "Payments").

### 0.6 A support mailbox, and the legal review

**A mailbox.** `hello@looks.example.com`, forwarded to an inbox a person actually reads. It is the support address on
the store listings, it is in the legal pages, and while billing is manual it is where people write to start or stop
Pro — the app tells them to, in every language ("To change or cancel, write to us."). The legal pages ship with
`hello@orevosh.app` in them; change it to yours.

**The legal review.** The app has three pages of its own: `#/terms`, `#/privacy` and `#/guidelines` (version 2, dated
2026-09-12, in English and Hebrew, in `src/FitCheck.Api/wwwroot/app/views/legal.js`). They describe what the code
actually does, in plain words. They are **not legal advice**: the governing-law line is a placeholder, and both stores
require the privacy policy at a public URL. Send them to a lawyer in your country before the address leaves your team,
and change anything you would not stand behind — the version line at the bottom of the page moves with the text.

### 0.7 The shopping list

Before you open a terminal, have these in front of you:

1. The domain, and the login to its DNS panel.
2. A Fly.io account (or a VPS with its IP address and your SSH key on it).
3. `sk-ant-…`, with a monthly spend limit set.
4. The SMTP host, user, password and from-address, with SPF and DKIM verified.
5. `hello@<your domain>`, reaching a real inbox.
6. The legal pages read by someone qualified.

Optional, and easy to add later: Stripe's four values, and a VAPID key pair for push (the app generates it in
section 1).

---

## 1. Fly.io, from nothing to a live address

Everything in this section runs on **your own computer**, from the repository folder. You do not need Docker or the
.NET SDK: Fly builds the image.

### 1.1 Install flyctl and sign in

```bash
curl -L https://fly.io/install.sh | sh                        # macOS and Linux
brew install flyctl                                           # or, on a Mac with Homebrew
pwsh -Command "iwr https://fly.io/install.ps1 -useb | iex"    # Windows PowerShell
```

Then:

```bash
fly auth signup      # or: fly auth login
```

### 1.2 Set your origin before anything is built

The domain is baked into static files — the link-preview tags, the landing pages and the store shell — so it has to be
right **before** the image is built. Do it now, once:

```bash
node tools/brand/set-origin.js https://looks.example.com
git diff --stat
```

It rewrites every place the placeholder `looks.example.com` appears in the eleven files it knows by name, and prints
each one with a count. Four of them are shipped to a browser or a store, and they are why this runs before the build:
`src/FitCheck.Api/wwwroot/index.html` (`og:url`, `og:image`, `twitter:image`), `wwwroot/landing/index.html` and
`landing/index.he.html` (canonical, both `hreflang` links, `og:url`, `og:image`, `twitter:image`) and
`mobile/capacitor.config.json` (`server.url`, `allowNavigation`). The other seven are the documents that quote the
origin, so the commands you paste from them are already yours: `mobile/README.md` (its `WKAppBoundDomains` note
included), `STORE.md` (its URL table included), `MARKETING.md`, `brand-kit/README.md`, `DEPLOY.md`, `README.md` and
`.env.example`. There is nothing left over for you to edit by hand.

Read the diff anyway, for one thing the script cannot know: it rewrote the sample email addresses too
(`hello@looks.example.com` in `.env.example` and `STORE.md` becomes `hello@` your domain), and that is a guess at your
mailbox, not a fact. To see what is left before you build:

```bash
node tools/brand/set-origin.js --check    # exits 1 while any placeholder is still in those eleven files
```

Commit the change, so the next deploy and every deploy after it carry it.

### 1.3 Create the app

From the repository folder:

```bash
fly launch --no-deploy --copy-config --name <your-app-name> --region fra
```

`--copy-config` uses the repository's [`fly.toml`](fly.toml) as it is and only writes your app name into it.
`--no-deploy` because the volume and the secrets come first. The name becomes the address
(`https://<your-app-name>.fly.dev`), so it must be unused across all of Fly. `fra` is Frankfurt; `fly platform regions`
lists the others — pick the one nearest your people and use the same one in the next step.

**If `fly launch` offers to add a Postgres database, Redis, or to "optimise" the settings, say no.** The app has its
own SQLite file on its own volume, and `fly.toml` already says one machine, port 8080, a `/data` mount, `/healthz`
checked every 30 seconds and HTTPS forced.

### 1.4 The volume

```bash
fly volumes create data --size 3 --region fra --yes
```

`data` is the name `fly.toml` mounts at `/data`. Everything people give the app lives there: `orevosh.db` and the
`storage/` folder with the photos and clips. 3 GB is a pilot; `fly volumes extend <volume id> --size 5` grows it later
without moving anything (`fly volumes list` shows the id).

### 1.5 The secrets

Secrets on Fly are environment variables. Every setting the app has can be set this way — a double underscore stands
for each colon, so `Plans:FreeChecksPerDay` is `Plans__FreeChecksPerDay`. Each `fly secrets set` restarts the machine,
so group them; the app is not running yet, so the restarts cost nothing here.

**Required** — the app will not check an outfit without it:

```bash
fly secrets set ANTHROPIC_API_KEY=sk-ant-...
```

**Email, from section 0.4** (leave these out and recovery is simply off):

```bash
fly secrets set \
  Email__Host=smtp.resend.com \
  Email__Port=587 \
  Email__User=resend \
  Email__Password=re_xxxxxxxxxxxxxxxx \
  "Email__From=OREVOSH <hello@looks.example.com>" \
  Email__PublicOrigin=https://looks.example.com
```

**Billing** — set the provider now and the rest in section 4:

```bash
fly secrets set Billing__Provider=manual
```

When Stripe is ready, the four that go with it, plus where Checkout returns to:

```bash
fly secrets set \
  Billing__Provider=stripe \
  Billing__StripeSecretKey=sk_live_... \
  Billing__StripePriceId=price_... \
  Billing__StripeWebhookSecret=whsec_... \
  Billing__PublicOrigin=https://looks.example.com
```

**Push notifications.** Generate the key pair with the app itself, once and only once — a new pair silently drops
every existing subscription. This command needs no database, so it can run before the first deploy:

```bash
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --vapid"
```

(If the machine does not exist yet, run `dotnet run -- --vapid` from `src/FitCheck.Api` on your own computer with the
.NET SDK, or come back to this after 1.6.) Then:

```bash
fly secrets set \
  Push__PublicKey=<the PUBLIC line> \
  Push__PrivateKey=<the PRIVATE line> \
  Push__Subject=mailto:hello@looks.example.com
```

**Moderators.** `Admin__Handles__0` promotes an account **that already exists** at every start, and a handle listed
here can no longer be signed up by anyone — so do not set it yet. Sign up first (1.8, steps 2 and 3), then either add
it or run `--admin`, which needs no restart:

```bash
fly secrets set Admin__Handles__0=<handle>      # only after that account exists
```

**Plans** — the defaults (3 free checks a day, 30 on Pro, 1 for a visitor) are the pilot's, so set only what you want
different. `Plans__ProPriceText` is text on the Pro page, not a price Stripe charges:

```bash
fly secrets set \
  Plans__FreeChecksPerDay=3 \
  Plans__ProChecksPerDay=30 \
  Plans__GuestChecksPerDay=1 \
  Plans__GuestAttemptsPerDay=20 \
  "Plans__ProPriceText=₪19 / month" \
  Plans__CompareNeedsPro=false
```

**The weekly board.** `Board__TimeZone` and `Board__WeekStartsOn` cut the week and label the archive, and changing
them later moves every past week's edges — **set them before the first week runs**:

```bash
fly secrets set Board__TimeZone=Asia/Jerusalem Board__WeekStartsOn=Sunday
```

The rest of the board's rules (`Board__MinChecksToCount` 1, `Board__MaxPerFirerPerAuthor` 3, `Board__NewAccountDays`
2, `Board__Size` 10, `Board__RisingDays` 30, `Board__CacheSeconds` 60) have pilot defaults. Leave the sponsor
(`Board__Sponsor__Name`, `__Handle`, `__PrizeText`, `__Url`) unset: the first hall should be earned, not presented.

**Affiliate links.** Only for a programme you have actually joined; with nothing listed, no link earns anything and
nothing is appended:

```bash
fly secrets set 'Affiliate__Hosts__amazon.com=tag=orevosh-20'    # only if you joined
```

Keep `Affiliate__Disclosure` at its default `true`: it is the line under every store link that says the app may earn a
commission, and the programmes' terms expect it.

**Request logging.** Off by default. Turn it on for the first days — one line per request is how you see what people
actually do, and what fails:

```bash
fly secrets set Logging__Requests=true
```

Turn it off again once the launch is quiet; it is a lot of lines.

### 1.6 Deploy

```bash
fly deploy --ha=false
```

`--ha=false` matters: without it Fly starts two machines for high availability, and this app must be one process with
one volume. Fly builds the image from the repository's `Dockerfile` on its builders.

Two variations, when you want them:

```bash
fly deploy --image ghcr.io/<owner>/orevosh:latest --ha=false    # the image the release workflow already built
fly deploy --local-only --ha=false                              # build on your own computer (needs Docker)
```

The first is the fastest path once the repository's **Release image** workflow has run (it builds on every push to
`main` and every `v*` tag). The package is private to the repository's collaborators until you make it public in
GitHub → Packages → orevosh → Package settings.

Then watch it come up:

```bash
fly status                                       # one machine, started, its check passing
fly logs                                         # "Database /data/orevosh.db is new: creating the schema from the migrations."
```

Right under that line a first start also prints one EF Core warning, `An operation of type 'SqlOperation' will be
attempted while a rebuild of table 'PostItems' is pending`: the Round 10 migration talking to itself on a fresh file.
The dry run of this runbook saw it on an empty database, and `/readyz` answered `db: ok` straight after. Nothing to do.

**Then, from your laptop, the smoke script:**

```bash
scripts/smoke.sh https://<your-app-name>.fly.dev
```

It needs only `curl`, and it reads in one go what section 1.8 has you read by hand: `/healthz` says `ok`, `/readyz` is
200 with every check `ok`, both landing pages are HTML carrying the slogan, `/api/config` answers, the shell carries the
link-preview tags (and they say your origin, not `looks.example.com`), the security headers are on, and the manifest is
served. One `OK`/`FAIL` line per check, `smoke: 8 ok, 0 failed` at the end, and exit code 1 on any failure, so a deploy
script can gate on it. Run it after every deploy, against the address people use (`https://looks.example.com` once 1.7
is done). A guest check is opt-in, `scripts/smoke.sh https://… --check` (or `--check photo.jpg` with a real outfit
photo), because on a live site it spends a real stylist call and this address's one free look for the day.

**If the log says it cannot write to `/data`** (`permission denied`, `unable to open database file`), the volume's
root does not belong to the container's non-root `app` user. Once:

```bash
fly ssh console -C "chown -R app:app /data"
fly machine restart <machine id>                 # fly status shows the id
```

### 1.7 The domain

```bash
fly certs add looks.example.com
```

It prints the DNS record to add — the `CNAME` or the `A`/`AAAA` pair from section 0.1. Add it in your registrar's DNS
panel, then:

```bash
fly certs check looks.example.com                # "issued" when it is done
```

Make sure `Email__PublicOrigin` and, if Stripe is on, `Billing__PublicOrigin` both say `https://looks.example.com`
(1.5), or the links in mails and the return from Checkout will point somewhere else.

### 1.8 The smoke test

In order. Each line should do what it says before you go on.

```bash
curl https://looks.example.com/healthz          # ok           — the app is alive and the database answers
curl -s https://looks.example.com/readyz | jq   # {"ok":true,...} — the app is ready to serve
curl -I https://looks.example.com/landing/      # 200          — the landing page
```

(`| jq` only pretty-prints the answer. If your computer has no `jq`, leave it off: the line still prints the same
JSON, on one line.)

`scripts/smoke.sh https://looks.example.com` (1.6) does those three lines and five more in one go, and exits 1 when
any of them is off. The phone half below is yours.

`/readyz` is the one to read. It is public, it is never cached, and it answers a small document: `ok`, and a `checks`
object where each name is either `"ok"` or a short reason. It checks `db` (a query answers and the schema is at the
current migration), `storage` (the photo folder exists and a file can be written and removed there) and, while
`Storage:Transcode` is on, `ffmpeg` (the binary is found). All three ok is a 200; anything failing is a **503** with
the failing check named. Nothing in it names a path, a version or a secret — it is safe to leave public and to point
an uptime checker at.

Then, on your phone, at `https://looks.example.com`:

1. **The landing page** loads and the Hebrew one does too (`/landing/index.he.html`).
2. **Sign up** with the handle you want to moderate with. This is the account step 3 promotes.
3. **Make the account a moderator:**
   ```bash
   fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --admin <handle>"
   ```
   No restart. Reload the app and the moderation queue is in that account's menu. The doctor (step 5) counts the
   moderators in the database, so its `admin` line reads `ok` from here on even though `Admin__Handles` is empty.
4. **Verify your first brand accounts**, once each brand has signed up and you know who is behind the account:
   ```bash
   fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --verify <handle>"
   ```
   That is the check inside the BRAND mark, and it is the only claim of authenticity the app makes. `--unverify`
   takes it away.
5. **Run the doctor, live:**
   ```bash
   fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --doctor --live"
   ```
   `--doctor` on its own reads the configuration and the machine — the database, the storage folder, ffmpeg, whether
   the Anthropic key is set and which model it points at, whether mail is configured and has a public origin, the push
   keys, the billing settings, the plan caps, the board's time zone, the moderator list, the affiliate hosts and the
   free disk space — fourteen checks in all — and prints one line per check, `ok` or a short reason, exiting 0 when
   everything a live server needs is there and 1 otherwise. `--live` adds the checks that leave the machine: one small
   call to Anthropic with your key and model (a few hundred tokens, a fraction of a cent), and two reads from Stripe
   when the provider is `stripe` (the price, and the webhook endpoint). It does not try the mail server: nothing here
   logs in to SMTP, so the test of the sender is asking the app for a password reset with your own address. Run
   `--doctor` whenever you like; run `--doctor --live` now, and again after any change to a key. On a first deploy
   expect `WARN push` (no VAPID keys until `DEPLOY.md`'s step 7) and nothing failing; `FAIL anthropic` means the key
   secret never reached the machine (1.5).
6. **Do a real check.** Photograph an outfit in the app, pick an intent, and read the verdict. This is the moment the
   Anthropic key, the storage folder and the model all have to be right at once.
7. **Post it** and open `#/board`. On launch day the look sits on the **Stylist's picks** tab (by score, no fires
   needed); the **Looks** tab wants fires that count, and a fire from an account younger than `Board__NewAccountDays`
   (2 days) does not count, then or later: the age is judged at the moment of the fire, so a fire from a second
   account you made just now raises the look's count and never fills the Looks tab, not even once that account is
   two days old. To see the Looks tab fill, fire from an account that is already two days old (or, two days on,
   unfire and fire again from the new one: that is a new fire, judged then), or set `Board__NewAccountDays=0` for
   the smoke, which makes the launch-day fire count at once, and put it back after. The dry run of this runbook
   proved exactly that.
8. **Open the numbers page**, `https://looks.example.com/#/admin/metrics`, signed in as the moderator. It should show
   the checks and the people you made in steps 6 and 7 (one check and one person if you stopped at step 6; guest
   checks are not counted). Anyone who is not a moderator gets a refusal.
9. **Install to the home screen** — Share → "Add to Home Screen" on iPhone; Chrome offers "Install" by itself on
   Android — and check the app opens full screen with its own icon.

If any of those fail, section 6 is the map.

### 1.9 Backups on Fly

Fly snapshots the volume daily and keeps five days. That is a backup on the same provider; take one of your own too:

```bash
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --backup /data/backups/manual --keep 7"
fly ssh console -u app -C "tar czf /data/backups/manual/storage-<stamp>.tgz -C /data/backups/manual storage-<stamp>"
fly sftp get /data/backups/manual/orevosh-<stamp>.db ./orevosh-<stamp>.db
fly sftp get /data/backups/manual/storage-<stamp>.tgz ./storage-<stamp>.tgz
fly ssh console -u app -C "rm -rf /data/backups/manual"
```

`--backup <dir>` writes a consistent single-file copy of the database (SQLite's own online snapshot, safe while the
app is writing) and a copy of the photo folder, and prints `database: …` and `storage: …` with the paths.
`--keep <n>` prunes that folder to the newest `n` database copies and the newest `n` storage copies after the new one
is complete, so a weekly run does
not fill the volume. The copies share the 3 GB volume with the live data, which is why the last line removes them.

The `<stamp>` in lines 2 to 4 is the one the first line printed, so run them one at a time and read its output before
the rest. `/data/backups/manual` is a folder of its own on purpose: the last line empties it, and nothing else on the
volume keeps copies there.

**The files hold every photo and clip people gave the app.** Keep them as private on your computer as they are on the
volume. There is no cron on Fly's machine: run the five lines weekly from your own computer, or from a scheduled
GitHub Actions job with a `FLY_API_TOKEN` secret (`fly tokens create deploy`).

Skip to **section 3**.

---

## 2. The other path: one rented server with Docker

Pick this only if you want the machine. It is about an hour the first time.

What you end up with:

```
phone ── https://looks.example.com ──▶ Caddy (certificate, port 443) ──▶ the app (port 8080, container only)
                                                                              │
                                                                         /data volume: orevosh.db + storage/
```

### 2.1 DNS first

Add the `A` record (and `AAAA` if the server has IPv6) from section 0.1 now, and wait until
`ping looks.example.com` answers from the server's address. Caddy asks Let's Encrypt for the certificate the moment it
starts, and that only works once the name resolves to this machine.

### 2.2 The server

SSH in as root. Docker's own installer brings Compose with it:

```bash
apt-get update && apt-get upgrade -y
curl -fsSL https://get.docker.com | sh
```

Open only what the app uses:

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable
```

On a 1 GB machine, a little swap so the build never runs out of memory:

```bash
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

### 2.3 The code, and your origin

```bash
apt-get install -y git
git clone https://github.com/<you>/<repository>.git /opt/orevosh
cd /opt/orevosh
```

Everything below runs from `/opt/orevosh`. **Set the origin before the build** — the link-preview tags and the landing
pages are static files inside the image:

```bash
node tools/brand/set-origin.js https://looks.example.com
```

(If node is not on the server, run it on your computer, commit, and `git pull` here. 1.2 lists the eleven files it
rewrites; `node tools/brand/set-origin.js --check` here says whether the checkout you are about to build still carries
the placeholder.)

### 2.4 The `.env` file

```bash
cp .env.example .env
nano .env
```

`.env.example` is commented line by line; the values are the same ones section 1.5 sets as Fly secrets, in the same
spelling. At minimum:

```
DOMAIN=looks.example.com
ANTHROPIC_API_KEY=sk-ant-...
```

Then the email block (0.4), `Billing__Provider=manual`, `Board__TimeZone` and `Board__WeekStartsOn`, and
`Logging__Requests=true` for the first days. Leave `Admin__Handles__0` commented out — it comes after you sign up.
`.env` is git-ignored and stays on the server; never paste it anywhere.

### 2.5 First start

```bash
docker compose up -d --build
docker compose ps                 # app and caddy: running / healthy
docker compose logs -f app        # Ctrl-C to stop watching
```

The first build downloads the .NET images and compiles the app — a few minutes on a small VPS. The log says what
happened to the database (`Database /data/orevosh.db is new: creating the schema from the migrations.`) and, since the
image ships ffmpeg, `Transcoding is on: ffmpeg version …`.

Caddy fetches the certificate on the first request; give it up to a minute. If it does not come,
`docker compose logs caddy` says why, and it is almost always DNS not pointing here yet or port 80/443 closed by the
provider's own firewall.

### 2.6 The same smoke test

Exactly section 1.8, with the commands in their server shape:

```bash
curl https://looks.example.com/healthz
curl -s https://looks.example.com/readyz | jq
docker compose exec app dotnet FitCheck.Api.dll --admin <handle>      # after you sign up
docker compose exec app dotnet FitCheck.Api.dll --verify <handle>
docker compose exec app dotnet FitCheck.Api.dll --doctor --live
```

And from your laptop, `scripts/smoke.sh https://looks.example.com` (1.6): the eight read-only checks in one go, exit 1
when any is off.

Push keys, if you want notifications (`docker compose run` because this one needs no database):

```bash
docker compose run --rm --no-deps app dotnet FitCheck.Api.dll --vapid
```

Paste the two lines into `Push__PublicKey` and `Push__PrivateKey` in `.env`, then `docker compose up -d`.

Then do the phone half of 1.8: sign up, check an outfit, post it, fire it, open `#/board` and `#/admin/metrics`,
install to the home screen.

### 2.7 Backups, by cron

`tools/backup.sh` calls the app's own `--backup` inside the container, copies the results out to `backups/` next to
the code, prunes, and removes the scratch folder from the data volume whatever happens:

```bash
tools/backup.sh
ls -l backups/
# -rw------- orevosh-20260905033000.db    drwx------ storage-20260905033000/
```

It keeps the newest `KEEP` database copies (14 by default) and the newest `KEEP_STORAGE` copies of the media folder
(2 by default) — the database is megabytes, a media copy is the whole folder. Everything it writes is readable by
root only (`umask 077`, the folder mode 700, `chmod -R go-rwx` after `docker cp`).

The scratch folder it uses inside the container is made fresh for each run (`mktemp -d /data/backup-scratch.XXXXXXXX`)
and removed on every exit path, including a run that fails halfway, so nothing of anyone's media is left on the data
volume. It is a folder of its own on purpose: the repository's other backup script, `scripts/backup.sh`, keeps its
copies **on** the volume in `/data/backups/nightly` and prunes them there, and the two can never reach each other's
files. If you point either one at a folder of your own, give it one nobody else writes to — the app's `--keep` prunes
every `orevosh-*.db` and `storage-*` in the folder it is handed, whoever wrote them.

Every night at 03:30:

```bash
(crontab -l 2>/dev/null; echo '30 3 * * * cd /opt/orevosh && KEEP=14 KEEP_STORAGE=2 tools/backup.sh >> /var/log/orevosh-backup.log 2>&1') | crontab -
```

**A backup on the same disk is not a backup.** Copy `backups/` off the machine at least weekly, and keep it as private
there as it is here. To another Linux machine, keeping the modes:

```bash
scp -rp root@looks.example.com:/opt/orevosh/backups ~/orevosh-backups
```

Or encrypted to a cloud drive, which has no file permissions of its own:

```bash
tar cz backups | gpg -c -o backups-$(date +%F).tgz.gpg
```

**Restore one once, now, before you need it** (section 6.4 has the commands). A backup you have never restored is a
hope, not a backup.

---

## 3. The first day

### 3.1 The origin is already right

You ran `set-origin` before the build (1.2 or 2.3), so the link previews and the landing pages carry the real domain.
Prove it: paste `https://looks.example.com` into a chat app and check that the card shows the 1200×630 image and the
slogan rather than a bare link. If it does not, the origin did not make it into the image — set it, rebuild, redeploy.

### 3.2 Real screenshots in the brand kit

The store screenshots and the landing page's phone screens in the repository show the **browser test's synthetic
outfit and Chromium's fake camera**. They are placeholders. Take real captures on a phone — the check screen, the
result, a look in the feed, a featured look, the camera recording — drop them into `tools/brand/templates/screens/`
under the names already there, and regenerate:

```bash
(cd tools/brand && npm install)              # once; or NODE_PATH=tools/e2e/node_modules if you ran the browser test
node tools/brand/render-kit.js               # everything, from the repository root
node tools/brand/render-kit.js store web     # or just the store screens and the landing page's
```

`brand-kit/README.md` lists every file it writes. Do this before any store submission: Apple rejects screenshots that
do not show the app as shipped.

### 3.3 Post the first ten looks yourself

An empty feed tells a visitor to leave. `MARKETING.md`'s "the first ten posts" is the list; post them from real
outfits, from your own account and a second one, and **tag the pieces on every one** — the brand and the model at
least, a store link only where a real one exists. That is what teaches everyone else what a post looks like here.

Open one challenge with a real prize, and have at least three brand accounts signed up and `--verify`'d.

### 3.4 Invite the narrow community

`MARKETING.md`'s four-week plan: one community small enough to see itself in the feed — one campus, one design
school, one city's streetwear scene. Week 1 is **twenty people invited by hand**, one message each, no group blast.
Ask each for one check and one post, and listen to what they ask about.

Do not open a second community until week-1 people are still checking in week 4.

### 3.5 Watch the numbers, and the log

**The numbers page** is `https://looks.example.com/#/admin/metrics`, signed in as a moderator — the return rate as the
hero figure, tiles for checks, people, posts, fires, comments, items tagged, store-link taps and board reads, and the
score distribution as bars. `MARKETING.md` says which of them matter and what a good one looks like. Read it weekly,
not hourly.

**The log** is `fly logs` or `docker compose logs --since 1h app`. The lines worth grepping:

```bash
fly logs | grep "Guest sweep"        # hourly: how many guest checks went unclaimed — model calls nobody signed up for
fly logs | grep "Board: week"        # after Saturday midnight: "closed, 38 rows", or "had no counted fires"
fly logs | grep "Items:"             # "Items: 3 on post <id> by <id>" — someone tagged the pieces on their look
fly logs | grep "Export:"            # "Export: <id> took their data" — someone downloaded their data
fly logs | grep "Block:"             # "Block: <id> blocked <id>" — someone shut someone out
fly logs | grep "Transcoding"        # at start: "Transcoding is on: ffmpeg version …"; per clip, one line with sizes and time
fly logs | grep "Email"              # "Email sent to …: …" for every confirmation and reset; "Email to … failed: …" when it did not
```

The two warnings to read rather than scroll past: `Board: the close failed; it runs again in five minutes`, and
`No link origin for host …: set Email:PublicOrigin …`, which means recovery mail is silently going nowhere.

Replace `fly logs` with `docker compose logs --since 1h app` on a server. `Logging__Requests=true` (1.5, 2.4) adds one
line per request on top of all of this.

### 3.6 The backup routine

- **On Fly:** the daily volume snapshots are automatic. Run the five lines of 1.9 weekly and keep the files somewhere
  private.
- **On a server:** the cron of 2.7 runs nightly. Copy `backups/` off the machine weekly.
- **Either way:** restore one into a throwaway before you need to (6.4).

### 3.7 When Anthropic is down

It happens, and the app is built for it. A check that the model cannot answer is stored with an error status and the
person gets **502 `error.model_failed`** — "The stylist couldn't look at this one. Please try again in a moment.", in
their own language. **It does not spend their allowance**: failed calls are not counted against `Plans__*` or
`Limits__ChecksPerDay`, and a guest's one free look survives a failure too (their address's slot is given back;
`error.guest_limit` is only ever sent for a look that was actually given). The app itself retries a retryable
failure twice, with a short pause, before it gives up on that photo.

So there is nothing to do but wait, and to say so if people ask. What is worth checking first is whether it is
Anthropic or you: `--doctor --live` makes one call with your key and tells you which.

---

## 4. Money

Skip all of this while `Billing__Provider` is `manual` — `--pro <handle> <months>` is a complete upgrade path, and the
Pro page and Settings both explain it to the person.

### 4.1 Switch the provider on, in test mode

Set the four values from section 0.5, with **test** keys (`sk_test_…`):

```bash
# Fly
fly secrets set Billing__Provider=stripe Billing__StripeSecretKey=sk_test_... \
  Billing__StripePriceId=price_... Billing__StripeWebhookSecret=whsec_... \
  Billing__PublicOrigin=https://looks.example.com

# a server: the same five lines in .env, then
docker compose up -d
```

Stripe is live only when the provider is `stripe` **and** all three keys are set. `https://looks.example.com/api/config`
then says `"billing": true` under `plans`, and the Pro page shows the button instead of the note.

### 4.2 Check it before anyone pays

```bash
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --stripe-check"
docker compose exec app dotnet FitCheck.Api.dll --stripe-check      # on a server
```

`--stripe-check` asks Stripe about the configuration you have given the app and prints three lines. `billing` reads
the settings alone: the provider and the three keys, each with the prefix it should have. `stripe-live` reads the
price back: the key is accepted, the price exists in the same mode as the key, and it is a **recurring** price that is
not archived — Checkout opens in subscription mode, so a one-time price fails here instead of failing the first person
who presses Go Pro. `stripe-webhook` reads Stripe's list of endpoints: one of them is your origin plus
`/api/billing/webhook`, it is enabled, and its events cover the five the app reads (an endpoint set to `*` counts).
Anything missing is named — "registered, but not for `customer.subscription.deleted`" means cancellations would never
end Pro. An endpoint on the same route under another name you answer to (your `fly.dev` address, say) is a warning
that names it rather than a failure, because the webhook only has to reach the route.

Two GETs, nothing written, nobody charged, and no key is printed. Run it after every change to a Stripe value, and
again when you swap test keys for live ones.

### 4.3 A test purchase

With the Stripe CLI on your computer:

```bash
stripe listen --forward-to https://looks.example.com/api/billing/webhook
```

It prints a `whsec_…` for the session; put that in `Billing__StripeWebhookSecret` while you test. Then, in the app,
open the Pro page, press Go Pro, and pay with Stripe's test card `4242 4242 4242 4242`, any future expiry, any CVC.
What should happen:

1. Checkout returns to `/#/pro?checkout=success`.
2. The log says the account is Pro until a date about 35 days out.
3. Settings and the Pro page show the end date, and the daily cap is now 30.
4. **"Manage subscription"** appears in Settings and on the Pro page. Press it: it should open Stripe's portal for
   that customer and come back to `/#/settings`. If it answers "Manage your plan by writing to us." instead, either
   the provider is still manual or this account never went through Checkout; if it fails outright, the portal
   configuration of section 0.5 step 4 is missing.
5. Cancel on Stripe's page. The webhook ends Pro, and the account is back on Free at the end of what it paid for.

Cancelling is on Stripe's side or `--pro <handle> off`; the app has no cancel button of its own, by decision.

### 4.4 Live keys

Swap `sk_test_…` for `sk_live_…`, create the webhook endpoint again **in live mode** and take its new `whsec_…`, set
both, and run `--stripe-check` once more. Then make one real purchase with your own card and refund it from Stripe's
dashboard.

**Nothing in this app has run against a live Stripe account yet.** Your first real subscription, renewal and
cancellation are the proof. Stripe's event log and the app's log are where to look if one of them misbehaves.

### 4.5 The manual path stays open

`--pro <handle> <months>` keeps working with Stripe on, and it is the right tool for early supporters, a brand you
want to thank, and anyone whose payment failed for a reason that is not their fault. It does not create anything on
Stripe's side, so such an account has nothing to manage in the portal — the app tells it to write to you instead.

---

## 5. The store apps, later

The web app installs to the home screen and behaves like a native app already. The store shells in `mobile/` are a
Capacitor wrap pointed at your URL; `mobile/README.md` builds them, `STORE.md` has the listings, the privacy labels
and the review notes.

Before you submit anything, all of these have to be true:

1. **Real screenshots** (3.2). Apple rejects synthetic ones.
2. **A way to block a person** — Apple's UGC guideline 1.2 requires it. This is now in the app: "Block" sits next to
   "Report" in a look's menu and in the "…" menu on someone else's profile, with a confirmation, and Settings →
   **Blocked accounts** lists who you blocked with an Unblock next to each. Nothing tells the blocked person.
3. **A support URL or address** that someone answers within a few days (0.6).
4. **A privacy policy at a public URL**: `https://looks.example.com/#/privacy`, with the terms and the guidelines next
   to it.
5. **The payments rule.** Apple and Google forbid the Stripe checkout inside the wrapped app. Either hide the Pro
   purchase there entirely, or add StoreKit and Play Billing. `STORE.md`, "Payments".

Two things the listing must also say, and both are true of the app today: **account deletion is in the app**
(Settings → Delete account, one step, everything gone) and **a person can download their own data** (Settings →
Download your data, a JSON file of everything they wrote).

---

## 6. When something breaks

### 6.1 Ask the app first

```bash
curl -s https://looks.example.com/readyz | jq     # which of db / storage / ffmpeg is unhappy
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --doctor"
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --doctor --live"
```

On a server: `docker compose exec app dotnet FitCheck.Api.dll --doctor`.

`/healthz` answers `ok` when the database answers and is what the proxy and the uptime checkers poll. `/readyz` is the
fuller answer and the one to read by hand. `--doctor` covers the configuration `/readyz` cannot see; `--doctor --live`
covers the things outside the machine.

### 6.2 Then the log

```bash
fly logs --no-tail | tail -200
docker compose logs --since 1h app          # on a server
docker compose logs caddy                   # certificates, on a server
```

Every 5xx is logged with its path. The grep lines of 3.5 are the routine ones.

### 6.3 The usual suspects

- **"The stylist couldn't look at this one."** — the Anthropic key, the spend limit, or Anthropic. `--doctor --live`
  says which. Nobody's allowance was spent (3.7).
- **No confirmation or reset mail** — `Email__PublicOrigin` unset (the log says
  `No link origin for host …`), SPF/DKIM not verified, or the provider refusing the sender. `--doctor` reads the mail
  settings, but nothing tries the login: send yourself a reset from the app to test the sender.
- **The certificate never arrives** — DNS is not pointing here yet, or 80/443 are closed upstream.
- **`unable to open database file`** — the volume's ownership (1.6).
- **Clips play on Android but not on iPhone** — `"transcoding": false` at `/api/config` means the image has no ffmpeg;
  `/readyz` says the same. Rebuild with `ffmpeg` in the Dockerfile's runtime stage.
- **The board did not close** — `grep "Board:"`. The closer runs at start and every five minutes and catches up on
  missed weeks by itself; `the close failed` is the line that needs you.
- **The disk is full** — `fly ssh console -C "df -h /data"` or `df -h /`. A full disk stops uploads and then writes.
  Extend the volume, or prune old media backups (`KEEP_STORAGE=1`).

### 6.4 Restore from a backup

**On a server**, the script asks before it replaces anything:

```bash
cd /opt/orevosh
tools/restore.sh backups/orevosh-20260905033000.db backups/storage-20260905033000
```

Leave the second argument out to put back the database only. It stops the app, replaces the files, hands them to the
container's user and starts the app again; the log will say what it did with the schema.

**On Fly**, the app is running while you swap the file, so pick a quiet moment:

```bash
fly sftp shell
# put orevosh-20260905033000.db /data/incoming.db
# exit
fly ssh console -C "sh -c 'cd /data && mv incoming.db orevosh.db && rm -f orevosh.db-wal orevosh.db-shm && chown app:app orevosh.db'"
fly ssh console -C "ls -l /data/orevosh.db"      # it must say: app app
fly machine restart <machine id>
```

**Do not leave the `chown` out.** `fly sftp` and a bare `fly ssh console` are root; the app runs as the `app` user
(`USER app` in the `Dockerfile`). A database owned by root comes back after the restart as `unable to open database
file` in `fly logs`, and the site stays down — at the worst possible moment, because you are already restoring. The
`ls -l` line is the proof before you restart.

Writes between the swap and the restart are lost. The media folder goes back the same way: `put` the archive, untar it
over `/data/storage`, and hand it over too — `fly ssh console -C "chown -R app:app /data/storage"`. Fly's own volume
snapshots are the other way back: `fly volumes snapshots list <volume id>`, then
`fly volumes create data --snapshot-id <id> --region fra`.

### 6.5 Roll back the code

The data is on the volume and the schema is versioned, so going back to the previous image does not touch anyone's
account.

```bash
# Fly, to a previous image
fly releases                                     # the list, newest first
fly deploy --image ghcr.io/<owner>/orevosh:<previous sha> --ha=false

# Fly, to a previous commit
git checkout <previous commit> && fly deploy --ha=false

# a server
cd /opt/orevosh
git checkout <previous commit> && docker compose build app && docker compose up -d
```

A rollback **does not undo a migration**. If the previous version predates a schema change, restore the database
backup from before the update as well (6.4) — which is why 2.7 and 1.9 exist, and why the update step takes a backup
first.

### 6.6 Nothing above helped

Take the output of `--doctor --live`, the last 200 lines of the log, and `curl -s …/readyz`, in that order. They say,
between them, what the app can see, what it has been told and what it has been doing.

---

# להעלות את OREVOSH לאוויר

זה ספר ההפעלה. הוא מניח שמעולם לא העליתם שום דבר לאינטרנט: כל פקודה כאן נועדה להעתקה, כל ערך מוסבר, ואין שום שלב
שכתוב בו "הגדירו את התשתית שלכם". ערב אחד מספיק — בערך שעתיים, אם ה-DNS מתעדכן מהר ויש כרטיס אשראי לשני החשבונות
שמבקשים אותו.

קראו קודם את פרק 0 ואספו את ששת הדברים שהוא מונה. אחר כך בחרו **אחד** מבין פרק 1 (Fly.io, המומלץ) ופרק 2 (שרת משלכם) —
לא את שניהם. פרקים 3 עד 6 זהים בשני המסלולים: היום הראשון, כסף, החנויות, ומה עושים כשמשהו נשבר.

`looks.example.com` מייצג כאן את הדומיין שלכם, ו-`<handle>` מייצג שם משתמש בלי ה-`@`. שני אלה, וכל מה שמופיע בסוגריים
משולשים, הם שלכם להחליף; שום דבר אחר לא. `set-origin` (1.2) כותב את הדומיין שלכם לתוך הקוד ולתוך שאר המסמכים, אבל לא
לתוך הדף הזה: כאן נשאר מציין המקום, כדי שהדף יישאר ספר הפעלה גם למי שיבוא אחריכם.

- **פרק 0** — מה צריך לפני שמתחילים
- **פרק 1** — Fly.io, מאפס עד כתובת חיה
- **פרק 2** — המסלול השני: שרת שכור אחד עם Docker
- **פרק 3** — היום הראשון
- **פרק 4** — כסף
- **פרק 5** — החנויות, בהמשך
- **פרק 6** — כשמשהו נשבר

הדפים שמאחורי הדף הזה: ב-[`DEPLOY.md`](DEPLOY.md) כל פרטי הפריסה וטבלאות ההגדרות, ב-[`README.md`](README.md) ההגדרות
וה-API, ב-[`MARKETING.md`](MARKETING.md) תוכנית ההשקה, ב-[`STORE.md`](STORE.md) דפי החנויות,
וב-[`DECISIONS.md`](DECISIONS.md) הסיבות לכל החלטה.

---

## 0. מה צריך לפני שמתחילים

שישה דברים. אף אחד מהם לא ארוך בפני עצמו; מה שלוקח זמן זה ה-DNS ועורך הדין.

### 0.1 דומיין

קונים דומיין — ב-Cloudflare Registrar, ב-Porkbun או ב-Namecheap, בערך **10 אירו לשנה**; כל רשם מתאים. תת-דומיין של
דומיין שכבר יש לכם (`looks.example.com`) טוב בדיוק באותה מידה ולא עולה כלום.

תוסיפו **רשומת DNS אחת**, ואיזו — תלוי במסלול:

- **Fly.io, על תת-דומיין** (`looks.example.com`): רשומת `CNAME`, בשם `looks`, שמצביעה אל `<your-app-name>.fly.dev`.
- **Fly.io, על הדומיין עצמו** (`example.com`): רשומת `A` ורשומת `AAAA` עם הכתובות ש-`fly ips list` מדפיס. דומיין
  ראשי לא יכול לקבל `CNAME`.
- **שרת משלכם**: רשומת `A` אל כתובת ה-IPv4 הציבורית של השרת, ורשומת `AAAA` אל ה-IPv6 אם יש לו.

במסלול Fly אל תוסיפו את הרשומה עכשיו — `fly certs add` ידפיס בפרק 1 בדיוק מה להוסיף. במסלול השרת הרשומה קודמת, כי
Caddy מבקש את התעודה ברגע שהוא עולה.

בכל מקרה, הרשומה צריכה להתפשט לפני ש-HTTPS עובד. הסימן הוא ש-`ping looks.example.com` עונה מהכתובת הנכונה; בדרך כלל
דקות, לפעמים שעה.

### 0.2 חשבון אחסון

**ההמלצה היא Fly.io.** Fly מריצה את הקונטיינר של האפליקציה על מכונה וירטואלית קטנה, מסיימת את ה-HTTPS בקצה שלה, שומרת
נפח קבוע לבסיס הנתונים ולתמונות, ומצלמת אותו כל יום. אין שרת לתחזק, אין חומת אש להגדיר ואין תעודה לחדש. נרשמים
ב-<https://fly.io> (או `fly auth signup` מהטרמינל); מבקשים כרטיס אשראי.

כמה זה עולה, לפי המחירון של Fly, למכונה ש-[`fly.toml`](fly.toml) מתאר:

- מכונת `shared-cpu-1x` אחת עם 512MB זיכרון: בערך **3 דולר לחודש**;
- נפח של 3GB לבסיס הנתונים, לתמונות ולקליפים: בערך **0.45 דולר לחודש**;
- תעבורה של פיילוט: בתוך המכסה החינמית.

כלומר בערך **3.5 עד 5 דולר לחודש**, ועד 10 דולר אם תגדילו את המכונה ל-1GB או את הנפח ל-10GB. אלה הערכות מהמחירון
המפורסם; `fly dashboard` מראה את החודש שבאמת יש לכם.

**החלופה היא שרת שכור אחד**, VPS ב-Hetzner, DigitalOcean, Scaleway, Contabo או OVH: בערך **5 אירו (בערך 6 דולר)
לחודש** עבור 1GB זיכרון ו-20GB דיסק, פלוס הדומיין. אתם מריצים עליו Docker ו-Caddy, הדיסק והגיבויים שלכם, והעדכונים של
המכונה עליכם. בחרו בזה אם אתם כבר מתחזקים שרת או אם אתם רוצים שהמידע יישב על מכונה ששכרתם ישירות. פרק 2 הוא ההליכה
צעד-צעד.

בכל מקרה, האפליקציה היא **תהליך אחד עם קובץ SQLite אחד**, ולכן היא רצה על מכונה אחת בדיוק. אל תפעילו שתיים.

### 0.3 מפתח API של Anthropic

כל בדיקת לוק היא קריאה אחת למודל, והקריאה הזו היא העלות היחידה של האפליקציה לפי שימוש.

1. נכנסים ל-<https://console.anthropic.com>.
2. **Plans & Billing** ← מוסיפים אמצעי תשלום וקונים קרדיט.
3. **קובעים תקרת הוצאה חודשית** באותו מקום. זו רשת הביטחון שאומרת שטעות או שבוע רע יעלו מספר שאתם בחרתם. קבעו אותה לפי
   מה שאתם מוכנים להפסיד, לא לפי מה שאתם מצפים להוציא.
4. **Settings ← API keys ← Create key**. מעתיקים פעם אחת (`sk-ant-…`); הקונסולה לא תראה את זה שוב.

**כמה עולה בדיקה אחת.** המודל הוא `Anthropic:Model` בקובץ `src/FitCheck.Api/appsettings.json`, כלומר `claude-sonnet-5`,
שמחירו **2 דולר למיליון טוקני קלט ו-10 דולר למיליון טוקני פלט**. בדיקה אחת שולחת את הרובריקה של הסטייליסט (בערך 1,900
טוקנים), את סכמת הכלי שהתשובה חייבת להיכנס אליה (בערך 600), את שורת הכוונה, ותמונה אחת שהטלפון כבר הקטין ל-1280 פיקסל
בצלע הארוכה לפני ההעלאה (בערך 1,600 טוקנים לתמונה בגודל כזה) — נניח 4,000 עד 4,500 טוקני קלט. התשובה היא קריאת כלי
אחת, בלי חשיבה (`AnthropicVisionClient` שולח `thinking: disabled`) ועם `Anthropic:MaxTokens` 1200, ולכן 300 עד 700
טוקני פלט בפועל ו-1,200 לכל היותר.

זה בערך **1.5 סנט לבדיקה**, ובמקרה הגרוע בערך 2.1 סנט (0.0045 × 2 + 0.0012 × 10). השוואת "איזה מהשניים?" שולחת שתי
תמונות, כלומר בערך **2 סנט**. **התייחסו לזה כאל הערכה**: המספרים באים מהמחירון של המודל ומספירת טוקנים של הפרומפט,
לא מחשבונית. דף השימוש בקונסולה הוא המספר האמיתי, וסוף השבוע הראשון שלכם הוא הזמן להסתכל בו.

האפליקציה גם חוסמת את הכמות מצידה: `Plans__GuestChecksPerDay` (בדיקה חינמית אחת למבקר בלי חשבון),
`Plans__FreeChecksPerDay` (3), `Plans__ProChecksPerDay` (30), התקרה לחשבון `Limits__ChecksPerDay` (30) והתקרה הגלובלית
`Limits__ChecksPerDayGlobal` (1000 ליום על פני כולם). אלף בדיקות ביום ב-1.5 סנט זה בערך 15 דולר ביום: עשו את החשבון
הזה על המספרים שלכם לפני שאתם מעלים אחד מהם.

### 0.4 שולח דואר

בלעדיו, מי ששוכח סיסמה צריך לפתוח חשבון חדש. איתו, האפליקציה שולחת קישור אימות וקישור איפוס ב-SMTP. שני ספקים עובדים
עם ההגדרות של האפליקציה בדיוק כמו שהן:

**Resend** (<https://resend.com>, מכסה חינמית של 3,000 מיילים בחודש). מוסיפים את הדומיין, ואז API Keys ← Create API Key:

```
Email__Host=smtp.resend.com
Email__Port=587
Email__User=resend
Email__Password=re_xxxxxxxxxxxxxxxx
Email__From=OREVOSH <hello@looks.example.com>
Email__PublicOrigin=https://looks.example.com
```

**Postmark** (<https://postmarkapp.com>). מאמתים כתובת שולח או דומיין, ואז Servers ← השרת שלכם ← API Tokens; הטוקן הוא
גם המשתמש וגם הסיסמה:

```
Email__Host=smtp.postmarkapp.com
Email__Port=587
Email__User=<server API token>
Email__Password=<server API token>
Email__From=hello@looks.example.com
Email__PublicOrigin=https://looks.example.com
```

**כתובת השולח חייבת להיות על הדומיין שלכם** (`hello@looks.example.com`), והדומיין חייב להיות מאומת אצל הספק, אחרת
הדואר נדחה או נוחת בספאם.

**SPF ו-DKIM.** כשמוסיפים את הדומיין אצל הספק, הוא נותן שתיים-שלוש רשומות DNS להדביק באותו פאנל מפרק 0.1: רשומת `TXT`
ל-SPF (`v=spf1 include:…`), רשומת `CNAME` אחת או שתיים ל-DKIM, ובדרך כלל גם רשומת `TXT` ל-DMARC. הוסיפו אותן וחכו
שהלוח של הספק יגיד שהדומיין מאומת לפני שאתם שולחים משהו אמיתי. דילוג על השלב הזה הוא הסיבה הנפוצה ביותר לכך שקישור
איפוס לא מגיע.

**`Email__PublicOrigin` הוא לא אופציונלי על שרת.** הקישורים במיילים נבנים ממנו ולעולם לא מהכתובת שדרכה הגיעה הבקשה,
כי כותרת `Host` היא בשליטת מי ששולח את הבקשה וטוקן האיפוס רוכב על הקישור. עם דואר דלוק והערך הזה ריק, האפליקציה מזהירה
בהתחלה (`Email is on but Email:PublicOrigin is not set`), עונה 502 לשינוי כתובת, ופשוט לא שולחת כלום על סיסמה שנשכחה.

### 0.5 Stripe — לא חובה בהשקה

**אין צורך ב-Stripe כדי לעלות לאוויר.** משאירים את `Billing__Provider` על `manual` ומעניקים Pro ביד:

```bash
# Fly
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --pro <handle> 3"
# שרת
docker compose exec app dotnet FitCheck.Api.dll --pro <handle> 3
```

שלושה חודשים, 31 יום כל אחד, מעכשיו (1 עד 120 חודשים); `--pro <handle> off` מחזיר את החשבון ל-Free. דף ה-Pro מציג
הערה במקום כפתור רכישה, בהגדרות מופיע תאריך הסיום, ותקופה שנגמרה חוזרת ל-Free מעצמה. בשבועות הראשונים זה המסלול העדיף:
מי שרוצה Pro כותב לכם, ואתם לומדים מה הוא אומר.

כשאתם מוכנים לגבות, בלוח של Stripe, **קודם במצב בדיקה**:

1. מוצר **OREVOSH Pro** עם מחיר חודשי מתחדש אחד. מעתיקים את מזהה המחיר (`price_…`).
2. Developers ← API keys ← המפתח הסודי (`sk_test_…` במצב בדיקה, `sk_live_…` אחר כך).
3. Developers ← Webhooks ← מוסיפים נקודת קצה בכתובת **`https://looks.example.com/api/billing/webhook`**, עם חמשת
   האירועים שהאפליקציה קוראת: `checkout.session.completed`, `customer.subscription.created`, `invoice.paid`,
   `customer.subscription.updated` ו-`customer.subscription.deleted`. מעתיקים את סוד החתימה שלה (`whsec_…`).
   `--stripe-check` (4.2) נוקב בכל אירוע שפספסתם, אז אין צורך לספור כאן.
4. Settings ← Billing ← **Customer portal**: פותחים פעם אחת ושומרים הגדרה, ובוחרים מה לקוח יכול לעשות שם (לבטל, להחליף
   אמצעי תשלום). Stripe לא תיצור סשן פורטל לפני שההגדרה הזו קיימת, והכפתור "ניהול המנוי" באפליקציה הוא סשן פורטל.

פרק 4 מדליק את ארבעת הערכים האלה ובודק אותם. לשים לב להמשך: בתוך אפליקציות החנויות התשלום הזה אסור וצריך להסתיר אותו
(`STORE.md`, "Payments").

### 0.6 תיבת דואר לתמיכה, והבדיקה המשפטית

**תיבת דואר.** `hello@looks.example.com`, שמועברת לתיבה שמישהו באמת קורא. זו כתובת התמיכה בדפי החנויות, היא מופיעה
בדפים המשפטיים, וכל עוד החיוב ידני זה המקום שאליו אנשים כותבים כדי להתחיל או להפסיק Pro — האפליקציה אומרת להם את זה,
בכל השפות ("כדי לשנות או לבטל, כתבו לנו."). בדפים המשפטיים כתוב היום `hello@orevosh.app`; החליפו לשלכם.

**הבדיקה המשפטית.** לאפליקציה שלושה דפים משלה: `#/terms`, `#/privacy` ו-`#/guidelines` (גרסה 2, מתאריך 2026-09-12,
בעברית ובאנגלית, בקובץ `src/FitCheck.Api/wwwroot/app/views/legal.js`). הם מתארים במילים פשוטות מה הקוד באמת עושה. הם
**אינם ייעוץ משפטי**: שורת הדין החל היא מציין מקום, ושתי החנויות דורשות את מדיניות הפרטיות בכתובת ציבורית. שלחו אותם
לעורך דין בארץ שלכם לפני שהכתובת יוצאת מהצוות, ושנו כל דבר שלא הייתם עומדים מאחוריו — שורת הגרסה בתחתית הדף זזה עם
הטקסט.

### 0.7 רשימת הקניות

לפני שפותחים טרמינל, שיהיה מולכם:

1. הדומיין, והסיסמה לפאנל ה-DNS שלו.
2. חשבון Fly.io (או VPS עם כתובת IP ומפתח SSH שלכם עליו).
3. `sk-ant-…`, עם תקרת הוצאה חודשית.
4. מארח, משתמש, סיסמה וכתובת שולח ל-SMTP, עם SPF ו-DKIM מאומתים.
5. `hello@<הדומיין שלכם>`, שמגיע לתיבה אמיתית.
6. הדפים המשפטיים, אחרי שמישהו מוסמך קרא אותם.

לא חובה, וקל להוסיף אחר כך: ארבעת הערכים של Stripe, וזוג מפתחות VAPID לפוש (האפליקציה מייצרת אותו בפרק 1).

---

## 1. Fly.io, מאפס עד כתובת חיה

כל מה שבפרק הזה רץ **על המחשב שלכם**, מתוך תיקיית המאגר. לא צריך Docker ולא את ה-SDK של .NET: Fly בונה את הדמות.

### 1.1 מתקינים את flyctl ונכנסים

```bash
curl -L https://fly.io/install.sh | sh                        # macOS ולינוקס
brew install flyctl                                           # או, על מק עם Homebrew
pwsh -Command "iwr https://fly.io/install.ps1 -useb | iex"    # Windows PowerShell
```

ואז:

```bash
fly auth signup      # או: fly auth login
```

### 1.2 קובעים את הכתובת לפני שבונים משהו

הדומיין צרוב בקבצים סטטיים — תגיות התצוגה המקדימה של קישור, דפי הנחיתה ומעטפת החנות — ולכן הוא חייב להיות נכון
**לפני** שהדמות נבנית. עושים את זה עכשיו, פעם אחת:

```bash
node tools/brand/set-origin.js https://looks.example.com
git diff --stat
```

זה מחליף כל מקום שבו מופיע מציין המקום `looks.example.com` באחד-עשר הקבצים שהוא מכיר בשמם, ומדפיס כל אחד מהם עם
מספר ההחלפות. ארבעה מהם נשלחים לדפדפן או לחנות, וזו הסיבה שזה רץ לפני הבנייה: `src/FitCheck.Api/wwwroot/index.html` (`og:url`, `og:image`,
`twitter:image`), `wwwroot/landing/index.html` ו-`landing/index.he.html` (canonical, שני קישורי `hreflang`, `og:url`,
`og:image`, `twitter:image`), ו-`mobile/capacitor.config.json` (`server.url`, `allowNavigation`). שבעת האחרים הם
המסמכים שמצטטים את הכתובת, כדי שהפקודות שתעתיקו מהם כבר יהיו שלכם: `mobile/README.md` (כולל ההערה על
`WKAppBoundDomains`), `STORE.md` (כולל טבלת הכתובות), `MARKETING.md`, `brand-kit/README.md`, `DEPLOY.md`, `README.md`
ו-`.env.example`. לא נשאר שום קובץ לערוך ביד.

בכל זאת קראו את ה-diff, בגלל דבר אחד שהסקריפט לא יכול לדעת: הוא החליף גם את כתובות הדואר לדוגמה
(`hello@looks.example.com` ב-`.env.example` וב-`STORE.md` הפך ל-`hello@` הדומיין שלכם), וזה ניחוש לגבי תיבת הדואר
שלכם, לא עובדה. כדי לראות מה נשאר לפני הבנייה:

```bash
node tools/brand/set-origin.js --check    # יוצא עם 1 כל עוד נשאר מציין מקום באחד-עשר הקבצים
```

עשו commit לשינוי, כדי שהפריסה הבאה וכל הפריסות אחריה יישאו אותו.

### 1.3 יוצרים את האפליקציה

מתוך תיקיית המאגר:

```bash
fly launch --no-deploy --copy-config --name <your-app-name> --region fra
```

`--copy-config` משתמש ב-[`fly.toml`](fly.toml) של המאגר כמו שהוא וכותב לתוכו רק את השם שלכם. `--no-deploy` כי הנפח
והסודות קודמים. השם הופך לכתובת (`https://<your-app-name>.fly.dev`), ולכן הוא חייב להיות פנוי בכל Fly. `fra` זה
פרנקפורט; `fly platform regions` מראה את השאר — בחרו את הקרוב לאנשים שלכם והשתמשו באותו אזור בשלב הבא.

**אם `fly launch` מציע להוסיף Postgres, Redis, או "לייעל" את ההגדרות — אמרו לא.** לאפליקציה יש קובץ SQLite משלה על נפח
משלה, ו-`fly.toml` כבר אומר מכונה אחת, פורט 8080, עגינה ב-`/data`, בדיקת `/healthz` כל 30 שניות ו-HTTPS כפוי.

### 1.4 הנפח

```bash
fly volumes create data --size 3 --region fra --yes
```

`data` הוא השם ש-`fly.toml` עוגן ב-`/data`. כל מה שאנשים נותנים לאפליקציה חי שם: `orevosh.db` ותיקיית `storage/` עם
התמונות והקליפים. 3GB זה פיילוט; `fly volumes extend <volume id> --size 5` מגדיל אחר כך בלי להזיז כלום
(`fly volumes list` מראה את המזהה).

### 1.5 הסודות

סודות ב-Fly הם משתני סביבה. אפשר להגדיר כך כל הגדרה של האפליקציה — קו תחתון כפול מחליף כל נקודתיים, כלומר
`Plans:FreeChecksPerDay` הוא `Plans__FreeChecksPerDay`. כל `fly secrets set` מפעיל את המכונה מחדש, אז כדאי לקבץ; כאן
האפליקציה עוד לא רצה, אז ההפעלות מחדש לא עולות כלום.

**חובה** — בלי זה האפליקציה לא תבדוק שום לוק:

```bash
fly secrets set ANTHROPIC_API_KEY=sk-ant-...
```

**דואר, מפרק 0.4** (בלי זה שחזור חשבון פשוט כבוי):

```bash
fly secrets set \
  Email__Host=smtp.resend.com \
  Email__Port=587 \
  Email__User=resend \
  Email__Password=re_xxxxxxxxxxxxxxxx \
  "Email__From=OREVOSH <hello@looks.example.com>" \
  Email__PublicOrigin=https://looks.example.com
```

**חיוב** — קובעים את הספק עכשיו ואת השאר בפרק 4:

```bash
fly secrets set Billing__Provider=manual
```

כש-Stripe מוכן, ארבעת הערכים שלו ועוד הכתובת שאליה Checkout חוזר:

```bash
fly secrets set \
  Billing__Provider=stripe \
  Billing__StripeSecretKey=sk_live_... \
  Billing__StripePriceId=price_... \
  Billing__StripeWebhookSecret=whsec_... \
  Billing__PublicOrigin=https://looks.example.com
```

**התראות פוש.** מייצרים את זוג המפתחות עם האפליקציה עצמה, פעם אחת ויחידה — זוג חדש מוחק בשקט כל מנוי קיים. הפקודה הזו
לא צריכה בסיס נתונים, אז אפשר להריץ אותה עוד לפני הפריסה הראשונה:

```bash
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --vapid"
```

(אם המכונה עוד לא קיימת, הריצו `dotnet run -- --vapid` מתוך `src/FitCheck.Api` על המחשב שלכם עם ה-SDK של .NET, או
חזרו לזה אחרי 1.6.) ואז:

```bash
fly secrets set \
  Push__PublicKey=<שורת PUBLIC> \
  Push__PrivateKey=<שורת PRIVATE> \
  Push__Subject=mailto:hello@looks.example.com
```

**מנהלים.** `Admin__Handles__0` מקדם בכל הפעלה חשבון **שכבר קיים**, ושם משתמש שמופיע כאן כבר לא ניתן להרשמה על ידי
אף אחד — אז אל תגדירו אותו עדיין. קודם נרשמים (1.8, שלבים 2 ו-3), ואז או מוסיפים אותו או מריצים `--admin`, שלא דורש
הפעלה מחדש:

```bash
fly secrets set Admin__Handles__0=<handle>      # רק אחרי שהחשבון הזה קיים
```

**מסלולים** — ברירות המחדל (3 בדיקות חינם ביום, 30 ב-Pro, אחת למבקר) הן של הפיילוט, אז הגדירו רק מה ששונה.
`Plans__ProPriceText` הוא טקסט בדף ה-Pro, לא מחיר ש-Stripe גובה:

```bash
fly secrets set \
  Plans__FreeChecksPerDay=3 \
  Plans__ProChecksPerDay=30 \
  Plans__GuestChecksPerDay=1 \
  Plans__GuestAttemptsPerDay=20 \
  "Plans__ProPriceText=₪19 לחודש" \
  Plans__CompareNeedsPro=false
```

**לוח השבוע.** `Board__TimeZone` ו-`Board__WeekStartsOn` חותכים את השבוע ומתייגים את הארכיון, ושינוי שלהם אחר כך מזיז
את הגבולות של כל שבוע שהיה — **קבעו אותם לפני שהשבוע הראשון רץ**:

```bash
fly secrets set Board__TimeZone=Asia/Jerusalem Board__WeekStartsOn=Sunday
```

לשאר הכללים (`Board__MinChecksToCount` 1, `Board__MaxPerFirerPerAuthor` 3, `Board__NewAccountDays` 2, `Board__Size`
10, `Board__RisingDays` 30, `Board__CacheSeconds` 60) יש ברירות מחדל של פיילוט. השאירו את הספונסר
(`Board__Sponsor__Name`, `__Handle`, `__PrizeText`, `__Url`) ריק: ההיכל הראשון צריך להיות מורווח, לא מוצג בחסות.

**קישורי שותפים.** רק לתוכנית שבאמת הצטרפתם אליה; בלי רשימה, שום קישור לא מרוויח כלום ושום דבר לא נוסף:

```bash
fly secrets set 'Affiliate__Hosts__amazon.com=tag=orevosh-20'    # רק אם הצטרפתם
```

השאירו את `Affiliate__Disclosure` על ברירת המחדל `true`: זו השורה מתחת לכל קישור חנות שאומרת שהאפליקציה עשויה
להרוויח עמלה, ותנאי התוכניות מצפים לה.

**רישום בקשות.** כבוי כברירת מחדל. הדליקו אותו לימים הראשונים — שורה לכל בקשה היא הדרך לראות מה אנשים באמת עושים, ומה
נכשל:

```bash
fly secrets set Logging__Requests=true
```

כבו אותו כשההשקה נרגעת; זה הרבה שורות.

### 1.6 פורסים

```bash
fly deploy --ha=false
```

`--ha=false` חשוב: בלעדיו Fly מפעילה שתי מכונות לזמינות גבוהה, והאפליקציה הזו חייבת להיות תהליך אחד עם נפח אחד. Fly
בונה את הדמות מה-`Dockerfile` של המאגר על השרתים שלה.

שתי וריאציות, כשרוצים אותן:

```bash
fly deploy --image ghcr.io/<owner>/orevosh:latest --ha=false    # הדמות שתהליך ה-Release כבר בנה
fly deploy --local-only --ha=false                              # בנייה על המחשב שלכם (צריך Docker)
```

הראשונה היא המהירה ביותר אחרי שתהליך **Release image** של המאגר רץ (הוא בונה בכל דחיפה ל-`main` ובכל תג `v*`). החבילה
פרטית לשותפי המאגר עד שתהפכו אותה לציבורית ב-GitHub ← Packages ← orevosh ← Package settings.

ואז מסתכלים איך זה עולה:

```bash
fly status                                       # מכונה אחת, started, הבדיקה שלה עוברת
fly logs                                         # "Database /data/orevosh.db is new: creating the schema from the migrations."
```

מיד מתחת לשורה הזו הפעלה ראשונה מדפיסה גם אזהרה אחת של EF Core, `An operation of type 'SqlOperation' will be
attempted while a rebuild of table 'PostItems' is pending`: המיגרציה של סבב 10 מדברת עם עצמה על קובץ חדש. ההרצה
היבשה של המדריך הזה ראתה אותה על בסיס נתונים ריק, ו-`/readyz` ענה `db: ok` מיד אחריה. אין מה לעשות.

**ואז, מהלפטופ, סקריפט העשן:**

```bash
scripts/smoke.sh https://<your-app-name>.fly.dev
```

הוא צריך רק `curl`, וקורא בבת אחת את מה שפרק 1.8 מבקש לקרוא ביד: `/healthz` אומר `ok`, `/readyz` הוא 200 עם כל
בדיקה `ok`, שני דפי הנחיתה הם HTML שנושא את הסלוגן, `/api/config` עונה, המעטפת נושאת את תגי התצוגה המקדימה של
הקישור (והם אומרים את הכתובת שלכם, לא `looks.example.com`), כותרות האבטחה קיימות, והמניפסט מוגש. שורת `OK`/`FAIL`
אחת לכל בדיקה, `smoke: 8 ok, 0 failed` בסוף, וקוד יציאה 1 על כל כישלון, כך שסקריפט פריסה יכול לעצור עליו. מריצים
אותו אחרי כל פריסה, מול הכתובת שאנשים משתמשים בה (`https://looks.example.com` אחרי 1.7). בדיקת אורח היא בבחירה,
`scripts/smoke.sh https://… --check` (או `--check photo.jpg` עם תמונת לוק אמיתית), כי באתר חי היא מבזבזת קריאה
אמיתית לסטייליסט ואת הבדיקה החינמית האחת של הכתובת הזו להיום.

**אם הלוג אומר שאי אפשר לכתוב ל-`/data`** (`permission denied`, `unable to open database file`), שורש הנפח לא שייך
למשתמש `app` שאינו root. פעם אחת:

```bash
fly ssh console -C "chown -R app:app /data"
fly machine restart <machine id>                 # fly status מראה את המזהה
```

### 1.7 הדומיין

```bash
fly certs add looks.example.com
```

הפקודה מדפיסה את רשומת ה-DNS להוסיף — ה-`CNAME` או זוג ה-`A`/`AAAA` מפרק 0.1. מוסיפים אותה בפאנל ה-DNS של הרשם, ואז:

```bash
fly certs check looks.example.com                # "issued" כשזה גמור
```

ודאו ש-`Email__PublicOrigin`, ואם Stripe דלוק גם `Billing__PublicOrigin`, אומרים `https://looks.example.com` (1.5),
אחרת הקישורים במיילים והחזרה מ-Checkout יצביעו למקום אחר.

### 1.8 בדיקת העשן

לפי הסדר. כל שורה צריכה לעשות מה שהיא אומרת לפני שממשיכים.

```bash
curl https://looks.example.com/healthz          # ok           — האפליקציה חיה ובסיס הנתונים עונה
curl -s https://looks.example.com/readyz | jq   # {"ok":true,...} — האפליקציה מוכנה להגיש
curl -I https://looks.example.com/landing/      # 200          — דף הנחיתה
```

(ה-`| jq` רק מסדר את התשובה. אם אין לכם `jq` על המחשב, השמיטו אותו: השורה עדיין מדפיסה את אותו JSON, בשורה אחת.)

`scripts/smoke.sh https://looks.example.com` (1.6) עושה את שלוש השורות האלה ועוד חמש בבת אחת, ויוצא ב-1 כשמשהו מהן
לא בסדר. החצי של הטלפון למטה נשאר שלכם.

`/readyz` היא זו שכדאי לקרוא. היא ציבורית, אף פעם לא נשמרת במטמון, ומחזירה מסמך קטן: `ok`, ואובייקט `checks` שבו כל שם
הוא או `"ok"` או סיבה קצרה. היא בודקת `db` (שאילתה עונה והסכמה במיגרציה הנוכחית), `storage` (תיקיית התמונות קיימת
ואפשר לכתוב ולמחוק בה קובץ) וגם, כל עוד `Storage:Transcode` דלוק, `ffmpeg` (הבינארי נמצא). שלושתם תקינים זה 200; כל
כישלון הוא **503** עם שם הבדיקה שנפלה. שום דבר בה לא נוקב בנתיב, בגרסה או בסוד — אפשר להשאיר אותה ציבורית ולכוון אליה
מנטר זמינות.

ואז, בטלפון, ב-`https://looks.example.com`:

1. **דף הנחיתה** נטען, וגם העברי (`/landing/index.he.html`).
2. **נרשמים** עם שם המשתמש שאיתו תנהלו. זה החשבון ששלב 3 כאן מקדם.
3. **הופכים אותו למנהל:**
   ```bash
   fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --admin <handle>"
   ```
   בלי הפעלה מחדש. מרעננים את האפליקציה ותור המודרציה נמצא בתפריט של החשבון הזה. הרופא (שלב 5) סופר את המנהלים
   בבסיס הנתונים, ולכן שורת ה-`admin` שלו אומרת `ok` מכאן והלאה גם כש-`Admin__Handles` ריק.
4. **מאמתים את חשבונות המותגים הראשונים**, אחרי שכל מותג נרשם ואתם יודעים מי עומד מאחורי החשבון:
   ```bash
   fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --verify <handle>"
   ```
   זה הווי בתוך סימן ה-BRAND, וזו טענת האותנטיות היחידה שהאפליקציה עושה. `--unverify` מבטל.
5. **מריצים את הרופא, חי:**
   ```bash
   fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --doctor --live"
   ```
   `--doctor` לבד קורא את ההגדרות ואת המכונה — בסיס הנתונים, תיקיית האחסון, ffmpeg, אם מפתח Anthropic מוגדר, אם הדואר
   מוגדר ויש לו כתובת ציבורית, מפתחות הפוש, הגדרות החיוב, מכסות התוכניות, אזור הזמן של הלוח, רשימת המנהלים, מארחי
   השותפים והמקום הפנוי בדיסק — ארבע עשרה בדיקות בסך הכול — ומדפיס שורה לכל בדיקה,
   `ok` או סיבה קצרה, ויוצא ב-0 כשכל מה ששרת חי צריך קיים וב-1 אחרת. `--live` מוסיף את הבדיקות שיוצאות מהמכונה: קריאה
   קטנה אחת ל-Anthropic עם המפתח והמודל שלכם (כמה מאות טוקנים, שבריר סנט), וקריאה אחת ל-Stripe כשהספק הוא `stripe`.
   את שרת הדואר הוא לא מנסה: שום דבר כאן לא מתחבר ל-SMTP, ולכן הבדיקה של השולח היא לבקש מהאפליקציה איפוס סיסמה
   לכתובת שלכם. את `--doctor` אפשר להריץ מתי שרוצים; את `--doctor --live` הריצו עכשיו, ושוב אחרי כל שינוי במפתח.
   בפריסה ראשונה מצפים ל-`WARN push` (אין מפתחות VAPID עד שלב 7 ב-`DEPLOY.md`) ולשום כישלון; `FAIL anthropic` אומר
   שסוד המפתח לא הגיע למכונה (1.5).
6. **עושים בדיקה אמיתית.** מצלמים לוק באפליקציה, בוחרים כוונה, וקוראים את הפסיקה. זה הרגע שבו מפתח Anthropic, תיקיית
   האחסון והמודל חייבים להיות נכונים בבת אחת.
7. **מפרסמים אותו** ופותחים את `#/board`. ביום ההשקה הלוק יושב בלשונית **הבחירות של הסטייליסט** (לפי ציון, בלי אש);
   לשונית **לוקים** רוצה אש שנספרת, ואש מחשבון צעיר מ-`Board__NewAccountDays` (יומיים) לא נספרת — לא עכשיו ולא
   אחר כך: הגיל נמדד ברגע האש, ולכן אש מחשבון שני שיצרתם הרגע מעלה את המונה של הלוק ולעולם לא ממלאת את לשונית
   הלוקים, גם לא כשהחשבון כבר בן יומיים. כדי לראות את לשונית הלוקים מתמלאת, תנו אש מחשבון שכבר בן יומיים (או,
   כעבור יומיים, בטלו את האש ותנו אותה שוב מהחשבון החדש: זו אש חדשה, שנמדדת אז), או קבעו `Board__NewAccountDays=0`
   לבדיקת העשן — ואז אש יום ההשקה נספרת מיד — והחזירו אחר כך. ההרצה היבשה של המדריך הזה הוכיחה בדיוק את זה.
8. **פותחים את דף המספרים**, `https://looks.example.com/#/admin/metrics`, מחוברים כמנהל. הוא אמור להראות את הבדיקות
   ואת האנשים שיצרתם בשלבים 6 ו-7 (בדיקה אחת ואדם אחד אם עצרתם בשלב 6; בדיקות אורח לא נספרות). מי שאינו מנהל מקבל
   סירוב.
9. **מתקינים למסך הבית** — שיתוף ← "הוסף למסך הבית" באייפון; כרום מציע "התקנה" מעצמו באנדרואיד — ובודקים שהאפליקציה
   נפתחת במסך מלא עם האייקון שלה.

אם משהו מזה נכשל, פרק 6 הוא המפה.

### 1.9 גיבויים ב-Fly

Fly מצלמת את הנפח כל יום ושומרת חמישה ימים. זה גיבוי אצל אותו ספק; קחו גם אחד משלכם:

```bash
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --backup /data/backups/manual --keep 7"
fly ssh console -u app -C "tar czf /data/backups/manual/storage-<stamp>.tgz -C /data/backups/manual storage-<stamp>"
fly sftp get /data/backups/manual/orevosh-<stamp>.db ./orevosh-<stamp>.db
fly sftp get /data/backups/manual/storage-<stamp>.tgz ./storage-<stamp>.tgz
fly ssh console -u app -C "rm -rf /data/backups/manual"
```

`--backup <dir>` כותב עותק עקבי של בסיס הנתונים בקובץ אחד (הצילום המקוון של SQLite עצמה, בטוח בזמן שהאפליקציה כותבת)
ועותק של תיקיית התמונות, ומדפיס `database: …` ו-`storage: …` עם הנתיבים. `--keep <n>` גוזם את התיקייה ל-`n` עותקי
בסיס הנתונים ול-`n` עותקי האחסון החדשים ביותר, אחרי שהעותק החדש הושלם, כדי שהרצה שבועית לא תמלא את הנפח. העותקים
חולקים את הנפח של 3GB עם המידע החי, ולכן השורה האחרונה מוחקת אותם.

ה-`<stamp>` בשורות 2 עד 4 הוא זה שהשורה הראשונה הדפיסה, אז הריצו שורה-שורה וקראו את הפלט לפני ההמשך.
`/data/backups/manual` היא תיקייה נפרדת בכוונה: השורה האחרונה מרוקנת אותה, ושום דבר אחר על הנפח לא שומר שם עותקים.

**הקבצים מכילים כל תמונה וכל קליפ שאנשים נתנו לאפליקציה.** שמרו עליהם פרטיים אצלכם כמו שהם על הנפח. אין cron על המכונה
של Fly: הריצו את חמש השורות פעם בשבוע מהמחשב שלכם, או מתוך משימה מתוזמנת ב-GitHub Actions עם סוד `FLY_API_TOKEN`
(`fly tokens create deploy`).

דלגו ל**פרק 3**.

---

## 2. המסלול השני: שרת שכור אחד עם Docker

בחרו בזה רק אם אתם רוצים את המכונה. בפעם הראשונה זה בערך שעה.

מה מקבלים בסוף:

```
phone ── https://looks.example.com ──▶ Caddy (certificate, port 443) ──▶ the app (port 8080, container only)
                                                                              │
                                                                         /data volume: orevosh.db + storage/
```

### 2.1 DNS קודם

הוסיפו עכשיו את רשומת ה-`A` (ו-`AAAA` אם יש IPv6) מפרק 0.1, וחכו עד ש-`ping looks.example.com` עונה מכתובת השרת.
Caddy מבקש תעודה מ-Let's Encrypt ברגע שהוא עולה, וזה עובד רק כשהשם מצביע על המכונה הזו.

### 2.2 השרת

נכנסים ב-SSH כ-root. המתקין של Docker מביא איתו גם את Compose:

```bash
apt-get update && apt-get upgrade -y
curl -fsSL https://get.docker.com | sh
```

פותחים רק את מה שהאפליקציה משתמשת בו:

```bash
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable
```

על מכונה של 1GB, קצת swap כדי שהבנייה לא תיגמר בלי זיכרון:

```bash
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
```

### 2.3 הקוד, והכתובת שלכם

```bash
apt-get install -y git
git clone https://github.com/<you>/<repository>.git /opt/orevosh
cd /opt/orevosh
```

כל מה שלמטה רץ מתוך `/opt/orevosh`. **קבעו את הכתובת לפני הבנייה** — תגיות התצוגה המקדימה ודפי הנחיתה הם קבצים
סטטיים בתוך הדמות:

```bash
node tools/brand/set-origin.js https://looks.example.com
```

(אם אין node על השרת, הריצו את זה על המחשב שלכם, עשו commit, ו-`git pull` כאן. 1.2 מונה את אחד-עשר הקבצים שהוא
משכתב; `node tools/brand/set-origin.js --check` כאן אומר אם העותק שאתם עומדים לבנות עדיין נושא את מציין המקום.)

### 2.4 קובץ ה-`.env`

```bash
cp .env.example .env
nano .env
```

`.env.example` מוסבר שורה-שורה; הערכים הם אותם ערכים שפרק 1.5 מגדיר כסודות ב-Fly, באותו איות. כמינימום:

```
DOMAIN=looks.example.com
ANTHROPIC_API_KEY=sk-ant-...
```

ואז בלוק הדואר (0.4), `Billing__Provider=manual`, `Board__TimeZone` ו-`Board__WeekStartsOn`, ו-`Logging__Requests=true`
לימים הראשונים. השאירו את `Admin__Handles__0` מוערת — היא באה אחרי שנרשמתם. `.env` לא נכנס ל-git ונשאר על השרת; אל
תדביקו אותו בשום מקום.

### 2.5 הפעלה ראשונה

```bash
docker compose up -d --build
docker compose ps                 # app ו-caddy: running / healthy
docker compose logs -f app        # Ctrl-C כדי להפסיק להסתכל
```

הבנייה הראשונה מורידה את דמויות .NET ומהדרת את האפליקציה — כמה דקות על VPS קטן. הלוג אומר מה קרה לבסיס הנתונים
(`Database /data/orevosh.db is new: creating the schema from the migrations.`), וכיוון שהדמות כוללת ffmpeg, גם
`Transcoding is on: ffmpeg version …`.

Caddy מביא את התעודה בבקשה הראשונה; תנו לו עד דקה. אם היא לא מגיעה, `docker compose logs caddy` אומר למה, וזה כמעט
תמיד DNS שעוד לא מצביע לכאן או פורט 80/443 שסגור בחומת האש של הספק.

### 2.6 אותה בדיקת עשן

בדיוק פרק 1.8, עם הפקודות בצורת השרת:

```bash
curl https://looks.example.com/healthz
curl -s https://looks.example.com/readyz | jq
docker compose exec app dotnet FitCheck.Api.dll --admin <handle>      # אחרי שנרשמתם
docker compose exec app dotnet FitCheck.Api.dll --verify <handle>
docker compose exec app dotnet FitCheck.Api.dll --doctor --live
```

ומהלפטופ, `scripts/smoke.sh https://looks.example.com` (1.6): שמונה הבדיקות לקריאה בלבד בבת אחת, יציאה ב-1 כשאחת
מהן לא בסדר.

מפתחות פוש, אם רוצים התראות (`docker compose run`, כי הפקודה הזו לא צריכה בסיס נתונים):

```bash
docker compose run --rm --no-deps app dotnet FitCheck.Api.dll --vapid
```

מדביקים את שתי השורות ל-`Push__PublicKey` ו-`Push__PrivateKey` ב-`.env`, ואז `docker compose up -d`.

ואז עושים את החצי הטלפוני של 1.8: נרשמים, בודקים לוק, מפרסמים, מדליקים, פותחים `#/board` ו-`#/admin/metrics`,
ומתקינים למסך הבית.

### 2.7 גיבויים, ב-cron

`tools/backup.sh` קורא ל-`--backup` של האפליקציה בתוך הקונטיינר, מוציא את התוצאות ל-`backups/` ליד הקוד, גוזם, ומוחק
את תיקיית העבודה מהנפח בכל מקרה:

```bash
tools/backup.sh
ls -l backups/
# -rw------- orevosh-20260905033000.db    drwx------ storage-20260905033000/
```

הוא שומר את `KEEP` עותקי בסיס הנתונים החדשים ביותר (14 כברירת מחדל) ואת `KEEP_STORAGE` העותקים החדשים ביותר של תיקיית
המדיה (2 כברירת מחדל) — בסיס הנתונים הוא מגהבייטים, עותק מדיה הוא כל התיקייה. כל מה שהוא כותב קריא ל-root בלבד
(`umask 077`, מצב 700 לתיקייה, ו-`chmod -R go-rwx` אחרי ה-`docker cp`).

תיקיית העבודה שהוא משתמש בה בתוך הקונטיינר נוצרת מחדש בכל הרצה (`mktemp -d /data/backup-scratch.XXXXXXXX`) ונמחקת בכל
מסלול יציאה, גם בהרצה שנכשלה באמצע, כך שלא נשארת על הנפח שום מדיה של אף אחד. זו תיקייה נפרדת בכוונה: סקריפט הגיבוי
השני במאגר, `scripts/backup.sh`, משאיר את העותקים שלו **על** הנפח ב-`/data/backups/nightly` וגוזם אותם שם, ושניהם לא
יכולים להגיע לקבצים אחד של השני. אם אתם מפנים אחד מהם לתיקייה משלכם, תנו לו תיקייה שאף אחד אחר לא כותב אליה — ה-`--keep`
של האפליקציה גוזם כל `orevosh-*.db` וכל `storage-*` בתיקייה שהוא מקבל, מי שלא כתב אותם.

כל לילה ב-03:30:

```bash
(crontab -l 2>/dev/null; echo '30 3 * * * cd /opt/orevosh && KEEP=14 KEEP_STORAGE=2 tools/backup.sh >> /var/log/orevosh-backup.log 2>&1') | crontab -
```

**גיבוי על אותו דיסק הוא לא גיבוי.** העתיקו את `backups/` מחוץ למכונה לפחות פעם בשבוע, ושמרו עליו שם פרטי כמו שהוא
כאן. למכונת לינוקס אחרת, תוך שמירה על ההרשאות:

```bash
scp -rp root@looks.example.com:/opt/orevosh/backups ~/orevosh-backups
```

או מוצפן לענן, שאין לו הרשאות קבצים משלו:

```bash
tar cz backups | gpg -c -o backups-$(date +%F).tgz.gpg
```

**שחזרו גיבוי אחד עכשיו, לפני שתצטרכו** (הפקודות בפרק 6.4). גיבוי שמעולם לא שוחזר הוא תקווה, לא גיבוי.

---

## 3. היום הראשון

### 3.1 הכתובת כבר נכונה

הרצתם `set-origin` לפני הבנייה (1.2 או 2.3), אז התצוגות המקדימות ודפי הנחיתה נושאים את הדומיין האמיתי. תוכיחו את זה:
הדביקו את `https://looks.example.com` באפליקציית צ'אט ובדקו שהכרטיס מראה את התמונה בגודל 1200×630 ואת הסלוגן, ולא קישור
ערום. אם לא — הכתובת לא נכנסה לדמות; קבעו אותה, בנו מחדש, פרסו.

### 3.2 צילומי מסך אמיתיים בערכת המותג

צילומי המסך לחנויות והטלפונים בדף הנחיתה שנמצאים במאגר מראים את **הלוק הסינתטי של מבחן הדפדפן ואת המצלמה המזויפת של
Chromium**. הם מציינים מקום. צלמו צילומים אמיתיים בטלפון — מסך הבדיקה, התוצאה, לוק בפיד, לוק מוצג, המצלמה מקליטה —
שימו אותם ב-`tools/brand/templates/screens/` תחת השמות שכבר שם, וייצרו מחדש:

```bash
(cd tools/brand && npm install)              # פעם אחת; או NODE_PATH=tools/e2e/node_modules אם הרצתם את מבחן הדפדפן
node tools/brand/render-kit.js               # הכול, משורש המאגר
node tools/brand/render-kit.js store web     # או רק מסכי החנויות ואלה של דף הנחיתה
```

`brand-kit/README.md` מפרט כל קובץ שהוא כותב. עשו את זה לפני כל הגשה לחנות: אפל דוחה צילומי מסך שלא מראים את
האפליקציה כפי שנשלחה.

### 3.3 פרסמו בעצמכם את עשרת הלוקים הראשונים

פיד ריק אומר למבקר ללכת. "עשרת הפוסטים הראשונים" ב-`MARKETING.md` היא הרשימה; פרסמו אותם מלוקים אמיתיים, מהחשבון שלכם
ומחשבון שני, ו**תייגו את הפריטים בכל אחד** — לפחות המותג והדגם, וקישור חנות רק היכן שיש אחד אמיתי. זה מה שמלמד את כל
השאר איך נראה פוסט כאן.

פתחו אתגר אחד עם פרס אמיתי, ודאגו שלפחות שלושה חשבונות מותג יהיו רשומים ומאומתים ב-`--verify`.

### 3.4 הזמינו את הקהילה הצרה

התוכנית של ארבעה שבועות ב-`MARKETING.md`: קהילה אחת קטנה מספיק כדי לראות את עצמה בפיד — קמפוס אחד, בית ספר אחד
לעיצוב, סצנת סטריטוור של עיר אחת. שבוע 1 הוא **עשרים אנשים שמוזמנים ביד**, הודעה אחת לכל אחד, בלי הפצה קבוצתית. בקשו
מכל אחד בדיקה אחת ופוסט אחד, והקשיבו למה שהם שואלים.

אל תפתחו קהילה שנייה לפני שאנשי שבוע 1 עדיין בודקים בשבוע 4.

### 3.5 עוקבים אחרי המספרים, ואחרי הלוג

**דף המספרים** הוא `https://looks.example.com/#/admin/metrics`, מחוברים כמנהל — שיעור החזרה כמספר הגדול, אריחים
לבדיקות, אנשים, פוסטים, אשים, תגובות, פריטים שתויגו, הקשות על קישורי חנות וצפיות בלוח, והתפלגות הציונים כעמודות.
`MARKETING.md` אומר אילו מהם חשובים ואיך נראה מספר טוב. קראו אותו שבועית, לא שעתית.

**הלוג** הוא `fly logs` או `docker compose logs --since 1h app`. השורות ששווה לחפש:

```bash
fly logs | grep "Guest sweep"        # כל שעה: כמה בדיקות אורח לא נתבעו — קריאות מודל שאיש לא נרשם בעבורן
fly logs | grep "Board: week"        # אחרי חצות שבת: "closed, 38 rows", או "had no counted fires"
fly logs | grep "Items:"             # "Items: 3 on post <id> by <id>" — מישהו תייג את הפריטים בלוק שלו
fly logs | grep "Export:"            # "Export: <id> took their data" — מישהו הוריד את המידע שלו
fly logs | grep "Block:"             # "Block: <id> blocked <id>" — מישהו חסם מישהו
fly logs | grep "Transcoding"        # בהתחלה: "Transcoding is on: ffmpeg version …"; לכל קליפ, שורה עם גדלים וזמן
fly logs | grep "Email"              # "Email sent to …: …" לכל אימות ואיפוס; "Email to … failed: …" כשזה לא הצליח
```

שתי האזהרות שכדאי לקרוא ולא לגלול מעליהן: `Board: the close failed; it runs again in five minutes`, וגם
`No link origin for host …: set Email:PublicOrigin …`, שאומרת שדואר השחזור הולך בשקט לשום מקום.

על שרת, החליפו את `fly logs` ב-`docker compose logs --since 1h app`. `Logging__Requests=true` (1.5, 2.4) מוסיף שורה
לכל בקשה על כל זה.

### 3.6 שגרת הגיבוי

- **ב-Fly:** צילומי הנפח היומיים אוטומטיים. הריצו את חמש השורות של 1.9 שבועית ושמרו את הקבצים במקום פרטי.
- **על שרת:** ה-cron של 2.7 רץ כל לילה. העתיקו את `backups/` מחוץ למכונה שבועית.
- **בשני המקרים:** שחזרו גיבוי אחד למקום זמני לפני שתצטרכו (6.4).

### 3.7 כש-Anthropic למטה

זה קורה, והאפליקציה בנויה לזה. בדיקה שהמודל לא הצליח לענות עליה נשמרת עם סטטוס שגיאה, והאדם מקבל
**502 `error.model_failed`** — "הסטייליסט לא הצליח להסתכל על התמונה הזו. שווה לנסות שוב בעוד רגע.", בשפה שלו.
**זה לא מנכה לו מהמכסה**: קריאות שנכשלו אינן נספרות מול `Plans__*` ולא מול `Limits__ChecksPerDay`, וגם הבדיקה
החינמית האחת של אורח שורדת כישלון (המקום של הכתובת שלו מוחזר; `error.guest_limit` נשלח רק על לוק שבאמת ניתן).
האפליקציה עצמה מנסה שוב פעמיים, עם השהיה קצרה, לפני שהיא מוותרת על התמונה הזו.

אז אין מה לעשות חוץ מלחכות, ולהגיד את זה אם שואלים. מה שכן שווה לבדוק קודם זה אם זו Anthropic או אתם:
`--doctor --live` עושה קריאה אחת עם המפתח שלכם ואומר מה מהשניים.

---

## 4. כסף

דלגו על כל זה כל עוד `Billing__Provider` הוא `manual` — `--pro <handle> <months>` הוא מסלול שדרוג שלם, ודף ה-Pro
וההגדרות מסבירים אותו לאדם.

### 4.1 מדליקים את הספק, במצב בדיקה

מגדירים את ארבעת הערכים מפרק 0.5, עם מפתחות **בדיקה** (`sk_test_…`):

```bash
# Fly
fly secrets set Billing__Provider=stripe Billing__StripeSecretKey=sk_test_... \
  Billing__StripePriceId=price_... Billing__StripeWebhookSecret=whsec_... \
  Billing__PublicOrigin=https://looks.example.com

# שרת: אותן חמש שורות ב-.env, ואז
docker compose up -d
```

Stripe חי רק כשהספק הוא `stripe` **וגם** שלושת המפתחות מוגדרים. אז `https://looks.example.com/api/config` אומר
`"billing": true` תחת `plans`, ודף ה-Pro מציג את הכפתור במקום ההערה.

### 4.2 בודקים לפני שמישהו משלם

```bash
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --stripe-check"
docker compose exec app dotnet FitCheck.Api.dll --stripe-check      # על שרת
```

`--stripe-check` שואל את Stripe על ההגדרות שנתתם לאפליקציה ומדפיס שלוש שורות. `billing` קוראת רק את ההגדרות: הספק
ושלושת המפתחות, כל אחד עם הקידומת שאמורה להיות לו. `stripe-live` קוראת את המחיר בחזרה: המפתח מתקבל, המחיר קיים באותו
מצב כמו המפתח, והוא מחיר **מתחדש** שלא הועבר לארכיון — Checkout נפתח במצב מנוי, ולכן מחיר חד-פעמי נופל כאן במקום
ליפול על האדם הראשון שילחץ Go Pro. `stripe-webhook` קוראת את רשימת נקודות הקצה של Stripe: אחת מהן היא הכתובת שלכם
ועוד `/api/billing/webhook`, היא מופעלת, והאירועים שלה מכסים את חמשת האירועים שהאפליקציה קוראת (נקודת קצה עם `*`
נחשבת). כל מה שחסר נוקב בשמו — "registered, but not for `customer.subscription.deleted`" אומר שביטולים לעולם לא
יסיימו Pro. נקודת קצה על אותו נתיב תחת שם אחר שאתם עונים בו (כתובת ה-`fly.dev` שלכם, למשל) היא אזהרה שנוקבת בשמה ולא
כישלון, כי ה-webhook צריך רק להגיע לנתיב.

שתי בקשות GET, שום כתיבה, שום חיוב, ושום מפתח לא מודפס. הריצו אותו אחרי כל שינוי בערך של Stripe, ושוב כשמחליפים
מפתחות בדיקה במפתחות חיים.

### 4.3 רכישת בדיקה

עם ה-Stripe CLI על המחשב שלכם:

```bash
stripe listen --forward-to https://looks.example.com/api/billing/webhook
```

זה מדפיס `whsec_…` לסשן; שימו אותו ב-`Billing__StripeWebhookSecret` בזמן הבדיקה. ואז, באפליקציה, פותחים את דף ה-Pro,
לוחצים Go Pro, ומשלמים בכרטיס הבדיקה של Stripe `4242 4242 4242 4242`, תוקף עתידי כלשהו ו-CVC כלשהו. מה שאמור לקרות:

1. Checkout חוזר אל `/#/pro?checkout=success`.
2. הלוג אומר שהחשבון הוא Pro עד תאריך בערך 35 יום קדימה.
3. בהגדרות ובדף ה-Pro מופיע תאריך הסיום, והמכסה היומית היא עכשיו 30.
4. **"ניהול המנוי"** מופיע בהגדרות ובדף ה-Pro. לחצו: זה אמור לפתוח את הפורטל של Stripe לאותו לקוח ולחזור
   אל `/#/settings`. אם במקום זה מופיע "כדי לשנות או לבטל, כתבו לנו." — או שהספק עדיין ידני, או שהחשבון הזה מעולם לא
   עבר Checkout; ואם זה נכשל לגמרי, חסרה הגדרת הפורטל מפרק 0.5 שלב 4.
5. מבטלים בדף של Stripe. ה-webhook מסיים את ה-Pro, והחשבון חוזר ל-Free בתום מה ששילם עליו.

הביטול הוא בצד של Stripe או `--pro <handle> off`; לאפליקציה אין כפתור ביטול משלה, מתוך החלטה.

### 4.4 מפתחות חיים

מחליפים `sk_test_…` ב-`sk_live_…`, יוצרים את נקודת הקצה של ה-webhook שוב **במצב חי** ולוקחים את ה-`whsec_…` החדש
שלה, מגדירים את שניהם, ומריצים `--stripe-check` עוד פעם. ואז עושים רכישה אמיתית אחת בכרטיס שלכם ומזכים אותה מהלוח של
Stripe.

**שום דבר באפליקציה הזו עוד לא רץ מול חשבון Stripe חי.** המנוי, החידוש והביטול האמיתיים הראשונים שלכם הם ההוכחה. יומן
האירועים של Stripe והלוג של האפליקציה הם המקום להסתכל בו אם אחד מהם מתנהג לא כשורה.

### 4.5 המסלול הידני נשאר פתוח

`--pro <handle> <months>` ממשיך לעבוד גם כש-Stripe דלוק, והוא הכלי הנכון לתומכים מוקדמים, למותג שרוצים להודות לו,
ולכל מי שהתשלום שלו נכשל מסיבה שאינה באשמתו. הוא לא יוצר כלום בצד של Stripe, ולכן לחשבון כזה אין מה לנהל בפורטל —
האפליקציה אומרת לו לכתוב לכם במקום.

---

## 5. החנויות, בהמשך

אפליקציית הרשת כבר מתקינה למסך הבית ומתנהגת כמו אפליקציה מקומית. מעטפות החנויות ב-`mobile/` הן עטיפת Capacitor
שמצביעה על הכתובת שלכם; `mobile/README.md` בונה אותן, ו-`STORE.md` מחזיק את הדפים, תוויות הפרטיות והערות הבדיקה.

לפני שמגישים משהו, כל אלה חייבים להיות נכונים:

1. **צילומי מסך אמיתיים** (3.2). אפל דוחה סינתטיים.
2. **דרך לחסום אדם** — הנחיה 1.2 של אפל דורשת את זה. עכשיו זה באפליקציה: "חסימה" יושבת ליד "דיווח" בתפריט של לוק
   ובתפריט ה-"…" בפרופיל של מישהו אחר, עם אישור, ובהגדרות ← **חשבונות חסומים** יש רשימה של מי שחסמתם עם ביטול חסימה
   ליד כל אחד. שום דבר לא מספר לנחסם.
3. **כתובת תמיכה** שמישהו עונה בה בתוך כמה ימים (0.6).
4. **מדיניות פרטיות בכתובת ציבורית**: `https://looks.example.com/#/privacy`, עם התנאים וההנחיות לידה.
5. **כלל התשלומים.** אפל וגוגל אוסרות את התשלום דרך Stripe בתוך האפליקציה העטופה. או שמסתירים שם את רכישת ה-Pro
   לגמרי, או שמוסיפים StoreKit ו-Play Billing. `STORE.md`, "Payments".

שני דברים שהדף בחנות חייב גם לומר, ושניהם נכונים באפליקציה היום: **מחיקת חשבון נמצאת באפליקציה** (הגדרות ← מחיקת
חשבון, צעד אחד, הכול נעלם) ו**אדם יכול להוריד את המידע שלו** (הגדרות ← הורדת המידע שלך, קובץ JSON עם כל מה שכתב).

---

## 6. כשמשהו נשבר

### 6.1 קודם כול שואלים את האפליקציה

```bash
curl -s https://looks.example.com/readyz | jq     # מי מבין db / storage / ffmpeg לא מרוצה
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --doctor"
fly ssh console -u app -C "dotnet /app/FitCheck.Api.dll --doctor --live"
```

על שרת: `docker compose exec app dotnet FitCheck.Api.dll --doctor`.

`/healthz` עונה `ok` כשבסיס הנתונים עונה, וזו הכתובת שהפרוקסי ומנטרי הזמינות דוגמים. `/readyz` היא התשובה המלאה יותר
וזו שכדאי לקרוא ביד. `--doctor` מכסה את ההגדרות ש-`/readyz` לא רואה; `--doctor --live` מכסה את מה שמחוץ למכונה.

### 6.2 ואז הלוג

```bash
fly logs --no-tail | tail -200
docker compose logs --since 1h app          # על שרת
docker compose logs caddy                   # תעודות, על שרת
```

כל 5xx נרשם עם הנתיב שלו. שורות ה-grep של 3.5 הן השגרתיות.

### 6.3 החשודים המיידיים

- **"הסטייליסט לא הצליח להסתכל על התמונה הזו."** — מפתח Anthropic, תקרת ההוצאה, או Anthropic עצמה.
  `--doctor --live` אומר מה מהם. אף אחד לא איבד מכסה (3.7).
- **אין מייל אימות או איפוס** — `Email__PublicOrigin` לא מוגדר (הלוג אומר `No link origin for host …`), SPF/DKIM לא
  מאומתים, או שהספק דוחה את השולח. `--doctor` קורא את הגדרות הדואר, אבל שום דבר לא מנסה להתחבר: שלחו לעצמכם איפוס
  סיסמה מהאפליקציה כדי לבדוק את השולח.
- **התעודה לא מגיעה** — ה-DNS עוד לא מצביע לכאן, או ש-80/443 סגורים במעלה הזרם.
- **`unable to open database file`** — הבעלות על הנפח (1.6).
- **קליפים מתנגנים באנדרואיד ולא באייפון** — `"transcoding": false` ב-`/api/config` אומר שאין ffmpeg בדמות;
  `/readyz` אומר את אותו הדבר. בנו מחדש עם `ffmpeg` בשלב ההרצה של ה-Dockerfile.
- **הלוח לא נסגר** — `grep "Board:"`. הסוגר רץ בהתחלה וכל חמש דקות ומשלים שבועות שהוחמצו מעצמו; `the close failed`
  היא השורה שדורשת אתכם.
- **הדיסק מלא** — `fly ssh console -C "df -h /data"` או `df -h /`. דיסק מלא עוצר העלאות ואחר כך כתיבות. הגדילו את
  הנפח, או גזמו גיבויי מדיה ישנים (`KEEP_STORAGE=1`).

### 6.4 שחזור מגיבוי

**על שרת**, הסקריפט שואל לפני שהוא מחליף משהו:

```bash
cd /opt/orevosh
tools/restore.sh backups/orevosh-20260905033000.db backups/storage-20260905033000
```

בלי הארגומנט השני משחזרים רק את בסיס הנתונים. הוא עוצר את האפליקציה, מחליף את הקבצים, מעביר אותם למשתמש של
הקונטיינר ומפעיל שוב; הלוג יגיד מה הוא עשה עם הסכמה.

**ב-Fly** האפליקציה רצה בזמן שמחליפים את הקובץ, אז בחרו רגע שקט:

```bash
fly sftp shell
# put orevosh-20260905033000.db /data/incoming.db
# exit
fly ssh console -C "sh -c 'cd /data && mv incoming.db orevosh.db && rm -f orevosh.db-wal orevosh.db-shm && chown app:app orevosh.db'"
fly ssh console -C "ls -l /data/orevosh.db"      # חייב להגיד: app app
fly machine restart <machine id>
```

**אל תוותרו על ה-`chown`.** `fly sftp` ו-`fly ssh console` בלי `-u` רצים כ-root, והאפליקציה רצה כמשתמש `app`
(`USER app` ב-`Dockerfile`). קובץ בסיס נתונים בבעלות root חוזר אחרי ההפעלה מחדש כ-`unable to open database file`
ב-`fly logs`, והאתר נשאר למטה — בדיוק ברגע הכי גרוע, כי אתם כבר באמצע שחזור. שורת ה-`ls -l` היא ההוכחה לפני ההפעלה
מחדש.

כתיבות שקרו בין ההחלפה להפעלה מחדש אבדו. תיקיית המדיה חוזרת באותה דרך: `put` לארכיון, פריסה שלו על `/data/storage`,
ואז מעבירים גם אותה — `fly ssh console -C "chown -R app:app /data/storage"`. צילומי הנפח של Fly הם הדרך השנייה חזרה:
`fly volumes snapshots list <volume id>`, ואז `fly volumes create data --snapshot-id <id> --region fra`.

### 6.5 חזרה לגרסה קודמת של הקוד

המידע נמצא על הנפח והסכמה מנוהלת במיגרציות, ולכן חזרה לדמות הקודמת לא נוגעת בחשבון של אף אחד.

```bash
# Fly, לדמות קודמת
fly releases                                     # הרשימה, החדש למעלה
fly deploy --image ghcr.io/<owner>/orevosh:<previous sha> --ha=false

# Fly, לקומיט קודם
git checkout <previous commit> && fly deploy --ha=false

# שרת
cd /opt/orevosh
git checkout <previous commit> && docker compose build app && docker compose up -d
```

חזרה לאחור **לא מבטלת מיגרציה**. אם הגרסה הקודמת קדמה לשינוי סכמה, שחזרו גם את גיבוי בסיס הנתונים שמלפני העדכון
(6.4) — בשביל זה קיימים 2.7 ו-1.9, ובשביל זה שלב העדכון לוקח גיבוי קודם.

### 6.6 שום דבר מכל זה לא עזר

קחו את הפלט של `--doctor --live`, את 200 השורות האחרונות של הלוג, ואת `curl -s …/readyz`, בסדר הזה. ביניהם הם אומרים
מה האפליקציה רואה, מה אמרו לה, ומה היא עשתה.

## Round 13 — the verdict's own verdict, languages shipped only when real (appended; the lead folds it into 3.5 and the Hebrew half)

**3.5, one more number.** `#/admin/metrics` has a block called *The stylist*: *Tip landed*, yes over yes and no, with
the split by intent and by language under it. Read it before the score distribution. Under 60% on one intent, tighten
that intent's line in the rubric; under 60% on one language, the translation of the tips is the suspect. *No-outfit
answers* next to it says how often people sent something that was not an outfit; a high number means the photo hint on
the check screen is not landing.

**Languages, before you invite anyone who reads Arabic or Russian.** The app ships with English and Hebrew live
(`Languages__Enabled__0=en`, `__1=he`). Arabic and Russian are in the build but off until a native reader has been
through `src/FitCheck.Api/wwwroot/i18n/ar.json` (or `ru.json`) and the matching block in `Services/Localizer.cs`; then
add `Languages__Enabled__2=ar` (or `ru`), restart, and check `…/api/config` lists it. Until then a phone in Arabic gets
the English app, and nothing half-translated reaches anyone.

**A photo with no outfit costs nobody a check.** Three times a day per person the stylist's "no outfit here" spends
nothing; after that it counts. Your Anthropic bill counts every one of them, so the guest brake and the global ceiling
stay where they are.

---

## סבב 13 — הפסק על הפסק, שפות רק כשהן אמיתיות (נספח; המוביל משלב ב-3.5 ובחלק העברי)

**3.5, עוד מספר אחד.** ב-`#/admin/metrics` יש בלוק בשם *הסטייליסט*: *הטיפ קלע*, כן מתוך כן ולא, עם פילוח לפי יעד ולפי
שפה. קוראים אותו לפני התפלגות הציונים. מתחת ל-60% ביעד אחד — מהדקים את השורה של היעד הזה ברובריקה; מתחת ל-60% בשפה
אחת — התרגום של הטיפים הוא החשוד. *תשובות "אין לוק"* לידו אומר כמה פעמים אנשים שלחו משהו שאינו לוק.

**שפות, לפני שמזמינים מישהו שקורא ערבית או רוסית.** האפליקציה יוצאת עם אנגלית ועברית פעילות. ערבית ורוסית נמצאות
בבילד אבל כבויות עד שדובר שפת אם יעבור על `wwwroot/i18n/ar.json` (או `ru.json`) ועל הבלוק המתאים ב-`Services/Localizer.cs`;
אז מוסיפים `Languages__Enabled__2=ar` (או `ru`), מפעילים מחדש, ובודקים ש-`…/api/config` מציג אותה. עד אז טלפון בערבית
מקבל את האפליקציה באנגלית, ושום דבר חצי-מתורגם לא מגיע לאף אחד.

**תמונה בלי לוק לא עולה לאף אחד בדיקה.** שלוש פעמים ביום לאדם ה"אין כאן לוק" של הסטייליסט לא נספר; אחרי זה הוא נספר.
חשבון Anthropic סופר כל אחת מהן, ולכן בלם האורחים והתקרה הגלובלית נשארים במקומם.
