using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// The kill switch. returnRate decides whether the next phase gets built; the social block says whether the loop turns.
/// Aggregates only, but they are the pilot's numbers: a moderator's session (the IsAdmin flag) is required to read them.
/// </summary>
public static class MetricsEndpoints
{
    private static readonly TimeSpan ReturnWindow = TimeSpan.FromDays(7);

    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/metrics/pilot", GetPilotAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GetPilotAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        // The same gate as /api/admin: a signed-in account (401 gone, 403 suspended) that carries the moderator flag.
        var (viewer, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (viewer is null)
        {
            return failure!;
        }

        if (!viewer.IsAdmin)
        {
            return UserEndpoints.Error(StatusCodes.Status403Forbidden, localizer.Get(viewer.PreferredLanguage, "error.admin_only"));
        }

        // Pilot scale (50 users, 20 checks/day cap): pulling the OK rows into memory is simpler than
        // hand-rolling the second-check window in SQL, and it keeps the math readable and testable. The stored
        // feedback comes along for the rubric v2 sub-scores, which live nowhere else.
        var checks = await db.Checks
            .Where(c => c.Status == CheckStatus.Ok)
            .Where(c => c.UserId != null)
            .Select(c => new { UserId = c.UserId!.Value, c.CreatedAt, c.Score, c.LatencyMs, c.Language, c.PromptVersion, c.FeedbackJson })
            .ToListAsync(ct);

        var metrics = Compute(checks.Select(c => new MetricRow(c.UserId, c.CreatedAt, c.Score ?? 0, c.LatencyMs, c.Language, c.PromptVersion, BreakdownOf(c.FeedbackJson))));

        var now = DateTime.UtcNow;
        var since = now - ReturnWindow;
        var active = new HashSet<Guid>();
        active.UnionWith(await db.Checks.Where(c => c.CreatedAt >= since && c.UserId != null).Select(c => c.UserId!.Value).Distinct().ToListAsync(ct));
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
            Featured: await db.Posts.CountAsync(p => !p.Hidden && p.FeaturedByBrandId != null, ct),
            // Looks posted with a clip. A clip on a check that was never posted, or whose post is hidden, is not a video in the feed.
            Videos: await db.Posts.CountAsync(p => !p.Hidden && db.Checks.Any(c => c.Id == p.CheckId && c.VideoPath != null && c.VideoPath != ""), ct),
            PushSubscriptions: await db.PushSubscriptions.CountAsync(ct),
            // Round 10: items a person touched (typed, branded or linked), store links followed, board reads. The two tallies
            // are Counter rows the item and board routes increment; nothing increments them until those routes are built.
            ItemsTagged: await db.PostItems.CountAsync(i => i.Source == ItemSource.User || i.Brand != null || i.Url != null, ct),
            ItemOuts: (int)Math.Min(int.MaxValue, await Counters.ReadAsync(db, CounterName.ItemOuts, ct)),
            BoardViews: (int)Math.Min(int.MaxValue, await Counters.ReadAsync(db, CounterName.BoardViews, ct)));

        return Results.Ok(metrics with { Social = social });
    }

    /// <summary>Breakdown is the rubric v2 sub-scores when the check has them; null for a v1 check.</summary>
    public sealed record MetricRow(Guid UserId, DateTime CreatedAt, int Score, int LatencyMs, string Language, string PromptVersion, ScoreBreakdown? Breakdown = null);

    /// <summary>The sub-scores out of a stored feedback document; null when there are none or the document is unreadable.</summary>
    public static ScoreBreakdown? BreakdownOf(string? feedbackJson)
    {
        if (string.IsNullOrEmpty(feedbackJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OutfitFeedback>(feedbackJson, AppJson.Options)?.Breakdown;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static PilotMetricsDto Compute(IEnumerable<MetricRow> rows)
    {
        var list = rows.ToList();
        var byUser = list.GroupBy(r => r.UserId).ToList();
        var withBreakdown = list.Where(r => r.Breakdown is not null).Select(r => r.Breakdown!).ToList();
        var breakdownAverages = withBreakdown.Count == 0
            ? null
            : new BreakdownAveragesDto(
                Math.Round(withBreakdown.Average(b => b.Fit), 2),
                Math.Round(withBreakdown.Average(b => b.Color), 2),
                Math.Round(withBreakdown.Average(b => b.Accessories), 2),
                withBreakdown.Count);

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
            ByPromptVersion: list.GroupBy(r => r.PromptVersion).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            BreakdownAverages: breakdownAverages);
    }
}
