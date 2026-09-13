using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// The weekly flames board (Round 10): five top-tens for the week that is running in Board:TimeZone (looks, people,
/// rising, by intent, the stylist's picks), a week from the archive, the hall of winners, and a moderator's exclusion.
/// <list type="bullet">
/// <item><c>GET /api/board?week=yyyy-MM-dd</c> (public): <see cref="BoardDto"/> for the current week, or for the week
/// that contains the date given (a date in the board's zone; a full ISO instant is taken as an instant), from the week
/// of the first look (last week at the latest) through next week. A week that is over and closed reads from
/// <see cref="Domain.WeeklyWinner"/>; any other week is computed by <see cref="Board"/> under the eligibility rules of
/// <see cref="Domain.BoardOptions"/>, with a look in <see cref="Domain.BoardExclusion"/> left off; the running week and
/// the one before it are served from memory for a minute. Counted in <see cref="Domain.CounterName.BoardViews"/> once per
/// answered request. Errors: error.board_week_invalid (garbage, or a week outside that span).</item>
/// <item><c>GET /api/board/hall</c> (public): <see cref="HallDto"/>, the closed weeks newest first, twelve at most.</item>
/// <item><c>POST /api/admin/board/exclude</c> (moderators): <see cref="ExcludeRequest"/> → 201 <see cref="BoardExclusionDto"/>;
/// error.post_not_found, error.board_excluded when the look is already off. <c>DELETE /api/admin/board/exclude/{postId}</c>
/// puts it back (204); error.board_not_excluded when it was not off. Both drop the board's cache.</item>
/// </list>
/// The closer (<see cref="BoardCloser"/>) writes <see cref="Domain.WeeklyWinner"/> rows once per week at the close in the
/// board's zone, sends <see cref="Domain.NotificationType.BoardRank"/> with the rank, and catches up after downtime; it and
/// the board read the clock through <see cref="Services.IClock"/> so a test can put the week where it wants it.
/// </summary>
public static class BoardEndpoints
{
    public const int HallWeeks = 12;

    public static IEndpointRouteBuilder MapBoardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/board", GetAsync);
        app.MapGet("/api/board/hall", HallAsync);
        // The same gate as every /api/admin route: a signed-in account (401 gone, 403 suspended) that carries the moderator flag.
        var admin = app.MapGroup("/api/admin/board").RequireAuthorization().AddEndpointFilter(AdminEndpoints.GateAsync);
        admin.MapPost("/exclude", ExcludeAsync);
        admin.MapDelete("/exclude/{postId:guid}", IncludeAsync);
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    private static async Task<IResult> GetAsync(
        HttpContext context, AppDbContext db, Board board, PostReader reader, Localizer localizer, string? week, CancellationToken ct)
    {
        BoardWeek target;
        if (string.IsNullOrWhiteSpace(week))
        {
            target = board.CurrentWeek();
        }
        else if (!board.TryParseWeek(week, out target) || !await board.CanAnswerAsync(db, target, ct))
        {
            // Garbage, the edges of the calendar, a week before the first look or beyond next week: refused, not computed.
            return Error(StatusCodes.Status400BadRequest, localizer.Get(Localizer.Resolve(null, context.Request), "error.board_week_invalid"));
        }

        await Counters.IncrementAsync(db, CounterName.BoardViews, ct);
        var result = await board.ReadAsync(db, target, ct);
        var dto = await board.ToDtoAsync(db, reader, result, Sessions.UserId(context.User), ct);
        return Results.Json(dto, AppJson.Options);
    }

    private static async Task<IResult> HallAsync(AppDbContext db, Board board, PostReader reader, CancellationToken ct)
    {
        var labels = await db.WeeklyWinners.Select(w => w.WeekStart).Distinct().OrderByDescending(w => w).Take(HallWeeks).ToListAsync(ct);
        if (labels.Count == 0)
        {
            return Results.Json(new HallDto([]), AppJson.Options);
        }

        var rows = await db.WeeklyWinners.Where(w => labels.Contains(w.WeekStart)).ToListAsync(ct);
        var users = await reader.RefsAsync(rows.Select(r => r.UserId), ct);
        // A look that is gone or under review keeps its place without a photo.
        var postIds = rows.Where(r => r.PostId != null).Select(r => r.PostId!.Value).Distinct().ToList();
        var visible = (await db.Posts.Where(p => postIds.Contains(p.Id) && !p.Hidden).Select(p => p.Id).ToListAsync(ct)).ToHashSet();

        var weeks = rows
            .GroupBy(r => r.WeekStart)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var week = board.WeekStartingOn(DateOnly.FromDateTime(g.Key));
                var winners = g
                    .OrderBy(r => Board.BoardOrder(r.Board)).ThenBy(r => r.Board, StringComparer.Ordinal).ThenBy(r => r.Rank)
                    .Select(r => new WeeklyWinnerDto(
                        r.Board, r.Rank,
                        users.GetValueOrDefault(r.UserId) ?? new UserRefDto("?", "?", AccountType.Person.ToString()),
                        r.PostId,
                        r.PostId is Guid postId && visible.Contains(postId) ? $"/api/posts/{postId}/image" : null,
                        r.Fires, r.Score))
                    .ToList();
                return new HallWeekDto(week.Start, week.End, winners);
            })
            .ToList();

        return Results.Json(new HallDto(weeks), AppJson.Options);
    }

    private static async Task<IResult> ExcludeAsync(
        HttpContext context, ExcludeRequest? request, AppDbContext db, Board board, Localizer localizer, ILogger<Board> logger, CancellationToken ct)
    {
        var admin = AdminEndpoints.Admin(context);
        var lang = admin.PreferredLanguage;
        if (request is null || request.PostId == Guid.Empty)
        {
            return Error(StatusCodes.Status400BadRequest, localizer.Get(lang, "error.invalid_request"));
        }

        // A hidden look can be excluded too: it may be shown again later, and the exclusion should hold.
        if (!await db.Posts.AnyAsync(p => p.Id == request.PostId, ct))
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(lang, "error.post_not_found"));
        }

        if (await db.BoardExclusions.AnyAsync(e => e.PostId == request.PostId, ct))
        {
            return Error(StatusCodes.Status409Conflict, localizer.Get(lang, "error.board_excluded"));
        }

        var reason = (request.Reason ?? "").Trim();
        if (reason.Length > 200)
        {
            reason = reason[..200];
        }

        var exclusion = new BoardExclusion { PostId = request.PostId, ByUserId = admin.Id, Reason = reason, CreatedAt = DateTime.UtcNow };
        db.BoardExclusions.Add(exclusion);
        await db.SaveChangesAsync(ct);
        board.Invalidate();
        logger.LogInformation("Board: {PostId} excluded by {UserId}: {Reason}", exclusion.PostId, admin.Id, reason);

        var dto = new BoardExclusionDto(exclusion.PostId, exclusion.Reason, PostReader.Ref(admin), DateTime.SpecifyKind(exclusion.CreatedAt, DateTimeKind.Utc));
        return Results.Json(dto, AppJson.Options, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> IncludeAsync(
        HttpContext context, Guid postId, AppDbContext db, Board board, Localizer localizer, ILogger<Board> logger, CancellationToken ct)
    {
        var admin = AdminEndpoints.Admin(context);
        var exclusion = await db.BoardExclusions.FirstOrDefaultAsync(e => e.PostId == postId, ct);
        if (exclusion is null)
        {
            return Error(StatusCodes.Status404NotFound, localizer.Get(admin.PreferredLanguage, "error.board_not_excluded"));
        }

        db.BoardExclusions.Remove(exclusion);
        await db.SaveChangesAsync(ct);
        board.Invalidate();
        logger.LogInformation("Board: {PostId} put back by {UserId}", postId, admin.Id);
        return Results.NoContent();
    }
}
