# OREVOSH — marketing

The positioning, the slogan, the voice, the first ten posts, a four-week launch plan for one narrow community, the
numbers to watch, and what not to do. The files every post needs are in `brand-kit/` (`brand-kit/README.md`); the
store texts are in `STORE.md`; the landing page is `/landing/` (`/landing/index.he.html` in Hebrew).

## Positioning

OREVOSH is a social app for looks: a stylist in your pocket that scores an outfit out of 10 for where it is going, in
ten seconds, with the breakdown and the one tip, and a community where the look gets fire, the brands you tag notice,
and the score travels to your story. Since Round 10 a look also says what is in it: the pieces are tagged by the
person who wears them (the brand, the model, a store link, a dot on the photo; the stylist suggests a brand only when
it can see the mark), and the week has a scoreboard, the flames board, five top tens that close Saturday night into a
hall of flame. It is not a marketplace, not a body app, not a filter: a tagged piece links out to the store and the
app sells nothing, it judges clothes, never the person, the check is private until you post, and orange means one
thing only, that a look caught fire. For a person it answers "does this work for tonight?" before they leave the
house; for a brand it is where the people who actually wear it post it, tag it and shop it.

## The slogan

**Check the look.** / **בודקים את הלוק.** with the tagline *A stylist in your pocket, and a community that lights it
up.* / *סטייליסט בכיס, וקהילה שמדליקה.* It is the app's verb (CHECK sits under the mark in the dock), it works as an
imperative and as a description, and the Hebrew is the same idiom people use.

Runners-up, kept so the owner can swap (change `COPY` at the top of `tools/brand/render-kit.js`, re-run it, and the
kit, the OG card and the landing screens follow; the i18n keys are `app.slogan` and `app.tagline`):

1. **Every look, checked.** / **כל לוק עובר בדיקה** — calmer, more product than gesture.
2. **Wear it. Check it. Light it up.** / **לובשים. בודקים. מדליקים.** — the three beats of the app; longer, better for
   video than for a header.

## Tone of voice

From `DESIGN.md`: young, chic, lit. Short sentences. No exclamation marks, ever. Verbs in the present, second person
in English, the plural-impersonal in Hebrew (בודקים, מפרסמים, מדליקים) like the app itself. Say fire, never like.
Say look, never outfit pic. Numbers are the hook (7/10, the one tip, 10 seconds). Never a word about body, face,
skin, age or gender, in any post, caption or reply, including the compliments. No hype words (amazing, insane,
must-have), no emoji strings, no hashtags in the first line. When in doubt, cut the sentence in half.

## The first ten posts

Formats: story (1080×1920, from `brand-kit/stories/`), reel/TikTok (vertical video, 7–15 s, filmed on a phone), and
carousel (square or 4:5 stills). The story templates are `story-1-check-the-look`, `story-2-the-one-tip` and
`story-3-brands`, each in English and Hebrew; the caption is the post's, the CTA line on the template stays.

| # | format | what | template | caption |
|---|---|---|---|---|
| 1 | reel | Screen recording: pick Date, add the photo, the ring draws, 7/10 lands, the one tip. Real time, no cuts, 10 s. | — | Check the look. Ten seconds, one tip. Link in bio. |
| 2 | story | The slogan over the check screen. | story-1 | (none: the template carries it) |
| 3 | carousel | 4 slides: the same look checked as Date, Office, Streetwear, Party. Four scores, one photo. | — | Same clothes, four places. Where is yours going tonight. |
| 4 | story | The result screen and the one tip, quoted. | story-2 | (none) |
| 5 | reel | The story card being saved and posted to an Instagram story, 8 s. | — | Your score, on your story. Checked on OREVOSH. |
| 6 | carousel | 3 slides: "What works", "The one tip", "Add one accessory", each on the stage in Outfit 800, the app's rings as the graphic. | — | The stylist reads three things. Fit. Color. Accessories. |
| 7 | story | A look with a brand tag and "Featured by". | story-3 | (none) |
| 8 | reel | Double-tap to fire in the feed, the burst, the count going up, 6 s. Sound: none, or a single snap. | — | Fire is the only reaction. Light it up. |
| 9 | reel | A brand's challenge: the brief, the prize, three entries, the vote. 12 s. | — | @brand opened a challenge. Post with #tag, the community votes. |
| 10 | carousel | 3 slides of house rules from the guidelines: clothes never the person; private until you post; report and it's gone. | — | The rules are short. Clothes, never you. |

Two more once the first week has closed, in the same voice:

| # | format | what | template | caption |
|---|---|---|---|---|
| 11 | reel | Tap a dot on a look, the item sheet opens: brand, model, "Shop at". Then "Looks like Nike? Confirm" on the post sheet. 8 s. | — | Tap the pants. The brand, the model, the store. You tag it, not the app. |
| 12 | carousel | The hall of flame: last week's top three looks with their medals, the #1 person. | — | The week closed Saturday night. Three looks, one hall. Yours next week. |

Rhythm: one a day for the first ten days, stories on top as they come from users (repost every story card that tags
the account, with the fire count). Every reel ends on the wordmark on the stage for one second; every caption's last
line is the CTA (Link in bio / לינק בביו). Hebrew versions of every post go out the same day on the same account.
From the second week on, Sunday's post is the hall: the top three looks of the week that closed, with their medals.

## A four-week launch, one narrow community

Pick one community small enough to see itself in the feed and close enough to reach in person: one campus, one
fashion or design school, one city's streetwear scene. The plan below says "the campus"; swap the word.

**Week 0 (prep).** Production up (`DEPLOY.md`), the domain in the OG tags and the landing page, a calibration run on
30 real photos (`scripts/calibrate.py`) so the scores spread and nothing in the feedback breaks rule 1. Ten seed looks
posted by the team from real outfits, every one with its pieces tagged (the brand and the model at least; a store link
only where there is a real one), three brand accounts (small labels the campus wears, contacted with a two-line
message and the brand story template, verified with `--verify` once you have spoken to them), one challenge opened
with a real prize. The board's week set to the campus's zone (`Board__TimeZone`, `DEPLOY.md`) before the first Sunday,
and no sponsor on the first week: the first hall should be earned, not presented. A moderator on duty (`--admin`).

**Week 1 (twenty people).** Invite by hand: twenty people who post outfits already, one message each, no group blast.
Ask for one check and one post; watch what they ask about and fix the copy. Post 1–5 from the list. Read
`/api/metrics/pilot` daily: `returnRate`, checks per user, share rate.

**Week 2 (two hundred).** The twenty invite five each. The first board week closes Saturday night: post the hall on
Sunday (post 12) and tell the three people on it in person. The first challenge closes and the winner gets the prize
on camera (a reel). A story template with the campus's own hashtag. Post 6–10. Reach out to the campus's student
media with one line and three screenshots. Fix the top three complaints before growing further. If one of the three
brands wants to present a week with a prize, week 3 is the first week to do it (`Board__Sponsor__*`, by hand).

**Week 3 (the campus).** Posters with the mark and the slogan (`lockup-2400x1200-dark.png` prints well at A3) where
people wait: the coffee line, the studio doors, the bus stop. A second challenge by a second brand. The story cards
of the week's top looks reposted daily. Ask the fifty most active for a ten-minute call each.

**Week 4 (hold).** No new growth push; watch whether week-1 and week-2 people are still checking (D7 and D14) and
whether posts still get fire without the team's own reactions. Decide with the numbers: fix retention before opening
a second community, or repeat weeks 1–3 in the next one.

## The numbers to watch

All from `/api/metrics/pilot` (moderator session) unless said otherwise; write them down weekly, not daily.

| metric | definition | pilot target | says |
|---|---|---|---|
| **D7 retention** | people whose second OK check happened within 7 days of their first ÷ people with at least one OK check (`returnRate`) | 35% by week 4 | the stylist is worth coming back to |
| **Checks per user** | OK checks ÷ people with at least one, per week | 3 a week | the habit; below 1.5 the app is a toy |
| **Share rate** | story cards saved or shared ÷ OK checks (the client's share sheet count; add a counter if missing) | 15% | the score is worth showing; this is the growth loop |
| Post rate | posts ÷ OK checks | 30% | people trust the score in public |
| Fire per post | fire ÷ posts in 7 days | 5 | the feed is alive without the team |
| Brand pull | posts that mention a brand ÷ posts | 25% | the brand side has a reason to be here |
| Tag rate | pieces a person tagged (`itemsTagged`) ÷ posts | 1 per post | people say what they wear without being asked |
| Store pull | store-link taps (`itemOuts`) ÷ pieces with a link | 0.5 a week | a tagged piece is worth a tap; the number a brand will ask for |
| Board pull | board reads (`boardViews`) ÷ people active in 7 days | 2 a week | the week has a rhythm; below 1 the board is decoration |
| Rule-1 incidents | reports with reason `person` + calibration scan hits | 0 | the promise holds |

## What not to do

- Do not buy followers, run giveaways for follows, or seed the feed with stock photos: the feed must be the campus,
  and the first hundred people can tell.
- Do not post body, face or transformation content, "rate my body" formats, or body before/after: it breaks rule 1
  and the ad platforms' policies both. (The app's own "After the tip" strip compares two outfits' scores, never a
  body; if it appears in a post, it is two looks side by side.)
- Do not show a real person's check without asking, even a friend's, even at 9/10. Story cards are the person's to post.
- Do not promise brand partnerships that do not exist; a brand account carries the verified check only after the owner
  ran `--verify` for it, and every other brand account is self-declared.
- Do not use exclamation marks, hype adjectives or emoji strings; do not use orange for anything but fire.
- Do not open a second community before D7 retention holds in the first one.
- Do not push Pro in the first four weeks; the habit comes first, the price after.
- Do not buy, trade or ask for fires for the board, and do not let a brand do it: only fires from people with a check
  count, three per person on one author's looks a week, and a moderator pulls a gamed look off the board. The
  stylist's picks board answers to nobody's fires at all. A week bought is a week the hall is worth nothing.
- Do not tag a brand on a piece you are not sure of, in a seed look or a post: the stylist only suggests a brand it can
  see, and a wrong brand on someone's photo is the one mistake this app cannot afford.
- Do not list an affiliate programme you have not joined, and do not hide the commission line under store links; the
  programmes' terms and consumer law both expect it, and the app shows it under every store link on purpose.
- Do not rely on the self-declared date of birth for anything that goes beyond the pilot (README, "Known limitations").
