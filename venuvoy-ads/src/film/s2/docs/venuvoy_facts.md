<!-- Read this before writing any Venuvoy ad. Made on 15 Nov 2026 after the founder said: "we are beyond just football:
sports trips in general, casinos, sports activities, and so much more; learn what really stands behind Venuvoy".
Seven readers went through the product repository (orhaV11/VENUVOY, main at a927bc3), the founder's build chat and the
two design pages; one editor merged their reports; four skeptics then tried to refute every claim an ad might make.
Raw reports: venuvoy_facts_raw.json (same folder). Paths in this file are inside the product repository. -->

# What Venuvoy really is: fact sheet for the ads (15 Nov 2026)

LIVE means in the interface on the main branch (a927bc3, 11 Nov 2026); the public site was not checked, so demo or full mode is UNCLEAR. Paths are under /home/user/venuvoy.

## What Venuvoy is

Venuvoy is a sports-trip planner: a web app, Hebrew by default and English at /en. You say where and when you travel, which sports and clubs you love and what you can spend. It builds two or three different, priced, day-by-day plans around real published fixtures, with a source, a check date and a status on every item. A paid option then tracks the fixtures and alerts you to changes. It sells only its own planning and tracking. Its words: "Venuvoy plans sports trips around real fixtures." / "Venuvoy מתכננת טיולי ספורט סביב משחקים אמיתיים." (README.md:3-12); hero "A sports trip you can trust." / "טיול ספורט שאפשר לסמוך עליו." (src/features/landing/text.ts:11, 91).

For a twelve-year-old: tell Venuvoy where you are going and which sports you like, and it draws up a few trip plans around real games.

## What it covers

Catalog (src/data/regions/*.json): 11,711 events, 13 sports with events, 88 competitions, 705 teams, 786 venues, 56 countries, 46 regions. Only England is "Full coverage"; 17 regions are partial and 28 are "Marquee events only".

| Vertical | What the user gets | Status |
|---|---|---|
| Football | 5,834 events, about 40 competitions, 40 countries | LIVE |
| US and European leagues | MLB 2,428; NBA, EuroLeague, EuroCup 1,669; NHL and Slovak hockey 1,373; NFL 193 | LIVE |
| Motorsport, rugby, tennis, golf to watch | 104 (F1, MotoGP, Formula E, IndyCar, WEC); 44; 37; 20 | LIVE |
| Thin or empty sports | horse racing 5, athletics 2, cricket 1, cycling 1; boxing, darts, snooker are planner tiles with 0 events | PARTIAL |
| Casinos | "Nightlife and casinos" / "חיי לילה וקזינו" switch; 18 casinos in 17 cities; at most one per trip, on a free evening. Sports bars: PLANNED, 0 places | LIVE, opt-in, adults only |
| Active days | 51 hikes, 13 city runs, 110 parkruns in 10 regions, placed by the planner. Rides, ski days: PLANNED | LIVE |
| Sport nearby | 15,961 places in 520 cities: 13,975 to play, 152 tours and museums, 1,834 shops; includes 30 shooting ranges, 41 ziplines, 25 indoor skydiving | LIVE, added by hand |
| Golf rounds, stadium tours | 19 rounds, 11 indoor golf, 17 tours placed by the planner, England only | PARTIAL |
| Tickets | "Get tickets" guide per game, official seller first; 44 ticket sites listed; seat maps for 45 venues; Scam Shield link check | LIVE; live prices UNCLEAR |
| Hotels, car hire, tours, flights | search links to Booking.com and GetYourGuide. Flights: only the user's own landing and departure times, no search | LIVE as links; flights PARTIAL |

Evidence: src/features/wizard/types.ts:16-33; src/engine/casino.ts:1-48; src/data/regions/active/*.json; public/data/spots/v1/index.json; src/engine/spots.ts:116-117, 183-184.

## What a user does, step by step

1. Starts from the home form (country or city, dates, sport chips; button "See what's on" / "מה יש בתאריכים שלי"), from one typed sentence in Hebrew or English ("Tell us in one sentence" / "ספרו לנו במשפט אחד"), or from "Build a trip around this match".
2. The sentence only fills the planner: "Fill in the planner" / "מלאו לי את האשף" (src/features/intake/text.ts:5-14, 49-60). Casinos, hikes and runs are switches there.
3. Presses "Create plans" / "ליצור מסלולים" and gets two or three versions: "Big Matches", "Balanced", "Easy Pace".
4. Opens the trip page: Plan, Tickets, Prepare, On the trip (תוכנית · כרטיסים · הכנות · בדרך).
5. Adds "Sport nearby" / "ספורט בסביבה" places and own items by hand.
6. Buys tickets, flights and the hotel elsewhere, through the links; may pay for tracking alerts.

## What it does not do

- "It does not sell tickets, flights or hotels and is not a party to any purchase you make elsewhere." (src/features/legal/content.ts:67). Nothing is booked inside Venuvoy.
- No flight search or price. The budget is "Excluding flights and hotels" / "בלי טיסות ולינה" (src/features/wizard/text.ts:207, 555-556).
- Casinos: "Listed only as a place to visit. Venuvoy does not offer or promote gambling." / "מוצג כמקום לבקר בו בלבד. Venuvoy לא מציעה ולא מקדמת הימורים." (src/features/plan/text.ts:337, 818-819). Never with children, no betting content, no partner link. The sentence box does not understand "casino".
- No food, sightseeing or concerts, except items the user adds. No promise that tickets are available, no "where to watch", no adverts, and no "not affiliated with clubs" sentence. Legal pages are drafts.
- The planner is fixed code, not AI; a model only helps read a hard sentence, in the full version.

## Honest verbs

Plan, build, find, see what's on, compare, check, track, alert, "get tickets" (a guide to the official seller). Not honest for Venuvoy itself: book, buy, sell, reserve, "AI plans your trip", "anywhere in the world".

## Money

"Planning is free. Pay once per trip to edit, execute and watch it." / "התכנון חינם. משלמים פעם אחת לטיול כדי לערוך, לבצע ולעקוב." No subscription. Plan unlock ₪49, $15, €14 or £12; Tracking ₪99, $29, €27 or £23; Bundle ₪129, $39, €36 or £31 (src/features/pricing/priceBook.ts:25-29). Some partner links may pay commission; the order never depends on it. Whether real payment is switched on is UNCLEAR.

## Who it is for and how the founder talks about it

His product-defining messages in the build chat could not be reached. From the ads chat: "אנחנו מעבר לרק כדורגל, אנחנו טיולי ספורט באופן כללי, קזינואים, פעילויות ספורט, וכל כך הרבה יותר"; "אני רוצה להתחיל עם קהל בינלאומי קודם, שהכל יהיה באנגלית בינתיים"; "מה שמשנה זה הקהילה"; Timo and Luna should be "בין לאומי הומוריסטי ונחמד, מדבר בגובה העיניים". Code records the casino rule as a "founder decision": places to visit, practical facts only (src/engine/casino.ts:2-3).

## Brand look

"Members' Club" direction (src/styles/globals.css:5-10, 79-101): cream #f4eee1, ink #26221c, ivory card #fbf8f1, racing green #1e4d38, header band #16392b, brass #b8955a (#c9a96a on green), oxblood #6f1d29. Fonts: Playfair Display (Latin headings), Frank Ruhl Libre (Hebrew headings), Assistant (text). Wordmark: VENUVOY in uppercase Playfair with wide spacing, cream on green, beside a round crest with a serif "V" in a double brass ring (src/components/layout/Monogram.tsx:3-26). In running text always "Venuvoy", in Latin letters. Caution: the app icons and share image still show an older navy #0f172a and mint #34d399 mark.

## What is new compared with "a football trip from one sentence"

- Sixteen sports in the planner, thirteen with events; football is half.
- Things travellers do themselves: hikes, runs, parkrun, golf rounds, about 16,000 places to play.
- An adults-only casino evening, as a place to visit; stadium tours, sports museums, club stores.
- Two or three plans instead of one, with sources, check dates and change alerts.
- Corrections: the sentence fills the planner; no flight; the hotel is a search link; the ticket is a guide.

## What this means for the ads (decisions taken on 15 Nov 2026)

- Say "sports trip", never only "football trip". The sign-off is now **"one sentence. your sports trip, planned."** and
  under the name one small line of sports that really have events: "football, basketball, racing, tennis, golf and more".
- What comes back after Luna's sentence (the ticks) may only be things the planner really places: the match, a golf
  round, a hike or a run, a stadium tour, "day by day". **Never "flight" and never "hotel"**: Venuvoy has no flight
  search and the hotel is only a search link. "match ticket" is replaced by "the match" (Venuvoy shows the official way
  to a ticket; it does not supply one).
- Venuvoy inside the film is drawn in its own look: caption classes `vvb chip` (racing-green plate, the round V crest,
  VENUVOY in cream Playfair capitals, brass keyline), `vvb type` (ivory card, brass inner rule, Assistant text) and
  `vvb tick` in `src/film/overlay.html`. The older `vv` classes (cream pill, violet letters) are not the brand.
- Verbs: plan, build, find, compare, check, track. Never book, buy, sell, reserve, "AI plans", "anywhere in the world".
- Casinos: the founder names them as part of Venuvoy, and the product's own rule (recorded as his decision) is: a place
  to visit on a free evening, adults only, never with children, no betting content, "does not offer or promote
  gambling". Two cartoon mascots on a casino evening may be read as gambling aimed at the young, and ad platforms
  restrict gambling content. Until the founder decides, casinos are not shown in the mascot films.
- It is a web app that can be added to the home screen; do not say "download the app" (no store app was found).
- Films made before this sheet (the four episodes of season 1 and "Meet Timo & Luna") still say "the whole football
  trip" and tick "flight" and "hotel". All of that text is drawn by the edit, so it can be corrected by re-rendering,
  with no new filming. Waiting for the founder's yes.
