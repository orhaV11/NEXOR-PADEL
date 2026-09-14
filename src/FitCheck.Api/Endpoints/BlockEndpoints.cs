using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Blocking (Round 11): one account shuts another out. The contract:
/// <list type="bullet">
/// <item><c>POST /api/users/{handle}/block</c> (session): writes a <see cref="Domain.Block"/> row, ends the follow in
/// both directions, and answers 200 <see cref="BlockDto"/>. Errors: error.user_not_found (404; a suspended account
/// answers like a missing one, as the profile does), error.cannot_block_self (400), error.already_blocked (409).
/// Silent: no notification, nothing pushed. Logged as "Block: {Blocker} blocked {Blocked}" (ids).</item>
/// <item><c>DELETE /api/users/{handle}/block</c> (session): removes the row; 204. error.not_blocked (404) when there was
/// none; the follow does not come back.</item>
/// <item><c>GET /api/users/me/blocks</c> (session): <see cref="BlocksDto"/>, the accounts the caller blocked, newest
/// first, whole (a person blocks a handful, not a feed).</item>
/// </list>
/// What a block does elsewhere sits behind <see cref="Blocks"/>, one predicate over the pair in either direction: the two
/// accounts' looks leave each other's feeds, Explore, search, the tag pages, the saved list and the profile grids; a
/// comment, a fire, a save, a follow, a mention or a feature that targets the other is refused with error.blocked (403);
/// the other's look, photo, clip and comment list read as missing (404); each side's comments leave the other's lists
/// (the rows stay); no notification crosses the pair and the old ones are left out of the list. The blocked person's
/// profile still opens for the blocker (with <see cref="ViewerProfileDto.Blocked"/> true, so the menu says Unblock) and
/// the blocker's profile opens for the blocked person as a profile with nothing on it. Nothing tells the blocked person:
/// no BlockedBy anywhere, no distinct error, no empty state that differs from a quiet account. The public board is
/// public and keeps every look (Services/Board.cs is untouched). A moderator can be blocked like anyone; the moderation
/// routes and the queue's way to a hidden look do not read the pair.
/// </summary>
public static class BlockEndpoints
{
    public static IEndpointRouteBuilder MapBlockEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();
        group.MapGet("/me/blocks", ListAsync);
        group.MapPost("/{handle}/block", BlockAsync);
        group.MapDelete("/{handle}/block", UnblockAsync);
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    private static async Task<IResult> ListAsync(HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        // The caller's own list, whole: the rows joined to the accounts they name (a deleted account took its row along).
        var rows = await db.Blocks
            .Where(b => b.BlockerId == me.Id)
            .Join(db.Users, b => b.BlockedId, u => u.Id, (b, u) => new { b.CreatedAt, User = u })
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        var items = rows.Select(x => new BlockDto(PostReader.Ref(x.User), DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc))).ToList();
        return Results.Json(new BlocksDto(items), AppJson.Options);
    }

    private static async Task<IResult> BlockAsync(
        string handle, HttpContext context, AppDbContext db, IClock clock, Localizer localizer, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var lang = me.PreferredLanguage;
        var target = await UserEndpoints.FindVisibleByHandleAsync(db, handle, ct);
        if (target is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.user_not_found"));
        }

        if (target.Id == me.Id)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.cannot_block_self"));
        }

        if (await db.Blocks.AnyAsync(b => b.BlockerId == me.Id && b.BlockedId == target.Id, ct))
        {
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.already_blocked"));
        }

        var now = clock.UtcNow;
        var block = new Block { BlockerId = me.Id, BlockedId = target.Id, CreatedAt = now };
        db.Blocks.Add(block);
        try
        {
            // The row and the two follows go together: a second tap racing this one finds the key taken and answers 409.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await db.Follows
                .Where(f => (f.FollowerId == me.Id && f.FollowedId == target.Id) || (f.FollowerId == target.Id && f.FollowedId == me.Id))
                .ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.already_blocked"));
        }

        // Ids, not handles: a log line must not carry a name next to the word "blocked".
        loggerFactory.CreateLogger(nameof(BlockEndpoints)).LogInformation("Block: {Blocker} blocked {Blocked}", me.Id, target.Id);
        return Results.Json(new BlockDto(PostReader.Ref(target), DateTime.SpecifyKind(now, DateTimeKind.Utc)), AppJson.Options);
    }

    private static async Task<IResult> UnblockAsync(string handle, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        // Any account, suspended ones included, like undoing a follow: the list must always be clearable.
        var target = await UserEndpoints.FindByHandleAsync(db, handle, ct);
        if (target is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.user_not_found"));
        }

        var removed = await db.Blocks.Where(b => b.BlockerId == me.Id && b.BlockedId == target.Id).ExecuteDeleteAsync(ct);
        if (removed == 0)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(me.PreferredLanguage, "error.not_blocked"));
        }

        return Results.NoContent();
    }
}
