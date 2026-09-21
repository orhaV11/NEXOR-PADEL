using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// What the daily allowances count: stored checks and comparisons (a comparison is a stylist call too) over a rolling
/// 24 hours, failed calls left out because a model outage must not eat anyone's allowance. One place for the check route,
/// the compare route, the "me" answer and the global ceiling, so the four cannot drift from each other.
/// Round 13: a "no outfit in this photo" answer gave the person nothing, so the first <c>forgivenNoOutfit</c> of them in
/// the window (Plans:NoOutfitForgivenPerDay, oldest first) are left out of the per-person counts; the ones after that
/// count like any stored call, so a stream of non-outfit photos still meets a cap. The global ceiling counts all of them:
/// it is about the bill, and every one was a model call.
/// Round 13 also puts the invite bonus here (the block at the end of this file): an accepted invite gives both accounts
/// one more check for that day, as a Counter row this file takes off the front of an account's counted calls, so the
/// check route, the comparison route and the "me" answer honour it without a line of their own.
/// Round 14: a PRO account's day is counted in two buckets (<see cref="Allowance"/>) — its checks apart from its
/// comparisons — so deciding between two outfits never spends a check. A free account and a guest keep the one bucket
/// they always had, and the global ceiling counts every call whatever the plan: it is about the bill.
/// </summary>
public static class Spend
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>
    /// Round 16 - the window the monthly allowance is counted over. A rolling 30 days rather than a calendar month, for
    /// the same reason the day is rolling: no cliff at midnight on the 1st, and nothing to reset.
    /// </summary>
    public static readonly TimeSpan MonthWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// Every counted call this account made in the rolling month - checks AND comparisons, both Pro buckets added back
    /// together, with no forgiveness and no invite bonus taken off. Those two are kindnesses about a single day; the
    /// month is about the bill, and a call that was made was paid for whichever bucket it landed in.
    /// </summary>
    public static async Task<int> MonthCountForUserAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var from = now - MonthWindow;
        return await CountedChecks(db, from).Where(c => c.UserId == userId).CountAsync(ct)
            + await CountedComparisons(db, from).Where(c => c.UserId == userId).CountAsync(ct);
    }

    /// <summary>
    /// When this account's counted calls were made, oldest first. Round 13: the day's invite bonus (see the block at the
    /// end of this file) is taken off the front of the list, so one extra check is what every caller sees — the check
    /// route, the comparison route and the "me" answer alike, with nothing to change in any of them.
    /// </summary>
    public static async Task<List<DateTime>> RecentForUserAsync(
        AppDbContext db, Guid userId, DateTime now, CancellationToken ct, int forgivenNoOutfit = 0, Allowance allowance = Allowance.Together)
    {
        var windowStart = now - Window;
        var times = await RecentAsync(
            allowance == Allowance.Compares ? null : CountedChecks(db, windowStart).Where(c => c.UserId == userId),
            allowance == Allowance.Checks ? null : CountedComparisons(db, windowStart).Where(c => c.UserId == userId),
            forgivenNoOutfit, ct);
        // The day's invite bonus is the check side's: one extra CHECK is what an invite promises, so a Pro account's
        // comparison bucket never quietly grows by it.
        return allowance == Allowance.Compares ? times : DropBonus(times, await BonusAsync(db, userId, now, ct));
    }

    /// <summary>When the counted calls made under a guest cookie's token were made, oldest first.</summary>
    public static Task<List<DateTime>> RecentForGuestAsync(AppDbContext db, string token, DateTime now, CancellationToken ct, int forgivenNoOutfit = 0)
    {
        var windowStart = now - Window;
        return RecentAsync(
            CountedChecks(db, windowStart).Where(c => c.UserId == null && c.GuestToken == token),
            CountedComparisons(db, windowStart).Where(c => c.UserId == null && c.GuestToken == token),
            forgivenNoOutfit, ct);
    }

    /// <summary>How many counted calls this account made in the rolling day (MeDto.checksToday).</summary>
    public static async Task<int> CountForUserAsync(
        AppDbContext db, Guid userId, DateTime now, CancellationToken ct, int forgivenNoOutfit = 0, Allowance allowance = Allowance.Together) =>
        (await RecentForUserAsync(db, userId, now, ct, forgivenNoOutfit, allowance)).Count;

    /// <summary>Everyone's counted calls in the rolling day, accounts and guests, checks and comparisons: what Limits:ChecksPerDayGlobal caps.</summary>
    public static async Task<int> StoredGlobalAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var windowStart = now - Window;
        return await CountedChecks(db, windowStart).CountAsync(ct) + await CountedComparisons(db, windowStart).CountAsync(ct);
    }

    /// <summary>
    /// Whether a stored no-outfit check is one of the forgiven ones: among the no-outfit checks and comparisons of its
    /// owner (or guest cookie) in the day up to and including itself, it is within the first <paramref name="forgivenNoOutfit"/>.
    /// The check route reads this after the save, to know whether the guest address's look was spent and to tell the client.
    /// </summary>
    public static async Task<bool> IsForgivenAsync(AppDbContext db, OutfitCheck check, int forgivenNoOutfit, CancellationToken ct)
    {
        if (check.Status != CheckStatus.NotOutfit || forgivenNoOutfit <= 0)
        {
            return false;
        }

        var windowStart = check.CreatedAt - Window;
        var checks = db.Checks.Where(c => c.CreatedAt >= windowStart && c.CreatedAt <= check.CreatedAt && c.Status == CheckStatus.NotOutfit && c.Id != check.Id);
        var comparisons = db.Comparisons.Where(c => c.CreatedAt >= windowStart && c.CreatedAt <= check.CreatedAt && c.Status == CheckStatus.NotOutfit);
        int before;
        if (check.UserId is { } userId)
        {
            before = await checks.CountAsync(c => c.UserId == userId, ct) + await comparisons.CountAsync(c => c.UserId == userId, ct);
        }
        else
        {
            before = await checks.CountAsync(c => c.UserId == null && c.GuestToken == check.GuestToken, ct)
                + await comparisons.CountAsync(c => c.UserId == null && c.GuestToken == check.GuestToken, ct);
        }

        return before < forgivenNoOutfit;
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

    /// <summary>
    /// The counted calls in time order: everything stored with a status that cost a model call, minus the first
    /// <paramref name="forgivenNoOutfit"/> no-outfit answers of the window (oldest first, checks and comparisons alike).
    /// </summary>
    private static async Task<List<DateTime>> RecentAsync(IQueryable<OutfitCheck>? checks, IQueryable<OutfitComparison>? comparisons, int forgivenNoOutfit, CancellationToken ct)
    {
        // Round 14: a null side is a bucket this allowance does not count (Pro's checks apart from its comparisons).
        // Left out here rather than read and discarded, so a split day is one database round trip, not two.
        var checkRows = checks is null ? [] : await checks.Select(c => new { c.CreatedAt, c.Status }).ToListAsync(ct);
        var comparisonRows = comparisons is null ? [] : await comparisons.Select(c => new { c.CreatedAt, c.Status }).ToListAsync(ct);
        var forgiven = 0;
        var times = new List<DateTime>();
        foreach (var row in checkRows.Concat(comparisonRows).OrderBy(r => r.CreatedAt))
        {
            if (row.Status == CheckStatus.NotOutfit && forgiven < forgivenNoOutfit)
            {
                forgiven++;
                continue;
            }

            times.Add(row.CreatedAt);
        }

        return times;
    }

    // ---------- Round 13 — the growth loop: the invite bonus in the allowance ----------

    /// <summary>
    /// The Counter row that holds one account's extra checks for one UTC day: <c>bonus:{userId:N}:{yyyyMMdd}</c>. An
    /// invite writes it (AuthEndpoints, at signup, once for each side of the pair) and only this file reads it.
    /// </summary>
    public static string BonusName(Guid userId, DateOnly day) => $"bonus:{userId:N}:{day:yyyyMMdd}";

    /// <summary>The extra checks this account has today, or 0. Never negative, and never more than a day's ceiling could absorb.</summary>
    public static async Task<int> BonusAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var bonus = await Counters.ReadAsync(db, BonusName(userId, DateOnly.FromDateTime(now)), ct);
        return (int)Math.Clamp(bonus, 0, 100);
    }

    /// <summary>
    /// Gives an account <paramref name="extra"/> more checks for the UTC day of <paramref name="now"/>. What "one extra
    /// check for today" means: the bonus is a day's, not the rolling window's, so it is gone tomorrow whatever was spent.
    /// </summary>
    public static Task GrantBonusAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct, int extra = 1) =>
        Counters.IncrementAsync(db, BonusName(userId, DateOnly.FromDateTime(now)), ct, extra);

    /// <summary>
    /// Takes the bonus off the counted calls, oldest first, the way a forgiven no-outfit answer is taken off: what is
    /// left is what the cap is measured against, so the cap, the retry-after and me.checksToday all say the same thing.
    /// The global ceiling never sees this — it is about the bill, and every one of those calls was made.
    /// </summary>
    private static List<DateTime> DropBonus(List<DateTime> times, int bonus) =>
        bonus <= 0 || times.Count == 0 ? times : times.Skip(Math.Min(bonus, times.Count)).ToList();
}
