using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// The moderation queue, for the accounts with <see cref="AppUser.IsAdmin"/> set (by the Admin:Handles sync at start or the
/// --admin command, see Data/AdminSync.cs): reported looks and comments, hide/show/delete, and account suspension. Every
/// route sits behind <see cref="GateAsync"/>, so a signed-in non-admin gets 403 before anything is looked up, on GET as much
/// as on POST. The gate reads the flag off the row it loads, never the handle in the cookie: a handle is something anyone
/// can register once it is free.
///
/// Suspension is one flag on the account plus the same Hidden flag reports use: on suspend every look and comment of the
/// account goes hidden, on lift the ones the community did not hide on its own (fewer than Limits:ReportsToHide reports)
/// come back. That way no feed, tag, Explore, challenge or profile-grid query needs an account check; only the single-post
/// door (<see cref="PostEndpoints.VisiblePostAsync(AppDbContext, Guid, HttpContext, CancellationToken)"/>) and the profile
/// routes look at the account. The cost is one imprecision: a look a moderator hid by hand with fewer reports than the
/// threshold comes back with the lift, and shows in the queue again if it still has reports.
/// </summary>
public static class AdminEndpoints
{
    public const int MaxQueue = 100;
    public const int MaxReasons = 5;
    public const int MaxUserMatches = 20;

    /// <summary>Where the gate leaves the moderator's row for the handler, so it is loaded once per request.</summary>
    private const string AdminItem = "orevosh.admin";

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization().AddEndpointFilter(GateAsync);
        group.MapGet("/queue", QueueAsync);
        group.MapGet("/users", SearchUsersAsync);
        group.MapPost("/posts/{id:guid}/hide", HidePostAsync);
        group.MapPost("/posts/{id:guid}/unhide", UnhidePostAsync);
        group.MapDelete("/posts/{id:guid}", DeletePostAsync);
        group.MapPost("/comments/{id:guid}/hide", HideCommentAsync);
        group.MapPost("/comments/{id:guid}/unhide", UnhideCommentAsync);
        group.MapDelete("/comments/{id:guid}", DeleteCommentAsync);
        group.MapPost("/users/{handle}/suspend", SuspendAsync);
        group.MapPost("/users/{handle}/unsuspend", UnsuspendAsync);
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    /// <summary>
    /// Whether the request's cookie belongs to a moderator who could pass the gate: the row's flag, looked up now. One indexed
    /// read, and only the callers that have a hidden look in hand pay for it (<see cref="PostEndpoints.VisiblePostAsync(AppDbContext, Guid, HttpContext, CancellationToken)"/>).
    /// </summary>
    public static async Task<bool> IsAdminViewerAsync(HttpContext context, AppDbContext db, CancellationToken ct)
    {
        return Sessions.UserId(context.User) is Guid viewerId && await db.Users.AnyAsync(u => u.Id == viewerId && u.IsAdmin && !u.Suspended, ct);
    }

    /// <summary>The gate: the signed-in account (401 gone, 403 suspended, as everywhere) must carry the IsAdmin flag (403 otherwise).</summary>
    private static async ValueTask<object?> GateAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var services = context.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var localizer = services.GetRequiredService<Localizer>();
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, context.RequestAborted);
        if (user is null)
        {
            return failure;
        }

        if (!user.IsAdmin)
        {
            return Error(StatusCodes.Status403Forbidden, localizer.Get(user.PreferredLanguage, "error.admin_only"));
        }

        context.Items[AdminItem] = user;
        return await next(invocation);
    }

    private static AppUser Admin(HttpContext context) =>
        context.Items[AdminItem] as AppUser ?? throw new InvalidOperationException("Admin route reached without the gate.");

    // ---- the queue ----

    private static async Task<IResult> QueueAsync(HttpContext context, AppDbContext db, PostReader reader, CancellationToken ct)
    {
        var admin = Admin(context);

        // Newest report first. The count on the row decides membership (it survives a reporter deleting their account, when
        // the report rows go); the report rows decide the order and carry the reasons.
        var posts = await db.Posts
            .Where(p => p.ReportCount > 0)
            .OrderByDescending(p => db.Reports.Where(r => r.PostId == p.Id).Max(r => (DateTime?)r.CreatedAt) ?? p.CreatedAt)
            .Take(MaxQueue)
            .ToListAsync(ct);
        var comments = await db.Comments
            .Where(c => c.ReportCount > 0)
            .OrderByDescending(c => db.Reports.Where(r => r.CommentId == c.Id).Max(r => (DateTime?)r.CreatedAt) ?? c.CreatedAt)
            .Take(MaxQueue)
            .ToListAsync(ct);

        var items = (await ItemsAsync(db, reader, admin, posts, comments, ct)).Take(MaxQueue).ToList();
        var dto = new AdminQueueDto(
            items,
            HiddenPosts: await db.Posts.CountAsync(p => p.Hidden, ct),
            HiddenComments: await db.Comments.CountAsync(c => c.Hidden, ct),
            SuspendedUsers: await db.Users.CountAsync(u => u.Suspended, ct));
        return Results.Json(dto, AppJson.Options);
    }

    /// <summary>
    /// Queue items for these looks and comments, newest report first: the reasons people gave (distinct, non-empty, the
    /// latest five) and the last report from the rows, the DTOs as the moderator sees them, the authors and whether they
    /// are suspended. A few batched lookups, never one per item.
    /// </summary>
    private static async Task<List<AdminReportDto>> ItemsAsync(
        AppDbContext db, PostReader reader, AppUser admin, IReadOnlyList<Post> posts, IReadOnlyList<Comment> comments, CancellationToken ct)
    {
        var postIds = posts.Select(p => p.Id).ToList();
        var commentIds = comments.Select(c => c.Id).ToList();
        var reports = await db.Reports
            .Where(r => (r.PostId != null && postIds.Contains(r.PostId.Value)) || (r.CommentId != null && commentIds.Contains(r.CommentId.Value)))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new { r.PostId, r.CommentId, r.Reason, r.CreatedAt })
            .ToListAsync(ct);
        var byTarget = reports.GroupBy(r => r.PostId ?? r.CommentId ?? Guid.Empty).ToDictionary(g => g.Key, g => g.ToList());

        var authorIds = posts.Select(p => p.UserId).Concat(comments.Select(c => c.UserId)).Distinct().ToList();
        var authors = await reader.RefsAsync(authorIds, ct);
        var suspended = (await db.Users.Where(u => authorIds.Contains(u.Id) && u.Suspended).Select(u => u.Id).ToListAsync(ct)).ToHashSet();
        var postDtos = (await reader.ToDtosAsync(posts, admin.Id, ct)).ToDictionary(p => p.Id);

        List<string> Reasons(Guid target) => byTarget.TryGetValue(target, out var rows)
            ? rows.Select(r => r.Reason.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxReasons).ToList()
            : [];
        DateTime Last(Guid target, DateTime fallback) =>
            DateTime.SpecifyKind(byTarget.TryGetValue(target, out var rows) ? rows[0].CreatedAt : fallback, DateTimeKind.Utc);

        var items = new List<AdminReportDto>(posts.Count + comments.Count);
        foreach (var post in posts)
        {
            items.Add(new AdminReportDto("post", post.Id, post.ReportCount, post.Hidden, Reasons(post.Id), Last(post.Id, post.CreatedAt),
                postDtos.GetValueOrDefault(post.Id), null, authors.GetValueOrDefault(post.UserId), suspended.Contains(post.UserId)));
        }

        foreach (var comment in comments)
        {
            var author = authors.GetValueOrDefault(comment.UserId) ?? new UserRefDto("?", "?", AccountType.Person.ToString());
            var dto = new CommentDto(comment.Id, author, comment.Text, comment.UserId == admin.Id, true, DateTime.SpecifyKind(comment.CreatedAt, DateTimeKind.Utc));
            items.Add(new AdminReportDto("comment", comment.Id, comment.ReportCount, comment.Hidden, Reasons(comment.Id), Last(comment.Id, comment.CreatedAt),
                null, dto, authors.GetValueOrDefault(comment.UserId), suspended.Contains(comment.UserId)));
        }

        return items.OrderByDescending(i => i.LastReportedAt).ToList();
    }

    private static async Task<IResult> PostItemAsync(AppDbContext db, PostReader reader, AppUser admin, Post post, CancellationToken ct) =>
        Results.Json((await ItemsAsync(db, reader, admin, [post], [], ct))[0], AppJson.Options);

    private static async Task<IResult> CommentItemAsync(AppDbContext db, PostReader reader, AppUser admin, Comment comment, CancellationToken ct) =>
        Results.Json((await ItemsAsync(db, reader, admin, [], [comment], ct))[0], AppJson.Options);

    // ---- looks ----

    private static async Task<IResult> HidePostAsync(Guid id, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var post = await db.Posts.FindAsync([id], ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.post_not_found"));
        }

        post.Hidden = true;
        await db.SaveChangesAsync(ct);
        return await PostItemAsync(db, reader, admin, post, ct);
    }

    private static async Task<IResult> UnhidePostAsync(Guid id, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var post = await db.Posts.FindAsync([id], ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.post_not_found"));
        }

        // Showing a look again also forgets its reports: the count goes back to zero and the report rows go, so the same
        // people can report it again if it recurs (one report per person per target is a unique index, so without this
        // their next tap would be a silent no-op). A suspended author's look stays hidden; the lift brings it back.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Reports.Where(r => r.PostId == id).ExecuteDeleteAsync(ct);
        post.ReportCount = 0;
        post.Hidden = await db.Users.AnyAsync(u => u.Id == post.UserId && u.Suspended, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await PostItemAsync(db, reader, admin, post, ct);
    }

    private static async Task<IResult> DeletePostAsync(
        Guid id, HttpContext context, AppDbContext db, IImageStore images, ILoggerFactory loggerFactory, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var post = await db.Posts.FindAsync([id], ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.post_not_found"));
        }

        // The check goes too, photo and clip included: a look a moderator removed must not be one tap from being posted again.
        await PostEndpoints.RemovePostAsync(db, post, images, loggerFactory.CreateLogger(nameof(AdminEndpoints)), purgeCheck: true, ct);
        return Results.NoContent();
    }

    // ---- comments ----

    private static async Task<IResult> HideCommentAsync(Guid id, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var comment = await db.Comments.FindAsync([id], ct);
        if (comment is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.comment_not_found"));
        }

        await SetCommentHiddenAsync(db, comment, hidden: true, ct);
        return await CommentItemAsync(db, reader, admin, comment, ct);
    }

    private static async Task<IResult> UnhideCommentAsync(Guid id, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var comment = await db.Comments.FindAsync([id], ct);
        if (comment is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.comment_not_found"));
        }

        // Same rule as a look: the reports are forgotten so they can be made again, and a suspended author's comment waits for the lift.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Reports.Where(r => r.CommentId == id).ExecuteDeleteAsync(ct);
        comment.ReportCount = 0;
        await db.SaveChangesAsync(ct);
        var authorSuspended = await db.Users.AnyAsync(u => u.Id == comment.UserId && u.Suspended, ct);
        await SetCommentHiddenAsync(db, comment, hidden: authorSuspended, ct);
        await tx.CommitAsync(ct);
        return await CommentItemAsync(db, reader, admin, comment, ct);
    }

    /// <summary>
    /// Flips a comment's Hidden flag and moves its post's comment count with it, the way the report threshold does: a
    /// hidden comment is not counted. Set-based on the flag, so two moderators flipping at once move the count once.
    /// </summary>
    private static async Task SetCommentHiddenAsync(AppDbContext db, Comment comment, bool hidden, CancellationToken ct)
    {
        var flipped = await db.Comments.Where(c => c.Id == comment.Id && c.Hidden != hidden)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Hidden, hidden), ct);
        comment.Hidden = hidden;
        if (flipped == 0)
        {
            return;
        }

        if (hidden)
        {
            await db.Posts.Where(p => p.Id == comment.PostId && p.CommentCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => p.CommentCount - 1), ct);
        }
        else
        {
            await db.Posts.Where(p => p.Id == comment.PostId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => p.CommentCount + 1), ct);
        }
    }

    private static async Task<IResult> DeleteCommentAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var comment = await db.Comments.FindAsync([id], ct);
        if (comment is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.comment_not_found"));
        }

        await PostEndpoints.RemoveCommentAsync(db, comment, ct);
        return Results.NoContent();
    }

    // ---- accounts ----

    /// <summary>Handle prefix matches; with no term, the suspended accounts, so a suspension can be lifted without remembering the handle.</summary>
    private static async Task<IResult> SearchUsersAsync(HttpContext context, AppDbContext db, string? q, CancellationToken ct)
    {
        var term = (q ?? "").Trim().TrimStart('@').ToLowerInvariant();
        if (term.Length > 40)
        {
            return Results.Json(new List<AdminUserDto>(), AppJson.Options);
        }

        var query = term.Length == 0 ? db.Users.Where(u => u.Suspended) : db.Users.Where(u => u.HandleLower.StartsWith(term));
        var users = await query.OrderBy(u => u.HandleLower).Take(MaxUserMatches).ToListAsync(ct);
        return Results.Json(await UserDtosAsync(db, users, ct), AppJson.Options);
    }

    private static async Task<IResult> SuspendAsync(string handle, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var user = await UserEndpoints.FindByHandleAsync(db, handle, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.user_not_found"));
        }

        // Moderators are not for each other to switch off, and not themselves either (the gate makes the caller one): that is
        // the --unadmin command on the box, not a tap.
        if (user.IsAdmin)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(admin.PreferredLanguage, "error.admin_protected"));
        }

        if (!user.Suspended)
        {
            var now = DateTime.UtcNow;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            user.Suspended = true;
            // Every look goes under the same flag reports use, so no list query anywhere needs an account check.
            await db.Posts.Where(p => p.UserId == user.Id && !p.Hidden).ExecuteUpdateAsync(s => s.SetProperty(p => p.Hidden, true), ct);
            // A brand's open challenges close now, with no winner and no notifications: the hashtag stops taking entries and
            // nobody is crowned by an account that is locked out. The lift does not reopen them; the brand opens a new one.
            await db.Challenges.Where(c => c.BrandId == user.Id && c.ResolvedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ResolvedAt, now).SetProperty(c => c.WinnerPostId, (Guid?)null), ct);
            // Comments too, and a comment that goes hidden leaves its post's count, as a reported one does.
            var visible = await db.Comments.Where(c => c.UserId == user.Id && !c.Hidden)
                .GroupBy(c => c.PostId).Select(g => new { PostId = g.Key, Count = g.Count() }).ToListAsync(ct);
            foreach (var entry in visible)
            {
                await db.Posts.Where(p => p.Id == entry.PostId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => Math.Max(0, p.CommentCount - entry.Count)), ct);
            }

            await db.Comments.Where(c => c.UserId == user.Id && !c.Hidden).ExecuteUpdateAsync(s => s.SetProperty(c => c.Hidden, true), ct);
            // Sessions are cookies, refused on the next request by RequireUserAsync. Pushes stop here: the browser subscribes
            // again if the account comes back and opts in again.
            await db.PushSubscriptions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return Results.Json((await UserDtosAsync(db, [user], ct))[0], AppJson.Options);
    }

    private static async Task<IResult> UnsuspendAsync(
        string handle, HttpContext context, AppDbContext db, IOptions<LimitsOptions> limits, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var user = await UserEndpoints.FindByHandleAsync(db, handle, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.user_not_found"));
        }

        if (user.Suspended)
        {
            var threshold = limits.Value.ReportsToHide;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            user.Suspended = false;
            // Back up, except what the community hid on its own: those wait for the queue.
            await db.Posts.Where(p => p.UserId == user.Id && p.Hidden && p.ReportCount < threshold)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Hidden, false), ct);
            var hidden = await db.Comments.Where(c => c.UserId == user.Id && c.Hidden && c.ReportCount < threshold)
                .GroupBy(c => c.PostId).Select(g => new { PostId = g.Key, Count = g.Count() }).ToListAsync(ct);
            foreach (var entry in hidden)
            {
                await db.Posts.Where(p => p.Id == entry.PostId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => p.CommentCount + entry.Count), ct);
            }

            await db.Comments.Where(c => c.UserId == user.Id && c.Hidden && c.ReportCount < threshold)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Hidden, false), ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return Results.Json((await UserDtosAsync(db, [user], ct))[0], AppJson.Options);
    }

    /// <summary>Accounts as the moderator sees them: every look counted, hidden ones included, and the reports against their looks and comments.</summary>
    private static async Task<List<AdminUserDto>> UserDtosAsync(AppDbContext db, IReadOnlyList<AppUser> users, CancellationToken ct)
    {
        if (users.Count == 0)
        {
            return [];
        }

        var ids = users.Select(u => u.Id).ToList();
        var posts = await db.Posts.Where(p => ids.Contains(p.UserId))
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count(), Reports = g.Sum(p => p.ReportCount) })
            .ToDictionaryAsync(x => x.UserId, ct);
        var comments = await db.Comments.Where(c => ids.Contains(c.UserId) && c.ReportCount > 0)
            .GroupBy(c => c.UserId)
            .Select(g => new { UserId = g.Key, Reports = g.Sum(c => c.ReportCount) })
            .ToDictionaryAsync(x => x.UserId, ct);

        return users.Select(u =>
        {
            posts.TryGetValue(u.Id, out var p);
            comments.TryGetValue(u.Id, out var c);
            return new AdminUserDto(PostReader.Ref(u), u.Suspended, p?.Count ?? 0, (p?.Reports ?? 0) + (c?.Reports ?? 0), DateTime.SpecifyKind(u.CreatedAt, DateTimeKind.Utc));
        }).ToList();
    }
}
