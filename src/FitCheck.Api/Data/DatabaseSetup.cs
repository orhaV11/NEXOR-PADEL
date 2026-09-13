using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FitCheck.Api.Data;

/// <summary>
/// Brings the SQLite file to the current schema on every start, so an update never loses a row and never needs a hand.
/// Three situations are told apart by what the file contains:
/// <list type="bullet">
/// <item>No file, or a file with no tables: <c>Migrate()</c> creates everything from the migrations.</item>
/// <item>A file with <c>__EFMigrationsHistory</c>: <c>Migrate()</c> applies whatever is pending, usually nothing.</item>
/// <item>A file with our tables but no history: a pilot database made by <c>EnsureCreated</c> in the rounds before migrations.
/// It is copied to <c>&lt;file&gt;.bak-&lt;stamp&gt;</c> first, then every missing table, column, foreign key and index is
/// added from the EF model and every column whose nullability differs from the model's is altered (never from a
/// hand-written list, so this keeps working as the model grows), and the history table is written as if the migrations
/// had run. From then on it is the second case.</item>
/// </list>
/// <para>
/// Migrations live in <c>Data/Migrations</c>. After changing the model, from the repository root (once:
/// <c>dotnet tool install -g dotnet-ef</c>):
/// <code>dotnet ef migrations add &lt;Name&gt; --project src/FitCheck.Api --output-dir Data/Migrations</code>
/// Always add a new migration; never regenerate <c>InitialCreate</c>. Deployed databases carry its row in the history
/// table, and a regenerated one (a new id) would be "pending" on every one of them and fail on the first CREATE TABLE.
/// The last regeneration was the commit that added <c>Users.IsAdmin</c>, while no database from a migration had shipped.
/// <c>DatabaseSetupTests</c> checks the migrations produce exactly the schema the model describes.
/// </para>
/// <para>
/// Every file database is switched to WAL once the schema is current: readers never wait on a writer, the push worker and
/// a request can share the file, and <c>--backup</c> snapshots it while the app runs. EF sets WAL only when its own creator
/// makes the file; this class opens the connection first to look at the file (which creates it), so that never runs here,
/// and a copy restored from <c>--backup</c> is written in rollback mode on purpose. Setting it every start is idempotent.
/// </para>
/// </summary>
public static class DatabaseSetup
{
    public static void Apply(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        Apply(scope.ServiceProvider.GetRequiredService<AppDbContext>(), logger);
    }

    public static void Apply(AppDbContext db, ILogger logger)
    {
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        db.Database.OpenConnection();
        try
        {
            var tables = ExistingNames(connection, "table");
            var modelTables = db.Model.GetRelationalModel().Tables.Select(t => t.Name).ToList();
            if (tables.Contains(HistoryRepository.DefaultTableName))
            {
                var pending = db.Database.GetPendingMigrations().ToList();
                if (pending.Count == 0)
                {
                    logger.LogInformation("Database {DataSource} is at the current schema.", connection.DataSource);
                }
                else
                {
                    logger.LogInformation("Database {DataSource}: applying {Count} pending migration(s): {Migrations}.",
                        connection.DataSource, pending.Count, string.Join(", ", pending));
                    db.Database.Migrate();
                }
            }
            else if (modelTables.Any(tables.Contains))
            {
                UpgradePilotDatabase(db, connection, logger);
            }
            else
            {
                logger.LogInformation("Database {DataSource} is new: creating the schema from the migrations.", connection.DataSource);
                db.Database.Migrate();
            }

            EnsureWal(connection);
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    /// <summary>WAL for file databases (idempotent, persisted in the file). An in-memory database has no journal to speak of.</summary>
    private static void EnsureWal(SqliteConnection connection)
    {
        if (string.IsNullOrEmpty(connection.DataSource) || connection.DataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A consistent copy of the database (SQLite's own <c>VACUUM INTO</c>, safe while the app is running) and a copy of the
    /// photo folder, both stamped with the UTC time, in <paramref name="targetDir"/>. Behind <c>dotnet FitCheck.Api.dll
    /// --backup &lt;dir&gt;</c>; see tools/backup.sh and DEPLOY.md.
    /// </summary>
    public static (string Database, string? Storage) Backup(string connectionString, string storageRoot, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var database = Path.GetFullPath(Path.Combine(targetDir, $"orevosh-{stamp}.db"));
        using (var source = new SqliteConnection(connectionString))
        {
            source.Open();
            Snapshot(source, database);
        }

        string? storage = null;
        if (Directory.Exists(storageRoot))
        {
            storage = Path.GetFullPath(Path.Combine(targetDir, $"storage-{stamp}"));
            try
            {
                CopyDirectory(storageRoot, storage);
            }
            catch
            {
                // Half a backup is worse than none: a nightly job that keeps the database and loses the photos would look fine
                // in a listing. Both halves go, then the failure reaches the caller (and the cron log).
                TryDelete(storage);
                TryDeleteFile(database);
                throw;
            }
        }

        return (database, storage);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The original failure is the one worth reporting.
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void UpgradePilotDatabase(AppDbContext db, SqliteConnection connection, ILogger logger)
    {
        var dataSource = connection.DataSource;
        if (File.Exists(dataSource))
        {
            var backup = $"{dataSource}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}";
            Snapshot(connection, backup);
            logger.LogInformation("Database {DataSource} predates migrations: copied it to {Backup} before upgrading.", dataSource, backup);
        }
        else
        {
            logger.LogWarning("Database {DataSource} predates migrations and is not a file: upgrading without a copy.", dataSource);
        }

        // The design-time model is the one the migrations tooling (and EnsureCreated) diff; the runtime model drops what it needs.
        var model = db.GetService<IDesignTimeModel>().Model;
        var relational = model.GetRelationalModel();
        var existingTables = ExistingNames(connection, "table");
        var existingIndexes = ExistingNames(connection, "index");
        var fromScratch = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, relational);

        // Tables first (the differ orders them so referenced tables come before their referrers), then columns on the tables
        // that were already there (added where missing, altered where the model says NULL and the file says NOT NULL or the
        // other way round: Round 9 made Checks.UserId nullable for guest checks), then the foreign keys those tables lack
        // (Round 9 added Posts.BeforePostId with ON DELETE SET NULL), then indexes, which may cover new columns.
        var operations = new List<MigrationOperation>();
        var created = new List<string>();
        var added = new List<string>();
        var altered = new List<string>();
        var linked = new List<string>();
        foreach (var operation in fromScratch.OfType<CreateTableOperation>().Where(o => !existingTables.Contains(o.Name)))
        {
            operations.Add(operation);
            created.Add(operation.Name);
        }

        foreach (var table in relational.Tables.Where(t => existingTables.Contains(t.Name)))
        {
            var columns = ExistingColumns(connection, table.Name);
            foreach (var column in table.Columns)
            {
                if (!columns.TryGetValue(column.Name, out var notNull))
                {
                    operations.Add(AddColumn(table, column));
                    added.Add($"{table.Name}.{column.Name}");
                    continue;
                }

                var nullableInFile = !notNull;
                if (nullableInFile != column.IsNullable)
                {
                    operations.Add(AlterColumn(table, column, wasNullable: nullableInFile));
                    altered.Add($"{table.Name}.{column.Name} {(column.IsNullable ? "nullable" : "not null")}");
                }
            }

            var foreignKeys = ExistingForeignKeys(connection, table.Name);
            foreach (var foreignKey in table.ForeignKeyConstraints.Where(fk => !foreignKeys.Contains(Shape(fk))))
            {
                operations.Add(AddForeignKeyOperation.CreateFrom(foreignKey));
                linked.Add(foreignKey.Name);
            }
        }

        operations.AddRange(fromScratch.OfType<CreateIndexOperation>().Where(o => !existingIndexes.Contains(o.Name)));

        // An altered column or a new foreign key is a table rebuild for SQLite (EF's generator writes it: the rows are copied
        // into a fresh table built from the model, the old one is dropped, the new one takes its name). Dropping a table
        // while foreign keys are enforced (the provider's default) would run the referencing tables' ON DELETE actions on
        // the rows being moved (Posts cascades from Checks), so enforcement is off around the batch, as Migrate() does; the
        // pragma is a no-op inside a transaction, hence outside it, and the connection gets its setting back afterwards
        // (it is pooled). The batch is one transaction, and a table that comes out of it with fewer rows than it went in
        // with rolls the whole thing back: the copy next to the file is then the state of things.
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
        var history = db.GetService<IHistoryRepository>();
        var migrations = db.Database.GetMigrations().ToList();
        var counted = relational.Tables.Select(t => t.Name).Where(existingTables.Contains).ToList();
        var foreignKeysWereOn = ForeignKeysEnabled(connection);
        Execute(connection, null, "PRAGMA foreign_keys = 0");
        try
        {
            using var transaction = connection.BeginTransaction();
            var before = RowCounts(connection, transaction, counted);
            foreach (var command in commands)
            {
                Execute(connection, transaction, command.CommandText);
            }

            var after = RowCounts(connection, transaction, counted);
            var lost = counted.Where(t => after[t] < before[t]).ToList();
            if (lost.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The upgrade of {dataSource} would lose rows in {string.Join(", ", lost.Select(t => $"{t} ({before[t]} -> {after[t]})"))}; rolled back, the file is untouched.");
            }

            Execute(connection, transaction, history.GetCreateIfNotExistsScript());
            foreach (var migration in migrations)
            {
                Execute(connection, transaction, history.GetInsertScript(new HistoryRow(migration, ProductInfo.GetVersion())));
            }

            transaction.Commit();
        }
        finally
        {
            Execute(connection, null, foreignKeysWereOn ? "PRAGMA foreign_keys = 1" : "PRAGMA foreign_keys = 0");
        }

        logger.LogInformation(
            "Database {DataSource} upgraded: {TableCount} table(s) created ({Tables}), {ColumnCount} column(s) added ({Columns}), " +
            "{AlteredCount} column(s) altered ({Altered}), {ForeignKeyCount} foreign key(s) added ({ForeignKeys}), " +
            "{IndexCount} index(es) created; recorded {Migrations} as applied.",
            dataSource, created.Count, string.Join(", ", created), added.Count, string.Join(", ", added),
            altered.Count, string.Join(", ", altered), linked.Count, string.Join(", ", linked),
            operations.Count(o => o is CreateIndexOperation), string.Join(", ", migrations));
    }

    /// <summary>
    /// An ALTER COLUMN for one model column whose nullability the file got wrong. SQLite cannot alter a column, so EF's
    /// generator turns this into a rebuild of the table from the model; a column going NOT NULL keeps the same empty value
    /// <see cref="AddColumn"/> would give it for the rows that hold NULL.
    /// </summary>
    private static AlterColumnOperation AlterColumn(ITable table, IColumn column, bool wasNullable)
    {
        var target = AddColumn(table, column);
        return new AlterColumnOperation
        {
            Schema = table.Schema,
            Table = table.Name,
            Name = column.Name,
            ClrType = target.ClrType,
            ColumnType = target.ColumnType,
            IsNullable = target.IsNullable,
            DefaultValue = target.DefaultValue,
            DefaultValueSql = target.DefaultValueSql,
            ComputedColumnSql = target.ComputedColumnSql,
            IsStored = target.IsStored,
            MaxLength = target.MaxLength,
            Precision = target.Precision,
            Scale = target.Scale,
            IsUnicode = target.IsUnicode,
            IsFixedLength = target.IsFixedLength,
            Collation = target.Collation,
            Comment = target.Comment,
            OldColumn = new AddColumnOperation
            {
                Schema = table.Schema,
                Table = table.Name,
                Name = column.Name,
                ClrType = target.ClrType,
                ColumnType = target.ColumnType,
                IsNullable = wasNullable
            }
        };
    }

    /// <summary>A foreign key as both the file and the model can describe it: its columns, the table it points at and the columns there.</summary>
    private static string Shape(IForeignKeyConstraint foreignKey) =>
        Shape(foreignKey.Columns.Select(c => c.Name), foreignKey.PrincipalTable.Name, foreignKey.PrincipalColumns.Select(c => c.Name));

    private static string Shape(IEnumerable<string> columns, string principalTable, IEnumerable<string> principalColumns) =>
        $"{string.Join(",", columns)} -> {principalTable} ({string.Join(",", principalColumns)})".ToLowerInvariant();

    /// <summary>
    /// An ALTER TABLE ADD COLUMN for one model column. SQLite refuses a NOT NULL column without a default on a table that has
    /// rows, so a required column with no model default gets the provider type's empty value: '' for text, 0 for numbers and
    /// booleans, the zero date for dates, the empty guid for ids.
    /// </summary>
    private static AddColumnOperation AddColumn(ITable table, IColumn column)
    {
        column.TryGetDefaultValue(out var defaultValue);
        var operation = new AddColumnOperation
        {
            Schema = table.Schema,
            Table = table.Name,
            Name = column.Name,
            ClrType = column.ProviderClrType,
            ColumnType = column.StoreType,
            IsNullable = column.IsNullable,
            DefaultValue = defaultValue,
            DefaultValueSql = column.DefaultValueSql,
            ComputedColumnSql = column.ComputedColumnSql,
            IsStored = column.IsStored,
            MaxLength = column.MaxLength,
            Precision = column.Precision,
            Scale = column.Scale,
            IsUnicode = column.IsUnicode,
            IsFixedLength = column.IsFixedLength,
            Collation = column.Collation,
            Comment = column.Comment,
        };
        if (!operation.IsNullable && operation.DefaultValue is null && operation.DefaultValueSql is null && operation.ComputedColumnSql is null)
        {
            var type = Nullable.GetUnderlyingType(column.ProviderClrType) ?? column.ProviderClrType;
            operation.DefaultValue = type == typeof(string) ? "" : type == typeof(byte[]) ? Array.Empty<byte>() : Activator.CreateInstance(type);
        }

        return operation;
    }

    /// <summary>
    /// A consistent copy of the open database in one self-contained file. <c>VACUUM INTO</c> is a snapshot that never blocks
    /// the app; the copy inherits WAL mode from the source (<see cref="Apply(AppDbContext, ILogger)"/> puts every file there),
    /// which would spread it over three files the moment it is opened, so it is switched to a rollback journal and closed for good.
    /// </summary>
    private static void Snapshot(SqliteConnection source, string target)
    {
        using (var vacuum = source.CreateCommand())
        {
            vacuum.CommandText = "VACUUM INTO $path";
            vacuum.Parameters.AddWithValue("$path", target);
            vacuum.ExecuteNonQuery();
        }

        using var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString());
        copy.Open();
        using var journal = copy.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode = DELETE";
        journal.ExecuteNonQuery();
    }

    private static HashSet<string> ExistingNames(SqliteConnection connection, string type)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type AND name NOT LIKE 'sqlite_%'";
        command.Parameters.AddWithValue("$type", type);
        return ReadNames(command);
    }

    /// <summary>The table's columns and whether each is NOT NULL.</summary>
    private static Dictionary<string, bool> ExistingColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, \"notnull\" FROM pragma_table_info($table)";
        command.Parameters.AddWithValue("$table", table);
        var columns = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns[reader.GetString(0)] = reader.GetInt64(1) != 0;
        }

        return columns;
    }

    /// <summary>The table's foreign keys in the shape <see cref="Shape(IForeignKeyConstraint)"/> gives the model's, so the two can be matched by what they join.</summary>
    private static HashSet<string> ExistingForeignKeys(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, \"table\", \"from\", IFNULL(\"to\", '') FROM pragma_foreign_key_list($table) ORDER BY id, seq";
        command.Parameters.AddWithValue("$table", table);
        var keys = new Dictionary<long, (string Principal, List<string> Columns, List<string> PrincipalColumns)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                if (!keys.TryGetValue(id, out var key))
                {
                    key = (reader.GetString(1), [], []);
                    keys[id] = key;
                }

                key.Columns.Add(reader.GetString(2));
                key.PrincipalColumns.Add(reader.GetString(3));
            }
        }

        return keys.Values.Select(k => Shape(k.Columns, k.Principal, k.PrincipalColumns)).ToHashSet(StringComparer.Ordinal);
    }

    private static bool ForeignKeysEnabled(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys";
        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }

    private static Dictionary<string, long> RowCounts(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<string> tables)
    {
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\"";
            counts[table] = (long)command.ExecuteScalar()!;
        }

        return counts;
    }

    private static HashSet<string> ReadNames(SqliteCommand command)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A recursive copy of a folder the app is writing to. A look deleted, or an account removed, between the listing and the
    /// copy is not a failed backup: what vanished is skipped. Anything else (disk full, a file that cannot be read) is fatal.
    /// </summary>
    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            try
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            try
            {
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
