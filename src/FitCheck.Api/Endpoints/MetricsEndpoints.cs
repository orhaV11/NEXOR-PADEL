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

    private static async Task<IResult> GetPilotAsync(HttpContext context, AppDbContext db, Localizer localizer, SpendMeter spend, Alerter alerter, CancellationToken ct)
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
            // Round 10: items a person touched (typed, branded or linked; the stylist's bare names are not tagging), store links
            // followed through /api/items/{id}/out, board reads. The two tallies are Counter rows the routes increment.
            ItemsTagged: await db.PostItems.CountAsync(i => i.Source == ItemSource.User || i.Brand != null || i.Url != null, ct),
            ItemOuts: (int)Math.Min(int.MaxValue, await Counters.ReadAsync(db, CounterName.ItemOuts, ct)),
            BoardViews: (int)Math.Min(int.MaxValue, await Counters.ReadAsync(db, CounterName.BoardViews, ct)),
            // Share videos are rendered and encoded on the phone; the server only hears that one was shared or saved.
            VideosMade: (int)Math.Min(int.MaxValue, await Counters.ReadAsync(db, CounterName.VideosMade, ct)),
            // ---- Round 14 — the community round: whether any of it changed anything ----
            // Looks posted with the grade kept private; comments begun from an opener (one tally for all three);
            // before/after shares with the two verdicts and with no numbers at all; challenges that state a rule.
            PrivateScores: await db.Posts.CountAsync(p => !p.Hidden && p.ScorePrivate, ct),
            CommentOpeners: (int)Math.Min(int.MaxValue, await Counters.ReadAsync(db, CounterName.CommentOpeners, ct)),
            BeforeAfterShares: (int)Math.Min(int.MaxValue, await Counters.ReadAsync(db, CounterName.BeforeAfterShares, ct)),
            BeforeAfterSharesPlain: (int)Math.Min(int.MaxValue, await Counters.ReadAsync(db, CounterName.BeforeAfterSharesPlain, ct)),
            ConstraintChallenges: await db.Challenges.CountAsync(c => c.Constraint != null, ct));

        // Round 13: the verdict's own verdict (FeedbackEndpoints): did the tip land, overall, by intent and by language.
        var stylist = await FeedbackEndpoints.StylistMetricsAsync(db, ct);

        // ---- Round 13 — money: the spend block. Its own numbers, read from the meter's Counter rows; it touches no
        // tile above. Every dollar is an ESTIMATE at the owner's configured prices, never an invoice. ----
        var spendToday = await spend.TodayAsync(db, ct);
        var money = new SpendMetricsDto(
            Today: spendToday,
            CeilingUsd: spend.CeilingUsd,
            Resting: spend.CeilingUsd > 0 && spendToday.EstimatedUsd >= spend.CeilingUsd,
            PriceInPerMillion: spend.Prices.In,
            PriceOutPerMillion: spend.Prices.Out,
            Series: await spend.SeriesAsync(db, ct),
            AlertWebhook: alerter.WebhookSet,
            AlertEmail: alerter.EmailSet,
            PromptCache: spend.PromptCache);

        // ---- Round 13 — the growth loop: the funnel and the invites (Services/Funnel.cs) ----
        // Fourteen days of landing views, guest checks, signups, first posts and public-page arrivals, today's
        // conversion between those steps, and the invites: sent, accepted, and who is inviting. Its own block, its own
        // DTO, nothing read or changed on any tile above it.
        var funnel = await Funnel.ComputeAsync(db, now, ct);

        // ---- Round 15 — the wardrobe, counted (Services/Wardrobe.cs) ----
        // Round 14 built a wardrobe and nothing counted it; MARKETING.md named the two numbers and said to count
        // WardrobeItems by hand until this existed. Its own block, its own DTO, nothing read or changed above it. The
        // keep rate's denominator is the same "accounts with at least one ok check" the hero tile already shows, so the
        // two numbers on the page cannot disagree about who has checked.
        var wardrobe = await WardrobeMetricsAsync(db, metrics.UsersWithAtLeastOneCheck, ct);

        // ---- Round 19 — Tomorrow, counted (Services/Tomorrow.cs) ----
        // Whether planned outfits get worn, how often a stored one is handed back instead of a new call, and how often
        // the model reached outside the wardrobe or had its sentence replaced. Thirty days, its own block.
        var tomorrow = await TomorrowMetricsAsync(db, now, ct);

        return Results.Ok(metrics with { Social = social, Stylist = stylist, Spend = money, Funnel = funnel, Wardrobe = wardrobe, Tomorrow = tomorrow });
    }

    /// <summary>
    /// MARKETING.md's two wardrobe numbers, with the counts they are made of. Five counts, no rows pulled into memory:
    /// pieces kept, accounts keeping, accounts that turned the wardrobe off for the stylist, checks answered with a
    /// typed reason and checks answered <c>dont_own</c>. <paramref name="checkedUsers"/> is the keep rate's denominator
    /// — accounts with at least one ok check, which the caller has already counted. Guests are left out of both sides:
    /// a guest has no wardrobe and no account to be a keeper of, and every other number on this page leaves them out
    /// until they are claimed.
    /// </summary>
    public static async Task<WardrobeMetricsDto> WardrobeMetricsAsync(AppDbContext db, int checkedUsers, CancellationToken ct)
    {
        var items = await db.WardrobeItems.CountAsync(ct);
        var keepers = await db.WardrobeItems.Select(i => i.UserId).Distinct().CountAsync(ct);
        var toStylistOff = await db.WardrobeSettings.CountAsync(s => !s.ToStylist, ct);
        // Round 19: a planned outfit answered with a typed reason counts here too — "I do not own that" on an outfit
        // composed from the wardrobe is exactly the wardrobe being wrong.
        var reasons = await db.Checks.CountAsync(c => c.UserId != null && c.UsefulReason != null, ct)
            + await db.Suggestions.CountAsync(s => s.UsefulReason != null, ct);
        var dontOwn = await db.Checks.CountAsync(c => c.UserId != null && c.UsefulReason == TipReason.DontOwn, ct)
            + await db.Suggestions.CountAsync(s => s.UsefulReason == TipReason.DontOwn, ct);

        return new WardrobeMetricsDto(
            Items: items,
            Keepers: keepers,
            CheckedUsers: checkedUsers,
            KeepRate: Rate(keepers, checkedUsers),
            DontOwn: dontOwn,
            Reasons: reasons,
            DontOwnRate: Rate(dontOwn, reasons),
            ToStylistOff: toStylistOff);
    }

    /// <summary>A share to four decimals, or null when there is nothing to divide by: no denominator is not zero percent.</summary>
    public static double? Rate(int part, int whole) => whole == 0 ? null : Math.Round((double)part / whole, 4);

    /// <summary>
    /// Round 19 — Tomorrow over the last thirty days: outfits composed (ok rows), how many were worn (answered yes, or
    /// checked from), the reuse rate (stored answers handed back over answers made), the refs the model returned that
    /// were not in its list, the sentences the template replaced, and the typed reasons. Counts, no rows in memory.
    /// </summary>
    public static async Task<TomorrowMetricsDto> TomorrowMetricsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var since = now.AddDays(-30);
        var rows = db.Suggestions.Where(s => s.Status == CheckStatus.Ok && s.CreatedAt >= since);
        var suggestions = await rows.CountAsync(ct);
        var worn = await rows.CountAsync(s => s.UsefulReason == TipReason.Worked || s.WornCheckId != null, ct);
        var reused = await rows.SumAsync(s => s.Reuses, ct);
        var invented = await rows.SumAsync(s => s.InventedRefs, ct);
        var templated = await rows.CountAsync(s => s.SentenceTemplated, ct);
        var reasons = new List<TasteCountDto>();
        foreach (var reason in TipReason.All)
        {
            var n = await rows.CountAsync(s => s.UsefulReason == reason, ct);
            if (n > 0)
            {
                reasons.Add(new TasteCountDto(reason, n));
            }
        }

        return new TomorrowMetricsDto(suggestions, worn, Rate(worn, suggestions), reused, Rate(reused, suggestions + reused), invented, templated, reasons);
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

        // Round 20: the slow tail, by nearest rank, over exactly the checks the average is over, so the two tiles never
        // describe different populations. All-time like the average, on purpose; a windowed pair is a later move.
        var latencies = list.Select(r => r.LatencyMs).OrderBy(ms => ms).ToList();
        var p95 = latencies.Count == 0 ? 0 : latencies[(int)Math.Ceiling(0.95 * latencies.Count) - 1];

        return new PilotMetricsDto(
            TotalChecks: list.Count,
            UsersWithAtLeastOneCheck: byUser.Count,
            UsersWithSecondCheckWithin7Days: returned,
            ReturnRate: byUser.Count == 0 ? 0.0 : Math.Round((double)returned / byUser.Count, 4),
            AvgLatencyMs: list.Count == 0 ? 0 : (int)Math.Round(list.Average(r => r.LatencyMs)),
            ScoreDistribution: scoreDistribution,
            ByLanguage: list.GroupBy(r => r.Language).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            ByPromptVersion: list.GroupBy(r => r.PromptVersion).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            BreakdownAverages: breakdownAverages,
            P95LatencyMs: p95);
    }
}
