using System.Globalization;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 20 — the pre-renewal recap: three days before a paying Pro's charge, one mail with three plain numbers the
/// model did not write and the way to manage the subscription; once per period. The same hourly pass prunes handled
/// Stripe events older than <see cref="BillingEndpoints.StripeEventKeep"/>, always, even with mail off.
/// <para>
/// Who gets it: an account whose Pro is running, that follows a subscription (<see cref="AppUser.BillingSubscriptionId"/>;
/// a --pro grant has no renewal and no portal) that will really charge (<see cref="AppUser.BillingRenews"/> is not
/// false: not set to cancel at the period end, not a trial with no card, not a declined renewal), with a confirmed
/// address, not suspended, and whose <see cref="AppUser.RenewalRecapUntil"/> is not already this charge. The charge is
/// <see cref="AppUser.BillingPeriodEnd"/>, the period Stripe named (review of Round 20: ProUntil carries a Checkout's
/// 35 or 368 days and any gift under them, so it named the wrong day for every first renewal); an account from before
/// that column falls back to <see cref="AppUser.ProUntil"/> minus <see cref="BillingEndpoints.RenewalSlack"/>, as
/// Round 20 did, until Stripe's next event names it. The send moment is <see cref="Lead"/> before the charge. A renewal
/// moves the charge on, and that is what makes the mail once per period. It is transactional, like the card and the
/// "Pro ended" letters: it ignores <see cref="AppUser.DigestOn"/> and carries no unsubscribe link, because it is the one
/// mail that can save the person money. The date is written in the person's language.
/// </para>
/// <para>
/// The three numbers are computed from the database over the last <see cref="Window"/>: comparisons decided, outfits
/// planned and how many of them were worn, and tips that named a piece the person already owned - the last one by
/// matching each check's stored tip against the wardrobe as it stood when the check was made. Nothing is sent when mail
/// is off or no public origin is configured: the mail's one link must never be built from a request, and this runs
/// without one.
/// </para>
/// </summary>
public sealed class RenewalRecap(
    IServiceScopeFactory scopes,
    IClock clock,
    IEmailSender email,
    IOptions<EmailOptions> emailOptions,
    IOptions<BillingOptions> billingOptions,
    Localizer localizer,
    ILogger<RenewalRecap> logger)
{
    /// <summary>What one run did, for the log line and the tests: mails sent, sends that failed and wait for the next hour, event rows pruned.</summary>
    public sealed record Run(int Sent, int Skipped, int Pruned);

    /// <summary>The three numbers of one account over the window.</summary>
    public sealed record Numbers(int Compared, int Planned, int Worn, int OwnedTips);

    /// <summary>How long before the charge the mail goes: enough to change the card or cancel, not so long it is forgotten.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromDays(3);

    /// <summary>The month the numbers are counted over.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>One pass: the pruning, then the recaps that are due. Safe to call on a schedule or from a test; never throws for one person's failed send.</summary>
    public async Task<Run> RunAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.UtcNow;

        var keepFrom = now - BillingEndpoints.StripeEventKeep;
        var pruned = await db.StripeEvents.Where(e => e.ReceivedAt < keepFrom).ExecuteDeleteAsync(ct);

        if (!email.Enabled)
        {
            logger.LogInformation("RenewalRecap: mail is off, nothing sent; {Pruned} old Stripe events pruned", pruned);
            return new Run(0, 0, pruned);
        }

        if (Origin() is not { Length: > 0 } origin)
        {
            logger.LogWarning("RenewalRecap: no Email:PublicOrigin (or Billing:PublicOrigin), so no link can be built and no mail goes out; {Pruned} old Stripe events pruned", pruned);
            return new Run(0, 0, pruned);
        }

        // Due when charge - lead <= now < charge, written the way the database can compare it: the period Stripe named,
        // or, for an account from before that column, ProUntil - slack.
        var chargeBy = now + Lead;
        var dueBefore = now + BillingEndpoints.RenewalSlack + Lead;
        var candidates = await db.Users
            .Where(u => u.Plan == Plans.Pro && u.ProUntil != null && u.ProUntil > now
                && u.BillingSubscriptionId != null && u.BillingRenews != false
                && u.Email != null && u.EmailVerifiedAt != null && !u.Suspended
                && ((u.BillingPeriodEnd != null && u.BillingPeriodEnd > now && u.BillingPeriodEnd <= chargeBy
                        && (u.RenewalRecapUntil == null || u.RenewalRecapUntil < u.BillingPeriodEnd))
                    || (u.BillingPeriodEnd == null && u.ProUntil <= dueBefore)))
            .ToListAsync(ct);

        var sent = 0;
        var skipped = 0;
        foreach (var user in candidates)
        {
            var charge = user.BillingPeriodEnd ?? user.ProUntil!.Value - BillingEndpoints.RenewalSlack;
            // Round 20 stamped the ProUntil, which is later than its charge, so an old stamp still covers its period.
            if (user.RenewalRecapUntil is { } stamped && stamped >= charge)
            {
                continue;
            }

            var numbers = await NumbersAsync(db, user.Id, now, ct);
            var language = Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : Localizer.DefaultLocale;
            var chargeDay = Localizer.Day(charge, language);
            // Settings is where "Manage subscription" mints the portal session; a portal url cannot be linked to
            // directly, because Stripe makes one per person on demand.
            var body = localizer.Get(language, "email.renewal_body",
                user.Handle,
                chargeDay,
                numbers.Compared.ToString(CultureInfo.InvariantCulture),
                numbers.Planned.ToString(CultureInfo.InvariantCulture),
                numbers.Worn.ToString(CultureInfo.InvariantCulture),
                numbers.OwnedTips.ToString(CultureInfo.InvariantCulture),
                origin + BillingEndpoints.PortalReturnPath);
            try
            {
                await email.SendAsync(new EmailMessage(user.Email!, localizer.Get(language, "email.renewal_subject", chargeDay), body), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "RenewalRecap: the mail to {Handle} did not go out; it is tried again next hour", user.Handle);
                skipped++;
                continue;
            }

            // Stamped per person, right after the send: a crash in the middle of a run never mails the same inbox twice.
            user.RenewalRecapUntil = charge;
            await db.SaveChangesAsync(ct);
            sent++;
        }

        logger.LogInformation("RenewalRecap: run at {Now:o}, {Sent} recaps sent, {Skipped} failed, {Pruned} old Stripe events pruned", now, sent, skipped, pruned);
        return new Run(sent, skipped, pruned);
    }

    /// <summary>
    /// The three numbers for one account over the window ending at <paramref name="now"/>, from the database alone.
    /// "Worn" is the Tomorrow screen's own definition (the person said it worked, or checked a look in it). A tip
    /// "named something they own" when the check's stored tip contains, case-insensitively, the name of a wardrobe
    /// piece the account had kept BEFORE that check: a piece kept from the same tip does not count as foresight.
    /// </summary>
    public static async Task<Numbers> NumbersAsync(AppDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var since = now - Window;
        var compared = await db.Comparisons.CountAsync(c => c.UserId == userId && c.Status == CheckStatus.Ok && c.CreatedAt >= since, ct);
        var suggestions = db.Suggestions.Where(s => s.UserId == userId && s.Status == CheckStatus.Ok && s.CreatedAt >= since);
        var planned = await suggestions.CountAsync(ct);
        var worn = await suggestions.CountAsync(s => s.UsefulReason == TipReason.Worked || s.WornCheckId != null, ct);

        var pieces = await db.WardrobeItems.Where(i => i.UserId == userId && i.NameKey != "")
            .Select(i => new { i.NameKey, i.CreatedAt })
            .ToListAsync(ct);
        var ownedTips = 0;
        if (pieces.Count > 0)
        {
            // The window's checks only, never the whole history: a month is bounded by the plan's own month cap.
            var checks = await db.Checks.Where(c => c.UserId == userId && c.Status == CheckStatus.Ok && c.CreatedAt >= since && c.FeedbackJson != null)
                .Select(c => new { c.CreatedAt, c.FeedbackJson })
                .ToListAsync(ct);
            foreach (var check in checks)
            {
                var tip = TipOf(check.FeedbackJson);
                if (tip.Length > 0 && pieces.Any(p => p.CreatedAt < check.CreatedAt && tip.Contains(p.NameKey, StringComparison.Ordinal)))
                {
                    ownedTips++;
                }
            }
        }

        return new Numbers(compared, planned, worn, ownedTips);
    }

    /// <summary>The stored tip, lower-cased for the match; empty for a row whose feedback cannot be read.</summary>
    private static string TipOf(string? feedbackJson)
    {
        try
        {
            return (JsonSerializer.Deserialize<OutfitFeedback>(feedbackJson!, AppJson.Options)?.OneTip ?? "").ToLowerInvariant();
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>Email:PublicOrigin, else Billing:PublicOrigin, else empty: a mailed link is never built from a request.</summary>
    public string Origin()
    {
        var configured = emailOptions.Value.PublicOrigin.Trim().TrimEnd('/');
        return configured.Length > 0 ? configured : billingOptions.Value.PublicOrigin.Trim().TrimEnd('/');
    }
}

/// <summary>
/// Wakes every hour, runs <see cref="RenewalRecap.RunAsync"/> and goes back to sleep; the first pass at start. Hourly
/// so a failed send is simply tried again; the idempotence is <see cref="AppUser.RenewalRecapUntil"/>, not the schedule.
/// </summary>
public sealed class RenewalRecapService(RenewalRecap worker, ILogger<RenewalRecapService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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
            logger.LogWarning(ex, "RenewalRecap: the run failed; it runs again on the next tick");
        }
    }
}
