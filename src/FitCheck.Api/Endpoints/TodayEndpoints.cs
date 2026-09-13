using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// The daily prompt and today's looks: the prompt the UTC day lands on (<see cref="DailyPrompts"/>), in the caller's
/// language, with the visible looks posted today under its hashtag, newest first, and whether the caller is among
/// them. Public like the feed; signed out there is simply nobody to have posted.
/// </summary>
public static class TodayEndpoints
{
    /// <summary>The most looks one day answers with: the strip shows eight, the page a grid; a pilot day never fills this.</summary>
    public const int MaxPosts = 60;

    public static IEndpointRouteBuilder MapTodayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/today", GetAsync);
        return app;
    }

    private static async Task<IResult> GetAsync(HttpContext context, AppDbContext db, PostReader reader, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var viewer = viewerId is Guid id ? await db.Users.FindAsync([id], ct) : null;
        var language = PostEndpoints.Language(context, viewer);
        var now = DateTime.UtcNow;
        var prompt = DailyPrompts.For(now);
        var since = DailyPrompts.DayOf(now);

        var posts = await db.PostTags
            .Where(t => t.Tag == prompt.Tag)
            .Join(db.Posts.Where(p => !p.Hidden && p.CreatedAt >= since), t => t.PostId, p => p.Id, (t, p) => p)
            .OrderByDescending(p => p.CreatedAt)
            .Take(MaxPosts)
            .ToListAsync(ct);

        // Posted counts the caller's own look even while it is under review: they did post, the strip should not nag.
        var posted = viewerId is Guid me && await db.PostTags
            .Where(t => t.Tag == prompt.Tag)
            .Join(db.Posts.Where(p => p.UserId == me && p.CreatedAt >= since), t => t.PostId, p => p.Id, (t, p) => p.Id)
            .AnyAsync(ct);

        var dto = new TodayDto(prompt.Tag, prompt.Title(language), prompt.Hint(language), prompt.Intent, since, await reader.ToDtosAsync(posts, viewerId, ct), posted);
        return Results.Json(dto, AppJson.Options);
    }
}
