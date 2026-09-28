# Does the stylist say the same thing twice?

`stylist.js` sends **the same photo** to a running OREVOSH several times and shows you what came back each time. That
is the whole idea: if the same outfit gets a 5, then an 8, then a 6, the score is not a judgement, it is a mood — and
nobody can trust a number that moves when nothing did.

We cannot pin it the usual way. The model OREVOSH runs on (claude-sonnet-5) removed the settings that used to make
answers repeatable — sending one now makes the whole request fail. So steadiness has to come from the words of the
rubric, and the only way to know whether it worked is to measure it. This is the measuring stick.

## Running it

You need a **running OREVOSH** with a **real Anthropic key** (this is the live stylist, not a pretend one), and an
account on it to sign in as.

```
node tools/eval/stylist.js --photo look.jpg --occasion date --style streetwear --runs 8 \
     --base http://127.0.0.1:5080 --handle myhandle --password ...
```

- `--photo` — the photo. Repeat it (`--photo a.jpg --photo b.jpg`) and each one gets its own table plus a line in a
  summary table at the end.
- `--occasion` — `everyday`, `date`, `office`, `party`, `formal`, `sport`. Where the outfit is going.
- `--style` — `streetwear`, `old-money`, `minimal`, `classic`, or `none`. How it should read. `none` is a real answer.
- `--runs` — how many times each photo is sent. 8 is the default and a good number: enough to see a wobble, not so many
  that it costs a fortune.
- `--note` — the free line a person would type ("dinner then a gig").
- `--language` — the language the feedback is written in (`en`, `he`, `ar`, `ru`).
- `--max-spread` — how far the score may move before the run counts as a failure. Default 2.
- `--json` — the raw numbers instead of the tables, for pasting into a spreadsheet.
- `--report <file>` — the same JSON written to a file while the tables still print, so one paid run gives you both.

## From Windows, against orevosh.com

The founder's machine is Windows with the PowerShell that ships with it (5.1; `pwsh` is a separate install and is not
needed). `tools\eval\calibrate.ps1` is the whole pass in one command, run from anywhere (a relative `-Photos` is read
from the folder you are in, and the window is left there):

```powershell
tools\eval\calibrate.ps1 -Photos C:\looks\calibration -Occasion date -Language he
```

- `-Photos` — a folder; every `.jpg` in it is sent, in name order. `-Occasion`, `-Style`, `-Language`, `-Runs` (8),
  `-MaxSpread` (2), `-Note` and `-Delay` are the flags above with the same words; `-Base` is `https://orevosh.com`
  unless you say otherwise.
- **The account.** `-Handle` or `OREVOSH_EVAL_HANDLE` names it; the password comes from `OREVOSH_EVAL_PASSWORD` or,
  when that is not set, from a masked prompt (a password typed on the command line is kept in PowerShell's history
  file, so `-Password` is there but not recommended). Either way the password reaches node in
  `OREVOSH_EVAL_PASSWORD`, never as an argument, and the window's own value is put back afterwards. That account
  spends one check per run out of its daily allowance, so an 8-run pass needs a Pro account: `fly ssh console -u app
  -C "dotnet /app/FitCheck.Api.dll --pro <handle> 1"` on the server, and `Limits__SpendPerDayUsd` is the ceiling the
  app stops itself at either way.
- **What it costs** is printed before anything is sent — `3 photos x 8 runs = 24 model calls, about 1-2 US cents
  each` and the 24 checks the account needs today — and the run goes on: a script that stops to ask looks frozen.
  The one exception is a pass longer than a Pro day (30 checks at the server's defaults, so three photos at 8 runs):
  it stops before asking for the password, because the runs past the 30th would be refused and the verdict read on
  fewer runs than asked. Split the folder over days, or pass `-Force` on a server whose `Plans__ProChecksPerDay` and
  `Limits__ChecksPerDay` were raised.
- `-DryRun` prints the exact command with the password masked and exits 0 without signing in. It is what CI runs.
- **The report.** Every line node prints is echoed as it comes and filed as
  `tools/eval/reports/<date>-<time>-<occasion>-<style>-<language>.txt` (the tables, the masked command, the base, the
  count) with the rows as `.json` beside it, UTF-8 without a byte-order mark, so the Hebrew tips survive. The folder is
  in `.gitignore`: a report carries the handle and the tips, and the verdict line is what gets pasted.
- **The last line is Hebrew**, green or red, and the exit code is stylist.js's own:
  - `בתוך 2: הציון של כל תמונה נשאר בטווח המותר.` — every photo stayed inside the allowed spread (exit 0);
  - `רחב מדי: look-1.jpg spread 3 (מותר: 2).` — one or more photos moved further than allowed (exit 1);
  - `אין פסק דין: 1 תמונות לא חזרו עם ציון. לא נמדד כלום.` — a photo never came back with a score (exit 1);
  - `הריצה לא יצאה לפועל: …` — the run could not be made: no node, node older than 18, an empty folder, a sign-in
    that failed, a server that did not answer (exit 1 before node ran, exit 2 from stylist.js);
  - then `הדו"ח: <path>` — where the report went.

If the Hebrew shows as boxes, the console window's font has no Hebrew glyphs; the report file has the same lines
and is the durable record. `node` must be on the PATH (the LTS from nodejs.org); the script checks the version first,
because a Node older than 18 fails inside stylist.js on `fetch` with a message that names nothing.

The account you sign in as spends one check per run out of its daily allowance, so a pass needs an account allowed
photos × runs checks that day: a Pro account (30 a day, so three photos at 8 runs), or a server with
`Plans__ProChecksPerDay` and `Limits__ChecksPerDay` raised. If it runs out, the tool says so, on the run it happened on,
instead of pretending.

## This spends real money

Every run is one real call to the model, with your key, on your bill. **8 runs = 8 calls.** Two photos at 8 runs = 16
calls. The arithmetic is exactly that: `photos x runs = calls`. A check is one image and a page of instructions in, a
short structured answer out — on the current pricing that lands around **one to two US cents a call**, so a default
8-run pass is a few cents and a 5-photo, 8-run sweep is somewhere near a dollar. Nothing here is free, and nothing here
is cached: sending the same photo twice costs twice.

Watch the day's spend on the numbers page (the app's own meter) rather than guessing, and keep `Limits__SpendPerDayUsd`
set while you do this — the app stops itself at that ceiling.

## What you are looking at

```
  run  score  fit  col  acc   match  tip     headline
    1      7    7    8    4      82  change  Clean lines, one loud shoe
    2      7    7    8    4      80  change  Clean lines, one loud shoe
    ...

  score      min 6   max 8   mean 7.1   sd 0.60   spread 2
  breakdown  2 different fit/colour/accessories reading(s); it moved on 3 of 8 run(s)
  intent     match moved 8 point(s) across the runs
  tip        1 keep(s), 7 change(s)
```

- **min / max** — the lowest and highest score the same photo got.
- **spread** — max minus min. The single number that matters. A spread of 1 is one notch of disagreement; 3 means the
  stylist does not really know.
- **mean** — the average. Useful for comparing photos, useless for judging steadiness on its own.
- **sd** (standard deviation) — how far the runs sit from that average, on average. Small = clustered, large = scattered.
  0 means every run gave the identical score.
- **breakdown** — how many different fit/colour/accessories readings came back, and how often they differed from the
  first run. The three little numbers are supposed to explain the big one; if they wander while the score holds, the
  explanation is decoration.
- **intent match** — how far the "reads as" percentage travelled.
- **tip** — how many runs answered with a **keep** ("this works, change nothing") instead of a change. A keep is meant to
  be rare and honest. Several keeps on a mediocre look is as much a problem as none at all on a good one.
- **the tips, side by side** — read them. This is the part a number cannot do for you: if run 3 says swap the shoes and
  run 5 says the shoes are the best thing in the photo, no spread number will tell you, and a person reading both would
  stop believing the app.

## What good looks like

| | |
|---|---|
| **Good** | spread 0–1, sd under 0.5, the same headline idea every time, tips that name the same piece |
| **Acceptable** | spread 2, sd under 1.0, tips that disagree on wording but not on which piece is weakest |
| **Not shippable** | spread 3 or more, tips that contradict each other, keeps appearing on a look that scores 5 |

The tool exits with a failure code when any photo's spread is over `--max-spread` (2 by default), so it can sit in a
script and complain by itself.

## Honest limits

- It measures **the app's answer**, rubric and all, not the model on its own. That is on purpose: the rubric is the part
  we can fix.
- Runs happen one after another with no pause unless you pass `--delay`. Nothing about the order is randomised.
- **No real-model numbers have been taken in this repository.** The sandbox this was written in has no route to
  Anthropic, so the harness was exercised against a local stand-in made to move its scores on purpose — enough to
  prove the tables, the arithmetic and the failure exit work, and not enough to say a single word about how steady the
  real stylist is. The first real pass is one command from the founder's machine — the Windows section above,
  `tools\eval\calibrate.ps1 -Photos <folder> -Occasion date -Language he` against `https://orevosh.com` — and its
  Hebrew verdict line is what to paste back.
