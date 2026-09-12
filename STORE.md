# OREVOSH — store listings

The texts and facts for the App Store and Google Play listings, in English and Hebrew, and what to prepare before
submitting. The native shells are described in `mobile/README.md`; the screenshots, feature graphic and icons are
in `brand-kit/` (`brand-kit/README.md` says which file goes where). `looks.example.com` stands for the production
origin everywhere (`DEPLOY.md`).

## Names

| field | limit | English | Hebrew |
|---|---|---|---|
| App name | 30 | `OREVOSH` | `OREVOSH` (the name is Latin in both stores; the wordmark is the logo) |
| Subtitle (App Store) / short description (Play, 80) | 30 / 80 | `A stylist in your pocket` (24) | `סטייליסט בכיס` (13) |
| Promotional text (App Store, editable without a release) | 170 | `Pick where the outfit is going, add a photo, and get a score out of 10 with the one tip. Post it, get fire, tag the brands you wear.` (131) | `בוחרים לאן הלוק הולך, מוסיפים תמונה, ומקבלים ציון מתוך 10 עם הטיפ האחד. מפרסמים, מקבלים אש, מתייגים את המותגים שאתם לובשים.` |
| Slogan (for the feature graphic, the first screenshot, ads) | — | `Check the look.` | `בודקים את הלוק.` |

## Description (App Store and Play, 4000 characters max)

### English

OREVOSH is a social app for looks.

Pick where the outfit is going: a date, the office, streetwear, old money, minimal, a party, sport. Add a photo, or film a short clip in the app and pick the frame. Ten seconds later a stylist scores the look out of 10 for that intent, breaks it into fit, color and accessories, lists what you are wearing, what works, the one tip, and the one accessory that would finish it. The check is private. Posting is a separate choice.

Post it and it joins a feed where people react with fire, comment, save and follow. Clips play in the feed. A story card carries the score to Instagram and TikTok. Tag the brands you wear with @ and add #tags: brands feature the community looks they love, open challenges with a prize, and tag products on their own looks. Browsing needs no account.

The stylist judges clothes, never the person. Nothing about body, face, skin, age or gender, ever. Photos are never reachable by a link; a deleted look makes its photo private again; deleting the account deletes everything, the same minute.

English and Hebrew, and the stylist writes in your language.

Free gives you a few checks a day. OREVOSH Pro gives you 30 checks a day, "Which one?" comparisons of two looks, and your insights over time.

For people 16 and over.

(1,171 characters)

### Hebrew

OREVOSH היא אפליקציה חברתית ללוקים.

בוחרים לאן הלוק הולך: דייט, משרד, סטריט, אולד מאני, מינימל, מסיבה, ספורט. מוסיפים תמונה, או מצלמים קליפ קצר בתוך האפליקציה ובוחרים פריים. עשר שניות אחר כך סטייליסט נותן ללוק ציון מתוך 10 ביחס לאותה כוונה, מפרק אותו לגזרה, צבע ואקססוריז, מונה מה אתם לובשים, מה עובד, את הטיפ האחד ואת האקססורי האחד שיסגור את הלוק. הבדיקה פרטית. הפרסום הוא החלטה נפרדת.

מפרסמים, והלוק נכנס לפיד שבו אנשים מגיבים באש, כותבים תגובות, שומרים ועוקבים. קליפים מתנגנים בפיד. כרטיס סטורי לוקח את הציון לאינסטגרם ולטיקטוק. מתייגים את המותגים שאתם לובשים עם @ ומוסיפים #תגיות: מותגים מציגים את הלוקים מהקהילה שהם אוהבים, פותחים אתגרים עם פרס ומתייגים מוצרים על הלוקים שלהם. כדי לגלוש לא צריך חשבון.

הסטייליסט שופט בגדים, אף פעם לא את האדם. שום מילה על גוף, פנים, עור, גיל או מגדר, אף פעם. תמונות לא נגישות בלינק; לוק שנמחק מחזיר את התמונה לפרטית; מחיקת החשבון מוחקת הכול, באותה דקה.

עברית ואנגלית, והסטייליסט כותב בשפה שלכם.

בחינם יש כמה בדיקות ביום. OREVOSH Pro נותן 30 בדיקות ביום, השוואות "איזה מהם?" בין שני לוקים, ואת התובנות שלכם לאורך זמן.

לגילאי 16 ומעלה.

## Keywords, categories, rating

- **App Store keywords** (100 characters, comma-separated, no spaces, the name and the category words are free):
  `outfit,style,stylist,fashion,ootd,look,fit,wardrobe,brands,streetwear,date,office,clothes,feedback` (99).
  Hebrew locale: `אאוטפיט,לוק,סטייל,סטייליסט,אופנה,בגדים,מותגים,סטריט,דייט,ארון`.
- **Categories:** App Store primary Lifestyle, secondary Social Networking. Google Play: Lifestyle (tag "Fashion").
- **Age rating.** The app's own rule is 16 and over, declared at signup and repeated in the guidelines. Answer the
  rating questionnaires as they are: user-generated content that is moderated, user interaction, no gambling, no
  violence, no unrestricted web access, and the "Mature/Suggestive" questions as "none" (the stylist refuses nudity and
  deletes the photo). The App Store's current tiers are 4+, 9+, 13+, 16+ and 18+: pick **16+** so the store agrees with
  the app. On Play the IARC questionnaire produces the ratings per region (expect Teen / PEGI 12 or 16); set the
  target audience to 16 and over in the "Target audience and content" section, which also keeps the listing out of the
  Designed for Families programme. Age is self-declared in the app (README, "Known limitations"): say so if asked, and
  plan the platform age-signal APIs before any push beyond the pilot.
- **Made for kids:** no. **Contains ads:** no. **In-app purchases:** yes, if Pro is offered inside the wrapped app (see
  "Payments" below).

## Privacy nutrition labels (App Store) and Data safety (Play)

What the app collects, linked to the account unless said otherwise, and why. Nothing is used for tracking or
advertising, nothing is shared with data brokers, and there is no third-party analytics SDK.

| data | collected? | why | notes |
|---|---|---|---|
| Photos and videos (the outfit photo, the clip) | yes, linked | app functionality: the check, and the post if the person posts it | The photo goes to the model provider (Anthropic) for the check; nothing else leaves the server. A rejected photo is deleted at once. |
| User content (captions, comments, tags, mentions, product links, bio) | yes, linked | app functionality | Public when posted. |
| Email address | optional, linked | account recovery (password reset link), nothing else | Never shown to others, never used for marketing. |
| Date of birth / age | yes, linked | the 16+ rule | The current build asks for a 16+ self-declaration checkbox at signup; when the signup form asks for a birth date instead, declare "Date of birth" and say it is used only for the age rule and never shown. Declare whichever the build you submit actually asks for. |
| Name, handle, avatar | yes, linked | app functionality, public profile | The display name is optional. |
| User ID | yes, linked | app functionality | An internal GUID. |
| Purchase history | only with Pro | app functionality | Stripe on the web; StoreKit / Play Billing in the wrapped app (below). Card details never reach the app. |
| Device ID, precise location, contacts, health, financial info, browsing history, search history | no | — | Search queries are not stored. |
| Crash data, performance data | no | — | Server logs keep the request path, the status and the client address for rate limiting (`X-Forwarded-For`), 30 days by default on the VPS setup. |
| Push notification token | yes, if the person turns push on | app functionality | Web Push subscription endpoint; deleted when push is turned off or the push service reports it gone. |

Also declare: **account creation is optional for browsing** and required for checking and posting; **account deletion
is in the app** (Settings → Delete account, one step, everything gone); **data can be deleted on request** at the
support address.

## URLs

| field | value |
|---|---|
| Marketing URL | `https://looks.example.com/landing/` (Hebrew: `https://looks.example.com/landing/index.he.html`) |
| Support URL | `https://looks.example.com/landing/#support` or a mailto: `hello@looks.example.com`. The stores want a page or an address a person answers within a few days. |
| Privacy policy URL | `https://looks.example.com/#/privacy` — required by both stores. |
| Terms (EULA) | `https://looks.example.com/#/terms` (Apple uses its standard EULA unless you paste your own). |
| Community guidelines | `https://looks.example.com/#/guidelines` — reviewers look for them in a UGC app. |

## Screenshots

Ten files per language in `brand-kit/store/` (`iphone-6.7-0N-{en,he}.png`, 1284×2778, and `android-0N-{en,he}.png`,
1080×1920), the feature graphic in `brand-kit/covers/play-feature-1024x500.png`, the icon in
`src/FitCheck.Api/wwwroot/icons/icon-512.png` (Play wants 512×512; App Store Connect takes the 1024 mark from
`brand-kit/logos/mark-1024-dark.png`). The captions, in order:

| # | English | Hebrew | screen |
|---|---|---|---|
| 1 | Check the look in 10 seconds | בודקים את הלוק ב-10 שניות | the check screen |
| 2 | Fit, color, accessories | גזרה, צבע, אקססוריז | the result |
| 3 | Post it, light it up | מפרסמים, מדליקים | a look in the feed |
| 4 | Brands feature the looks they love | מותגים מציגים את הלוקים שהם אוהבים | a tagged, featured look |
| 5 | Film it in the app | מצלמים בתוך האפליקציה | the camera, recording |

**Before uploading, replace the screens with real captures** (`brand-kit/README.md`, "Before the store listings"):
the files in the repository show the browser test's synthetic outfit and Chromium's fake camera, and the Hebrew set
borrows the English check and camera screens. Apple rejects screenshots that do not show the app as shipped.

## Payments

Pro is bought on the web through Stripe. Inside a native app, Apple (guideline 3.1.1) and Google (Play Billing
policy) require their own billing for digital subscriptions and forbid links or buttons that lead to another way to
pay. Before submitting the wrapped app, do one of:

1. **Hide the Pro purchase in the wrapped app** and keep it on the web (the app may show the Pro state and the
   benefits, but no "Go Pro" button, no price, no link). Simplest; Apple accepts "reader"-style apps that do not
   sell, as long as nothing points to the web purchase.
2. **Add StoreKit and Play Billing** through Capacitor plugins and have the server accept a store receipt as a Pro
   entitlement next to Stripe. More work; needed if Pro is to be sold in the app.

The Stripe checkout must not be reachable from the wrapped app, or the review fails.

## What to prepare for review

- **A test account for the reviewer** (App Review Information → Sign-in required): a handle and password on the
  production server with a few looks already posted, Pro switched on by hand (`pro.manual` is the current build's
  path), and a second account that follows it so the Following feed is not empty. Reviewers do check whether the
  camera and the check work: leave the daily cap at its default so the account is not blocked mid-review.
- **Notes for the reviewer**, in the "Notes" box (English):
  "OREVOSH lets people get a stylist's read of an outfit photo and share the look with a community. The stylist is
  an AI model that judges only the clothes: it is instructed never to comment on body, face, skin, age or gender, and
  it refuses photos with nudity, sexual content or an apparent minor (the photo is deleted immediately). Checks are
  private until the person posts. Posting, reacting and commenting need an account (16+, declared at signup); browsing
  does not. User-generated content: every look and comment can be reported from its menu; three reports from different
  people hide it from everyone but its author; moderators review a queue and can hide, delete and suspend. Account
  deletion is in Settings and removes everything. Test account: @<handle> / <password>."
- **The UGC checklist Apple applies (guideline 1.2):** filtering of objectionable content (the model refuses nudity and
  non-outfit photos; comments are reported and hidden), a way to report (yes), **a way to block users (not in the app
  today: add "Block @handle" to the look's and the profile's menu before submitting, or expect a rejection)**, and
  published contact information (the support address on the listing). Sign in with Apple is not required because the
  app offers no third-party sign-in.
- **Camera and photo permission strings** for iOS (`mobile/README.md`): "OREVOSH uses the camera to photograph or
  film your outfit for a check." and "OREVOSH saves your story card to your photos."
- **Content rights:** the app displays user photos only; brand accounts are self-declared for the pilot, and the
  listing should not claim official brand partnerships that do not exist.
- **Export compliance (App Store):** the app uses only standard HTTPS; answer "no" to proprietary encryption.
- **Google Play:** a Data safety form filled from the table above, the content rating questionnaire, the target
  audience (16+), a privacy policy URL, and the "News", "Financial" and "Health" declarations as not applicable.
