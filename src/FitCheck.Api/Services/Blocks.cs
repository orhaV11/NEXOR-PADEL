using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>
/// The one place that answers "is there a block between these two" (Round 11). A <see cref="Block"/> row is one direction,
/// and every filter reads it both ways: whoever tapped, neither side sees or reaches the other. Scoped per request, so a
/// viewer's set of hidden accounts is read once however many routes and lists ask for it; a signed-out viewer hides
/// nothing. The blocked person is never told: the filters leave things out and refuse with error.blocked, and nothing here
/// says which side acted.
/// </summary>
public sealed class Blocks(AppDbContext db)
{
    private readonly Dictionary<Guid, HashSet<Guid>> _hidden = new();
    private readonly Dictionary<Guid, List<string>> _hiddenHandles = new();

    /// <summary>The accounts hidden from this viewer: everyone they blocked and everyone who blocked them. Empty signed out.</summary>
    public async Task<IReadOnlySet<Guid>> HiddenFromAsync(Guid? viewerId, CancellationToken ct)
    {
        if (viewerId is not Guid viewer)
        {
            return new HashSet<Guid>();
        }

        if (!_hidden.TryGetValue(viewer, out var set))
        {
            var ids = await db.Blocks
                .Where(b => b.BlockerId == viewer || b.BlockedId == viewer)
                .Select(b => b.BlockerId == viewer ? b.BlockedId : b.BlockerId)
                .ToListAsync(ct);
            set = ids.ToHashSet();
            _hidden[viewer] = set;
        }

        return set;
    }

    /// <summary>Whether either account blocked the other. An account is never shut from itself.</summary>
    public async Task<bool> BetweenAsync(Guid a, Guid b, CancellationToken ct) =>
        a != b && (await HiddenFromAsync(a, ct)).Contains(b);

    /// <summary>
    /// One direction only: whether the first account blocked the second. The single question that may be answered to a
    /// person about their own act (ViewerProfileDto.Blocked); the other direction is never asked on anyone's behalf.
    /// </summary>
    public Task<bool> HasBlockedAsync(Guid blockerId, Guid blockedId, CancellationToken ct) =>
        db.Blocks.AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId, ct);

    /// <summary>The same question when the other side is known by handle only (a notification's actor), case-insensitively.</summary>
    public async Task<bool> BetweenAsync(Guid userId, string otherHandle, CancellationToken ct)
    {
        var hidden = await HiddenFromAsync(userId, ct);
        if (hidden.Count == 0)
        {
            return false;
        }

        var lower = otherHandle.ToLowerInvariant();
        var otherId = await db.Users.Where(u => u.HandleLower == lower).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        return otherId is Guid id && hidden.Contains(id);
    }

    /// <summary>
    /// The handles of the hidden accounts, as notifications name their actor. Read once per viewer, like the ids.
    /// </summary>
    public async Task<IReadOnlyList<string>> HiddenHandlesAsync(Guid? viewerId, CancellationToken ct)
    {
        if (viewerId is not Guid viewer)
        {
            return [];
        }

        if (!_hiddenHandles.TryGetValue(viewer, out var handles))
        {
            var hidden = await HiddenFromAsync(viewer, ct);
            handles = hidden.Count == 0 ? [] : await db.Users.Where(u => hidden.Contains(u.Id)).Select(u => u.Handle).ToListAsync(ct);
            _hiddenHandles[viewer] = handles;
        }

        return handles;
    }

    /// <summary>Looks by accounts hidden from the viewer leave the query. Untouched for a signed-out viewer or one with no blocks.</summary>
    public async Task<IQueryable<Post>> FilterAsync(IQueryable<Post> posts, Guid? viewerId, CancellationToken ct)
    {
        var hidden = await HiddenFromAsync(viewerId, ct);
        if (hidden.Count == 0)
        {
            return posts;
        }

        var ids = hidden.ToList();
        return posts.Where(p => !ids.Contains(p.UserId));
    }

    /// <summary>Accounts hidden from the viewer leave the query (Explore's brands, the people in a search).</summary>
    public async Task<IQueryable<AppUser>> FilterAsync(IQueryable<AppUser> users, Guid? viewerId, CancellationToken ct)
    {
        var hidden = await HiddenFromAsync(viewerId, ct);
        if (hidden.Count == 0)
        {
            return users;
        }

        var ids = hidden.ToList();
        return users.Where(u => !ids.Contains(u.Id));
    }

    /// <summary>Comments by accounts hidden from the viewer leave the query: a blocked person's words stay in the row, unseen.</summary>
    public async Task<IQueryable<Comment>> FilterAsync(IQueryable<Comment> comments, Guid? viewerId, CancellationToken ct)
    {
        var hidden = await HiddenFromAsync(viewerId, ct);
        if (hidden.Count == 0)
        {
            return comments;
        }

        var ids = hidden.ToList();
        return comments.Where(c => !ids.Contains(c.UserId));
    }
}
