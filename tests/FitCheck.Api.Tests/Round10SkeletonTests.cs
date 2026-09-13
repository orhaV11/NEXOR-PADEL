using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Tests;

/// <summary>
/// What the Round 10 skeleton enforces on its own, before the builders: the migration over a Round 9 file (items keep
/// their rows and get ids), the item search over migrated rows, the 501 stubs behind the right gates, the options and
/// their defaults, the clock seam, the counters, the cascades and the notification kind.
/// </summary>
public class Round10MigrationTests : IDisposable
{
    private const string Round9 = "20260908134015_Round9";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fitcheck-tests", "r10-" + Guid.NewGuid().ToString("N"));

    public Round10MigrationTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void The_round10_migration_keeps_round9_items_and_gives_each_its_own_id_source_and_order()
    {
        var path = Path.Combine(_root, "round9.db");
        var userId = Guid.NewGuid();
        var (post1, post2) = (Guid.NewGuid(), Guid.NewGuid());
        SeedRound9(path, userId, post1, post2);
        Assert.DoesNotContain("Id", Columns(path, "PostItems"));

        using (var db = Open(path))
        {
            DatabaseSetup.Apply(db, NullLogger.Instance);

            var items = db.PostItems.OrderBy(i => i.PostId).ThenBy(i => i.Position).ToList();
            Assert.Equal(3, items.Count);
            Assert.All(items, i => Assert.NotEqual(Guid.Empty, i.Id));
            Assert.Equal(3, items.Select(i => i.Id).Distinct().Count());
            Assert.All(items, i => Assert.Equal(ItemSource.Stylist, i.Source));
            Assert.All(items, i => Assert.Null(i.Brand));
            Assert.All(items, i => Assert.False(i.Confirmed));
            // The two items of the first look keep the order they were written in; the second look's one item is first.
            var first = items.Where(i => i.PostId == post1).OrderBy(i => i.Position).Select(i => (i.Name, i.Position)).ToList();
            Assert.Equal([("black boots", 0), ("wool coat", 1)], first);
            Assert.Equal([("silk scarf", 0)], items.Where(i => i.PostId == post2).Select(i => (i.Name, i.Position)).ToList());
            Assert.Equal("shoes", items.Single(i => i.Name == "black boots").Category);
            Assert.Equal(db.Database.GetMigrations().OrderBy(m => m), db.Database.GetAppliedMigrations().OrderBy(m => m));

            // The minted id is one EF can look up: the provider binds a Guid as upper-case text and SQLite compares text
            // exactly, so a lower-case id in the file would be a row no key lookup ever finds.
            var boots = items.Single(i => i.Name == "black boots");
            Assert.NotNull(db.PostItems.AsNoTracking().SingleOrDefault(i => i.Id == boots.Id));
            Assert.Equal(boots.Id.ToString().ToUpperInvariant(), Scalar(path, "SELECT \"Id\" FROM \"PostItems\" WHERE \"Name\" = 'black boots'"));
        }

        // The upgraded file has the shape a fresh one gets: the same columns, keys and indexes on every table.
        Assert.Equal(StructureOf(Fresh("reference.db")), StructureOf(path));
        Assert.Contains("IX_PostItems_Name", Names(path, "index"));
        Assert.Contains("IX_PostItems_Brand", Names(path, "index"));
        Assert.Contains("IX_WeeklyWinners_WeekStart_Board_Rank", Names(path, "index"));
        Assert.Contains("IX_Fires_CreatedAt", Names(path, "index"));
        Assert.Contains("Rank", Columns(path, "Notifications"));
    }

    [Fact]
    public async Task The_app_starts_on_a_round9_database_and_the_item_search_still_finds_the_migrated_looks()
    {
        using var app = new TestApp();
        Directory.CreateDirectory(app.Root);
        var (post1, post2) = (Guid.NewGuid(), Guid.NewGuid());
        SeedRound9(app.DatabasePath, Guid.NewGuid(), post1, post2, password: "password123");

        var client = app.NewClient();
        var search = await client.GetFromJsonAsync<JsonElement>("/api/search?q=boots");
        var found = search.GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([post1], found);

        var scarf = await client.GetFromJsonAsync<JsonElement>("/api/search?q=scarf");
        Assert.Equal([post2], scarf.GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList());

        // The veteran signs in, the file is at the current schema, nothing was copied aside (a migrated file needs no backup).
        var login = await client.PostAsJsonAsync("/api/auth/login", new { handle = "veteran", password = "password123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Id", Columns(app.DatabasePath, "PostItems"));
        Assert.Empty(Directory.GetFiles(app.Root, "test.db.bak-*"));

        // A migrated row is one the veteran can tag by its id and one the out door finds: both go through the key.
        var look = await client.GetFromJsonAsync<JsonElement>($"/api/posts/{post1}");
        var boots = look.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "black boots").GetProperty("id").GetGuid();
        var tagged = await ItemsTests.PatchItemsAsync(client, post1, new object[] { new { id = boots, brand = "Dr. Martens", url = "https://shop.example/boots" } });
        Assert.Equal(HttpStatusCode.OK, tagged.StatusCode);
        var row = Assert.Single((await tagged.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal(boots, row.GetProperty("id").GetGuid());
        Assert.Equal("Stylist", row.GetProperty("source").GetString());
        var door = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Found, (await door.GetAsync($"/api/items/{boots}/out")).StatusCode);
    }

    /// <summary>A file exactly as Round 9 left it: migrated to Round9, one person, two posted looks, three stylist items keyed on (PostId, Name).</summary>
    private static void SeedRound9(string path, Guid userId, Guid post1, Guid post2, string? password = null)
    {
        using (var db = Open(path))
        {
            db.GetService<IMigrator>().Migrate(Round9);
            // Users, Checks and Posts have the same columns in Round 9 and Round 10, so the current model writes them as they were.
            db.Users.Add(NewUser(userId, "veteran", password));
            var (check1, check2) = (Guid.NewGuid(), Guid.NewGuid());
            db.Checks.Add(NewCheck(check1, userId));
            db.Checks.Add(NewCheck(check2, userId));
            db.Posts.Add(NewPost(post1, userId, check1, DateTime.UtcNow.AddMinutes(-2)));
            db.Posts.Add(NewPost(post2, userId, check2, DateTime.UtcNow.AddMinutes(-1)));
            db.SaveChanges();
        }

        // The items as Round 9 wrote them: no Id column exists yet, so raw SQL, the post id bound the way EF binds a Guid.
        const string insert = "INSERT INTO \"PostItems\" (\"PostId\", \"Name\", \"Category\") VALUES ($post, $name, $category)";
        Execute(path, insert, ("$post", post1), ("$name", "black boots"), ("$category", "shoes"));
        Execute(path, insert, ("$post", post1), ("$name", "wool coat"), ("$category", "outerwear"));
        Execute(path, insert, ("$post", post2), ("$name", "silk scarf"), ("$category", "accessory"));
        SqliteConnection.ClearAllPools();
    }

    private string Fresh(string name)
    {
        var path = Path.Combine(_root, name);
        using var db = Open(path);
        db.Database.Migrate();
        return path;
    }

    private static AppDbContext Open(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);

    private static AppUser NewUser(Guid id, string handle, string? password)
    {
        var user = new AppUser
        {
            Id = id,
            Handle = handle,
            HandleLower = handle.ToLowerInvariant(),
            PasswordHash = "x",
            PreferredLanguage = "en",
            Confirmed16Plus = true,
            CreatedAt = DateTime.UtcNow,
        };
        if (password is not null)
        {
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, password);
        }

        return user;
    }

    private static OutfitCheck NewCheck(Guid id, Guid userId) => new()
    {
        Id = id, UserId = userId, Intent = StyleIntent.Casual, Language = "en", ImagePath = "legacy/photo.jpg", Status = CheckStatus.Ok, Score = 7,
        PromptVersion = "v2", CreatedAt = DateTime.UtcNow.AddMinutes(-3)
    };

    private static Post NewPost(Guid id, Guid userId, Guid checkId, DateTime createdAt) => new()
    {
        Id = id, UserId = userId, CheckId = checkId, Intent = StyleIntent.Casual, Score = 7, IntentMatch = 70, Headline = "Seeded", CreatedAt = createdAt
    };

    /// <summary>Tables with their columns (name, type, NOT NULL, primary key) and indexes with their statements: the shape, as DatabaseSetupTests compares it.</summary>
    private static List<string> StructureOf(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 'table ' || m.name || ' ' || c.name || ' ' || c.type || ' ' || c.\"notnull\" || ' ' || c.pk " +
            "FROM sqlite_master m JOIN pragma_table_info(m.name) c WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite_%' AND m.name <> $history " +
            "UNION ALL SELECT 'index ' || name || ' ' || IFNULL(sql, '') FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%' " +
            "ORDER BY 1";
        command.Parameters.AddWithValue("$history", HistoryRepository.DefaultTableName);
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static List<string> Names(string path, string type)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type";
        command.Parameters.AddWithValue("$type", type);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static List<string> Columns(string path, string table)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($table)";
        command.Parameters.AddWithValue("$table", table);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
    }

    private static void Execute(string path, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: temp folders are not worth a failing test.
        }
    }
}

/// <summary>The gates the skeleton mapped stay in front of the routes the builders filled in.</summary>
public class Round10StubTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public Round10StubTests(TestApp app)
    {
        _app = app;
    }

    // Every public route of the skeleton is built now (ItemsTests, BoardTests); the 501 theory is gone with the stubs.

    [Fact]
    public async Task Tagging_items_needs_a_session_then_a_look_of_ones_own()
    {
        // Built by the items builder (ItemsTests); the gate the skeleton mapped stays: a session first, then the look.
        var postId = Guid.NewGuid();
        var body = new { items = new[] { new { name = "black boots", category = "shoes", brand = "Nike" } } };
        var anonymous = await _app.NewClient().PatchAsJsonAsync($"/api/posts/{postId}/items", body);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var (client, _, _) = await _app.NewUserAsync("r10_tagger");
        var response = await client.PatchAsJsonAsync($"/api/posts/{postId}/items", body);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Board_exclusion_is_for_moderators()
    {
        var postId = Guid.NewGuid();
        var anonymous = await _app.NewClient().PostAsJsonAsync("/api/admin/board/exclude", new { postId, reason = "spam" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var (person, _, _) = await _app.NewUserAsync("r10_person");
        Assert.Equal(HttpStatusCode.Forbidden, (await person.PostAsJsonAsync("/api/admin/board/exclude", new { postId, reason = "spam" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await person.DeleteAsync($"/api/admin/board/exclude/{postId}")).StatusCode);

        // Past the gate the route is built (BoardTests): a look that does not exist is 404, one that was never off is 404.
        var (moderator, _, _) = await _app.NewUserAsync("r10_mod");
        Assert.Equal(AdminChange.Changed, await _app.PromoteAsync("r10_mod"));
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.PostAsJsonAsync("/api/admin/board/exclude", new { postId, reason = "spam" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.DeleteAsync($"/api/admin/board/exclude/{postId}")).StatusCode);
    }
}

/// <summary>The options, the clock, the counters, the cascades and the notification kind the skeleton adds.</summary>
public class Round10SeamTests
{
    [Fact]
    public void Board_and_affiliate_options_bind_with_the_plans_defaults()
    {
        using var app = new TestApp();
        var board = app.Services.GetRequiredService<IOptions<BoardOptions>>().Value;
        Assert.Equal(DayOfWeek.Sunday, board.WeekStartsOn);
        Assert.Equal("Asia/Jerusalem", board.TimeZone);
        Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(board.TimeZone));
        Assert.Equal(1, board.MinChecksToCount);
        Assert.Equal(3, board.MaxPerFirerPerAuthor);
        Assert.Equal(2, board.NewAccountDays);
        Assert.Equal(10, board.Size);
        Assert.Equal(30, board.RisingDays);
        Assert.Null(board.Sponsor);

        var affiliate = app.Services.GetRequiredService<IOptions<AffiliateOptions>>().Value;
        Assert.True(affiliate.Disclosure);
        Assert.Empty(affiliate.Hosts);
        Assert.Null(affiliate.ParametersFor("amazon.com"));

        // Real time until a test sets it.
        var clock = app.Services.GetRequiredService<IClock>();
        Assert.InRange(clock.UtcNow, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public void Board_and_affiliate_options_take_overrides_and_the_clock_can_be_set()
    {
        using var app = new TestApp
        {
            Settings =
            {
                ["Board:TimeZone"] = "Europe/London",
                ["Board:WeekStartsOn"] = "Monday",
                ["Board:Size"] = "5",
                ["Board:MaxPerFirerPerAuthor"] = "1",
                ["Board:Sponsor:Name"] = "NEXOR",
                ["Board:Sponsor:Handle"] = "nexor",
                ["Board:Sponsor:PrizeText"] = "A jacket from the new drop",
                ["Board:Sponsor:Url"] = "https://nexor.example/board",
                ["Affiliate:Disclosure"] = "false",
                ["Affiliate:Hosts:amazon.com"] = "tag=orevosh-20",
                ["Affiliate:Hosts:Zara.com"] = "?utm_source=orevosh",
            }
        };

        var board = app.Services.GetRequiredService<IOptions<BoardOptions>>().Value;
        Assert.Equal("Europe/London", board.TimeZone);
        Assert.Equal(DayOfWeek.Monday, board.WeekStartsOn);
        Assert.Equal(5, board.Size);
        Assert.Equal(1, board.MaxPerFirerPerAuthor);
        Assert.Equal(2, board.NewAccountDays);
        var sponsor = Assert.IsType<BoardSponsorOptions>(board.Sponsor);
        Assert.True(sponsor.Enabled);
        Assert.Equal("NEXOR", sponsor.Name);
        Assert.Equal("nexor", sponsor.Handle);
        Assert.Equal("A jacket from the new drop", sponsor.PrizeText);

        var affiliate = app.Services.GetRequiredService<IOptions<AffiliateOptions>>().Value;
        Assert.False(affiliate.Disclosure);
        Assert.Equal("tag=orevosh-20", affiliate.ParametersFor("amazon.com"));
        Assert.Equal("tag=orevosh-20", affiliate.ParametersFor("www.amazon.com"));
        Assert.Equal("tag=orevosh-20", affiliate.ParametersFor("smile.Amazon.com"));
        Assert.Equal("utm_source=orevosh", affiliate.ParametersFor("zara.com"));
        Assert.Null(affiliate.ParametersFor("notamazon.com"));
        Assert.Null(affiliate.ParametersFor("amazon.com.evil.example"));
        Assert.Null(affiliate.ParametersFor(""));

        var moment = new DateTime(2026, 9, 12, 20, 59, 0, DateTimeKind.Utc);
        app.Clock.Now = moment;
        Assert.Equal(moment, app.Services.GetRequiredService<IClock>().UtcNow);
        app.Clock.Now = null;
        Assert.InRange(app.Services.GetRequiredService<IClock>().UtcNow, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Theory]
    [InlineData("https://www.zara.com/il/en/coat-p123.html", true, "zara.com")]
    [InlineData("http://shop.example/item", true, "shop.example")]
    [InlineData("HTTPS://NEXOR.EXAMPLE/x", true, "nexor.example")]
    [InlineData("https://www.terminalx.com/search?q=נעליים", true, "terminalx.com")]
    [InlineData("https://חנות.co.il/x", true, "חנות.co.il")]
    [InlineData("javascript:alert(1)", false, null)]
    [InlineData("data:text/html;base64,AAAA", false, null)]
    [InlineData("ftp://files.example/x", false, null)]
    [InlineData("/relative/path", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void A_store_link_is_http_or_https_and_nothing_else(string? url, bool ok, string? host)
    {
        Assert.Equal(ok, PostItems.IsStoreUrl(url));
        Assert.Equal(host, PostItems.HostOf(url));
        Assert.False(PostItems.IsStoreUrl("https://" + new string('a', 500) + ".example"));
    }

    [Theory]
    [InlineData("https://www.Example.com/p?x=1#f", "https://www.Example.com/p?x=1#f")]
    [InlineData("https://www.terminalx.com/search?q=נעליים", "https://www.terminalx.com/search?q=%D7%A0%D7%A2%D7%9C%D7%99%D7%99%D7%9D")]
    [InlineData("https://shop.example/été#top", "https://shop.example/%C3%A9t%C3%A9#top")]
    [InlineData("https://חנות.co.il/x?a=1", "https://xn--9dbd1a4b.co.il/x?a=1")]
    [InlineData("http://Shop.Example:8080/été", "http://shop.example:8080/%C3%A9t%C3%A9")]
    [InlineData("https://[::1]:8443/été", "https://[::1]:8443/%C3%A9t%C3%A9")]
    public void A_store_link_leaves_in_a_form_a_header_carries_and_an_ascii_one_as_it_was(string stored, string ascii)
    {
        Assert.Equal(ascii, PostItems.AsciiUrl(stored));
        Assert.All(PostItems.AsciiUrl(stored), c => Assert.InRange(c, ' ', '~'));
        // The affiliate parameters go onto the ASCII form, after its query and before its fragment.
        var hash = ascii.IndexOf('#');
        var head = hash < 0 ? ascii : ascii[..hash];
        var fragment = hash < 0 ? "" : ascii[hash..];
        Assert.Equal(head + (head.Contains('?') ? "&" : "?") + "tag=1" + fragment, PostItems.OutUrl(stored, "tag=1"));
    }

    [Fact]
    public async Task Counters_survive_in_the_database_and_the_metrics_read_them()
    {
        using var app = new TestApp();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await Counters.ReadAsync(db, CounterName.ItemOuts, CancellationToken.None));
            await Counters.IncrementAsync(db, CounterName.ItemOuts, CancellationToken.None);
            await Counters.IncrementAsync(db, CounterName.ItemOuts, CancellationToken.None);
            await Counters.IncrementAsync(db, CounterName.BoardViews, CancellationToken.None, by: 5);
            Assert.Equal(2, await Counters.ReadAsync(db, CounterName.ItemOuts, CancellationToken.None));
            Assert.Equal(5, await Counters.ReadAsync(db, CounterName.BoardViews, CancellationToken.None));
        }

        var (moderator, _, _) = await app.NewUserAsync("r10_metrics_mod");
        await app.PromoteAsync("r10_metrics_mod");
        var metrics = await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");
        var social = metrics.GetProperty("social");
        Assert.Equal(2, social.GetProperty("itemOuts").GetInt32());
        Assert.Equal(5, social.GetProperty("boardViews").GetInt32());
        Assert.Equal(0, social.GetProperty("itemsTagged").GetInt32());
    }

    [Fact]
    public async Task Posting_writes_stylist_items_with_ids_order_and_no_brand_and_the_check_carries_brand_seen()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Parse("""
            { "status": "ok", "score": 7, "intent_match": 70, "headline": "Seeded", "vibe": "seeded",
              "items": [
                { "name": "White tee", "category": "top", "verdict": "works", "note": "", "brand_seen": null },
                { "name": "Dark jeans", "category": "bottom", "verdict": "neutral", "note": "" },
                { "name": "Running shoes", "category": "shoes", "verdict": "weak", "note": "", "brand_seen": "Nike" }
              ],
              "working": ["Seeded"], "one_tip": "Seeded." }
            """);
        var (client, _, _) = await app.NewUserAsync("r10_poster");
        var checkId = await app.CheckAsync(client);

        // The check says what the stylist saw, item by item, and the stored document keeps it.
        var check = await client.GetFromJsonAsync<JsonElement>($"/api/checks/{checkId}");
        var items = check.GetProperty("feedback").GetProperty("items").EnumerateArray().ToList();
        Assert.False(items[0].TryGetProperty("brandSeen", out _));
        Assert.False(items[1].TryGetProperty("brandSeen", out _));
        Assert.Equal("Nike", items[2].GetProperty("brandSeen").GetString());

        var post = await app.PostAsync(client, checkId);
        var postId = post.GetProperty("id").GetGuid();
        // The card carries the stylist's pieces (items builder), bare: a name and a category each, never the brand seen.
        Assert.Equal(3, post.GetProperty("itemCount").GetInt32());
        Assert.All(post.GetProperty("items").EnumerateArray(), i => Assert.False(i.TryGetProperty("brand", out _)));

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.PostItems.Where(i => i.PostId == postId).OrderBy(i => i.Position).ToListAsync();
        Assert.Equal(["white tee", "dark jeans", "running shoes"], rows.Select(r => r.Name).ToList());
        Assert.Equal([0, 1, 2], rows.Select(r => r.Position).ToList());
        Assert.Equal(3, rows.Select(r => r.Id).Distinct().Count());
        Assert.All(rows, r => Assert.Equal(ItemSource.Stylist, r.Source));
        // The brand the stylist saw is a suggestion for the post sheet, never a row: the person confirms it first.
        Assert.All(rows, r => Assert.Null(r.Brand));
        Assert.All(rows, r => Assert.False(r.Confirmed));
        Assert.Contains("\"brandSeen\":\"Nike\"", (await db.Checks.SingleAsync(c => c.Id == checkId)).FeedbackJson);
    }

    [Fact]
    public async Task Deleting_an_account_or_a_look_takes_the_board_rows_with_it()
    {
        using var app = new TestApp();
        var (owner, ownerId, _) = await app.NewUserAsync("r10_owner");
        var (moderator, moderatorId, _) = await app.NewUserAsync("r10_excluder");
        var postId = await app.CheckAndPostAsync(owner);
        var keptPostId = await app.CheckAndPostAsync(owner);
        var weekStart = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BoardExclusions.Add(new BoardExclusion { PostId = postId, ByUserId = moderatorId, Reason = "not an outfit", CreatedAt = DateTime.UtcNow });
            db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = weekStart, Board = BoardName.Looks, Rank = 1, PostId = keptPostId, UserId = ownerId, Fires = 12 });
            db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = weekStart, Board = BoardName.Intent(StyleIntent.Date), Rank = 3, PostId = postId, UserId = ownerId, Fires = 4 });
            db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = weekStart, Board = BoardName.People, Rank = 2, UserId = moderatorId, Fires = 9 });
            await db.SaveChangesAsync();
            // One place per board per week: the closer running twice writes nothing the second time.
            db.WeeklyWinners.Add(new WeeklyWinner { Id = Guid.NewGuid(), WeekStart = weekStart, Board = BoardName.Looks, Rank = 1, UserId = moderatorId, Fires = 1 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Deleting the look: its exclusion goes, its place in the archive stays with the look set to null.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/posts/{postId}")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.BoardExclusions.ToListAsync());
            var place = await db.WeeklyWinners.SingleAsync(w => w.Rank == 3);
            Assert.Null(place.PostId);
            Assert.Equal(ownerId, place.UserId);
        }

        // Deleting the account: every place of theirs goes; the other person's stays.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/users/me")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var left = await db.WeeklyWinners.ToListAsync();
            var only = Assert.Single(left);
            Assert.Equal(moderatorId, only.UserId);
            Assert.Empty(await db.PostItems.ToListAsync());
        }

        Assert.Equal(HttpStatusCode.NoContent, (await moderator.DeleteAsync("/api/users/me")).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.WeeklyWinners.ToListAsync());
        }
    }

    [Fact]
    public async Task A_board_rank_notification_carries_its_rank_and_the_push_line_reads_it()
    {
        Assert.Equal("board_rank", NotificationType.BoardRank);
        var localizer = new Localizer();
        Assert.Equal("You finished #7 this week", localizer.Get("en", "push.board_rank", 7));
        Assert.Contains("7", localizer.Get("he", "push.board_rank", 7));
        Assert.Contains("7", localizer.Get("ar", "push.board_rank", 7));
        Assert.Contains("7", localizer.Get("ru", "push.board_rank", 7));
        // The tap lands on the week that closed: an instant a week before the send, which ?week= takes as an instant.
        var job = new PushJob(Guid.NewGuid(), NotificationType.BoardRank, "someone", null, null, null, 7);
        Assert.Equal("/#/board?week=2027-01-13T10:00:00Z", PushSender.UrlFor(job, new DateTime(2027, 1, 20, 10, 0, 0, DateTimeKind.Utc)));
        var sent = PushSender.UrlFor(job);
        Assert.StartsWith("/#/board?week=", sent);
        var instant = DateTime.Parse(sent["/#/board?week=".Length..], null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.InRange(instant, DateTime.UtcNow.AddDays(-7).AddMinutes(-1), DateTime.UtcNow.AddDays(-7).AddMinutes(1));

        using var app = new TestApp();
        var (client, userId, handle) = await app.NewUserAsync("r10_ranked");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            scope.ServiceProvider.GetRequiredService<Notifier>().Add(userId, NotificationType.BoardRank, handle, rank: 7);
            await db.SaveChangesAsync();
        }

        var notifications = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        var item = Assert.Single(notifications.GetProperty("items").EnumerateArray());
        Assert.Equal("board_rank", item.GetProperty("type").GetString());
        Assert.Equal(7, item.GetProperty("rank").GetInt32());
        Assert.Equal(handle, item.GetProperty("actorHandle").GetString());
        Assert.Equal(1, notifications.GetProperty("unread").GetInt32());
    }
}
