#!/usr/bin/env bash
# Back up the running OREVOSH without stopping it: a consistent copy of the database (SQLite's VACUUM INTO, safe while
# the app is writing) and a copy of the photo/clip folder, made inside the container by the app's own --backup command,
# copied out to ./backups (or the folder given as $1), then removed from the container. Keeps the newest $KEEP (default
# 14) of each on the host. Run it from anywhere; the cron line is in DEPLOY.md.
set -euo pipefail
cd "$(dirname "$0")/.."
dest="${1:-backups}"
keep="${KEEP:-14}"
mkdir -p "$dest"

out="$(docker compose exec -T app dotnet FitCheck.Api.dll --backup /data/backups)"
db="$(sed -n 's/^database: //p' <<<"$out")"
storage="$(sed -n 's/^storage: //p' <<<"$out")"
if [ -z "$db" ]; then
  echo "backup failed: $out" >&2
  exit 1
fi

docker compose cp "app:$db" "$dest/"
docker compose exec -T app rm -f "$db"
if [ -n "$storage" ] && [ "$storage" != "none" ]; then
  docker compose cp "app:$storage" "$dest/"
  docker compose exec -T app rm -rf "$storage"
fi

# Prune: newest first, drop everything past $keep.
ls -1dt "$dest"/orevosh-*.db 2>/dev/null | tail -n +$((keep + 1)) | xargs -r rm -f
ls -1dt "$dest"/storage-* 2>/dev/null | tail -n +$((keep + 1)) | xargs -r rm -rf

echo "backup written to $dest/$(basename "$db")${storage:+ and $dest/$(basename "$storage")}"
