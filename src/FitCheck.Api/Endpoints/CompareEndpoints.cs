using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// "Which one?": two photos, one stylist call, one winner. A comparison is private like a check and never postable; it
/// counts against the same daily allowance as a check, and needs Pro when Plans:CompareNeedsPro says so. The photos
/// are stored only once the stylist confirmed two outfits, under the owner's folder (so an account deletion takes them
/// with the folder), and are served through /api/compare/{id}/image/{a|b} to the owner only.
/// </summary>
public static class CompareEndpoints
{
    // Room for multipart boundaries and the small text fields around the two photos.
    private const long MultipartOverheadBytes = 256 * 1024;

    /// <summary>How many comparisons the history route returns.</summary>
    public const int HistoryLength = 20;

    public static IEndpointRouteBuilder MapCompareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/compare");

        // The form is read by hand from the request, so the antiforgery filter has nothing to validate;
        // the X-Requested-With check in Program.cs covers CSRF for every state-changing call.
        group.MapPost("/", CreateAsync).DisableAntiforgery().RequireAuthorization();
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization();
        group.MapGet("/{id:guid}/image/{side}", GetImageAsync).RequireAuthorization();

        app.MapGet("/api/users/me/comparisons", ListMineAsync).RequireAuthorization();

        return app;
    }

    /// <summary>
    /// The photos of a comparison share the owner's folder with the checks' photos, through the same store call, under an
    /// id derived from the comparison's: the last byte of the comparison id is XORed with 0xA1 for side A and 0xB2 for
    /// side B, so the two files are &lt;userId&gt;/&lt;derived id&gt;.&lt;ext&gt; and go with the folder when the account goes.
    /// </summary>
    public static Guid SideImageId(Guid comparisonId, string side)
    {
        var bytes = comparisonId.ToByteArray();
        bytes[15] ^= side == "a" ? (byte)0xA1 : (byte)0xB2;
        return new Guid(bytes);
    }

    private static Task<string> SaveSideAsync(IImageStore images, Guid userId, Guid comparisonId, string side, ImageFormat format, byte[] bytes) =>
        images.SaveAsync(userId, SideImageId(comparisonId, side), format, bytes, CancellationToken.None);

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        AppDbContext db,
        IImageStore images,
        IOutfitVisionClient vision,
        CheckCapacity capacity,
        Localizer localizer,
        IOptions<StorageOptions> storage,
        IOptions<LimitsOptions> limits,
        IOptions<PlanOptions> plans,
        ILogger<OutfitComparer> logger,
        CancellationToken ct)
    {
        var request = context.Request;
        var maxBytes = storage.Value.MaxImageBytes;
        var maxMb = maxBytes / (1024 * 1024);
        var maxBodyBytes = 2 * maxBytes + MultipartOverheadBytes;
        var headerLanguage = Localizer.Resolve(null, request);

        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var userId = user.Id;
        var now = DateTime.UtcNow;
        var preferred = Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : headerLanguage;

        // The Pro gate comes before the body is read: no point buffering two photos for an answer that is already no.
        if (plans.Value.CompareNeedsPro && !Plans.IsPro(user, now))
        {
            return UserEndpoints.Error(StatusCodes.Status403Forbidden, localizer.Get(preferred, "error.pro_required"));
        }

        if (!request.HasFormContentType)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(headerLanguage, "error.invalid_request"));
        }

        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = maxBodyBytes;
        }

        if (request.ContentLength > maxBodyBytes)
        {
            return UserEndpoints.Error(StatusCodes.Status413PayloadTooLarge, localizer.Get(headerLanguage, "error.image_too_large", maxMb));
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
                ? UserEndpoints.Error(StatusCodes.Status413PayloadTooLarge, localizer.Get(headerLanguage, "error.image_too_large", maxMb))
                : UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(headerLanguage, "error.invalid_request"));
        }

        // Same rule as a check: a shipped locale in the form wins; anything else falls back to the stored preference.
        if (!Localizer.TryMatch(form["language"].ToString(), out var language))
        {
            language = Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : Localizer.DefaultLocale;
        }

        if (!Enum.TryParse<StyleIntent>(form["intent"], ignoreCase: true, out var intent) || !Enum.IsDefined(intent))
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.intent_invalid"));
        }

        var occasion = OutfitAnalyzer.SanitizeOccasion(form["occasion"].ToString());
        if (occasion.Length > CheckEndpoints.OccasionMaxLength)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.occasion_too_long"));
        }

        // Both photos, or neither: one outfit is a check, not a comparison.
        var fileA = form.Files.GetFile("imageA");
        var fileB = form.Files.GetFile("imageB");
        if (fileA is null || fileA.Length == 0 || fileB is null || fileB.Length == 0)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.compare_two_photos"));
        }

        // Each still is validated exactly like a check's: size first, then the magic bytes, never the client's content type.
        var (bytesA, formatA, problemA) = await ReadStillAsync(fileA, maxBytes, maxMb, language, localizer, ct);
        if (problemA is not null)
        {
            return problemA;
        }

        var (bytesB, formatB, problemB) = await ReadStillAsync(fileB, maxBytes, maxMb, language, localizer, ct);
        if (problemB is not null)
        {
            return problemB;
        }

        // The allowance is one number for checks and comparisons together (Spend counts both, for this route, the check
        // route and the global ceiling alike): the plan's cap, never above Limits:ChecksPerDay. Failed calls do not count,
        // on either side: a model outage must not eat the user's allowance.
        var cap = Plans.CapFor(user, plans.Value, limits.Value, now);
        var recent = await Spend.RecentForUserAsync(db, userId, now, ct);
        var storedGlobal = await Spend.StoredGlobalAsync(db, now, ct);

        var verdict = capacity.TryReserve(userId, recent.Count, cap, storedGlobal, limits.Value.ChecksPerDayGlobal, out var reservation);
        if (verdict == CapacityVerdict.UserCapReached)
        {
            if (Spend.RetryAfterSeconds(recent, cap, now) is { } retryAfter)
            {
                context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            }

            // A free account hears what Pro would give it; a Pro account at its own ceiling just hears the number.
            var message = Plans.IsPro(user, now)
                ? localizer.Get(language, "error.rate_limited", cap)
                : localizer.Get(language, "error.plan_limit", cap, plans.Value.ProChecksPerDay);
            return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, message);
        }

        if (verdict == CapacityVerdict.GlobalCapReached)
        {
            return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.rate_limited_global"));
        }

        using var _ = reservation;
        var comparison = new OutfitComparison
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Intent = intent,
            Occasion = occasion.Length == 0 ? null : occasion,
            Language = language,
            PromptVersion = OutfitComparer.PromptVersion,
            CreatedAt = now
        };

        // The photos are written only once the model has confirmed two outfits, so a rejected, unrecognised, failed or
        // abandoned comparison never leaves a private image on disk. Same rule as a check.
        var comparer = new OutfitComparer(vision);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var feedback = await comparer.CompareAsync(bytesA, formatA!.MediaType, bytesB, formatB!.MediaType, intent, occasion, language, ct);
            comparison.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            comparison.Status = feedback.Status;

            switch (feedback.Status)
            {
                case CheckStatus.Ok:
                    comparison.Winner = feedback.Winner;
                    comparison.FeedbackJson = JsonSerializer.Serialize(feedback, AppJson.Options);
                    comparison.ImagePathA = await SaveSideAsync(images, userId, comparison.Id, "a", formatA, bytesA);
                    comparison.ImagePathB = await SaveSideAsync(images, userId, comparison.Id, "b", formatB, bytesB);
                    break;
                case CheckStatus.NotOutfit:
                    // Keep the friendly explanation (it says which photo to replace); there is no outfit to remember.
                    comparison.FeedbackJson = JsonSerializer.Serialize(feedback, AppJson.Options);
                    break;
                default:
                    // Rejected: nothing but the status survives. Not the model's words, not the wearer's note.
                    comparison.Occasion = null;
                    break;
            }
        }
        catch (VisionRefusedException ex)
        {
            logger.LogInformation(ex, "Comparison {ComparisonId} refused by the API", comparison.Id);
            comparison.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            comparison.Status = CheckStatus.Rejected;
            comparison.Occasion = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The phone went away mid-call. Nothing was written, nothing counts against the cap.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Comparison {ComparisonId} failed", comparison.Id);
            comparison.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            comparison.Status = CheckStatus.Error;
            db.Comparisons.Add(comparison);
            await db.SaveChangesAsync(CancellationToken.None);
            return UserEndpoints.Error(StatusCodes.Status502BadGateway, localizer.Get(language, "error.model_failed"));
        }

        db.Comparisons.Add(comparison);
        await db.SaveChangesAsync(CancellationToken.None);

        return Results.Json(ToDto(comparison, localizer), AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>One still into memory, checked like a check's: the size cap (413), then the magic bytes (415).</summary>
    private static async Task<(byte[] Bytes, ImageFormat? Format, IResult? Problem)> ReadStillAsync(
        IFormFile file, long maxBytes, long maxMb, string language, Localizer localizer, CancellationToken ct)
    {
        if (file.Length > maxBytes)
        {
            return ([], null, UserEndpoints.Error(StatusCodes.Status413PayloadTooLarge, localizer.Get(language, "error.image_too_large", maxMb)));
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var format = ImageFormat.Detect(bytes);
        if (format is null)
        {
            return ([], null, UserEndpoints.Error(StatusCodes.Status415UnsupportedMediaType, localizer.Get(language, "error.image_format")));
        }

        return (bytes, format, null);
    }

    /// <summary>
    /// The DTO. A rejected row stores nothing but the status, so the neutral message is added here in the comparison's
    /// language; the image URLs are empty unless both photos are on disk (only an ok comparison keeps them).
    /// </summary>
    public static ComparisonDto ToDto(OutfitComparison comparison, Localizer localizer)
    {
        ComparisonFeedback? feedback = null;
        if (comparison.Status == CheckStatus.Rejected)
        {
            feedback = new ComparisonFeedback
            {
                Status = CheckStatus.Rejected,
                Winner = "",
                ScoreA = 1,
                ScoreB = 1,
                Message = localizer.Get(comparison.Language, "feedback.rejected")
            };
        }
        else if (comparison.FeedbackJson is not null)
        {
            feedback = JsonSerializer.Deserialize<ComparisonFeedback>(comparison.FeedbackJson, AppJson.Options);
        }

        var hasImages = comparison.ImagePathA.Length > 0 && comparison.ImagePathB.Length > 0;
        return new ComparisonDto(
            comparison.Id,
            comparison.Intent,
            comparison.Occasion,
            comparison.Language,
            DateTime.SpecifyKind(comparison.CreatedAt, DateTimeKind.Utc),
            comparison.LatencyMs,
            comparison.Status,
            feedback,
            hasImages ? $"/api/compare/{comparison.Id}/image/a" : "",
            hasImages ? $"/api/compare/{comparison.Id}/image/b" : "");
    }

    /// <summary>Owner only. A wrong owner gets the same 404 as a missing id, so ids do not leak existence.</summary>
    private static async Task<IResult> GetAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var comparison = await FindOwnAsync(id, context, db, ct);
        if (comparison is null)
        {
            return NotFound(context, localizer);
        }

        return Results.Json(ToDto(comparison, localizer), AppJson.Options);
    }

    /// <summary>
    /// One of the two photos, to the owner only, never cached by anything but that person's browser. The only route that
    /// serves a comparison photo; a comparison without photos (not an outfit, rejected, failed) answers 404 like a
    /// missing one.
    /// </summary>
    private static async Task<IResult> GetImageAsync(
        Guid id, string side, HttpContext context, AppDbContext db, IImageStore images, Localizer localizer, CancellationToken ct)
    {
        var comparison = side is "a" or "b" ? await FindOwnAsync(id, context, db, ct) : null;
        var path = comparison is null ? "" : side == "a" ? comparison.ImagePathA : comparison.ImagePathB;
        var stream = path.Length == 0 ? null : images.OpenRead(path);
        if (stream is null)
        {
            return NotFound(context, localizer);
        }

        var mediaType = Path.GetExtension(path) switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        context.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.Stream(stream, mediaType);
    }

    /// <summary>The caller's last comparisons, newest first, for a history.</summary>
    private static async Task<IResult> ListMineAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var comparisons = await db.Comparisons
            .Where(c => c.UserId == user.Id)
            .OrderByDescending(c => c.CreatedAt)
            .Take(HistoryLength)
            .ToListAsync(ct);

        return Results.Json(comparisons.Select(c => ToDto(c, localizer)).ToList(), AppJson.Options);
    }

    private static async Task<OutfitComparison?> FindOwnAsync(Guid id, HttpContext context, AppDbContext db, CancellationToken ct)
    {
        var userId = Sessions.UserId(context.User);
        var comparison = await db.Comparisons.FirstOrDefaultAsync(c => c.Id == id, ct);
        return comparison is null || userId is null || comparison.UserId != userId ? null : comparison;
    }

    private static IResult NotFound(HttpContext context, Localizer localizer) =>
        UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, context.Request), "error.comparison_not_found"));
}
