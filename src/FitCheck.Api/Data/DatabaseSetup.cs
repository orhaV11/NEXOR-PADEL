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
/// It is copied to <c>&lt;file&gt;.bak-&lt;stamp&gt;</c> first, then every missing table, column and index is added from the
/// EF model (never from a hand-written list, so this keeps working as the model grows), and the history table is written
/// as if the migrations had run. From then on it is the second case.</item>
/// </list>
/// <para>
/// Migrations live in <c>Data/Migrations</c>. After changing the model, from the repository root (once:
/// <c>dotnet tool install -g dotnet-ef</c>):
/// <code>dotnet ef migrations add &lt;Name&gt; --project src/FitCheck.Api --output-dir Data/Migrations</code>
/// While nothing has shipped, regenerate the single initial migration instead of stacking: delete <c>Data/Migrations</c>
/// and run the same command with the name <c>InitialCreate</c>. <c>DatabaseSetupTests</c> checks the migrations produce
/// exactly the schema the model describes.
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
        }
        finally
        {
            db.Database.CloseConnection();
        }
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
            CopyDirectory(storageRoot, storage);
        }

        return (database, storage);
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
        // that were already there, then indexes, which may cover new columns.
        var operations = new List<MigrationOperation>();
        var created = new List<string>();
        var added = new List<string>();
        foreach (var operation in fromScratch.OfType<CreateTableOperation>().Where(o => !existingTables.Contains(o.Name)))
        {
            operations.Add(operation);
            created.Add(operation.Name);
        }

        foreach (var table in relational.Tables.Where(t => existingTables.Contains(t.Name)))
        {
            var columns = ExistingColumns(connection, table.Name);
            foreach (var column in table.Columns.Where(c => !columns.Contains(c.Name)))
            {
                operations.Add(AddColumn(table, column));
                added.Add($"{table.Name}.{column.Name}");
            }
        }

        operations.AddRange(fromScratch.OfType<CreateIndexOperation>().Where(o => !existingIndexes.Contains(o.Name)));

        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
        var history = db.GetService<IHistoryRepository>();
        var migrations = db.Database.GetMigrations().ToList();
        using (var transaction = connection.BeginTransaction())
        {
            foreach (var command in commands)
            {
                Execute(connection, transaction, command.CommandText);
            }

            Execute(connection, transaction, history.GetCreateIfNotExistsScript());
            foreach (var migration in migrations)
            {
                Execute(connection, transaction, history.GetInsertScript(new HistoryRow(migration, ProductInfo.GetVersion())));
            }

            transaction.Commit();
        }

        logger.LogInformation(
            "Database {DataSource} upgraded: {TableCount} table(s) created ({Tables}), {ColumnCount} column(s) added ({Columns}), " +
            "{IndexCount} index(es) created; recorded {Migrations} as applied.",
            dataSource, created.Count, string.Join(", ", created), added.Count, string.Join(", ", added),
            operations.Count(o => o is CreateIndexOperation), string.Join(", ", migrations));
    }

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
    /// the app; the copy inherits WAL mode from the source (EF creates its files that way), which would spread it over
    /// three files the moment it is opened, so it is switched to a rollback journal and closed for good.
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

    private static HashSet<string> ExistingColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($table)";
        command.Parameters.AddWithValue("$table", table);
        return ReadNames(command);
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

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}
