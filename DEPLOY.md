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
account recovery, the checks that run on every push, and the go-live checklist at the end.

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
configuration table goes in the same way (`fly secrets set Limits__ChecksPerDay=10`), and so do the push keys (step 7)
and the email settings ("Email for account recovery", below). Every `fly secrets set` restarts the machine with the new
values; several in one command is one restart.

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
7, `--backup` in step 9), as the `app` user so the files it writes belong to the app. `Admin__Handles__0=<handle>` as a
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
the links in mails carry the domain.

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
- **The model bill:** the spend limit in the Anthropic console is the backstop; `Limits__ChecksPerDay` and
  `Limits__ChecksPerDayGlobal` are the caps.
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
| `Email__PublicOrigin` | `https://looks.example.com`: the origin the links in mails carry. Empty means the address the request came in on, which is right behind Caddy or on Fly |
| `Email__UseStartTls` | `true` by default, for port 587. Leave it |

Mail is on when `Email__Host` and `Email__From` are both set. Three providers that work with exactly these lines:

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
provider's answer (a refused sender, a wrong password).

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
| `Email__Host`, `Email__Port`, `Email__User`, `Email__Password`, `Email__From`, `Email__PublicOrigin` | Account recovery by mail. Leave them out until you have an SMTP provider; "Email for account recovery" above has the exact lines for Resend, Postmark and Gmail. |

Any setting from the README's configuration table can be added to `.env` in the same shape, for example
`Limits__ChecksPerDay=10` (a double underscore stands for the colon). `.env` is git-ignored and stays on the
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

The app has four maintenance commands. None starts the server; all run from `/opt/orevosh`:

| Command | What it does |
|---|---|
| `docker compose run --rm --no-deps app dotnet FitCheck.Api.dll --vapid` | Prints a VAPID key pair for push (step 8). Needs no database, so it works before the first start |
| `docker compose exec app dotnet FitCheck.Api.dll --backup /data/backups` | A consistent copy of the database and the media folder into that folder on the volume (step 9; `tools/backup.sh` wraps it and brings the copies out) |
| `docker compose exec app dotnet FitCheck.Api.dll --admin <handle>` | Makes an existing account a moderator |
| `docker compose exec app dotnet FitCheck.Api.dll --unadmin <handle>` | Takes that away |

`docker compose exec` needs the app running. On a laptop the same commands are `dotnet run -- --admin <handle>` and so
on (the README, "Maintenance commands").

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
  table, column and index without dropping a row, and records the migrations as applied so the next update takes
  the normal path (`predates migrations: copied it to ... before upgrading` followed by `upgraded: ...`). The copy
  stays on the volume; delete it once you are happy (`docker compose exec app rm /data/orevosh.db.bak-...`).

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
- **The model bill.** `Limits__ChecksPerDay` and `Limits__ChecksPerDayGlobal` cap it; the spend limit in the
  Anthropic console is the backstop. `https://looks.example.com/api/metrics/pilot` shows how much the pilot is used
  (open it signed in as a moderator; anyone else gets 403).
- **Memory.** A 1 GB server runs the app (about 150 MB) and Caddy comfortably; `docker stats` shows both.
- **Updates to the server itself.** `apt-get update && apt-get upgrade -y` monthly, `reboot` when it asks.

## 12. What the app does for security, and what it does not yet

In place: HTTPS with automatic renewal; HttpOnly, Secure, SameSite=Strict session cookies; a CSRF header on every
write; passwords hashed with ASP.NET Core's hasher; rate limits on signup, login, checks, and per account on comments
and reports; photos and clips never served by path; uploads checked by their bytes, not their declared type; moderation as a flag on the account row, set
only at start from `Admin:Handles` and by the `--admin` command, never by anything a request carries; push
subscriptions only to public push-service names (a literal address, `localhost` or a single-label name is refused, so
the app cannot be pointed at its own network) and at most 10 per account; the app container runs as a non-root user
with nothing published except through Caddy; and on every response `Strict-Transport-Security: max-age=31536000`
(over https, for this host only: no `includeSubDomains`, so nothing else under your domain is forced onto HTTPS by
this app), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`
and a `Permissions-Policy` that keeps camera and microphone to the app itself.

Built, and waiting on a setting from you: account recovery works once `Email__*` points at a provider ("Email for
account recovery"); clip transcoding runs once ffmpeg is in the image (the checklist below); the pilot metrics answer
only to a moderator's session.

Still missing before a public launch, in rough order of importance:

1. **Age assurance.** The 16+ checkbox is self-declared. Integrate the Apple and Google age-signal APIs or a provider
   and gate signup on the result.
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
5. **Brand verification**, and **one process only**: the checks-per-day reservation and the rate limiters' windows
   live in memory, so run one `app` container (one machine on Fly). Multiple instances need a shared store.
6. **The rate limiters trust `X-Forwarded-For`**, which is right behind Caddy on the private compose network and
   behind Fly's edge; never publish port 8080 on a host.

## Go-live checklist

Before the address leaves the team, in this order:

1. **A domain and HTTPS.** `fly certs add`, or steps 2 and 6 of the server path. `https://…/healthz` answers `ok`,
   the padlock is there on a phone, and the app installs to the home screen.
2. **The Anthropic key with a spend limit.** Set a monthly limit in the console; `Limits__ChecksPerDay` (20) and
   `Limits__ChecksPerDayGlobal` (1000) cap the volume from the app's side. A thousand checks a day is a real bill: do
   the arithmetic for your model and your pilot's size before raising either.
3. **The first moderator**, signed up and then promoted with `--admin` (Fly step 6, server step 7). Two is better than
   one: someone has to look at the queue every day.
4. **VAPID keys** set once (`--vapid`) and never regenerated.
5. **Email** pointed at a real provider and tested with your own address. Without it, a forgotten password means a
   new account.
6. **Backups running and copied off the box.** On a server: the nightly cron of step 9 and a weekly copy elsewhere.
   On Fly: the daily snapshots are on, plus a weekly `--backup` and `fly sftp get` of your own. Restore one once,
   before you need to.
7. **The guidelines page reviewed** (in the app, linked from signup): it says what gets reported and what happens to
   a report, what deletion removes, and that the photos are the person's own. Change anything you would not stand behind.
8. **Age is self-declared, and the limits say so.** The checkbox is not age assurance; the README's "Known
   limitations" is the honest list. Read it, decide who you invite, and plan the age-signal integration before a public
   launch.
9. **Watch the disk.** Clips are up to 40 MB each and a media backup is the whole folder: `df -h` weekly on a server,
   `fly ssh console -C "df -h /data"` on Fly, and grow the volume before it fills. A full disk stops uploads and, worse,
   writes.
10. **Transcoding needs ffmpeg in the image and CPU to spare.** With `ffmpeg` on the machine the app re-encodes clips to
    H.264 MP4 in the background (`Storage__Transcode`, on by default; `Storage__FfmpegPath` when it is not on the PATH),
    so an Android WebM plays on iPhones. Check `https://…/api/config`: `"transcoding": true` means it is running;
    `false` means the image has no ffmpeg (add `ffmpeg` to the `apt-get install` line of the Dockerfile's runtime
    stage and redeploy) and clips play only where the sender's codec does. A 30-second clip takes on the order of a
    minute of a shared CPU; on Fly, `fly scale vm shared-cpu-2x` if the app gets sluggish while a clip converts.
11. **Rate limits that fit the launch.** Signups per address (50 an hour), logins (30 per quarter hour), comments (30
    an hour per account) and reports (20 an hour per account) are pilot numbers; a launch party on one Wi-Fi needs
    `Limits__SignupsPerHourPerIp` raised for the evening, and the per-account ones (`Limits__CommentsPerHour`,
    `Limits__ReportsPerHour`) are meant to stay where nobody meets them by hand.
