#!/usr/bin/env bash
# Back up the running OREVOSH without stopping it: a consistent single-file copy of the database (SQLite's VACUUM INTO,
# safe while the app is writing) and a copy of the photo/clip folder, made inside the container by the app's own --backup
# command, copied out to ./backups (or the folder given as $1), then removed from the container. Keeps the newest $KEEP
# (default 14) database copies and only the newest $KEEP_STORAGE (default 2) storage copies on the host: the database is
# small, a storage copy is the whole media folder. Run it from anywhere; the cron line is in DEPLOY.md.
#
# The copies hold every photo and clip people gave the app, so they are readable by root only: umask 077 for anything this
# script creates, the destination made 700, and chmod -R go-rwx once docker cp is done (docker cp keeps the container's
# modes, which are world-readable). Whatever happens, nothing stays on the data volume: the container's scratch folder is
# removed on every exit path, so a run that fails halfway leaves no copy of the media behind.
#
# That scratch folder is made fresh for this run (mktemp -d /data/backup-scratch.XXXXXXXX, inside the container, as the
# container's user) and this script removes only the one it made. It is deliberately not a folder anything else keeps
# copies in: scripts/backup.sh, the cron-able half that leaves its copies **on** the volume, defaults to
# /data/backups/nightly, and the two must never be pointed at one folder — this one empties the folder it owns on every
# exit, and the app's --keep prunes whatever it finds in the folder it is given.
set -euo pipefail
umask 077
cd "$(dirname "$0")/.."
dest="${1:-backups}"
keep="${KEEP:-14}"
keep_storage="${KEEP_STORAGE:-2}"
install -d -m 700 "$dest"

# The name is decided by the container, not by this script: two runs at once (a cron line and an impatient hand) then get
# a folder each instead of sharing one and pruning each other's copy in flight.
scratch="$(docker compose exec -T app mktemp -d /data/backup-scratch.XXXXXXXX | tail -n 1 | tr -d '\r')" || scratch=""
case "$scratch" in
  /data/backup-scratch.????????) ;;
  *)
    echo "backup failed: could not make a scratch folder on the data volume (got \"$scratch\")" >&2
    exit 1
    ;;
esac

cleanup() {
  # Only ever the folder this run made, named in full: never a folder anyone keeps backups in.
  docker compose exec -T app rm -rf "$scratch" >/dev/null 2>&1 || true
}
trap cleanup EXIT

out="$(docker compose exec -T app dotnet FitCheck.Api.dll --backup "$scratch")"
db="$(sed -n 's/^database: //p' <<<"$out")"
storage="$(sed -n 's/^storage: //p' <<<"$out")"
[ "$storage" != "none" ] || storage=""
if [ -z "$db" ]; then
  echo "backup failed: $out" >&2
  exit 1
fi

docker compose cp "app:$db" "$dest/"
if [ -n "$storage" ]; then
  docker compose cp "app:$storage" "$dest/"
fi
chmod -R go-rwx "$dest"

# Prune, only now that the new copies are complete: a run that fails never costs an old backup. The names carry a UTC
# stamp, so sorting by name is sorting by time.
prune() {
  local keep="$1"
  shift
  [ "$#" -gt 0 ] || return 0
  printf '%s\n' "$@" | sort -r | tail -n +"$((keep + 1))" | while IFS= read -r old; do
    if [ -n "$old" ]; then
      rm -rf -- "$old"
    fi
  done
}
shopt -s nullglob
prune "$keep" "$dest"/orevosh-*.db
prune "$keep_storage" "$dest"/storage-*

echo "backup written to $dest/$(basename "$db")${storage:+ and $dest/$(basename "$storage")}"
