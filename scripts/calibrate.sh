#!/usr/bin/env bash
# Posts every photo in a folder as one throwaway user and prints the score distribution.
# Usage: scripts/calibrate.sh <folder> [intent] [language] [base-url]
# Needs bash 3.2+, curl, grep, tr. Runs against a server that has ANTHROPIC_API_KEY set.
set -euo pipefail

FOLDER="${1:?folder of jpg/png/webp photos}"
INTENT="${2:-Casual}"
LANGUAGE="${3:-en}"
BASE="${4:-http://localhost:5000}"

user=$(curl -sS -X POST "$BASE/api/users" -H 'Content-Type: application/json' \
  -d "{\"handle\":\"calibration\",\"confirmed16Plus\":true,\"language\":\"$LANGUAGE\"}")
user_id=$(printf '%s' "$user" | grep -o '"id":"[^"]*"' | head -n1 | cut -d'"' -f4)
if [ -z "$user_id" ]; then echo "could not create user: $user" >&2; exit 1; fi
trap 'curl -sS -o /dev/null -X DELETE "$BASE/api/users/$user_id"' EXIT

scores=""
for photo in "$FOLDER"/*; do
  case "$(printf '%s' "$photo" | tr '[:upper:]' '[:lower:]')" in *.jpg|*.jpeg|*.png|*.webp) ;; *) continue ;; esac
  response=$(curl -sS -w '\n%{http_code}' -F "userId=$user_id" -F "intent=$INTENT" -F "language=$LANGUAGE" -F "image=@$photo" "$BASE/api/checks")
  http_status=$(printf '%s' "$response" | tail -n1)
  body=$(printf '%s' "$response" | sed '$d')
  # Top-level fields come before "feedback" in the response, so the first match is the check itself.
  check_status=$(printf '%s' "$body" | grep -o '"status":"[a-z_]*"' | head -n1 | cut -d'"' -f4)
  score=""
  if [ "$check_status" = "ok" ]; then
    score=$(printf '%s' "$body" | grep -o '"score":[0-9]*' | head -n1 | cut -d: -f2)
  fi
  headline=$(printf '%s' "$body" | grep -o '"headline":"[^"]*"' | head -n1 | cut -d'"' -f4)
  error=$(printf '%s' "$body" | grep -o '"error":"[^"]*"' | head -n1 | cut -d'"' -f4)
  printf '%-36s HTTP %s  %-11s score %-3s %s\n' "$(basename "$photo")" "$http_status" "${check_status:-error}" "${score:--}" "${headline:-$error}"
  if [ -n "$score" ]; then scores="$scores $score"; fi
done

echo
total=$(printf '%s\n' $scores | grep -c . || true)
echo "distribution ($total scored):"
for s in 1 2 3 4 5 6 7 8 9 10; do
  n=$(printf '%s\n' $scores | grep -c "^$s\$" || true)
  bar=""
  if [ "$n" -gt 0 ]; then bar=$(printf '#%.0s' $(seq 1 "$n")); fi
  printf '%3s | %-20s %s\n' "$s" "$bar" "$n"
done
