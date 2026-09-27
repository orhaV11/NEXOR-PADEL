using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 20 — "Did you try the tip?": a day after a scored verdict whose change-tip was never answered, one nudge (an
/// activity row and a push) to a push-subscribed account, inside the local day, once per check and once per person a
/// day. Everything that decides who is nudged is a row the person already wrote: an ok check with a change tip, no
/// answer under it (worked and didn't-work mean they tried; not-my-style and don't-own mean they will not), no pair on
/// either side, a push subscription (an activity row nobody would be told about is a row nobody asked for), and no
/// try_tip row for that check yet. The check stays inside its window (<see cref="PushOptions.TryTipAfterHours"/> to
/// that plus <see cref="PushOptions.TryTipWindowHours"/>) across the quiet hours, so a verdict from last night is nudged
/// on the morning's run and a verdict from last week is never nudged at all.
/// </summary>
public sealed class TryTipNudge(IServiceScopeFactory scopes, IClock clock, Board board, IOptions<PushOptions> options, ILogger<TryTipNudge> logger)
{
    /// <summary>Runs one pass and returns how many nudges were written.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.TryTipNudge)
        {
            logger.LogDebug("TryTip: Push:TryTipNudge is false, nothing nudged");
            return 0;
        }

        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        // The quiet hours are the server's one city (Board:TimeZone, the same local Digest reads), not the person's: a
        // pilot user abroad may hear this at an odd hour, which is noted and accepted for a one-city pilot.
        var localHour = TimeZoneInfo.ConvertTimeFromUtc(now, board.Zone).Hour;
        if (localHour < settings.TryTipDayStart || localHour >= settings.TryTipDayEnd)
        {
            return 0;
        }

        var newest = now.AddHours(-settings.TryTipAfterHours);
        var oldest = newest.AddHours(-settings.TryTipWindowHours);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidates = await db.Checks.AsNoTracking()
            .Where(c => c.UserId != null && c.Status == CheckStatus.Ok
                && c.CreatedAt <= newest && c.CreatedAt > oldest
                && c.Useful == null && c.UsefulReason == null
                && db.Users.Any(u => u.Id == c.UserId && !u.Suspended)
                && db.PushSubscriptions.Any(s => s.UserId == c.UserId)
                && !db.CheckLinks.Any(l => l.BeforeCheckId == c.Id || l.AfterCheckId == c.Id)
                && !db.Notifications.Any(n => n.Type == NotificationType.TryTip && n.CheckId == c.Id))
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, UserId = c.UserId!.Value, c.FeedbackJson })
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // One a day per person: an account nudged in the last day, about any check, waits.
        var since = now.AddHours(-24);
        var nudgedToday = (await db.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.TryTip && n.CreatedAt > since)
            .Select(n => n.UserId)
            .ToListAsync(ct)).ToHashSet();

        var notifier = scope.ServiceProvider.GetRequiredService<Notifier>();
        var written = 0;
        foreach (var candidate in candidates)
        {
            if (nudgedToday.Contains(candidate.UserId) || !HasChangeTip(candidate.FeedbackJson))
            {
                continue;
            }

            var handle = await db.Users.Where(u => u.Id == candidate.UserId).Select(u => u.Handle).FirstOrDefaultAsync(ct);
            if (handle is null)
            {
                continue;
            }

            try
            {
                if (await notifier.TryTipAsync(candidate.UserId, handle, candidate.Id, ct, now))
                {
                    await db.SaveChangesAsync(ct);
                    written++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // One person's failed row must not cost everyone else theirs; the check is still inside its window next hour.
                logger.LogWarning(ex, "TryTip: the nudge for check {CheckId} was not written; the run goes on", candidate.Id);
            }

            // Newest first, so the check that carries the nudge is the most recent one the person left unanswered.
            nudgedToday.Add(candidate.UserId);
        }

        if (written > 0)
        {
            logger.LogInformation("TryTip: {Count} nudged", written);
        }

        return written;
    }

    /// <summary>A change tip with words in it. A keep ("this works, change nothing") has nothing to try, and an unreadable verdict is left alone.</summary>
    public static bool HasChangeTip(string? feedbackJson)
    {
        if (string.IsNullOrEmpty(feedbackJson))
        {
            return false;
        }

        try
        {
            var feedback = JsonSerializer.Deserialize<OutfitFeedback>(feedbackJson, AppJson.Options);
            return feedback is not null && feedback.TipKind == TipKinds.Change && !string.IsNullOrWhiteSpace(feedback.OneTip);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Wakes every hour; the first pass at start. A failed pass is logged and tried again on the next tick.</summary>
public sealed class TryTipNudgeService(TryTipNudge worker, ILogger<TryTipNudgeService> logger) : BackgroundService
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
            logger.LogWarning(ex, "TryTipNudge: the run failed; it runs again on the next tick");
        }
    }
}
