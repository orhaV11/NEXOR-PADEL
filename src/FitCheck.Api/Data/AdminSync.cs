using FitCheck.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Data;

/// <summary>What <see cref="AdminSync.SetAdminAsync"/> found: no such account, the flag flipped, or it already had that value.</summary>
public enum AdminChange
{
    NotFound,
    Changed,
    Unchanged
}

/// <summary>
/// The two ways an account becomes (or stops being) a moderator, both writing <see cref="AppUser.IsAdmin"/>:
/// <list type="bullet">
/// <item>At start, right after the schema is current, every existing account whose handle is in Admin:Handles is promoted.
/// The sync never demotes: taking a handle off the list keeps the moderator; only <c>--unadmin</c> removes one.</item>
/// <item><c>dotnet FitCheck.Api.dll --admin &lt;handle&gt;</c> and <c>--unadmin &lt;handle&gt;</c> set and clear the flag for one
/// existing account, from a shell on the box, while the app runs or not.</item>
/// </list>
/// The account must exist first: signup refuses the listed handles, so the owner signs up before listing the handle.
/// </summary>
public static class AdminSync
{
    public static async Task ApplyAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handles = scope.ServiceProvider.GetRequiredService<IOptions<AdminOptions>>().Value.Handles;
        await PromoteListedAsync(db, handles, logger, CancellationToken.None);
    }

    /// <summary>Promotes every existing account on the list, logging each one. Returns how many were promoted just now.</summary>
    public static async Task<int> PromoteListedAsync(AppDbContext db, IReadOnlyList<string> handles, ILogger logger, CancellationToken ct)
    {
        var listed = handles.Select(h => h.Trim().ToLowerInvariant()).Where(h => h.Length > 0).Distinct().ToList();
        if (listed.Count == 0)
        {
            return 0;
        }

        var users = await db.Users.Where(u => listed.Contains(u.HandleLower) && !u.IsAdmin).ToListAsync(ct);
        foreach (var user in users)
        {
            user.IsAdmin = true;
            logger.LogInformation("Account {Handle} is listed in Admin:Handles: promoted to moderator.", user.Handle);
        }

        var missing = listed.Where(h => !users.Any(u => u.HandleLower == h)).ToList();
        if (missing.Count > 0)
        {
            // Either already a moderator (fine) or not signed up yet: the handle is reserved, the owner signs up, then restarts.
            var already = (await db.Users.Where(u => missing.Contains(u.HandleLower) && u.IsAdmin).Select(u => u.HandleLower).ToListAsync(ct)).ToHashSet();
            foreach (var handle in missing.Where(h => !already.Contains(h)))
            {
                logger.LogWarning("Admin:Handles lists {Handle}, but no account has that handle yet; sign up with it, then restart.", handle);
            }
        }

        if (users.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return users.Count;
    }

    /// <summary>
    /// Behind <c>--admin</c> / <c>--unadmin</c>: opens the database by its connection string (which must already exist: this
    /// never creates one) and sets the flag on the account with that handle, case-insensitively.
    /// </summary>
    public static async Task<AdminChange> SetAdminAsync(string connectionString, string handle, bool isAdmin, CancellationToken ct = default)
    {
        var connection = new SqliteConnectionStringBuilder(connectionString);
        if (!connection.DataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            connection.Mode = SqliteOpenMode.ReadWrite;
        }

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection.ConnectionString).Options);
        var lower = handle.Trim().TrimStart('@').ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.HandleLower == lower, ct);
        if (user is null)
        {
            return AdminChange.NotFound;
        }

        if (user.IsAdmin == isAdmin)
        {
            return AdminChange.Unchanged;
        }

        user.IsAdmin = isAdmin;
        await db.SaveChangesAsync(ct);
        return AdminChange.Changed;
    }
}
