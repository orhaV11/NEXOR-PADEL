using System.Globalization;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 16 — the month, written back to the person. The numbers are computed here, in code, from their own checks;
/// the model is handed those numbers and asked to write them as sentences in their language. It is never asked what
/// the numbers are, because a recap that invented a figure about somebody's own year is worse than no recap at all.
/// </summary>
public sealed class Recaps(AppDbContext db, IOutfitVisionClient vision, IClock clock)
{
    /// <summary>Below this a month says nothing about a person, and no call is made.</summary>
    public const int MinChecks = 4;

    /// <summary>The window it reads. Thirty days rather than the calendar month, so a recap asked for on the 2nd is not empty.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    /// <summary>What the model may write. Four sentences is a paragraph; more is a report nobody reads.</summary>
    public const int MaxTokens = 400;

    private const string SystemPrompt = """
        You write one short paragraph for somebody about their own month of outfit checks in a style app.

        You are given FIGURES that were computed from their checks. Every number you write must be one of those
        figures, exactly as given. Never estimate, never round differently, never add a figure that is not there, and
        never invent an event, a garment or an occasion that is not in the input. If a figure is absent, write around
        it rather than guessing it.

        Write 2 to 4 sentences, in the language named in the input, addressed to the person as "you". Warm and plain,
        the way a friend who happens to be a stylist would say it. Name one thing that went well and one thing to
        carry into next month. No greeting, no sign-off, no lists, no emoji, no headings.
        """;

    private static readonly VisionTool Tool = new(
        "write_recap",
        "Write the person's month back to them as one short paragraph.",
        JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "paragraph": { "type": "string", "description": "2 to 4 sentences, in the language asked for." }
              },
              "required": ["paragraph"],
              "additionalProperties": false
            }
            """).RootElement);

    /// <summary>The month a recap is keyed on: the first day of the current UTC month, at midnight.</summary>
    public static DateTime MonthOf(DateTime now) => new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// This month's recap for this account in this language: the stored one when there is one, a new one when there
    /// are enough checks to say anything, and null when there are not. Null is a first-class answer — the page then
    /// says how many more checks it needs rather than showing an invented paragraph.
    /// </summary>
    public async Task<Recap?> ForAsync(AppUser user, string language, Localizer localizer, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var month = MonthOf(now);
        var stored = await db.Recaps.FirstOrDefaultAsync(r => r.UserId == user.Id && r.Month == month && r.Language == language, ct);
        if (stored is not null)
        {
            return stored;
        }

        var rows = await db.Checks.AsNoTracking()
            .Where(c => c.UserId == user.Id && c.Status == CheckStatus.Ok && c.CreatedAt >= now - Window)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Intent, c.Score, c.FeedbackJson })
            .ToListAsync(ct);
        if (rows.Count < MinChecks)
        {
            return null;
        }

        var checks = rows
            .Select(r => new InsightsEndpoints.CheckRow(r.Intent, r.Score,
                r.FeedbackJson is null ? null : JsonSerializer.Deserialize<OutfitFeedback>(r.FeedbackJson, AppJson.Options)))
            .ToList();
        var insights = InsightsEndpoints.Compute(checks, user.StreakCount, language, localizer);

        var answer = await vision.AnalyzeAsync(
            new VisionRequest(SystemPrompt, Figures(insights, language), default, "", Tool), ct);
        var paragraph = answer.TryGetProperty("paragraph", out var text) ? (text.GetString() ?? "").Trim() : "";
        if (paragraph.Length == 0)
        {
            throw new VisionClientException("The recap came back with no paragraph.");
        }

        var recap = new Recap
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Month = month,
            Language = language,
            Text = paragraph.Length > 2000 ? paragraph[..2000] : paragraph,
            Checks = checks.Count,
            CreatedAt = now
        };
        db.Recaps.Add(recap);
        await db.SaveChangesAsync(ct);
        return recap;
    }

    /// <summary>
    /// Everything the model is told, and nothing else. No photograph, no caption, no handle, no e-mail: a paragraph
    /// about somebody's month needs their numbers, and their numbers are all it gets.
    /// </summary>
    public static string Figures(InsightsDto insights, string language)
    {
        var text = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        text.Append("Language to write in: ").Append(language).Append('\n');
        text.Append("Checks in the last 30 days: ").Append(insights.Checks.ToString(culture)).Append('\n');
        if (insights.AvgScore is { } average)
        {
            text.Append("Average score out of 10: ").Append(average.ToString("0.0", culture)).Append('\n');
        }

        if (insights.BestScore is { } best)
        {
            text.Append("Best score: ").Append(best.ToString(culture)).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(insights.BestIntent))
        {
            text.Append("The occasion they score highest on: ").Append(insights.BestIntent).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(insights.WeakestCategory) && insights.WeakestShare is { } share)
        {
            text.Append("The part most often called weakest: ").Append(insights.WeakestCategory)
                .Append(", in ").Append((share * 100).ToString("0", culture)).Append("% of checks\n");
        }

        if (insights.AccessoriesMissingShare is { } missing)
        {
            text.Append("Checks where accessories were missing: ").Append((missing * 100).ToString("0", culture)).Append("%\n");
        }

        if (insights.Streak > 1)
        {
            text.Append("Days in a row with a check: ").Append(insights.Streak.ToString(culture)).Append('\n');
        }

        return text.ToString();
    }
}
