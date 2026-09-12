using System.Collections.Concurrent;
using System.Globalization;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// One week of the board. FirstDay is the local date the week starts on in Board:TimeZone; Start and End are the UTC
/// instants of that midnight and of the next week's (the window is [Start, End)); Label is FirstDay as a UTC date, the
/// key <see cref="WeeklyWinner.WeekStart"/> is written with.
/// </summary>
public sealed record BoardWeek(DateOnly FirstDay, DateTime Start, DateTime End)
{
    public DateTime Label => new(FirstDay.Year, FirstDay.Month, FirstDay.Day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>"2026-09-06": the label as the ?week= query and the log lines spell it.</summary>
    public string Key => FirstDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// One place on one board before it is turned into a DTO or an archive row: the person, the look (null on the people
/// board), the fires that counted, the stylist's score on the picks board, the looks posted that week on the people board.
/// </summary>
public sealed record BoardEntry(int Rank, Guid UserId, Guid? PostId, int Fires, int? Score = null, int? Looks = null);

/// <summary>The five boards of one week. Closed: read from the archive rather than computed.</summary>
public sealed record BoardResult(
    BoardWeek Week, bool Closed, int CountedFires,
    List<BoardEntry> Looks, List<BoardEntry> People, List<BoardEntry> Rising, Dictionary<StyleIntent, List<BoardEntry>> Intents, List<BoardEntry> Picks)
{
    /// <summary>Every place of every board with its board name, in the order the archive and the hall list them.</summary>
    public IEnumerable<(string Board, BoardEntry Entry)> All()
    {
        foreach (var entry in Looks)
        {
            yield return (BoardName.Looks, entry);
        }

        foreach (var entry in People)
        {
            yield return (BoardName.People, entry);
        }

        foreach (var entry in Rising)
        {
            yield return (BoardName.Rising, entry);
        }

        foreach (var (intent, entries) in Intents.OrderBy(pair => pair.Key))
        {
            foreach (var entry in entries)
            {
                yield return (BoardName.Intent(intent), entry);
            }
        }

        foreach (var entry in Picks)
        {
            yield return (BoardName.Picks, entry);
        }
    }
}

/// <summary>
/// The weekly flames board (Round 10): the week's window in the board's zone, which fires count, the five boards, and a
/// short memory cache so a busy Saturday evening does not recompute the week on every read. The rules are
/// <see cref="BoardOptions"/>: a fire counts when the firer has made at least MinChecksToCount ok checks (by the week's
/// end), when their account was older than NewAccountDays at the time of the fire, when the look is not their own, and
/// while it is within the first MaxPerFirerPerAuthor fires from that person to that author in the week. A look a moderator
/// excluded (<see cref="BoardExclusion"/>) or one under review (Hidden) is on no board. One instance for the app: the
/// routes and the closer pass their own <see cref="AppDbContext"/> in.
/// </summary>
public sealed class Board
{
    /// <summary>How long a computed week is served from memory (Board:CacheSeconds). Real time, not the board's clock: it is about load.</summary>
    public TimeSpan CacheTtl => TimeSpan.FromSeconds(Math.Max(0, _options.CacheSeconds));

    /// <summary>The places on the looks board that wear a badge the week after (<see cref="BadgeAsync"/>).</summary>
    public const int BadgeRanks = 3;

    private readonly BoardOptions _options;
    private readonly IClock _clock;
    private readonly TimeZoneInfo _zone;
    private readonly ConcurrentDictionary<DateOnly, (long At, BoardResult Result)> _cache = new();

    public Board(IOptions<BoardOptions> options, IClock clock, ILogger<Board> logger)
    {
        _options = options.Value;
        _clock = clock;
        _zone = ResolveZone(_options.TimeZone, logger);
    }

    public BoardOptions Options => _options;

    public TimeZoneInfo Zone => _zone;

    /// <summary>The zone the week is cut in; UTC, with a warning, when the configured id is not one this machine knows.</summary>
    public static TimeZoneInfo ResolveZone(string id, ILogger logger)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            logger.LogWarning("Board: the time zone {TimeZone} is not known here; the week runs in UTC", id);
            return TimeZoneInfo.Utc;
        }
    }

    // ---- the window ----

    public BoardWeek CurrentWeek() => WeekOf(_clock.UtcNow);

    /// <summary>The week that contains a UTC instant.</summary>
    public BoardWeek WeekOf(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);
        return WeekContaining(DateOnly.FromDateTime(local));
    }

    /// <summary>The week that contains a local date (a date in the board's zone).</summary>
    public BoardWeek WeekContaining(DateOnly localDate)
    {
        var back = ((int)localDate.DayOfWeek - (int)_options.WeekStartsOn + 7) % 7;
        return WeekStartingOn(localDate.AddDays(-back));
    }

    /// <summary>The week whose first day is the local date given (the archive's label), whatever WeekStartsOn says today.</summary>
    public BoardWeek WeekStartingOn(DateOnly firstDay) => new(firstDay, MidnightUtc(firstDay), MidnightUtc(firstDay.AddDays(7)));

    public BoardWeek Previous(BoardWeek week) => WeekStartingOn(week.FirstDay.AddDays(-7));

    public BoardWeek Next(BoardWeek week) => WeekStartingOn(week.FirstDay.AddDays(7));

    /// <summary>Seconds until the week closes, 0 once it has.</summary>
    public int ClosesIn(BoardWeek week)
    {
        var seconds = Math.Ceiling((week.End - _clock.UtcNow).TotalSeconds);
        return seconds <= 0 ? 0 : (int)Math.Min(int.MaxValue, seconds);
    }

    /// <summary>
    /// ?week=: "yyyy-MM-dd" is a date in the board's zone and answers the week that contains it; a full ISO-8601 instant
    /// (the weekStart a board answered, say) is taken as an instant. Anything else is garbage.
    /// </summary>
    public bool TryParseWeek(string? text, out BoardWeek week)
    {
        text = text?.Trim() ?? "";
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            week = WeekContaining(date);
            return true;
        }

        if (text.Contains('T') && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var instant))
        {
            week = WeekOf(instant);
            return true;
        }

        week = default!;
        return false;
    }

    /// <summary>Local midnight of a date as a UTC instant. A midnight a DST change skips (not Israel's, which changes at 02:00) moves to the first valid minute.</summary>
    private DateTime MidnightUtc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        for (var attempt = 0; attempt < 12 && _zone.IsInvalidTime(local); attempt++)
        {
            local = local.AddMinutes(15);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, _zone);
    }

    // ---- reading ----

    /// <summary>
    /// The week as the board shows it: the archive once the week is over and closed, otherwise the computation, served from
    /// memory for <see cref="CacheTtl"/>.
    /// </summary>
    public async Task<BoardResult> ReadAsync(AppDbContext db, BoardWeek week, CancellationToken ct)
    {
        if (week.End <= _clock.UtcNow)
        {
            var archived = await FromArchiveAsync(db, week, ct);
            if (archived is not null)
            {
                return archived;
            }
        }

        if (CacheTtl > TimeSpan.Zero && _cache.TryGetValue(week.FirstDay, out var cached) && Environment.TickCount64 - cached.At < CacheTtl.TotalMilliseconds)
        {
            return cached.Result;
        }

        var result = await ComputeAsync(db, week, ct);
        _cache[week.FirstDay] = (Environment.TickCount64, result);
        return result;
    }

    /// <summary>Forgets every cached week: after an exclusion, and in tests that want the next read fresh.</summary>
    public void Invalidate() => _cache.Clear();

    /// <summary>Whether the closer has written the week (any archive row under its label).</summary>
    public static Task<bool> IsClosedAsync(AppDbContext db, BoardWeek week, CancellationToken ct)
    {
        var label = week.Label;
        return db.WeeklyWinners.AnyAsync(w => w.WeekStart == label, ct);
    }

    /// <summary>The closed week out of its <see cref="WeeklyWinner"/> rows; null when the week was never closed.</summary>
    public async Task<BoardResult?> FromArchiveAsync(AppDbContext db, BoardWeek week, CancellationToken ct)
    {
        var label = week.Label;
        var rows = await db.WeeklyWinners.Where(w => w.WeekStart == label).OrderBy(w => w.Rank).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return null;
        }

        var looks = new List<BoardEntry>();
        var people = new List<BoardEntry>();
        var rising = new List<BoardEntry>();
        var picks = new List<BoardEntry>();
        var intents = new Dictionary<StyleIntent, List<BoardEntry>>();
        foreach (var row in rows)
        {
            var entry = new BoardEntry(row.Rank, row.UserId, row.PostId, row.Fires, row.Score);
            switch (row.Board)
            {
                case BoardName.Looks:
                    looks.Add(entry);
                    break;
                case BoardName.People:
                    people.Add(entry);
                    break;
                case BoardName.Rising:
                    rising.Add(entry);
                    break;
                case BoardName.Picks:
                    picks.Add(entry);
                    break;
                default:
                    if (row.Board.StartsWith(BoardName.IntentPrefix, StringComparison.Ordinal)
                        && Enum.TryParse<StyleIntent>(row.Board[BoardName.IntentPrefix.Length..], ignoreCase: false, out var intent))
                    {
                        if (!intents.TryGetValue(intent, out var list))
                        {
                            list = [];
                            intents[intent] = list;
                        }

                        list.Add(entry);
                    }

                    break;
            }
        }

        return new BoardResult(week, Closed: true, CountedFires: looks.Sum(e => e.Fires), looks, people, rising, intents, picks);
    }

    // ---- computing ----

    private sealed record FireRow(Guid PostId, Guid FirerId, Guid AuthorId, DateTime CreatedAt);

    private sealed record PostRow(Guid Id, Guid UserId, StyleIntent Intent, int Score, DateTime CreatedAt);

    /// <summary>The week from its fires and looks, the rules applied. Never cached here; <see cref="ReadAsync"/> is the cached door.</summary>
    public async Task<BoardResult> ComputeAsync(AppDbContext db, BoardWeek week, CancellationToken ct)
    {
        var rules = _options;
        var start = week.Start;
        var end = week.End;
        var excluded = (await db.BoardExclusions.Select(e => e.PostId).ToListAsync(ct)).ToHashSet();

        // The week's fires with their looks. A look under review or off the board is out before anything is counted, and
        // so is a fire on one's own look.
        var fires = await db.Fires
            .Where(f => f.CreatedAt >= start && f.CreatedAt < end)
            .Join(db.Posts.Where(p => !p.Hidden), f => f.PostId, p => p.Id, (f, p) => new FireRow(f.PostId, f.UserId, p.UserId, f.CreatedAt))
            .ToListAsync(ct);
        fires.RemoveAll(f => excluded.Contains(f.PostId) || f.FirerId == f.AuthorId);

        // The looks that were fired plus the looks posted this week (the picks board and the people board's "looks" count).
        var firedIds = fires.Select(f => f.PostId).Distinct().ToList();
        var posts = await db.Posts
            .Where(p => !p.Hidden && (firedIds.Contains(p.Id) || (p.CreatedAt >= start && p.CreatedAt < end)))
            .Select(p => new PostRow(p.Id, p.UserId, p.Intent, p.Score, p.CreatedAt))
            .ToListAsync(ct);
        posts.RemoveAll(p => excluded.Contains(p.Id));
        var postsById = posts.ToDictionary(p => p.Id);

        // The firers' standing (ok checks by the week's end, account age) and the authors' age for the rising board.
        var userIds = fires.Select(f => f.FirerId).Concat(posts.Select(p => p.UserId)).Distinct().ToList();
        var created = userIds.Count == 0
            ? new Dictionary<Guid, DateTime>()
            : await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.CreatedAt }).ToDictionaryAsync(u => u.Id, u => u.CreatedAt, ct);
        var firerIds = fires.Select(f => f.FirerId).Distinct().ToList();
        var checks = rules.MinChecksToCount <= 0 || firerIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.Checks
                .Where(c => c.UserId != null && firerIds.Contains(c.UserId.Value) && c.Status == CheckStatus.Ok && c.CreatedAt < end)
                .GroupBy(c => c.UserId!.Value)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.UserId, g => g.Count, ct);

        // The rules, fire by fire in the order they happened, so the pair cap keeps a person's first fires on an author.
        var counted = new List<FireRow>();
        var perPair = new Dictionary<(Guid Firer, Guid Author), int>();
        foreach (var fire in fires.OrderBy(f => f.CreatedAt).ThenBy(f => f.PostId))
        {
            if (rules.MinChecksToCount > 0 && checks.GetValueOrDefault(fire.FirerId) < rules.MinChecksToCount)
            {
                continue;
            }

            if (!created.TryGetValue(fire.FirerId, out var firerCreated) || firerCreated.AddDays(rules.NewAccountDays) > fire.CreatedAt)
            {
                continue;
            }

            var pair = (fire.FirerId, fire.AuthorId);
            var soFar = perPair.GetValueOrDefault(pair);
            if (rules.MaxPerFirerPerAuthor > 0 && soFar >= rules.MaxPerFirerPerAuthor)
            {
                continue;
            }

            perPair[pair] = soFar + 1;
            counted.Add(fire);
        }

        var size = Math.Max(1, rules.Size);
        var firesByPost = counted.GroupBy(f => f.PostId).ToDictionary(g => g.Key, g => g.Count());

        // Looks: the fired looks by counted fires, an earlier look first on a tie. A look hidden between the two queries is simply gone.
        var fired = firesByPost.Keys.Where(postsById.ContainsKey).Select(id => postsById[id]).ToList();
        List<BoardEntry> RankLooks(IEnumerable<PostRow> candidates) => candidates
            .Where(p => firesByPost.GetValueOrDefault(p.Id) > 0)
            .OrderByDescending(p => firesByPost[p.Id]).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Take(size)
            .Select((p, index) => new BoardEntry(index + 1, p.UserId, p.Id, firesByPost[p.Id]))
            .ToList();
        var looks = RankLooks(fired);

        // People: the sum over their looks; ties go to the older account. Looks: what they posted this week.
        var postedThisWeek = posts.Where(p => p.CreatedAt >= start && p.CreatedAt < end).GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.Count());
        var people = fired
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Fires = g.Sum(p => firesByPost[p.Id]) })
            .OrderByDescending(x => x.Fires).ThenBy(x => created.GetValueOrDefault(x.UserId)).ThenBy(x => x.UserId)
            .Take(size)
            .Select((x, index) => new BoardEntry(index + 1, x.UserId, null, x.Fires, Looks: postedThisWeek.GetValueOrDefault(x.UserId)))
            .ToList();

        // Rising: the looks of people whose account is younger than RisingDays at the week's end.
        var risingSince = end.AddDays(-Math.Max(0, rules.RisingDays));
        var rising = RankLooks(fired.Where(p => created.TryGetValue(p.UserId, out var authorCreated) && authorCreated > risingSince));

        // By intent: the looks board per StyleIntent, only where something was counted.
        var intents = fired.GroupBy(p => p.Intent).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => RankLooks(g));

        // Picks: the looks posted this week by the stylist's score, counted fires then age on a tie. Not gameable: a fire moves nothing past a score.
        var picks = posts
            .Where(p => p.CreatedAt >= start && p.CreatedAt < end)
            .OrderByDescending(p => p.Score).ThenByDescending(p => firesByPost.GetValueOrDefault(p.Id)).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Take(size)
            .Select((p, index) => new BoardEntry(index + 1, p.UserId, p.Id, firesByPost.GetValueOrDefault(p.Id), Score: p.Score))
            .ToList();

        return new BoardResult(week, Closed: false, counted.Count, looks, people, rising, intents, picks);
    }

    // ---- DTOs ----

    /// <summary>
    /// The week as GET /api/board answers it: every row carries the person; a look's row carries the look when it still
    /// exists and is visible (an archived place whose look is gone keeps the place). Me: the caller's best place on each
    /// board when signed in and on it.
    /// </summary>
    public async Task<BoardDto> ToDtoAsync(AppDbContext db, PostReader reader, BoardResult result, Guid? viewerId, CancellationToken ct)
    {
        var week = result.Week;
        var entries = result.All().ToList();
        var postIds = entries.Where(e => e.Entry.PostId != null).Select(e => e.Entry.PostId!.Value).Distinct().ToList();
        var posts = postIds.Count == 0 ? new List<Post>() : await db.Posts.Where(p => postIds.Contains(p.Id) && !p.Hidden).ToListAsync(ct);
        var postDtos = (await reader.ToDtosAsync(posts, viewerId, ct)).ToDictionary(p => p.Id);
        var users = await reader.RefsAsync(entries.Select(e => e.Entry.UserId), ct);

        // The people board says how many looks each person posted that week; the archive does not keep it, so it is counted now.
        var peopleIds = result.People.Select(e => e.UserId).Distinct().ToList();
        var start = week.Start;
        var end = week.End;
        var posted = peopleIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.Posts.Where(p => !p.Hidden && peopleIds.Contains(p.UserId) && p.CreatedAt >= start && p.CreatedAt < end)
                .GroupBy(p => p.UserId).Select(g => new { UserId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.UserId, g => g.Count, ct);

        BoardRowDto Row(BoardEntry entry, bool people, bool picks) => new(
            entry.Rank, entry.Fires,
            entry.PostId is Guid postId ? postDtos.GetValueOrDefault(postId) : null,
            users.GetValueOrDefault(entry.UserId),
            people ? entry.Looks ?? posted.GetValueOrDefault(entry.UserId) : null,
            picks ? entry.Score : null);
        List<BoardRowDto> Rows(List<BoardEntry> list, bool people = false, bool picks = false) => list.Select(e => Row(e, people, picks)).ToList();

        static int? BestRank(IEnumerable<BoardEntry> list, Guid viewer) =>
            list.Where(e => e.UserId == viewer).Select(e => (int?)e.Rank).Min();

        BoardMeDto? me = null;
        if (viewerId is Guid viewer)
        {
            var mine = new BoardMeDto(
                BestRank(result.Looks, viewer), BestRank(result.People, viewer), BestRank(result.Rising, viewer),
                BestRank(result.Intents.Values.SelectMany(v => v), viewer), BestRank(result.Picks, viewer));
            me = mine.Looks is null && mine.People is null && mine.Rising is null && mine.Intent is null && mine.Picks is null ? null : mine;
        }

        var sponsor = _options.Sponsor is { Enabled: true } s
            ? new BoardSponsorDto(s.Name.Trim(), Blank(s.Handle), Blank(s.PrizeText), Blank(s.Url))
            : null;

        return new BoardDto(
            week.Start, week.End, result.Closed ? 0 : ClosesIn(week), result.Closed,
            Rows(result.Looks), Rows(result.People, people: true), Rows(result.Rising),
            result.Intents.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(), pair => Rows(pair.Value)),
            Rows(result.Picks, picks: true),
            sponsor, me);
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Last week's badge: the best place in the top <see cref="BadgeRanks"/> of the looks board of the week before the
    /// current one, worn for this week only. Null the week after, and for everyone else.
    /// </summary>
    public async Task<BadgeDto?> BadgeAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var last = Previous(CurrentWeek());
        var label = last.Label;
        var rank = await db.WeeklyWinners
            .Where(w => w.UserId == userId && w.WeekStart == label && w.Board == BoardName.Looks && w.Rank <= BadgeRanks)
            .OrderBy(w => w.Rank)
            .Select(w => (int?)w.Rank)
            .FirstOrDefaultAsync(ct);
        return rank is int place ? new BadgeDto(BoardName.Looks, place, last.Start) : null;
    }

    /// <summary>The order the hall lists boards in: looks, people, rising, the intents, picks.</summary>
    public static int BoardOrder(string board) => board switch
    {
        BoardName.Looks => 0,
        BoardName.People => 1,
        BoardName.Rising => 2,
        BoardName.Picks => 4,
        _ => 3
    };
}
