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

        var rows = await db.Notifications
            .Where(n => n.UserId == me.Id)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        // Notifications store the actor's handle; the display name is looked up now so renames show through.
        var handles = rows.Select(n => n.ActorHandle.ToLowerInvariant()).Distinct().ToList();
        var actors = await db.Users
            .Where(u => handles.Contains(u.HandleLower))
            .Select(u => new { u.HandleLower, u.Handle, u.DisplayName, u.AvatarPath, u.AvatarVersion })
            .ToDictionaryAsync(u => u.HandleLower, u => (Name: PostReader.NameOf(u.Handle, u.DisplayName), Avatar: PostReader.AvatarUrl(u.Handle, u.AvatarPath, u.AvatarVersion)), ct);

        var items = rows.Select(n =>
        {
            var found = actors.TryGetValue(n.ActorHandle.ToLowerInvariant(), out var actor);
            return new NotificationDto(
                n.Id, n.Type, n.ActorHandle, found ? actor.Name : n.ActorHandle, found ? actor.Avatar : null,
                n.PostId, n.ChallengeId, DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc), n.ReadAt != null);
        }).ToList();
        var unread = await db.Notifications.CountAsync(n => n.UserId == me.Id && n.ReadAt == null, ct);
        return Results.Json(new NotificationsDto(items, unread), AppJson.Options);
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
