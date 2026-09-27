using System.Globalization;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 20 — the morning loop behind a switch: at Plans:TomorrowMorningHour in Board:TimeZone, one push per day to
/// accounts with a push subscription, a wardrobe of two kinds and something left to spend, saying today's outfit is a
/// tap away. It never composes: the class takes neither <see cref="Tomorrow"/> nor the vision client, and the tap does
/// the composing inside the person's own allowance. Round 19 kept this out because a push about tomorrow "would spend on
/// people who did not ask"; this one spends nothing, is off by default for the server, and is a switch the person owns.
/// <para>
/// The gates run in the compose route's own order, cheap and global first, so a tap can never land on a refusal the
/// sender could have foreseen: the server flag, Tomorrow itself, push, the hour; then per account the plan, the wardrobe
/// switch, enough pieces of enough kinds, no outfit for today yet, and something left today and this month. The money
/// ceiling is deliberately not checked: it resets at UTC midnight, before any morning here, and a stored answer is served
/// while the stylist rests.
/// </para>
/// <para>
/// One <see cref="TomorrowPush"/> row per account per local day is written and saved BEFORE the push is queued: the
/// unique key makes a second process's attempt a no-op, and a crash between the two costs one morning, never a double.
/// The row is also the metric, and its OpenedAt is stamped by the tap (<c>GET /api/tomorrow?from=push</c>).
/// </para>
/// </summary>
public sealed class TomorrowMorning(
    IServiceScopeFactory scopes, Board board, IClock clock, PushSender push, IOptions<PlanOptions> plans, IOptions<LimitsOptions> limits, ILogger<TomorrowMorning> logger)
{
    public sealed record Run(int Sent, int Skipped, string Reason = "");

    public static readonly TimeOnly DefaultHour = new(7, 30);

    /// <summary>How long after the hour a push still says "today" truthfully; past it the morning is gone and the day is skipped.</summary>
    public static readonly TimeSpan SendWindow = TimeSpan.FromHours(3);

    /// <summary>How long after a push a <c>?from=push</c> read counts as opening it.</summary>
    public static readonly TimeSpan OpenWindow = TimeSpan.FromHours(24);

    private readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>
    /// A test's stand-in for another process: called with the account and the day once its receipt row is added and
    /// before it is saved, so the test can write the same row from the side and watch this run give way. Null outside tests.
    /// </summary>
    public Func<Guid, DateOnly, CancellationToken, Task>? BeforeSave { get; set; }

    /// <summary>The configured local hour, or 07:30 when the setting is not HH:mm (the doctor warns).</summary>
    public static TimeOnly HourOf(PlanOptions plans) =>
        TimeOnly.TryParseExact((plans.TomorrowMorningHour ?? "").Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hour) ? hour : DefaultHour;

    /// <summary>Whether the setting parses at all; the doctor's line reads this to say when the fallback is in use.</summary>
    public static bool HourIsValid(PlanOptions plans) =>
        TimeOnly.TryParseExact((plans.TomorrowMorningHour ?? "").Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>
    /// Today's send moment, when this instant is inside its window: the local date in Board:TimeZone, the configured hour
    /// on that date as a UTC instant, and null before the hour or more than <see cref="SendWindow"/> after it. The local
    /// day is the row key.
    /// </summary>
    public (DateTime DueAtUtc, DateOnly Day)? Due(DateTime nowUtc)
    {
        var zone = board.Zone;
        var utc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
        var due = Board.LocalToUtc(day, HourOf(plans.Value), zone);
        return utc < due || utc - due > SendWindow ? null : (due, day);
    }

    /// <summary>One pass. Safe on a schedule or from a test; two calls at once run one after the other.</summary>
    public async Task<Run> RunAsync(CancellationToken ct)
    {
        await _running.WaitAsync(ct);
        try
        {
            return await RunCoreAsync(ct);
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task<Run> RunCoreAsync(CancellationToken ct)
    {
        var settings = plans.Value;
        if (!settings.TomorrowMorningPush)
        {
            logger.LogDebug("TomorrowMorning: Plans:TomorrowMorningPush is off, nothing sent");
            return new Run(0, 0, "off");
        }

        if (!settings.TomorrowEnabled)
        {
            // A tap would land on a 404.
            logger.LogDebug("TomorrowMorning: Plans:TomorrowEnabled is off, nothing sent");
            return new Run(0, 0, "tomorrow off");
        }

        if (!push.Enabled)
        {
            logger.LogDebug("TomorrowMorning: push is off (no VAPID keys), nothing sent");
            return new Run(0, 0, "push off");
        }

        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        if (Due(now) is not var (dueAtUtc, day))
        {
            return new Run(0, 0, "not due");
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Pilot scale: the subscribed accounts not yet pushed today, as a list. The person's own switch and a suspension
        // are read here; everything after is per account, in the compose route's order.
        var subscribed = db.PushSubscriptions.Select(s => s.UserId).Distinct();
        var candidates = await db.Users
            .Where(u => subscribed.Contains(u.Id) && !u.Suspended && u.TomorrowPushOn
                && !db.TomorrowPushes.Any(p => p.UserId == u.Id && p.Day == day))
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(ct);

        // "Composed today already": an outfit for this date (last night's "for tomorrow" is today's outfit), or anything
        // composed since this local day began (an early bird who asked for "today" before the hour, whatever date their
        // phone put on it). Yesterday's outfit for yesterday does not silence today's ping.
        var dayStartUtc = Board.LocalToUtc(day, TimeOnly.MinValue, board.Zone);

        var sent = 0;
        var skipped = 0;
        foreach (var user in candidates)
        {
            try
            {
                var reason = await ReasonToSkipAsync(db, user, day, dayStartUtc, now, ct);
                if (reason is not null)
                {
                    skipped++;
                    logger.LogDebug("TomorrowMorning: {UserId} skipped: {Reason}", user.Id, reason);
                    continue;
                }

                // The receipt first, then the push: the unique key is what makes this once a morning across processes.
                db.TomorrowPushes.Add(new TomorrowPush { Id = Guid.NewGuid(), UserId = user.Id, Day = day, SentAt = now });
                if (BeforeSave is { } beforeSave)
                {
                    await beforeSave(user.Id, day, ct);
                }

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    db.ChangeTracker.Clear();
                    skipped++;
                    logger.LogInformation("TomorrowMorning: {UserId} was already pushed for {Day} by another run; nothing sent", user.Id, day);
                    continue;
                }

                push.Enqueue(new PushJob(user.Id, NotificationType.TomorrowMorning, user.Handle, null, null));
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // One account's failure must not cost everyone else their morning; the window is open until the next tick.
                db.ChangeTracker.Clear();
                skipped++;
                logger.LogWarning(ex, "TomorrowMorning: {UserId} was not pushed; the run goes on", user.Id);
            }
        }

        logger.LogInformation("TomorrowMorning: run at {Now:o} for {Day}, {Sent} sent, {Skipped} skipped (due {Due:o})", now, day, sent, skipped, dueAtUtc);
        return new Run(sent, skipped);
    }

    /// <summary>
    /// The spend-free gates of the compose route, in its order; null means the push goes. Nothing here asks the model.
    /// </summary>
    private async Task<string?> ReasonToSkipAsync(AppDbContext db, AppUser user, DateOnly day, DateTime dayStartUtc, DateTime now, CancellationToken ct)
    {
        var settings = plans.Value;
        var isPro = Plans.IsPro(user, now);
        var cap = isPro ? Plans.ProSuggestionCap(settings, limits.Value) : Plans.FreeSuggestionCap(settings, limits.Value);
        if (!Plans.TomorrowReachesStylist(user, settings, now) || cap <= 0 || settings.WardrobeNamesFor(isPro) <= 0)
        {
            return "plan";
        }

        if (!await Wardrobe.ToStylistAsync(db, user.Id, ct))
        {
            return "wardrobe switch off";
        }

        var offered = await Tomorrow.OfferedAsync(db, user, settings, isPro, ct);
        if (offered.Count < Math.Max(0, settings.SuggestionMinPieces) || Tomorrow.Kinds(Tomorrow.Refs(offered)).Count < Math.Max(0, settings.SuggestionMinCategories))
        {
            return "not enough pieces";
        }

        if (await db.Suggestions.AnyAsync(s => s.UserId == user.Id && s.Status != CheckStatus.Error && (s.ForDate == day || s.CreatedAt >= dayStartUtc), ct))
        {
            return "composed today already";
        }

        var numbers = await Tomorrow.NumbersAsync(db, user, settings, limits.Value, now, ct);
        if (numbers.LeftToday <= 0 || (numbers.CapMonth > 0 && numbers.LeftMonth <= 0))
        {
            return "nothing left to spend";
        }

        return null;
    }

    /// <summary>
    /// The tap: the newest push to this account inside <see cref="OpenWindow"/> that is not yet opened gets its stamp.
    /// Returns whether one was stamped. Counts once per push; a read with no push behind it counts nothing.
    /// </summary>
    public static async Task<bool> MarkOpenedAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var since = now - OpenWindow;
        var row = await db.TomorrowPushes
            .Where(p => p.UserId == userId && p.OpenedAt == null && p.SentAt >= since)
            .OrderByDescending(p => p.SentAt)
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return false;
        }

        row.OpenedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>SQLite's constraint error (19) for the unique index on account and day.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19 } || ex.InnerException?.InnerException is SqliteException { SqliteErrorCode: 19 };
}

/// <summary>Wakes every quarter hour; the first pass at start. A failed pass is logged and tried again on the next tick.</summary>
public sealed class TomorrowMorningService(TomorrowMorning worker, ILogger<TomorrowMorningService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunSafelyAsync(stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task RunSafelyAsync(CancellationToken ct)
    {
        try
        {
            await worker.RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TomorrowMorning: the run failed; it runs again on the next tick");
        }
    }
}
