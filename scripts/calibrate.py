#!/usr/bin/env python3
"""Calibration run: real photos, the real model, one throwaway user.

Posts every photo in a folder for each intent and language you ask for, then prints a table, the score
distribution, latency, and a scan of the feedback for words that would break the "clothes, never the person"
rule. Writes a JSON report so two prompt versions can be compared side by side.

    python3 scripts/calibrate.py ./photos --intent Casual --intent Date --language en --language he

Needs only Python 3.8+ and a running server with ANTHROPIC_API_KEY set. The throwaway user and everything it
created are deleted at the end, so the pilot metrics stay clean.
"""
import argparse
import json
import mimetypes
import os
import re
import sys
import time
import urllib.error
import urllib.request
import uuid
from collections import Counter, defaultdict

PHOTO_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp"}
SCORES = range(1, 11)

# Words that point at the person rather than the clothes. Matched as whole words, case-insensitive, and meant
# for a human to read: garment vocabulary that collides with them (leather is "עור", skinny jeans, slim fit,
# high heels, hot pink, old money, boyfriend jeans) is deliberately left out to keep the list honest.
FORBIDDEN = {
    "en": [
        "body", "bodies", "figure", "curvy", "curves", "slimming", "fat", "chubby", "plus-size", "weight", "height",
        "petite", "skin", "complexion", "face", "facial", "pretty", "beautiful", "handsome", "attractive", "sexy",
        "gorgeous", "ugly", "age", "young", "girl", "man", "woman", "feminine", "masculine", "flattering", "flatters",
    ],
    "he": [
        "גוף", "הגוף", "גופך", "רזה", "רזים", "שמן", "שמנה", "משקל", "גובה", "פנים", "מושך", "מושכת", "סקסי", "סקסית",
        "גיל", "צעיר", "צעירה", "מבוגר", "מבוגרת", "ילדה", "ילד", "גבר", "אישה", "נשי", "נשית", "גברי", "גברית",
        "מחמיא", "מחמיאה",
    ],
}


# One cookie jar for the run: the account created below signs in through it, like the browser does.
OPENER = urllib.request.build_opener(urllib.request.HTTPCookieProcessor())


def request(method, url, body=None, headers=None, timeout=90):
    # Every non-GET call to the API needs this header (the CSRF guard); the cookie carries the session.
    all_headers = {"X-Requested-With": "Orevosh", **(headers or {})}
    req = urllib.request.Request(url, data=body, method=method, headers=all_headers)
    try:
        with OPENER.open(req, timeout=timeout) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()


def multipart(fields, file_field, file_name, file_bytes):
    boundary = "----orevosh" + uuid.uuid4().hex
    parts = []
    for name, value in fields.items():
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n".encode("utf-8"))
    content_type = mimetypes.guess_type(file_name)[0] or "application/octet-stream"
    parts.append(
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"{file_field}\"; filename=\"{file_name}\"\r\n"
        f"Content-Type: {content_type}\r\n\r\n".encode("utf-8") + file_bytes + b"\r\n"
    )
    parts.append(f"--{boundary}--\r\n".encode("utf-8"))
    return b"".join(parts), f"multipart/form-data; boundary={boundary}"


def scan_forbidden(feedback, language):
    """Returns the forbidden words found anywhere in the user-facing text of one feedback object."""
    texts = [feedback.get("headline", ""), feedback.get("vibe", ""), feedback.get("oneTip", ""), feedback.get("message", "") or ""]
    texts += feedback.get("working", []) or []
    for item in feedback.get("items", []) or []:
        texts += [item.get("name", ""), item.get("note", "")]
    blob = " ".join(t for t in texts if t)
    hits = []
    for word in FORBIDDEN.get(language, []) + (FORBIDDEN["en"] if language != "en" else []):
        pattern = r"(?<![\w֐-׿])" + re.escape(word) + r"(?![\w֐-׿])"
        if re.search(pattern, blob, flags=re.IGNORECASE):
            hits.append(word)
    return hits


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("folder", help="folder with jpg/png/webp photos")
    parser.add_argument("--intent", action="append", help="repeatable; default Casual", default=None)
    parser.add_argument("--language", action="append", help="repeatable; default en", default=None)
    parser.add_argument("--base", default="http://localhost:5000")
    parser.add_argument("--report", default=None, help="where to write the JSON report (default: calibration-<time>.json)")
    args = parser.parse_args()

    intents = args.intent or ["Casual"]
    languages = args.language or ["en"]
    photos = sorted(
        os.path.join(args.folder, name) for name in os.listdir(args.folder)
        if os.path.splitext(name)[1].lower() in PHOTO_EXTENSIONS
    )
    if not photos:
        sys.exit(f"no photos in {args.folder}")

    # A throwaway account per run (the handle carries a random suffix); it is deleted at the end with its photos.
    handle = "calib_" + uuid.uuid4().hex[:8]
    status, body = request("POST", f"{args.base}/api/auth/signup",
                           json.dumps({"handle": handle, "password": uuid.uuid4().hex, "confirmed16Plus": True,
                                       "language": languages[0]}).encode(),
                           {"Content-Type": "application/json"})
    if status != 201:
        sys.exit(f"could not create user: {status} {body[:200]!r}")

    rows = []
    try:
        for language in languages:
            for intent in intents:
                for photo in photos:
                    with open(photo, "rb") as handle:
                        data = handle.read()
                    payload, content_type = multipart(
                        {"intent": intent, "language": language}, "image", os.path.basename(photo), data)
                    started = time.time()
                    status, body = request("POST", f"{args.base}/api/checks", payload, {"Content-Type": content_type})
                    elapsed = int((time.time() - started) * 1000)
                    row = {"photo": os.path.basename(photo), "intent": intent, "language": language, "http": status,
                           "elapsedMs": elapsed, "status": None, "score": None, "intentMatch": None, "headline": "",
                           "oneTip": "", "items": 0, "fit": None, "color": None, "accessories": None,
                           "accessoriesVerdict": None, "forbidden": [], "error": None}
                    try:
                        parsed = json.loads(body)
                    except ValueError:
                        parsed = {}
                    if status == 201:
                        feedback = parsed.get("feedback") or {}
                        # Rubric v2: the three sub-scores and the accessories verdict; absent on a v1 server.
                        breakdown = feedback.get("breakdown") or {}
                        accessories = feedback.get("accessories") or {}
                        row.update(status=parsed.get("status"), score=parsed.get("score"),
                                   intentMatch=feedback.get("intentMatch"), headline=feedback.get("headline", ""),
                                   oneTip=feedback.get("oneTip", ""), items=len(feedback.get("items") or []),
                                   fit=breakdown.get("fit"), color=breakdown.get("color"),
                                   accessories=breakdown.get("accessories"), accessoriesVerdict=accessories.get("verdict"),
                                   forbidden=scan_forbidden(feedback, language), feedback=feedback,
                                   latencyMs=parsed.get("latencyMs"))
                    else:
                        row["error"] = parsed.get("error") or body[:120].decode("utf-8", "replace")
                    rows.append(row)
                    flag = " !!" if row["forbidden"] else ""
                    sub = "/".join(str(row[k] or "-") for k in ("fit", "color", "accessories"))
                    print(f"{row['photo'][:28]:<28} {intent:<10} {language:<3} HTTP {status}  {str(row['status'] or row['error'])[:10]:<10} "
                          f"score {str(row['score'] or '-'):<3} f/c/a {sub:<8} acc {str(row['accessoriesVerdict'] or '-'):<8} "
                          f"match {str(row['intentMatch'] or '-'):<4} {elapsed:>6} ms  {row['headline'][:60]}{flag}")
    finally:
        request("DELETE", f"{args.base}/api/users/me")

    ok = [r for r in rows if r["status"] == "ok"]
    print()
    print(f"{len(ok)} scored of {len(rows)} checks; "
          f"{sum(1 for r in rows if r['status'] == 'not_outfit')} not_outfit, "
          f"{sum(1 for r in rows if r['status'] == 'rejected')} rejected, "
          f"{sum(1 for r in rows if r['http'] != 201)} failed")
    if ok:
        latencies = sorted(r["elapsedMs"] for r in ok)
        print(f"latency: median {latencies[len(latencies) // 2]} ms, max {latencies[-1]} ms")
        by_group = defaultdict(Counter)
        for r in ok:
            by_group[(r["intent"], r["language"])][r["score"]] += 1
        for (intent, language), counts in sorted(by_group.items()):
            print(f"\n{intent} / {language}")
            for score in SCORES:
                n = counts.get(score, 0)
                print(f"{score:>3} | {'#' * n:<20} {n}")
        total = Counter(r["score"] for r in ok)
        clustered = sum(total.get(s, 0) for s in (7, 8))
        if len(ok) >= 5 and clustered / len(ok) > 0.7:
            print("\nWARNING: more than 70% of scores are 7 or 8. Tighten the calibration text and bump PromptVersion.")
        headlines = [r["headline"] for r in ok]
        if len(set(headlines)) < len(headlines):
            print("WARNING: repeated headlines; the model is being generic.")
        # Rubric v2: the sub-scores' means and how the accessories verdicts fall. A v1 server has neither.
        with_breakdown = [r for r in ok if r["fit"] is not None]
        if with_breakdown:
            means = " ".join(f"{k} {sum(r[k] for r in with_breakdown) / len(with_breakdown):.1f}" for k in ("fit", "color", "accessories"))
            print(f"\nsub-scores (mean over {len(with_breakdown)}): {means}")
            verdicts = Counter(r["accessoriesVerdict"] or "-" for r in with_breakdown)
            print("accessories: " + ", ".join(f"{v} {verdicts[v]}" for v in ("adds", "neutral", "missing", "clashes", "-") if verdicts.get(v)))
            if len(with_breakdown) >= 5 and verdicts.get("neutral", 0) / len(with_breakdown) > 0.7:
                print("WARNING: more than 70% of accessories verdicts are neutral. The rubric is not committing; tighten the ACCESSORIES text.")
    flagged = [r for r in rows if r["forbidden"]]
    if flagged:
        print("\nRULE 1 HITS (judge clothes, never the person). Read these before inviting anyone:")
        for r in flagged:
            print(f"  {r['photo']} {r['intent']}/{r['language']}: {', '.join(r['forbidden'])}")
    else:
        print("\nrule 1 scan: no body, face, age or gender words found in any feedback.")

    report = args.report or f"calibration-{time.strftime('%Y%m%d-%H%M%S')}.json"
    with open(report, "w", encoding="utf-8") as handle:
        json.dump({"base": args.base, "intents": intents, "languages": languages, "rows": rows}, handle, ensure_ascii=False, indent=2)
    print(f"\nreport written to {report}")


if __name__ == "__main__":
    main()
