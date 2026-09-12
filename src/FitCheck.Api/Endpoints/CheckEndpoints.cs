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

public static class CheckEndpoints
{
    public const int OccasionMaxLength = 120;

    /// <summary>
    /// The anonymous check path's abuse brake: Plans:GuestAttemptsPerDay attempts per client address per day, whatever
    /// they come to (429 error.too_fast beyond it). The guest's cap, Plans:GuestChecksPerDay per cookie and per address,
    /// is the handler's and counts only the checks it stored. A signed-in call passes through it unlimited; the plan cap
    /// is the handler's too.
    /// </summary>
    public const string GuestPolicy = "guest";

    // Room for multipart boundaries and the small text fields around the image and the clip.
    private const long MultipartOverheadBytes = 256 * 1024;

    public static IEndpointRouteBuilder MapCheckEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/checks");

        // The form is read by hand from the request, so the antiforgery filter has nothing to validate;
        // the X-Requested-With check in Program.cs covers CSRF for every state-changing call.
        // No session needed: a visitor gets one check as a guest (GuestChecks); the handler tells the two apart.
        group.MapPost("/", CreateAsync).DisableAntiforgery().RequireRateLimiting(GuestPolicy);
        group.MapPost("/claim", ClaimAsync).RequireAuthorization();
        group.MapGet("/{id:guid}", GetAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        AppDbContext db,
        IImageStore images,
        OutfitAnalyzer analyzer,
        CheckCapacity capacity,
        GuestAddressCounter guestAddresses,
        Localizer localizer,
        IOptions<StorageOptions> storage,
        IOptions<LimitsOptions> limits,
        IOptions<PlanOptions> plans,
        ILogger<OutfitAnalyzer> logger,
        Transcoder transcoder,
        CancellationToken ct)
    {
        var request = context.Request;
        var maxBytes = storage.Value.MaxImageBytes;
        var maxMb = maxBytes / (1024 * 1024);
        var maxVideoBytes = storage.Value.MaxVideoBytes;
        var maxVideoMb = maxVideoBytes / (1024 * 1024);
        var maxVideoSeconds = storage.Value.MaxVideoSeconds;
        // Whether a clip rides along is only known once the form is parsed, so the body limit admits both and the
        // per-file limits below decide. A body over the pair can only be an oversize clip in practice (the client
        // downscales stills to 1280px), so that is the message, unless clips are switched off on this server.
        var maxBodyBytes = maxBytes + maxVideoBytes + MultipartOverheadBytes;
        var headerLanguage = Localizer.Resolve(null, request);
        var bodyTooLarge = maxVideoBytes > 0
            ? localizer.Get(headerLanguage, "error.video_too_large", maxVideoMb, maxVideoSeconds)
            : localizer.Get(headerLanguage, "error.image_too_large", maxMb);

        // Signed in: the account, with the usual refusals for a cookie that outlived it or a suspension. Signed out: a
        // guest, named by the cookie when there is one (a token is minted at the cap check otherwise).
        AppUser? user = null;
        string? guestToken = null;
        if (Sessions.UserId(context.User) is not null)
        {
            var (found, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
            if (found is null)
            {
                return failure!;
            }

            user = found;
        }
        else
        {
            guestToken = GuestChecks.Read(context);
        }

        if (!request.HasFormContentType)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(headerLanguage, "error.invalid_request"));
        }

        // Reject oversize bodies before buffering them; the transport limit backs up the form limit.
        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = maxBodyBytes;
        }

        if (request.ContentLength > maxBodyBytes)
        {
            return UserEndpoints.Error(StatusCodes.Status413PayloadTooLarge, bodyTooLarge);
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
                ? UserEndpoints.Error(StatusCodes.Status413PayloadTooLarge, bodyTooLarge)
                : UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(headerLanguage, "error.invalid_request"));
        }

        // The check's language is what the feedback is written in. A shipped locale in the form wins; anything
        // else (missing, stale, unknown) falls back to the user's stored preference, never to a header guess. A guest
        // has no stored preference, so the header is the only default left.
        if (!Localizer.TryMatch(form["language"].ToString(), out var language))
        {
            language = user is not null
                ? Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : Localizer.DefaultLocale
                : headerLanguage;
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

        // An optional clip of the same look. The stylist never sees it: the still above is the frame the wearer picked
        // for the check, and the clip is what gets posted next to the verdict. Only its size and container are checked
        // here, in the same order as the photo's and before the cap, so a user at the cap still learns about a bad file.
        // ASP.NET has already buffered a file this size to a temp file; it is never read into memory whole.
        var clip = form.Files.GetFile("video");
        VideoFormat? videoFormat = null;
        if (clip is { Length: > 0 })
        {
            if (clip.Length > maxVideoBytes)
            {
                return UserEndpoints.Error(StatusCodes.Status413PayloadTooLarge, localizer.Get(language, "error.video_too_large", maxVideoMb, maxVideoSeconds));
            }

            var head = new byte[VideoFormat.SniffLength];
            int sniffed;
            await using (var probe = clip.OpenReadStream())
            {
                sniffed = await probe.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
            }

            videoFormat = VideoFormat.Detect(head.AsSpan(0, sniffed));
            if (videoFormat is null)
            {
                return UserEndpoints.Error(StatusCodes.Status415UnsupportedMediaType, localizer.Get(language, "error.video_format"));
            }
        }

        var now = DateTime.UtcNow;
        int cap;
        Guid reservationKey;
        List<DateTime> recent;
        GuestAddressCounter.Reservation? addressReservation = null;
        if (user is not null)
        {
            // The plan's cap, never above Limits:ChecksPerDay. Comparisons are stylist calls too and share the allowance
            // (Spend counts both). Failed calls do not count: a model outage must not eat the user's allowance.
            cap = Plans.CapFor(user, plans.Value, limits.Value, now);
            reservationKey = user.Id;
            recent = await Spend.RecentForUserAsync(db, user.Id, now, ct);
        }
        else
        {
            // A guest: Plans:GuestChecksPerDay per cookie token, from the rows, and the same per client address, from what
            // this process stored (GuestAddressCounter: a refused upload or a failed call never spends an address's look;
            // the "guest" policy in front of the route only brakes attempts). Zero means guests are off on this server,
            // and the door says sign in rather than "that was your free look".
            cap = plans.Value.GuestChecksPerDay;
            if (cap <= 0)
            {
                return UserEndpoints.Error(StatusCodes.Status401Unauthorized, localizer.Get(language, "error.sign_in_required"));
            }

            if (guestToken is null)
            {
                guestToken = GuestChecks.NewToken();
                recent = [];
            }
            else
            {
                recent = await Spend.RecentForGuestAsync(db, guestToken, now, ct);
            }

            reservationKey = GuestChecks.ReservationKey(guestToken);

            // Last in the branch, so nothing can throw between taking the slot and the lease below that gives it back.
            var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (!guestAddresses.TryReserve(address, cap, now, out addressReservation, out var addressRetryAfter))
            {
                if (addressRetryAfter is { } seconds)
                {
                    context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                }

                return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.guest_limit"));
            }
        }

        // The address's slot, given back on every path but the stored, counted check below (Commit).
        using var addressLease = addressReservation;
        var storedGlobal = await Spend.StoredGlobalAsync(db, now, ct);

        var verdict = capacity.TryReserve(reservationKey, recent.Count, cap, storedGlobal, limits.Value.ChecksPerDayGlobal, out var reservation);
        if (verdict == CapacityVerdict.UserCapReached)
        {
            if (Spend.RetryAfterSeconds(recent, cap, now) is { } retryAfter)
            {
                context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            }

            // A free account hears what Pro would give it; a Pro account at its own ceiling just hears the number, as on
            // the compare route; a guest hears that the look was the free one.
            var message = user is null ? localizer.Get(language, "error.guest_limit")
                : Plans.IsPro(user, now) ? localizer.Get(language, "error.rate_limited", cap)
                : localizer.Get(language, "error.plan_limit", cap, plans.Value.ProChecksPerDay);
            return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, message);
        }

        if (verdict == CapacityVerdict.GlobalCapReached)
        {
            return UserEndpoints.Error(StatusCodes.Status429TooManyRequests, localizer.Get(language, "error.rate_limited_global"));
        }

        using var _ = reservation;
        // The guest's cookie goes out with this answer whatever the verdict: from here the check is this visitor's to
        // read, and to keep by signing up. A signed-in check carries no token.
        if (user is null)
        {
            GuestChecks.Issue(context, guestToken!, now);
        }

        var owner = user?.Id ?? GuestChecks.StorageFolder;
        var check = new OutfitCheck
        {
            Id = Guid.NewGuid(),
            UserId = user?.Id,
            GuestToken = user is null ? guestToken : null,
            Intent = intent,
            Occasion = occasion.Length == 0 ? null : occasion,
            Language = language,
            PromptVersion = OutfitAnalyzer.PromptVersion,
            CreatedAt = now
        };

        // The photo is written only once the model has confirmed an outfit, so a rejected, unrecognised, failed or
        // abandoned check never leaves a private image on disk. The clip follows the same rule, after the photo.
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
                    check.ImagePath = await images.SaveAsync(owner, check.Id, format, bytes, CancellationToken.None);
                    if (clip is not null && videoFormat is not null)
                    {
                        check.VideoPath = await TryStoreClipAsync(images, owner, check.Id, videoFormat, clip, logger);
                    }

                    if (user is not null)
                    {
                        UpdateStreak(user, now);
                    }

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
        // Stored with a status that cost a model call: the address's look is spent. (The 502 above stored an error row and commits nothing.)
        addressReservation?.Commit(now);

        // After the commit, so the worker finds the row; it re-encodes the clip to H.264 MP4 in the background (a no-op without
        // ffmpeg). A guest's clip waits until the check is claimed; the claim queues it.
        if (check.VideoPath is not null && user is not null)
        {
            transcoder.Enqueue(check.Id);
        }

        return Results.Json(CheckDto.FromEntity(check, localizer, null), AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// Streams the clip from the request buffer into the store. A clip that cannot be written is not worth the check:
    /// the verdict and the still are already in hand, so the failure is logged and the check goes out without a clip.
    /// </summary>
    private static async Task<string?> TryStoreClipAsync(
        IImageStore images, Guid owner, Guid checkId, VideoFormat format, IFormFile clip, ILogger logger)
    {
        try
        {
            await using var source = clip.OpenReadStream();
            return await images.SaveVideoAsync(owner, checkId, format, source, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Check {CheckId}: the clip could not be stored; the check keeps its still only", checkId);
            return null;
        }
    }

    /// <summary>Consecutive UTC days with an ok check. A missed day starts over; a second check today changes nothing.</summary>
    public static void UpdateStreak(AppUser user, DateTime now)
    {
        var today = now.Date;
        if (user.LastCheckDate == today)
        {
            return;
        }

        user.StreakCount = user.LastCheckDate == today.AddDays(-1) ? user.StreakCount + 1 : 1;
        user.LastCheckDate = today;
    }

    /// <summary>
    /// Everything the caller's guest cookie names becomes the caller's: the check made before signing up follows the
    /// person into the account (and can be posted from here), and the cookie goes. 200 { claimed: 0 } when there was
    /// nothing to claim, so the client can call it blind after every sign-in. A file that cannot be moved leaves the rows
    /// the guest's and the cookie in place, and answers 500: the client claims again on its next load.
    /// </summary>
    private static async Task<IResult> ClaimAsync(
        HttpContext context, AppDbContext db, IImageStore images, Transcoder transcoder, Localizer localizer, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var claimed = 0;
        var token = GuestChecks.Read(context);
        if (token is not null)
        {
            var logger = loggerFactory.CreateLogger(typeof(GuestChecks).FullName!);
            try
            {
                claimed = await GuestChecks.ClaimAsync(db, images, transcoder, token, user.Id, DateTime.UtcNow, logger, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Claim: the guest check(s) could not be moved into {UserId}'s folder; they stay the guest's, the cookie stays, and the client claims again on its next load", user.Id);
                return UserEndpoints.Error(StatusCodes.Status500InternalServerError, localizer.Get(user.PreferredLanguage, "error.server"));
            }

            GuestChecks.Clear(context);
            if (claimed > 0)
            {
                logger.LogInformation("Claim: {Count} guest row(s) now belong to {UserId}", claimed, user.Id);
            }
        }

        return Results.Json(new ClaimResultDto(claimed), AppJson.Options);
    }

    /// <summary>
    /// The owner, or the guest whose cookie made it. Anyone else, and a wrong owner, gets the same 404 as a missing id,
    /// so ids do not leak existence.
    /// </summary>
    private static async Task<IResult> GetAsync(
        Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var userId = Sessions.UserId(context.User);
        var guestToken = GuestChecks.Read(context);
        var check = await db.Checks.FirstOrDefaultAsync(c => c.Id == id, ct);
        var mine = check is not null
            && ((userId is not null && check.UserId == userId)
                || (check.UserId is null && guestToken is not null && check.GuestToken == guestToken));
        if (!mine)
        {
            return UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, context.Request), "error.check_not_found"));
        }

        var postId = await db.Posts.Where(p => p.CheckId == id).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        return Results.Json(CheckDto.FromEntity(check!, localizer, postId), AppJson.Options);
    }
}
