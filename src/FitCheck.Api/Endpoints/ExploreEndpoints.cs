using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>Explore: trending tags, brands to follow, top looks, open challenges; search; tag feeds. See PHASE3.md.</summary>
public static class ExploreEndpoints
{
    private static readonly TimeSpan TrendingWindow = TimeSpan.FromDays(7);
    private const int TrendingTagCount = 10;
    private const int BrandCount = 10;
    private const int TopLookCount = 6;
    private const int OpenChallengeCount = 5;
    private const int SearchResultCount = 10;
    private const int SearchMaxLength = 40;

    public static IEndpointRouteBuilder MapExploreEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/explore", ExploreAsync);
        app.MapGet("/api/search", SearchAsync);
        app.MapGet("/api/tags/{tag}/posts", TagPostsAsync);
        return app;
    }

    /// <summary>A user with the follower count that orders it, projected by the same query that picks it.</summary>
    private sealed class RankedUser
    {
        public AppUser User { get; init; } = null!;
        public int Followers { get; init; }
    }

    private static async Task<IResult> ExploreAsync(HttpContext context, AppDbContext db, PostReader reader, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var now = DateTime.UtcNow;
        var since = now - TrendingWindow;
        var recent = db.Posts.Where(p => !p.Hidden && p.CreatedAt >= since);

        var trending = await db.PostTags
            .Join(recent, t => t.PostId, p => p.Id, (t, p) => t.Tag)
            .GroupBy(t => t)
            .Select(g => new { Tag = g.Key, Posts = g.Count() })
            .OrderByDescending(x => x.Posts).ThenBy(x => x.Tag)
            .Take(TrendingTagCount)
            .ToListAsync(ct);

        var brands = await db.Users
            .Where(u => u.AccountType == AccountType.Brand)
            .Select(u => new RankedUser { User = u, Followers = db.Follows.Count(f => f.FollowedId == u.Id) })
            .OrderByDescending(x => x.Followers).ThenBy(x => x.User.HandleLower)
            .Take(BrandCount)
            .ToListAsync(ct);

        var topLooks = await recent
            .OrderByDescending(p => p.FireCount).ThenByDescending(p => p.CreatedAt)
            .Take(TopLookCount)
            .ToListAsync(ct);

        var challenges = await db.Challenges
            .Where(c => c.EndsAt > now && c.ResolvedAt == null)
            .OrderBy(c => c.EndsAt)
            .Take(OpenChallengeCount)
            .ToListAsync(ct);

        var dto = new ExploreDto(
            trending.Select(x => new TagDto(x.Tag, x.Posts)).ToList(),
            await CardsAsync(db, brands, viewerId, ct),
            await reader.ToDtosAsync(topLooks, viewerId, ct),
            await ChallengeEndpoints.ToDtosAsync(db, reader, challenges, viewerId, now, ct));
        return Results.Json(dto, AppJson.Options);
    }

    private static async Task<IResult> SearchAsync(HttpContext context, AppDbContext db, Localizer localizer, string? q, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var query = (q ?? "").Trim();
        if (query.Length is 0 or > SearchMaxLength)
        {
            return PostEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(PostEndpoints.Language(context, null), "error.search_invalid"));
        }

        // "@nexor" and "#denim" are how people write handles and tags; the marker itself is part of neither.
        var term = NormalizeTerm(query);
        if (term.Length == 0)
        {
            return Results.Json(new SearchDto([], []), AppJson.Options);
        }

        var users = await db.Users
            .Where(u => u.HandleLower.StartsWith(term) || (u.DisplayName != null && u.DisplayName.ToLower().Contains(term)))
            .Select(u => new RankedUser { User = u, Followers = db.Follows.Count(f => f.FollowedId == u.Id) })
            .OrderBy(x => x.User.AccountType == AccountType.Brand ? 0 : 1)
            .ThenByDescending(x => x.Followers)
            .ThenBy(x => x.User.HandleLower)
            .Take(SearchResultCount)
            .ToListAsync(ct);

        var tags = await db.PostTags
            .Where(t => t.Tag.StartsWith(term))
            .Join(db.Posts.Where(p => !p.Hidden), t => t.PostId, p => p.Id, (t, p) => t.Tag)
            .GroupBy(t => t)
            .Select(g => new { Tag = g.Key, Posts = g.Count() })
            .OrderByDescending(x => x.Posts).ThenBy(x => x.Tag)
            .Take(SearchResultCount)
            .ToListAsync(ct);

        var dto = new SearchDto(await CardsAsync(db, users, viewerId, ct), tags.Select(x => new TagDto(x.Tag, x.Posts)).ToList());
        return Results.Json(dto, AppJson.Options);
    }

    /// <summary>Visible posts carrying a tag, newest first. An unknown tag is an empty page, never a 404.</summary>
    private static async Task<IResult> TagPostsAsync(string tag, HttpContext context, AppDbContext db, PostReader reader, int? offset, int? limit, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var (skip, take) = PostEndpoints.Page(offset, limit);
        var name = NormalizeTag(tag);
        if (name.Length == 0)
        {
            return Results.Json(new FeedDto([], null), AppJson.Options);
        }

        var posts = await db.PostTags
            .Where(t => t.Tag == name)
            .Join(db.Posts.Where(p => !p.Hidden), t => t.PostId, p => p.Id, (t, p) => p)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(ct);
        return Results.Json(await PostEndpoints.PageDtoAsync(reader, posts, viewerId, skip, take, ct), AppJson.Options);
    }

    /// <summary>" #Denim " → "denim": the shape tags are stored in (see PostTag), so a route or search value can meet them.</summary>
    public static string NormalizeTag(string? tag)
    {
        var trimmed = (tag ?? "").Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].Trim();
        }

        return trimmed.ToLowerInvariant();
    }

    private static string NormalizeTerm(string query) =>
        (query.Length > 0 && query[0] is '#' or '@' ? query[1..] : query).Trim().ToLowerInvariant();

    /// <summary>Cards in the order given: user ref, followers, visible posts, whether the viewer follows them. Two batched lookups.</summary>
    private static async Task<List<UserCardDto>> CardsAsync(AppDbContext db, List<RankedUser> rows, Guid? viewerId, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.User.Id).ToList();
        var posts = await db.Posts
            .Where(p => ids.Contains(p.UserId) && !p.Hidden)
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);
        var following = viewerId is Guid me
            ? (await db.Follows.Where(f => f.FollowerId == me && ids.Contains(f.FollowedId)).Select(f => f.FollowedId).ToListAsync(ct)).ToHashSet()
            : new HashSet<Guid>();

        return rows
            .Select(r => new UserCardDto(PostReader.Ref(r.User), r.Followers, posts.GetValueOrDefault(r.User.Id), following.Contains(r.User.Id)))
            .ToList();
    }
}
