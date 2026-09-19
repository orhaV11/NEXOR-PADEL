#!/usr/bin/env bash
# Put a backup back, or move a pilot database from a laptop onto the server. Stops the app, replaces the database (and
# the photo/clip folder when a storage copy is given too), hands the files to the container's user, starts the app.
# The app checks the file on start: one made by these migrations is used as is, one made by an earlier round (dotnet
# run before migrations existed) is copied to orevosh.db.bak-<stamp> and upgraded in place, rows kept.
#
#   tools/restore.sh backups/orevosh-20260905033000.db [backups/storage-20260905033000]
#   tools/restore.sh ~/laptop/orevosh.db ~/laptop/storage
set -euo pipefail
cd "$(dirname "$0")/.."
db="${1:?usage: tools/restore.sh <orevosh.db> [<storage folder>]}"
storage="${2:-}"
if [ ! -f "$db" ]; then
  echo "no such file: $db" >&2
  exit 1
fi
if [ -n "$storage" ] && [ ! -d "$storage" ]; then
  echo "no such folder: $storage" >&2
  exit 1
fi

what="the live database"
[ -z "$storage" ] || what="the live database and every photo and clip"
read -r -p "This replaces $what with $db${storage:+ and $storage}. Continue? [y/N] " answer
[ "${answer:-n}" = "y" ] || exit 1

mounts=(-v "$(realpath "$db"):/restore/orevosh.db:ro")
if [ -n "$storage" ]; then
  mounts+=(-v "$(realpath "$storage"):/restore/storage:ro")
fi

docker compose stop app
# A one-off container on the same data volume, as root so it can write there and chown to the app user afterwards.
docker compose run --rm --no-deps --user root "${mounts[@]}" --entrypoint sh app -c '
  set -e
  rm -f /data/orevosh.db /data/orevosh.db-wal /data/orevosh.db-shm
  cp /restore/orevosh.db /data/orevosh.db
  if [ -d /restore/storage ]; then
    rm -rf /data/storage
    cp -r /restore/storage /data/storage
  fi
  chown -R app:app /data
'
docker compose start app
echo "restored $db${storage:+ and $storage}; the app is starting (docker compose logs -f app)"
