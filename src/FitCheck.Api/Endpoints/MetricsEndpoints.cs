using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>The kill switch. returnRate decides whether Phase 2 gets built.</summary>
public static class MetricsEndpoints
{
    private static readonly TimeSpan ReturnWindow = TimeSpan.FromDays(7);

    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/metrics/pilot", GetPilotAsync);
        return app;
    }

    private static async Task<IResult> GetPilotAsync(AppDbContext db, CancellationToken ct)
    {
        // Pilot scale (50 users, 20 checks/day cap): pulling the OK rows into memory is simpler than
        // hand-rolling the second-check window in SQL, and it keeps the math readable and testable.
        var checks = await db.Checks
            .Where(c => c.Status == CheckStatus.Ok)
            .Select(c => new { c.UserId, c.CreatedAt, c.Score, c.LatencyMs, c.Language, c.PromptVersion })
            .ToListAsync(ct);

        return Results.Ok(Compute(checks.Select(c => new MetricRow(c.UserId, c.CreatedAt, c.Score ?? 0, c.LatencyMs, c.Language, c.PromptVersion))));
    }

    public sealed record MetricRow(Guid UserId, DateTime CreatedAt, int Score, int LatencyMs, string Language, string PromptVersion);

    public static PilotMetricsDto Compute(IEnumerable<MetricRow> rows)
    {
        var list = rows.ToList();
        var byUser = list.GroupBy(r => r.UserId).ToList();

        var returned = byUser.Count(g =>
        {
            var ordered = g.OrderBy(r => r.CreatedAt).Take(2).ToList();
            return ordered.Count == 2 && ordered[1].CreatedAt - ordered[0].CreatedAt <= ReturnWindow;
        });

        var scoreDistribution = Enumerable.Range(1, 10)
            .ToDictionary(score => score.ToString(), score => list.Count(r => r.Score == score));

        return new PilotMetricsDto(
            TotalChecks: list.Count,
            UsersWithAtLeastOneCheck: byUser.Count,
            UsersWithSecondCheckWithin7Days: returned,
            ReturnRate: byUser.Count == 0 ? 0.0 : Math.Round((double)returned / byUser.Count, 4),
            AvgLatencyMs: list.Count == 0 ? 0 : (int)Math.Round(list.Average(r => r.LatencyMs)),
            ScoreDistribution: scoreDistribution,
            ByLanguage: list.GroupBy(r => r.Language).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            ByPromptVersion: list.GroupBy(r => r.PromptVersion).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()));
    }
}
