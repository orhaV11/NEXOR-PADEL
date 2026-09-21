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

The account you sign in as spends one check per run out of its daily allowance, so an 8-run pass needs an account
allowed at least 8 checks that day (a Pro account, or a server with `Plans__ProChecksPerDay` raised). If it runs out,
the tool says so, on the run it happened on, instead of pretending.

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
- **No real-model numbers have ever been taken.** The sandbox this was written in has no route to Anthropic, so the
  harness was exercised against a local stand-in made to move its scores on purpose — enough to prove the tables, the
  arithmetic and the failure exit work, and not enough to say a single word about how steady the real stylist is. The
  first real pass is still to be run, by someone with a key.
