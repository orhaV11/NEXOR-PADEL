# FitCheck — Phase 2: the social layer

Phase 1 answered "will one person come back for a second check?". Phase 2 turns the check into a post,
adds the loop that makes people come back for each other, and gives brands a reason to show up:
challenges with a prize, decided by the crowd.

Same stack, same repo, same rules. Everything below is built on top of the Phase 1 API.

## What changes for the user

- **The feed is the home screen, open to everyone.** Scrolling, profiles and challenges need no account.
  An account is needed to react, comment, check, post or vote. Filter the feed by intent to hunt for
  inspiration ("show me office looks") and save posts to a private collection.
- **Accounts are real.** Handle + password, optional display name, bio and website. A brand ticks "this is a
  brand account" at signup and gets a badge, a "New challenge" button, and product links on its posts.
- **A check stays private until you tap "Post it".** Posting publishes the photo, the intent, the stylist's
  score and headline, and an optional caption. The one tip and the item breakdown stay private coaching.
- **Fire, comment, save, follow, feed.** One positive reaction (fire), short comments (200 characters),
  private saves, following, and a feed with three tabs: Fresh, Top (most fire in the last 7 days) and
  Following, each filterable by intent. Profiles show posts, followers, fire received, best score and the
  current daily streak.
- **Challenges.** A brand opens a challenge: title, brief, intent, prize, optional product link, end date.
  People enter with a check of that intent (one entry each). Everyone votes (one vote per challenge, can be
  changed until the end). When it ends the winner is fixed automatically, and both the winner and the brand
  get a notification. Fulfilment of the prize happens between them, outside the app.
- **Activity.** In-app notifications: fire on your post, new follower, vote on your entry, a new entry in
  your challenge, challenge ended, you won.
- **Shop.** Brand posts and challenges carry up to three product links. No payments in this phase.

## What stays out, on purpose

- **No downvotes, no public score comparisons between people.** The stylist scores the outfit against its
  intent; the crowd decides challenges.
- **No payments, no push notifications, no native app.** Product links open the brand's own site.
- **No age assurance beyond the 16+ checkbox.** Still required before public launch.

## Safety and privacy rules carried over

1. Judge clothes, never the person. Applies to the stylist and to every string in the UI.
2. Photos are private until posted. Posting is explicit and per check. Deleting a post makes the photo
   private again; deleting the account removes everything.
3. Only checks the stylist marked `ok` can be posted, so nothing the model refused ever becomes public.
4. Any post or comment with three reports is hidden pending review. The post owner can delete any comment
   on their post. Comments are the harassment channel of every social product; this is the minimum, and
   a moderation queue is the first Phase 3 item.
5. The daily check cap, the global ceiling and the signup limiter stay.

## Data model (additions)

```
AppUser      + PasswordHash, AccountType (Person|Brand), DisplayName?, Bio?, Website?, StreakCount, LastCheckDate?
Post         { Id, UserId, CheckId (unique), Intent, Score, IntentMatch, Headline, Caption?, ChallengeId?, FireCount, CommentCount, ReportCount, Hidden, CreatedAt }
Comment      { Id, PostId, UserId, Text, ReportCount, Hidden, CreatedAt }
SavedPost    { UserId, PostId, CreatedAt }
ProductLink  { Id, PostId, Label, Url (https), Price? }              brand posts only, max 3
Fire         { PostId, UserId, CreatedAt }
Follow       { FollowerId, FollowedId, CreatedAt }
Challenge    { Id, BrandId, Title, Brief, Intent, Prize, PrizeUrl?, EndsAt, WinnerPostId?, ResolvedAt?, CreatedAt }
ChallengeVote{ ChallengeId, UserId, PostId, CreatedAt }
Notification { Id, UserId, Type, ActorHandle, PostId?, ChallengeId?, CreatedAt, ReadAt? }
Report       { Id, PostId?, CommentId?, ReporterId, Reason, CreatedAt }
```

## API (additions; cookie session, JSON, camelCase)

Every non-GET request must carry the header `X-Requested-With: FitCheck`; that, plus a SameSite=Strict
cookie, is the CSRF defence. Reads (feed, posts, profiles, challenges, comments, images) are public;
viewer-specific fields are false for anonymous readers.

| Method & path | Notes |
|---|---|
| `POST /api/auth/signup` | `{ handle, password, confirmed16Plus, language, accountType, displayName? }` → 201 user, sets cookie |
| `POST /api/auth/login` | `{ handle, password }` → user, sets cookie. Rate limited per address |
| `POST /api/auth/logout` | 204 |
| `GET /api/auth/me` | current user with counts, 401 if signed out |
| `PATCH /api/users/me` | `{ language?, displayName?, bio?, website? }` |
| `DELETE /api/users/me` | removes the account and everything it created |
| `POST /api/checks` | as Phase 1, owner from the cookie |
| `GET /api/users/me/checks` | last 50 own checks |
| `POST /api/posts` | `{ checkId, caption?, challengeId?, products? }` |
| `GET /api/posts/{id}` | post with viewer state |
| `GET /api/posts/{id}/image` | the photo, only while the post is public |
| `DELETE /api/posts/{id}` | owner only; the photo becomes private again |
| `POST/DELETE /api/posts/{id}/fire` | `{ fireCount, fired }` |
| `POST /api/posts/{id}/report` | `{ reason }` |
| `POST/DELETE /api/posts/{id}/save` | private bookmark |
| `GET /api/me/saved` | saved posts |
| `GET /api/posts/{id}/comments` | visible comments, oldest first |
| `POST /api/posts/{id}/comments` | `{ text }` |
| `DELETE /api/comments/{id}` | author or post owner |
| `POST /api/comments/{id}/report` | `{ reason }` |
| `GET /api/feed?tab=fresh|top|following&intent=&offset=&limit=` | `{ items, nextOffset }`; public except `following` |
| `GET /api/users/{handle}` | public profile with viewer state |
| `GET /api/users/{handle}/posts?offset=` | |
| `POST/DELETE /api/users/{handle}/follow` | `{ followers, following }` |
| `GET /api/challenges?state=open|ended` | with entry counts and the top 3 |
| `GET /api/challenges/{id}` | detail, entries by votes, viewer's vote, winner |
| `POST /api/challenges` | brand only |
| `POST /api/challenges/{id}/vote` | `{ postId }`, while open |
| `GET /api/notifications` | last 50 + unread count |
| `POST /api/notifications/read` | mark all read |
| `GET /api/metrics/pilot` | Phase 1 block + `social` block |

## Client

Five tabs: Feed, Challenges, Check (the big one), Activity, Profile. Hash routes, no framework, no build
step; split into `index.html`, `app.js`, `app.css` and the two locale files. Dark base, one electric
accent, big display numerals, short deliberate motion (fire burst, score count-up, card entrance).
Hebrew RTL and the locale switcher stay.
