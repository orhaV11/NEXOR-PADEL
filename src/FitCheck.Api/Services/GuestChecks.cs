using System.Security.Cryptography;
using System.Text;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>What POST /api/checks/claim answers: how many guest checks and comparisons now belong to the caller.</summary>
public sealed record ClaimResultDto(int Claimed);

/// <summary>
/// Check before signing up. A visitor's first check sets a guest cookie carrying a random token, and the check row keeps
/// that token where an owner would be. Signing up or in with the cookie claims the rows (<see cref="ClaimAsync"/>): they
/// get the owner, lose the token, and their photos move into the owner's folder, so everything downstream (posting, the
/// history, account deletion) sees an ordinary check. Rows nobody claims expire with the cookie, one day, swept by
/// <see cref="GuestCheckSweeper"/> with their files.
/// </summary>
public static class GuestChecks
{
    public const string CookieName = "orevosh.guest";

    /// <summary>How long a guest's cookie, checks and photos live unless they are claimed.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    /// <summary>
    /// The store folder a guest's photo or clip is written to until it is claimed or swept. Not an account id: nothing
    /// deletes it as a whole, files come and go one by one.
    /// </summary>
    public static readonly Guid StorageFolder = Guid.Empty;

    private const int TokenBytes = 32;

    /// <summary>32 random bytes as base64url, no padding.</summary>
    private const int TokenLength = 43;

    public static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>The token from the guest cookie, or null when there is none or it is not one of ours.</summary>
    public static string? Read(HttpContext context) =>
        context.Request.Cookies.TryGetValue(CookieName, out var value) && IsToken(value) ? value : null;

    public static bool IsToken(string? value) =>
        value is { Length: TokenLength } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Sets the guest cookie: HttpOnly, SameSite=Strict, Secure over https (like the session), one day.</summary>
    public static void Issue(HttpContext context, string token, DateTime now) =>
        context.Response.Cookies.Append(CookieName, token, Options(context, new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)) + Lifetime));

    /// <summary>Drops the guest cookie (after a claim: the rows it named are the account's now).</summary>
    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, Options(context, null));

    private static CookieOptions Options(HttpContext context, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = context.Request.IsHttps,
        Path = "/",
        IsEssential = true,
        Expires = expires
    };

    /// <summary>The in-memory reservation key for a guest's checks in flight (<see cref="CheckCapacity"/> is keyed by Guid).</summary>
    public static Guid ReservationKey(string token) =>
        new(SHA256.HashData(Encoding.ASCII.GetBytes(token)).AsSpan(0, 16));

    /// <summary>
    /// Moves every check and comparison carrying the token to the account: owner set, token cleared, ClaimedAt stamped,
    /// photos and clips copied into the owner's folder, the old files removed once the rows are saved, and each claimed clip
    /// queued for the transcoder (which leaves a guest's clip alone until then). All or nothing: a file that cannot be
    /// copied aborts the claim before anything is saved (the fresh copies are removed, the rows stay the guest's, the
    /// caller answers 500 and the client claims again on its next load), so an owned row never points outside the
    /// owner's folder, which is what account deletion and the sweeper rely on. Returns how many rows moved.
    /// </summary>
    public static async Task<int> ClaimAsync(
        AppDbContext db, IImageStore images, Transcoder transcoder, string token, Guid userId, DateTime now, ILogger logger, CancellationToken ct)
    {
        var checks = await db.Checks.Where(c => c.UserId == null && c.GuestToken == token).ToListAsync(ct);
        var comparisons = await db.Comparisons.Where(c => c.UserId == null && c.GuestToken == token).ToListAsync(ct);
        if (checks.Count == 0 && comparisons.Count == 0)
        {
            return 0;
        }

        var copied = new List<(string Old, string New)>();
        try
        {
            foreach (var check in checks)
            {
                check.ImagePath = (await CopyAsync(images, check.ImagePath, userId, copied, ct)) ?? "";
                check.VideoPath = await CopyAsync(images, check.VideoPath, userId, copied, ct);
                check.UserId = userId;
                check.GuestToken = null;
                check.ClaimedAt = now;
            }

            foreach (var comparison in comparisons)
            {
                comparison.ImagePathA = (await CopyAsync(images, comparison.ImagePathA, userId, copied, ct)) ?? "";
                comparison.ImagePathB = (await CopyAsync(images, comparison.ImagePathB, userId, copied, ct)) ?? "";
                comparison.UserId = userId;
                comparison.GuestToken = null;
                comparison.ClaimedAt = now;
            }

            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // The rows were not saved: the copies are orphans and the originals still serve. The context still holds the
            // half-moved rows; nothing saves them, the request ends here.
            foreach (var (_, fresh) in copied)
            {
                TryDelete(images, fresh, logger);
            }

            throw;
        }

        foreach (var (old, _) in copied)
        {
            TryDelete(images, old, logger);
        }

        foreach (var check in checks.Where(c => !string.IsNullOrEmpty(c.VideoPath)))
        {
            transcoder.Enqueue(check.Id);
        }

        return checks.Count + comparisons.Count;
    }

    /// <summary>
    /// Copies one stored file (&lt;folder&gt;/&lt;id&gt;.&lt;ext&gt;) into the owner's folder under the same name through the
    /// store's own doors, and returns the new relative path. An empty path (a check that kept no photo) or one already in
    /// the owner's folder is returned as it is. Anything else that cannot be copied, a file that is not in the store or a
    /// path that is not in the store's shape included, throws: a claimed row must never keep a path outside its owner's folder.
    /// </summary>
    private static async Task<string?> CopyAsync(
        IImageStore images, string? relative, Guid userId, List<(string Old, string New)> copied, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return relative;
        }

        var name = Path.GetFileNameWithoutExtension(relative);
        var extension = Path.GetExtension(relative).TrimStart('.').ToLowerInvariant();
        var folder = Path.GetDirectoryName(relative) ?? "";
        if (string.Equals(folder, userId.ToString("N"), StringComparison.OrdinalIgnoreCase))
        {
            return relative;
        }

        if (!Guid.TryParseExact(name, "N", out var id))
        {
            throw new InvalidOperationException($"The stored path {relative} is not in the store's shape.");
        }

        await using var source = images.OpenRead(relative)
                                 ?? throw new FileNotFoundException("The file to claim is not in the store.", relative);
        string fresh;
        switch (extension)
        {
            case "jpg" or "png" or "webp":
            {
                var format = extension switch { "jpg" => ImageFormat.Jpeg, "png" => ImageFormat.Png, _ => ImageFormat.WebP };
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, ct);
                fresh = await images.SaveAsync(userId, id, format, buffer.ToArray(), ct);
                break;
            }
            case "mp4" or "webm":
                fresh = await images.SaveVideoAsync(userId, id, VideoFormat.FromPath(relative), source, ct);
                break;
            default:
                throw new InvalidOperationException($"The stored path {relative} is not a photo or a clip the store knows.");
        }

        copied.Add((relative, fresh));
        return fresh;
    }

    private static void TryDelete(IImageStore images, string relative, ILogger logger)
    {
        try
        {
            images.Delete(relative);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Claim: {Path} could not be removed", relative);
        }
    }
}

/// <summary>
/// Expires what guests never claimed: checks and comparisons with no owner that are older than <see cref="GuestChecks.Lifetime"/>
/// go with their photos and clips, once at start and then every hour. A guest cookie lives exactly that long, so nothing a
/// live cookie can still name is removed.
/// </summary>
public sealed class GuestCheckSweeper(IServiceScopeFactory scopes, IImageStore images, ILogger<GuestCheckSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepSafelyAsync(stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task SweepSafelyAsync(CancellationToken ct)
    {
        try
        {
            await SweepAsync(DateTime.UtcNow, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The guest sweep failed; it runs again in an hour");
        }
    }

    /// <summary>Removes unclaimed guest rows created before <paramref name="now"/> minus a day, files first. Returns the counts.</summary>
    public async Task<(int Checks, int Comparisons)> SweepAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now - GuestChecks.Lifetime;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var checks = await db.Checks.Where(c => c.UserId == null && c.CreatedAt < cutoff).ToListAsync(ct);
        foreach (var check in checks)
        {
            images.Delete(check.ImagePath);
            if (!string.IsNullOrEmpty(check.VideoPath))
            {
                images.Delete(check.VideoPath);
            }
        }

        var comparisons = await db.Comparisons.Where(c => c.UserId == null && c.CreatedAt < cutoff).ToListAsync(ct);
        foreach (var comparison in comparisons)
        {
            images.Delete(comparison.ImagePathA);
            images.Delete(comparison.ImagePathB);
        }

        db.Checks.RemoveRange(checks);
        db.Comparisons.RemoveRange(comparisons);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Guest sweep: removed {Checks} unclaimed check(s) and {Comparisons} comparison(s) older than a day",
            checks.Count, comparisons.Count);
        return (checks.Count, comparisons.Count);
    }
}
