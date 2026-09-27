using System.Text.Json;
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
        new(user.Handle, NameOf(user.Handle, user.DisplayName), user.AccountType.ToString(), AvatarUrl(user.Handle, user.AvatarPath, user.AvatarVersion), user.Verified);

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
            .Select(u => new { u.Id, u.Handle, u.DisplayName, u.AccountType, u.AvatarPath, u.AvatarVersion, u.Verified })
            .ToDictionaryAsync(u => u.Id, u => new UserRefDto(u.Handle, NameOf(u.Handle, u.DisplayName), u.AccountType.ToString(), AvatarUrl(u.Handle, u.AvatarPath, u.AvatarVersion), u.Verified), ct);
    }

    /// <summary>
    /// Round 14 — post the look, keep the grade: whether this viewer may read a look's number. The author always may;
    /// a moderator may (<paramref name="viewerIsModerator"/>, which only the moderation queue passes); everyone else
    /// sees no number on a look whose author kept the grade private. It is the only question asked about the flag, so
    /// every route that returns a look answers it the same way.
    /// </summary>
    private static bool MayReadScore(Post post, Guid? viewerId, bool viewerIsModerator) =>
        !post.ScorePrivate || viewerIsModerator || (viewerId is Guid viewer && viewer == post.UserId);

    public async Task<List<PostDto>> ToDtosAsync(
        IReadOnlyList<Post> posts, Guid? viewerId, CancellationToken ct, IReadOnlyDictionary<Guid, int>? votes = null,
        bool viewerIsModerator = false)
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

        // Which looks carry a clip, and what the stylist said of each piece (Round 21: the verdict dot beside a name on
        // the card): one query over the page's checks, never one per post.
        var checkIds = posts.Select(p => p.CheckId).Distinct().ToList();
        var checks = await db.Checks
            .Where(c => checkIds.Contains(c.Id))
            .Select(c => new { c.Id, c.VideoPath, c.Status, c.FeedbackJson })
            .ToListAsync(ct);
        var withClip = checks.Where(c => !string.IsNullOrEmpty(c.VideoPath)).Select(c => c.Id).ToHashSet();
        var verdicts = checks.ToDictionary(c => c.Id, c => VerdictsOf(c.Status, c.FeedbackJson));

        // "After the tip": the earlier look's score and photo, one query over the page's before ids. A before look
        // under review is left off (its photo answers 404 to everyone else); one that was deleted left null behind.
        var beforeIds = posts.Where(p => p.BeforePostId != null).Select(p => p.BeforePostId!.Value).Distinct().ToList();
        var befores = beforeIds.Count == 0
            ? new Dictionary<Guid, BeforeDto>()
            : await db.Posts
                .Where(p => beforeIds.Contains(p.Id) && !p.Hidden)
                .Select(p => new { p.Id, p.Score, p.ScorePrivate, p.UserId })
                // Round 14: the earlier look's own choice decides its number on the strip; the thumbnail and the link stay.
                .ToDictionaryAsync(
                    p => p.Id,
                    p => new BeforeDto(
                        p.Id,
                        !p.ScorePrivate || viewerIsModerator || (viewerId is Guid v && v == p.UserId) ? p.Score : null,
                        $"/api/posts/{p.Id}/image"),
                    ct);

        // The pieces on each look, in their order (Round 10): every card carries them, so the tag toggle, the item sheet and
        // the item search need no second read. One query over the page.
        var items = (await db.PostItems.Where(i => postIds.Contains(i.PostId)).OrderBy(i => i.Position).ToListAsync(ct))
            .GroupBy(i => i.PostId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return posts.Select(p =>
        {
            var user = users.GetValueOrDefault(p.UserId) ?? new UserRefDto("?", "?", AccountType.Person.ToString());
            var said = verdicts.GetValueOrDefault(p.CheckId);
            var pieces = (items.GetValueOrDefault(p.Id) ?? []).Select(i => ItemDto(i, said)).ToList();
            // Round 14 — post the look, keep the grade: one question, asked once, for the three numbers the verdict is.
            var score = MayReadScore(p, viewerId, viewerIsModerator);
            return new PostDto(
                p.Id,
                user,
                p.Intent,
                score ? p.Score : null,
                score ? p.IntentMatch : null,
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
                score && p.FitScore is int fit && p.ColorScore is int color && p.AccessoriesScore is int accessories ? new BreakdownDto(fit, color, accessories) : null,
                p.BeforePostId is Guid beforeId ? befores.GetValueOrDefault(beforeId) : null,
                pieces,
                pieces.Count,
                p.ScorePrivate);
        }).ToList();
    }

    /// <summary>
    /// One piece as the wire carries it: the raw link for the owner's sheet, its host for "Shop at {host}", and the
    /// stylist's verdict when <paramref name="verdicts"/> (the check's, by stored name) has one for this name.
    /// </summary>
    public static PostItemDto ItemDto(PostItem item, IReadOnlyDictionary<string, string>? verdicts = null) =>
        new(item.Id, item.Name, item.Category, item.Brand, item.Model, item.Url, PostItems.HostOf(item.Url), item.Source, item.X, item.Y, item.Confirmed,
            verdicts is not null && verdicts.TryGetValue(item.Name, out var verdict) ? verdict : null);

    /// <summary>
    /// The verdict behind each piece a check named, keyed by the name exactly as <see cref="PostItems"/> stores a row
    /// (lower-cased, one space between words, cut to the column), which is how a row typed back in any case or spacing
    /// still finds its own. Only the stylist's three words are kept; the first of two pieces with one name is the row
    /// the posting kept. Empty for a check that is not ok, or whose stored feedback cannot be read.
    /// </summary>
    public static Dictionary<string, string> VerdictsOf(string status, string? feedbackJson)
    {
        var verdicts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (status != CheckStatus.Ok || string.IsNullOrEmpty(feedbackJson))
        {
            return verdicts;
        }

        OutfitFeedback? feedback;
        try
        {
            feedback = JsonSerializer.Deserialize<OutfitFeedback>(feedbackJson, AppJson.Options);
        }
        catch (JsonException)
        {
            return verdicts;
        }

        foreach (var item in feedback?.Items ?? [])
        {
            var name = PostItems.NormalizeName(item.Name);
            if (name.Length > 0 && Array.IndexOf(OutfitAnalyzer.Verdicts, item.Verdict) >= 0)
            {
                verdicts.TryAdd(name, item.Verdict);
            }
        }

        return verdicts;
    }
}
