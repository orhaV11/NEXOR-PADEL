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
///
/// Round 20 - owner tooling without a terminal: the account actions the --verify and --pro commands did from the box
/// (verify/unverify, Pro for N months/back to Free), keeping an account off every board, and a read of the sponsor
/// settings, all behind the same gate, each write one audit line naming the moderator and one shared per-moderator brake
/// (Limits:AdminActionsPerHour). The commands stay as the terminal fallback.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Round 20: the rate-limit policy on the account actions (verify, Pro, the board), Limits:AdminActionsPerHour.</summary>
    public const string ActionsPolicy = "admin-actions";

    /// <summary>Round 20: the most months one Pro grant may run.</summary>
    public const int MaxProMonths = 120;

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
        // The account actions share one per-moderator brake (Limits:AdminActionsPerHour) and each writes one audit line: a
        // stolen moderator cookie or a script is slowed and named, a hand never notices.
        group.MapPost("/users/{handle}/suspend", SuspendAsync).RequireRateLimiting(ActionsPolicy);
        group.MapPost("/users/{handle}/unsuspend", UnsuspendAsync).RequireRateLimiting(ActionsPolicy);
        // Round 20 - owner tooling without a terminal: what --verify and --pro do, from #/admin, plus keeping an account
        // off the board and a read of the sponsor settings.
        group.MapPost("/users/{handle}/verify", VerifyAsync).RequireRateLimiting(ActionsPolicy);
        group.MapPost("/users/{handle}/unverify", UnverifyAsync).RequireRateLimiting(ActionsPolicy);
        group.MapPost("/users/{handle}/pro", GrantProAsync).RequireRateLimiting(ActionsPolicy);
        group.MapDelete("/users/{handle}/pro", RemoveProAsync).RequireRateLimiting(ActionsPolicy);
        group.MapPost("/users/{handle}/board-exclusion", ExcludeAccountAsync).RequireRateLimiting(ActionsPolicy);
        group.MapDelete("/users/{handle}/board-exclusion", IncludeAccountAsync).RequireRateLimiting(ActionsPolicy);
        group.MapGet("/sponsor", SponsorAsync);
        // Round 16 - the affiliate line: what left for a shop, and what a partner says it earned.
        group.MapGet("/affiliate", AffiliateAsync);
        group.MapPost("/affiliate/commissions", ImportCommissionsAsync);
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
    public static async ValueTask<object?> GateAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
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

    public static AppUser Admin(HttpContext context) =>
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
        // Round 14 — post the look, keep the grade: the queue is the one place a look's number is read by someone who is
        // not its author. A moderator judges what was reported, and a look whose grade is private is still judged whole.
        var postDtos = (await reader.ToDtosAsync(posts, admin.Id, ct, viewerIsModerator: true)).ToDictionary(p => p.Id);

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
    private static async Task<IResult> SearchUsersAsync(HttpContext context, AppDbContext db, IClock clock, string? q, CancellationToken ct)
    {
        var term = (q ?? "").Trim().TrimStart('@').ToLowerInvariant();
        if (term.Length > 40)
        {
            return Results.Json(new List<AdminUserDto>(), AppJson.Options);
        }

        var query = term.Length == 0 ? db.Users.Where(u => u.Suspended) : db.Users.Where(u => u.HandleLower.StartsWith(term));
        var users = await query.OrderBy(u => u.HandleLower).Take(MaxUserMatches).ToListAsync(ct);
        return Results.Json(await UserDtosAsync(db, users, clock, ct), AppJson.Options);
    }

    private static async Task<IResult> SuspendAsync(
        string handle, HttpContext context, AppDbContext db, IClock clock, ILoggerFactory loggerFactory, Localizer localizer, CancellationToken ct)
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
            Audit(loggerFactory).LogInformation("Admin: {Handle} suspended by {Moderator}", user.Handle, admin.Handle);
        }

        return await RowAsync(db, user, clock, ct);
    }

    private static async Task<IResult> UnsuspendAsync(
        string handle, HttpContext context, AppDbContext db, IOptions<LimitsOptions> limits, IClock clock, ILoggerFactory loggerFactory,
        Localizer localizer, CancellationToken ct)
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
            Audit(loggerFactory).LogInformation("Admin: {Handle} suspension lifted by {Moderator}", user.Handle, admin.Handle);
        }

        return await RowAsync(db, user, clock, ct);
    }

    // ---- Round 20: the account actions ----

    /// <summary>
    /// The audit logger for the account actions: one Information line per write, naming the target and the moderator by
    /// handle (as Billing's lines do), never an email or a cookie. The line is the record; the row only carries the flag.
    /// </summary>
    private static ILogger Audit(ILoggerFactory loggerFactory) => loggerFactory.CreateLogger(typeof(AdminEndpoints).FullName!);

    /// <summary>The refreshed account row every write answers, so the screen re-renders from the server's word.</summary>
    private static async Task<IResult> RowAsync(AppDbContext db, AppUser user, IClock clock, CancellationToken ct) =>
        Results.Json((await UserDtosAsync(db, [user], clock, ct))[0], AppJson.Options);

    /// <summary>The verified mark, what --verify sets. Idempotent, and allowed on anyone including yourself: verification is not punitive.</summary>
    private static Task<IResult> VerifyAsync(string handle, HttpContext context, AppDbContext db, IClock clock, ILoggerFactory loggerFactory, Localizer localizer, CancellationToken ct) =>
        SetVerifiedAsync(handle, verified: true, context, db, clock, loggerFactory, localizer, ct);

    private static Task<IResult> UnverifyAsync(string handle, HttpContext context, AppDbContext db, IClock clock, ILoggerFactory loggerFactory, Localizer localizer, CancellationToken ct) =>
        SetVerifiedAsync(handle, verified: false, context, db, clock, loggerFactory, localizer, ct);

    private static async Task<IResult> SetVerifiedAsync(
        string handle, bool verified, HttpContext context, AppDbContext db, IClock clock, ILoggerFactory loggerFactory, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var user = await UserEndpoints.FindByHandleAsync(db, handle, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.user_not_found"));
        }

        if (user.Verified != verified)
        {
            user.Verified = verified;
            await db.SaveChangesAsync(ct);
            if (verified)
            {
                Audit(loggerFactory).LogInformation("Admin: {Handle} verified by {Moderator}", user.Handle, admin.Handle);
            }
            else
            {
                Audit(loggerFactory).LogInformation("Admin: {Handle} verification removed by {Moderator}", user.Handle, admin.Handle);
            }
        }

        return await RowAsync(db, user, clock, ct);
    }

    /// <summary>
    /// Pro for a number of months, the --pro command's arithmetic (31 days a month from now). Refused for an account that
    /// pays through Stripe: the next invoice or subscription webhook would rewrite the plan under the moderator's hand, and
    /// after a "Remove Pro" the person would keep paying for a plan they no longer have. Their plan is changed in Stripe.
    /// </summary>
    private static async Task<IResult> GrantProAsync(
        string handle, ProGrantRequest? body, HttpContext context, AppDbContext db, IClock clock, ILoggerFactory loggerFactory, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var user = await UserEndpoints.FindByHandleAsync(db, handle, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.user_not_found"));
        }

        var months = body?.Months ?? 0;
        if (months < 1 || months > MaxProMonths)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(admin.PreferredLanguage, "error.pro_months", MaxProMonths));
        }

        if (!string.IsNullOrWhiteSpace(user.BillingSubscriptionId))
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(admin.PreferredLanguage, "error.pro_billing"));
        }

        var until = DateTime.SpecifyKind(clock.UtcNow.AddDays(31.0 * months), DateTimeKind.Utc);
        user.Plan = Plans.Pro;
        user.ProUntil = until;
        await db.SaveChangesAsync(ct);
        Audit(loggerFactory).LogInformation("Admin: {Handle} on Pro until {Until:yyyy-MM-dd} by {Moderator} ({Months} months)", user.Handle, until, admin.Handle, months);
        return await RowAsync(db, user, clock, ct);
    }

    /// <summary>Back to Free now. Answers the row even when the account was already free, as lifting a suspension that is not there does.</summary>
    private static async Task<IResult> RemoveProAsync(
        string handle, HttpContext context, AppDbContext db, IClock clock, ILoggerFactory loggerFactory, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var user = await UserEndpoints.FindByHandleAsync(db, handle, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.user_not_found"));
        }

        if (!string.IsNullOrWhiteSpace(user.BillingSubscriptionId))
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(admin.PreferredLanguage, "error.pro_billing"));
        }

        if (!string.Equals(user.Plan, Plans.Free, StringComparison.OrdinalIgnoreCase) || user.ProUntil is not null)
        {
            user.Plan = Plans.Free;
            user.ProUntil = null;
            await db.SaveChangesAsync(ct);
            Audit(loggerFactory).LogInformation("Admin: {Handle} back on Free by {Moderator}", user.Handle, admin.Handle);
        }

        return await RowAsync(db, user, clock, ct);
    }

    /// <summary>
    /// Keeps every look of this account off every open board from now (Board.ComputeAsync drops the author's looks and
    /// the fires on them). One column, not a row per look, so the looks not yet posted stay off too. The reason is not
    /// stored: the audit line is the record, and the row only needs the flag.
    /// </summary>
    private static async Task<IResult> ExcludeAccountAsync(
        string handle, BoardExclusionRequest? body, HttpContext context, AppDbContext db, Board board, IClock clock, ILoggerFactory loggerFactory,
        Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var user = await UserEndpoints.FindByHandleAsync(db, handle, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.user_not_found"));
        }

        if (user.BoardExcludedAt is not null)
        {
            return Error(StatusCodes.Status409Conflict, localizer.Get(admin.PreferredLanguage, "error.board_account_excluded"));
        }

        var reason = (body?.Reason ?? "").Trim();
        if (reason.Length > 200)
        {
            reason = reason[..200];
        }

        user.BoardExcludedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        board.Invalidate();
        Audit(loggerFactory).LogInformation("Admin: {Handle} excluded from the board by {Moderator}: {Reason}", user.Handle, admin.Handle, reason);
        return await RowAsync(db, user, clock, ct);
    }

    private static async Task<IResult> IncludeAccountAsync(
        string handle, HttpContext context, AppDbContext db, Board board, IClock clock, ILoggerFactory loggerFactory, Localizer localizer, CancellationToken ct)
    {
        var admin = Admin(context);
        var user = await UserEndpoints.FindByHandleAsync(db, handle, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.user_not_found"));
        }

        if (user.BoardExcludedAt is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.board_account_included"));
        }

        user.BoardExcludedAt = null;
        await db.SaveChangesAsync(ct);
        board.Invalidate();
        Audit(loggerFactory).LogInformation("Admin: {Handle} back on the board by {Moderator}", user.Handle, admin.Handle);
        return await RowAsync(db, user, clock, ct);
    }

    /// <summary>
    /// The sponsor of the week as the server read it from Board:Sponsor:* at start, read-only: whether one is set, the
    /// link as the board shows it (and whether the raw setting was dropped for not being http(s)), and whether the handle
    /// the owner typed is an account here and a verified brand. The setting itself stays in the environment; this tells
    /// the owner what it came to without a terminal.
    /// </summary>
    private static async Task<IResult> SponsorAsync(AppDbContext db, Board board, CancellationToken ct)
    {
        var sponsor = board.Options.Sponsor;
        if (sponsor is not { Enabled: true })
        {
            return Results.Json(new AdminSponsorDto(false, null, null, null, null, false, null, null), AppJson.Options);
        }

        var handle = sponsor.Handle.Trim().TrimStart('@');
        bool? exists = null;
        bool? verified = null;
        if (handle.Length > 0)
        {
            var lower = handle.ToLowerInvariant();
            var row = await db.Users.Where(u => u.HandleLower == lower).Select(u => new { u.Verified }).FirstOrDefaultAsync(ct);
            exists = row is not null;
            verified = row?.Verified ?? false;
        }

        var prize = sponsor.PrizeText.Trim();
        var dto = new AdminSponsorDto(
            true, sponsor.Name.Trim(), handle.Length == 0 ? null : handle, prize.Length == 0 ? null : prize,
            board.SponsorUrl, UrlDropped: !string.IsNullOrWhiteSpace(sponsor.Url) && board.SponsorUrl is null, exists, verified);
        return Results.Json(dto, AppJson.Options);
    }

    /// <summary>
    /// Accounts as the moderator sees them: every look counted, hidden ones included, the reports against their looks and
    /// comments, and (Round 20) the flags the Accounts section acts on. The plan is read through the clock, as everywhere
    /// else, so a granted Pro that ran out reads free and ProUntil travels only while the plan is pro.
    /// </summary>
    private static async Task<List<AdminUserDto>> UserDtosAsync(AppDbContext db, IReadOnlyList<AppUser> users, IClock clock, CancellationToken ct)
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

        var now = clock.UtcNow;
        return users.Select(u =>
        {
            posts.TryGetValue(u.Id, out var p);
            comments.TryGetValue(u.Id, out var c);
            var pro = Plans.IsPro(u, now);
            return new AdminUserDto(PostReader.Ref(u), u.Suspended, p?.Count ?? 0, (p?.Reports ?? 0) + (c?.Reports ?? 0), DateTime.SpecifyKind(u.CreatedAt, DateTimeKind.Utc),
                Verified: u.Verified,
                Plan: pro ? Plans.Pro : Plans.Free,
                ProUntil: pro && u.ProUntil is DateTime until ? DateTime.SpecifyKind(until, DateTimeKind.Utc) : null,
                BoardExcluded: u.BoardExcludedAt is not null,
                IsAdmin: u.IsAdmin);
        }).ToList();
    }

    // ---------- Round 16 - the affiliate line ----------

    /// <summary>
    /// What the shops sent us and what they say it earned. Clicks by store and by day, and money in its three states,
    /// each currency on its own line because a partner reports in its own and nothing here converts.
    /// <para>
    /// Expected and confirmed are never added together. Expected money is a sale inside its return window; treating it
    /// as income is how a business spends what it does not have, so the page keeps them apart and so does this.
    /// </para>
    /// </summary>
    private static async Task<IResult> AffiliateAsync(AppDbContext db, IClock clock, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var since = now.AddDays(-30);

        // Grouped into anonymous shapes and mapped after: EF cannot translate a record constructor inside a GroupBy,
        // and the counts here are small enough that the mapping is free.
        var clicks = (await db.ItemClicks.Where(c => c.CreatedAt >= since)
                .GroupBy(c => new { c.Host, c.Earning })
                .Select(g => new { g.Key.Host, g.Key.Earning, Clicks = g.Count() })
                .OrderByDescending(row => row.Clicks)
                .Take(50)
                .ToListAsync(ct))
            .Select(row => new AffiliateClicksDto(row.Host, row.Earning, row.Clicks))
            .ToList();

        var money = (await db.Commissions.Where(c => c.OccurredAt >= since)
                .GroupBy(c => new { c.Host, c.Currency, c.State })
                .Select(g => new { g.Key.Host, g.Key.Currency, g.Key.State, Amount = g.Sum(c => c.AmountMinor), Count = g.Count() })
                .OrderByDescending(row => row.Amount)
                .Take(50)
                .ToListAsync(ct))
            .Select(row => new AffiliateMoneyDto(row.Host, row.Currency, row.State, row.Amount / 100m, row.Count))
            .ToList();

        // The number that says whether any of this is switched on at all: clicks that could never have earned.
        var total = await db.ItemClicks.CountAsync(c => c.CreatedAt >= since, ct);
        var earning = await db.ItemClicks.CountAsync(c => c.CreatedAt >= since && c.Earning, ct);
        return Results.Json(new AffiliateDto(total, earning, clicks, money), AppJson.Options);
    }

    /// <summary>
    /// Import what a partner reported. Idempotent by (host, external id): the same sale arriving again — which is the
    /// normal way a commission moves from expected to confirmed — updates its row rather than adding a second one.
    /// A report is a file of rows and arrives in one call, so the answer says how many were written.
    /// </summary>
    private static async Task<IResult> ImportCommissionsAsync(
        CommissionImportRequest body, AppDbContext db, Localizer localizer, IClock clock, HttpContext context, CancellationToken ct)
    {
        var language = Localizer.Resolve(null, context.Request);
        var rows = body.Rows ?? [];
        if (rows.Count == 0 || rows.Count > MaxCommissionRows)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.commissions_batch", MaxCommissionRows));
        }

        var now = clock.UtcNow;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Host) || string.IsNullOrWhiteSpace(row.ExternalId)
                || string.IsNullOrWhiteSpace(row.Currency) || row.Currency.Trim().Length != 3
                || !CommissionState.IsKnown(row.State))
            {
                return Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.commission_row", row.ExternalId ?? ""));
            }

            await Affiliates.RecordAsync(db, row.Host, row.ExternalId, row.Amount, row.Currency, row.State!,
                row.OccurredAt == default ? now : row.OccurredAt, row.ItemId, now, ct);
        }

        await db.SaveChangesAsync(ct);
        return Results.Json(new CommissionImportDto(rows.Count), AppJson.Options);
    }

    /// <summary>One partner report in one call; more than this is a file to split, not a request to make bigger.</summary>
    public const int MaxCommissionRows = 500;
}
