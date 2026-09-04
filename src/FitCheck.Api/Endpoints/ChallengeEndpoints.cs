using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

public static class ChallengeEndpoints
{
    private static readonly TimeSpan MinDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromDays(60);

    public static IEndpointRouteBuilder MapChallengeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/challenges");
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync).RequireAuthorization();
        group.MapPost("/{id:guid}/vote", VoteAsync).RequireAuthorization();
        group.MapDelete("/{id:guid}/vote", UnvoteAsync).RequireAuthorization();
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    private sealed record Loaded(
        Dictionary<Guid, List<Post>> EntriesByChallenge,
        Dictionary<Guid, int> VotesByPost,
        Dictionary<Guid, UserRefDto> Brands,
        Dictionary<Guid, Guid> ViewerVotes);

    /// <summary>One pass of batched queries for a set of challenges: entries, vote counts, brands, the viewer's votes.</summary>
    private static async Task<Loaded> LoadAsync(AppDbContext db, List<Challenge> challenges, Guid? viewerId, CancellationToken ct)
    {
        var ids = challenges.Select(c => c.Id).ToList();
        var entries = await db.Posts
            .Where(p => p.ChallengeId != null && ids.Contains(p.ChallengeId.Value) && !p.Hidden)
            .ToListAsync(ct);
        var votes = await db.ChallengeVotes
            .Where(v => ids.Contains(v.ChallengeId))
            .GroupBy(v => v.PostId)
            .Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PostId, g => g.Count, ct);
        var brandIds = challenges.Select(c => c.BrandId).Distinct().ToList();
        var brands = await db.Users.Where(u => brandIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Handle, u.DisplayName, u.AccountType })
            .ToDictionaryAsync(u => u.Id, u => new UserRefDto(u.Handle, PostReader.NameOf(u.Handle, u.DisplayName), u.AccountType.ToString()), ct);
        var viewerVotes = viewerId is Guid me
            ? await db.ChallengeVotes.Where(v => v.UserId == me && ids.Contains(v.ChallengeId)).ToDictionaryAsync(v => v.ChallengeId, v => v.PostId, ct)
            : new Dictionary<Guid, Guid>();

        var byChallenge = entries
            .GroupBy(p => p.ChallengeId!.Value)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(p => votes.GetValueOrDefault(p.Id))
                .ThenBy(p => p.CreatedAt)
                .ToList());
        return new Loaded(byChallenge, votes, brands, viewerVotes);
    }

    private static async Task<ChallengeDto> ToDtoAsync(
        Challenge challenge, Loaded loaded, PostReader reader, Guid? viewerId, DateTime now, int topCount, CancellationToken ct)
    {
        var entries = loaded.EntriesByChallenge.GetValueOrDefault(challenge.Id) ?? [];
        var top = await reader.ToDtosAsync(entries.Take(topCount).ToList(), viewerId, ct, loaded.VotesByPost);
        var myEntry = viewerId is null ? null : entries.FirstOrDefault(p => p.UserId == viewerId);
        return new ChallengeDto(
            challenge.Id,
            loaded.Brands.GetValueOrDefault(challenge.BrandId) ?? new UserRefDto("?", "?", "Brand"),
            challenge.Title,
            challenge.Brief,
            challenge.Intent,
            challenge.Prize,
            challenge.PrizeUrl,
            DateTime.SpecifyKind(challenge.EndsAt, DateTimeKind.Utc),
            IsOpen: challenge.ResolvedAt is null && challenge.EndsAt > now,
            Entries: entries.Count,
            Votes: entries.Sum(p => loaded.VotesByPost.GetValueOrDefault(p.Id)),
            challenge.WinnerPostId,
            top,
            new ChallengeViewerDto(
                IsBrand: viewerId == challenge.BrandId,
                HasEntered: myEntry is not null,
                VotedPostId: loaded.ViewerVotes.TryGetValue(challenge.Id, out var voted) ? voted : null,
                MyEntryId: myEntry?.Id),
            DateTime.SpecifyKind(challenge.CreatedAt, DateTimeKind.Utc));
    }

    private static async Task<IResult> ListAsync(
        HttpContext context, AppDbContext db, PostReader reader, Notifier notifier, string? state, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var now = DateTime.UtcNow;
        var ended = string.Equals(state, "ended", StringComparison.OrdinalIgnoreCase);
        var challenges = ended
            ? await db.Challenges.Where(c => c.EndsAt <= now).OrderByDescending(c => c.EndsAt).Take(50).ToListAsync(ct)
            : await db.Challenges.Where(c => c.EndsAt > now).OrderBy(c => c.EndsAt).Take(50).ToListAsync(ct);

        foreach (var challenge in challenges)
        {
            await ChallengeResolver.ResolveIfEndedAsync(db, notifier, challenge, now, ct);
        }

        var loaded = await LoadAsync(db, challenges, viewerId, ct);
        var dtos = new List<ChallengeDto>(challenges.Count);
        foreach (var challenge in challenges)
        {
            dtos.Add(await ToDtoAsync(challenge, loaded, reader, viewerId, now, 3, ct));
        }

        return Results.Json(dtos, AppJson.Options);
    }

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext context, AppDbContext db, PostReader reader, Notifier notifier, Localizer localizer, CancellationToken ct)
    {
        var viewerId = Sessions.UserId(context.User);
        var challenge = await db.Challenges.FindAsync([id], ct);
        if (challenge is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(Localizer.Resolve(null, context.Request), "error.challenge_not_found"));
        }

        var now = DateTime.UtcNow;
        await ChallengeResolver.ResolveIfEndedAsync(db, notifier, challenge, now, ct);
        var loaded = await LoadAsync(db, [challenge], viewerId, ct);
        var dto = await ToDtoAsync(challenge, loaded, reader, viewerId, now, 3, ct);
        var entries = await reader.ToDtosAsync(loaded.EntriesByChallenge.GetValueOrDefault(id) ?? [], viewerId, ct, loaded.VotesByPost);
        var winner = challenge.WinnerPostId is Guid w ? entries.FirstOrDefault(e => e.Id == w) : null;
        return Results.Json(new ChallengeDetailDto(dto, entries, winner), AppJson.Options);
    }

    private static async Task<IResult> CreateAsync(
        CreateChallengeRequest body, HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        if (me.AccountType != AccountType.Brand)
        {
            return Error(StatusCodes.Status403Forbidden, localizer.Get(me.PreferredLanguage, "error.brand_only"));
        }

        var now = DateTime.UtcNow;
        var title = OutfitAnalyzer.SanitizeOccasion(body.Title);
        var brief = (body.Brief ?? "").Trim();
        var prize = OutfitAnalyzer.SanitizeOccasion(body.Prize);
        var prizeUrl = string.IsNullOrWhiteSpace(body.PrizeUrl) ? null : body.PrizeUrl.Trim();
        var endsAt = body.EndsAt?.ToUniversalTime();
        var intentKnown = Enum.TryParse<StyleIntent>(body.Intent, ignoreCase: true, out var intent) && Enum.IsDefined(intent);
        var valid = title.Length is > 0 and <= 80
                    && brief.Length is > 0 and <= 500
                    && prize.Length is > 0 and <= 200
                    && (prizeUrl is null || UserEndpoints.IsHttpsUrl(prizeUrl, 500))
                    && intentKnown
                    && endsAt is DateTime e && e >= now + MinDuration && e <= now + MaxDuration;
        if (!valid)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.challenge_invalid"));
        }

        var challenge = new Challenge
        {
            Id = Guid.NewGuid(),
            BrandId = me.Id,
            Title = title,
            Brief = brief,
            Intent = intent,
            Prize = prize,
            PrizeUrl = prizeUrl,
            EndsAt = endsAt!.Value,
            CreatedAt = now
        };
        db.Challenges.Add(challenge);
        await db.SaveChangesAsync(ct);

        var loaded = await LoadAsync(db, [challenge], me.Id, ct);
        return Results.Json(await ToDtoAsync(challenge, loaded, reader, me.Id, now, 3, ct), AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> VoteAsync(
        Guid id, VoteRequest body, HttpContext context, AppDbContext db, Notifier notifier, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var challenge = await db.Challenges.FindAsync([id], ct);
        if (challenge is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.challenge_not_found"));
        }

        var now = DateTime.UtcNow;
        if (challenge.ResolvedAt is not null || challenge.EndsAt <= now)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.challenge_closed"));
        }

        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == body.PostId && p.ChallengeId == id && !p.Hidden, ct);
        if (post is null)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.not_an_entry"));
        }

        if (post.UserId == me.Id)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.vote_own"));
        }

        var existing = await db.ChallengeVotes.FirstOrDefaultAsync(v => v.ChallengeId == id && v.UserId == me.Id, ct);
        if (existing is null)
        {
            db.ChallengeVotes.Add(new ChallengeVote { ChallengeId = id, UserId = me.Id, PostId = post.Id, CreatedAt = now });
        }
        else if (existing.PostId != post.Id)
        {
            existing.PostId = post.Id;
            existing.CreatedAt = now;
        }

        // One "voted for your entry" per voter and entry, however many times the vote moves back and forth.
        await notifier.AddOnceAsync(post.UserId, NotificationType.Vote, me.Handle, post.Id, id, ct);

        await db.SaveChangesAsync(ct);
        var votes = await db.ChallengeVotes.CountAsync(v => v.ChallengeId == id && v.PostId == post.Id, ct);
        return Results.Json(new VoteStateDto(post.Id, votes), AppJson.Options);
    }

    private static async Task<IResult> UnvoteAsync(Guid id, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var challenge = await db.Challenges.FindAsync([id], ct);
        if (challenge is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.challenge_not_found"));
        }

        if (challenge.ResolvedAt is not null || challenge.EndsAt <= DateTime.UtcNow)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(me.PreferredLanguage, "error.challenge_closed"));
        }

        var existing = await db.ChallengeVotes.FirstOrDefaultAsync(v => v.ChallengeId == id && v.UserId == me.Id, ct);
        if (existing is null)
        {
            return Results.Json(new VoteStateDto(null, 0), AppJson.Options);
        }

        var postId = existing.PostId;
        db.ChallengeVotes.Remove(existing);
        await db.SaveChangesAsync(ct);
        var votes = await db.ChallengeVotes.CountAsync(v => v.ChallengeId == id && v.PostId == postId, ct);
        return Results.Json(new VoteStateDto(null, votes), AppJson.Options);
    }
}
