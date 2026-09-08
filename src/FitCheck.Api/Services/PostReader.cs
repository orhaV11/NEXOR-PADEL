using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>Turns a page of posts into DTOs with a handful of batched lookups, whatever the viewer's state.</summary>
public sealed class PostReader(AppDbContext db)
{
    public static string NameOf(string handle, string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? handle : displayName!;

    /// <summary>The only avatar URL shape. Versioned so clients can cache it for a day.</summary>
    public static string? AvatarUrl(string handle, string? avatarPath, int avatarVersion) =>
        string.IsNullOrEmpty(avatarPath) ? null : $"/api/users/{Uri.EscapeDataString(handle)}/avatar?v={avatarVersion}";

    public static UserRefDto Ref(AppUser user) =>
        new(user.Handle, NameOf(user.Handle, user.DisplayName), user.AccountType.ToString(), AvatarUrl(user.Handle, user.AvatarPath, user.AvatarVersion));

    /// <summary>User refs for a set of ids in one query. Missing ids are simply absent.</summary>
    public async Task<Dictionary<Guid, UserRefDto>> RefsAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Handle, u.DisplayName, u.AccountType, u.AvatarPath, u.AvatarVersion })
            .ToDictionaryAsync(u => u.Id, u => new UserRefDto(u.Handle, NameOf(u.Handle, u.DisplayName), u.AccountType.ToString(), AvatarUrl(u.Handle, u.AvatarPath, u.AvatarVersion)), ct);
    }

    public async Task<List<PostDto>> ToDtosAsync(
        IReadOnlyList<Post> posts, Guid? viewerId, CancellationToken ct, IReadOnlyDictionary<Guid, int>? votes = null)
    {
        if (posts.Count == 0)
        {
            return [];
        }

        var postIds = posts.Select(p => p.Id).ToList();
        var tags = (await db.PostTags.Where(t => postIds.Contains(t.PostId)).ToListAsync(ct))
            .GroupBy(t => t.PostId).ToDictionary(g => g.Key, g => g.Select(t => t.Tag).OrderBy(t => t, StringComparer.Ordinal).ToList());
        var mentionRows = await db.PostMentions.Where(m => postIds.Contains(m.PostId)).ToListAsync(ct);

        // Authors, mentioned accounts and featuring brands in one lookup.
        var userIds = posts.Select(p => p.UserId)
            .Concat(mentionRows.Select(m => m.UserId))
            .Concat(posts.Where(p => p.FeaturedByBrandId != null).Select(p => p.FeaturedByBrandId!.Value));
        var users = await RefsAsync(userIds, ct);
        var mentions = mentionRows.GroupBy(m => m.PostId)
            .ToDictionary(g => g.Key, g => g.Select(m => users.GetValueOrDefault(m.UserId)).Where(u => u is not null).Select(u => u!).OrderBy(u => u.Handle, StringComparer.Ordinal).ToList());

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

        // Which looks carry a clip: one query over the page's checks, never one per post.
        var checkIds = posts.Select(p => p.CheckId).Distinct().ToList();
        var withClip = (await db.Checks
                .Where(c => checkIds.Contains(c.Id) && c.VideoPath != null && c.VideoPath != "")
                .Select(c => c.Id)
                .ToListAsync(ct))
            .ToHashSet();

        return posts.Select(p =>
        {
            var user = users.GetValueOrDefault(p.UserId) ?? new UserRefDto("?", "?", AccountType.Person.ToString());
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
                DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
                tags.GetValueOrDefault(p.Id) ?? [],
                mentions.GetValueOrDefault(p.Id) ?? [],
                p.FeaturedByBrandId is Guid brandId ? users.GetValueOrDefault(brandId) : null,
                withClip.Contains(p.CheckId) ? $"/api/posts/{p.Id}/video" : null,
                // The three columns are written together at posting time; a look from before rubric v2 has none.
                p.FitScore is int fit && p.ColorScore is int color && p.AccessoriesScore is int accessories ? new BreakdownDto(fit, color, accessories) : null);
        }).ToList();
    }
}
