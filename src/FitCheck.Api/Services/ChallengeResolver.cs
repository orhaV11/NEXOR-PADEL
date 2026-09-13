using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// Fixes a challenge's winner the first time it is read after its end. No scheduler needed: nobody can see
/// a result before somebody asks for it, and the conditional update makes sure only one reader does the work.
/// </summary>
public static class ChallengeResolver
{
    public static async Task<bool> ResolveIfEndedAsync(AppDbContext db, Notifier notifier, Challenge challenge, DateTime now, CancellationToken ct)
    {
        if (challenge.ResolvedAt is not null || challenge.EndsAt > now)
        {
            return false;
        }

        var entries = await db.Posts
            .Where(p => p.ChallengeId == challenge.Id && !p.Hidden)
            .Select(p => new { p.Id, p.UserId, p.CreatedAt })
            .ToListAsync(ct);
        var votes = await db.ChallengeVotes
            .Where(v => v.ChallengeId == challenge.Id)
            .GroupBy(v => v.PostId)
            .Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var counts = votes.ToDictionary(v => v.PostId, v => v.Count);

        // Most votes wins; the earlier entry wins a tie, so nobody can snipe by entering late.
        var winner = entries
            .OrderByDescending(e => counts.GetValueOrDefault(e.Id))
            .ThenBy(e => e.CreatedAt)
            .FirstOrDefault();

        var claimed = await db.Challenges
            .Where(c => c.Id == challenge.Id && c.ResolvedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.ResolvedAt, now)
                .SetProperty(c => c.WinnerPostId, winner == null ? null : winner.Id), ct);

        // Whoever won the claim wrote the row; everyone reads it back rather than trusting their own pick.
        await db.Entry(challenge).ReloadAsync(ct);
        if (claimed == 0)
        {
            return false;
        }

        var brand = await db.Users.FindAsync([challenge.BrandId], ct);
        if (winner is not null)
        {
            var winnerUser = await db.Users.FindAsync([winner.UserId], ct);
            if (winnerUser is not null && brand is not null)
            {
                notifier.Add(winnerUser.Id, NotificationType.Won, brand.Handle, winner.Id, challenge.Id);
                notifier.Add(brand.Id, NotificationType.Ended, winnerUser.Handle, winner.Id, challenge.Id);
            }
        }
        else if (brand is not null)
        {
            notifier.Add(brand.Id, NotificationType.Ended, brand.Handle, null, challenge.Id);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
