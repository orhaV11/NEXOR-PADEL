using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>Queues in-app notifications on the current unit of work. The caller saves.</summary>
public sealed class Notifier(AppDbContext db)
{
    public void Add(Guid userId, string type, string actorHandle, Guid? postId = null, Guid? challengeId = null)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            ActorHandle = actorHandle,
            PostId = postId,
            ChallengeId = challengeId,
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>Same actor, same post, same type: one notification, not one per tap.</summary>
    public async Task AddOnceAsync(Guid userId, string type, string actorHandle, Guid? postId, Guid? challengeId, CancellationToken ct)
    {
        var exists = await db.Notifications.AnyAsync(
            n => n.UserId == userId && n.Type == type && n.ActorHandle == actorHandle && n.PostId == postId && n.ChallengeId == challengeId, ct);
        if (!exists)
        {
            Add(userId, type, actorHandle, postId, challengeId);
        }
    }
}
