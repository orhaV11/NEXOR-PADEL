using System.Globalization;
using FitCheck.Api.Domain;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 20 — the morning loop behind a switch: at Plans:TomorrowMorningHour in Board:TimeZone, one push per day to
/// accounts with a push subscription, a wardrobe of two kinds and something left to spend, saying today's outfit is a
/// tap away. It never composes: the tap does, inside the person's own allowance. The skeleton; the builder fills
/// RunAsync from the brief.
/// </summary>
public sealed class TomorrowMorning(ILogger<TomorrowMorning> logger)
{
    public sealed record Run(int Sent, int Skipped, string Reason = "");

    public static readonly TimeOnly DefaultHour = new(7, 30);

    /// <summary>The configured local hour, or 07:30 when the setting is not HH:mm (the doctor warns).</summary>
    public static TimeOnly HourOf(PlanOptions plans) =>
        TimeOnly.TryParseExact((plans.TomorrowMorningHour ?? "").Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hour) ? hour : DefaultHour;

    public Task<Run> RunAsync(CancellationToken ct)
    {
        logger.LogDebug("TomorrowMorning: not built yet");
        return Task.FromResult(new Run(0, 0, "not built"));
    }
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
