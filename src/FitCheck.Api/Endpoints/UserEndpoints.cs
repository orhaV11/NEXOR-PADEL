using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapPatch("/me", UpdateMeAsync).RequireAuthorization();
        group.MapDelete("/me", DeleteMeAsync).RequireAuthorization();
        group.MapGet("/me/checks", ListMyChecksAsync).RequireAuthorization();
        group.MapGet("/me/saved", ListSavedAsync).RequireAuthorization();
        group.MapGet("/{handle}", GetProfileAsync);
        group.MapGet("/{handle}/posts", ListPostsAsync);
        group.MapPost("/{handle}/follow", FollowAsync).RequireAuthorization();
        group.MapDelete("/{handle}/follow", UnfollowAsync).RequireAuthorization();

        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    public static bool IsHttpsUrl(string? url, int maxLength) =>
        url is not null && url.Length <= maxLength && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>Loads the signed-in user or answers 401 when the cookie outlived the account.</summary>
    public static async Task<(AppUser? User, IResult? Failure)> RequireUserAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([Sessions.RequiredUserId(context.User)], ct);
        if (user is not null)
        {
            return (user, null);
        }

        await Sessions.SignOutAsync(context);
        return (null, Error(StatusCodes.Status401Unauthorized, localizer.Get(Localizer.Resolve(null, context.Request), "error.sign_in_required")));
    }

    private static async Task<IResult> UpdateMeAsync(
        UpdateMeRequest body, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (user, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        if (body.Language is not null)
        {
            if (!Localizer.TryMatch(body.Language, out var language))
            {
                return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.language_invalid"));
            }

            user.PreferredLanguage = language;
        }

        if (body.DisplayName is not null)
        {
            var name = OutfitAnalyzer.SanitizeText(body.DisplayName, multiline: false);
            if (name.Length > 40)
            {
                return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.profile_invalid"));
            }

            user.DisplayName = name.Length == 0 ? null : name;
        }

        if (body.Bio is not null)
        {
            var bio = OutfitAnalyzer.SanitizeText(body.Bio, multiline: true);
            if (bio.Length > 160)
            {
                return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.profile_invalid"));
            }

            user.Bio = bio.Length == 0 ? null : bio;
        }

        if (body.Website is not null)
        {
            var website = body.Website.Trim();
            if (website.Length > 0 && !IsHttpsUrl(website, 200))
            {
                return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.website_invalid"));
            }

            user.Website = website.Length == 0 ? null : website;
        }

        await db.SaveChangesAsync(ct);
        return Results.Json(await AuthEndpoints.ToMeAsync(db, user, ct), AppJson.Options);
    }

    /// <summary>Removes the account and everything it touched. No soft delete, no recovery.</summary>
    private static async Task<IResult> DeleteMeAsync(
        HttpContext context, AppDbContext db, IImageStore images, Localizer localizer, CancellationToken ct)
    {
        var (user, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var id = user.Id;
        // Files first: if a row delete fails the user can retry, but an orphaned photo would have no owner to delete it.
        images.DeleteUser(id);

        // All rows go or none do: a failure half-way must not leave counters decremented twice on a retry.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var myPostIds = await db.Posts.Where(p => p.UserId == id).Select(p => p.Id).ToListAsync(ct);
        var myChallengeIds = await db.Challenges.Where(c => c.BrandId == id).Select(c => c.Id).ToListAsync(ct);

        // Reactions this user gave to other people's posts come off their counters.
        var firedPosts = await db.Fires.Where(f => f.UserId == id && !myPostIds.Contains(f.PostId)).Select(f => f.PostId).ToListAsync(ct);
        foreach (var postId in firedPosts)
        {
            await db.Posts.Where(p => p.Id == postId && p.FireCount > 0).ExecuteUpdateAsync(s => s.SetProperty(p => p.FireCount, p => p.FireCount - 1), ct);
        }

        var commentedPosts = await db.Comments.Where(c => c.UserId == id && !c.Hidden && !myPostIds.Contains(c.PostId)).GroupBy(c => c.PostId)
            .Select(g => new { PostId = g.Key, Count = g.Count() }).ToListAsync(ct);
        foreach (var entry in commentedPosts)
        {
            await db.Posts.Where(p => p.Id == entry.PostId).ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => Math.Max(0, p.CommentCount - entry.Count)), ct);
        }

        await db.Fires.Where(f => f.UserId == id || myPostIds.Contains(f.PostId)).ExecuteDeleteAsync(ct);
        await db.Reports.Where(r => r.ReporterId == id || (r.PostId != null && myPostIds.Contains(r.PostId.Value))).ExecuteDeleteAsync(ct);
        await db.Comments.Where(c => c.UserId == id || myPostIds.Contains(c.PostId)).ExecuteDeleteAsync(ct);
        await db.ChallengeVotes.Where(v => v.UserId == id || myPostIds.Contains(v.PostId)).ExecuteDeleteAsync(ct);
        await db.SavedPosts.Where(s => s.UserId == id || myPostIds.Contains(s.PostId)).ExecuteDeleteAsync(ct);
        await db.ProductLinks.Where(l => myPostIds.Contains(l.PostId)).ExecuteDeleteAsync(ct);
        // Own notifications, plus everyone else's that point at a post or challenge about to disappear.
        await db.Notifications.Where(n => n.UserId == id
                || (n.PostId != null && myPostIds.Contains(n.PostId.Value))
                || (n.ChallengeId != null && myChallengeIds.Contains(n.ChallengeId.Value)))
            .ExecuteDeleteAsync(ct);
        await db.Follows.Where(f => f.FollowerId == id || f.FollowedId == id).ExecuteDeleteAsync(ct);
        await db.Challenges.Where(c => c.WinnerPostId != null && myPostIds.Contains(c.WinnerPostId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.WinnerPostId, (Guid?)null), ct);

        // Challenges this brand opened disappear; other people's entries stay as plain posts.
        if (myChallengeIds.Count > 0)
        {
            await db.Posts.Where(p => p.ChallengeId != null && myChallengeIds.Contains(p.ChallengeId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ChallengeId, (Guid?)null), ct);
            await db.ChallengeVotes.Where(v => myChallengeIds.Contains(v.ChallengeId)).ExecuteDeleteAsync(ct);
            await db.Challenges.Where(c => c.BrandId == id).ExecuteDeleteAsync(ct);
        }

        await db.Posts.Where(p => p.UserId == id).ExecuteDeleteAsync(ct);
        await db.Checks.Where(c => c.UserId == id).ExecuteDeleteAsync(ct);
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await Sessions.SignOutAsync(context);
        return Results.NoContent();
    }

    private static async Task<IResult> ListMyChecksAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (user, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var checks = await db.Checks
            .Where(c => c.UserId == user.Id)
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        var checkIds = checks.Select(c => c.Id).ToList();
        var posts = await db.Posts.Where(p => checkIds.Contains(p.CheckId)).ToDictionaryAsync(p => p.CheckId, p => p.Id, ct);

        return Results.Json(checks.Select(c => CheckDto.FromEntity(c, localizer, posts.TryGetValue(c.Id, out var postId) ? postId : null)).ToList(), AppJson.Options);
    }

    private static async Task<IResult> ListSavedAsync(
        HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, int? offset, int? limit, CancellationToken ct)
    {
        var (user, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var (skip, take) = PostEndpoints.Page(offset, limit);
        var posts = await db.SavedPosts
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt)
            .Join(db.Posts.Where(p => !p.Hidden), s => s.PostId, p => p.Id, (s, p) => new { s.CreatedAt, Post = p })
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Post)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(ct);

        return Results.Json(await PostEndpoints.PageDtoAsync(reader, posts, user.Id, skip, take, ct), AppJson.Options);
    }

    private static async Task<IResult> GetProfileAsync(string handle, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var lower = handle.ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.HandleLower == lower, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, context.Request), "error.user_not_found"));
        }

        var viewerId = Sessions.UserId(context.User);
        var visible = db.Posts.Where(p => p.UserId == user.Id && !p.Hidden);
        var profile = new ProfileDto(
            user.Handle,
            user.Name,
            user.AccountType.ToString(),
            user.Bio,
            user.Website,
            Posts: await visible.CountAsync(ct),
            Followers: await db.Follows.CountAsync(f => f.FollowedId == user.Id, ct),
            Following: await db.Follows.CountAsync(f => f.FollowerId == user.Id, ct),
            FireReceived: await visible.SumAsync(p => p.FireCount, ct),
            BestScore: await visible.MaxAsync(p => (int?)p.Score, ct),
            Streak: user.StreakCount,
            CreatedAt: DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc),
            Viewer: new ViewerProfileDto(
                IsMe: viewerId == user.Id,
                Following: viewerId is Guid v && await db.Follows.AnyAsync(f => f.FollowerId == v && f.FollowedId == user.Id, ct)));

        return Results.Json(profile, AppJson.Options);
    }

    private static async Task<IResult> ListPostsAsync(
        string handle, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, int? offset, int? limit, CancellationToken ct)
    {
        var lower = handle.ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.HandleLower == lower, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, context.Request), "error.user_not_found"));
        }

        var viewerId = Sessions.UserId(context.User);
        var (skip, take) = PostEndpoints.Page(offset, limit);
        // Owners see their own hidden posts, marked, so they know a post is under review.
        var query = db.Posts.Where(p => p.UserId == user.Id);
        if (viewerId != user.Id)
        {
            query = query.Where(p => !p.Hidden);
        }

        var posts = await query.OrderByDescending(p => p.CreatedAt).Skip(skip).Take(take + 1).ToListAsync(ct);
        return Results.Json(await PostEndpoints.PageDtoAsync(reader, posts, viewerId, skip, take, ct), AppJson.Options);
    }

    private static async Task<IResult> FollowAsync(
        string handle, HttpContext context, AppDbContext db, Notifier notifier, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lower = handle.ToLowerInvariant();
        var target = await db.Users.FirstOrDefaultAsync(u => u.HandleLower == lower, ct);
        if (target is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.user_not_found"));
        }

        if (target.Id == me.Id)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.follow_self"));
        }

        if (!await db.Follows.AnyAsync(f => f.FollowerId == me.Id && f.FollowedId == target.Id, ct))
        {
            db.Follows.Add(new Follow { FollowerId = me.Id, FollowedId = target.Id, CreatedAt = DateTime.UtcNow });
            await notifier.AddOnceAsync(target.Id, NotificationType.Follow, me.Handle, null, null, ct);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two taps raced; the primary key kept one row, which is the state the caller asked for.
            }
        }

        return Results.Json(new FollowStateDto(await db.Follows.CountAsync(f => f.FollowedId == target.Id, ct), true), AppJson.Options);
    }

    private static async Task<IResult> UnfollowAsync(
        string handle, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lower = handle.ToLowerInvariant();
        var target = await db.Users.FirstOrDefaultAsync(u => u.HandleLower == lower, ct);
        if (target is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.user_not_found"));
        }

        await db.Follows.Where(f => f.FollowerId == me.Id && f.FollowedId == target.Id).ExecuteDeleteAsync(ct);
        return Results.Json(new FollowStateDto(await db.Follows.CountAsync(f => f.FollowedId == target.Id, ct), false), AppJson.Options);
    }
}
