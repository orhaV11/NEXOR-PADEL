using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>The home feed: "for you" (ranked, the default), "following", "top" (most fire in 7 days) and "fresh" (newest).</summary>
public static class FeedEndpoints
{
    private static readonly TimeSpan TopWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan ForYouWindow = TimeSpan.FromDays(30);

    /// <summary>Newest visible posts of the last 30 days that get ranked: small enough to score in memory, plenty for a pilot.</summary>
    public const int ForYouCandidates = 400;

    public static IEndpointRouteBuilder MapFeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/feed", FeedAsync);
        return app;
    }

    private static async Task<IResult> FeedAsync(
        HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, string? tab, string? intent, int? offset, int? limit, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var (skip, take) = PostEndpoints.Page(offset, limit);
        var now = DateTime.UtcNow;

        IQueryable<Post> query = db.Posts.Where(p => !p.Hidden);
        if (Enum.TryParse<StyleIntent>(intent, ignoreCase: true, out var filter) && Enum.IsDefined(filter))
        {
            query = query.Where(p => p.Intent == filter);
        }

        switch ((tab ?? "").ToLowerInvariant())
        {
            case "top":
                var since = now - TopWindow;
                query = query.Where(p => p.CreatedAt >= since).OrderByDescending(p => p.FireCount).ThenByDescending(p => p.CreatedAt);
                break;
            case "following":
                if (viewerId is not Guid me)
                {
                    return PostEndpoints.Error(StatusCodes.Status401Unauthorized, localizer.Get(PostEndpoints.Language(context, null), "error.sign_in_required"));
                }

                query = query.Where(p => db.Follows.Any(f => f.FollowerId == me && f.FollowedId == p.UserId)).OrderByDescending(p => p.CreatedAt);
                break;
            case "fresh":
                query = query.OrderByDescending(p => p.CreatedAt);
                break;
            default:
                // "foryou", nothing, or a tab this version does not know.
                return Results.Json(await ForYouAsync(db, reader, query, viewerId, now, skip, take, ct), AppJson.Options);
        }

        var posts = await query.Skip(skip).Take(take + 1).ToListAsync(ct);
        return Results.Json(await PostEndpoints.PageDtoAsync(reader, posts, viewerId, skip, take, ct), AppJson.Options);
    }

    /// <summary>Scores the newest candidates in memory (see <see cref="FeedRanker"/>), then pages the ranked list with the usual offset contract.</summary>
    private static async Task<FeedDto> ForYouAsync(
        AppDbContext db, PostReader reader, IQueryable<Post> visible, Guid? viewerId, DateTime now, int skip, int take, CancellationToken ct)
    {
        var since = now - ForYouWindow;
        var candidates = await visible
            .Where(p => p.CreatedAt >= since)
            .OrderByDescending(p => p.CreatedAt)
            .Take(ForYouCandidates)
            .ToListAsync(ct);

        // Signed out there is nobody to personalise for: fire, comments, a brand's feature and age decide.
        var followed = new HashSet<Guid>();
        var interests = new HashSet<StyleIntent>();
        var checkedIntents = new HashSet<StyleIntent>();
        if (viewerId is Guid me && candidates.Count > 0)
        {
            var authorIds = candidates.Select(p => p.UserId).Distinct().ToList();
            followed = (await db.Follows.Where(f => f.FollowerId == me && authorIds.Contains(f.FollowedId)).Select(f => f.FollowedId).ToListAsync(ct)).ToHashSet();
            interests = FeedRanker.ParseInterests(await db.Users.Where(u => u.Id == me).Select(u => u.Interests).FirstOrDefaultAsync(ct));
            checkedIntents = (await db.Checks
                .Where(c => c.UserId == me && c.Status == CheckStatus.Ok && c.CreatedAt >= since)
                .Select(c => c.Intent)
                .Distinct()
                .ToListAsync(ct)).ToHashSet();
        }

        var ranked = FeedRanker.Rank(
            candidates,
            p => FeedRanker.Score(
                p.FireCount, p.CommentCount, p.CreatedAt, now,
                authorFollowed: followed.Contains(p.UserId),
                intentInInterests: interests.Contains(p.Intent),
                viewerCheckedIntent: checkedIntents.Contains(p.Intent),
                featured: p.FeaturedByBrandId is not null),
            p => p.CreatedAt);
        return await PostEndpoints.PageDtoAsync(reader, ranked.Skip(skip).Take(take + 1).ToList(), viewerId, skip, take, ct);
    }
}
