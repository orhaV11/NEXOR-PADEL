# Putting OREVOSH on a server

This is the walkthrough for running the app for real people on one small rented server (a 5 EUR/month VPS is
enough for a pilot), with your own domain, HTTPS, backups and updates that keep everyone's data. It assumes you
can open a terminal and copy commands; it does not assume you have done this before.

To run the app on your own computer instead (Windows, Mac, Linux), see [Run it in 5 minutes](README.md#run-it-in-5-minutes)
in the README: that needs only the .NET SDK and a tunnel, no Docker, no domain.

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
`Database /data/orevosh.db is new: creating the schema from the migrations.` Then open `https://looks.example.com`
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

- **Disk.** Clips are up to 40 MB each and a storage backup is the whole media folder, two of them kept (step 9).
  `df -h /` weekly; `docker system prune -f` removes old build layers. When the disk is the problem, the answer is
  object storage (below).
- **Health.** `https://looks.example.com/healthz` returns `ok`; anything else, or no answer, is worth a look. A free
  uptime checker (UptimeRobot, Better Stack) can ping it every few minutes and email you. Docker also checks it
  itself: `docker compose ps` shows `(healthy)` or `(unhealthy)`.
- **Logs.** `docker compose logs --since 1h app` for the app (every 5xx is logged with the path), `docker compose logs
  caddy` for certificates and traffic. Logs are rotated by Docker.
- **The model bill.** `Limits__ChecksPerDay` and `Limits__ChecksPerDayGlobal` cap it; the spend limit in the
  Anthropic console is the backstop. `https://looks.example.com/api/metrics/pilot` shows how much the pilot is used
  (it is unauthenticated, see the README's limitations; do not hand out the link).
- **Memory.** A 1 GB server runs the app (about 150 MB) and Caddy comfortably; `docker stats` shows both.
- **Updates to the server itself.** `apt-get update && apt-get upgrade -y` monthly, `reboot` when it asks.

## 12. What the app does for security, and what it does not yet

In place: HTTPS with automatic renewal; HttpOnly, Secure, SameSite=Strict session cookies; a CSRF header on every
write; passwords hashed with ASP.NET Core's hasher; rate limits on signup, login and checks; photos and clips never
served by path; uploads checked by their bytes, not their declared type; moderation as a flag on the account row, set
only at start from `Admin:Handles` and by the `--admin` command, never by anything a request carries; push
subscriptions only to public push-service names (a literal address, `localhost` or a single-label name is refused, so
the app cannot be pointed at its own network) and at most 10 per account; the app container runs as a non-root user
with nothing published except through Caddy; and on every response `Strict-Transport-Security: max-age=31536000`
(over https, for this host only: no `includeSubDomains`, so nothing else under your domain is forced onto HTTPS by
this app), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`
and a `Permissions-Policy` that keeps camera and microphone to the app itself.

Still missing before a public launch, in rough order of importance:

1. **Password reset.** Accounts have no email address, so a forgotten password means a new account. Needs an email
   provider (Postmark, Resend, SES) and a reset flow.
2. **Age assurance.** The 16+ checkbox is self-declared. Integrate the Apple and Google age-signal APIs or a provider
   and gate signup on the result.
3. **Server-side transcoding for clips.** Phones upload what they recorded (up to 40 MB, 30 s): iPhones record H.264
   MP4, Chrome records H.264 MP4 when the device can and WebM otherwise, and a WebM clip from an Android phone does
   not play on older iPhones. Nothing re-encodes it, so playback depends on the viewer's browser supporting the
   sender's codec and the files are larger than they need to be. ffmpeg in a worker, or a video service, fixes both;
   this is the launch item.
4. **Object storage.** Photos and clips sit on the server's disk behind `IImageStore`. An S3-compatible bucket
   (Hetzner, Backblaze, R2) makes the disk stop being the limit and the backups a bucket policy.
5. **A Content-Security-Policy header.** Not set yet: the client uses Google Fonts and inline styles, which need
   nonces or hashes before a strict policy can go in without breaking the app.
6. **Metrics behind a password** (`/api/metrics/pilot`), **brand verification**, and **one process only**: the
   checks-per-day reservation lives in memory, so run one `app` container. Multiple instances need a shared store.
7. **The rate limiters trust `X-Forwarded-For`**, which is right behind Caddy on the private compose network; never
   publish port 8080 on the host.
