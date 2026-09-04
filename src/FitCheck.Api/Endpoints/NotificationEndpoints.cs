using FitCheck.Api.Data;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapPost("/read", MarkReadAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var items = await db.Notifications
            .Where(n => n.UserId == me.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationDto(n.Id, n.Type, n.ActorHandle, n.PostId, n.ChallengeId, n.CreatedAt, n.ReadAt != null))
            .ToListAsync(ct);
        var unread = await db.Notifications.CountAsync(n => n.UserId == me.Id && n.ReadAt == null, ct);
        var dto = new NotificationsDto(
            items.Select(n => n with { CreatedAt = DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc) }).ToList(), unread);
        return Results.Json(dto, AppJson.Options);
    }

    private static async Task<IResult> MarkReadAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var now = DateTime.UtcNow;
        await db.Notifications.Where(n => n.UserId == me.Id && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return Results.NoContent();
    }
}
