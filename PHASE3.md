# OREVOSH — Phase 3: the real thing

Phase 2 proved the loop: check, post, react, follow, challenge. Phase 3 turns the pilot into a product with
its own name, a phone-native feel, and the mechanics that let fashion brands and people meet: tags, brand
mentions, brand-featured looks, an Explore tab, avatars, a "For you" feed. Challenges stay, but as one
section of Explore rather than a tab; brand mode is a setting, not a signup question.

The .NET project keeps its `FitCheck.Api` name for now (a mechanical rename for when the repository gets
its final name). Everything a person sees says OREVOSH.

## Design

- **Name:** OREVOSH, always upper case in the wordmark. `app.name` in both locales is "OREVOSH".
- **Palette (dark, phone-first):** `--bg #0b0b0f`, `--surface #15151c`, `--surface-2 #1e1e27`, `--line #2b2b36`,
  `--ink #f4f4f7`, `--ink-2 #b9b9c6`, `--ink-3 #7f7f8e`, **`--accent #b39dff` (lilac)** with `--accent-ink #150f2e`,
  `--accent-2 #ff8fb1` (rose, for the brand mark and "featured"), `--fire #ff6a2b` (reactions), `--ok #5ee6a0`,
  `--danger #ff5d7a`. The neon yellow is gone. One token to change if the owner wants another accent.
- **Type:** Heebo for text (Latin + Hebrew), Syne 800 for the wordmark, display numerals and Latin headings;
  Hebrew headings fall back to Heebo 800. Fonts from Google Fonts with system fallbacks.
- **Feel:** edge-to-edge photo cards on phones, 44px+ touch targets, bottom tab bar with the check button
  raised in the middle, bottom sheets instead of inline forms, skeleton loaders, infinite scroll,
  pull-to-refresh on the feed, double-tap a photo to fire, avatars everywhere, `prefers-reduced-motion`
  honoured. Installable as a PWA (manifest + service worker for the app shell; the API and photos are never
  cached).

## Information architecture

Bottom tabs: **Home** `#/` (feed) · **Explore** `#/explore` · **Check** `#/check` (raised) · **Activity**
`#/activity` · **Profile** `#/me`.

Routes: `#/` and `#/feed[/following]`, `#/explore`, `#/search/:q`, `#/tag/:tag`, `#/post/:id`,
`#/u/:handle[/community|/featured]`, `#/challenges[/ended]`, `#/challenge/:id`, `#/new-challenge`,
`#/check`, `#/result`, `#/activity`, `#/me`, `#/saved`, `#/checks`, `#/settings`, `#/login`, `#/signup`,
`#/welcome` (onboarding after signup).

- **Home:** two segments, *For you* and *Following*, intent chips under them. For you needs no account.
- **Explore:** search box (people, brands, tags); sections: Trending tags (7 days), Brands to follow, Top looks
  this week, Open challenges (with "all challenges" link).
- **Signup:** handle, password, 16+. Nothing else. Then `#/welcome`: pick the styles you wear (intent chips,
  multi-select, skippable) and follow a few brands (if any exist). Then Home.
- **Settings:** avatar (upload / remove), display name, bio, website, language, **Brand account** toggle with
  a one-line explanation (challenges, product links, community tab), delete account.
- **Profile:** avatar, name, bio, website, stats. Tabs: *Looks* for everyone; brands also get *Community*
  (looks that mention the brand) and *Featured* (looks the brand featured); a person gets *Featured* when
  any of their looks was featured.
- **Post card:** avatar, name, handle, intent tag, photo with score badge, headline, caption with tappable
  `#tags` and `@mentions`, "Featured by NEXOR" badge, "in challenge" link, product links, actions
  (fire, comments, save, share, more).

## Data model additions

- `AppUser`: `AvatarPath string?` (under the private storage root, `<userId>/avatar.<ext>`), `AvatarVersion int`
  (bumped on every upload; part of the URL so caches refresh), `Interests string?` (comma-separated
  `StyleIntent` names, at most 8).
- `Post`: `FeaturedByBrandId Guid?`, `FeaturedAt DateTime?`.
- `PostTag { PostId, Tag }` — PK (PostId, Tag), index (Tag, PostId). Tag is lower case, no `#`.
- `PostMention { PostId, UserId }` — PK (PostId, UserId), index (UserId).
- `NotificationType`: `mention`, `featured` added.

## API additions and changes

Conventions unchanged (JSON camelCase, nulls omitted, `X-Requested-With: Orevosh` on every non-GET `/api`
call, cookie `orevosh.session`).

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/auth/signup` | `{ handle, password, confirmed16Plus, language, displayName? }` | as before; `accountType` still accepted but the client no longer sends it |
| `PATCH /api/users/me` 🔒 | `{ language?, displayName?, bio?, website?, accountType?, interests? }` | `accountType` is `Person` or `Brand`, switchable any time; `interests` is a list of intents (≤ 8, unknown ones 400) |
| `POST /api/users/me/avatar` 🔒 | multipart `image` (JPEG/PNG/WebP, ≤ 2 MB) | `200` me with the new `avatarUrl`; 413 / 415 as for checks |
| `DELETE /api/users/me/avatar` 🔒 | — | `200` me |
| `GET /api/users/{handle}/avatar?v=` | — | the image, `Cache-Control: public, max-age=86400`; 404 when none |
| `GET /api/users/{handle}/community` | `?offset&limit` | `FeedDto` of visible posts that mention this account, newest first |
| `GET /api/users/{handle}/featured` | `?offset&limit` | brand: posts it featured (newest featured first); person: their posts that were featured |
| `POST /api/posts` 🔒 | as before | the caption is parsed on the server: `#tags` (`#` + letters/digits/underscore, 2–30 chars, first 5, lower-cased) and `@mentions` (existing handles, case-insensitive, first 5, not yourself). Mentioned accounts get a `mention` notification |
| `POST /api/posts/{id}/feature` 🔒 | — | brand only. The post must mention the brand or be an entry in one of its challenges, and be visible. 409 `error.already_featured` if another brand featured it first. Returns `{ featuredBy }`; the author gets a `featured` notification |
| `DELETE /api/posts/{id}/feature` 🔒 | — | only the brand that featured it. `{ featuredBy: null }` |
| `GET /api/feed` | `?tab=foryou\|following\|top\|fresh&intent&offset&limit` | `foryou` is the default and the new ranking below; `following` needs a session; `top` is most fire in 7 days; `fresh` is newest |
| `GET /api/explore` | — | `{ trendingTags: [{ tag, posts }], brands: [{ user, followers, posts, following }], topLooks: [PostDto], challenges: [ChallengeDto] }` — tags from the last 7 days (top 10), brands by followers (top 10), top 6 looks by fire in 7 days, up to 5 open challenges |
| `GET /api/search?q=` | — | `{ users: [{ user, followers, posts, following }], tags: [{ tag, posts }] }`; `q` 1–40 chars; handle prefix or display-name substring, brands first, 10 each |
| `GET /api/tags/{tag}/posts` | `?offset&limit` | `FeedDto`, newest first (empty for an unknown tag) |
| `GET /api/notifications` 🔒 | — | types now include `mention` and `featured` |
| `GET /api/metrics/pilot` | — | `social` gains `mentions` and `featured` |

DTO changes: `UserRefDto` + `avatarUrl?`; `MeDto` + `avatarUrl?`, `interests`; `ProfileDto` + `avatarUrl?`,
`featured` (count), `community` (count, brands); `PostDto` + `tags`, `mentions` (`UserRefDto[]`),
`featuredBy` (`UserRefDto?`); new `TagDto(tag, posts)`, `UserCardDto(user, followers, posts, following)`,
`ExploreDto`, `SearchDto`, `FeatureStateDto(featuredBy?)`.

### For you ranking

Candidates: visible posts from the last 30 days, newest 400. Score per viewer:
`ln(1 + fireCount) + 0.5·ln(1 + commentCount) + 2 if the viewer follows the author + 1 if the intent is in the
viewer's interests + 0.5 if the viewer had an ok check with that intent in the last 30 days + 1 if featured
by a brand − 0.35 × hours since posting / 24`, ties by newest. Offset paging walks the ranked list. Signed out:
no follow, interest or check terms. Deterministic for a given second, good enough for a pilot; a real
recommender is a later phase.

### Rules that stay

Clothes never the person; checks private until posted; the only photo routes are the post image and the
avatar; 16+ self-declared (documented); refusals stored as status only; one delete for everything (now also
avatar file, tags, mentions, feature marks); caps and the CSRF header; three reports hide.

## Client architecture

`wwwroot/app/core.js` exports the shared kit: `state`, `t()`, `locale`, `api()`, `el()`, `icon()`, `avatar()`,
`toast()`, `announce()`, `navigate()`, `requireSignIn()`, `sheet()` (bottom sheet), `skeleton()`,
`infiniteList()`, `pullToRefresh()`, `doubleTap()`, `relative()`, `fmtNumber()`, `intentLabel()`,
`richCaption()` (turns `#tag`/`@handle` into links), `postCard()`, `userRow()`, `register(route, view)`.
Views live in `wwwroot/app/views/*.js`, one file per area, each registering its routes. `wwwroot/app/main.js`
imports everything and boots. `sw.js` and `manifest.webmanifest` sit at the root of `wwwroot`.

## Tests

xUnit: avatar upload/serve/delete and account-type switch; interests validation; tag and mention parsing at
post time with notifications; feature/unfeature rules; For you ranking terms; explore and search; tag feed;
deletion cascades for the new rows; metrics. Browser test: signup → welcome (interests, follow a brand) →
check → post with `#tag @brand` → Explore search → tag page → brand community tab → brand features the look
→ author's activity → avatar upload → manifest and service worker registered → PWA meta present.
