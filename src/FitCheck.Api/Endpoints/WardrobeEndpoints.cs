using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Round 14 — the wardrobe that builds itself. A plain list of the pieces somebody owns, every one of them kept from a
/// check that named it, one tap at a time. Nothing here asks anyone to photograph a closet.
/// <list type="bullet">
/// <item><c>GET /api/wardrobe</c> (session): <see cref="WardrobeDto"/> — the account's pieces, most recently worn first,
/// each with the looks it appeared in (newest first, with the post id where that check was published); the fair-use cap
/// (Plans:WardrobeMaxItems); this account's own switch for sending the names to the stylist; and whether its plan lets
/// them travel at all (Plans:WardrobeNeedsPro). A FREE account sees its whole wardrobe: the wardrobe cannot build itself
/// behind a wall, and it is the thing that makes Pro worth buying.</item>
/// <item><c>POST /api/wardrobe</c> (session): <see cref="KeepWardrobeItemRequest"/> — keep one piece of one check.
/// 201 with the <see cref="WardrobeItemDto"/>, or 200 when the piece was already kept and this check was added to it.
/// The name must be one the check actually named (<see cref="Wardrobe.NamesOn"/>): the wardrobe holds clothes somebody
/// was photographed wearing, never free text. Errors: error.check_not_found (404, another account's check or a guest's),
/// error.wardrobe_unknown_piece (400), error.wardrobe_full (409, at Plans:WardrobeMaxItems).</item>
/// <item><c>PATCH /api/wardrobe/{id}</c> (session, owner): <see cref="RenameWardrobeItemRequest"/> — the person's own
/// name for a piece ("the brown tights"). 200 with the row. Errors: error.wardrobe_name_invalid (400),
/// error.wardrobe_not_found (404, which is also what another account's piece answers), error.wardrobe_full (409 when the
/// new name is already another of this account's pieces — two rows cannot become one without losing a look).</item>
/// <item><c>DELETE /api/wardrobe/{id}</c> (session, owner): 204. Its appearances go with it; the checks do not. Anything
/// that is not a piece of the caller's answers 204 too, existing or not, so the route is idempotent for its owner and
/// says nothing at all to anyone else.</item>
/// <item><c>POST /api/wardrobe/stylist</c> (session): <see cref="WardrobeStylistRequest"/> — whether this account's piece
/// names travel with its checks. 200 with the wardrobe. 403 error.pro_required for a free account while
/// Plans:WardrobeNeedsPro is on: the list is everyone's, the advice from it is what Pro sells.</item>
/// </list>
/// Round 20 — filling it faster, three more doors:
/// <list type="bullet">
/// <item><c>POST /api/wardrobe/keep-all</c> (session): <see cref="KeepAllWardrobeRequest"/> — every piece the check
/// named, in one request; the names are the server's own <see cref="Wardrobe.NamesOn"/>, never the client's. 200 with
/// <see cref="KeepAllWardrobeDto"/> (a batch has no single 201 to give; <c>Added</c> says it): a known piece gains
/// this look and is never refused by the cap, a new one is added while the account is under Plans:WardrobeMaxItems and
/// skipped once it is not (<c>Full</c>). A check that named nothing keeps nothing and still answers 200. Errors:
/// error.invalid_request (400, no check id), error.check_not_found (404, another account's or a guest's), and
/// error.wardrobe_full (409) only when nothing at all could be written.</item>
/// <item><c>GET /api/wardrobe/unkept</c> (session): <see cref="UnkeptWardrobeDto"/> — the pieces named on the account's
/// last Plans:WardrobeUnkeptChecks scored checks that are not in its wardrobe, newest look first, each once with the
/// check that named it, so keeping one goes through <c>POST /api/wardrobe</c> unchanged. Empty when the setting is 0
/// or nothing is left. The wardrobe records no refusals, so "Not this one" in the keep row brings a piece back here.</item>
/// <item><c>POST /api/wardrobe/moment</c> (session): <see cref="WardrobeMomentRequest"/> — the Pro moment's tally,
/// "shown" or "go". 204 either way; the counter moves only while <see cref="Plans.WardrobeProMoment"/> is true for
/// this account right now, so a script cannot inflate a rate the moment never earned. 400 error.invalid_request for
/// any other step. The moment itself is <see cref="WardrobeDto"/>.ProMoment, computed here, never on the client.</item>
/// </list>
/// What the names are for lives in <see cref="Wardrobe"/>: a tip that can name a piece the wearer already owns is advice,
/// and one that cannot is shopping.
/// </summary>
public static class WardrobeEndpoints
{
    public static IEndpointRouteBuilder MapWardrobeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/wardrobe").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapPost("/", KeepAsync);
        group.MapGet("/unkept", UnkeptAsync);
        group.MapPost("/keep-all", KeepAllAsync);
        group.MapPost("/moment", MomentAsync);
        group.MapPost("/stylist", StylistAsync);
        group.MapPatch("/{id:guid}", RenameAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    private static async Task<IResult> ListAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IClock clock, IOptions<PlanOptions> plans, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        return me is null ? failure! : Results.Json(await ReadAsync(db, me, plans.Value, clock.UtcNow, ct), AppJson.Options);
    }

    private static async Task<IResult> KeepAsync(
        KeepWardrobeItemRequest? body, HttpContext context, AppDbContext db, Localizer localizer, IClock clock,
        IOptions<PlanOptions> plans, ILoggerFactory loggers, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lang = me.PreferredLanguage;
        var name = Wardrobe.CleanName(body?.Name);
        if (body?.CheckId is not { } checkId || name.Length == 0)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.wardrobe_name_invalid", Wardrobe.NameMaxLength));
        }

        // The check has to be this account's own. A guest's check and another person's answer alike, so a stranger's id
        // tells nobody anything (the rule this route declares in SecurityTests).
        var check = await db.Checks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkId, ct);
        if (check is null || check.UserId != me.Id)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.check_not_found"));
        }

        // Only a piece the stylist named on THIS check. That is what keeps the wardrobe a wardrobe.
        var candidate = Wardrobe.NamesOn(check).FirstOrDefault(c => Wardrobe.KeyOf(c.Name) == Wardrobe.KeyOf(name));
        if (candidate is null)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.wardrobe_unknown_piece"));
        }

        // The cap is a brake on a script, not a product limit; a piece already kept is never refused by it, because
        // keeping it again only adds this look to the row it already has.
        var known = await db.WardrobeItems.AnyAsync(i => i.UserId == me.Id && i.NameKey == Wardrobe.KeyOf(candidate.Name), ct);
        if (!known && await Wardrobe.CountAsync(db, me.Id, ct) >= plans.Value.WardrobeMaxItems)
        {
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.wardrobe_full", plans.Value.WardrobeMaxItems));
        }

        var (item, added) = await Wardrobe.KeepAsync(db, me.Id, check, candidate.Name, candidate.Category, clock.UtcNow, ct);
        loggers.CreateLogger(nameof(WardrobeEndpoints)).LogInformation("Wardrobe: {UserId} kept a piece from check {CheckId} ({Added})", me.Id, check.Id, added ? "new" : "again");
        var dto = (await DtosAsync(db, me.Id, [item], ct)).Single();
        return Results.Json(dto, AppJson.Options, statusCode: added ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    }

    private static async Task<IResult> KeepAllAsync(
        KeepAllWardrobeRequest? body, HttpContext context, AppDbContext db, Localizer localizer, IClock clock,
        IOptions<PlanOptions> plans, ILoggerFactory loggers, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lang = me.PreferredLanguage;
        if (body?.CheckId is not { } checkId)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.invalid_request"));
        }

        // The same door as the single route: a guest's check and another person's answer alike.
        var check = await db.Checks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkId, ct);
        if (check is null || check.UserId != me.Id)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.check_not_found"));
        }

        var max = plans.Value.WardrobeMaxItems;
        var candidates = Wardrobe.NamesOn(check);
        var (items, added, skipped) = await Wardrobe.KeepAllAsync(db, me.Id, check, candidates, max, clock.UtcNow, ct);
        if (items.Count == 0 && skipped > 0)
        {
            // Nothing fitted and nothing known was touched: the plain refusal the single route gives, rather than a 200
            // that says it kept nothing.
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.wardrobe_full", max));
        }

        if (items.Count > 0)
        {
            await Counters.IncrementAsync(db, CounterName.WardrobeKeepAll, ct);
            loggers.CreateLogger(nameof(WardrobeEndpoints)).LogInformation(
                "Wardrobe: {UserId} kept all from check {CheckId} ({Added} new, {Skipped} refused by the cap)", me.Id, check.Id, added, skipped);
        }

        var count = await Wardrobe.CountAsync(db, me.Id, ct);
        var dto = new KeepAllWardrobeDto(items.Count, added, skipped, skipped > 0, count, max, await DtosAsync(db, me.Id, items, ct));
        return Results.Json(dto, AppJson.Options);
    }

    private static async Task<IResult> UnkeptAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IOptions<PlanOptions> plans, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var (pieces, checks) = await Wardrobe.UnkeptAsync(db, me.Id, plans.Value.WardrobeUnkeptChecks, Wardrobe.UnkeptMaxPieces, ct);
        var dto = new UnkeptWardrobeDto(pieces.Select(p => new UnkeptPieceDto(p.Name, p.Category, p.CheckId, p.WornAt, p.PostId)).ToList(), checks);
        return Results.Json(dto, AppJson.Options);
    }

    private static async Task<IResult> MomentAsync(
        WardrobeMomentRequest? body, HttpContext context, AppDbContext db, Localizer localizer, IClock clock,
        IOptions<PlanOptions> plans, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var name = body?.Step switch
        {
            "shown" => CounterName.WardrobeMomentShown,
            "go" => CounterName.WardrobeMomentGo,
            _ => null
        };
        if (name is null)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.invalid_request"));
        }

        // A tally is never worth an error, and it only moves while the moment is true for this account right now: the
        // rate on the numbers page is shown over tapped for moments the server itself would have drawn.
        var count = await Wardrobe.CountAsync(db, me.Id, ct);
        if (Plans.WardrobeProMoment(me, plans.Value, clock.UtcNow, count))
        {
            await Counters.IncrementAsync(db, name, ct);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> RenameAsync(
        Guid id, RenameWardrobeItemRequest? body, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lang = me.PreferredLanguage;
        var name = Wardrobe.CleanName(body?.Name);
        if (name.Length == 0)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.wardrobe_name_invalid", Wardrobe.NameMaxLength));
        }

        var item = await db.WardrobeItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null || item.UserId != me.Id)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.wardrobe_not_found"));
        }

        var key = Wardrobe.KeyOf(name);
        if (key != item.NameKey && await db.WardrobeItems.AnyAsync(i => i.UserId == me.Id && i.NameKey == key, ct))
        {
            // Renaming one piece onto another would have to merge two rows and drop a look from one of them. Refused
            // plainly instead: delete the one you meant to lose.
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.wardrobe_full", await Wardrobe.CountAsync(db, me.Id, ct)));
        }

        item.Name = name;
        item.NameKey = key;
        await db.SaveChangesAsync(ct);
        return Results.Json((await DtosAsync(db, me.Id, [item], ct)).Single(), AppJson.Options);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        // 204 for anything that is not a piece of the caller's, whether or not it exists: a second tap on your own is not
        // an error, and a stranger's id must not answer differently from a made-up one — a delete that said "not yours"
        // for a real id and "gone" for a fake one would be an oracle for what other accounts keep. The rule is declared
        // that way in SecurityTests, and WardrobeTests proves the owner's piece is still there afterwards.
        var mine = await db.WardrobeItems.FirstOrDefaultAsync(i => i.Id == id && i.UserId == me.Id, ct);
        if (mine is null)
        {
            return Results.NoContent();
        }

        await db.WardrobeAppearances.Where(a => a.ItemId == mine.Id).ExecuteDeleteAsync(ct);
        db.WardrobeItems.Remove(mine);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> StylistAsync(
        WardrobeStylistRequest? body, HttpContext context, AppDbContext db, Localizer localizer, IClock clock,
        IOptions<PlanOptions> plans, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var now = clock.UtcNow;
        // The list is everyone's; a tip that names a piece you already own is what Pro sells.
        if (!Plans.WardrobeReachesStylist(me, plans.Value, now))
        {
            return Error(StatusCodes.Status403Forbidden, localizer.Get(me.PreferredLanguage, "error.pro_required"));
        }

        var setting = await Wardrobe.SettingAsync(db, me.Id, now, ct);
        setting.ToStylist = body?.On ?? true;
        setting.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Results.Json(await ReadAsync(db, me, plans.Value, now, ct), AppJson.Options);
    }

    /// <summary>The whole wardrobe as the client reads it: the pieces, the cap, the switch and whether the plan honours it.</summary>
    private static async Task<WardrobeDto> ReadAsync(AppDbContext db, AppUser me, PlanOptions plans, DateTime now, CancellationToken ct)
    {
        var rows = await Wardrobe.ListAsync(db, me.Id, ct);
        var items = await DtosAsync(db, me.Id, rows.Select(r => r.Item).ToList(), ct);
        var setting = await db.WardrobeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == me.Id, ct);
        // Round 20: the Pro moment's truth travels with the list, so the sentence the client draws is the server's fact.
        return new WardrobeDto(items, plans.WardrobeMaxItems, setting?.ToStylist ?? true, Plans.WardrobeReachesStylist(me, plans, now),
            ProMoment: Plans.WardrobeProMoment(me, plans, now, items.Count), ProSees: plans.WardrobeNamesFor(true));
    }

    /// <summary>
    /// The rows with their looks, in one read of the appearances and one of the posts behind them, whatever the count.
    /// A look that was published carries its post id, so the list can open the look it names.
    /// </summary>
    private static async Task<List<WardrobeItemDto>> DtosAsync(AppDbContext db, Guid userId, List<WardrobeItem> items, CancellationToken ct)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var ids = items.Select(i => i.Id).ToList();
        var looks = await db.WardrobeAppearances.AsNoTracking()
            .Where(a => ids.Contains(a.ItemId))
            .OrderByDescending(a => a.WornAt)
            .ToListAsync(ct);
        var checkIds = looks.Select(a => a.CheckId).Distinct().ToList();
        // The account's own visible looks only: a check whose post was deleted or hidden is a private check again.
        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.UserId == userId && !p.Hidden && checkIds.Contains(p.CheckId))
            .Select(p => new { p.CheckId, p.Id })
            .ToListAsync(ct);
        var postByCheck = posts.ToDictionary(p => p.CheckId, p => p.Id);
        var byItem = looks.ToLookup(a => a.ItemId);
        return items.Select(item => new WardrobeItemDto(
            item.Id, item.Name, item.Category, Utc(item.CreatedAt), Utc(item.LastSeenAt),
            byItem[item.Id].Select(a => new WardrobeLookDto(a.CheckId, Utc(a.WornAt), postByCheck.TryGetValue(a.CheckId, out var postId) ? postId : null)).ToList())).ToList();
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
