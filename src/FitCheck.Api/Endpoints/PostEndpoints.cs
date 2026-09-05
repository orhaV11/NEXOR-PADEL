using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

public static class PostEndpoints
{
    public const int CaptionMaxLength = 140;
    public const int CommentMaxLength = 200;
    public const int MaxProducts = 3;
    private const int DefaultPage = 20;
    private const int MaxPage = 30;

    public static IEndpointRouteBuilder MapPostEndpoints(this IEndpointRouteBuilder app)
    {
        var posts = app.MapGroup("/api/posts");
        posts.MapPost("/", CreateAsync).RequireAuthorization();
        posts.MapGet("/{id:guid}", GetAsync);
        posts.MapGet("/{id:guid}/image", GetImageAsync);
        posts.MapGet("/{id:guid}/video", GetVideoAsync);
        posts.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization();
        posts.MapPost("/{id:guid}/fire", FireAsync).RequireAuthorization();
        posts.MapDelete("/{id:guid}/fire", UnfireAsync).RequireAuthorization();
        posts.MapPost("/{id:guid}/save", SaveAsync).RequireAuthorization();
        posts.MapDelete("/{id:guid}/save", UnsaveAsync).RequireAuthorization();
        posts.MapPost("/{id:guid}/report", ReportAsync).RequireAuthorization();
        posts.MapPost("/{id:guid}/feature", FeatureAsync).RequireAuthorization();
        posts.MapDelete("/{id:guid}/feature", UnfeatureAsync).RequireAuthorization();
        posts.MapGet("/{id:guid}/comments", ListCommentsAsync);
        posts.MapPost("/{id:guid}/comments", AddCommentAsync).RequireAuthorization();

        var comments = app.MapGroup("/api/comments");
        comments.MapDelete("/{id:guid}", DeleteCommentAsync).RequireAuthorization();
        comments.MapPost("/{id:guid}/report", ReportCommentAsync).RequireAuthorization();

        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    public static (int Skip, int Take) Page(int? offset, int? limit) =>
        (Math.Max(0, offset ?? 0), Math.Clamp(limit ?? DefaultPage, 1, MaxPage));

    /// <summary>Takes a page fetched with one extra row and turns it into items plus the next offset.</summary>
    public static async Task<FeedDto> PageDtoAsync(PostReader reader, List<Post> fetched, Guid? viewerId, int skip, int take, CancellationToken ct)
    {
        var hasMore = fetched.Count > take;
        var page = hasMore ? fetched.Take(take).ToList() : fetched;
        return new FeedDto(await reader.ToDtosAsync(page, viewerId, ct), hasMore ? skip + take : null);
    }

    public static string Language(HttpContext context, AppUser? user) =>
        user?.PreferredLanguage ?? Localizer.Resolve(null, context.Request);

    private static async Task<IResult> CreateAsync(
        CreatePostRequest body, HttpContext context, AppDbContext db, PostReader reader, Notifier notifier, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lang = me.PreferredLanguage;
        var check = await db.Checks.FirstOrDefaultAsync(c => c.Id == body.CheckId && c.UserId == me.Id, ct);
        if (check is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.check_not_found"));
        }

        // Nothing the stylist refused or could not read ever becomes public.
        if (check.Status != CheckStatus.Ok || string.IsNullOrEmpty(check.ImagePath) || check.FeedbackJson is null)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.check_not_postable"));
        }

        if (await db.Posts.AnyAsync(p => p.CheckId == check.Id, ct))
        {
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.already_posted"));
        }

        var caption = string.IsNullOrWhiteSpace(body.Caption) ? null : OutfitAnalyzer.SanitizeOccasion(body.Caption);
        if (caption is { Length: > CaptionMaxLength })
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.caption_too_long"));
        }

        var products = body.Products ?? [];
        if (products.Count > 0 && me.AccountType != AccountType.Brand)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.products_brand_only"));
        }

        if (products.Count > MaxProducts || products.Any(p =>
                string.IsNullOrWhiteSpace(p.Label) || p.Label.Trim().Length > 60
                || !UserEndpoints.IsHttpsUrl(p.Url?.Trim(), 500)
                || p.Price is { Length: > 20 }))
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.product_invalid"));
        }

        Challenge? challenge = null;
        if (body.ChallengeId is Guid challengeId)
        {
            challenge = await db.Challenges.FindAsync([challengeId], ct);
            if (challenge is null)
            {
                return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.challenge_not_found"));
            }

            if (challenge.ResolvedAt is not null || challenge.EndsAt <= DateTime.UtcNow)
            {
                return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.challenge_closed"));
            }

            if (challenge.BrandId == me.Id)
            {
                return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.brand_own_challenge"));
            }

            if (await db.Posts.AnyAsync(p => p.ChallengeId == challenge.Id && p.UserId == me.Id, ct))
            {
                return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.already_entered"));
            }
        }

        // Hashtag entry: a look posted with an open challenge's tag enters it, once per person. The challenge's intent is
        // its theme, not a gate; the crowd decides what fits.
        var captionTags = CaptionParser.Tags(caption);
        if (challenge is null && captionTags.Count > 0)
        {
            var open = await db.Challenges
                .Where(c => c.ResolvedAt == null && c.EndsAt > DateTime.UtcNow && captionTags.Contains(c.Tag))
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct);
            foreach (var candidate in open)
            {
                if (candidate.BrandId == me.Id || await db.Posts.AnyAsync(p => p.ChallengeId == candidate.Id && p.UserId == me.Id, ct))
                {
                    continue;
                }

                challenge = candidate;
                break;
            }
        }

        var feedback = JsonSerializer.Deserialize<OutfitFeedback>(check.FeedbackJson, AppJson.Options) ?? new OutfitFeedback();
        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = me.Id,
            CheckId = check.Id,
            Intent = check.Intent,
            Score = check.Score ?? feedback.Score,
            IntentMatch = feedback.IntentMatch,
            Headline = feedback.Headline.Length > 160 ? feedback.Headline[..160] : feedback.Headline,
            Caption = caption,
            ChallengeId = challenge?.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.Posts.Add(post);
        for (var i = 0; i < products.Count; i++)
        {
            var product = products[i];
            db.ProductLinks.Add(new ProductLink
            {
                Id = Guid.NewGuid(), PostId = post.Id, Position = i,
                Label = OutfitAnalyzer.SanitizeText(product.Label, multiline: false), Url = product.Url!.Trim(),
                Price = OutfitAnalyzer.SanitizeText(product.Price, multiline: false) is { Length: > 0 } price ? price : null
            });
        }

        // #tags and @mentions are parsed here, never taken from the client: they become the rows that tag pages,
        // community tabs and Explore query. Unknown handles stay plain text, and mentioning yourself is not a mention.
        foreach (var tag in CaptionParser.Tags(caption))
        {
            db.PostTags.Add(new PostTag { PostId = post.Id, Tag = tag });
        }

        var handles = CaptionParser.Mentions(caption).Select(h => h.ToLowerInvariant()).ToList();
        if (handles.Count > 0)
        {
            var mentioned = await db.Users.Where(u => handles.Contains(u.HandleLower) && u.Id != me.Id).Select(u => u.Id).ToListAsync(ct);
            foreach (var userId in mentioned)
            {
                db.PostMentions.Add(new PostMention { PostId = post.Id, UserId = userId });
                await notifier.AddOnceAsync(userId, NotificationType.Mention, me.Handle, post.Id, null, ct);
            }
        }

        if (challenge is not null && challenge.BrandId != me.Id)
        {
            notifier.Add(challenge.BrandId, NotificationType.Entry, me.Handle, post.Id, challenge.Id);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two taps on "Post it" raced; a unique index decided (one post per check, one entry per challenge).
            db.ChangeTracker.Clear();
            var posted = await db.Posts.AnyAsync(p => p.CheckId == check.Id, ct);
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, posted ? "error.already_posted" : "error.already_entered"));
        }

        var dto = (await reader.ToDtosAsync([post], me.Id, ct))[0];
        return Results.Json(dto, AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// A post the caller may see: a visible one for everyone; a hidden one for its author (who gets the badge) and for a
    /// moderator (the queue shows the look and its photo through the same routes as everyone else). A suspended author's
    /// looks are hidden and stay out of reach while their cookie is still alive: RequireUserAsync only runs on writes.
    /// </summary>
    public static Task<Post?> VisiblePostAsync(AppDbContext db, Guid id, HttpContext context, CancellationToken ct) =>
        VisiblePostAsync(db, id, Sessions.UserId(context.User), AdminEndpoints.IsAdminViewer(context), ct);

    /// <summary>The same rule without a request, so without the moderator allowance. On a route, prefer the overload with the context.</summary>
    public static Task<Post?> VisiblePostAsync(AppDbContext db, Guid id, Guid? viewerId, CancellationToken ct) =>
        VisiblePostAsync(db, id, viewerId, viewerIsAdmin: false, ct);

    private static async Task<Post?> VisiblePostAsync(AppDbContext db, Guid id, Guid? viewerId, bool viewerIsAdmin, CancellationToken ct)
    {
        var post = await db.Posts.FindAsync([id], ct);
        if (post is null || !post.Hidden || viewerIsAdmin)
        {
            return post;
        }

        if (post.UserId != viewerId || await db.Users.AnyAsync(u => u.Id == post.UserId && u.Suspended, ct))
        {
            return null;
        }

        return post;
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var post = await VisiblePostAsync(db, id, context, ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Language(context, null), "error.post_not_found"));
        }

        var votes = post.ChallengeId is null ? 0 : await db.ChallengeVotes.CountAsync(v => v.PostId == post.Id, ct);
        var dto = (await reader.ToDtosAsync([post], viewerId, ct, new Dictionary<Guid, int> { [post.Id] = votes }))[0];
        return Results.Json(dto, AppJson.Options);
    }

    /// <summary>The only way a photo leaves the server: through a post that is public right now.</summary>
    private static async Task<IResult> GetImageAsync(Guid id, HttpContext context, AppDbContext db, IImageStore images, CancellationToken ct)
    {
        var post = await VisiblePostAsync(db, id, context, ct);
        if (post is null)
        {
            return Results.NotFound();
        }

        var imagePath = await db.Checks.Where(c => c.Id == post.CheckId).Select(c => c.ImagePath).FirstOrDefaultAsync(ct);
        var stream = imagePath is null ? null : images.OpenRead(imagePath);
        if (stream is null)
        {
            return Results.NotFound();
        }

        var mediaType = Path.GetExtension(imagePath) switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        context.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.Stream(stream, mediaType);
    }

    /// <summary>
    /// The clip behind a look, under the same door as the photo: a visible post, or 404. Ranges are honoured so a
    /// &lt;video&gt; can seek and Safari, which asks for bytes=0-1 first, can play at all.
    /// </summary>
    private static async Task<IResult> GetVideoAsync(Guid id, HttpContext context, AppDbContext db, IImageStore images, CancellationToken ct)
    {
        var post = await VisiblePostAsync(db, id, context, ct);
        if (post is null)
        {
            return Results.NotFound();
        }

        var videoPath = await db.Checks.Where(c => c.Id == post.CheckId).Select(c => c.VideoPath).FirstOrDefaultAsync(ct);
        var stream = string.IsNullOrEmpty(videoPath) ? null : images.OpenRead(videoPath);
        if (stream is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.File(stream, VideoFormat.FromPath(videoPath!).MediaType, enableRangeProcessing: true);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext context, AppDbContext db, IImageStore images, ILoggerFactory loggerFactory, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var post = await db.Posts.FindAsync([id], ct);
        if (post is null || post.UserId != me.Id)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.post_not_found"));
        }

        await RemovePostAsync(db, post, images, loggerFactory.CreateLogger(nameof(PostEndpoints)), purgeCheck: false, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Takes a look down: every row that points at it, then the files. The owner's delete keeps the check and its photo
    /// (the check is private again and can be posted afresh) but drops the clip, which only ever existed for this post
    /// and should not sit on disk for a look its owner withdrew. A moderator's delete (<paramref name="purgeCheck"/>)
    /// removes the check as well, photo and clip included, so the same content cannot simply be posted again. Set-based,
    /// so a second delete racing this one finds nothing and answers like this one, never 500. Files go after the commit:
    /// a row pointing at a missing file would show a broken player, while an orphaned file leaves with the account folder.
    /// </summary>
    public static async Task RemovePostAsync(AppDbContext db, Post post, IImageStore images, ILogger logger, bool purgeCheck, CancellationToken ct)
    {
        var id = post.Id;
        var check = await db.Checks.Where(c => c.Id == post.CheckId).Select(c => new { c.ImagePath, c.VideoPath }).FirstOrDefaultAsync(ct);
        var files = new List<string>();
        if (!string.IsNullOrEmpty(check?.VideoPath))
        {
            files.Add(check.VideoPath);
        }

        if (purgeCheck && !string.IsNullOrEmpty(check?.ImagePath))
        {
            files.Add(check.ImagePath);
        }

        // Dependents cascade at the database, but explicit deletes keep the behaviour obvious and SQLite-agnostic.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Fires.Where(f => f.PostId == id).ExecuteDeleteAsync(ct);
        await db.Reports.Where(r => r.CommentId != null && db.Comments.Any(c => c.Id == r.CommentId && c.PostId == id)).ExecuteDeleteAsync(ct);
        await db.Comments.Where(c => c.PostId == id).ExecuteDeleteAsync(ct);
        await db.SavedPosts.Where(s => s.PostId == id).ExecuteDeleteAsync(ct);
        await db.ChallengeVotes.Where(v => v.PostId == id).ExecuteDeleteAsync(ct);
        await db.Reports.Where(r => r.PostId == id).ExecuteDeleteAsync(ct);
        await db.ProductLinks.Where(l => l.PostId == id).ExecuteDeleteAsync(ct);
        await db.PostTags.Where(t => t.PostId == id).ExecuteDeleteAsync(ct);
        await db.PostMentions.Where(m => m.PostId == id).ExecuteDeleteAsync(ct);
        // Nothing may keep pointing at a post that is gone: activity rows would link to a 404, a challenge to no winner.
        await db.Notifications.Where(n => n.PostId == id).ExecuteDeleteAsync(ct);
        await db.Challenges.Where(c => c.WinnerPostId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.WinnerPostId, (Guid?)null), ct);
        await db.Posts.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        if (purgeCheck)
        {
            await db.Checks.Where(c => c.Id == post.CheckId).ExecuteDeleteAsync(ct);
        }
        else if (!string.IsNullOrEmpty(check?.VideoPath))
        {
            await db.Checks.Where(c => c.Id == post.CheckId).ExecuteUpdateAsync(s => s.SetProperty(c => c.VideoPath, (string?)null), ct);
        }

        await tx.CommitAsync(ct);

        foreach (var path in files)
        {
            try
            {
                images.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Post {PostId}: the file {Path} could not be removed", id, path);
            }
        }
    }

    private static async Task<IResult> FireAsync(Guid id, HttpContext context, AppDbContext db, Notifier notifier, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var post = await VisiblePostAsync(db, id, context, ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.post_not_found"));
        }

        if (!await db.Fires.AnyAsync(f => f.PostId == id && f.UserId == me.Id, ct))
        {
            db.Fires.Add(new Fire { PostId = id, UserId = me.Id, CreatedAt = DateTime.UtcNow });
            if (post.UserId != me.Id)
            {
                await notifier.AddOnceAsync(post.UserId, NotificationType.Fire, me.Handle, id, null, ct);
            }

            try
            {
                // The row and the counter move together, so an unfire arriving mid-way cannot see one without the other.
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.SaveChangesAsync(ct);
                await db.Posts.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.FireCount, p => p.FireCount + 1), ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A double tap raced itself; the primary key kept the count honest.
            }
        }

        var count = await db.Posts.Where(p => p.Id == id).Select(p => p.FireCount).FirstAsync(ct);
        return Results.Json(new FireStateDto(count, true), AppJson.Options);
    }

    private static async Task<IResult> UnfireAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var removed = await db.Fires.Where(f => f.PostId == id && f.UserId == me.Id).ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            await db.Posts.Where(p => p.Id == id && p.FireCount > 0).ExecuteUpdateAsync(s => s.SetProperty(p => p.FireCount, p => p.FireCount - 1), ct);
        }

        var count = await db.Posts.Where(p => p.Id == id).Select(p => (int?)p.FireCount).FirstOrDefaultAsync(ct);
        if (count is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.post_not_found"));
        }

        return Results.Json(new FireStateDto(count.Value, false), AppJson.Options);
    }

    private static async Task<IResult> SaveAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var post = await VisiblePostAsync(db, id, context, ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.post_not_found"));
        }

        if (!await db.SavedPosts.AnyAsync(s => s.PostId == id && s.UserId == me.Id, ct))
        {
            db.SavedPosts.Add(new SavedPost { PostId = id, UserId = me.Id, CreatedAt = DateTime.UtcNow });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Already saved by a racing tap.
            }
        }

        return Results.Json(new SaveStateDto(true), AppJson.Options);
    }

    private static async Task<IResult> UnsaveAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        await db.SavedPosts.Where(s => s.PostId == id && s.UserId == me.Id).ExecuteDeleteAsync(ct);
        return Results.Json(new SaveStateDto(false), AppJson.Options);
    }

    private static async Task<IResult> ReportAsync(
        Guid id, ReportRequest body, HttpContext context, AppDbContext db, IOptions<LimitsOptions> limits, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var post = await db.Posts.FindAsync([id], ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.post_not_found"));
        }

        if (post.UserId == me.Id)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.report_own"));
        }

        if (await db.Reports.AnyAsync(r => r.PostId == id && r.ReporterId == me.Id, ct))
        {
            return Results.NoContent();
        }

        db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(), PostId = id, ReporterId = me.Id,
            Reason = Truncate(OutfitAnalyzer.SanitizeOccasion(body.Reason), 200), CreatedAt = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Results.NoContent();   // the same person reported twice at once; one row is enough
        }

        // The count comes from the rows, so two reports landing together cannot both write "2".
        post.ReportCount = await db.Reports.CountAsync(r => r.PostId == id, ct);
        if (post.ReportCount >= limits.Value.ReportsToHide)
        {
            post.Hidden = true;
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>
    /// A brand puts its name on a look that mentioned it or entered one of its challenges. One brand per look,
    /// first come; the author hears about it once.
    /// </summary>
    private static async Task<IResult> FeatureAsync(Guid id, HttpContext context, AppDbContext db, Notifier notifier, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lang = me.PreferredLanguage;
        if (me.AccountType != AccountType.Brand)
        {
            return Error(StatusCodes.Status403Forbidden, localizer.Get(lang, "error.brand_only"));
        }

        var post = await db.Posts.FindAsync([id], ct);
        if (post is null || post.Hidden)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.post_not_found"));
        }

        var state = new FeatureStateDto(PostReader.Ref(me));
        if (post.FeaturedByBrandId == me.Id)
        {
            return Results.Json(state, AppJson.Options);   // a second tap asks for the state the look already has
        }

        var mentioned = await db.PostMentions.AnyAsync(m => m.PostId == id && m.UserId == me.Id, ct);
        var entered = post.ChallengeId is Guid challengeId && await db.Challenges.AnyAsync(c => c.Id == challengeId && c.BrandId == me.Id, ct);
        if (!mentioned && !entered)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.feature_not_allowed"));
        }

        if (post.FeaturedByBrandId is not null)
        {
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.already_featured"));
        }

        Guid? brandId = me.Id;
        DateTime? now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // The mark is written only while nobody holds it, so two brands tapping together cannot both win.
        var claimed = await db.Posts.Where(p => p.Id == id && p.FeaturedByBrandId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.FeaturedByBrandId, brandId).SetProperty(p => p.FeaturedAt, now), ct);
        if (claimed == 0)
        {
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.already_featured"));
        }

        if (post.UserId != me.Id)
        {
            await notifier.AddOnceAsync(post.UserId, NotificationType.Featured, me.Handle, post.Id, null, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Json(state, AppJson.Options);
    }

    /// <summary>Only the account that featured the look can take its name off it. Undoing twice is not an error.</summary>
    private static async Task<IResult> UnfeatureAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lang = me.PreferredLanguage;
        var post = await db.Posts.Where(p => p.Id == id).Select(p => new { p.FeaturedByBrandId }).FirstOrDefaultAsync(ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.post_not_found"));
        }

        if (post.FeaturedByBrandId is Guid other && other != me.Id)
        {
            return Error(StatusCodes.Status403Forbidden, localizer.Get(lang, "error.not_featured_by_you"));
        }

        // Cleared only while it is still ours; a look nobody features is already the state asked for.
        await db.Posts.Where(p => p.Id == id && p.FeaturedByBrandId == me.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.FeaturedByBrandId, (Guid?)null).SetProperty(p => p.FeaturedAt, (DateTime?)null), ct);
        return Results.Json(new FeatureStateDto(null), AppJson.Options);
    }

    private static async Task<IResult> ListCommentsAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var post = await VisiblePostAsync(db, id, context, ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Language(context, null), "error.post_not_found"));
        }

        var comments = await db.Comments
            .Where(c => c.PostId == id && !c.Hidden)
            .OrderBy(c => c.CreatedAt)
            .Take(200)
            .ToListAsync(ct);
        var userIds = comments.Select(c => c.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Handle, u.DisplayName, u.AccountType, u.AvatarPath, u.AvatarVersion })
            .ToDictionaryAsync(u => u.Id, ct);

        var dtos = comments.Select(c =>
        {
            users.TryGetValue(c.UserId, out var u);
            var user = u is null ? new UserRefDto("?", "?", "Person") : new UserRefDto(u.Handle, PostReader.NameOf(u.Handle, u.DisplayName), u.AccountType.ToString(), PostReader.AvatarUrl(u.Handle, u.AvatarPath, u.AvatarVersion));
            var isMine = c.UserId == viewerId;
            return new CommentDto(c.Id, user, c.Text, isMine, isMine || post.UserId == viewerId, DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc));
        }).ToList();
        return Results.Json(dtos, AppJson.Options);
    }

    private static async Task<IResult> AddCommentAsync(
        Guid id, CreateCommentRequest body, HttpContext context, AppDbContext db, Notifier notifier, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var post = await VisiblePostAsync(db, id, context, ct);
        if (post is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.post_not_found"));
        }

        var text = OutfitAnalyzer.SanitizeOccasion(body.Text);
        if (text.Length is 0 or > CommentMaxLength)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.comment_invalid"));
        }

        var comment = new Comment { Id = Guid.NewGuid(), PostId = id, UserId = me.Id, Text = text, CreatedAt = DateTime.UtcNow };
        db.Comments.Add(comment);
        if (post.UserId != me.Id)
        {
            await notifier.AddOnceAsync(post.UserId, NotificationType.Comment, me.Handle, id, null, ct);
        }

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.SaveChangesAsync(ct);
            await db.Posts.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => p.CommentCount + 1), ct);
            await tx.CommitAsync(ct);
        }

        var dto = new CommentDto(comment.Id, PostReader.Ref(me), comment.Text, true, true,
            DateTime.SpecifyKind(comment.CreatedAt, DateTimeKind.Utc));
        return Results.Json(dto, AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> DeleteCommentAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var comment = await db.Comments.FindAsync([id], ct);
        if (comment is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.comment_not_found"));
        }

        var postOwner = await db.Posts.Where(p => p.Id == comment.PostId).Select(p => (Guid?)p.UserId).FirstOrDefaultAsync(ct);
        if (comment.UserId != me.Id && postOwner != me.Id)
        {
            return Error(StatusCodes.Status403Forbidden, localizer.Get(me.PreferredLanguage, "error.forbidden"));
        }

        await RemoveCommentAsync(db, comment, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// Removes a comment and its reports. A visible comment leaves its post's count here; a hidden one already did when it
    /// was hidden. Set-based, so a racing second delete answers like the first.
    /// </summary>
    public static async Task RemoveCommentAsync(AppDbContext db, Comment comment, CancellationToken ct)
    {
        var id = comment.Id;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Reports.Where(r => r.CommentId == id).ExecuteDeleteAsync(ct);
        var removed = await db.Comments.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        if (removed > 0 && !comment.Hidden)
        {
            await db.Posts.Where(p => p.Id == comment.PostId && p.CommentCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => p.CommentCount - 1), ct);
        }

        await tx.CommitAsync(ct);
    }

    private static async Task<IResult> ReportCommentAsync(
        Guid id, ReportRequest body, HttpContext context, AppDbContext db, IOptions<LimitsOptions> limits, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var comment = await db.Comments.FindAsync([id], ct);
        if (comment is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.comment_not_found"));
        }

        if (comment.UserId == me.Id)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.report_own"));
        }

        if (await db.Reports.AnyAsync(r => r.CommentId == id && r.ReporterId == me.Id, ct))
        {
            return Results.NoContent();
        }

        db.Reports.Add(new Report
        {
            Id = Guid.NewGuid(), CommentId = id, ReporterId = me.Id,
            Reason = Truncate(OutfitAnalyzer.SanitizeOccasion(body.Reason), 200), CreatedAt = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Results.NoContent();
        }

        comment.ReportCount = await db.Reports.CountAsync(r => r.CommentId == id, ct);
        if (comment.ReportCount >= limits.Value.ReportsToHide && !comment.Hidden)
        {
            // Only the request that flips Hidden moves the post's count; two simultaneous third reports cannot both.
            var hiddenNow = await db.Comments.Where(c => c.Id == id && !c.Hidden).ExecuteUpdateAsync(s => s.SetProperty(c => c.Hidden, true), ct);
            comment.Hidden = true;
            if (hiddenNow > 0)
            {
                await db.Posts.Where(p => p.Id == comment.PostId && p.CommentCount > 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.CommentCount, p => p.CommentCount - 1), ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
