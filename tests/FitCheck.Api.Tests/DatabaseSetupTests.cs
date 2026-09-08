using System.Net;
using System.Net.Http.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace FitCheck.Api.Tests;

/// <summary>The migrations, the three start-up cases of <see cref="DatabaseSetup"/>, and the backup command.</summary>
public class DatabaseSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fitcheck-tests", "setup-" + Guid.NewGuid().ToString("N"));

    public DatabaseSetupTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void The_migrations_produce_exactly_the_schema_the_model_describes()
    {
        var byModel = Path.Combine(_root, "model.db");
        var byMigrations = Path.Combine(_root, "migrations.db");
        using (var db = Open(byModel))
        {
            db.Database.EnsureCreated();
        }

        using (var db = Open(byMigrations))
        {
            db.Database.Migrate();
        }

        // The shape, not the CREATE text: a later migration adds its columns with ALTER TABLE, which appends them, while
        // EnsureCreated writes them in model order. Same tables, columns, types, nullability, keys and indexes is the test.
        var expected = StructureOf(byModel);
        var actual = StructureOf(byMigrations);

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
        Assert.Equal(Names(byModel, "table").Order(), Names(byMigrations, "table").Where(t => t != HistoryRepository.DefaultTableName).Order());
    }

    [Fact]
    public void A_new_file_gets_every_table_and_the_history_of_every_migration()
    {
        var path = Path.Combine(_root, "new.db");
        using var db = Open(path);

        DatabaseSetup.Apply(db, NullLogger.Instance);

        var tables = Names(path, "table");
        foreach (var table in db.Model.GetRelationalModel().Tables)
        {
            Assert.Contains(table.Name, tables);
        }

        Assert.Contains(HistoryRepository.DefaultTableName, tables);
        Assert.Equal(db.Database.GetMigrations().OrderBy(m => m), db.Database.GetAppliedMigrations().OrderBy(m => m));
        Assert.Empty(db.Database.GetPendingMigrations());
        Assert.Empty(Directory.GetFiles(_root, "*.bak-*"));
        // WAL, so readers never wait on a writer: the setup opens the file before Migrate() and so must set it itself.
        Assert.Equal("wal", Scalar(path, "PRAGMA journal_mode"));
    }

    [Fact]
    public void A_pilot_database_made_without_migrations_is_copied_then_upgraded_without_losing_rows()
    {
        var path = Path.Combine(_root, "pilot.db");
        var userId = Guid.NewGuid();
        using (var db = Open(path))
        {
            db.Database.EnsureCreated();
            db.Users.Add(NewUser(userId, "pilot_user"));
            db.SaveChanges();
        }

        // What an earlier round's file looks like: no history table, a column and a table that did not exist yet, an index
        // that was added later.
        Execute(path, "ALTER TABLE \"Users\" DROP COLUMN \"Suspended\"");
        Execute(path, "DROP TABLE \"PushSubscriptions\"");
        Execute(path, "DROP INDEX \"IX_Checks_UserId_CreatedAt\"");
        Assert.DoesNotContain("Suspended", Columns(path, "Users"));
        Assert.DoesNotContain("PushSubscriptions", Names(path, "table"));
        Assert.DoesNotContain(HistoryRepository.DefaultTableName, Names(path, "table"));

        using (var db = Open(path))
        {
            DatabaseSetup.Apply(db, NullLogger.Instance);

            var user = db.Users.Single(u => u.Id == userId);
            Assert.Equal("pilot_user", user.Handle);
            Assert.False(user.Suspended);
            Assert.Empty(db.PushSubscriptions.ToList());
            Assert.Equal(db.Database.GetMigrations().OrderBy(m => m), db.Database.GetAppliedMigrations().OrderBy(m => m));
        }

        Assert.Contains("Suspended", Columns(path, "Users"));
        Assert.Contains("PushSubscriptions", Names(path, "table"));
        Assert.Contains("IX_Checks_UserId_CreatedAt", Names(path, "index"));
        Assert.Contains("IX_PushSubscriptions_Endpoint", Names(path, "index"));
        Assert.Equal(StructureOf(Fresh("reference.db")), StructureOf(path));

        var backups = Directory.GetFiles(_root, "pilot.db.bak-*");
        var backup = Assert.Single(backups);
        Assert.DoesNotContain("Suspended", Columns(backup, "Users"));
        Assert.Equal("pilot_user", Scalar(backup, "SELECT \"Handle\" FROM \"Users\""));
        Assert.Equal("wal", Scalar(path, "PRAGMA journal_mode"));
        Assert.Equal("delete", Scalar(backup, "PRAGMA journal_mode"));

        // A second start finds the history table and does nothing: no new copy, same schema.
        using (var db = Open(path))
        {
            DatabaseSetup.Apply(db, NullLogger.Instance);
            Assert.Equal("pilot_user", db.Users.Single(u => u.Id == userId).Handle);
        }

        Assert.Single(Directory.GetFiles(_root, "pilot.db.bak-*"));
        Assert.Equal(StructureOf(Fresh("reference2.db")), StructureOf(path));
    }

    [Fact]
    public void A_database_made_by_the_migrations_is_left_alone_and_switched_to_wal()
    {
        var path = Path.Combine(_root, "migrated.db");
        using (var db = Open(path))
        {
            // The way the app's start reaches Migrate(): the connection is opened first to look at the file, which creates it,
            // so EF's own creator (the one place EF sets WAL) never runs and the file is left with a rollback journal. A copy
            // restored from --backup is in that mode too.
            db.Database.OpenConnection();
            db.Database.Migrate();
            db.Database.CloseConnection();
            db.Users.Add(NewUser(Guid.NewGuid(), "migrated_user"));
            db.SaveChanges();
        }

        var before = SchemaOf(path);
        Assert.Equal("delete", Scalar(path, "PRAGMA journal_mode"));
        using (var db = Open(path))
        {
            DatabaseSetup.Apply(db, NullLogger.Instance);
            Assert.Equal("migrated_user", db.Users.Single().Handle);
        }

        Assert.Equal(before, SchemaOf(path));
        Assert.Empty(Directory.GetFiles(_root, "*.bak-*"));
        Assert.Equal("wal", Scalar(path, "PRAGMA journal_mode"));
    }

    [Fact]
    public async Task The_app_starts_on_a_pilot_database_and_its_people_can_still_sign_in()
    {
        using var app = new TestApp();
        Directory.CreateDirectory(app.Root);
        var path = Path.Combine(app.Root, "test.db");
        using (var db = Open(path))
        {
            db.Database.EnsureCreated();
            db.Users.Add(NewUser(Guid.NewGuid(), "veteran", password: "password123"));
            db.SaveChanges();
        }

        Execute(path, "ALTER TABLE \"Users\" DROP COLUMN \"Suspended\"");
        Execute(path, "DROP TABLE \"PushSubscriptions\"");

        var client = app.NewClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { handle = "veteran", password = "password123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var newcomer = await app.SignupAsync(app.NewClient(), "newcomer");
        Assert.Equal("newcomer", newcomer.GetProperty("handle").GetString());

        Assert.Single(Directory.GetFiles(app.Root, "test.db.bak-*"));
        Assert.Contains("Suspended", Columns(path, "Users"));
        Assert.Contains("PushSubscriptions", Names(path, "table"));
    }

    [Fact]
    public async Task The_app_promotes_listed_accounts_at_start_and_reserves_their_handles()
    {
        // The owner signed up in an earlier run; now the handle is in Admin:Handles and the app restarts.
        using var app = new TestApp { AdminHandles = "Owner_Mod" };
        Directory.CreateDirectory(app.Root);
        using (var db = Open(app.DatabasePath))
        {
            db.Database.Migrate();
            db.Users.Add(NewUser(Guid.NewGuid(), "owner_mod", password: "password123"));
            db.Users.Add(NewUser(Guid.NewGuid(), "bystander", password: "password123"));
            db.SaveChanges();
        }

        var owner = app.NewClient();
        var login = await owner.PostAsJsonAsync("/api/auth/login", new { handle = "owner_mod", password = "password123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/admin/queue")).StatusCode);
        Assert.Equal("1", Scalar(app.DatabasePath, "SELECT \"IsAdmin\" FROM \"Users\" WHERE \"HandleLower\" = 'owner_mod'"));

        var bystander = app.NewClient();
        var other = await bystander.PostAsJsonAsync("/api/auth/login", new { handle = "bystander", password = "password123" });
        Assert.False((await other.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await bystander.GetAsync("/api/admin/queue")).StatusCode);

        // The listed handle is nobody else's to register, in any case.
        var taken = await app.NewClient().PostAsJsonAsync("/api/auth/signup", new { handle = "OWNER_MOD", password = "password123", confirmed16Plus = true, language = "en" });
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
    }

    [Fact]
    public void Backup_copies_the_database_and_the_photo_folder_with_a_stamp()
    {
        var path = Path.Combine(_root, "live.db");
        using (var db = Open(path))
        {
            db.Database.Migrate();
            db.Users.Add(NewUser(Guid.NewGuid(), "backed_up"));
            db.SaveChanges();
        }

        var storage = Path.Combine(_root, "storage");
        Directory.CreateDirectory(Path.Combine(storage, "user1"));
        File.WriteAllBytes(Path.Combine(storage, "user1", "check.jpg"), [1, 2, 3]);
        var target = Path.Combine(_root, "backups");

        var (database, storageCopy) = DatabaseSetup.Backup($"Data Source={path}", storage, target);

        Assert.StartsWith(Path.Combine(target, "orevosh-"), database);
        Assert.EndsWith(".db", database);
        Assert.True(File.Exists(database));
        Assert.Equal("backed_up", Scalar(database, "SELECT \"Handle\" FROM \"Users\""));
        Assert.NotNull(storageCopy);
        Assert.StartsWith(Path.Combine(target, "storage-"), storageCopy);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(storageCopy!, "user1", "check.jpg")));

        // The original is untouched and still opens.
        using (var db = Open(path))
        {
            Assert.Equal("backed_up", db.Users.Single().Handle);
        }
    }

    [Fact]
    public void Backup_without_a_photo_folder_copies_only_the_database()
    {
        var path = Path.Combine(_root, "bare.db");
        using (var db = Open(path))
        {
            db.Database.Migrate();
        }

        var (database, storageCopy) = DatabaseSetup.Backup($"Data Source={path}", Path.Combine(_root, "nowhere"), Path.Combine(_root, "backups"));

        Assert.True(File.Exists(database));
        Assert.Null(storageCopy);
    }

    [Fact]
    public void Backup_skips_a_file_that_vanishes_between_the_listing_and_the_copy()
    {
        var path = Path.Combine(_root, "vanish.db");
        using (var db = Open(path))
        {
            db.Database.Migrate();
        }

        var storage = Path.Combine(_root, "storage");
        Directory.CreateDirectory(Path.Combine(storage, "user1"));
        File.WriteAllBytes(Path.Combine(storage, "user1", "check.jpg"), [1, 2, 3]);
        // A link to a file that is not there: listed with the folder, gone by the time it is copied. Symbolic links need a
        // privilege on some Windows setups; without one there is nothing to test here.
        if (!TryLink(Path.Combine(storage, "user1", "gone.jpg"), Path.Combine(_root, "never-existed.jpg"), directory: false))
        {
            return;
        }

        var (database, storageCopy) = DatabaseSetup.Backup($"Data Source={path}", storage, Path.Combine(_root, "backups"));

        Assert.True(File.Exists(database));
        Assert.NotNull(storageCopy);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(storageCopy!, "user1", "check.jpg")));
        Assert.False(File.Exists(Path.Combine(storageCopy!, "user1", "gone.jpg")));
    }

    [Fact]
    public void A_failing_storage_copy_leaves_no_partial_backup()
    {
        var path = Path.Combine(_root, "failing.db");
        using (var db = Open(path))
        {
            db.Database.Migrate();
        }

        var storage = Path.Combine(_root, "storage");
        Directory.CreateDirectory(Path.Combine(storage, "user1"));
        File.WriteAllBytes(Path.Combine(storage, "user1", "check.jpg"), [1, 2, 3]);
        // A folder that links back to the storage folder: the copy descends until the path is too long or too many links deep,
        // which is a failure that is not a vanished file, after real files were already copied.
        if (!TryLink(Path.Combine(storage, "loop"), storage, directory: true))
        {
            return;
        }

        var target = Path.Combine(_root, "backups");
        Assert.ThrowsAny<IOException>(() => DatabaseSetup.Backup($"Data Source={path}", storage, target));

        // Neither half is left behind: a listing of the backup folder must never show a copy that is missing its photos.
        Assert.Empty(Directory.Exists(target) ? Directory.GetFileSystemEntries(target) : []);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(storage, "user1", "check.jpg")));
    }

    private static bool TryLink(string link, string target, bool directory)
    {
        try
        {
            if (directory)
            {
                Directory.CreateSymbolicLink(link, target);
            }
            else
            {
                File.CreateSymbolicLink(link, target);
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
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

    private static AppUser NewUser(Guid id, string handle, string? password = null)
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

    /// <summary>Every table and index with its CREATE statement, minus the migrations history, so two files can be compared.</summary>
    private static List<string> SchemaOf(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type || ' ' || name || ' ' || IFNULL(sql, '') FROM sqlite_master " +
                              "WHERE name NOT LIKE 'sqlite_%' AND name <> $history ORDER BY type, name";
        command.Parameters.AddWithValue("$history", HistoryRepository.DefaultTableName);
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    /// <summary>
    /// Tables with their columns (name, declared type, NOT NULL, primary key) and indexes with their statements. An upgraded
    /// table has its new columns appended with a DEFAULT, so its CREATE text differs from a fresh one while its shape is the
    /// same; this is the shape.
    /// </summary>
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

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
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

/// <summary>The headers every response carries, and the one that only https gets.</summary>
public class SecurityHeadersTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public SecurityHeadersTests(TestApp app)
    {
        _app = app;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/config")]
    [InlineData("/healthz")]
    [InlineData("/api/posts/00000000-0000-0000-0000-000000000000")]
    public async Task Every_response_says_nosniff_no_framing_and_a_tight_referrer(string path)
    {
        var response = await _app.NewClient().GetAsync(path);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("camera=(self), microphone=(self), geolocation=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")));
    }

    [Fact]
    public async Task Hsts_is_sent_only_when_the_request_came_in_over_https()
    {
        var client = _app.NewClient();

        var plain = await client.GetAsync("/healthz");
        Assert.False(plain.Headers.Contains("Strict-Transport-Security"));

        var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add("X-Forwarded-Proto", "https");
        var secure = await client.SendAsync(request);
        // This host only: the owner may run the app on a bare domain next to subdomains that are not ours to pin.
        Assert.Equal("max-age=31536000", Assert.Single(secure.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task The_headers_survive_an_error_response()
    {
        var response = await _app.BareClient().PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
    }
}
