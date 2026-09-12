using System.Globalization;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// What your checks say about you: numbers for tiles and two to four ready sentences in your language, computed over
/// your last ok checks. Private like the checks themselves, and behind Pro exactly when "which one?" comparisons are
/// (Plans:CompareNeedsPro). The item search that goes with it lives in ExploreEndpoints.
/// </summary>
public static class InsightsEndpoints
{
    /// <summary>How many of the newest ok checks are read. Enough for a habit, bounded for the pilot box.</summary>
    public const int Window = 200;

    /// <summary>Below this the page is an invitation, not a reading: one check says nothing about a person.</summary>
    public const int MinChecks = 3;

    /// <summary>An intent needs this many checks before its average may call it your best; otherwise the most checked one is.</summary>
    public const int MinIntentChecks = 2;

    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users/me/insights", GetAsync).RequireAuthorization();
        return app;
    }

    /// <summary>One ok check as the math sees it: the intent, the stored score and the parsed feedback (null when unreadable).</summary>
    public sealed record CheckRow(StyleIntent Intent, int? Score, OutfitFeedback? Feedback);

    private static async Task<IResult> GetAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IOptions<PlanOptions> plans, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        // The one wall Pro may have, shared with comparisons: on only when the owner sells both (the Pro page then lists them).
        if (plans.Value.CompareNeedsPro && !Plans.IsPro(me, DateTime.UtcNow))
        {
            return UserEndpoints.Error(StatusCodes.Status403Forbidden, localizer.Get(PostEndpoints.Language(context, me), "error.pro_required"));
        }

        var rows = await db.Checks
            .Where(c => c.UserId == me.Id && c.Status == CheckStatus.Ok)
            .OrderByDescending(c => c.CreatedAt)
            .Take(Window)
            .Select(c => new { c.Intent, c.Score, c.FeedbackJson })
            .ToListAsync(ct);

        var checks = rows.Select(r => new CheckRow(r.Intent, r.Score, Parse(r.FeedbackJson))).ToList();
        var dto = Compute(checks, me.StreakCount, PostEndpoints.Language(context, me), localizer);
        return Results.Json(dto, AppJson.Options);
    }

    /// <summary>
    /// The insights over ok checks, newest first. A pure function of its inputs so the math can be tested on a seeded
    /// list without a database.
    /// </summary>
    public static InsightsDto Compute(IReadOnlyList<CheckRow> checks, int streak, string language, Localizer localizer)
    {
        if (checks.Count < MinChecks)
        {
            return new InsightsDto(checks.Count, null, null, null, null, null, null, streak, []);
        }

        var avgScore = Round1(checks.Average(ScoreOf));
        var bestScore = checks.Max(ScoreOf);

        // Best intent: the highest average among intents checked at least twice; with none of those, the most checked
        // one. Ties go to the more checked, then the higher average, then the enum order, so the answer is stable.
        var byIntent = checks
            .GroupBy(c => c.Intent)
            .Select(g => new { Intent = g.Key, Count = g.Count(), Avg = g.Average(ScoreOf) })
            .ToList();
        var eligible = byIntent.Where(x => x.Count >= MinIntentChecks).ToList();
        var best = (eligible.Count > 0
                ? eligible.OrderByDescending(x => x.Avg).ThenByDescending(x => x.Count)
                : byIntent.OrderByDescending(x => x.Count).ThenByDescending(x => x.Avg))
            .ThenBy(x => x.Intent)
            .First();

        // Weakest category: the item category most often given a "weak" verdict, counted once per check, over the
        // checks that carry items at all; the share is that count over those checks, so it reads as "in N% of your looks".
        var withItems = checks.Where(c => c.Feedback is { Items.Count: > 0 }).ToList();
        var weakCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var check in withItems)
        {
            var weakCategories = check.Feedback!.Items
                .Where(i => string.Equals(i.Verdict, "weak", StringComparison.OrdinalIgnoreCase))
                .Select(i => PostItems.NormalizeCategory(i.Category))
                .Distinct();
            foreach (var category in weakCategories)
            {
                weakCounts[category] = weakCounts.GetValueOrDefault(category) + 1;
            }
        }

        string? weakestCategory = null;
        double? weakestShare = null;
        if (weakCounts.Count > 0)
        {
            var top = weakCounts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => Array.IndexOf(PostItems.Categories, kv.Key))
                .First();
            weakestCategory = top.Key;
            weakestShare = Share(top.Value, withItems.Count);
        }

        // Accessories: over the checks that carry the rubric v2 read, how often nothing was on.
        var v2 = checks.Where(c => c.Feedback?.Accessories is not null).ToList();
        double? accessoriesMissingShare = v2.Count == 0
            ? null
            : Share(v2.Count(c => string.Equals(c.Feedback!.Accessories!.Verdict, "missing", StringComparison.OrdinalIgnoreCase)), v2.Count);

        var lines = new List<string>
        {
            localizer.Get(language, "insights.line_best", localizer.Get(language, "insights.intent_" + best.Intent), Fmt1(best.Avg))
        };
        if (weakestCategory is not null)
        {
            lines.Add(localizer.Get(language, "insights.line_weak", localizer.Get(language, "insights.cat_" + weakestCategory), Percent(weakestShare!.Value)));
        }

        if (accessoriesMissingShare > 0)
        {
            lines.Add(localizer.Get(language, "insights.line_accessories", Percent(accessoriesMissingShare.Value)));
        }

        if (streak >= 2)
        {
            lines.Add(localizer.Get(language, "insights.line_streak", streak));
        }

        return new InsightsDto(checks.Count, avgScore, bestScore, best.Intent.ToString(), weakestCategory, weakestShare, accessoriesMissingShare, streak, lines);
    }

    /// <summary>An ok check always stores its score; the feedback's own is the fallback for a row written another way.</summary>
    private static int ScoreOf(CheckRow check) => check.Score ?? check.Feedback?.Score ?? 0;

    private static double Round1(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static double Share(int part, int whole) => Math.Round((double)part / whole, 3, MidpointRounding.AwayFromZero);

    private static string Fmt1(double value) => Round1(value).ToString("0.0", CultureInfo.InvariantCulture);

    private static int Percent(double share) => (int)Math.Round(share * 100, MidpointRounding.AwayFromZero);

    private static OutfitFeedback? Parse(string? feedbackJson)
    {
        if (string.IsNullOrEmpty(feedbackJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OutfitFeedback>(feedbackJson, AppJson.Options);
        }
        catch (JsonException)
        {
            // A row from a build that stored something else: it still counts as a check, just without items.
            return null;
        }
    }
}
