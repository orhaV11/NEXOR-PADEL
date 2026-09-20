using System.Globalization;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// The data export (Round 11): everything an account wrote, as one file. The contract:
/// <list type="bullet">
/// <item><c>GET /api/users/me/export</c> (session): 200 with an <see cref="ExportDto"/> body serialised with
/// <see cref="AppJson.Options"/> (camelCase, enums as names, nulls left out), <c>Content-Type: application/json</c>,
/// <c>Content-Disposition: attachment; filename="orevosh-&lt;handle&gt;-&lt;yyyyMMdd&gt;.json"</c> (a handle with letters
/// outside ASCII gets an ASCII stand-in there and the real name in <c>filename*</c>, RFC 5987, which browsers prefer) and
/// <c>Cache-Control: no-store</c>, so the browser saves it and no cache keeps it. Built in one read per table, the
/// account's own rows only (checks, looks with their tags and pieces, comments, follows both ways, comparisons, blocks,
/// notifications), newest first; hidden looks and hidden comments included (they are the person's words). Comments carry
/// only the text the person wrote, follows and followers only handles, notifications only a type and a time: nothing in
/// the file is another person's. No photo, no clip, no birth date, no password hash, no billing ids. A suspended account
/// still exports (the session cookie it still holds opens this one door: the words are theirs whatever the account did).
/// Errors: error.export_failed (500) when a read fails half-way; nothing is written anywhere, so a retry is safe.
/// <see cref="ExportsPerHour"/> per account per hour through the <see cref="Policy"/> rate limiter (429 error.too_fast
/// past it): the file is every table at once, and three an hour is plenty for a person and a brake for a script.
/// Logged as "Export: {UserId} took their data".</item>
/// </list>
/// The client side (settings.export, export.hint, export.ready in the i18n files) fetches it and hands the blob to the
/// browser as a download (views/settings.js).
/// </summary>
public static class ExportEndpoints
{
    /// <summary>The rate-limiting policy's name (Program.cs): a fixed hour per account.</summary>
    public const string Policy = "export";

    public const int ExportsPerHour = 3;

    /// <summary>The log category of the export line.</summary>
    public const string LogCategory = "FitCheck.Api.Export";

    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users/me/export", ExportAsync).RequireAuthorization().RequireRateLimiting(Policy);
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    /// <summary>The Content-Disposition value for an export made now: the plain filename, plus filename* when the handle needs it.</summary>
    public static string ContentDisposition(string handle, DateTime now)
    {
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var plain = $"orevosh-{handle}-{date}.json";
        if (plain.All(c => c < 128))
        {
            return $"attachment; filename=\"{plain}\"";
        }

        // A header value is ASCII: the plain name keeps what it can of the handle (or "account" when nothing is left) and
        // the real name travels percent-encoded in filename*, which every current browser reads first.
        var kept = new string(handle.Where(c => c < 128).ToArray());
        var fallback = $"orevosh-{(kept.Length > 0 ? kept : "account")}-{date}.json";
        return $"attachment; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(plain)}";
    }

    private static async Task<IResult> ExportAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IClock clock, ILoggerFactory loggers, CancellationToken ct)
    {
        // Not RequireUserAsync: that door is shut to a suspended account, and this one stays open to it.
        var user = await db.Users.FindAsync([Sessions.RequiredUserId(context.User)], ct);
        if (user is null)
        {
            await Sessions.SignOutAsync(context);
            return Error(StatusCodes.Status401Unauthorized, localizer.Get(Localizer.Resolve(null, context.Request), "error.sign_in_required"));
        }

        var now = clock.UtcNow;
        var logger = loggers.CreateLogger(LogCategory);
        ExportDto export;
        try
        {
            export = await BuildAsync(db, user, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Export: the data of {UserId} could not be read", user.Id);
            return Error(StatusCodes.Status500InternalServerError, localizer.Get(user.PreferredLanguage, "error.export_failed"));
        }

        logger.LogInformation("Export: {UserId} took their data", user.Id);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentDisposition = ContentDisposition(user.Handle, now);
        return Results.Json(export, AppJson.Options);
    }

    /// <summary>The document, one read per table, the account's own rows only, newest first.</summary>
    private static async Task<ExportDto> BuildAsync(AppDbContext db, AppUser user, DateTime now, CancellationToken ct)
    {
        var id = user.Id;
        var (plan, proUntil) = BillingEndpoints.EffectivePlan(user, now);
        var account = new ExportAccountDto(
            user.Handle, user.Name, user.AccountType.ToString(), user.PreferredLanguage, user.Email, Utc(user.CreatedAt), plan, proUntil);

        var checkRows = await db.Checks.AsNoTracking()
            .Where(c => c.UserId == id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.CreatedAt, c.Intent, c.Occasion, c.Style, c.Note, c.Score, c.Status, c.FeedbackJson, c.Useful, c.UsefulAt, c.UsefulNote })
            .ToListAsync(ct);
        var checks = checkRows.Select(c =>
        {
            var feedback = c.FeedbackJson is null ? null : JsonSerializer.Deserialize<OutfitFeedback>(c.FeedbackJson, AppJson.Options);
            return new ExportCheckDto(
                c.Id, Utc(c.CreatedAt), c.Intent, c.Note, c.Score,
                NullIfBlank(feedback?.Headline), NullIfBlank(feedback?.OneTip),
                feedback?.Breakdown is { } b ? new BreakdownDto(b.Fit, b.Color, b.Accessories) : null,
                feedback?.Items.Select(i => new ExportItemDto(i.Name, i.Category)).ToList() ?? [],
                c.Status,
                // Round 13: what the person said about the tip; their words, so they travel with the check.
                c.Useful, c.UsefulAt is { } usefulAt ? Utc(usefulAt) : null, c.UsefulNote,
                // Round 14: the pair behind the one word, and whether the tip was a change or a keep.
                c.Occasion, c.Style, feedback is null || feedback.Status != CheckStatus.Ok ? null : feedback.TipKind);
        }).ToList();

        var postRows = await db.Posts.AsNoTracking()
            .Where(p => p.UserId == id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.CreatedAt, p.Caption, p.Intent, p.Score, p.FireCount, p.CommentCount })
            .ToListAsync(ct);
        // The tags and pieces of those looks, through the owner rather than a list of ids: one read each, whatever the count.
        var tags = await db.PostTags.AsNoTracking()
            .Where(t => db.Posts.Any(p => p.Id == t.PostId && p.UserId == id))
            .Select(t => new { t.PostId, t.Tag })
            .ToListAsync(ct);
        var pieces = await db.PostItems.AsNoTracking()
            .Where(i => db.Posts.Any(p => p.Id == i.PostId && p.UserId == id))
            .OrderBy(i => i.Position)
            .Select(i => new { i.PostId, i.Name, i.Category, i.Brand, i.Model, i.Url })
            .ToListAsync(ct);
        var tagsByPost = tags.ToLookup(t => t.PostId, t => t.Tag);
        var piecesByPost = pieces.ToLookup(i => i.PostId, i => new ExportItemDto(i.Name, i.Category, i.Brand, i.Model, i.Url));
        var posts = postRows.Select(p => new ExportPostDto(
            p.Id, Utc(p.CreatedAt), p.Caption, p.Intent, p.Score, tagsByPost[p.Id].ToList(), piecesByPost[p.Id].ToList(), p.FireCount, p.CommentCount)).ToList();

        var comments = (await db.Comments.AsNoTracking()
            .Where(c => c.UserId == id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.PostId, c.CreatedAt, c.Text })
            .ToListAsync(ct)).Select(c => new ExportCommentDto(c.PostId, Utc(c.CreatedAt), c.Text)).ToList();

        // The other ends of the follows and blocks, joined to their accounts (the rows cascade with them, so every id resolves).
        var follows = await HandlesAsync(db.Follows.Where(f => f.FollowerId == id)
            .Join(db.Users, f => f.FollowedId, u => u.Id, (f, u) => new ExportHandleDto(u.Handle, f.CreatedAt)), ct);
        var followers = await HandlesAsync(db.Follows.Where(f => f.FollowedId == id)
            .Join(db.Users, f => f.FollowerId, u => u.Id, (f, u) => new ExportHandleDto(u.Handle, f.CreatedAt)), ct);
        var blocks = await HandlesAsync(db.Blocks.Where(b => b.BlockerId == id)
            .Join(db.Users, b => b.BlockedId, u => u.Id, (b, u) => new ExportHandleDto(u.Handle, b.CreatedAt)), ct);

        var comparisons = (await db.Comparisons.AsNoTracking()
            .Where(c => c.UserId == id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.CreatedAt, c.Winner })
            .ToListAsync(ct)).Select(c => new ExportComparisonDto(c.Id, Utc(c.CreatedAt), c.Winner)).ToList();

        var notifications = (await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == id)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new { n.Type, n.CreatedAt })
            .ToListAsync(ct)).Select(n => new ExportNotificationDto(n.Type, Utc(n.CreatedAt))).ToList();

        return new ExportDto(now, account, checks, posts, comments, follows, followers, comparisons, blocks, notifications);
    }

    /// <summary>One read of a handle-and-since query, newest first, the times marked UTC as the rest of the API does.</summary>
    private static async Task<List<ExportHandleDto>> HandlesAsync(IQueryable<ExportHandleDto> rows, CancellationToken ct) =>
        (await rows.ToListAsync(ct)).OrderByDescending(h => h.Since).Select(h => h with { Since = Utc(h.Since) }).ToList();

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
