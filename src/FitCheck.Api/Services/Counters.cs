using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// Named tallies that survive a restart (<see cref="Counter"/>): an out-click on a store link, a board view. One row per
/// name, incremented in place with an upsert so two requests never race a read-modify-write; the metrics read them.
/// Never a number the app decides anything on.
/// </summary>
public static class Counters
{
    public static Task IncrementAsync(AppDbContext db, string name, CancellationToken ct, long by = 1) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"Counters\" (\"Name\", \"Value\") VALUES ({name}, {by}) ON CONFLICT(\"Name\") DO UPDATE SET \"Value\" = \"Value\" + {by}", ct);

    /// <summary>The tally, or 0 when nothing has been counted under the name yet.</summary>
    public static async Task<long> ReadAsync(AppDbContext db, string name, CancellationToken ct) =>
        await db.Counters.Where(c => c.Name == name).Select(c => (long?)c.Value).FirstOrDefaultAsync(ct) ?? 0;
}
