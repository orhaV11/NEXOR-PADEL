namespace FitCheck.Api.Services;

/// <summary>
/// Round 20 — "Did you try the tip?": a day after a scored verdict whose tip was never answered, one nudge (an activity
/// row and a push) to a push-subscribed account, inside the local day, once per check and once per person a day. The
/// skeleton; the builder fills RunAsync from the brief.
/// </summary>
public sealed class TryTipNudge(ILogger<TryTipNudge> logger)
{
    /// <summary>Runs one pass and returns how many nudges were written.</summary>
    public Task<int> RunAsync(CancellationToken ct)
    {
        logger.LogDebug("TryTip: not built yet");
        return Task.FromResult(0);
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
