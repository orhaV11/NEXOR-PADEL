namespace FitCheck.Api.Services;

/// <summary>
/// Round 20 — the pre-renewal recap: three days before a paying Pro's charge, one mail with three plain numbers the
/// model did not write and the way to manage the subscription; once per period. The same hourly pass prunes handled
/// Stripe events older than a month. The skeleton; the builder fills RunAsync from the brief.
/// </summary>
public sealed class RenewalRecap(ILogger<RenewalRecap> logger)
{
    public sealed record Run(int Sent, int Skipped, int Pruned);

    public static readonly TimeSpan Lead = TimeSpan.FromDays(3);
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    public Task<Run> RunAsync(CancellationToken ct)
    {
        logger.LogDebug("RenewalRecap: not built yet");
        return Task.FromResult(new Run(0, 0, 0));
    }
}

/// <summary>Wakes every hour; the first pass at start. A failed pass is logged and tried again on the next tick.</summary>
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
