using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Web Push: a browser registers its push subscription for the signed-in account, removes it, asks whether the account
/// has any, and can ask for a test ping. The public key itself travels on /api/config. Every route needs a session and,
/// being non-GET, the X-Requested-With header. Round 20: <c>GET/POST /api/push/morning</c> is the person's own switch on
/// the morning "your outfit for today is one tap away" push (<see cref="TomorrowMorning"/>), beside whether this server
/// offers it at all and the local hour it goes at.
/// </summary>
public static class PushEndpoints
{
    public const int EndpointMaxLength = 1000;
    public const int P256dhMaxLength = 200;
    public const int AuthMaxLength = 100;

    /// <summary>Browsers per account. A person has a phone, a laptop, maybe a tablet; past ten it is a script, and the oldest row goes.</summary>
    public const int MaxPerAccount = 10;

    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/push").RequireAuthorization();
        group.MapGet("/state", StateAsync);
        group.MapPost("/subscriptions", SubscribeAsync);
        group.MapDelete("/subscriptions", UnsubscribeAsync);
        group.MapPost("/test", TestAsync);
        group.MapGet("/morning", MorningStateAsync);
        group.MapPost("/morning", SetMorningStateAsync);
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    /// <summary>
    /// A browser's subscription looks like this or it is useless: an https push service URL on a real host name, the client's
    /// uncompressed P-256 point (65 bytes) and its 16-byte auth secret, both base64url. Everything else is refused before it
    /// is stored.
    /// </summary>
    public static bool IsValidSubscription(string? endpoint, string? p256dh, string? auth)
    {
        if (!UserEndpoints.IsHttpsUrl(endpoint, EndpointMaxLength) || !IsPushServiceHost(new Uri(endpoint!)))
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

    /// <summary>
    /// The push services live on public DNS names (fcm.googleapis.com, web.push.apple.com, updates.push.services.mozilla.com,
    /// ...). A literal address, localhost or a single-label name is not one of them; it is a way of making this server post
    /// signed requests at something on its own network, and it is refused.
    /// </summary>
    public static bool IsPushServiceHost(Uri endpoint) =>
        endpoint.HostNameType == UriHostNameType.Dns
        && !endpoint.IsLoopback
        && !string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        && endpoint.Host.Trim('.').Contains('.');

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

        // At most MaxPerAccount browsers per account: with this one about to be the newest, the oldest of the others make room.
        var others = db.PushSubscriptions.Where(s => s.UserId == me.Id && s.Endpoint != endpoint);
        var excess = await others.CountAsync(ct) - (MaxPerAccount - 1);
        if (excess > 0)
        {
            db.PushSubscriptions.RemoveRange(await others.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).Take(excess).ToListAsync(ct));
        }

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

    /// <summary>The morning switch as it stands, and whether anything could come of it on this server.</summary>
    private static async Task<IResult> MorningStateAsync(HttpContext context, AppDbContext db, PushSender push, Localizer localizer, IOptions<PlanOptions> plans, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        return me is null ? failure! : Results.Json(MorningStateOf(me, push, plans.Value), AppJson.Options);
    }

    /// <summary>
    /// The person's own switch. 400 error.invalid_request without an answer; 400 error.tomorrow_push_off while the server does
    /// not offer the push (the flag, Tomorrow or push itself is off), so the switch is never a promise nothing keeps.
    /// </summary>
    private static async Task<IResult> SetMorningStateAsync(
        MorningPushRequest? body, HttpContext context, AppDbContext db, PushSender push, Localizer localizer, IOptions<PlanOptions> plans, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        if (body?.On is not { } on)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.invalid_request"));
        }

        if (!MorningOffered(push, plans.Value))
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.tomorrow_push_off"));
        }

        me.TomorrowPushOn = on;
        await db.SaveChangesAsync(ct);
        return Results.Json(MorningStateOf(me, push, plans.Value), AppJson.Options);
    }

    /// <summary>The server offers the morning push when its flag is on, Tomorrow is on (a tap would otherwise 404) and push has keys.</summary>
    public static bool MorningOffered(PushSender push, PlanOptions plans) => plans.TomorrowMorningPush && plans.TomorrowEnabled && push.Enabled;

    private static MorningPushStateDto MorningStateOf(AppUser me, PushSender push, PlanOptions plans) =>
        new(me.TomorrowPushOn, MorningOffered(push, plans), TomorrowMorning.HourOf(plans).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));

    private static async Task<PushStateDto> StateOfAsync(AppDbContext db, PushSender push, Guid userId, CancellationToken ct) =>
        new(push.Enabled, await db.PushSubscriptions.AnyAsync(s => s.UserId == userId, ct));
}
