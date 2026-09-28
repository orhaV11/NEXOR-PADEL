using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 20 — the guest who met the day ceiling and asked, at signup, for one note when the stylist is back.
/// <para>
/// Copy alone was refused: "we'll tell you when the stylist is back" with nothing behind it is exactly the kind of
/// promise this codebase does not make. The smallest honest mechanism, with no schema change, is a <see cref="Counter"/>
/// row per account (<see cref="AskedName"/>, the <c>welcome:{id}</c> precedent), set by the signup route only while the
/// ceiling is really closed, and this pass every five minutes: once <see cref="SpendMeter.CeilingReachedAsync"/> says
/// the day is open again, each row becomes one activity line through the <see cref="Notifier"/> (which is also the push
/// path, so a browser that subscribed hears it on the lock screen), one mail where the account has a confirmed address
/// and a public origin is configured for the link, and then the row goes, so it happens once. A row for an account that
/// no longer exists or is suspended is simply removed. A mail that fails to send is logged and the row still goes: the
/// in-app line was written, and that is the part of the promise every account gets.
/// </para>
/// <para>
/// Review fixes: the note keeps the try-tip nudge's day (<see cref="PushOptions.TryTipDayStart"/> to
/// <see cref="PushOptions.TryTipDayEnd"/>, local to Board:TimeZone). The ceiling reopens at UTC midnight, which is the
/// small hours here, and a phone that buzzes at three to say the stylist is back is not what anybody asked for; the rows
/// wait for the first pass of the morning. And each person's row goes, with their line, in a save of its own BEFORE the
/// mail: a save that fails, or a shutdown in the middle of the pass, leaves that row for the next pass and never mails
/// somebody a second time (the mail says there will not be another).
/// </para>
/// </summary>
public sealed class StylistBack(
    IServiceScopeFactory scopes,
    SpendMeter meter,
    IEmailSender email,
    IOptions<EmailOptions> mailOptions,
    IOptions<BillingOptions> billingOptions,
    IOptions<PushOptions> pushOptions,
    Board board,
    IClock clock,
    Localizer localizer,
    ILogger<StylistBack> logger)
{
    /// <summary>The prefix every ask shares; the pass loads its rows by it.</summary>
    public const string Prefix = "stylist_back:";

    /// <summary>The Counter row that records the ask.</summary>
    public static string AskedName(Guid userId) => $"{Prefix}{userId:N}";

    /// <summary>Email:PublicOrigin, else Billing:PublicOrigin, else empty: a mailed link is never built from a request.</summary>
    public string Origin()
    {
        var configured = mailOptions.Value.PublicOrigin.Trim().TrimEnd('/');
        return configured.Length > 0 ? configured : billingOptions.Value.PublicOrigin.Trim().TrimEnd('/');
    }

    /// <summary>Whether this instant is inside the local day the note may go out in (the try-tip nudge's hours).</summary>
    public bool InsideTheDay(DateTime utcNow)
    {
        var settings = pushOptions.Value;
        var localHour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), board.Zone).Hour;
        return localHour >= settings.TryTipDayStart && localHour < settings.TryTipDayEnd;
    }

    /// <summary>
    /// Runs one pass and returns how many people were told. Zero while the stylist still rests, and zero in the night:
    /// the rows wait.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        if (!InsideTheDay(clock.UtcNow))
        {
            return 0;
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Counters.Where(c => c.Name.StartsWith(Prefix)).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return 0;
        }

        if (await meter.CeilingReachedAsync(db, ct))
        {
            return 0;
        }

        var notifier = scope.ServiceProvider.GetRequiredService<Notifier>();
        var origin = Origin();
        var told = 0;
        foreach (var row in rows)
        {
            // A row whose account is gone or suspended is simply removed.
            var user = Guid.TryParseExact(row.Name[Prefix.Length..], "N", out var userId)
                ? await db.Users.SingleOrDefaultAsync(u => u.Id == userId && !u.Suspended, ct)
                : null;
            db.Counters.Remove(row);
            if (user is not null)
            {
                // The person is their own actor, as on a board place: not a pair, so the block check passes, and the push
                // goes out through the same job the activity row queues.
                await notifier.AddAsync(user.Id, NotificationType.StylistBack, user.Handle, null, null, ct);
            }

            // The claim is committed before anything leaves: the row goes with the line in one save. If it fails (or
            // another process removed the row first), nothing was sent, the context lets go of both, and the next pass
            // finds whatever is still there.
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "StylistBack: a row was not claimed; it waits for the next pass");
                continue;
            }

            if (user is null)
            {
                continue;
            }

            told++;
            if (email.Enabled && mailOptions.Value.Enabled && user.Email is not null && user.EmailVerifiedAt is not null && origin.Length > 0)
            {
                var language = Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : Localizer.DefaultLocale;
                try
                {
                    await email.SendAsync(new EmailMessage(
                        user.Email,
                        localizer.Get(language, "mail.stylist_back_subject"),
                        localizer.Get(language, "mail.stylist_back_body", user.Name, origin + "/#/check")), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "StylistBack: the mail to {Handle} did not go out; the in-app line is written and the row goes", user.Handle);
                }
            }
        }

        if (told > 0)
        {
            logger.LogInformation("StylistBack: told {Count} account(s) the stylist is back", told);
        }

        return told;
    }
}

/// <summary>Wakes every five minutes; the first pass at start. A failed pass is logged and tried again on the next tick.</summary>
public sealed class StylistBackService(StylistBack worker, ILogger<StylistBackService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

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
            logger.LogWarning(ex, "StylistBack: the run failed; it runs again on the next tick");
        }
    }
}
