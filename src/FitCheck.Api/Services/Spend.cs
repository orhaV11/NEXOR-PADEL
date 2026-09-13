using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// What the daily allowances count: stored checks and comparisons (a comparison is a stylist call too) over a rolling
/// 24 hours, failed calls left out because a model outage must not eat anyone's allowance. One place for the check route,
/// the compare route, the "me" answer and the global ceiling, so the four cannot drift from each other.
/// </summary>
public static class Spend
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>When this account's counted calls were made, oldest first.</summary>
    public static Task<List<DateTime>> RecentForUserAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var windowStart = now - Window;
        return RecentAsync(
            CountedChecks(db, windowStart).Where(c => c.UserId == userId),
            CountedComparisons(db, windowStart).Where(c => c.UserId == userId),
            ct);
    }

    /// <summary>When the counted calls made under a guest cookie's token were made, oldest first.</summary>
    public static Task<List<DateTime>> RecentForGuestAsync(AppDbContext db, string token, DateTime now, CancellationToken ct)
    {
        var windowStart = now - Window;
        return RecentAsync(
            CountedChecks(db, windowStart).Where(c => c.UserId == null && c.GuestToken == token),
            CountedComparisons(db, windowStart).Where(c => c.UserId == null && c.GuestToken == token),
            ct);
    }

    /// <summary>How many counted calls this account made in the rolling day (MeDto.checksToday).</summary>
    public static async Task<int> CountForUserAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var windowStart = now - Window;
        return await CountedChecks(db, windowStart).CountAsync(c => c.UserId == userId, ct)
               + await CountedComparisons(db, windowStart).CountAsync(c => c.UserId == userId, ct);
    }

    /// <summary>Everyone's counted calls in the rolling day, accounts and guests, checks and comparisons: what Limits:ChecksPerDayGlobal caps.</summary>
    public static async Task<int> StoredGlobalAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var windowStart = now - Window;
        return await CountedChecks(db, windowStart).CountAsync(ct) + await CountedComparisons(db, windowStart).CountAsync(ct);
    }

    /// <summary>
    /// Seconds until a caller refused at <paramref name="cap"/> gets a permit back: the count has to drop below the cap,
    /// so it is the (count - cap + 1)th oldest call that must leave the window, not the oldest one (the two are the same
    /// only while the count equals the cap; a lapsed Pro, or a lowered cap, leaves more calls in the window than the cap
    /// allows). Null when nothing is stored yet, when only calls in flight filled the cap.
    /// </summary>
    public static int? RetryAfterSeconds(IReadOnlyList<DateTime> recent, int cap, DateTime now)
    {
        if (recent.Count == 0)
        {
            return null;
        }

        var index = Math.Clamp(recent.Count - cap, 0, recent.Count - 1);
        return Math.Max(1, (int)Math.Ceiling((recent[index] + Window - now).TotalSeconds));
    }

    private static IQueryable<OutfitCheck> CountedChecks(AppDbContext db, DateTime windowStart) =>
        db.Checks.Where(c => c.CreatedAt >= windowStart && c.Status != CheckStatus.Error);

    private static IQueryable<OutfitComparison> CountedComparisons(AppDbContext db, DateTime windowStart) =>
        db.Comparisons.Where(c => c.CreatedAt >= windowStart && c.Status != CheckStatus.Error);

    private static async Task<List<DateTime>> RecentAsync(IQueryable<OutfitCheck> checks, IQueryable<OutfitComparison> comparisons, CancellationToken ct)
    {
        var checkTimes = await checks.Select(c => c.CreatedAt).ToListAsync(ct);
        var comparisonTimes = await comparisons.Select(c => c.CreatedAt).ToListAsync(ct);
        return checkTimes.Concat(comparisonTimes).OrderBy(t => t).ToList();
    }
}
