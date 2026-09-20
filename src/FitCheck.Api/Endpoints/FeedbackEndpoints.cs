using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// The verdict's own verdict (Round 13). After the one tip, the result screen asks "Did the tip land?" with yes and no;
/// this is where the answer goes. The contract:
/// <list type="bullet">
/// <item><c>POST /api/checks/{id}/useful</c> with <c>{ useful: bool, note?: string }</c>: the owner, or the guest whose
/// cookie made the check (the same rule as <c>GET /api/checks/{id}</c>; anyone else, and a call with neither a session
/// nor a guest cookie, gets its 404 <c>error.check_not_found</c>, so ids do not leak existence). Only a check the stylist
/// scored can be rated (400 <c>error.useful_not_scored</c> otherwise: there was no tip). <c>useful</c> is required (400
/// <c>error.useful_invalid</c>); <c>note</c> is optional, trimmed, control characters and line breaks removed, at most
/// <see cref="NoteMaxLength"/> characters (400 <c>error.useful_note_too_long</c>), stored as null when empty. The answer
/// may be changed: every call overwrites the three columns (<see cref="OutfitCheck.Useful"/>, <see cref="OutfitCheck.UsefulAt"/>
/// from <see cref="IClock"/>, <see cref="OutfitCheck.UsefulNote"/>) and answers 200 with what is stored
/// (<see cref="UsefulDto"/>). Rate limited by the <see cref="Policy"/> policy, <see cref="PerHour"/> an hour per account or
/// address (429 <c>error.too_fast</c>), like the other tallies. Logged as <c>Useful: check {CheckId} {Verdict}</c> with
/// no note in the log line (it is the person's words).</item>
/// <item>The numbers page: <see cref="StylistMetricsAsync"/> computes the yes / no / unanswered split over every ok
/// check by an account, overall, by intent and by language, and <c>MetricsEndpoints</c> puts it on
/// <c>PilotMetricsDto.stylist</c>.</item>
/// </list>
/// </summary>
public static class FeedbackEndpoints
{
    public const string Policy = "useful";
    public const int PerHour = 60;
    public const int NoteMaxLength = 120;

    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/checks/{id:guid}/useful", UsefulAsync).RequireRateLimiting(Policy);
        return app;
    }

    private static async Task<IResult> UsefulAsync(
        Guid id, UsefulRequest? body, HttpContext context, AppDbContext db, Localizer localizer, IClock clock, ILogger<OutfitAnalyzer> logger, CancellationToken ct)
    {
        var language = Localizer.Resolve(null, context.Request);
        var userId = Sessions.UserId(context.User);
        var guestToken = GuestChecks.Read(context);
        var check = await db.Checks.FirstOrDefaultAsync(c => c.Id == id, ct);
        var mine = check is not null
            && ((userId is not null && check.UserId == userId)
                || (check.UserId is null && guestToken is not null && check.GuestToken == guestToken));
        if (!mine)
        {
            return UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(language, "error.check_not_found"));
        }

        // The check's own language is the one the person read the tip in; the messages follow it when it is known.
        language = Localizer.IsSupported(check!.Language) ? check.Language : language;
        if (check.Status != CheckStatus.Ok)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.useful_not_scored"));
        }

        if (body?.Useful is not { } useful)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.useful_invalid"));
        }

        var note = OutfitAnalyzer.SanitizeOccasion(body.Note);
        if (note.Length > NoteMaxLength)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.useful_note_too_long"));
        }

        var now = clock.UtcNow;
        check.Useful = useful;
        check.UsefulAt = now;
        check.UsefulNote = note.Length == 0 ? null : note;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Useful: check {CheckId} {Verdict}", check.Id, useful ? "yes" : "no");

        return Results.Json(new UsefulDto(check.Id, useful, DateTime.SpecifyKind(now, DateTimeKind.Utc), check.UsefulNote), AppJson.Options);
    }

    /// <summary>
    /// The stylist's numbers: over every ok check by an account, how many said the tip landed, how many said it missed,
    /// how many never said, overall, by intent (the StyleIntent name) and by language; plus the no-outfit and rejected
    /// counts over the same accounts' checks, so the numbers page can see the door as well as the verdict.
    /// </summary>
    public static async Task<StylistMetricsDto> StylistMetricsAsync(AppDbContext db, CancellationToken ct)
    {
        var rows = await db.Checks.AsNoTracking()
            .Where(c => c.UserId != null && c.Status == CheckStatus.Ok)
            .Select(c => new { c.Intent, c.Language, c.Useful })
            .ToListAsync(ct);

        return new StylistMetricsDto(
            Split(rows.Select(r => r.Useful)),
            rows.GroupBy(r => r.Intent.ToString()).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => Split(g.Select(r => r.Useful))),
            rows.GroupBy(r => r.Language).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => Split(g.Select(r => r.Useful))),
            await db.Checks.CountAsync(c => c.UserId != null && c.Status == CheckStatus.NotOutfit, ct),
            await db.Checks.CountAsync(c => c.UserId != null && c.Status == CheckStatus.Rejected, ct));
    }

    /// <summary>Yes, no, unanswered, and yes over the answered ones (four decimals); null while nobody has answered.</summary>
    public static UsefulSplitDto Split(IEnumerable<bool?> verdicts)
    {
        int yes = 0, no = 0, unanswered = 0;
        foreach (var verdict in verdicts)
        {
            switch (verdict)
            {
                case true: yes++; break;
                case false: no++; break;
                default: unanswered++; break;
            }
        }

        var answered = yes + no;
        return new UsefulSplitDto(yes, no, unanswered, answered == 0 ? null : Math.Round((double)yes / answered, 4));
    }
}
