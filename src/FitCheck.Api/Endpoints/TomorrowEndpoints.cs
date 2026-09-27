using System.Globalization;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Round 19 — Tomorrow: <c>GET /api/tomorrow</c> (the screen's first paint, never a model call), <c>POST /api/tomorrow</c>
/// (one planned outfit, every gate the check route has and in the same order) and <c>POST /api/tomorrow/{id}/useful</c>
/// (the thumbs, in the check's own vocabulary). Signed in only: a guest has no wardrobe. Plans:TomorrowEnabled off
/// makes every route a 404.
/// </summary>
public static class TomorrowEndpoints
{
    public static IEndpointRouteBuilder MapTomorrowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tomorrow").RequireAuthorization();
        group.MapGet("/", ReadAsync);
        group.MapPost("/", ComposeAsync).DisableAntiforgery();
        group.MapPost("/{id:guid}/useful", UsefulAsync).RequireRateLimiting(FeedbackEndpoints.Policy);
        return app;
    }

    /// <summary>The wardrobe as the model would be shown it: the plan's slice of the rows, each with its looks.</summary>
    private static async Task<List<(WardrobeItem Item, List<WardrobeAppearance> Looks)>> OfferedAsync(AppDbContext db, AppUser me, PlanOptions plans, bool isPro, CancellationToken ct)
    {
        var rows = await Wardrobe.ListAsync(db, me.Id, ct);
        var looks = rows.ToDictionary(r => r.Item.Id, r => r.Looks);
        return Wardrobe.PromptItems(rows.Select(r => r.Item), plans.WardrobeNamesFor(isPro))
            .Select(item => (item, looks[item.Id]))
            .ToList();
    }

    /// <summary>
    /// What is left today and this month, computed the way the routes enforce it: Pro against its own bucket, free
    /// against its brake AND its shared day, whichever is smaller; the month against the one pot. Never negative.
    /// </summary>
    private static async Task<Tomorrow.Numbers> NumbersAsync(AppDbContext db, AppUser me, PlanOptions plans, LimitsOptions limits, DateTime now, CancellationToken ct)
    {
        var suggestionsToday = await Spend.RecentSuggestionsForUserAsync(db, me.Id, now, ct);
        int cap, left;
        if (Plans.IsPro(me, now))
        {
            cap = Plans.ProSuggestionCap(plans, limits);
            left = cap - suggestionsToday.Count;
        }
        else
        {
            cap = Plans.FreeSuggestionCap(plans, limits);
            var together = await Spend.RecentForUserAsync(db, me.Id, now, ct, plans.NoOutfitForgivenPerDay, Allowance.Together);
            left = Math.Min(cap - suggestionsToday.Count, Plans.CapFor(me, plans, limits, now) - together.Count);
        }

        var capMonth = Plans.MonthlyCallsFor(me, plans, now);
        var leftMonth = capMonth > 0 ? capMonth - await Spend.MonthCountForUserAsync(db, me.Id, now, ct) : 0;
        return new Tomorrow.Numbers(Math.Max(0, left), cap, Math.Max(0, leftMonth), capMonth);
    }

    private static IResult NotFound(Localizer localizer, string language) =>
        UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(language, "error.tomorrow_not_found"));

    /// <summary>
    /// The screen's first paint: what this account may do and why not, the wardrobe against the two minimums, the
    /// strip of its own pieces as photos, the recent outfits, the numbers, the chips to pre-light. Never a model call.
    /// </summary>
    private static async Task<IResult> ReadAsync(
        HttpContext context, AppDbContext db, Tomorrow tomorrow, Localizer localizer, IClock clock,
        IOptions<PlanOptions> plans, IOptions<LimitsOptions> limits, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var language = PostEndpoints.Language(context, me);
        if (!plans.Value.TomorrowEnabled)
        {
            return NotFound(localizer, language);
        }

        var now = clock.UtcNow;
        var isPro = Plans.IsPro(me, now);
        var offered = await OfferedAsync(db, me, plans.Value, isPro, ct);
        var pieces = Tomorrow.Refs(offered);
        var stylistOn = await Wardrobe.ToStylistAsync(db, me.Id, ct);
        var available = Plans.TomorrowReachesStylist(me, plans.Value, now)
            && (isPro ? Plans.ProSuggestionCap(plans.Value, limits.Value) : Plans.FreeSuggestionCap(plans.Value, limits.Value)) > 0
            && plans.Value.WardrobeNamesFor(isPro) > 0;

        var stripPieces = pieces.Take(Tomorrow.StripLength).ToList();
        var photos = await tomorrow.NewestPhotosAsync(me.Id, stripPieces, ct);
        var strip = stripPieces.Select(p =>
        {
            var photo = photos.TryGetValue(p.Item.Id, out var id) ? (Guid?)id : null;
            return new StripPieceDto(p.Item.Id, p.Name, p.Item.Category, photo, photo is { } check ? $"/api/checks/{check}/image" : null, p.Looks.Count);
        }).ToList();

        var numbers = await NumbersAsync(db, me, plans.Value, limits.Value, now, ct);
        var recentRows = await db.Suggestions.AsNoTracking()
            .Where(s => s.UserId == me.Id && s.Status != CheckStatus.Error)
            .OrderByDescending(s => s.CreatedAt)
            .Take(Tomorrow.RecentLength)
            .ToListAsync(ct);
        var recentPieces = await tomorrow.PiecesOfAsync(recentRows.Select(r => r.Id), ct);
        var recent = new List<SuggestionDto>();
        foreach (var row in recentRows)
        {
            var rowPieces = recentPieces.GetValueOrDefault(row.Id, []);
            recent.Add(await tomorrow.DtoAsync(row, rowPieces, numbers, reused: false, stale: await tomorrow.IsStaleAsync(row, rowPieces, ct), counted: false, ct));
        }

        var newest = recentRows.FirstOrDefault(r => r.Status == CheckStatus.Ok);
        string? defaultOccasion = newest?.Occasion.ToString();
        string? defaultStyle = newest?.Style?.ToString();
        if (newest is null)
        {
            var lastCheck = await db.Checks.AsNoTracking()
                .Where(c => c.UserId == me.Id && c.Status == CheckStatus.Ok)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new { c.Occasion, c.Style })
                .FirstOrDefaultAsync(ct);
            defaultOccasion = lastCheck?.Occasion.ToString();
            defaultStyle = lastCheck?.Style?.ToString();
        }

        return Results.Json(new TomorrowDto(
            available, stylistOn, plans.Value.TomorrowNeedsPro && !isPro,
            offered.Count, Tomorrow.Kinds(pieces).Count, Math.Max(0, plans.Value.SuggestionMinPieces), Math.Max(0, plans.Value.SuggestionMinCategories),
            plans.Value.WardrobeNamesFor(isPro), strip, recent,
            numbers.LeftToday, numbers.CapToday, numbers.LeftMonth, numbers.CapMonth, defaultOccasion, defaultStyle), AppJson.Options);
    }

    /// <summary>
    /// One planned outfit. The gates, in the check route's order, the cheap and spend-free refusals first: signed in;
    /// the kill switch; the body; the plan; the wardrobe switch; enough pieces of enough kinds; a stored answer
    /// (served even while the stylist rests); the money ceiling (a Pro account passes it); the month; the day, the
    /// bucket and the brake inside one in-flight reservation; then the call.
    /// </summary>
    private static async Task<IResult> ComposeAsync(
        TomorrowRequest? body, HttpContext context, AppDbContext db, Tomorrow tomorrow, Localizer localizer, IClock clock,
        CheckCapacity capacity, SpendMeter spend, IOptions<PlanOptions> plans, IOptions<LimitsOptions> limits, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var language = PostEndpoints.Language(context, me);
        if (!plans.Value.TomorrowEnabled)
        {
            return NotFound(localizer, language);
        }

        // 2. The body. Where the outfit is going is the one thing that has to be said.
        if (!StyleIntents.TryParseOccasion(body?.Occasion, out var occasion))
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.intent_invalid"));
        }

        if (!StyleIntents.TryParseStyle(body?.Style, out var style))
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.invalid_request"));
        }

        var when = (body?.When ?? "tomorrow").Trim().ToLowerInvariant();
        if (when is not ("today" or "tomorrow"))
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.invalid_request"));
        }

        var now = clock.UtcNow;
        // The phone's own calendar date, honoured within a day of the server's (a phone in Auckland is tomorrow already);
        // anything else, or nothing, is the UTC date.
        var utcToday = DateOnly.FromDateTime(now);
        var today = DateOnly.TryParseExact(body?.Today, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var phoneDay)
            && Math.Abs(phoneDay.DayNumber - utcToday.DayNumber) <= 1
            ? phoneDay
            : utcToday;
        var forDate = when == "tomorrow" ? today.AddDays(1) : today;
        var fresh = body?.Fresh == true;

        // 3. The plan.
        if (!Plans.TomorrowReachesStylist(me, plans.Value, now))
        {
            return UserEndpoints.Error(StatusCodes.Status403Forbidden, localizer.Get(language, "error.pro_required"));
        }

        // 4. The wardrobe switch: the person shut that door themselves. Nothing spent.
        if (!await Wardrobe.ToStylistAsync(db, me.Id, ct))
        {
            return UserEndpoints.Error(StatusCodes.Status409Conflict, localizer.Get(language, "error.tomorrow_stylist_off"));
        }

        // 5. Enough pieces of enough kinds, or a first-class "not enough": nothing spent, the model never asked.
        var isPro = Plans.IsPro(me, now);
        var offered = await OfferedAsync(db, me, plans.Value, isPro, ct);
        var kinds = Tomorrow.Kinds(Tomorrow.Refs(offered)).Count;
        var minPieces = Math.Max(0, plans.Value.SuggestionMinPieces);
        var minKinds = Math.Max(0, plans.Value.SuggestionMinCategories);
        if (offered.Count < minPieces || kinds < minKinds)
        {
            return Results.Json(new TomorrowNeedsDto(offered.Count, kinds, minPieces, minKinds), AppJson.Options);
        }

        // 6. A stored answer, before any gate that could spend: five taps pay once, and a cached answer is served even
        // while the stylist rests.
        if (!fresh && await tomorrow.FindReusableAsync(me.Id, occasion, style, language, forDate, now, ct) is { } reusable)
        {
            var reusablePieces = (await tomorrow.PiecesOfAsync([reusable.Id], ct)).GetValueOrDefault(reusable.Id, []);
            var numbers = await NumbersAsync(db, me, plans.Value, limits.Value, now, ct);
            var stale = await tomorrow.IsStaleAsync(reusable, reusablePieces, ct);
            return Results.Json(await tomorrow.DtoAsync(reusable, reusablePieces, numbers, reused: true, stale, counted: false, ct), AppJson.Options);
        }

        // 7. The money ceiling: Round 17's rule, a Pro account passes it.
        if (await spend.CeilingReachedAsync(db, ct) && !isPro)
        {
            return UserEndpoints.Error(StatusCodes.Status503ServiceUnavailable, localizer.Get(language, "error.stylist_resting"));
        }

        // 8. The month: the same pot the check and the compare use, and a planned outfit is in it now.
        var monthly = Plans.MonthlyCallsFor(me, plans.Value, now);
        if (monthly > 0 && await Spend.MonthCountForUserAsync(db, me.Id, now, ct) >= monthly)
        {
            return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.month_limit", monthly));
        }

        // 9. The day, the bucket and the brake, then the reservation. Pro: its own bucket under its own key, so a check
        // in flight never fills a planned outfit's slot. Free: its one shared day first (checks, comparisons and planned
        // outfits in flight together), then the brake on planned outfits under its own key, each against its own list,
        // so a refusal names the right number and the right wait, and in-flight double-taps cannot slip under either.
        var suggestionsToday = await Spend.RecentSuggestionsForUserAsync(db, me.Id, now, ct);
        var brake = isPro ? Plans.ProSuggestionCap(plans.Value, limits.Value) : Plans.FreeSuggestionCap(plans.Value, limits.Value);
        var storedGlobal = await Spend.StoredGlobalAsync(db, now, ct);
        var suggestionKey = CheckCapacity.KeyFor(me.Id, Allowance.Suggestions);
        IDisposable? reservation;
        IDisposable? brakeReservation = null;
        CapacityVerdict verdict;
        if (isPro)
        {
            verdict = capacity.TryReserve(suggestionKey, suggestionsToday.Count, brake, storedGlobal, limits.Value.ChecksPerDayGlobal, out reservation);
            if (verdict == CapacityVerdict.UserCapReached)
            {
                RetryAfter(Spend.RetryAfterSeconds(suggestionsToday, brake, now));
                return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.tomorrow_limit", brake));
            }
        }
        else
        {
            var dayCap = Plans.CapFor(me, plans.Value, limits.Value, now);
            var recent = await Spend.RecentForUserAsync(db, me.Id, now, ct, plans.Value.NoOutfitForgivenPerDay, Allowance.Together);
            verdict = capacity.TryReserve(me.Id, recent.Count, dayCap, storedGlobal, limits.Value.ChecksPerDayGlobal, out reservation);
            if (verdict == CapacityVerdict.UserCapReached)
            {
                RetryAfter(Spend.RetryAfterSeconds(recent, dayCap, now));
                return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.plan_limit", dayCap,
                    plans.Value.ProCallsPerMonth > 0 ? plans.Value.ProCallsPerMonth : Plans.ProCap(plans.Value, limits.Value)));
            }

            if (verdict == CapacityVerdict.Ok && capacity.TryReserve(suggestionKey, suggestionsToday.Count, brake, out brakeReservation) != CapacityVerdict.Ok)
            {
                // The day's slot goes back: nothing is being spent.
                reservation?.Dispose();
                RetryAfter(Spend.RetryAfterSeconds(suggestionsToday, brake, now));
                return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.tomorrow_free_limit", Plans.ProSuggestionCap(plans.Value, limits.Value)));
            }
        }

        if (verdict == CapacityVerdict.GlobalCapReached)
        {
            return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.rate_limited_global"));
        }

        using var _ = reservation;
        using var __ = brakeReservation;

        void RetryAfter(int? seconds)
        {
            if (seconds is { } wait)
            {
                context.Response.Headers.RetryAfter = wait.ToString(CultureInfo.InvariantCulture);
            }
        }

        // 10, 11. The call. The taste advisory rides along under the same gate as on a check.
        var seq = await tomorrow.NextSeqAsync(me.Id, occasion, style, language, forDate, ct);
        var tasteAllowed = !(plans.Value.TasteNeedsPro && !isPro);
        var input = new Tomorrow.ComposeInput(occasion, style, when, forDate, language, fresh, body?.Lat, body?.Lon, offered, tasteAllowed, seq);
        Tomorrow.Composed composed;
        try
        {
            composed = await tomorrow.ComposeAsync(me, input, now, ct);
        }
        catch (VisionClientException)
        {
            // The row is stored as error and not counted; the person is told nothing was spent.
            return UserEndpoints.Error(StatusCodes.Status502BadGateway, localizer.Get(language, "error.tomorrow_failed"));
        }

        // 12. The answer, with the numbers as they stand now that the row is stored.
        var after = await NumbersAsync(db, me, plans.Value, limits.Value, now, ct);
        var dto = await tomorrow.DtoAsync(composed.Row, composed.Pieces, after, reused: false, stale: false, counted: composed.Row.Status != CheckStatus.Error, ct);
        return Results.Json(dto, AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// The thumbs on a planned outfit, in the check's own vocabulary and under the check's own guards
    /// (<see cref="FeedbackEndpoints.ParseUseful"/>), written onto the row's four columns so Taste reads one shape.
    /// </summary>
    private static async Task<IResult> UsefulAsync(
        Guid id, UsefulRequest? body, HttpContext context, AppDbContext db, Localizer localizer, IClock clock,
        IOptions<PlanOptions> plans, ILogger<Tomorrow> logger, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var language = PostEndpoints.Language(context, me);
        if (!plans.Value.TomorrowEnabled)
        {
            return NotFound(localizer, language);
        }

        var row = await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == me.Id, ct);
        if (row is null)
        {
            return NotFound(localizer, language);
        }

        language = Localizer.IsSupported(row.Language) ? row.Language : language;
        if (row.Status != CheckStatus.Ok)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.useful_not_scored"));
        }

        var (refused, useful, reason, note) = FeedbackEndpoints.ParseUseful(body, language, localizer);
        if (refused is not null)
        {
            return refused;
        }

        var now = clock.UtcNow;
        row.Useful = useful;
        row.UsefulAt = now;
        row.UsefulNote = note;
        row.UsefulReason = reason;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Useful: suggestion {SuggestionId} {Verdict} {Reason}", row.Id, useful ? "yes" : "no", reason ?? "-");
        return Results.Json(new UsefulDto(row.Id, useful, DateTime.SpecifyKind(now, DateTimeKind.Utc), note, reason), AppJson.Options);
    }
}
