using System.Diagnostics;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

public static class CheckEndpoints
{
    public const int OccasionMaxLength = 120;
    private static readonly TimeSpan CapWindow = TimeSpan.FromHours(24);

    // Room for multipart boundaries and the small text fields around the image.
    private const long MultipartOverheadBytes = 256 * 1024;

    public static IEndpointRouteBuilder MapCheckEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/checks");

        // The form is read by hand from the request, so the antiforgery filter has nothing to validate.
        group.MapPost("/", CreateAsync).DisableAntiforgery();
        group.MapGet("/{id:guid}", GetAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        AppDbContext db,
        IImageStore images,
        OutfitAnalyzer analyzer,
        CheckCapacity capacity,
        Localizer localizer,
        IOptions<StorageOptions> storage,
        IOptions<LimitsOptions> limits,
        ILogger<OutfitAnalyzer> logger,
        CancellationToken ct)
    {
        var request = context.Request;
        var maxBytes = storage.Value.MaxImageBytes;
        var maxMb = maxBytes / (1024 * 1024);
        var headerLanguage = Localizer.Resolve(null, request);

        if (!request.HasFormContentType)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(headerLanguage, "error.invalid_request"));
        }

        // Reject oversize bodies before buffering them; the transport limit backs up the form limit.
        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = maxBytes + MultipartOverheadBytes;
        }

        if (request.ContentLength > maxBytes + MultipartOverheadBytes)
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

        if (!Guid.TryParse(form["userId"], out var userId))
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(headerLanguage, "error.user_not_found"));
        }

        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(headerLanguage, "error.user_not_found"));
        }

        // The check's language is what the feedback is written in. A shipped locale in the form wins; anything
        // else (missing, stale, unknown) falls back to the user's stored preference, never to a header guess.
        if (!Localizer.TryMatch(form["language"].ToString(), out var language))
        {
            language = Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : Localizer.DefaultLocale;
        }

        if (!Enum.TryParse<StyleIntent>(form["intent"], ignoreCase: true, out var intent) || !Enum.IsDefined(intent))
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.intent_invalid"));
        }

        var occasion = OutfitAnalyzer.SanitizeOccasion(form["occasion"].ToString());
        if (occasion.Length > OccasionMaxLength)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.occasion_too_long"));
        }

        var file = form.Files.GetFile("image");
        if (file is null || file.Length == 0)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.image_required"));
        }

        if (file.Length > maxBytes)
        {
            return UserEndpoints.Error(StatusCodes.Status413PayloadTooLarge, localizer.Get(language, "error.image_too_large", maxMb));
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        // Magic bytes, not the client's content type: the browser's guess is not a security boundary.
        var format = ImageFormat.Detect(bytes);
        if (format is null)
        {
            return UserEndpoints.Error(StatusCodes.Status415UnsupportedMediaType, localizer.Get(language, "error.image_format"));
        }

        var now = DateTime.UtcNow;
        var cap = limits.Value.ChecksPerDay;
        var windowStart = now - CapWindow;
        // Failed calls do not count: a model outage must not eat the user's allowance.
        var recent = await db.Checks
            .Where(c => c.UserId == userId && c.CreatedAt >= windowStart && c.Status != CheckStatus.Error)
            .OrderBy(c => c.CreatedAt)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);
        var storedGlobal = await db.Checks.CountAsync(c => c.CreatedAt >= windowStart && c.Status != CheckStatus.Error, ct);

        var verdict = capacity.TryReserve(userId, recent.Count, cap, storedGlobal, limits.Value.ChecksPerDayGlobal, out var reservation);
        if (verdict == CapacityVerdict.UserCapReached)
        {
            if (recent.Count > 0)
            {
                var retryAfter = (int)Math.Ceiling((recent[0] + CapWindow - now).TotalSeconds);
                context.Response.Headers.RetryAfter = Math.Max(retryAfter, 1).ToString();
            }

            return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.rate_limited", cap));
        }

        if (verdict == CapacityVerdict.GlobalCapReached)
        {
            return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.rate_limited_global"));
        }

        using var _ = reservation;
        var check = new OutfitCheck
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Intent = intent,
            Occasion = occasion.Length == 0 ? null : occasion,
            Language = language,
            PromptVersion = OutfitAnalyzer.PromptVersion,
            CreatedAt = now
        };

        // The photo is written only once the model has confirmed an outfit, so a rejected, unrecognised, failed or
        // abandoned check never leaves a private image on disk.
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var feedback = await analyzer.AnalyzeAsync(bytes, format.MediaType, intent, occasion, language, ct);
            check.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            check.Status = feedback.Status;

            switch (feedback.Status)
            {
                case CheckStatus.Ok:
                    check.Score = feedback.Score;
                    check.FeedbackJson = JsonSerializer.Serialize(feedback, AppJson.Options);
                    check.ImagePath = await images.SaveAsync(userId, check.Id, format, bytes, CancellationToken.None);
                    break;
                case CheckStatus.NotOutfit:
                    // Keep the friendly explanation; there is no outfit to remember.
                    check.FeedbackJson = JsonSerializer.Serialize(feedback, AppJson.Options);
                    break;
                default:
                    // Rejected: nothing but the status survives. Not the model's words, not the wearer's note.
                    check.Occasion = null;
                    break;
            }
        }
        catch (VisionRefusedException ex)
        {
            logger.LogInformation(ex, "Check {CheckId} refused by the API", check.Id);
            check.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            check.Status = CheckStatus.Rejected;
            check.Occasion = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The phone went away mid-check. Nothing was written, nothing counts against the cap.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Check {CheckId} failed", check.Id);
            check.LatencyMs = (int)stopwatch.ElapsedMilliseconds;
            check.Status = CheckStatus.Error;
            db.Checks.Add(check);
            await db.SaveChangesAsync(CancellationToken.None);
            return UserEndpoints.Error(StatusCodes.Status502BadGateway, localizer.Get(language, "error.model_failed"));
        }

        db.Checks.Add(check);
        await db.SaveChangesAsync(CancellationToken.None);

        return Results.Json(CheckDto.FromEntity(check, localizer), AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>Owner only. A wrong owner gets the same 404 as a missing id, so ids do not leak existence.</summary>
    private static async Task<IResult> GetAsync(
        Guid id, Guid? userId, HttpRequest request, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var check = await db.Checks.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (check is null || userId is null || check.UserId != userId)
        {
            return UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, request), "error.check_not_found"));
        }

        return Results.Json(CheckDto.FromEntity(check, localizer), AppJson.Options);
    }
}
