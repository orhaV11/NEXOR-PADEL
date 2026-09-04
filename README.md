# FitCheck — Phase 1 pilot

A 10-second outfit check. Pick where the outfit is going (date, office, streetwear…), add a photo, and get
back a score **relative to that intent**, a breakdown of the visible items, two or three things that work,
and **the one tip**: the single highest-impact change you can make today.

Phase 1 is private and single-player: AI feedback only. Its only job is to answer one question with real
data: *do people come back for a second check within 7 days without being pushed?* That number is
`returnRate` on `/api/metrics/pilot`, and it decides whether Phase 2 gets built.

English is the default language, Hebrew (RTL) ships alongside it, and the stylist writes its feedback in the
user's language.

## Run it in 5 minutes

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and an Anthropic API key.

```bash
export ANTHROPIC_API_KEY=sk-ant-...        # the only secret; never put it in appsettings
cd src/FitCheck.Api
dotnet run
```

Open http://localhost:5000 (the port is printed on start). The SQLite database (`fitcheck.db`) and the
private photo folder (`storage/`) are created next to the project on first run; both are git-ignored.

### On a phone

Phones need HTTPS for the camera and share sheet, so put a tunnel in front of the local server:

```bash
# Cloudflare Tunnel (no account needed for a quick tunnel)
cloudflared tunnel --url http://localhost:5000

# or ngrok
ngrok http 5000
```

Send the printed `https://…` URL to your pilot users. The page is a single file served from `wwwroot`, same
origin as the API, so nothing else needs configuring.

### Read the pilot metrics

```bash
curl -s http://localhost:5000/api/metrics/pilot | jq
```

```json
{
  "totalChecks": 0,
  "usersWithAtLeastOneCheck": 0,
  "usersWithSecondCheckWithin7Days": 0,
  "returnRate": 0.0,
  "avgLatencyMs": 0,
  "scoreDistribution": { "1": 0, "2": 0, "3": 0, "4": 0, "5": 0, "6": 0, "7": 0, "8": 0, "9": 0, "10": 0 },
  "byLanguage": {},
  "byPromptVersion": {}
}
```

All numbers cover checks with `status = "ok"`. `returnRate` = users whose second OK check happened at most 7
days after their first ÷ users with at least one OK check.

### Run the tests

```bash
dotnet test        # from the repository root (FitCheck.sln)
```

90 tests: magic-byte detection, the disk image store, analyzer mapping and clamping, locale matching, the
Anthropic client against a scripted HTTP handler (request shape, single retry, refusal handling), and endpoint
smoke tests against the real app with a scripted vision client (user validation, uploads, 413/415/429/502,
deletion removing files, metrics math on a seeded dataset).

There is also a browser smoke test in [`tools/e2e`](tools/e2e/README.md): Playwright drives the real page in a
phone viewport against the real API with only the Anthropic API stubbed, in Hebrew and English.

### Check the calibration before inviting people

The score calibration lives in the prompt and only a real sample tells you whether it holds. With the
server running and a folder of 10 varied outfit photos:

```bash
scripts/calibrate.sh ./sample-photos Casual
```

It creates a throwaway user, checks every photo, prints each score and the distribution, then deletes the
user. If most scores land on 7–8, tighten the calibration text in `Services/OutfitAnalyzer.cs` and bump
`PromptVersion` so the two rubrics can be compared in `byPromptVersion`.

## Configuration

`src/FitCheck.Api/appsettings.json`:

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | `Data Source=fitcheck.db` | SQLite file, created on first run (`EnsureCreated`, no migrations) |
| `Anthropic:Model` | `claude-sonnet-5` | Must support forced tool use: Sonnet 5, Opus 5, the 4.x family, Haiku 4.5 |
| `Anthropic:MaxTokens` | `1200` | Output budget for the tool call (Hebrew is token-heavy) |
| `Anthropic:BaseUrl` | `https://api.anthropic.com` | Override to point at a stub in tests |
| `Storage:Root` | `storage` | Private photo folder. Relative paths resolve against the content root, never `wwwroot` |
| `Storage:MaxImageBytes` | `6291456` | Upload limit (6 MB). The client downscales to 1280px JPEG first |
| `Limits:ChecksPerDay` | `20` | Per-user cap over a rolling 24 hours |
| `Limits:ChecksPerDayGlobal` | `1000` | Ceiling across all users over a rolling 24 hours, so a leaked URL cannot run up an unbounded bill |
| `Limits:SignupsPerHourPerIp` | `20` | New accounts per client address per hour (address taken from `X-Forwarded-For` behind the tunnel) |

Any key can be overridden with an environment variable, e.g. `Limits__ChecksPerDay=5`.
`ANTHROPIC_API_KEY` is read from the environment only.

## API

All responses are JSON (camelCase). Errors are `{ "error": "<message in the caller's language>" }`.
Language for messages: an explicit `language` field wins, then `Accept-Language`, then English.

| Method & path | Body | Returns |
|---|---|---|
| `POST /api/users` | `{ handle, confirmed16Plus, language }` | `201 { id, handle, language }`. 400 if not 16+ or the handle is not 2–40 characters |
| `PATCH /api/users/{id}` | `{ language }` | Updated user. 400 for an unsupported language |
| `DELETE /api/users/{id}` | — | 204. Deletes the user, every check and every photo file |
| `GET /api/users/{id}/checks` | — | Last 50 checks, newest first (all statuses; the client shows OK ones) |
| `POST /api/checks` | multipart: `userId`, `intent`, `occasion?`, `language`, `image` | `201 { id, intent, occasion, language, createdAt, latencyMs, status, score, feedback }`. 413 too large, 415 not JPEG/PNG/WebP, 429 over the daily cap (with `Retry-After`), 502 model failure |
| `GET /api/checks/{id}?userId=` | — | The check, owner only (404 otherwise) |
| `GET /api/metrics/pilot` | — | See above |

`intent` is one of `Casual, Date, Streetwear, OldMoney, Minimal, Office, Party, Sport`.
`feedback.status` is `ok`, `not_outfit` or `rejected`; only `ok` carries items, working and oneTip.

## How it is built

```
FitCheck.sln
src/FitCheck.Api/
  Program.cs                      wiring, EnsureCreated, error shape, static files
  appsettings.json
  Domain/                         StyleIntent, AppUser, OutfitCheck, OutfitFeedback, options
  Data/AppDbContext.cs            SQLite via EF Core, index on (UserId, CreatedAt)
  Services/OutfitAnalyzer.cs      ← the product: system prompt, intent guide, tool schema, PromptVersion, mapping
  Services/AnthropicVisionClient  Messages API over HttpClient: base64 image + forced tool call, 60s timeout, one retry
  Services/DiskImageStore.cs      storage/<userId>/<checkId>.<ext>, behind IImageStore for a later blob store
  Services/Localizer.cs           server messages (en/he) and Accept-Language matching
  Endpoints/                      users, checks, metrics
  wwwroot/index.html              the whole client: vanilla HTML/CSS/JS, no build step
  wwwroot/i18n/en.json, he.json   UI strings; add a locale by adding a file
tests/FitCheck.Api.Tests/         xUnit
tools/e2e/                        optional browser smoke test (Playwright + a stub of the Anthropic API)
scripts/calibrate.sh              score-distribution check against the real model
```

The model is forced to call a tool (`tool_choice: {type: "tool"}`) whose input schema is our feedback
shape, so the answer is always JSON we can validate. Scores are clamped to 1–10, intent match to 0–100,
and anything descriptive is dropped when the status is not `ok`.

### Rules the code enforces

- **Clothes, never the person.** The prompt forbids any reference to body, face, skin, age or gender, and
  the UI copy follows the same rule.
- **Photos are private.** They live under `Storage:Root`, outside `wwwroot`, and no route serves them. A
  test asserts that every stored photo returns 404 on every plausible URL.
- **16+ only, self-declared.** Account creation fails without the checkbox. See the limitations below.
- **Bad input is refused.** Non-outfit photos get a friendly state. Nudity, sexual content or an apparent
  minor gets a neutral rejection: the photo is deleted immediately, nothing but the status is stored, and
  the model's own words are never shown.
- **One endpoint deletes everything.** User row, checks, photo files.
- **No hallucinated brands or items.** Instruction in the prompt; the model may only name what is visible.
- **Cost control.** 20 checks per user per rolling 24 hours (429 with a friendly message), counted including
  checks still in flight so a parallel burst cannot slip past; a global ceiling of 1000 checks a day across
  everyone; 20 new accounts per hour per client address; a 6 MB upload cap; and the client downscales to
  1280px JPEG before uploading. Failed model calls do not count toward the caps.

## Known limitations (read before inviting anyone)

- **The user id is the only credential.** Anyone who has a user's id can read their checks, post checks
  in their name and delete their account. Ids are random GUIDs held in `localStorage`, which is fine for a
  50-person private pilot and nothing more. Real authentication is Phase 2.
- **Age is self-declared.** A checkbox is not age assurance. Before any public launch, integrate the Apple
  and Google age-signal APIs (or an equivalent provider) and gate account creation on the result.
- **Metrics are unauthenticated.** `/api/metrics/pilot` only returns aggregates, but put it behind a
  password or an allow-list before the URL leaves the team.
- **Single process, single SQLite file.** The in-flight reservation that closes the cap race lives in
  memory, so running two instances would reopen it. One instance is all the pilot needs.
- **The signup limiter trusts `X-Forwarded-For`.** That is right behind the tunnel and spoofable if Kestrel
  is exposed directly; the global daily ceiling bounds the damage either way.
- **Calibration is unverified until you run it.** The build was tested against a stubbed model; run
  `scripts/calibrate.sh` on real photos before judging scores.
- **Photos stay on disk until the user deletes their account.** There is no retention job yet.

## Phase 2 (only if returnRate says so)

Real authentication, age assurance, closet memory built on the history list, crowd feedback and duels,
share images, a feed, push notifications, native wrappers, payments, shopping links, and a blob store
behind `IImageStore`. None of it is scaffolded here on purpose.

## Decisions

Every judgment call made while building, and every place the brief and instinct disagreed, is in
[`DECISIONS.md`](DECISIONS.md).
