using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Web Push: a browser registers its push subscription for the signed-in account, removes it, asks whether the account
/// has any, and can ask for a test ping. The public key itself travels on /api/config. Every route needs a session and,
/// being non-GET, the X-Requested-With header.
/// </summary>
public static class PushEndpoints
{
    public const int EndpointMaxLength = 1000;
    public const int P256dhMaxLength = 200;
    public const int AuthMaxLength = 100;

    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/push").RequireAuthorization();
        group.MapGet("/state", StateAsync);
        group.MapPost("/subscriptions", SubscribeAsync);
        group.MapDelete("/subscriptions", UnsubscribeAsync);
        group.MapPost("/test", TestAsync);
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    /// <summary>
    /// A browser's subscription looks like this or it is useless: an https push service URL, the client's uncompressed
    /// P-256 point (65 bytes) and its 16-byte auth secret, both base64url. Everything else is refused before it is stored.
    /// </summary>
    public static bool IsValidSubscription(string? endpoint, string? p256dh, string? auth)
    {
        if (!UserEndpoints.IsHttpsUrl(endpoint, EndpointMaxLength))
        {
            return false;
        }

        if (p256dh is null || p256dh.Length > P256dhMaxLength || auth is null || auth.Length > AuthMaxLength)
        {
            return false;
        }

        var point = PushSender.FromBase64Url(p256dh);
        var secret = PushSender.FromBase64Url(auth);
        return point is { Length: 65 } && point[0] == 0x04 && secret is { Length: 16 };
    }

    private static async Task<IResult> StateAsync(HttpContext context, AppDbContext db, PushSender push, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        return Results.Json(await StateOfAsync(db, push, me.Id, ct), AppJson.Options);
    }

    private static async Task<IResult> SubscribeAsync(
        PushSubscribeRequest body, HttpContext context, AppDbContext db, PushSender push, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        if (!push.Enabled)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.push_disabled"));
        }

        var endpoint = body.Endpoint?.Trim();
        var p256dh = body.P256dh?.Trim();
        var auth = body.Auth?.Trim();
        if (!IsValidSubscription(endpoint, p256dh, auth))
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.push_invalid"));
        }

        // One row per browser endpoint. A browser that signs into another account takes its subscription along: the
        // person holding the phone is the one who should get its pings.
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = me.Id,
                Endpoint = endpoint!,
                P256dh = p256dh!,
                Auth = auth!,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.UserId = me.Id;
            existing.P256dh = p256dh!;
            existing.Auth = auth!;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two subscribes for the same endpoint at once: the other one won the unique index; re-own on top of it.
            db.ChangeTracker.Clear();
            await db.PushSubscriptions.Where(s => s.Endpoint == endpoint)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, me.Id).SetProperty(x => x.P256dh, p256dh!).SetProperty(x => x.Auth, auth!), ct);
        }

        return Results.Json(await StateOfAsync(db, push, me.Id, ct), AppJson.Options);
    }

    /// <summary>204 whether or not the endpoint was known: the browser side is already gone, the server must not argue.</summary>
    private static async Task<IResult> UnsubscribeAsync(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PushSubscribeRequest? body,
        [FromQuery] string? endpoint, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var target = (body?.Endpoint ?? endpoint)?.Trim();
        if (!string.IsNullOrEmpty(target))
        {
            // Only the account that owns the row may drop it; another account's endpoint is simply not found.
            await db.PushSubscriptions.Where(s => s.Endpoint == target && s.UserId == me.Id).ExecuteDeleteAsync(ct);
        }

        return Results.NoContent();
    }

    /// <summary>A "your look caught fire" ping to the caller's own browsers, so the person can see what a push looks like.</summary>
    private static async Task<IResult> TestAsync(HttpContext context, AppDbContext db, PushSender push, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        if (!push.Enabled)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.push_disabled"));
        }

        push.Enqueue(new PushJob(me.Id, NotificationType.Fire, me.Handle, null, null));
        return Results.Accepted();
    }

    private static async Task<PushStateDto> StateOfAsync(AppDbContext db, PushSender push, Guid userId, CancellationToken ct) =>
        new(push.Enabled, await db.PushSubscriptions.AnyAsync(s => s.UserId == userId, ct));
}
