using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

public static class UserEndpoints
{
    public const int MaxInterests = 8;
    public const long AvatarMaxBytes = 2 * 1024 * 1024;

    // Room for the multipart boundary and headers around the photo.
    private const long MultipartOverheadBytes = 256 * 1024;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users");

        group.MapPatch("/me", UpdateMeAsync).RequireAuthorization();
        group.MapDelete("/me", DeleteMeAsync).RequireAuthorization();
        // The form is read by hand, so the antiforgery filter has nothing to validate; the X-Requested-With check covers CSRF.
        group.MapPost("/me/avatar", UploadAvatarAsync).DisableAntiforgery().RequireAuthorization();
        group.MapDelete("/me/avatar", DeleteAvatarAsync).RequireAuthorization();
        group.MapPost("/me/email/resend", ResendVerificationAsync).RequireAuthorization().RequireRateLimiting(AuthEndpoints.RecoveryPolicy);
        group.MapGet("/me/checks", ListMyChecksAsync).RequireAuthorization();
        group.MapGet("/me/saved", ListSavedAsync).RequireAuthorization();
        group.MapGet("/{handle}", GetProfileAsync);
        group.MapGet("/{handle}/avatar", GetAvatarAsync);
        group.MapGet("/{handle}/posts", ListPostsAsync);
        group.MapGet("/{handle}/community", ListCommunityAsync);
        group.MapGet("/{handle}/featured", ListFeaturedAsync);
        group.MapPost("/{handle}/follow", FollowAsync).RequireAuthorization();
        group.MapDelete("/{handle}/follow", UnfollowAsync).RequireAuthorization();

        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    /// <summary>Unix seconds, always increasing: a handle that is freed and taken again never reuses a cached avatar URL.</summary>
    public static int NextAvatarVersion(int current) =>
        Math.Max(current + 1, (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % int.MaxValue));

    public static bool IsHttpsUrl(string? url, int maxLength) =>
        url is not null && url.Length <= maxLength && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// Loads the signed-in user, or answers 401 when the cookie outlived the account and 403 when the account is suspended.
    /// Both clear the cookie. Sessions are cookies, so this refusal on the next request is what a suspension enforces.
    /// </summary>
    public static async Task<(AppUser? User, IResult? Failure)> RequireUserAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([Sessions.RequiredUserId(context.User)], ct);
        if (user is null)
        {
            await Sessions.SignOutAsync(context);
            return (null, Error(StatusCodes.Status401Unauthorized, localizer.Get(Localizer.Resolve(null, context.Request), "error.sign_in_required")));
        }

        if (user.Suspended)
        {
            await Sessions.SignOutAsync(context);
            return (null, Error(StatusCodes.Status403Forbidden, localizer.Get(user.PreferredLanguage, "error.suspended")));
        }

        return (user, null);
    }

    /// <summary>Any account by handle, suspended ones included: for the moderation routes and for undoing a follow.</summary>
    public static Task<AppUser?> FindByHandleAsync(AppDbContext db, string handle, CancellationToken ct)
    {
        var lower = handle.ToLowerInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.HandleLower == lower, ct);
    }

    /// <summary>An account as the public sees it: a suspended one answers like a missing one on every profile route.</summary>
    private static Task<AppUser?> FindVisibleByHandleAsync(AppDbContext db, string handle, CancellationToken ct)
    {
        var lower = handle.ToLowerInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.HandleLower == lower && !u.Suspended, ct);
    }

    /// <summary>Parses an enum by name only, case-insensitively: "3" is neither a style intent nor an account type.</summary>
    private static bool TryParseName<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        var trimmed = value?.Trim();
        return trimmed is { Length: > 0 }
            && Enum.TryParse(trimmed, ignoreCase: true, out result)
            && Enum.IsDefined(result)
            && string.Equals(result.ToString(), trimmed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Visible looks that mention this account: a brand's community tab, and the count on its profile.</summary>
    private static IQueryable<Post> MentioningPosts(AppDbContext db, Guid userId) =>
        db.Posts.Where(p => !p.Hidden && db.PostMentions.Any(m => m.PostId == p.Id && m.UserId == userId));

    /// <summary>Brand: looks it featured. Person: their looks a brand featured. Visible ones only, either way.</summary>
    private static IQueryable<Post> FeaturedPosts(AppDbContext db, AppUser user) =>
        user.AccountType == AccountType.Brand
            ? db.Posts.Where(p => !p.Hidden && p.FeaturedByBrandId == user.Id)
            : db.Posts.Where(p => !p.Hidden && p.UserId == user.Id && p.FeaturedByBrandId != null);

    private static async Task<IResult> UpdateMeAsync(
        UpdateMeRequest body, HttpContext context, AppDbContext db, Localizer localizer, IEmailSender email, IOptions<EmailOptions> emailOptions,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var (user, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        // Email: null leaves it, "" clears it (with the verification and every open link), a new address replaces it
        // unverified and gets a link. Checked first, so nothing else changes when the address is refused.
        var newAddress = false;
        if (body.Email is not null)
        {
            var trimmed = body.Email.Trim();
            if (trimmed.Length == 0)
            {
                if (user.Email is not null)
                {
                    user.Email = null;
                    user.EmailVerifiedAt = null;
                    await RecoveryTokens.VoidOpenAsync(db, user.Id, AuthTokenPurpose.Verify, DateTime.UtcNow, ct);
                    await RecoveryTokens.VoidOpenAsync(db, user.Id, AuthTokenPurpose.Reset, DateTime.UtcNow, ct);
                }
            }
            else
            {
                // An address nobody can confirm is not worth keeping: with mail off the field is refused, not stored.
                if (!email.Enabled)
                {
                    return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.email_disabled"));
                }

                if (!RecoveryTokens.TryNormalizeEmail(trimmed, out var address))
                {
                    return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.email_invalid"));
                }

                if (address != user.Email)
                {
                    if (await db.Users.AnyAsync(u => u.Email == address && u.Id != user.Id, ct))
                    {
                        return Error(StatusCodes.Status409Conflict, localizer.Get(user.PreferredLanguage, "error.email_taken"));
                    }

                    // Every new address mails a link; one account cannot turn that into a stream at someone's inbox.
                    if (await RecoveryTokens.ThrottledAsync(db, user.Id, AuthTokenPurpose.Verify, ct))
                    {
                        return Error(StatusCodes.Status429TooManyRequests, localizer.Get(user.PreferredLanguage, "error.recovery_limited"));
                    }

                    user.Email = address;
                    user.EmailVerifiedAt = null;
                    await RecoveryTokens.VoidOpenAsync(db, user.Id, AuthTokenPurpose.Reset, DateTime.UtcNow, ct);
                    newAddress = true;
                }
            }
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

        // Brand mode is a setting, switchable any time; what changes is which routes open up, not what exists.
        if (body.AccountType is not null)
        {
            if (!TryParseName<AccountType>(body.AccountType, out var accountType))
            {
                return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.account_type_invalid"));
            }

            user.AccountType = accountType;
        }

        if (body.Interests is not null)
        {
            var interests = new List<StyleIntent>();
            foreach (var entry in body.Interests)
            {
                if (!TryParseName<StyleIntent>(entry, out var intent))
                {
                    return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.interests_invalid"));
                }

                if (!interests.Contains(intent))
                {
                    interests.Add(intent);
                }
            }

            if (interests.Count > MaxInterests)
            {
                return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.interests_invalid"));
            }

            // Canonical names in the order picked, so they come back the way the person chose them. An empty list clears them.
            user.Interests = interests.Count == 0 ? null : string.Join(',', interests);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (newAddress)
        {
            // Two accounts raced for the same address; the unique index decided.
            return Error(StatusCodes.Status409Conflict, localizer.Get(user.PreferredLanguage, "error.email_taken"));
        }

        if (newAddress && !await AuthEndpoints.SendVerificationAsync(context, db, user, email, emailOptions, localizer, loggerFactory.CreateLogger(typeof(UserEndpoints)), ct))
        {
            // The address and its link are saved: "Send the link again" in Settings is the retry.
            return Error(StatusCodes.Status502BadGateway, localizer.Get(user.PreferredLanguage, "error.email_send_failed"));
        }

        return Results.Json(await AuthEndpoints.ToMeAsync(db, user, ct), AppJson.Options);
    }

    /// <summary>A fresh verification link for the address on the account (the earlier links stop working). 204 when it went out.</summary>
    private static async Task<IResult> ResendVerificationAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IEmailSender email, IOptions<EmailOptions> emailOptions, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var (user, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        if (!email.Enabled)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.email_disabled"));
        }

        if (user.Email is null || user.EmailVerifiedAt is not null)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.invalid_request"));
        }

        if (await RecoveryTokens.ThrottledAsync(db, user.Id, AuthTokenPurpose.Verify, ct))
        {
            return Error(StatusCodes.Status429TooManyRequests, localizer.Get(user.PreferredLanguage, "error.recovery_limited"));
        }

        if (!await AuthEndpoints.SendVerificationAsync(context, db, user, email, emailOptions, localizer, loggerFactory.CreateLogger(typeof(UserEndpoints)), ct))
        {
            return Error(StatusCodes.Status502BadGateway, localizer.Get(user.PreferredLanguage, "error.email_send_failed"));
        }

        return Results.NoContent();
    }

    /// <summary>Replaces the profile photo. Detected from its bytes, capped at 2 MB, stored beside the person's checks.</summary>
    private static async Task<IResult> UploadAvatarAsync(
        HttpContext context, AppDbContext db, IImageStore images, Localizer localizer, CancellationToken ct)
    {
        var (user, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var lang = user.PreferredLanguage;
        var request = context.Request;
        if (!request.HasFormContentType)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.invalid_request"));
        }

        // Reject oversize bodies before buffering them; the transport limit backs up the form limit.
        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = AvatarMaxBytes + MultipartOverheadBytes;
        }

        if (request.ContentLength > AvatarMaxBytes + MultipartOverheadBytes)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, localizer.Get(lang, "error.avatar_too_large"));
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or InvalidOperationException)
        {
            var tooLarge = ex.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase)
                           || ex.Message.Contains("too large", StringComparison.OrdinalIgnoreCase)
                           || ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge };
            return tooLarge
                ? Error(StatusCodes.Status413PayloadTooLarge, localizer.Get(lang, "error.avatar_too_large"))
                : Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.invalid_request"));
        }

        var file = form.Files.GetFile("image");
        if (file is null)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.invalid_request"));
        }

        if (file.Length > AvatarMaxBytes)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, localizer.Get(lang, "error.avatar_too_large"));
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        // Magic bytes, not the client's content type or file name: the browser's guess is not a security boundary.
        var format = ImageFormat.Detect(bytes);
        if (format is null)
        {
            return Error(StatusCodes.Status415UnsupportedMediaType, localizer.Get(lang, "error.avatar_invalid"));
        }

        user.AvatarPath = await images.SaveAvatarAsync(user.Id, format, bytes, CancellationToken.None);
        // A new version is a new URL, so a day-long cache can never show the old photo.
        user.AvatarVersion = NextAvatarVersion(user.AvatarVersion);
        await db.SaveChangesAsync(ct);
        return Results.Json(await AuthEndpoints.ToMeAsync(db, user, ct), AppJson.Options);
    }

    private static async Task<IResult> DeleteAvatarAsync(
        HttpContext context, AppDbContext db, IImageStore images, Localizer localizer, CancellationToken ct)
    {
        var (user, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        if (user.AvatarPath is not null)
        {
            // File first, as with account deletion: a row pointing at a missing file just 404s, an orphaned file has no owner.
            images.Delete(user.AvatarPath);
            user.AvatarPath = null;
            user.AvatarVersion = NextAvatarVersion(user.AvatarVersion);
            await db.SaveChangesAsync(ct);
        }

        return Results.Json(await AuthEndpoints.ToMeAsync(db, user, ct), AppJson.Options);
    }

    /// <summary>
    /// The second and last photo route. Public and cacheable for a day: the version in the URL changes when the photo does.
    /// A suspended account reads as missing here as on every other profile route; a cached copy can outlive the suspension by
    /// a day at most.
    /// </summary>
    private static async Task<IResult> GetAvatarAsync(string handle, HttpContext context, AppDbContext db, IImageStore images, CancellationToken ct)
    {
        var lower = handle.ToLowerInvariant();
        var avatarPath = await db.Users.Where(u => u.HandleLower == lower && !u.Suspended).Select(u => u.AvatarPath).FirstOrDefaultAsync(ct);
        if (avatarPath is null)
        {
            return Results.NotFound();
        }

        var stream = images.OpenRead(avatarPath);
        if (stream is null)
        {
            return Results.NotFound();
        }

        var mediaType = Path.GetExtension(avatarPath) switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        context.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.Stream(stream, mediaType);
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

        // A moderator's handle freed by deletion could be registered by anyone; the flag goes with the row, so nothing would
        // be inherited, but a handle in Admin:Handles would be promoted again on the next restart. Un-admin first, on purpose.
        if (user.IsAdmin)
        {
            return Error(StatusCodes.Status403Forbidden, localizer.Get(user.PreferredLanguage, "error.admin_delete"));
        }

        var id = user.Id;
        var handle = user.Handle;
        // Files first (photos and the avatar share the folder): if a row delete fails the user can retry, but an
        // orphaned photo would have no owner to delete it. By path as well as by folder: a check or comparison the account
        // claimed from a guest was copied into the folder, but a row that still names a file elsewhere must not outlive
        // "delete my account and photos" either. A missing file is not an error.
        foreach (var check in await db.Checks.Where(c => c.UserId == id).Select(c => new { c.ImagePath, c.VideoPath }).ToListAsync(ct))
        {
            images.Delete(check.ImagePath);
            if (!string.IsNullOrEmpty(check.VideoPath))
            {
                images.Delete(check.VideoPath);
            }
        }

        foreach (var comparison in await db.Comparisons.Where(c => c.UserId == id).Select(c => new { c.ImagePathA, c.ImagePathB }).ToListAsync(ct))
        {
            images.Delete(comparison.ImagePathA);
            images.Delete(comparison.ImagePathB);
        }

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

        // Looks this brand featured stay up, unfeatured; looks that mentioned this account lose the mention.
        await db.Posts.Where(p => p.FeaturedByBrandId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.FeaturedByBrandId, (Guid?)null).SetProperty(p => p.FeaturedAt, (DateTime?)null), ct);
        await db.PostMentions.Where(m => m.UserId == id || myPostIds.Contains(m.PostId)).ExecuteDeleteAsync(ct);
        await db.PostTags.Where(t => myPostIds.Contains(t.PostId)).ExecuteDeleteAsync(ct);

        await db.Fires.Where(f => f.UserId == id || myPostIds.Contains(f.PostId)).ExecuteDeleteAsync(ct);
        await db.Reports.Where(r => r.ReporterId == id || (r.PostId != null && myPostIds.Contains(r.PostId.Value))).ExecuteDeleteAsync(ct);
        await db.Comments.Where(c => c.UserId == id || myPostIds.Contains(c.PostId)).ExecuteDeleteAsync(ct);
        await db.ChallengeVotes.Where(v => v.UserId == id || myPostIds.Contains(v.PostId)).ExecuteDeleteAsync(ct);
        await db.SavedPosts.Where(s => s.UserId == id || myPostIds.Contains(s.PostId)).ExecuteDeleteAsync(ct);
        await db.ProductLinks.Where(l => myPostIds.Contains(l.PostId)).ExecuteDeleteAsync(ct);
        // Own notifications, everyone else's that point at a post or challenge about to disappear, and the mention
        // and featured ones this account sent: the mark or mention they announce is gone.
        // Everything addressed to this account, and everything it caused elsewhere: a freed handle must not inherit old activity.
        await db.Notifications.Where(n => n.UserId == id || n.ActorHandle == user.Handle).ExecuteDeleteAsync(ct);
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

        // Round 10: the items on this account's looks, the board's memory of it, and the looks it pulled off the board as a
        // moderator (a moderator cannot delete while moderating, so that last set is empty in practice; the cascade agrees).
        await db.PostItems.Where(i => myPostIds.Contains(i.PostId)).ExecuteDeleteAsync(ct);
        await db.BoardExclusions.Where(e => e.ByUserId == id || myPostIds.Contains(e.PostId)).ExecuteDeleteAsync(ct);
        await db.WeeklyWinners.Where(w => w.UserId == id).ExecuteDeleteAsync(ct);

        await db.Posts.Where(p => p.UserId == id).ExecuteDeleteAsync(ct);
        await db.Checks.Where(c => c.UserId == id).ExecuteDeleteAsync(ct);
        // The recovery links cascade with the row, but a link that outlived its account would be a way back in; explicit.
        await db.AuthTokens.Where(t => t.UserId == id).ExecuteDeleteAsync(ct);
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
        var user = await FindVisibleByHandleAsync(db, handle, ct);
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
                Following: viewerId is Guid v && await db.Follows.AnyAsync(f => f.FollowerId == v && f.FollowedId == user.Id, ct)),
            AvatarUrl: PostReader.AvatarUrl(user.Handle, user.AvatarPath, user.AvatarVersion),
            Featured: await FeaturedPosts(db, user).CountAsync(ct),
            Community: await MentioningPosts(db, user.Id).CountAsync(ct),
            Verified: user.Verified);

        return Results.Json(profile, AppJson.Options);
    }

    private static async Task<IResult> ListPostsAsync(
        string handle, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, int? offset, int? limit, CancellationToken ct)
    {
        var user = await FindVisibleByHandleAsync(db, handle, ct);
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

    /// <summary>Looks that mention this account, newest first. Public, like the profile itself.</summary>
    private static async Task<IResult> ListCommunityAsync(
        string handle, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, int? offset, int? limit, CancellationToken ct)
    {
        var user = await FindVisibleByHandleAsync(db, handle, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, context.Request), "error.user_not_found"));
        }

        var (skip, take) = PostEndpoints.Page(offset, limit);
        var posts = await MentioningPosts(db, user.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(ct);
        return Results.Json(await PostEndpoints.PageDtoAsync(reader, posts, Sessions.UserId(context.User), skip, take, ct), AppJson.Options);
    }

    /// <summary>For a brand, the looks it featured; for a person, their looks a brand featured. Newest featured first.</summary>
    private static async Task<IResult> ListFeaturedAsync(
        string handle, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, int? offset, int? limit, CancellationToken ct)
    {
        var user = await FindVisibleByHandleAsync(db, handle, ct);
        if (user is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, context.Request), "error.user_not_found"));
        }

        var (skip, take) = PostEndpoints.Page(offset, limit);
        var posts = await FeaturedPosts(db, user)
            .OrderByDescending(p => p.FeaturedAt)
            .ThenByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(ct);
        return Results.Json(await PostEndpoints.PageDtoAsync(reader, posts, Sessions.UserId(context.User), skip, take, ct), AppJson.Options);
    }

    private static async Task<IResult> FollowAsync(
        string handle, HttpContext context, AppDbContext db, Notifier notifier, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var target = await FindVisibleByHandleAsync(db, handle, ct);
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

        var target = await FindByHandleAsync(db, handle, ct);
        if (target is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.user_not_found"));
        }

        await db.Follows.Where(f => f.FollowerId == me.Id && f.FollowedId == target.Id).ExecuteDeleteAsync(ct);
        return Results.Json(new FollowStateDto(await db.Follows.CountAsync(f => f.FollowedId == target.Id, ct), false), AppJson.Options);
    }
}
