#!/usr/bin/env bash
# OREVOSH post-deploy smoke: run from a laptop against the live site, after every deploy.
#
#   scripts/smoke.sh https://orevosh.app            # the eight read-only checks
#   scripts/smoke.sh https://orevosh.app --check    # plus one guest check: spends a real stylist call and the address's free look
#   scripts/smoke.sh https://orevosh.app --check photo.jpg   # the same with your own outfit photo (better than the built-in one)
#
# Needs only curl (and the POSIX tools every shell has: grep, sed, tr, wc, mktemp). Prints one OK/FAIL line per check and
# exits 1 when any check failed, so a deploy script or a cron can gate on it. It reads the same things LAUNCH.md 1.8 has
# you read by hand: /healthz, /readyz, the two landing pages, /api/config, the link-preview tags in the shell, the
# security headers and the manifest. Nothing here writes to the site unless you pass --check.
set -u

usage() {
  # the comment block above, from line 2 down to the first line that is not a comment (so nothing below it can leak in)
  sed -n '2,${/^#/!q;s/^# \{0,1\}//p;}' "$0"
  exit 2
}

ORIGIN=""
CHECK=no
PHOTO=""
while [ $# -gt 0 ]; do
  case "$1" in
    -h|--help) usage ;;
    --check) CHECK=yes; if [ $# -gt 1 ] && [ -f "$2" ]; then PHOTO="$2"; shift; fi ;;
    -*) echo "unknown option: $1" >&2; usage ;;
    *) if [ -z "$ORIGIN" ]; then ORIGIN="$1"; else echo "one origin only" >&2; usage; fi ;;
  esac
  shift
done
[ -n "$ORIGIN" ] || usage
ORIGIN="${ORIGIN%/}"
case "$ORIGIN" in http://*|https://*) ;; *) echo "the origin must start with http:// or https:// (got $ORIGIN)" >&2; usage ;; esac
command -v curl >/dev/null 2>&1 || { echo "curl is needed and was not found" >&2; exit 2; }

TMP=$(mktemp -d 2>/dev/null || mktemp -d -t orevosh-smoke)
trap 'rm -rf "$TMP"' EXIT
FAILS=0
PASSES=0
UA="orevosh-smoke/1"

ok()   { PASSES=$((PASSES + 1)); printf 'OK   %-9s %s\n' "$1" "$2"; }
fail() { FAILS=$((FAILS + 1));   printf 'FAIL %-9s %s\n' "$1" "$2"; }

# fetch <name> <path> [curl args...]: body in $TMP/<name>.body, headers in $TMP/<name>.head, status in $STATUS ("000" when nothing answered).
fetch() {
  local name="$1" path="$2"; shift 2
  STATUS=$(curl -sS -L --max-time 20 -A "$UA" -o "$TMP/$name.body" -D "$TMP/$name.head" -w '%{http_code}' "$@" "$ORIGIN$path" 2>"$TMP/$name.err") || STATUS="000"
  [ -n "$STATUS" ] || STATUS="000"
}
header() { tr -d '\r' < "$TMP/$1.head" | grep -i "^$2:" | tail -1 | sed 's/^[^:]*: *//'; }
curl_error() { tr -d '\n' < "$TMP/$1.err" | sed 's/^curl: ([0-9]*) //' | cut -c1-120; }

# 1. /healthz: the app is alive and the database answers.
fetch healthz /healthz
if [ "$STATUS" = "200" ] && [ "$(tr -d '\r\n' < "$TMP/healthz.body")" = "ok" ]; then ok healthz "ok"
elif [ "$STATUS" = "000" ]; then fail healthz "no answer from $ORIGIN ($(curl_error healthz))"
else fail healthz "http $STATUS, body: $(head -c 80 "$TMP/healthz.body" | tr -d '\r\n')"; fi

# 2. /readyz: 200 and every check "ok".
fetch readyz /readyz
if [ "$STATUS" = "200" ] && grep -q '"ok":true' "$TMP/readyz.body"; then
  NOT_OK=$(grep -oE '"[a-z0-9_]+":"[^"]*"' "$TMP/readyz.body" | grep -v ':"ok"' | tr '\n' ' ')
  if [ -z "$NOT_OK" ]; then ok readyz "$(grep -oE '"[a-z0-9_]+":"ok"' "$TMP/readyz.body" | sed 's/"//g; s/:ok//' | tr '\n' ' ' | sed 's/ $//') all ok"
  else fail readyz "not every check is ok: $NOT_OK"; fi
elif [ "$STATUS" = "503" ]; then fail readyz "503: $(grep -oE '"[a-z0-9_]+":"[^"]*"' "$TMP/readyz.body" | grep -v ':"ok"' | tr '\n' ' ')"
elif [ "$STATUS" = "000" ]; then fail readyz "no answer ($(curl_error readyz))"
else fail readyz "http $STATUS"; fi

# 3 and 4. The landing pages, English and Hebrew: HTML carrying the slogan.
landing() {
  local name="$1" path="$2" slogan="$3"
  fetch "$name" "$path"
  local ctype; ctype=$(header "$name" content-type)
  if [ "$STATUS" != "200" ]; then fail "$name" "http $STATUS for $path"
  elif ! printf '%s' "$ctype" | grep -qi 'text/html'; then fail "$name" "$path is not HTML (content-type: $ctype)"
  elif ! grep -qF -- "$slogan" "$TMP/$name.body"; then fail "$name" "$path does not carry the slogan \"$slogan\""
  else ok "$name" "$path is HTML and says \"$slogan\""; fi
}
landing landing-en /landing/ 'Check the look.'
landing landing-he /landing/index.he.html 'בודקים את הלוק.'

# 5. /api/config answers: the client's first call.
fetch config /api/config
if [ "$STATUS" = "200" ] && grep -q '"maxImageBytes"' "$TMP/config.body"; then
  ok config "guests $(grep -oE '"guestChecksPerDay":[0-9]+' "$TMP/config.body" | sed 's/.*://') a day, transcoding $(grep -oE '"transcoding":(true|false)' "$TMP/config.body" | sed 's/.*://'), email $(grep -oE '"email":(true|false)' "$TMP/config.body" | sed 's/.*://')"
elif [ "$STATUS" = "000" ]; then fail config "no answer ($(curl_error config))"
else fail config "http $STATUS"; fi

# 6. The shell carries the link-preview tags, and they point at this site, not at the placeholder.
fetch shell /
if [ "$STATUS" != "200" ]; then fail preview "the shell (/) answered http $STATUS"
else
  OG_TITLE=$(grep -o '<meta property="og:title" content="[^"]*"' "$TMP/shell.body" | head -1 | sed 's/.*content="//; s/"$//')
  OG_IMAGE=$(grep -o '<meta property="og:image" content="[^"]*"' "$TMP/shell.body" | head -1 | sed 's/.*content="//; s/"$//')
  OG_URL=$(grep -o '<meta property="og:url" content="[^"]*"' "$TMP/shell.body" | head -1 | sed 's/.*content="//; s/"$//')
  TW_CARD=$(grep -o '<meta name="twitter:card" content="[^"]*"' "$TMP/shell.body" | head -1 | sed 's/.*content="//; s/"$//')
  if [ -z "$OG_TITLE" ] || [ -z "$OG_IMAGE" ] || [ -z "$OG_URL" ] || [ -z "$TW_CARD" ]; then fail preview "og:title, og:image, og:url or twitter:card is missing from the shell"
  elif printf '%s %s' "$OG_URL" "$OG_IMAGE" | grep -q 'looks\.example\.com'; then fail preview "the tags still say looks.example.com: run node tools/brand/set-origin.js <origin> and rebuild (LAUNCH.md 1.2)"
  else ok preview "og:title \"$OG_TITLE\", og:url $OG_URL, twitter:card $TW_CARD"; fi
fi

# 7. The security headers on the shell (and HSTS when the site is https).
if [ "$STATUS" = "200" ]; then
  MISSING=""
  for h in X-Content-Type-Options X-Frame-Options Referrer-Policy Permissions-Policy; do
    [ -n "$(header shell "$h")" ] || MISSING="$MISSING $h"
  done
  case "$ORIGIN" in https://*) [ -n "$(header shell Strict-Transport-Security)" ] || MISSING="$MISSING Strict-Transport-Security" ;; esac
  if [ -n "$MISSING" ]; then fail headers "missing:$MISSING"
  else ok headers "nosniff, $(header shell X-Frame-Options), $(header shell Referrer-Policy)$(case "$ORIGIN" in https://*) printf ', HSTS %s' "$(header shell Strict-Transport-Security)";; esac)"; fi
else
  fail headers "not checked: the shell answered http $STATUS"
fi

# 8. The manifest is served with its type: what makes the app installable.
fetch manifest /manifest.webmanifest
CTYPE=$(header manifest content-type)
if [ "$STATUS" = "200" ] && printf '%s' "$CTYPE" | grep -qi 'manifest+json' && grep -q '"start_url"' "$TMP/manifest.body"; then ok manifest "$CTYPE, $(grep -oE '"name": *"[^"]*"' "$TMP/manifest.body" | head -1 | sed 's/"name": *//')"
elif [ "$STATUS" = "200" ]; then fail manifest "served, but content-type is \"$CTYPE\" or start_url is missing"
else fail manifest "http $STATUS"; fi

# 9 (opt-in). One guest check: the upload, the storage folder, the Anthropic key and the model, all at once. It spends a
# real stylist call and this address's one free look for the day, so it runs only with --check.
if [ "$CHECK" = "yes" ]; then
  if [ -z "$PHOTO" ]; then
    # A 64x64 JPEG of two flat colour blocks: enough for the door, the store and the call. The stylist will most likely
    # answer that it is not an outfit, which still proves the key and the model; pass a real photo for a real verdict.
    PHOTO="$TMP/outfit.jpg"
    printf '\377\330\377\340\000\020JFIF\000\001\001\000\000\001\000\001\000\000\377\333\000C\000\020\013\014\016\014\n\020\016\r\016\022\021\020\023\030(\032\030\026\026\0301#%%\035(:3=<9387@H\\N@DWE78PmQW_bghg>Mqypdx\\egc\377\333\000C\001\021\022\022\030\025\030/\032\032/cB8Bcccccccccccccccccccccccccccccccccccccccccccccccccc\377\300\000\021\010\000@\000@\003\001"\000\002\021\001\003\021\001\377\304\000\037\000\000\001\005\001\001\001\001\001\001\000\000\000\000\000\000\000\000\001\002\003\004\005\006\007\010\t\n\013\377\304\000\265\020\000\002\001\003\003\002\004\003\005\005\004\004\000\000\001}\001\002\003\000\004\021\005\022!1A\006\023Qa\007"q\0242\201\221\241\010#B\261\301\025R\321\360$3br\202\t\n\026\027\030\031\032%%&'"'"'()*456789:CDEFGHIJSTUVWXYZcdefghijstuvwxyz\203\204\205\206\207\210\211\212\222\223\224\225\226\227\230\231\232\242\243\244\245\246\247\250\251\252\262\263\264\265\266\267\270\271\272\302\303\304\305\306\307\310\311\312\322\323\324\325\326\327\330\331\332\341\342\343\344\345\346\347\350\351\352\361\362\363\364\365\366\367\370\371\372\377\304\000\037\001\000\003\001\001\001\001\001\001\001\001\001\000\000\000\000\000\000\001\002\003\004\005\006\007\010\t\n\013\377\304\000\265\021\000\002\001\002\004\004\003\004\007\005\004\004\000\001\002w\000\001\002\003\021\004\005!1\006\022AQ\007aq\023"2\201\010\024B\221\241\261\301\t#3R\360\025br\321\n\026$4\341%%\361\027\030\031\032&'"'"'()*56789:CDEFGHIJSTUVWXYZcdefghijstuvwxyz\202\203\204\205\206\207\210\211\212\222\223\224\225\226\227\230\231\232\242\243\244\245\246\247\250\251\252\262\263\264\265\266\267\270\271\272\302\303\304\305\306\307\310\311\312\322\323\324\325\326\327\330\331\332\342\343\344\345\346\347\350\351\352\362\363\364\365\366\367\370\371\372\377\332\000\014\003\001\000\002\021\003\021\000?\000\373\356\212(\240\017\377\331' > "$PHOTO"
  fi
  STATUS=$(curl -sS --max-time 90 -A "$UA" -o "$TMP/check.body" -D "$TMP/check.head" -w '%{http_code}' \
    -H 'X-Requested-With: Orevosh' -H 'Accept-Language: en' \
    -F intent=Casual -F language=en -F "image=@$PHOTO;type=image/jpeg" "$ORIGIN/api/checks" 2>"$TMP/check.err") || STATUS="000"
  CSTATUS=$(grep -oE '"status":"[a-z_]+"' "$TMP/check.body" | head -1 | sed 's/"status"://; s/"//g')
  case "$STATUS" in
    201)
      if [ "$CSTATUS" = "ok" ]; then ok check "the stylist answered: score $(grep -oE '"score":[0-9]+' "$TMP/check.body" | head -1 | sed 's/.*://')/10 in $(grep -oE '"latencyMs":[0-9]+' "$TMP/check.body" | sed 's/.*://') ms"
      else ok check "the stylist answered \"$CSTATUS\" (the key, the model and the storage work; pass a real outfit photo for a score)"; fi ;;
    401) fail check "guests are off on this server (Plans__GuestChecksPerDay=0): sign in to check" ;;
    429) fail check "this address's free look is spent for today (Retry-After $(header check Retry-After) s): try tomorrow, from another network, or with an account" ;;
    502) fail check "502: the stylist did not answer ($(head -c 120 "$TMP/check.body" | tr -d '\r\n')): check ANTHROPIC_API_KEY and Anthropic__Model with --doctor --live" ;;
    000) fail check "no answer within 90 s ($(curl_error check))" ;;
    *)   fail check "http $STATUS: $(head -c 120 "$TMP/check.body" | tr -d '\r\n')" ;;
  esac
fi

echo "smoke: $PASSES ok, $FAILS failed ($ORIGIN)"
[ "$FAILS" -eq 0 ]
