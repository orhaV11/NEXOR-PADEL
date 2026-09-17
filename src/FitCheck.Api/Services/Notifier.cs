using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// Queues in-app notifications on the current unit of work. The caller saves. Every row also becomes a push job for
/// <see cref="PushSender"/>, which goes out in the background and neither blocks nor fails the request. No line crosses
/// a block (Round 11): both ways in — <see cref="AddAsync"/> and <see cref="AddOnceAsync"/> — drop a notification whose
/// actor is on either side of one with the person, before the row is written and before the push job is queued, so the
/// lock screen never names the other account either. A line the person is their own actor in (a board place) is not a
/// pair and always goes.
/// </summary>
public sealed class Notifier(AppDbContext db, PushSender push, Blocks blocks)
{
    /// <summary>Rank: the place on the board, for <see cref="NotificationType.BoardRank"/> only (the actor there is the person themselves).</summary>
    public async Task AddAsync(
        Guid userId, string type, string actorHandle, Guid? postId, Guid? challengeId, CancellationToken ct, int? rank = null)
    {
        if (await blocks.BetweenAsync(userId, actorHandle, ct))
        {
            return;
        }

        Write(userId, type, actorHandle, postId, challengeId, rank);
    }

    /// <summary>Same actor, same post, same type: one notification, not one per tap. None at all across a block.</summary>
    public async Task AddOnceAsync(Guid userId, string type, string actorHandle, Guid? postId, Guid? challengeId, CancellationToken ct)
    {
        if (await blocks.BetweenAsync(userId, actorHandle, ct))
        {
            return;
        }

        var exists = await db.Notifications.AnyAsync(
            n => n.UserId == userId && n.Type == type && n.ActorHandle == actorHandle && n.PostId == postId && n.ChallengeId == challengeId, ct);
        if (!exists)
        {
            Write(userId, type, actorHandle, postId, challengeId, null);
        }
    }

    /// <summary>The row and the push job that announces it, once the pair has been cleared. The one place either is made.</summary>
    private void Write(Guid userId, string type, string actorHandle, Guid? postId, Guid? challengeId, int? rank)
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
}
