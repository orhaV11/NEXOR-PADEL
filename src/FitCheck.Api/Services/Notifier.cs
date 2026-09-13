using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// Queues in-app notifications on the current unit of work. The caller saves. Every row also becomes a push job for
/// <see cref="PushSender"/>, which goes out in the background and neither blocks nor fails the request.
/// </summary>
public sealed class Notifier(AppDbContext db, PushSender push)
{
    /// <summary>Rank: the place on the board, for <see cref="NotificationType.BoardRank"/> only (the actor there is the person themselves).</summary>
    public void Add(Guid userId, string type, string actorHandle, Guid? postId = null, Guid? challengeId = null, int? rank = null)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            ActorHandle = actorHandle,
            PostId = postId,
            ChallengeId = challengeId,
            Rank = rank,
            CreatedAt = DateTime.UtcNow
        };
        db.Notifications.Add(notification);
        // The job carries the row's id: the worker sends only once the row is committed, and never for a request that rolled back.
        push.Enqueue(new PushJob(userId, type, actorHandle, postId, challengeId, notification.Id, rank));
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
