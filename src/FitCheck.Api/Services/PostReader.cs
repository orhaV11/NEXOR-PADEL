using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>Turns a page of posts into DTOs with four batched lookups, whatever the viewer's state.</summary>
public sealed class PostReader(AppDbContext db)
{
    public static string NameOf(string handle, string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? handle : displayName!;

    public async Task<List<PostDto>> ToDtosAsync(
        IReadOnlyList<Post> posts, Guid? viewerId, CancellationToken ct, IReadOnlyDictionary<Guid, int>? votes = null)
    {
        if (posts.Count == 0)
        {
            return [];
        }

        var postIds = posts.Select(p => p.Id).ToList();
        var userIds = posts.Select(p => p.UserId).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Handle, u.DisplayName, u.AccountType })
            .ToDictionaryAsync(u => u.Id, ct);

        var challengeIds = posts.Where(p => p.ChallengeId != null).Select(p => p.ChallengeId!.Value).Distinct().ToList();
        var challengeTitles = challengeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Challenges.Where(c => challengeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);

        var fired = new HashSet<Guid>();
        var saved = new HashSet<Guid>();
        if (viewerId is Guid viewer)
        {
            fired = (await db.Fires.Where(f => f.UserId == viewer && postIds.Contains(f.PostId)).Select(f => f.PostId).ToListAsync(ct)).ToHashSet();
            saved = (await db.SavedPosts.Where(s => s.UserId == viewer && postIds.Contains(s.PostId)).Select(s => s.PostId).ToListAsync(ct)).ToHashSet();
        }

        var links = (await db.ProductLinks.Where(l => postIds.Contains(l.PostId)).OrderBy(l => l.Position).ToListAsync(ct))
            .GroupBy(l => l.PostId)
            .ToDictionary(g => g.Key, g => g.Select(l => new ProductLinkDto(l.Label, l.Url, l.Price)).ToList());

        return posts.Select(p =>
        {
            users.TryGetValue(p.UserId, out var u);
            var user = u is null
                ? new UserRefDto("?", "?", AccountType.Person.ToString())
                : new UserRefDto(u.Handle, NameOf(u.Handle, u.DisplayName), u.AccountType.ToString());
            return new PostDto(
                p.Id,
                user,
                p.Intent,
                p.Score,
                p.IntentMatch,
                p.Headline,
                p.Caption,
                p.ChallengeId,
                p.ChallengeId is Guid cid && challengeTitles.TryGetValue(cid, out var title) ? title : null,
                p.FireCount,
                p.CommentCount,
                fired.Contains(p.Id),
                saved.Contains(p.Id),
                viewerId is not null && p.UserId == viewerId,
                p.Hidden,
                votes is not null && votes.TryGetValue(p.Id, out var v) ? v : 0,
                links.TryGetValue(p.Id, out var l) ? l : [],
                $"/api/posts/{p.Id}/image",
                DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc));
        }).ToList();
    }
}
