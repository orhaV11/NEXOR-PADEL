using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>The kill switch. returnRate decides whether the next phase gets built; the social block says whether the loop turns.</summary>
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

        var metrics = Compute(checks.Select(c => new MetricRow(c.UserId, c.CreatedAt, c.Score ?? 0, c.LatencyMs, c.Language, c.PromptVersion)));

        var now = DateTime.UtcNow;
        var since = now - ReturnWindow;
        var active = new HashSet<Guid>();
        active.UnionWith(await db.Checks.Where(c => c.CreatedAt >= since).Select(c => c.UserId).Distinct().ToListAsync(ct));
        active.UnionWith(await db.Fires.Where(f => f.CreatedAt >= since).Select(f => f.UserId).Distinct().ToListAsync(ct));
        active.UnionWith(await db.Comments.Where(c => c.CreatedAt >= since).Select(c => c.UserId).Distinct().ToListAsync(ct));
        active.UnionWith(await db.ChallengeVotes.Where(v => v.CreatedAt >= since).Select(v => v.UserId).Distinct().ToListAsync(ct));

        var social = new SocialMetricsDto(
            Users: await db.Users.CountAsync(ct),
            Brands: await db.Users.CountAsync(u => u.AccountType == AccountType.Brand, ct),
            Posts: await db.Posts.CountAsync(p => !p.Hidden, ct),
            Fires: await db.Fires.CountAsync(ct),
            Follows: await db.Follows.CountAsync(ct),
            Comments: await db.Comments.CountAsync(c => !c.Hidden, ct),
            ChallengesOpen: await db.Challenges.CountAsync(c => c.EndsAt > now, ct),
            ChallengesEnded: await db.Challenges.CountAsync(c => c.EndsAt <= now, ct),
            Votes: await db.ChallengeVotes.CountAsync(ct),
            ActiveUsers7d: active.Count,
            // Mentions live on their posts, so a hidden look takes its mentions out of the count with it.
            Mentions: await db.PostMentions.CountAsync(m => db.Posts.Any(p => p.Id == m.PostId && !p.Hidden), ct),
            Featured: await db.Posts.CountAsync(p => !p.Hidden && p.FeaturedByBrandId != null, ct));

        return Results.Ok(metrics with { Social = social });
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
