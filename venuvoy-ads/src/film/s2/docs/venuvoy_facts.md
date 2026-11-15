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

## Every claim an ad might make, tested against the code

Four skeptics tried to refute each claim by following the code from the screens a user can reach. Use the safe wording.

- **Venuvoy plans trips around basketball games.** TRUE. EN: "Not only football. Venuvoy also plans trips around basketball games: NBA, EuroLeague and EuroCup." HE: "לא רק כדורגל. Venuvoy מתכננת גם טיולים סביב משחקי כדורסל: NBA, יורוליג ויורוקאפ." Do not name other basketball leagues or national teams, and do not promise exact ticket prices for basketball (they are estimated ranges).
- **Venuvoy plans trips around Formula 1 race weekends.** TRUE. "Not only football: Venuvoy also plans a trip around a Formula 1 Grand Prix weekend." Hebrew: "לא רק כדורגל: גם סוף שבוע של פורמולה 1." Do not state 2027 race dates or start times as fixed (all 24 rows are provisional), do not promise ticket prices, and do not say tickets are included or sold.
- **The Venuvoy planner lets you choose from 16 spectator sports.** TRUE, WITH LIMITS. "Pick from 16 sports in the planner: football, basketball, baseball, ice hockey, American football, motorsport, tennis, rugby, golf and more." Hebrew: "16 ענפים לבחירה: כדורגל, כדורסל, בייסבול, הוקי קרח, פוטבול, מוטוספורט, טניס, רוגבי, גולף ועוד". Do not say or show events in all 16 sports, and do not feature boxing, darts or snooker (no events in the catalog); cricket, cycling, athletics and horse racing have 1 to 5 events each, so do not headline them either.
- **Venuvoy has boxing, darts and snooker events to plan a trip around.** FALSE. do not say. Do not name or show boxing, darts or snooker as something Venuvoy plans around. Sports that do have events in the catalog and may be named: football, baseball, basketball, ice hockey, American football, motorsport (incl. Formula 1), rugby, tennis, golf; horse racing, athletics, cricket and cycling have only 1-5 events each, so do not feature them.
- **Venuvoy can add a casino evening to an adults-only trip.** TRUE, WITH LIMITS. EN: "Adults-only trip? Turn on 'Nightlife and casinos' and Venuvoy can suggest one casino visit on a free evening, in 17 cities such as Monaco, Las Vegas and London. 18+ (21+ in some places), ID required." HE: "טיול של מבוגרים בלבד? מדליקים 'חיי לילה וקזינו', ו-Venuvoy יכולה להציע ביקור אחד בקזינו בערב פנוי, ב-17 ערים כמו מונקו, לאס וגאס ולונדון. מגיל 18 (בחלק מהמקומות 21), עם תעודה מזהה." Do not say gambling, betting, odds, winning or bonuses; do not say "casinos everywhere" or "casino trips"; never show it on a trip with children; do not imply Venuvoy books or partners with a casino.
- **Venuvoy shows casinos near the stadium.** TRUE, WITH LIMITS. "Want a casino evening on the trip? Switch it on, and Venuvoy can add one to a free evening, with the entry rules. Adults only, in 17 cities such as Las Vegas, Monaco, London, Madrid and Barcelona." Do NOT say 'near the stadium', 'casinos in every city' or 'nightlife' as bars and clubs. Do not show or imply betting, gambling or winning: the product itself says it does not offer or promote gambling.
- **Venuvoy offers betting or gambling.** FALSE. Do not say. Honest alternative: "Want a casino night on a free evening? Switch it on and Venuvoy adds one to the plan, as a place to visit, adults only." Never show or imply betting, odds, winnings, bonuses or a way to gamble through Venuvoy, and never show children in a casino scene. Available in 17 cities, so do not say "in every city".
- **A Venuvoy trip starts from one typed sentence.** TRUE, WITH LIMITS. "Describe your trip in one sentence and Venuvoy fills in the planner for you; check it and press Create plans." Hebrew: "כתבו את הטיול במשפט אחד, Venuvoy ממלא את האשף, ואתם לוחצים 'ליצור מסלולים'." Do not say the sentence alone produces the finished trip, and do not show a home screen where the sentence box is the first thing visible (the form is the default).
- **Venuvoy books the hotel.** FALSE. Do not say "books the hotel", "books the flight" or "buys the ticket". Honest version, EN: "Venuvoy builds the plan, shows you the official ticket links, and gives you a hotel search for your dates in each city you stay in. You book it yourself on the hotel site. Venuvoy does not sell tickets, flights or hotels." HE: "Venuvoy בונה את התוכנית, מציגה את הקישורים הרשמיים לכרטיסים ונותנת חיפוש מלונות לתאריכים שלכם בכל עיר לינה. את ההזמנה עושים בעצמכם באתר המלונות. Venuvoy לא מוכרת כרטיסים, טיסות או מלונות."
- **Venuvoy finds the flight for your trip.** FALSE. Do not say Venuvoy finds, searches or books the flight (and do not say it finds the hotel). Honest alternative: "Tell Venuvoy when you land and when you fly home. It fits the games around your flights and shows the latest time you can land and still make the match."
- **Venuvoy sells match tickets.** FALSE. Do not say. Honest alternative: "Venuvoy shows you where and when to buy: official ticket links, a comparison of ticket sites and a step-by-step ticket guide. You buy from the seller." Also do not say Venuvoy books or sells the flight or the hotel.
- **For each game, Venuvoy shows how to get a ticket, with the official seller first.** TRUE, WITH LIMITS. "For the games in your plan, Venuvoy shows how to get tickets and puts the official seller first. We never sell tickets." Hebrew: "לכל משחק בתוכנית Venuvoy מראה איך משיגים כרטיסים, וקודם כול את המוכר הרשמי. אנחנו לא מוכרים כרטיסים." Do not say "every game" or "a full step-by-step guide for free", and do not imply Venuvoy sells, holds or guarantees tickets.
- **Venuvoy builds two or three different priced plans for the same trip.** TRUE, WITH LIMITS. EN: "Up to three genuinely different plans for the same trip, each with an estimated cost (≈) for tickets and getting around. Flights and hotels not included." HE: "עד שלוש תוכניות שונות באמת לאותו טיול, כל אחת עם עלות משוערת (≈) לכרטיסים ולנסיעות ביעד. בלי טיסות ולינה." The product's own "2–3 plans" is acceptable if "estimated" sits next to it. Do not say "always three", "exact prices" or "the full price of the trip".
- **Planning a trip on Venuvoy is free.** TRUE. "Planning is free." / "התכנון חינם." Also fine: "2-3 plans at no cost." Do not say 'Venuvoy is free', 'everything is free' or 'no account needed': editing the plan, the ticket guides, exports and change tracking are one-time paid per trip, and the full site asks for a free account.
- **Venuvoy can add hikes and runs to a sports trip.** TRUE, WITH LIMITS. "In England, Scotland, Germany, the Netherlands, Austria, Italy, Spain, Greece and Slovakia, Venuvoy can add a hike, a run or a Saturday-morning parkrun to your sports trip." Shorter: "In many European cities Venuvoy adds a hike or a run between the matches." Do not say "anywhere" or "in every city"; hikes exist for 25 cities only. Do not mention cycling routes or ski days.
- **Venuvoy lists places to play sport in 520 cities.** TRUE. "Free time on your trip? Venuvoy lists places to play sport near you, in 520 cities." Hebrew: "יש זמן פנוי בטיול? Venuvoy מציג מקומות לשחק ספורט בסביבה, ב-520 ערים." May add "nearly 14,000 places" (13,975). Do not say Venuvoy books them or guarantees opening hours, and do not imply the planner inserts them automatically: the traveller picks one and adds it.
- **Venuvoy automatically adds courts and activities from Sport nearby to your plan.** FALSE. Do not say "automatically". EN: "A free afternoon? Open 'Sport nearby' and add a court, a climbing wall or a stadium tour to your plan with one tap." HE: "יש אחר צהריים פנוי? פותחים 'ספורט בסביבה' ומוסיפים לתוכנית מגרש, קיר טיפוס או סיור באצטדיון בנגיעה אחת." Also true: "On free days Venuvoy can fit in a golf round or a stadium tour by itself."
- **Venuvoy lists shooting ranges and skydiving.** TRUE, WITH LIMITS. "Free afternoon? In some cities Venuvoy shows sport to do yourself nearby, even a shooting range or indoor skydiving, and you add it to your plan." Say 'in some cities' (about 25 cities each). Skydiving from a plane exists in only 3 places, so do not feature it; show a wind tunnel if skydiving is pictured. Do not say Venuvoy books these or that they are in every destination.
- **Venuvoy can add a round of golf to your trip.** TRUE, WITH LIMITS. "Staying in London or Manchester? Venuvoy can fit a round of golf into a free day." Generic form: "In some cities Venuvoy can add a round of golf." Do not say "anywhere", do not set the golf scene in Spain, Portugal, Scotland or the USA, and do not say Venuvoy books the tee time.
- **Venuvoy can add a stadium tour to your trip.** TRUE, WITH LIMITS. "On a free day, Venuvoy can put a stadium tour in your plan." Hebrew: "ביום פנוי Venuvoy יכול לשלב בתוכנית סיור באצטדיון." If a stadium is shown or named, use one that is in the data: Old Trafford or the Etihad (the product's own example), Anfield, the Emirates, Tottenham, the Bernabéu, San Siro. Do not say Venuvoy books the tour, and do not show a tour on match day or at a ground not in the list.
- **Venuvoy plans sports trips anywhere in the world.** TRUE, WITH LIMITS. Do not say "anywhere in the world" or "any city". EN: "Sports trips in 56 countries: full coverage in England, partial coverage across much of Europe, the US and Japan, and the big marquee events elsewhere." HE: "טיולי ספורט ב-56 מדינות: כיסוי מלא באנגליה, כיסוי חלקי בחלק גדול מאירופה, בארצות הברית וביפן, ואירועי הדגל הגדולים בשאר המקומות." Short form: "Sports trips across Europe, the US and beyond."
- **Venuvoy watches your games and alerts you when one moves.** TRUE, WITH LIMITS. "With tracking on, Venuvoy keeps an eye on the games in your plan and tells you when a kick-off, a date or a venue changes, with up to three replacements." Present it as a paid add-on per trip, not as part of free planning. Do not say 'instantly', 'in real time' or 'push notification', and do not promise it for every sport.
- **Every fixture in a Venuvoy plan shows its source and the date it was checked.** TRUE. "Every match in your plan comes with its source, the date it was checked and a status." Hebrew: "לכל משחק יש מקור, תאריך בדיקה וסטטוס." Do not say "every match is verified" or "confirmed": most fixtures are marked provisional.
- **Venuvoy uses AI to plan your trip.** FALSE. do not say "AI plans your trip" or "AI-powered planner". Honest alternatives: "Venuvoy plans your trip around real fixtures, and shows where every number came from" (the home page's own line: "A sports-trip planner that shows where every number came from" / "מתכנן טיולי ספורט שמסביר מאיפה כל נתון הגיע", src/features/landing/text.ts:90, 10). If AI must be mentioned at all, only this: "type your trip in a sentence; if a word isn't recognised, an AI model helps read it."
- **Venuvoy checks whether a ticket link comes from an official seller.** TRUE. EN: "Got a ticket link? Paste it into Venuvoy's free Scam Shield. It tells you if the site is an official seller, an authorised one, a legal resale market, or one it could not verify." HE: "קיבלתם קישור לכרטיסים? מדביקים אותו במגן הכרטיסים החינמי של Venuvoy, והוא אומר אם האתר הוא מוכר רשמי, מוכר מורשה, שוק משני חוקי, או שלא הצלחנו לאמת אותו." Do not say "guarantees the ticket is real", "protects you from scams" or "catches every fake".
- **Venuvoy compares ticket prices across ticket sites.** TRUE, WITH LIMITS. "Venuvoy shows where to get tickets: the official seller first, then other ticket sites compared by buyer protection and fees." Do NOT say 'compares prices', 'finds the cheapest ticket' or 'best price'. Do not say Venuvoy sells tickets.
- **Venuvoy recommends sports bars and nightlife spots.** FALSE. Do not say. Do not show Venuvoy recommending a pub, bar or club. Honest alternative for "things to do around the match": "Sport nearby: places to play (courts, climbing, bowling, karting, mini golf, pools), stadium tours and sports museums, and sports shops, in 520 cities, added to your plan with one tap." For evenings, only the opt-in casino visit is real (see c7).
- **Venuvoy is a mobile app you download.** NOT VERIFIED. Do not say "download the app" and do not show App Store or Google Play badges unless the founder confirms a store listing. Honest wording: "Open Venuvoy in your browser, on your phone or computer. You can add it to your home screen, and your trip works offline." Hebrew: "פותחים את Venuvoy בדפדפן, בטלפון או במחשב. אפשר להוסיף למסך הבית, והטיול עובד גם בלי רשת."

Limits that matter most when writing a scene: golf rounds and planner-placed stadium tours exist only in England;
hikes and runs in nine European countries; casinos in 17 cities; only England has full coverage of fixtures; the typed
sentence fills the planner and the traveller then presses "Create plans" (the films compress this into "Done.").
