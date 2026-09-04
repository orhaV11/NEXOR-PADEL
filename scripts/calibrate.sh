#!/usr/bin/env bash
# Posts every photo in a folder as one throwaway user and prints the score distribution.
# Usage: scripts/calibrate.sh <folder> [intent] [language] [base-url]
set -euo pipefail

FOLDER="${1:?folder of jpg/png/webp photos}"
INTENT="${2:-Casual}"
LANGUAGE="${3:-en}"
BASE="${4:-http://localhost:5000}"

user=$(curl -sS -X POST "$BASE/api/users" -H 'Content-Type: application/json' \
  -d "{\"handle\":\"calibration\",\"confirmed16Plus\":true,\"language\":\"$LANGUAGE\"}")
user_id=$(printf '%s' "$user" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p')
if [ -z "$user_id" ]; then echo "could not create user: $user" >&2; exit 1; fi
trap 'curl -sS -o /dev/null -X DELETE "$BASE/api/users/$user_id"' EXIT

scores=()
for photo in "$FOLDER"/*; do
  case "${photo,,}" in *.jpg|*.jpeg|*.png|*.webp) ;; *) continue ;; esac
  response=$(curl -sS -w '\n%{http_code}' -F "userId=$user_id" -F "intent=$INTENT" -F "language=$LANGUAGE" -F "image=@$photo" "$BASE/api/checks")
  status=$(printf '%s' "$response" | tail -n1)
  body=$(printf '%s' "$response" | sed '$d')
  score=$(printf '%s' "$body" | sed -n 's/.*"score":\([0-9]*\).*/\1/p' | head -n1)
  headline=$(printf '%s' "$body" | sed -n 's/.*"headline":"\([^"]*\)".*/\1/p' | head -n1)
  printf '%-40s HTTP %s  score %-3s %s\n' "$(basename "$photo")" "$status" "${score:--}" "$headline"
  [ -n "$score" ] && scores+=("$score")
done

echo
echo "distribution (${#scores[@]} scored):"
for s in 1 2 3 4 5 6 7 8 9 10; do
  n=0; for v in "${scores[@]:-}"; do [ "$v" = "$s" ] && n=$((n+1)); done
  printf '%3s | %s\n' "$s" "$(printf '#%.0s' $(seq 1 $n) 2>/dev/null)"
done
