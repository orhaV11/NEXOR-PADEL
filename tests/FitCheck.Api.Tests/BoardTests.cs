using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FitCheck.Api.Tests;

/// <summary>
/// The board's fixtures. Weeks are picked in 2027 so an account signed up now is older than NewAccountDays at every fire
/// and older than RisingDays at every week's end; a test that wants a young account sets its CreatedAt. Fires are written
/// straight to the table with the instant the scenario needs; the fire route stamps real time, which is not in any week
/// the board's fake clock points at.
/// </summary>
public static class BoardFixtures
{
    public static readonly TimeZoneInfo Israel = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    /// <summary>A Sunday in winter (UTC+2). Week k starts on <see cref="Sunday"/>(k).</summary>
    public static readonly DateOnly FirstSunday = new(2027, 1, 10);

    public static DateOnly Sunday(int week) => FirstSunday.AddDays(7 * week);

    /// <summary>A wall-clock moment in Israel as the UTC instant, computed here independently of the board's own math.</summary>
    public static DateTime Local(DateOnly date, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Unspecified), Israel);

    /// <summary>Wednesday noon of week k: a "now" inside the week.</summary>
    public static DateTime Midweek(int week) => Local(Sunday(week).AddDays(3), 12);

    public static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static async Task WithDbAsync(TestApp app, Func<AppDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>A fire at a chosen instant. The counter moves like the route moves it; the board never reads it.</summary>
    public static Task FireAsync(TestApp app, Guid postId, Guid firerId, DateTime at) => WithDbAsync(app, async db =>
    {
        db.Fires.Add(new Fire { PostId = postId, UserId = firerId, CreatedAt = at });
        await db.SaveChangesAsync();
        await db.Posts.Where(p => p.Id == postId).ExecuteUpdateAsync(s => s.SetProperty(p => p.FireCount, p => p.FireCount + 1));
    });

    public static Task SetUserCreatedAsync(TestApp app, Guid userId, DateTime at) => WithDbAsync(app, db =>
        db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, at)));

    /// <summary>Moves a check to the instant the scenario needs; the check route stamps real time, like the fire route.</summary>
    public static Task SetCheckAsync(TestApp app, Guid checkId, DateTime at) => WithDbAsync(app, db =>
        db.Checks.Where(c => c.Id == checkId).ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, at)));

    /// <summary>A place written straight into the archive, the way the closer writes it (or another process did).</summary>
    public static Task ArchiveAsync(TestApp app, DateOnly sunday, string board, int rank, Guid userId, Guid? postId, int fires) => WithDbAsync(app, async db =>
    {
        var label = new DateTime(sunday.Year, sunday.Month, sunday.Day, 0, 0, 0, DateTimeKind.Utc);
        db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = label, Board = board, Rank = rank, PostId = postId, UserId = userId, Fires = fires });
        await db.SaveChangesAsync();
    });

    public static Task SetPostAsync(TestApp app, Guid postId, DateTime? createdAt = null, int? score = null) => WithDbAsync(app, async db =>
    {
        if (createdAt is DateTime created)
        {
            await db.Posts.Where(p => p.Id == postId).ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, created));
        }

        if (score is int value)
        {
            await db.Posts.Where(p => p.Id == postId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Score, value));
        }
    });

    public static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>GET /api/board, the cache dropped first so the read sees what the test just wrote.</summary>
    public static async Task<JsonElement> BoardAsync(TestApp app, HttpClient? client = null, string? week = null)
    {
        app.Services.GetRequiredService<Board>().Invalidate();
        var response = await (client ?? app.NewClient()).GetAsync("/api/board" + (week is null ? "" : "?week=" + week));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    public static List<Guid> PostIds(JsonElement board, string list) =>
        board.GetProperty(list).EnumerateArray().Select(r => r.GetProperty("post").GetProperty("id").GetGuid()).ToList();

    public static List<(int Rank, int Fires, Guid PostId)> LookRows(JsonElement board, string list) =>
        board.GetProperty(list).EnumerateArray()
            .Select(r => (r.GetProperty("rank").GetInt32(), r.GetProperty("fires").GetInt32(), r.GetProperty("post").GetProperty("id").GetGuid()))
            .ToList();

    public static List<(int Rank, int Fires, string Handle)> PeopleRows(JsonElement board) =>
        board.GetProperty("people").EnumerateArray()
            .Select(r => (r.GetProperty("rank").GetInt32(), r.GetProperty("fires").GetInt32(), r.GetProperty("user").GetProperty("handle").GetString()!))
            .ToList();

    public static Task<int> CloseAsync(TestApp app) => app.Services.GetRequiredService<BoardCloser>().CloseDueWeeksAsync(CancellationToken.None);

    public static async Task<List<WeeklyWinner>> WinnersAsync(TestApp app, DateOnly sunday)
    {
        var label = new DateTime(sunday.Year, sunday.Month, sunday.Day, 0, 0, 0, DateTimeKind.Utc);
        List<WeeklyWinner> rows = [];
        await WithDbAsync(app, async db => rows = await db.WeeklyWinners.Where(w => w.WeekStart == label).OrderBy(w => w.Board).ThenBy(w => w.Rank).ToListAsync());
        return rows;
    }

    public static async Task<List<JsonElement>> BoardRankNotificationsAsync(HttpClient client)
    {
        var notifications = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        return notifications.GetProperty("items").EnumerateArray().Where(n => n.GetProperty("type").GetString() == "board_rank").ToList();
    }
}

/// <summary>The rules, the five boards, the window and the routes, on one app: every test lives in its own week.</summary>
public class BoardTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public BoardTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static DateOnly Sunday(int week) => BoardFixtures.Sunday(week);
    private static DateTime Local(DateOnly date, int hour, int minute = 0) => BoardFixtures.Local(date, hour, minute);
    private Task Fire(Guid postId, Guid firerId, DateTime at) => BoardFixtures.FireAsync(_app, postId, firerId, at);
    private Task<JsonElement> Board(HttpClient? client = null, string? week = null) => BoardFixtures.BoardAsync(_app, client, week);

    /// <summary>A person who has made one ok check: their fires count.</summary>
    private async Task<(HttpClient Client, Guid Id)> CheckerAsync(string handle)
    {
        var (client, id, _) = await _app.NewUserAsync(handle);
        await _app.CheckAsync(client);
        return (client, id);
    }

    [Fact]
    public async Task A_fire_counts_only_from_someone_who_has_made_a_check()
    {
        _app.Clock.Now = BoardFixtures.Midweek(0);
        var (author, _, _) = await _app.NewUserAsync("bd_author0");
        var post = await _app.CheckAndPostAsync(author);
        var (_, checker) = await CheckerAsync("bd_checker0");
        var (_, lurker, _) = await _app.NewUserAsync("bd_lurker0");

        await Fire(post, checker, Local(Sunday(0).AddDays(1), 10));
        await Fire(post, lurker, Local(Sunday(0).AddDays(1), 11));

        var board = await Board();
        Assert.Equal([(1, 1, post)], BoardFixtures.LookRows(board, "looks"));
        Assert.Equal([(1, 1, "bd_author0")], BoardFixtures.PeopleRows(board));
        // "Looks" is what the person posted inside the week; this look was posted at signup time, outside it.
        Assert.Equal(0, board.GetProperty("people")[0].GetProperty("looks").GetInt32());
    }

    [Fact]
    public async Task Board_MinChecksToCount_zero_turns_the_check_rule_off()
    {
        using var app = new TestApp { Settings = new() { ["Board:MinChecksToCount"] = "0" } };
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(0);
        var (author, _, _) = await app.NewUserAsync("bd_author_nc");
        var post = await app.CheckAndPostAsync(author);
        var (_, lurker, _) = await app.NewUserAsync("bd_lurker_nc");
        await BoardFixtures.FireAsync(app, post, lurker, Local(Sunday(0).AddDays(1), 10));

        var board = await BoardFixtures.BoardAsync(app);
        Assert.Equal([(1, 1, post)], BoardFixtures.LookRows(board, "looks"));
    }

    [Fact]
    public async Task A_fire_from_an_account_younger_than_two_days_does_not_count()
    {
        _app.Clock.Now = BoardFixtures.Midweek(1);
        var (author, _, _) = await _app.NewUserAsync("bd_author1");
        var post = await _app.CheckAndPostAsync(author);
        var (_, newbie) = await CheckerAsync("bd_newbie1");
        var (_, settled) = await CheckerAsync("bd_settled1");
        var at = Local(Sunday(1).AddDays(2), 9);
        await BoardFixtures.SetUserCreatedAsync(_app, newbie, at.AddDays(-1));
        await BoardFixtures.SetUserCreatedAsync(_app, settled, at.AddDays(-2));

        await Fire(post, newbie, at);
        await Fire(post, settled, at.AddMinutes(1));

        var board = await Board();
        Assert.Equal([(1, 1, post)], BoardFixtures.LookRows(board, "looks"));
    }

    [Fact]
    public async Task A_fire_on_ones_own_look_does_not_count()
    {
        _app.Clock.Now = BoardFixtures.Midweek(2);
        var (author, authorId, _) = await _app.NewUserAsync("bd_author2");
        var post = await _app.CheckAndPostAsync(author);
        await Fire(post, authorId, Local(Sunday(2).AddDays(1), 10));

        var board = await Board(author);
        Assert.Empty(board.GetProperty("looks").EnumerateArray());
        Assert.Empty(board.GetProperty("people").EnumerateArray());
        Assert.Empty(board.GetProperty("intents").EnumerateObject());
        Assert.False(board.TryGetProperty("me", out _));
    }

    [Fact]
    public async Task Only_the_first_three_fires_from_one_person_on_one_author_count()
    {
        _app.Clock.Now = BoardFixtures.Midweek(3);
        var (author, _, _) = await _app.NewUserAsync("bd_author3");
        var posts = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            posts.Add(await _app.CheckAndPostAsync(author));
        }

        var (_, fan) = await CheckerAsync("bd_fan3");
        var (_, other) = await CheckerAsync("bd_other3");
        var monday = Local(Sunday(3).AddDays(1), 8);
        for (var i = 0; i < 5; i++)
        {
            await Fire(posts[i], fan, monday.AddHours(i));
        }

        // A different person's fire on the fourth look is the fourth look's own.
        await Fire(posts[3], other, monday.AddHours(6));

        var board = await Board();
        var looks = BoardFixtures.LookRows(board, "looks");
        Assert.Equal(4, looks.Count);
        Assert.All(looks, row => Assert.Equal(1, row.Fires));
        Assert.Equal(posts.Take(4).ToHashSet(), looks.Select(r => r.PostId).ToHashSet());
        Assert.DoesNotContain(posts[4], looks.Select(r => r.PostId));
        Assert.Equal([(1, 4, "bd_author3")], BoardFixtures.PeopleRows(board));
    }

    [Fact]
    public async Task Ties_go_to_the_earlier_look()
    {
        _app.Clock.Now = BoardFixtures.Midweek(4);
        var (author, _, _) = await _app.NewUserAsync("bd_author4");
        var newer = await _app.CheckAndPostAsync(author);
        var older = await _app.CheckAndPostAsync(author);
        await BoardFixtures.SetPostAsync(_app, older, createdAt: Local(Sunday(4), 9));
        await BoardFixtures.SetPostAsync(_app, newer, createdAt: Local(Sunday(4), 10));
        var (_, a) = await CheckerAsync("bd_a4");
        var (_, b) = await CheckerAsync("bd_b4");
        var monday = Local(Sunday(4).AddDays(1), 8);
        await Fire(newer, a, monday);
        await Fire(newer, b, monday.AddMinutes(1));
        await Fire(older, a, monday.AddMinutes(2));
        await Fire(older, b, monday.AddMinutes(3));

        var board = await Board();
        Assert.Equal([(1, 2, older), (2, 2, newer)], BoardFixtures.LookRows(board, "looks"));
        // Same score, same fires: the picks board breaks the tie the same way.
        Assert.Equal([older, newer], BoardFixtures.PostIds(board, "picks"));
    }

    [Fact]
    public async Task Intent_boards_exist_only_for_intents_with_a_counted_fire()
    {
        _app.Clock.Now = BoardFixtures.Midweek(5);
        var (author, _, _) = await _app.NewUserAsync("bd_author5");
        var date = await _app.CheckAndPostAsync(author, intent: "Date");
        var office = await _app.CheckAndPostAsync(author, intent: "Office");
        await _app.CheckAndPostAsync(author, intent: "Party");
        var (_, fan) = await CheckerAsync("bd_fan5");
        var monday = Local(Sunday(5).AddDays(1), 8);
        await Fire(date, fan, monday);
        await Fire(office, fan, monday.AddHours(1));

        var board = await Board();
        var intents = board.GetProperty("intents");
        Assert.Equal(["Date", "Office"], intents.EnumerateObject().Select(p => p.Name).Order().ToList());
        Assert.Equal([(1, 1, date)], BoardFixtures.LookRows(intents, "Date"));
        Assert.Equal([(1, 1, office)], BoardFixtures.LookRows(intents, "Office"));
    }

    [Fact]
    public async Task The_picks_board_ranks_by_the_stylists_score_and_fires_only_break_ties()
    {
        _app.Clock.Now = BoardFixtures.Midweek(6);
        var (author, _, _) = await _app.NewUserAsync("bd_author6");
        var nine = await _app.CheckAndPostAsync(author);
        var six = await _app.CheckAndPostAsync(author);
        var eightFired = await _app.CheckAndPostAsync(author);
        var eightQuiet = await _app.CheckAndPostAsync(author);
        var monday = Local(Sunday(6).AddDays(1), 8);
        await BoardFixtures.SetPostAsync(_app, nine, createdAt: monday, score: 9);
        await BoardFixtures.SetPostAsync(_app, six, createdAt: monday, score: 6);
        await BoardFixtures.SetPostAsync(_app, eightQuiet, createdAt: monday, score: 8);
        await BoardFixtures.SetPostAsync(_app, eightFired, createdAt: monday.AddHours(1), score: 8);
        var fans = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            fans.Add((await CheckerAsync($"bd_fan6_{i}")).Id);
        }

        foreach (var fan in fans)
        {
            await Fire(six, fan, monday.AddHours(2));
        }

        await Fire(eightFired, fans[0], monday.AddHours(3));

        var board = await Board();
        Assert.Equal([six, eightFired], BoardFixtures.PostIds(board, "looks"));
        var picks = board.GetProperty("picks").EnumerateArray()
            .Select(r => (r.GetProperty("post").GetProperty("id").GetGuid(), r.GetProperty("score").GetInt32(), r.GetProperty("fires").GetInt32()))
            .ToList();
        Assert.Equal([(nine, 9, 0), (eightFired, 8, 1), (eightQuiet, 8, 0), (six, 6, 3)], picks);
    }

    [Fact]
    public async Task Rising_lists_the_looks_of_accounts_younger_than_thirty_days()
    {
        _app.Clock.Now = BoardFixtures.Midweek(13);
        var (veteran, _, _) = await _app.NewUserAsync("bd_veteran13");
        var (newface, newfaceId, _) = await _app.NewUserAsync("bd_newface13");
        await BoardFixtures.SetUserCreatedAsync(_app, newfaceId, Local(Sunday(13), 0).AddDays(-10));
        var old = await _app.CheckAndPostAsync(veteran);
        var fresh = await _app.CheckAndPostAsync(newface);
        var (_, fan) = await CheckerAsync("bd_fan13");
        var monday = Local(Sunday(13).AddDays(1), 8);
        await Fire(old, fan, monday);
        await Fire(fresh, fan, monday.AddHours(1));

        var board = await Board();
        Assert.Equal(2, BoardFixtures.LookRows(board, "looks").Count);
        Assert.Equal([(1, 1, fresh)], BoardFixtures.LookRows(board, "rising"));
        Assert.Equal("bd_newface13", board.GetProperty("rising")[0].GetProperty("user").GetProperty("handle").GetString());
    }

    [Fact]
    public async Task Hidden_and_excluded_looks_are_on_no_board_and_an_exclusion_can_be_lifted()
    {
        _app.Clock.Now = BoardFixtures.Midweek(7);
        var (author, _, _) = await _app.NewUserAsync("bd_author7");
        var hidden = await _app.CheckAndPostAsync(author);
        var excluded = await _app.CheckAndPostAsync(author);
        var kept = await _app.CheckAndPostAsync(author);
        await BoardFixtures.SetPostAsync(_app, excluded, createdAt: Local(Sunday(7), 9));
        var (_, fan) = await CheckerAsync("bd_fan7");
        var monday = Local(Sunday(7).AddDays(1), 8);
        await Fire(hidden, fan, monday);
        await Fire(excluded, fan, monday.AddHours(1));
        await Fire(kept, fan, monday.AddHours(2));

        var (moderator, moderatorId, _) = await _app.NewUserAsync("bd_mod7");
        await _app.PromoteAsync("bd_mod7");
        Assert.Equal(HttpStatusCode.OK, (await moderator.PostAsync($"/api/admin/posts/{hidden}/hide", null)).StatusCode);

        var created = await moderator.PostAsJsonAsync("/api/admin/board/exclude", new { postId = excluded, reason = "  not an outfit  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var exclusion = await BoardFixtures.Json(created);
        Assert.Equal(excluded, exclusion.GetProperty("postId").GetGuid());
        Assert.Equal("not an outfit", exclusion.GetProperty("reason").GetString());
        Assert.Equal("bd_mod7", exclusion.GetProperty("by").GetProperty("handle").GetString());

        var again = await moderator.PostAsJsonAsync("/api/admin/board/exclude", new { postId = excluded, reason = "twice" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("This look is already off the board.", (await BoardFixtures.Json(again)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.PostAsJsonAsync("/api/admin/board/exclude", new { postId = Guid.NewGuid(), reason = "gone" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await moderator.PostAsJsonAsync("/api/admin/board/exclude", new { reason = "no look" })).StatusCode);

        var board = await Board();
        Assert.Equal([(1, 1, kept)], BoardFixtures.LookRows(board, "looks"));
        Assert.Empty(BoardFixtures.PostIds(board, "picks"));
        Assert.Equal([(1, 1, "bd_author7")], BoardFixtures.PeopleRows(board));
        await BoardFixtures.WithDbAsync(_app, async db => Assert.Equal(moderatorId, (await db.BoardExclusions.SingleAsync(e => e.PostId == excluded)).ByUserId));

        Assert.Equal(HttpStatusCode.NoContent, (await moderator.DeleteAsync($"/api/admin/board/exclude/{excluded}")).StatusCode);
        var lifted = await moderator.DeleteAsync($"/api/admin/board/exclude/{excluded}");
        Assert.Equal(HttpStatusCode.NotFound, lifted.StatusCode);
        Assert.Equal("This look isn't off the board.", (await BoardFixtures.Json(lifted)).GetProperty("error").GetString());

        board = await Board();
        // Both have one fire; the kept look carries its signup-time date and is the earlier of the two.
        Assert.Equal([kept, excluded], BoardFixtures.PostIds(board, "looks"));
        Assert.Equal([excluded], BoardFixtures.PostIds(board, "picks"));
        Assert.DoesNotContain(hidden, BoardFixtures.PostIds(board, "looks"));
    }

    [Fact]
    public async Task The_week_is_cut_at_midnight_in_Jerusalem_and_survives_the_DST_change()
    {
        _app.Clock.Now = BoardFixtures.Midweek(9);
        var (author, _, _) = await _app.NewUserAsync("bd_author9");
        var lastWeek = await _app.CheckAndPostAsync(author);
        var thisWeek = await _app.CheckAndPostAsync(author);
        var (_, fan) = await CheckerAsync("bd_fan9");
        // 23:30 on the last Saturday of week 8, and 00:30 on the first Sunday of week 9: an hour apart, a week apart.
        await Fire(lastWeek, fan, Local(Sunday(9).AddDays(-1), 23, 30));
        await Fire(thisWeek, fan, Local(Sunday(9), 0, 30));

        var board = await Board();
        Assert.Equal(Local(Sunday(9), 0), board.GetProperty("weekStart").GetDateTime());
        Assert.Equal(Local(Sunday(10), 0), board.GetProperty("weekEnd").GetDateTime());
        Assert.Equal(new DateTime(2027, 3, 13, 22, 0, 0, DateTimeKind.Utc), board.GetProperty("weekStart").GetDateTime());
        Assert.Equal((int)Math.Ceiling((Local(Sunday(10), 0) - BoardFixtures.Midweek(9)).TotalSeconds), board.GetProperty("closesIn").GetInt32());
        Assert.False(board.GetProperty("closed").GetBoolean());
        Assert.Equal([thisWeek], BoardFixtures.PostIds(board, "looks"));

        var previous = await Board(week: BoardFixtures.Key(Sunday(8)));
        Assert.Equal([lastWeek], BoardFixtures.PostIds(previous, "looks"));
        Assert.Equal(0, previous.GetProperty("closesIn").GetInt32());
        // Any day of the week names it, and so does the instant a board answered as weekStart.
        Assert.Equal([lastWeek], BoardFixtures.PostIds(await Board(week: BoardFixtures.Key(Sunday(8).AddDays(6))), "looks"));
        Assert.Equal([lastWeek], BoardFixtures.PostIds(await Board(week: Uri.EscapeDataString(Local(Sunday(8), 0).ToString("O"))), "looks"));

        // Week 10 (21-27 March 2027) has the spring change: Friday 02:00 jumps to 03:00, so the week is 167 hours long.
        _app.Clock.Now = BoardFixtures.Midweek(10);
        var dstWeek = await _app.CheckAndPostAsync(author);
        var afterDst = await _app.CheckAndPostAsync(author);
        await Fire(dstWeek, fan, Local(Sunday(11).AddDays(-1), 23, 30));
        await Fire(afterDst, fan, Local(Sunday(11), 0, 30));
        var dst = await Board();
        Assert.Equal(new DateTime(2027, 3, 20, 22, 0, 0, DateTimeKind.Utc), dst.GetProperty("weekStart").GetDateTime());
        Assert.Equal(new DateTime(2027, 3, 27, 21, 0, 0, DateTimeKind.Utc), dst.GetProperty("weekEnd").GetDateTime());
        Assert.Equal(TimeSpan.FromHours(167), dst.GetProperty("weekEnd").GetDateTime() - dst.GetProperty("weekStart").GetDateTime());
        Assert.Equal([dstWeek], BoardFixtures.PostIds(dst, "looks"));
        Assert.Equal([afterDst], BoardFixtures.PostIds(await Board(week: BoardFixtures.Key(Sunday(11))), "looks"));
    }

    [Fact]
    public async Task A_week_that_is_not_a_date_is_refused_in_the_callers_language()
    {
        _app.Clock.Now = BoardFixtures.Midweek(0);
        foreach (var week in new[] { "nope", "2027-01-32", "2027-1-1", "12" })
        {
            var response = await _app.NewClient().GetAsync("/api/board?week=" + week);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("That week doesn't look right.", (await BoardFixtures.Json(response)).GetProperty("error").GetString());
        }

        var hebrew = _app.NewClient();
        hebrew.DefaultRequestHeaders.AcceptLanguage.ParseAdd("he-IL");
        Assert.Equal("השבוע הזה לא נראה נכון.", (await BoardFixtures.Json(await hebrew.GetAsync("/api/board?week=nope"))).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Board_reads_are_counted_for_the_metrics_but_refused_ones_are_not()
    {
        _app.Clock.Now = BoardFixtures.Midweek(0);
        var (moderator, _, _) = await _app.NewUserAsync("bd_metrics_mod");
        await _app.PromoteAsync("bd_metrics_mod");
        var before = (await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social").GetProperty("boardViews").GetInt32();

        await Board();
        await Board(week: BoardFixtures.Key(Sunday(0)));
        Assert.Equal(HttpStatusCode.BadRequest, (await _app.NewClient().GetAsync("/api/board?week=nope")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _app.NewClient().GetAsync("/api/board/hall")).StatusCode);

        var after = (await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social").GetProperty("boardViews").GetInt32();
        Assert.Equal(before + 2, after);
    }

    [Fact]
    public async Task The_board_is_served_from_memory_for_a_minute()
    {
        _app.Clock.Now = BoardFixtures.Midweek(12);
        var (author, _, _) = await _app.NewUserAsync("bd_author12");
        var post = await _app.CheckAndPostAsync(author);
        var (_, a) = await CheckerAsync("bd_a12");
        var (_, b) = await CheckerAsync("bd_b12");
        await Fire(post, a, Local(Sunday(12).AddDays(1), 8));

        var first = await Board();
        Assert.Equal([(1, 1, post)], BoardFixtures.LookRows(first, "looks"));

        await Fire(post, b, Local(Sunday(12).AddDays(1), 9));
        var cached = await BoardFixtures.Json(await _app.NewClient().GetAsync("/api/board"));
        Assert.Equal([(1, 1, post)], BoardFixtures.LookRows(cached, "looks"));

        var fresh = await Board();
        Assert.Equal([(1, 2, post)], BoardFixtures.LookRows(fresh, "looks"));
    }

    [Fact]
    public async Task Size_sponsor_and_the_callers_own_places()
    {
        using var app = new TestApp
        {
            Settings = new()
            {
                ["Board:Size"] = "2",
                ["Board:Sponsor:Name"] = "NEXOR",
                ["Board:Sponsor:Handle"] = "nexor",
                ["Board:Sponsor:PrizeText"] = "A jacket from the new drop",
                ["Board:Sponsor:Url"] = "https://nexor.example/board"
            }
        };
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(0);
        var (author, _, _) = await app.NewUserAsync("bd_author_s");
        var (second, _, _) = await app.NewUserAsync("bd_second_s");
        var top = await app.CheckAndPostAsync(author);
        var middle = await app.CheckAndPostAsync(second);
        var last = await app.CheckAndPostAsync(author);
        var fans = new List<(HttpClient Client, Guid Id)>();
        for (var i = 0; i < 3; i++)
        {
            var (client, id, _) = await app.NewUserAsync($"bd_fan_s{i}");
            await app.CheckAsync(client);
            fans.Add((client, id));
        }

        var monday = Local(Sunday(0).AddDays(1), 8);
        foreach (var (fan, index) in fans.Select((f, i) => (f.Id, i)))
        {
            await BoardFixtures.FireAsync(app, top, fan, monday.AddMinutes(index));
            if (index < 2)
            {
                await BoardFixtures.FireAsync(app, middle, fan, monday.AddMinutes(10 + index));
            }

            if (index < 1)
            {
                await BoardFixtures.FireAsync(app, last, fan, monday.AddMinutes(20 + index));
            }
        }

        var board = await BoardFixtures.BoardAsync(app, author);
        Assert.Equal([top, middle], BoardFixtures.PostIds(board, "looks"));
        Assert.Equal([(1, 4, "bd_author_s"), (2, 2, "bd_second_s")], BoardFixtures.PeopleRows(board));
        Assert.Equal(0, board.GetProperty("people")[0].GetProperty("looks").GetInt32());
        var sponsor = board.GetProperty("sponsor");
        Assert.Equal("NEXOR", sponsor.GetProperty("name").GetString());
        Assert.Equal("nexor", sponsor.GetProperty("handle").GetString());
        Assert.Equal("A jacket from the new drop", sponsor.GetProperty("prizeText").GetString());
        Assert.Equal("https://nexor.example/board", sponsor.GetProperty("url").GetString());
        var me = board.GetProperty("me");
        Assert.Equal(1, me.GetProperty("looks").GetInt32());
        Assert.Equal(1, me.GetProperty("people").GetInt32());
        Assert.Equal(1, me.GetProperty("intent").GetInt32());
        Assert.False(me.TryGetProperty("rising", out _));

        var theirs = await BoardFixtures.BoardAsync(app, second);
        Assert.Equal(2, theirs.GetProperty("me").GetProperty("looks").GetInt32());
        Assert.False((await BoardFixtures.BoardAsync(app, fans[0].Client)).TryGetProperty("me", out _));
        Assert.False((await BoardFixtures.BoardAsync(app)).TryGetProperty("me", out _));
    }

    [Fact]
    public async Task A_week_before_the_first_look_or_beyond_next_week_is_refused_and_the_calendars_edges_are_not_a_crash()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(4);
        var client = app.NewClient();
        async Task<HttpStatusCode> Status(string week) => (await client.GetAsync("/api/board?week=" + Uri.EscapeDataString(week))).StatusCode;

        // Nothing posted yet: this week, last week (the way back from a fresh board) and next week answer; nothing else does.
        Assert.Equal(HttpStatusCode.OK, await Status(BoardFixtures.Key(Sunday(4))));
        Assert.Equal(HttpStatusCode.OK, await Status(BoardFixtures.Key(Sunday(3))));
        Assert.Equal(HttpStatusCode.OK, await Status(BoardFixtures.Key(Sunday(5).AddDays(6))));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(BoardFixtures.Key(Sunday(2))));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(BoardFixtures.Key(Sunday(6))));
        Assert.Equal(HttpStatusCode.BadRequest, await Status("2020-01-05"));

        // The first and the last days of the calendar, as dates and as instants: garbage, in the caller's language, never 500.
        foreach (var week in new[] { "0001-01-01", "0001-01-06", "9999-12-31", "9999-12-26", "0001-01-01T00:00:00Z", "9999-12-31T23:59:59Z", "9999-12-31T23:00:00-05:00", "0001-01-01T01:00:00+05:00" })
        {
            var response = await client.GetAsync("/api/board?week=" + Uri.EscapeDataString(week));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("That week doesn't look right.", (await BoardFixtures.Json(response)).GetProperty("error").GetString());
        }

        // The first look moves the floor back to its week (a picks board can exist without a single fire).
        var (author, authorId, _) = await app.NewUserAsync("bw_author");
        var post = await app.CheckAndPostAsync(author);
        await BoardFixtures.SetPostAsync(app, post, createdAt: Local(Sunday(1), 9));
        Assert.Equal(HttpStatusCode.OK, await Status(BoardFixtures.Key(Sunday(1))));
        Assert.Equal(HttpStatusCode.OK, await Status(BoardFixtures.Key(Sunday(2))));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(BoardFixtures.Key(Sunday(0))));
        Assert.Equal([post], BoardFixtures.PostIds(await BoardFixtures.BoardAsync(app, week: BoardFixtures.Key(Sunday(1))), "picks"));

        // So does an archived week older than any look that is left (the looks of a closed week can be deleted; its places stay).
        await BoardFixtures.ArchiveAsync(app, Sunday(-2), BoardName.Looks, 1, authorId, null, 1);
        Assert.Equal(HttpStatusCode.OK, await Status(BoardFixtures.Key(Sunday(-2))));
        Assert.Equal(HttpStatusCode.OK, await Status(BoardFixtures.Key(Sunday(-1))));
        Assert.Equal(HttpStatusCode.BadRequest, await Status(BoardFixtures.Key(Sunday(-3))));
        Assert.True((await BoardFixtures.BoardAsync(app, week: BoardFixtures.Key(Sunday(-2)))).GetProperty("closed").GetBoolean());
    }

    [Fact]
    public async Task Only_the_running_week_and_the_one_before_it_are_kept_in_memory()
    {
        _app.Clock.Now = BoardFixtures.Midweek(22);
        var board = _app.Services.GetRequiredService<Board>();
        var (author, _, _) = await _app.NewUserAsync("bd_author22");
        var post = await _app.CheckAndPostAsync(author);
        var (_, fan) = await CheckerAsync("bd_fan22");
        var (_, other) = await CheckerAsync("bd_other22");
        await Fire(post, fan, Local(Sunday(20).AddDays(1), 8));
        var client = _app.NewClient();
        async Task<JsonElement> Read(int week) => await BoardFixtures.Json(await client.GetAsync("/api/board?week=" + BoardFixtures.Key(Sunday(week))));

        // Six distinct weeks read, two entries left behind: the running week and last week. Anything older or later is not kept.
        board.Invalidate();
        foreach (var week in new[] { 22, 21, 20, 23, 19, 18, 22, 20 })
        {
            Assert.False((await Read(week)).GetProperty("closed").GetBoolean());
        }

        Assert.Equal(2, board.CachedWeeks);

        // An older week is computed on every read: a fire written after one read shows on the next without a cache drop.
        Assert.Equal([(1, 1, post)], BoardFixtures.LookRows(await Read(20), "looks"));
        await Fire(post, other, Local(Sunday(20).AddDays(1), 9));
        Assert.Equal([(1, 2, post)], BoardFixtures.LookRows(await Read(20), "looks"));
        Assert.Equal(2, board.CachedWeeks);

        // Two weeks on, neither entry is the running week or the one before it: both go with the next read that is kept.
        _app.Clock.Now = BoardFixtures.Midweek(24);
        await Read(24);
        Assert.Equal(1, board.CachedWeeks);
    }

    [Fact]
    public async Task A_check_later_in_the_week_makes_earlier_fires_count_and_one_after_the_weeks_end_does_not()
    {
        _app.Clock.Now = BoardFixtures.Midweek(24);
        var (author, _, _) = await _app.NewUserAsync("bd_author24");
        var post = await _app.CheckAndPostAsync(author);
        var (fan, fanId, _) = await _app.NewUserAsync("bd_fan24");
        await Fire(post, fanId, Local(Sunday(24).AddDays(1), 10));

        // No check yet: the Monday fire is nothing.
        Assert.Empty((await Board()).GetProperty("looks").EnumerateArray());

        // A check on Friday, days after the fire: the fire counts now.
        var check = await _app.CheckAsync(fan);
        await BoardFixtures.SetCheckAsync(_app, check, Local(Sunday(24).AddDays(5), 18));
        Assert.Equal([(1, 1, post)], BoardFixtures.LookRows(await Board(), "looks"));

        // The same check a minute after the week's end: not by the week's end, so the fire is nothing again, read now or later.
        await BoardFixtures.SetCheckAsync(_app, check, Local(Sunday(25), 0, 1));
        Assert.Empty((await Board()).GetProperty("looks").EnumerateArray());
        _app.Clock.Now = BoardFixtures.Midweek(25);
        Assert.Empty((await Board(week: BoardFixtures.Key(Sunday(24)))).GetProperty("looks").EnumerateArray());
    }

    [Fact]
    public async Task An_exclusion_outlives_the_moderator_who_made_it()
    {
        _app.Clock.Now = BoardFixtures.Midweek(26);
        var (author, _, _) = await _app.NewUserAsync("bd_author26");
        var post = await _app.CheckAndPostAsync(author);
        var (_, fan) = await CheckerAsync("bd_fan26");
        await Fire(post, fan, Local(Sunday(26).AddDays(1), 8));
        var (moderator, moderatorId, _) = await _app.NewUserAsync("bd_mod26");
        await _app.PromoteAsync("bd_mod26");
        Assert.Equal(HttpStatusCode.Created, (await moderator.PostAsJsonAsync("/api/admin/board/exclude", new { postId = post, reason = "spam" })).StatusCode);
        Assert.Empty((await Board()).GetProperty("looks").EnumerateArray());

        // The moderator's row goes (what deleting the account does to it): the exclusion stays, unsigned, and the look stays off.
        await BoardFixtures.WithDbAsync(_app, db => db.Users.Where(u => u.Id == moderatorId).ExecuteDeleteAsync());
        await BoardFixtures.WithDbAsync(_app, async db =>
        {
            var exclusion = await db.BoardExclusions.SingleAsync(e => e.PostId == post);
            Assert.Null(exclusion.ByUserId);
            Assert.Equal("spam", exclusion.Reason);
        });
        Assert.Empty((await Board()).GetProperty("looks").EnumerateArray());
        Assert.Empty((await Board()).GetProperty("picks").EnumerateArray());
    }
}

/// <summary>Board:Sponsor:Url reaches the page only as an http(s) link: validated once at start, checked again on the client.</summary>
public class BoardSponsorUrlTests
{
    [Theory]
    [InlineData("https://nexor.example/board", "https://nexor.example/board")]
    [InlineData("http://nexor.example", "http://nexor.example")]
    [InlineData("  https://nexor.example/drop?x=1#top  ", "https://nexor.example/drop?x=1#top")]
    [InlineData("nexor.example", "https://nexor.example")]
    [InlineData("www.nexor.example/drop?x=1", "https://www.nexor.example/drop?x=1")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("JavaScript:alert(1)", null)]
    [InlineData("ftp://nexor.example", null)]
    [InlineData("mailto:hi@nexor.example", null)]
    [InlineData("https://user:pw@nexor.example", null)]
    [InlineData("nexor.example@evil.example", null)]
    [InlineData("https://", null)]
    [InlineData("not a url", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void The_link_is_http_or_https_or_nothing(string? configured, string? expected) =>
        Assert.Equal(expected, BoardSponsorOptions.NormalizeUrl(configured));

    [Fact]
    public async Task A_bare_host_is_https_on_the_board_and_a_bad_scheme_is_dropped()
    {
        using (var app = new TestApp { Settings = new() { ["Board:Sponsor:Name"] = "NEXOR", ["Board:Sponsor:Url"] = "nexor.example" } })
        {
            var sponsor = (await BoardFixtures.BoardAsync(app)).GetProperty("sponsor");
            Assert.Equal("NEXOR", sponsor.GetProperty("name").GetString());
            Assert.Equal("https://nexor.example", sponsor.GetProperty("url").GetString());
            Assert.Equal("https://nexor.example", app.Services.GetRequiredService<Board>().SponsorUrl);
        }

        using (var app = new TestApp { Settings = new() { ["Board:Sponsor:Name"] = "NEXOR", ["Board:Sponsor:Url"] = "javascript:alert(1)" } })
        {
            var sponsor = (await BoardFixtures.BoardAsync(app)).GetProperty("sponsor");
            Assert.Equal("NEXOR", sponsor.GetProperty("name").GetString());
            Assert.False(sponsor.TryGetProperty("url", out _));
            Assert.Null(app.Services.GetRequiredService<Board>().SponsorUrl);
        }
    }
}

/// <summary>Collects what a service logs, so a test can read the closer's own account of a run.</summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines)
        {
            Lines.Add(formatter(state, exception));
        }
    }
}

/// <summary>The closer, the archive, the badge and the hall: each test on a fresh app, since the closer walks every week.</summary>
public class BoardCloserTests
{
    private static DateOnly Sunday(int week) => BoardFixtures.Sunday(week);
    private static DateTime Local(DateOnly date, int hour, int minute = 0) => BoardFixtures.Local(date, hour, minute);

    private static async Task<(HttpClient Client, Guid Id)> CheckerAsync(TestApp app, string handle)
    {
        var (client, id, _) = await app.NewUserAsync(handle);
        await app.CheckAsync(client);
        return (client, id);
    }

    /// <summary>A VAPID key pair so push is on, the same way PushTests make one.</summary>
    private static (string PublicKey, string PrivateKey) VapidKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(true);
        var point = new byte[65];
        point[0] = 0x04;
        p.Q.X!.CopyTo(point, 1);
        p.Q.Y!.CopyTo(point, 33);
        return (PushSender.Base64Url(point), PushSender.Base64Url(p.D!));
    }

    /// <summary>A browser's subscription for one person: the endpoint the recorder will see.</summary>
    private static async Task<string> SubscribeAsync(HttpClient client, string name)
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);
        var point = new byte[65];
        point[0] = 0x04;
        p.Q.X!.CopyTo(point, 1);
        p.Q.Y!.CopyTo(point, 33);
        var endpoint = $"https://push.example.test/send/{name}-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint, p256dh = PushSender.Base64Url(point), auth = PushSender.Base64Url(RandomNumberGenerator.GetBytes(16)) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return endpoint;
    }

    [Fact]
    public async Task The_closer_writes_the_week_once_and_tells_the_top_of_the_looks_board()
    {
        var (publicKey, privateKey) = VapidKeys();
        using var app = new TestApp { PushPublicKey = publicKey, PushPrivateKey = privateKey };
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(0);
        var (first, firstId, _) = await app.NewUserAsync("bc_first");
        var (second, secondId, _) = await app.NewUserAsync("bc_second");
        var winner = await app.CheckAndPostAsync(first);
        var runnerUp = await app.CheckAndPostAsync(second);
        var monday = Local(Sunday(0).AddDays(1), 8);
        await BoardFixtures.SetPostAsync(app, winner, createdAt: monday);
        await BoardFixtures.SetPostAsync(app, runnerUp, createdAt: monday.AddHours(1));
        var endpoint = await SubscribeAsync(first, "bc_first");
        for (var i = 0; i < 3; i++)
        {
            var (_, fan) = await CheckerAsync(app, $"bc_fan{i}");
            await BoardFixtures.FireAsync(app, winner, fan, monday.AddHours(2 + i));
            if (i == 0)
            {
                await BoardFixtures.FireAsync(app, runnerUp, fan, monday.AddHours(6));
            }
        }

        // Still open: nothing to close, and nothing is told.
        Assert.Equal(0, await BoardFixtures.CloseAsync(app));
        Assert.Empty(await BoardFixtures.WinnersAsync(app, Sunday(0)));

        app.Clock.Now = Local(Sunday(1), 0, 3);
        var rows = await BoardFixtures.CloseAsync(app);
        // looks 2 + people 2 + intent:Date 2 + picks 2; nobody is rising.
        Assert.Equal(8, rows);
        var winners = await BoardFixtures.WinnersAsync(app, Sunday(0));
        Assert.Equal(8, winners.Count);
        var looks = winners.Where(w => w.Board == "looks").OrderBy(w => w.Rank).ToList();
        Assert.Equal([(1, winner, firstId, 3), (2, runnerUp, secondId, 1)], looks.Select(w => (w.Rank, w.PostId!.Value, w.UserId, w.Fires)).ToList());
        Assert.All(looks, w => Assert.Null(w.Score));
        var people = winners.Where(w => w.Board == "people").OrderBy(w => w.Rank).ToList();
        Assert.Equal([(1, firstId, 3), (2, secondId, 1)], people.Select(w => (w.Rank, w.UserId, w.Fires)).ToList());
        Assert.All(people, w => Assert.Null(w.PostId));
        Assert.Equal([7, 7], winners.Where(w => w.Board == "picks").Select(w => w.Score).ToList());
        Assert.Contains(winners, w => w.Board == "intent:Date" && w.Rank == 1 && w.PostId == winner);

        var told = await BoardFixtures.BoardRankNotificationsAsync(first);
        Assert.Equal(1, told.Single().GetProperty("rank").GetInt32());
        Assert.Equal("bc_first", told.Single().GetProperty("actorHandle").GetString());
        Assert.Equal(winner, told.Single().GetProperty("postId").GetGuid());
        Assert.Equal(2, (await BoardFixtures.BoardRankNotificationsAsync(second)).Single().GetProperty("rank").GetInt32());
        Assert.Single(await app.PushHandler.WaitForAsync(endpoint));
        // The push's tap lands on the week that closed, not on the new empty one: the instant a week before the send names it.
        var pushUrl = PushSender.UrlFor(new PushJob(firstId, NotificationType.BoardRank, "bc_first", winner, null, null, 1), app.Clock.Now);
        Assert.StartsWith("/#/board?week=", pushUrl);
        var landed = await BoardFixtures.BoardAsync(app, first, pushUrl["/#/board?week=".Length..]);
        Assert.True(landed.GetProperty("closed").GetBoolean());
        Assert.Equal(Local(Sunday(0), 0), landed.GetProperty("weekStart").GetDateTime());
        Assert.Equal(1, landed.GetProperty("me").GetProperty("looks").GetInt32());

        // A second run finds the week closed: no rows, no second notification.
        Assert.Equal(0, await BoardFixtures.CloseAsync(app));
        Assert.Equal(8, (await BoardFixtures.WinnersAsync(app, Sunday(0))).Count);
        Assert.Single(await BoardFixtures.BoardRankNotificationsAsync(first));

        // The closed week answers from the archive; the running week is empty.
        var archived = await BoardFixtures.BoardAsync(app, first, BoardFixtures.Key(Sunday(0)));
        Assert.True(archived.GetProperty("closed").GetBoolean());
        Assert.Equal(0, archived.GetProperty("closesIn").GetInt32());
        Assert.Equal([(1, 3, winner), (2, 1, runnerUp)], BoardFixtures.LookRows(archived, "looks"));
        Assert.Equal([(1, 3, "bc_first"), (2, 1, "bc_second")], BoardFixtures.PeopleRows(archived));
        Assert.Equal(1, archived.GetProperty("people")[0].GetProperty("looks").GetInt32());
        Assert.Equal(7, archived.GetProperty("picks")[0].GetProperty("score").GetInt32());
        Assert.Equal(1, archived.GetProperty("me").GetProperty("looks").GetInt32());
        var running = await BoardFixtures.BoardAsync(app);
        Assert.False(running.GetProperty("closed").GetBoolean());
        Assert.Empty(running.GetProperty("looks").EnumerateArray());
        Assert.Equal(Local(Sunday(1), 0), running.GetProperty("weekStart").GetDateTime());
    }

    [Fact]
    public async Task A_week_another_run_closed_first_stands_as_that_run_wrote_it()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(0);
        var (author, _, _) = await app.NewUserAsync("bc_race_author");
        var post = await app.CheckAndPostAsync(author);
        var (_, fan) = await CheckerAsync(app, "bc_race_fan");
        await BoardFixtures.FireAsync(app, post, fan, Local(Sunday(0).AddDays(1), 8));
        var (_, otherId, _) = await app.NewUserAsync("bc_race_other");

        app.Clock.Now = Local(Sunday(1), 0, 3);
        var log = new RecordingLogger<BoardCloser>();
        var closer = new BoardCloser(app.Services.GetRequiredService<IServiceScopeFactory>(), app.Services.GetRequiredService<Board>(), app.Clock, log);
        // The other process wins the race: its first place lands between this run's check of the week and its save.
        var interposed = 0;
        closer.BeforeSave = async (week, _) =>
        {
            interposed++;
            await BoardFixtures.ArchiveAsync(app, week.FirstDay, BoardName.Looks, 1, otherId, post, 99);
        };

        Assert.Equal(0, await closer.CloseDueWeeksAsync(CancellationToken.None));
        Assert.Equal(1, interposed);
        Assert.Contains("Board: week 2027-01-10 was already closed by another run; nothing written", log.Lines);
        Assert.DoesNotContain(log.Lines, line => line.Contains("closed,"));
        var only = Assert.Single(await BoardFixtures.WinnersAsync(app, Sunday(0)));
        Assert.Equal((otherId, 99), (only.UserId, only.Fires));
        // The notification queued next to the dropped rows went with them; the other run's rows stand.
        Assert.Empty(await BoardFixtures.BoardRankNotificationsAsync(author));

        closer.BeforeSave = null;
        Assert.Equal(0, await closer.CloseDueWeeksAsync(CancellationToken.None));
        Assert.Equal(1, interposed);
        Assert.Single(await BoardFixtures.WinnersAsync(app, Sunday(0)));
    }

    [Fact]
    public async Task The_closer_catches_up_after_downtime_and_only_the_last_week_is_told()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(0);
        var (early, _, _) = await app.NewUserAsync("bc_early");
        var (late, _, _) = await app.NewUserAsync("bc_late");
        var (quiet, _, _) = await app.NewUserAsync("bc_quiet");
        var earlyPost = await app.CheckAndPostAsync(early);
        var latePost = await app.CheckAndPostAsync(late);
        var quietPost = await app.CheckAndPostAsync(quiet);
        var (_, fan) = await CheckerAsync(app, "bc_fan");
        var (_, lurker, _) = await app.NewUserAsync("bc_lurker");
        await BoardFixtures.FireAsync(app, earlyPost, fan, Local(Sunday(0).AddDays(1), 8));
        // Week 1 has a fire that does not count (no check behind it); week 2 has one that does.
        await BoardFixtures.FireAsync(app, quietPost, lurker, Local(Sunday(1).AddDays(1), 8));
        await BoardFixtures.FireAsync(app, latePost, fan, Local(Sunday(2).AddDays(1), 8));

        app.Clock.Now = BoardFixtures.Midweek(3);
        var rows = await BoardFixtures.CloseAsync(app);
        Assert.Equal(6, rows);
        Assert.Equal(3, (await BoardFixtures.WinnersAsync(app, Sunday(0))).Count);
        Assert.Empty(await BoardFixtures.WinnersAsync(app, Sunday(1)));
        Assert.Equal(3, (await BoardFixtures.WinnersAsync(app, Sunday(2))).Count);
        Assert.Empty(await BoardFixtures.BoardRankNotificationsAsync(early));
        Assert.Empty(await BoardFixtures.BoardRankNotificationsAsync(quiet));
        Assert.Equal(1, (await BoardFixtures.BoardRankNotificationsAsync(late)).Single().GetProperty("rank").GetInt32());
        Assert.Equal(0, await BoardFixtures.CloseAsync(app));

        // The empty week stays computed and open-looking; the hall lists the closed ones, newest first.
        var empty = await BoardFixtures.BoardAsync(app, week: BoardFixtures.Key(Sunday(1)));
        Assert.False(empty.GetProperty("closed").GetBoolean());
        Assert.Empty(empty.GetProperty("looks").EnumerateArray());

        var hall = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/board/hall");
        var weeks = hall.GetProperty("weeks").EnumerateArray().ToList();
        Assert.Equal([Local(Sunday(2), 0), Local(Sunday(0), 0)], weeks.Select(w => w.GetProperty("weekStart").GetDateTime()).ToList());
        Assert.Equal(Local(Sunday(3), 0), weeks[0].GetProperty("weekEnd").GetDateTime());
        var lateWinners = weeks[0].GetProperty("winners").EnumerateArray().ToList();
        Assert.Equal(["looks", "people", "intent:Date"], lateWinners.Select(w => w.GetProperty("board").GetString()).ToList());
        var place = lateWinners[0];
        Assert.Equal(1, place.GetProperty("rank").GetInt32());
        Assert.Equal("bc_late", place.GetProperty("user").GetProperty("handle").GetString());
        Assert.Equal(latePost, place.GetProperty("postId").GetGuid());
        Assert.Equal($"/api/posts/{latePost}/image", place.GetProperty("imageUrl").GetString());
        Assert.Equal(1, place.GetProperty("fires").GetInt32());
        Assert.False(lateWinners[1].TryGetProperty("postId", out _));
    }

    [Fact]
    public async Task The_badge_is_worn_the_week_after_a_top_three_finish_and_then_comes_off()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(0);
        var authors = new List<(HttpClient Client, string Handle, Guid Post)>();
        for (var i = 1; i <= 4; i++)
        {
            var (client, _, handle) = await app.NewUserAsync($"bc_badge{i}");
            authors.Add((client, handle, await app.CheckAndPostAsync(client)));
        }

        var fans = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            fans.Add((await CheckerAsync(app, $"bc_bfan{i}")).Id);
        }

        // Places 1..4: four, three, two and one fire.
        var monday = Local(Sunday(0).AddDays(1), 8);
        for (var place = 0; place < 4; place++)
        {
            for (var fan = 0; fan < 4 - place; fan++)
            {
                await BoardFixtures.FireAsync(app, authors[place].Post, fans[fan], monday.AddMinutes(place * 10 + fan));
            }
        }

        Assert.False((await authors[0].Client.GetFromJsonAsync<JsonElement>("/api/auth/me")).TryGetProperty("badge", out _));

        app.Clock.Now = Local(Sunday(1), 9);
        Assert.True(await BoardFixtures.CloseAsync(app) > 0);
        var me = await authors[0].Client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var badge = me.GetProperty("badge");
        Assert.Equal("looks", badge.GetProperty("board").GetString());
        Assert.Equal(1, badge.GetProperty("rank").GetInt32());
        Assert.Equal(Local(Sunday(0), 0), badge.GetProperty("weekStart").GetDateTime());
        var profile = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/bc_badge2");
        Assert.Equal(2, profile.GetProperty("badge").GetProperty("rank").GetInt32());
        Assert.Equal(3, (await app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/bc_badge3")).GetProperty("badge").GetProperty("rank").GetInt32());
        Assert.False((await app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/bc_badge4")).TryGetProperty("badge", out _));
        Assert.False((await authors[3].Client.GetFromJsonAsync<JsonElement>("/api/auth/me")).TryGetProperty("badge", out _));

        // The week after, the badge is gone (week 1 had no fires, so nothing replaces it).
        app.Clock.Now = BoardFixtures.Midweek(2);
        await BoardFixtures.CloseAsync(app);
        Assert.False((await authors[0].Client.GetFromJsonAsync<JsonElement>("/api/auth/me")).TryGetProperty("badge", out _));
        Assert.False((await app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/bc_badge2")).TryGetProperty("badge", out _));
    }

    [Fact]
    public async Task A_week_with_no_counted_fires_writes_nothing()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(0);
        var (author, _, _) = await app.NewUserAsync("bc_nf_author");
        var post = await app.CheckAndPostAsync(author);
        await BoardFixtures.SetPostAsync(app, post, createdAt: Local(Sunday(0).AddDays(1), 8));
        var (_, lurker, _) = await app.NewUserAsync("bc_nf_lurker");
        await BoardFixtures.FireAsync(app, post, lurker, Local(Sunday(0).AddDays(1), 9));

        app.Clock.Now = BoardFixtures.Midweek(1);
        Assert.Equal(0, await BoardFixtures.CloseAsync(app));
        await BoardFixtures.WithDbAsync(app, async db => Assert.Empty(await db.WeeklyWinners.ToListAsync()));
        Assert.Empty(await BoardFixtures.BoardRankNotificationsAsync(author));
        Assert.Empty((await app.NewClient().GetFromJsonAsync<JsonElement>("/api/board/hall")).GetProperty("weeks").EnumerateArray());

        // The picks board of that week still computes from the looks posted in it; only the archive stays empty.
        var week = await BoardFixtures.BoardAsync(app, week: BoardFixtures.Key(Sunday(0)));
        Assert.False(week.GetProperty("closed").GetBoolean());
        Assert.Equal([post], BoardFixtures.PostIds(week, "picks"));
    }

    [Fact]
    public async Task The_hall_lists_the_newest_twelve_closed_weeks_with_places_in_board_order()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();
        var (owner, ownerId, _) = await app.NewUserAsync("bc_hall");
        var post = await app.CheckAndPostAsync(owner);
        var gone = await app.CheckAndPostAsync(owner);
        await BoardFixtures.WithDbAsync(app, async db =>
        {
            for (var week = 0; week < 14; week++)
            {
                var sunday = Sunday(week);
                var label = new DateTime(sunday.Year, sunday.Month, sunday.Day, 0, 0, 0, DateTimeKind.Utc);
                db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = label, Board = BoardName.Picks, Rank = 1, PostId = post, UserId = ownerId, Fires = 0, Score = 7 });
                db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = label, Board = BoardName.Looks, Rank = 2, PostId = gone, UserId = ownerId, Fires = 1 });
                db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = label, Board = BoardName.Looks, Rank = 1, PostId = post, UserId = ownerId, Fires = 5 });
                db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = label, Board = BoardName.Intent(StyleIntent.Date), Rank = 1, PostId = post, UserId = ownerId, Fires = 5 });
                db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = label, Board = BoardName.People, Rank = 1, UserId = ownerId, Fires = 6 });
            }

            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/posts/{gone}")).StatusCode);

        var hall = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/board/hall");
        var weeks = hall.GetProperty("weeks").EnumerateArray().ToList();
        Assert.Equal(12, weeks.Count);
        Assert.Equal(Local(Sunday(13), 0), weeks[0].GetProperty("weekStart").GetDateTime());
        Assert.Equal(Local(Sunday(14), 0), weeks[0].GetProperty("weekEnd").GetDateTime());
        Assert.Equal(Local(Sunday(2), 0), weeks[11].GetProperty("weekStart").GetDateTime());

        var winners = weeks[0].GetProperty("winners").EnumerateArray().ToList();
        Assert.Equal(["looks", "looks", "people", "intent:Date", "picks"], winners.Select(w => w.GetProperty("board").GetString()).ToList());
        Assert.Equal([1, 2, 1, 1, 1], winners.Select(w => w.GetProperty("rank").GetInt32()).ToList());
        Assert.Equal($"/api/posts/{post}/image", winners[0].GetProperty("imageUrl").GetString());
        // The deleted look's place stands, without a look or a photo.
        Assert.False(winners[1].TryGetProperty("postId", out _));
        Assert.False(winners[1].TryGetProperty("imageUrl", out _));
        Assert.Equal("bc_hall", winners[1].GetProperty("user").GetProperty("handle").GetString());
        Assert.Equal(7, winners[4].GetProperty("score").GetInt32());
        Assert.All(winners.Take(4), w => Assert.False(w.TryGetProperty("score", out _)));
    }
}
