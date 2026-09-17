#!/usr/bin/env bash
# A backup you can put in cron and forget: the app's own --backup writes a consistent copy of the database (SQLite's
# VACUUM INTO, safe while people are using it) and a copy of the photo/clip folder into a folder on the data volume, and
# --keep removes the older copies there once the new one is complete. One command, no TTY, no prompt, no interaction.
#
#   scripts/backup.sh                      # /data/backups inside the container, newest 14 kept
#   scripts/backup.sh /data/backups 30     # somewhere else, newest 30 kept
#   KEEP=7 scripts/backup.sh               # the same through the environment (for a crontab line)
#   scripts/backup.sh --local /srv/backups # no Docker: this box runs the app itself (systemd, a bare publish)
#
# Cron, as root on the server (the app writes the copies, so nothing here needs a password):
#   15 3 * * * /opt/orevosh/scripts/backup.sh >> /var/log/orevosh-backup.log 2>&1
# Exit code 0 only when a copy was really written; anything else is worth a mail from cron. Two runs never overlap: the
# second one exits 0 immediately rather than fighting the first for the disk.
#
# WHICH SCRIPT. This one keeps the copies **on the server**, inside the data volume, pruned by the app: one line in cron,
# nothing to mount, and a restore is a docker compose cp away. It does not protect you from losing the machine.
# tools/backup.sh is the other half: it runs the same command and then copies the result **off** the volume onto the host
# (./backups), prunes there, and tightens the modes, which is what you want before you rsync or scp the copies somewhere
# else entirely. Run this one nightly and that one before a risky change, or run only that one — never mix their folders,
# since both prune by the same names.
#
# The copies hold every photo and clip people gave the app. umask 077 for anything created here; the app writes them as
# the container's user; keep whatever you move them to just as private (DEPLOY.md, "Backups").
set -euo pipefail
umask 077

local_mode=0
if [ "${1:-}" = "--local" ]; then
  local_mode=1
  shift
fi

dir="${1:-${BACKUP_DIR:-/data/backups}}"
keep="${2:-${KEEP:-14}}"
service="${COMPOSE_SERVICE:-app}"
lock="${BACKUP_LOCK:-/tmp/orevosh-backup.lock}"

case "$keep" in
  ''|*[!0-9]*) echo "backup: KEEP must be a whole number of copies to keep, got \"$keep\"" >&2; exit 2 ;;
esac
[ "$keep" -ge 1 ] || { echo "backup: KEEP must be 1 or more" >&2; exit 2; }

# One run at a time, on a file descriptor this shell holds until it exits: a nightly run that is still copying when the
# next hour's fires must not be joined by a second one. Without flock (a slim image, a BSD) the run simply goes ahead:
# a second VACUUM INTO is safe, it only costs disk, and refusing to back up because a lock tool is missing is worse.
if command -v flock >/dev/null 2>&1 && exec 9>"$lock" 2>/dev/null; then
  if ! flock -n 9; then
    echo "backup: another run holds $lock; skipping this one"
    exit 0
  fi
fi

run() {
  if [ "$local_mode" = "1" ]; then
    # The published app on this box. Set OREVOSH_APP_DIR when it is not /opt/orevosh/app.
    ( cd "${OREVOSH_APP_DIR:-/opt/orevosh/app}" && dotnet FitCheck.Api.dll --backup "$dir" --keep "$keep" )
  else
    # The container, from the folder that holds docker-compose.yml (this script's parent by default).
    ( cd "${OREVOSH_COMPOSE_DIR:-$(dirname "$0")/..}" && docker compose exec -T "$service" dotnet FitCheck.Api.dll --backup "$dir" --keep "$keep" )
  fi
}

started="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
if ! out="$(run 2>&1)"; then
  echo "[$started] backup FAILED"
  echo "$out"
  exit 1
fi

# --backup prints "database: <path>", "storage: <path|none>" and one "removed: <path>" per copy retention took.
db="$(printf '%s\n' "$out" | sed -n 's/^database: //p')"
storage="$(printf '%s\n' "$out" | sed -n 's/^storage: //p')"
removed="$(printf '%s\n' "$out" | sed -n 's/^removed: //p' | wc -l | tr -d ' ')"
if [ -z "$db" ]; then
  echo "[$started] backup FAILED: no database copy in the output"
  echo "$out"
  exit 1
fi

[ "$storage" != "none" ] || storage="(no photo folder yet)"
echo "[$started] backup ok: $db, $storage; $removed older copy/copies removed, newest $keep kept"
