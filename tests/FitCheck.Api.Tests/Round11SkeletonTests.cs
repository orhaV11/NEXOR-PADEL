using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace FitCheck.Api.Tests;

/// <summary>
/// What the Round 11 skeleton enforces on its own, before the builders: the migration over a Round 10 file (the Blocks
/// table, Users.BillingSubscriptionId, every row kept), the 501 stubs behind the right gates, the block row's key,
/// index and cascades, the subscription id's shape and the webhook that does not read it yet, the profile that never
/// says BlockedBy, the server strings in four languages, the export and readiness shapes, and the client keys.
/// </summary>
public class Round11MigrationTests : IDisposable
{
    private const string Round10 = "20260912151344_Round10";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fitcheck-tests", "r11-" + Guid.NewGuid().ToString("N"));

    public Round11MigrationTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void The_round11_migration_adds_blocks_and_the_subscription_id_and_keeps_a_round10_file_as_it_was()
    {
        var path = Path.Combine(_root, "round10.db");
        var userId = Guid.NewGuid();
        SeedRound10(path, userId);
        Assert.DoesNotContain("BillingSubscriptionId", Columns(path, "Users"));
        Assert.DoesNotContain("Blocks", Names(path, "table"));

        using (var db = Open(path))
        {
            DatabaseSetup.Apply(db, NullLogger.Instance);

            var veteran = db.Users.Single(u => u.Id == userId);
            Assert.Equal("veteran", veteran.Handle);
            Assert.Equal("cus_veteran", veteran.BillingCustomerId);
            Assert.Null(veteran.BillingSubscriptionId);
            Assert.Empty(db.Blocks.ToList());
            Assert.Equal(db.Database.GetMigrations().OrderBy(m => m), db.Database.GetAppliedMigrations().OrderBy(m => m));
            Assert.Contains(db.Database.GetAppliedMigrations(), m => m.EndsWith("_Round11"));
        }

        // The upgraded file has the shape a fresh one gets: the same columns, keys and indexes on every table.
        Assert.Equal(StructureOf(Fresh("reference.db")), StructureOf(path));
        Assert.Contains("BillingSubscriptionId", Columns(path, "Users"));
        Assert.Equal(["BlockerId", "BlockedId", "CreatedAt"], Columns(path, "Blocks"));
        Assert.Contains("IX_Blocks_BlockedId", Names(path, "index"));
        // A migrated file needs no copy aside; nothing was rebuilt.
        Assert.Empty(Directory.GetFiles(_root, "*.bak-*"));
    }

    /// <summary>A file exactly as Round 10 left it: migrated to Round10, one account with a Stripe customer, written in raw SQL since the Users table has one column fewer than the model.</summary>
    private static void SeedRound10(string path, Guid userId)
    {
        using (var db = Open(path))
        {
            db.GetService<IMigrator>().Migrate(Round10);
        }

        Execute(path,
            "INSERT INTO \"Users\" (\"Id\", \"Handle\", \"HandleLower\", \"PasswordHash\", \"AccountType\", \"AvatarVersion\", \"Confirmed16Plus\", \"PreferredLanguage\", " +
            "\"StreakCount\", \"Plan\", \"BillingCustomerId\", \"Verified\", \"Suspended\", \"IsAdmin\", \"CreatedAt\") " +
            "VALUES ($id, 'veteran', 'veteran', 'x', 'Person', 0, 1, 'en', 0, 'free', 'cus_veteran', 0, 0, 0, $created)",
            ("$id", userId), ("$created", DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss")));
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

    internal static List<string> Names(string path, string type)
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

/// <summary>The routes the skeleton maps answer 501 with a sentence, behind the gates the builders keep.</summary>
public class Round11StubTests : IClassFixture<TestApp>
{
    private const string NotBuiltEnglish = "This part of OREVOSH isn't built yet.";
    private const string NotBuiltHebrew = "החלק הזה של OREVOSH עדיין לא בנוי.";

    private readonly TestApp _app;

    public Round11StubTests(TestApp app)
    {
        _app = app;
    }

    private static async Task<string> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()!;

    [Fact]
    public async Task Readiness_is_public_and_answers_501_in_the_callers_language()
    {
        var response = await _app.NewClient().GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal(NotBuiltEnglish, await ErrorOf(response));

        var hebrew = _app.NewClient();
        hebrew.DefaultRequestHeaders.AcceptLanguage.ParseAdd("he-IL");
        Assert.Equal(NotBuiltHebrew, await ErrorOf(await hebrew.GetAsync("/readyz")));

        // The liveness line is untouched: plain text, 200, never cached.
        var health = await _app.NewClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("ok", await health.Content.ReadAsStringAsync());
        Assert.Equal("no-store", health.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Blocking_needs_a_session_and_the_csrf_header_then_answers_501()
    {
        var anonymous = _app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/users/someone/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/users/someone/block")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users/me/blocks")).StatusCode);

        var (client, _, _) = await _app.NewUserAsync("r11_blocker");
        foreach (var response in new[]
                 {
                     await client.PostAsync("/api/users/someone/block", null),
                     await client.DeleteAsync("/api/users/someone/block"),
                     await client.GetAsync("/api/users/me/blocks"),
                 })
        {
            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
            Assert.Equal(NotBuiltEnglish, await ErrorOf(response));
        }

        // A write without the CSRF header is refused before any handler, as every other write is.
        var bare = _app.BareClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.PostAsync("/api/users/someone/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.DeleteAsync("/api/users/someone/block")).StatusCode);
    }

    /// <summary>Built (the billing builder): the gates stay; the manual provider's portal is 404 and the export answers. The rest is in BillingTests and ExportTests.</summary>
    [Fact]
    public async Task The_portal_and_the_export_need_a_session()
    {
        var anonymous = _app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/billing/portal", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users/me/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _app.BareClient().PostAsync("/api/billing/portal", null)).StatusCode);

        var (client, _, _) = await _app.NewUserAsync("r11_exporter", language: "he");
        var portal = await client.PostAsync("/api/billing/portal", null);
        Assert.Equal(HttpStatusCode.NotFound, portal.StatusCode);
        // The builder reads the account's language, not Accept-Language.
        Assert.Equal("כדי לנהל את התוכנית, כותבים לנו.", await ErrorOf(portal));
        var export = await client.GetAsync("/api/users/me/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("r11_exporter", (await export.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("account").GetProperty("handle").GetString());
        // Nothing went to Stripe while the provider is manual.
        Assert.Empty(_app.StripeHandler.PortalRequests);
    }
}

/// <summary>The block row, the subscription id, the profile, the strings, the export and readiness shapes, the client keys.</summary>
public class Round11SeamTests : IClassFixture<TestApp>, IClassFixture<StripeBillingApp>
{
    private readonly TestApp _app;
    private readonly StripeBillingApp _stripe;

    public Round11SeamTests(TestApp app, StripeBillingApp stripe)
    {
        _app = app;
        _stripe = stripe;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static void WithDb(TestApp app, Action<AppDbContext> action)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        action(db);
        db.SaveChanges();
    }

    private static T ReadDb<T>(TestApp app, Func<AppDbContext, T> read)
    {
        using var scope = app.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task A_block_is_one_row_per_pair_and_direction_and_goes_with_either_account()
    {
        var (blocker, blockerId, _) = await _app.NewUserAsync("r11_shut");
        var (_, blockedId, _) = await _app.NewUserAsync("r11_out");
        var (_, thirdId, _) = await _app.NewUserAsync("r11_third");

        WithDb(_app, db =>
        {
            db.Blocks.Add(new Block { BlockerId = blockerId, BlockedId = blockedId, CreatedAt = DateTime.UtcNow });
            // The other direction is its own row: both may block each other.
            db.Blocks.Add(new Block { BlockerId = blockedId, BlockedId = blockerId, CreatedAt = DateTime.UtcNow });
            db.Blocks.Add(new Block { BlockerId = thirdId, BlockedId = blockerId, CreatedAt = DateTime.UtcNow });
        });
        Assert.Equal(3, ReadDb(_app, db => db.Blocks.Count()));

        // The key refuses the same pair twice.
        Assert.Throws<DbUpdateException>(() => WithDb(_app, db => db.Blocks.Add(new Block { BlockerId = blockerId, BlockedId = blockedId, CreatedAt = DateTime.UtcNow })));

        // The index the feed and profile filters read: "who blocked me".
        Assert.Contains("IX_Blocks_BlockedId", Round11MigrationTests.Names(_app.DatabasePath, "index"));

        // The database cascade alone (a row deleted around the endpoint) takes both ends of a pair with the account.
        WithDb(_app, db => db.Users.Where(u => u.Id == blockedId).ExecuteDelete());
        Assert.Equal([thirdId], ReadDb(_app, db => db.Blocks.Select(b => b.BlockerId).ToList()));

        // DELETE /api/users/me removes the rows explicitly, like every other table, so a freed handle starts unblocked.
        WithDb(_app, db => db.Blocks.Add(new Block { BlockerId = blockerId, BlockedId = thirdId, CreatedAt = DateTime.UtcNow }));
        Assert.Equal(HttpStatusCode.NoContent, (await blocker.DeleteAsync("/api/users/me")).StatusCode);
        Assert.Empty(ReadDb(_app, db => db.Blocks.ToList()));
    }

    /// <summary>Built (the billing builder): the webhook now stores the id from checkout.session.completed; the lifecycle is in BillingTests.</summary>
    [Fact]
    public async Task The_subscription_id_is_a_short_nullable_string_the_webhook_stores_from_checkout()
    {
        var (client, id, _) = await _stripe.NewUserAsync("r11_subscriber");
        using (var scope = _stripe.Services.CreateScope())
        {
            var property = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model.FindEntityType(typeof(AppUser))!.FindProperty(nameof(AppUser.BillingSubscriptionId))!;
            Assert.Equal(64, property.GetMaxLength());
            Assert.True(property.IsNullable);
            Assert.Null(property.GetContainingIndexes().FirstOrDefault());
        }

        // A completed Checkout names its subscription: the webhook grants Pro as before and now remembers the id.
        var payload = new
        {
            id = "evt_r11",
            type = "checkout.session.completed",
            data = new { @object = new { id = "cs_r11", @object = "checkout.session", client_reference_id = id.ToString("N"), customer = "cus_r11", subscription = "sub_r11" } }
        };
        var body = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, BillingEndpoints.WebhookPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation(StripeClient.SignatureHeader, StripeClient.SignatureHeaderValue(StripeBillingApp.WebhookSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), body));
        Assert.Equal(HttpStatusCode.OK, (await _stripe.BareClient().SendAsync(request)).StatusCode);

        var user = ReadDb(_stripe, db => db.Users.Single(u => u.Id == id));
        Assert.Equal("pro", user.Plan);
        Assert.Equal("cus_r11", user.BillingCustomerId);
        Assert.Equal("sub_r11", user.BillingSubscriptionId);
        Assert.Equal("pro", (await Json(await client.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());

        // The column takes a value and gives it back; nothing about it reaches "me".
        WithDb(_stripe, db => db.Users.Single(u => u.Id == id).BillingSubscriptionId = "sub_r11_by_hand");
        Assert.Equal("sub_r11_by_hand", ReadDb(_stripe, db => db.Users.Single(u => u.Id == id).BillingSubscriptionId));
        var me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.DoesNotContain(me.EnumerateObject(), p => p.Name.Contains("billing", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("subscription", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_profile_says_whether_the_viewer_blocked_it_and_never_whether_it_blocked_the_viewer()
    {
        var (viewer, viewerId, _) = await _app.NewUserAsync("r11_viewer");
        var (_, ownerId, owner) = await _app.NewUserAsync("r11_owner");
        // Even with a block in the row, the DTO carries no BlockedBy: the skeleton answers false for Blocked (the builder reads the row).
        WithDb(_app, db => db.Blocks.Add(new Block { BlockerId = ownerId, BlockedId = viewerId, CreatedAt = DateTime.UtcNow }));

        foreach (var client in new[] { viewer, _app.NewClient() })
        {
            var profile = await Json(await client.GetAsync($"/api/users/{owner}"));
            var view = profile.GetProperty("viewer");
            Assert.False(view.GetProperty("blocked").GetBoolean());
            Assert.Equal(["isMe", "following", "blocked"], view.EnumerateObject().Select(p => p.Name).ToArray());
        }

        // The record itself has no such member either, so no serializer setting can leak it.
        Assert.DoesNotContain(typeof(ViewerProfileDto).GetProperties(), p => p.Name.Contains("BlockedBy", StringComparison.OrdinalIgnoreCase));
        Assert.False(new ViewerProfileDto(IsMe: false, Following: false).Blocked);
    }

    [Theory]
    [InlineData("en", "error.blocked", "You can't interact with this account.")]
    [InlineData("en", "error.portal_unavailable", "Manage your plan by writing to us.")]
    [InlineData("en", "error.cannot_block_self", "You can't block yourself.")]
    [InlineData("he", "error.blocked", "אי אפשר לפעול מול החשבון הזה.")]
    public void The_server_strings_read_as_written(string locale, string key, string expected) =>
        Assert.Equal(expected, new Localizer().Get(locale, key));

    [Theory]
    [InlineData("error.cannot_block_self")]
    [InlineData("error.already_blocked")]
    [InlineData("error.not_blocked")]
    [InlineData("error.blocked")]
    [InlineData("error.portal_unavailable")]
    [InlineData("error.export_failed")]
    public void The_server_strings_exist_in_every_locale(string key)
    {
        var localizer = new Localizer();
        var english = localizer.Get("en", key);
        Assert.NotEqual(key, english);
        foreach (var locale in Localizer.SupportedLocales.Where(l => l != "en"))
        {
            var text = localizer.Get(locale, key);
            Assert.NotEqual(key, text);
            Assert.NotEqual(english, text);
        }
    }

    [Fact]
    public void The_export_and_readiness_documents_serialise_to_the_documented_shape()
    {
        var at = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);
        var export = new ExportDto(
            at,
            new ExportAccountDto("noa", "Noa", "Person", "he", "noa@example.test", at.AddDays(-40), "pro", at.AddDays(20)),
            [new ExportCheckDto(Guid.NewGuid(), at, StyleIntent.Date, "dinner", 7, "Clean casual", "Swap the shoes", new BreakdownDto(7, 8, 5), [new ExportItemDto("White tee", "top")], "ok")],
            [new ExportPostDto(Guid.NewGuid(), at, "first look", StyleIntent.Date, 7, ["ootd"], [new ExportItemDto("Running shoes", "shoes", "Nike", "Air Max 90", "https://shop.example/x")], 3, 1)],
            [new ExportCommentDto(Guid.NewGuid(), at, "nice")],
            [new ExportHandleDto("dan", at)],
            [new ExportHandleDto("nexor", at)],
            [new ExportComparisonDto(Guid.NewGuid(), at, "A")],
            [new ExportHandleDto("troll", at)],
            [new ExportNotificationDto("fire", at)]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(export, AppJson.Options));
        var root = json.RootElement;
        Assert.Equal(["exportedAt", "account", "checks", "posts", "comments", "follows", "followers", "comparisons", "blocks", "notifications"], Keys(root));
        Assert.Equal(["handle", "name", "accountType", "language", "email", "createdAt", "plan", "proUntil"], Keys(root.GetProperty("account")));
        Assert.DoesNotContain("birthDate", Keys(root.GetProperty("account")));
        Assert.Equal(["id", "createdAt", "intent", "occasion", "score", "headline", "tip", "breakdown", "items", "status"], Keys(root.GetProperty("checks")[0]));
        Assert.Equal("Date", root.GetProperty("checks")[0].GetProperty("intent").GetString());
        Assert.Equal(["name", "category"], Keys(root.GetProperty("checks")[0].GetProperty("items")[0]));
        Assert.Equal(["id", "createdAt", "caption", "intent", "score", "tags", "items", "fires", "comments"], Keys(root.GetProperty("posts")[0]));
        Assert.Equal(["name", "category", "brand", "model", "url"], Keys(root.GetProperty("posts")[0].GetProperty("items")[0]));
        Assert.Equal(["postId", "createdAt", "text"], Keys(root.GetProperty("comments")[0]));
        Assert.Equal(["handle", "since"], Keys(root.GetProperty("follows")[0]));
        Assert.Equal(["handle", "since"], Keys(root.GetProperty("blocks")[0]));
        Assert.Equal(["id", "createdAt", "winner"], Keys(root.GetProperty("comparisons")[0]));
        Assert.Equal(["type", "createdAt"], Keys(root.GetProperty("notifications")[0]));

        // No email on the account means no key at all, not null.
        var anonymous = export with { Account = export.Account with { Email = null, ProUntil = null } };
        using var quiet = JsonDocument.Parse(JsonSerializer.Serialize(anonymous, AppJson.Options));
        Assert.Equal(["handle", "name", "accountType", "language", "createdAt", "plan"], Keys(quiet.RootElement.GetProperty("account")));

        Assert.Equal("""{"ok":false,"checks":{"db":"ok","storage":"not writable"}}""",
            JsonSerializer.Serialize(new ReadyDto(false, new Dictionary<string, string> { ["db"] = "ok", ["storage"] = "not writable" }), AppJson.Options));
        Assert.Equal("""{"url":"https://billing.stripe.com/p/session/x"}""", JsonSerializer.Serialize(new PortalDto("https://billing.stripe.com/p/session/x"), AppJson.Options));
        Assert.Equal("""{"items":[]}""", JsonSerializer.Serialize(new BlocksDto([]), AppJson.Options));
    }

    [Fact]
    public void The_client_keys_are_in_all_four_i18n_files_with_the_same_placeholders()
    {
        string[] added =
        [
            "block.block", "block.unblock", "block.blocked_title", "block.blocked_empty", "block.confirm_title", "block.confirm_body", "block.done", "block.undone", "block.coming",
            "settings.blocked", "settings.export", "export.hint", "export.ready", "billing.manage", "billing.manage_hint", "billing.manual_hint"
        ];
        var files = Localizer.SupportedLocales.ToDictionary(l => l, l => ReadI18n(l));
        var english = files["en"];
        Assert.Equal("Blocked accounts", english["block.blocked_title"]);
        Assert.Equal("Block {name}?", english["block.confirm_title"]);
        Assert.Equal("Blocked.", english["block.done"]);
        Assert.Equal("Manage subscription", english["billing.manage"]);
        foreach (var (locale, table) in files)
        {
            foreach (var key in added)
            {
                Assert.True(table.ContainsKey(key), $"{locale}: {key} missing");
                Assert.False(string.IsNullOrWhiteSpace(table[key]), $"{locale}: {key} empty");
            }

            if (locale == "en")
            {
                continue;
            }

            Assert.Equal(english.Keys.Order(), table.Keys.Order());
            foreach (var key in added)
            {
                Assert.Equal(Placeholders(english[key]), Placeholders(table[key]));
                Assert.NotEqual(english[key], table[key]);
            }
        }
    }

    private static string[] Keys(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();

    private static string Placeholders(string text) =>
        string.Join(",", System.Text.RegularExpressions.Regex.Matches(text, @"\{[a-z]+\}").Select(m => m.Value).Order());

    /// <summary>One client dictionary, read from the source tree (found by walking up from the test binaries to the solution file).</summary>
    private static Dictionary<string, string> ReadI18n(string locale)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FitCheck.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "FitCheck.Api", "wwwroot", "i18n", locale + ".json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }
}
