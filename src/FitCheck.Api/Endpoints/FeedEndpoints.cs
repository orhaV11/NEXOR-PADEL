using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>The home feed: "for you" (ranked), "following", "top" (most fire in 7 days) and "fresh" (newest).</summary>
public static class FeedEndpoints
{
    private static readonly TimeSpan TopWindow = TimeSpan.FromDays(7);

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

        switch ((tab ?? "fresh").ToLowerInvariant())
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
            default:
                query = query.OrderByDescending(p => p.CreatedAt);
                break;
        }

        var posts = await query.Skip(skip).Take(take + 1).ToListAsync(ct);
        return Results.Json(await PostEndpoints.PageDtoAsync(reader, posts, viewerId, skip, take, ct), AppJson.Options);
    }

}
