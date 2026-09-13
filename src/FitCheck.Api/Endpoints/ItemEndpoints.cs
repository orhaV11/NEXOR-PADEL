using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Items on a look (Round 10): the owner tags the pieces (brand, model, store link, a dot on the photo), anyone can find
/// looks by brand or category, and a store link leaves the app through one door. The contract:
/// <list type="bullet">
/// <item><c>PATCH /api/posts/{id}/items</c> (owner): the whole list (<see cref="UpdateItemsRequest"/>), at most
/// <see cref="PostItems.MaxTagged"/> items, validated by <see cref="PostItems.Apply"/>: a typed name 1..40, a stylist
/// name sent back as it was, brand ≤ 40, model ≤ 60, url http(s) ≤ 500 without user info (<see cref="PostItems.IsStoreUrl"/>),
/// x and y both in 0..1 or both absent, confirmed only with a brand. A row named by id keeps its source while only its
/// brand, model, link, dot or confirmation change; a new row is the person's. Answers the post's <see cref="PostItemDto"/>
/// list in order. Errors: error.items_too_many, error.item_invalid, error.item_url_invalid, error.item_position_invalid;
/// error.post_not_found (404) for a look that is not the caller's or is hidden.</item>
/// <item><c>GET /api/items?brand=&amp;category=&amp;q=&amp;offset=&amp;limit=</c> (public): <see cref="ItemsDto"/>, visible looks
/// carrying one item that matches every filter given (brand case-insensitively, category exactly, q anywhere in the name,
/// the brand or the model), newest first, paged like a feed; no filter at all is an empty page.</item>
/// <item><c>GET /api/items/brands?q=</c> (public): <see cref="BrandsDto"/> for the autocomplete: brands already on visible
/// looks with their look counts, merged case-insensitively, plus brand accounts whose handle or name matches, at most
/// <see cref="BrandsCount"/>; a brand account of the same name rides on the tagged brand as its Account.</item>
/// <item><c>GET /api/items/{id}/out</c> (public, the "out" rate-limit policy): 302 to the item's url (in its ASCII form,
/// <see cref="PostItems.AsciiUrl"/>, when it was pasted with characters outside ASCII) with the affiliate parameters for
/// its host (<see cref="AffiliateOptions.ParametersFor"/>) appended, <c>Referrer-Policy: no-referrer</c>,
/// <c>Cache-Control: no-store</c>, counted in <see cref="CounterName.ItemOuts"/> once the Location header is set; 404 for
/// a missing item, one without a link, or one on a hidden look.</item>
/// </list>
/// </summary>
public static class ItemEndpoints
{
    /// <summary>Rate-limit policy (Program.cs) on /api/items/{id}/out: <see cref="OutsPerMinute"/> per client address, a minute's window.</summary>
    public const string OutPolicy = "out";

    public const int OutsPerMinute = 60;

    /// <summary>Brands the autocomplete answers with, at most.</summary>
    public const int BrandsCount = 20;

    /// <summary>The longest q the item search and the autocomplete take, like /api/search.</summary>
    public const int QueryMaxLength = 40;

    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/api/posts/{id:guid}/items", UpdateItemsAsync).RequireAuthorization();
        app.MapGet("/api/items", SearchAsync);
        app.MapGet("/api/items/brands", BrandsAsync);
        app.MapGet("/api/items/{id:guid}/out", OutAsync).RequireRateLimiting(OutPolicy);
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    private static async Task<IResult> UpdateItemsAsync(
        Guid id, UpdateItemsRequest? body, HttpContext context, AppDbContext db, Localizer localizer, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lang = me.PreferredLanguage;
        // No body, and no list in it, is a broken call, not an empty list: an empty list clears the look and has to be said.
        if (body?.Items is null)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.item_invalid"));
        }

        var post = await db.Posts.FindAsync([id], ct);
        // Another person's look and a hidden one answer alike, so the route tells a stranger nothing about what exists.
        if (post is null || post.UserId != me.Id || post.Hidden)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.post_not_found"));
        }

        var existing = await db.PostItems.Where(i => i.PostId == id).OrderBy(i => i.Position).ToListAsync(ct);
        var error = PostItems.Apply(id, body.Items, existing, out var rows);
        if (error is not null)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, error, PostItems.MaxTagged));
        }

        // The rows the person kept were edited in place (tracked); the rest go, the new ones come.
        db.PostItems.RemoveRange(existing.Where(r => !rows.Contains(r)));
        db.PostItems.AddRange(rows.Where(r => !existing.Contains(r)));
        await db.SaveChangesAsync(ct);
        loggerFactory.CreateLogger(nameof(ItemEndpoints)).LogInformation("Items: {Count} on post {PostId} by {UserId}", rows.Count, id, me.Id);
        return Results.Json(rows.Select(PostReader.ItemDto).ToList(), AppJson.Options);
    }

    private static async Task<IResult> SearchAsync(
        HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, string? brand, string? category, string? q, int? offset, int? limit, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var (skip, take) = PostEndpoints.Page(offset, limit);
        var brandTerm = Optional(brand?.Trim());
        var categoryTerm = Optional(category?.Trim().ToLowerInvariant());
        var term = Optional(q?.Trim());
        if (brandTerm is { Length: > PostItems.BrandMaxLength } || term is { Length: > QueryMaxLength })
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(PostEndpoints.Language(context, null), "error.search_invalid"));
        }

        // Nothing asked, or a category nobody stores: an empty page, never everything and never a 404.
        if ((brandTerm is null && categoryTerm is null && term is null) || (categoryTerm is not null && Array.IndexOf(PostItems.Categories, categoryTerm) < 0))
        {
            return Results.Json(new ItemsDto(brandTerm, categoryTerm, term, [], null), AppJson.Options);
        }

        // One row has to match every filter: "Nike" + "bottom" are the Nike pants, not a Nike top on a look with pants.
        // Names are stored lower-cased and SQLite's LIKE folds ASCII case, so the lowered term meets a brand or a model
        // typed in any case too; "%" and "_" typed by a person are literal.
        var matching = db.PostItems.AsQueryable();
        if (brandTerm is not null)
        {
            matching = matching.Where(i => i.Brand != null && EF.Functions.Collate(i.Brand, "NOCASE") == brandTerm);
        }

        if (categoryTerm is not null)
        {
            matching = matching.Where(i => i.Category == categoryTerm);
        }

        if (term is not null)
        {
            var pattern = "%" + ExploreEndpoints.EscapeLike(term.ToLowerInvariant()) + "%";
            matching = matching.Where(i => EF.Functions.Like(i.Name, pattern, "\\")
                                           || (i.Brand != null && EF.Functions.Like(i.Brand, pattern, "\\"))
                                           || (i.Model != null && EF.Functions.Like(i.Model, pattern, "\\")));
        }

        var posts = await db.Posts
            .Where(p => !p.Hidden
                        && matching.Any(i => i.PostId == p.Id)
                        && db.Users.Any(u => u.Id == p.UserId && !u.Suspended))
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(ct);
        var page = await PostEndpoints.PageDtoAsync(reader, posts, viewerId, skip, take, ct);

        // The brand goes back in the spelling most looks carry ("Nike" for ?brand=nike), so the page can head itself.
        var brandName = brandTerm is null
            ? null
            : await db.PostItems
                .Where(i => i.Brand != null && EF.Functions.Collate(i.Brand, "NOCASE") == brandTerm)
                .GroupBy(i => i.Brand)
                .Select(g => new { Brand = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ThenBy(x => x.Brand)
                .Select(x => x.Brand)
                .FirstOrDefaultAsync(ct) ?? brandTerm;
        return Results.Json(new ItemsDto(brandName, categoryTerm, term, page.Items, page.NextOffset), AppJson.Options);
    }

    private static async Task<IResult> BrandsAsync(HttpContext context, AppDbContext db, Localizer localizer, string? q, CancellationToken ct)
    {
        var term = (q ?? "").Trim();
        if (term.Length > QueryMaxLength)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(PostEndpoints.Language(context, null), "error.search_invalid"));
        }

        // Brands on visible looks, grouped in .NET so "Nike" and "nike" are one brand in any script (SQLite's lower() and
        // LIKE fold ASCII only). Pilot scale: the branded rows are a short list.
        var tagged = await db.PostItems
            .Where(i => i.Brand != null && db.Posts.Any(p => p.Id == i.PostId && !p.Hidden && db.Users.Any(u => u.Id == p.UserId && !u.Suspended)))
            .Select(i => new { Brand = i.Brand!, i.PostId })
            .ToListAsync(ct);
        var entries = new Dictionary<string, BrandEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in tagged.Where(t => term.Length == 0 || t.Brand.StartsWith(term, StringComparison.OrdinalIgnoreCase)).GroupBy(t => t.Brand, StringComparer.OrdinalIgnoreCase))
        {
            // The spelling most rows carry heads the brand; ties go to the first in ordinal order, so the answer is stable.
            var name = group.GroupBy(t => t.Brand, StringComparer.Ordinal).OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key;
            entries[name] = new BrandEntry(name, group.Select(t => t.PostId).Distinct().Count(), null, false);
        }

        // Brand accounts whose handle starts with the term or whose name carries it, as the search matches them; a suspended
        // account is nobody's brand. One of the same name as a tagged brand rides on it; the rest stand on their own.
        var lower = term.ToLowerInvariant();
        var accounts = await db.Users
            .Where(u => u.AccountType == AccountType.Brand && !u.Suspended && (term == "" || u.HandleLower.StartsWith(lower) || u.DisplayName != null))
            .ToListAsync(ct);
        foreach (var account in accounts.OrderBy(u => u.HandleLower, StringComparer.Ordinal))
        {
            var name = PostReader.NameOf(account.Handle, account.DisplayName);
            if (term.Length > 0 && !account.HandleLower.StartsWith(lower, StringComparison.Ordinal) && !name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = entries.ContainsKey(account.Handle) ? account.Handle : name;
            if (entries.TryGetValue(key, out var entry))
            {
                if (entry.Account is null)
                {
                    entries[key] = entry with { Account = PostReader.Ref(account), Verified = account.Verified };
                }
            }
            else
            {
                entries[name] = new BrandEntry(name, 0, PostReader.Ref(account), account.Verified);
            }
        }

        var items = entries.Values
            .OrderByDescending(e => e.Looks)
            .ThenByDescending(e => e.Account is not null)
            .ThenByDescending(e => e.Verified)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(BrandsCount)
            .Select(e => new BrandDto(e.Name, e.Looks, e.Account))
            .ToList();
        return Results.Json(new BrandsDto(items), AppJson.Options);
    }

    private sealed record BrandEntry(string Name, int Looks, UserRefDto? Account, bool Verified);

    /// <summary>
    /// The one door a store link leaves through. The link is stored as given and sent in a form a header can carry
    /// (<see cref="PostItems.AsciiUrl"/>); the affiliate parameters for its host are appended here, never stored, so a
    /// change of programme changes every link at once. No referrer: the store learns nothing about the look or the person;
    /// no caching: every tap is counted, and only once the Location header holds the link, so a tap that did not leave is
    /// not a tap that left.
    /// </summary>
    private static async Task<IResult> OutAsync(
        Guid id, HttpContext context, AppDbContext db, IOptions<AffiliateOptions> affiliate, Localizer localizer, CancellationToken ct)
    {
        var item = await db.PostItems.Where(i => i.Id == id).Select(i => new { i.Url, i.PostId }).FirstOrDefaultAsync(ct);
        // A missing item, one without a link, and one on a hidden look answer alike: the door only opens onto a public look.
        if (item is null || !PostItems.IsStoreUrl(item.Url) || !await db.Posts.AnyAsync(p => p.Id == item.PostId && !p.Hidden, ct))
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, context.Request), "error.item_not_found"));
        }

        var url = item.Url!.Trim();
        var target = PostItems.OutUrl(url, affiliate.Value.ParametersFor(new Uri(url, UriKind.Absolute).Host));
        // The header first: Kestrel refuses a value it cannot send, and a refusal must not be counted as a tap that left.
        context.Response.Headers.Location = target;
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.CacheControl = "no-store";
        await Counters.IncrementAsync(db, CounterName.ItemOuts, ct);
        return Results.Redirect(target);
    }

    private static string? Optional(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
