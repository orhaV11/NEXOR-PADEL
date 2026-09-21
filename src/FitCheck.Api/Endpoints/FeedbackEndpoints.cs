using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// The verdict's own verdict (Round 13) and the loop it feeds (Round 14). After the one tip, the result screen asks what
/// happened; this is where the answer goes, where a second photo of the same look is linked to the first, and where the
/// taste profile is read and switched off. The contract:
/// <list type="bullet">
/// <item><c>POST /api/checks/{id}/useful</c> with <c>{ useful?: bool, reason?: string, note?: string }</c>: the owner, or
/// the guest whose cookie made the check (the same rule as <c>GET /api/checks/{id}</c>; anyone else, and a call with
/// neither a session nor a guest cookie, gets its 404 <c>error.check_not_found</c>, so ids do not leak existence). Only a
/// check the stylist scored can be rated (400 <c>error.useful_not_scored</c> otherwise: there was no tip). Round 14:
/// <c>reason</c> is one of <see cref="TipReason"/> — <c>worked</c>, <c>didnt_work</c>, <c>not_my_style</c>,
/// <c>dont_own</c> — and decides <c>useful</c> on its own (only <c>worked</c> is a yes), so the Round 13 rate keeps
/// meaning "the tip landed"; an unknown reason is 400 <c>error.reason_invalid</c>. One of <c>reason</c> and
/// <c>useful</c> is required (400 <c>error.useful_invalid</c>); <c>note</c> is optional, trimmed, control characters and
/// line breaks removed, at most <see cref="NoteMaxLength"/> characters (400 <c>error.useful_note_too_long</c>), stored as
/// null when empty. The answer may be changed: every call overwrites the four columns
/// (<see cref="OutfitCheck.Useful"/>, <see cref="OutfitCheck.UsefulAt"/> from <see cref="IClock"/>,
/// <see cref="OutfitCheck.UsefulNote"/>, <see cref="OutfitCheck.UsefulReason"/>) and answers 200 with what is stored
/// (<see cref="UsefulDto"/>). Rate limited by the <see cref="Policy"/> policy, <see cref="PerHour"/> an hour per account
/// or address (429 <c>error.too_fast</c>), like the other tallies. Logged as <c>Useful: check {CheckId} {Verdict}</c>
/// with the reason and no note in the log line (the note is the person's words).</item>
/// <item><c>POST /api/checks/{id}/tried</c> with <c>{ beforeId }</c> (session): "I tried it". Links the check at
/// <c>{id}</c> as the AFTER of the check at <c>beforeId</c> and answers 201 <see cref="TriedPairDto"/> — both verdicts,
/// both tips, and what changed in the combination. Both checks must be the caller's own and scored, and the link is
/// written only now, after both verdicts exist: the second check went through <c>POST /api/checks</c> like any other, the
/// stylist was never told it was an attempt at its tip, and nothing here can move a score. Refusals:
/// <c>error.check_not_found</c> (404, either check), <c>error.tried_not_scored</c> (400),
/// <c>error.tried_same_check</c> (400), <c>error.tried_order</c> (400, the "after" was not made after the "before"),
/// <c>error.tried_already</c> (409, one of them is already half of a pair).</item>
/// <item><c>POST /api/checks/{id}/tried/prefer</c> with <c>{ prefer: "before" | "after" }</c> (session): stores which of
/// the two the person prefers and answers 200 with the pair. <c>error.prefer_invalid</c> (400) for anything else, the
/// check's 404 when the pair is not theirs.</item>
/// <item><c>GET /api/users/me/tried</c> (session): <see cref="TriedListDto"/>, the caller's own pairs, newest first,
/// at most <see cref="PairsPage"/>.</item>
/// <item><c>GET /api/users/me/taste</c>, <c>PATCH</c> with <c>{ learning }</c>, <c>DELETE</c> (session): the taste card —
/// the facts, the literal advisory the stylist is sent, the learning switch and the clear. Nothing in it is a secret from
/// its subject. See <see cref="Taste"/>.</item>
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

    /// <summary>How many pairs GET /api/users/me/tried answers with. A person tries a handful, not a feed.</summary>
    public const int PairsPage = 20;

    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/checks/{id:guid}/useful", UsefulAsync).RequireRateLimiting(Policy);

        // Round 14 — the loop. The pair and the profile belong to an account: a guest has one look a day, not a history.
        var tried = app.MapGroup("/api/checks").RequireAuthorization();
        tried.MapPost("/{id:guid}/tried", TriedAsync).RequireRateLimiting(Policy);
        tried.MapPost("/{id:guid}/tried/prefer", PreferAsync).RequireRateLimiting(Policy);

        var me = app.MapGroup("/api/users/me").RequireAuthorization();
        me.MapGet("/tried", ListTriedAsync);
        me.MapGet("/taste", TasteAsync);
        me.MapPatch("/taste", TasteSwitchAsync).RequireRateLimiting(Policy);
        me.MapDelete("/taste", TasteClearAsync).RequireRateLimiting(Policy);
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

        // Round 14: a typed reason is the row of taps; it decides the yes/no on its own. An unknown word is refused rather
        // than folded into "no", so a client that invents one is told instead of quietly storing a lie.
        string? reason = null;
        if (body?.Reason is { } given && given.Trim().Length > 0)
        {
            reason = given.Trim().ToLowerInvariant();
            if (!TipReason.IsKnown(reason))
            {
                return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.reason_invalid"));
            }
        }

        if (reason is null && body?.Useful is null)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.useful_invalid"));
        }

        var useful = reason is not null ? TipReason.Landed(reason) : body!.Useful!.Value;

        var note = OutfitAnalyzer.SanitizeOccasion(body?.Note);
        if (note.Length > NoteMaxLength)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.useful_note_too_long"));
        }

        var now = clock.UtcNow;
        check.Useful = useful;
        check.UsefulAt = now;
        check.UsefulNote = note.Length == 0 ? null : note;
        check.UsefulReason = reason;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Useful: check {CheckId} {Verdict} {Reason}", check.Id, useful ? "yes" : "no", reason ?? "-");

        return Results.Json(
            new UsefulDto(check.Id, useful, DateTime.SpecifyKind(now, DateTimeKind.Utc), check.UsefulNote, reason), AppJson.Options);
    }

    // ---- Round 14 — "I tried it" ----

    /// <summary>
    /// Links a check as the attempt at an earlier one. Everything is checked against the caller's own rows, and the pair
    /// is written only here, once both verdicts exist. The second check was an ordinary check: no field of this request
    /// reached the stylist, and no score is read, compared or changed by this route.
    /// </summary>
    private static async Task<IResult> TriedAsync(
        Guid id, TriedRequest? body, HttpContext context, AppDbContext db, Localizer localizer, IClock clock, ILogger<Taste> logger, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var language = user.PreferredLanguage;
        var after = await db.Checks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.UserId == user.Id, ct);
        if (after is null)
        {
            return UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(language, "error.check_not_found"));
        }

        if (body?.BeforeId is not { } beforeId)
        {
            return UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(language, "error.check_not_found"));
        }

        if (beforeId == id)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.tried_same_check"));
        }

        var before = await db.Checks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == beforeId && c.UserId == user.Id, ct);
        if (before is null)
        {
            return UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(language, "error.check_not_found"));
        }

        if (before.Status != CheckStatus.Ok || after.Status != CheckStatus.Ok)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.tried_not_scored"));
        }

        if (after.CreatedAt <= before.CreatedAt)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.tried_order"));
        }

        var taken = await db.CheckLinks.AnyAsync(
            l => l.BeforeCheckId == beforeId || l.AfterCheckId == beforeId || l.BeforeCheckId == id || l.AfterCheckId == id, ct);
        if (taken)
        {
            return UserEndpoints.Error(StatusCodes.Status409Conflict, localizer.Get(language, "error.tried_already"));
        }

        var link = new CheckLink
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            BeforeCheckId = beforeId,
            AfterCheckId = id,
            Preferred = "",
            CreatedAt = clock.UtcNow
        };
        db.CheckLinks.Add(link);
        await db.SaveChangesAsync(ct);
        await Counters.IncrementAsync(db, CounterName.TriedPairs, ct);
        logger.LogInformation("Tried: check {AfterId} follows {BeforeId}", id, beforeId);

        return Results.Json(Pair(link, before, after), AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>"Which do you prefer?", stored as the person's own answer. It changes nothing about either score.</summary>
    private static async Task<IResult> PreferAsync(
        Guid id, PreferRequest? body, HttpContext context, AppDbContext db, Localizer localizer, IClock clock, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var language = user.PreferredLanguage;
        var link = await db.CheckLinks.FirstOrDefaultAsync(
            l => l.UserId == user.Id && (l.BeforeCheckId == id || l.AfterCheckId == id), ct);
        if (link is null)
        {
            return UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(language, "error.check_not_found"));
        }

        var prefer = body?.Prefer?.Trim().ToLowerInvariant();
        if (!PairSide.IsKnown(prefer))
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.prefer_invalid"));
        }

        link.Preferred = prefer!;
        link.PreferredAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        var before = await db.Checks.AsNoTracking().FirstAsync(c => c.Id == link.BeforeCheckId, ct);
        var after = await db.Checks.AsNoTracking().FirstAsync(c => c.Id == link.AfterCheckId, ct);
        return Results.Json(Pair(link, before, after), AppJson.Options);
    }

    /// <summary>The caller's own pairs, newest first. Their rows only; there is nothing here of anybody else's.</summary>
    private static async Task<IResult> ListTriedAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var links = await db.CheckLinks.AsNoTracking()
            .Where(l => l.UserId == user.Id)
            .OrderByDescending(l => l.CreatedAt)
            .Take(PairsPage)
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return Results.Json(new TriedListDto([]), AppJson.Options);
        }

        var ids = links.SelectMany(l => new[] { l.BeforeCheckId, l.AfterCheckId }).Distinct().ToList();
        var checks = await db.Checks.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var posts = await db.Posts.AsNoTracking()
            .Where(p => ids.Contains(p.CheckId))
            .Select(p => new { p.CheckId, p.Id })
            .ToDictionaryAsync(p => p.CheckId, p => p.Id, ct);

        var items = links
            .Where(l => checks.ContainsKey(l.BeforeCheckId) && checks.ContainsKey(l.AfterCheckId))
            .Select(l => Pair(l, checks[l.BeforeCheckId], checks[l.AfterCheckId], posts))
            .ToList();
        return Results.Json(new TriedListDto(items), AppJson.Options);
    }

    /// <summary>The pair as the app draws it: both sides, what changed, and the person's preference.</summary>
    public static TriedPairDto Pair(CheckLink link, OutfitCheck before, OutfitCheck after, Dictionary<Guid, Guid>? posts = null) =>
        new(
            link.Id,
            Side(before, posts),
            Side(after, posts),
            Taste.Changes(before.FeedbackJson, after.FeedbackJson),
            link.Preferred.Length == 0 ? null : link.Preferred,
            link.PreferredAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null,
            DateTime.SpecifyKind(link.CreatedAt, DateTimeKind.Utc));

    private static TriedSideDto Side(OutfitCheck check, Dictionary<Guid, Guid>? posts)
    {
        OutfitFeedback? feedback = null;
        if (check.FeedbackJson is not null)
        {
            try
            {
                feedback = JsonSerializer.Deserialize<OutfitFeedback>(check.FeedbackJson, AppJson.Options);
            }
            catch (JsonException)
            {
                feedback = null;
            }
        }

        return new TriedSideDto(
            check.Id,
            check.Intent,
            DateTime.SpecifyKind(check.CreatedAt, DateTimeKind.Utc),
            check.Score ?? feedback?.Score ?? 0,
            feedback?.IntentMatch ?? 0,
            feedback?.Headline ?? "",
            feedback?.OneTip ?? "",
            check.UsefulReason,
            posts is not null && posts.TryGetValue(check.Id, out var postId) ? postId : null);
    }

    // ---- Round 14 — the taste profile ----

    private static async Task<IResult> TasteAsync(HttpContext context, AppDbContext db, Taste taste, Localizer localizer, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        return user is null ? failure! : Results.Json(await taste.CardAsync(user.Id, ct), AppJson.Options);
    }

    private static async Task<IResult> TasteSwitchAsync(
        TasteRequest? body, HttpContext context, AppDbContext db, Taste taste, Localizer localizer, ILogger<Taste> logger, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        if (body?.Learning is not { } learning)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.taste_invalid"));
        }

        await taste.SaveAsync(user.Id, learning, clear: false, ct);
        logger.LogInformation("Taste: learning {State} for {UserId}", learning ? "on" : "off", user.Id);
        return Results.Json(await taste.CardAsync(user.Id, ct), AppJson.Options);
    }

    /// <summary>
    /// "Clear what you have learned": the line is drawn at now and nothing before it is ever read again, so the card is
    /// empty on the next breath and the stylist is sent nothing until there is something new. The checks and the looks
    /// themselves are the person's own history and are not touched; the profile built from them is.
    /// </summary>
    private static async Task<IResult> TasteClearAsync(
        HttpContext context, AppDbContext db, Taste taste, Localizer localizer, ILogger<Taste> logger, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        await taste.SaveAsync(user.Id, learning: null, clear: true, ct);
        logger.LogInformation("Taste: cleared for {UserId}", user.Id);
        return Results.Json(await taste.CardAsync(user.Id, ct), AppJson.Options);
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
