using FitCheck.Api.Data;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 20 — the guest who met the day ceiling and asked, at signup, for one note when the stylist is back. The ask is
/// a Counter row per account; when the ceiling has cleared, one activity row (and its push), one mail where an address
/// is verified, and the row goes. The skeleton; the builder fills RunAsync from the brief.
/// </summary>
public sealed class StylistBack(ILogger<StylistBack> logger)
{
    /// <summary>The Counter row that records the ask.</summary>
    public static string AskedName(Guid userId) => $"stylist_back:{userId:N}";

    /// <summary>Runs one pass and returns how many people were told.</summary>
    public Task<int> RunAsync(CancellationToken ct)
    {
        logger.LogDebug("StylistBack: not built yet");
        return Task.FromResult(0);
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
