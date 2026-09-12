namespace FitCheck.Api.Endpoints;

/// <summary>
/// The weekly flames board (Round 10): five top-tens for the week that is running in Board:TimeZone (looks, people,
/// rising, by intent, the stylist's picks), a week from the archive, the hall of winners, and a moderator's exclusion.
/// Skeleton: every route answers 501 until the board-server builder fills it. The contract:
/// <list type="bullet">
/// <item><c>GET /api/board?week=yyyy-MM-dd</c> (public): <see cref="BoardDto"/> for the current week, or for the week
/// that contains the date given (a closed week reads from <see cref="Domain.WeeklyWinner"/>). The eligibility rules are
/// <see cref="Domain.BoardOptions"/>; a look in <see cref="Domain.BoardExclusion"/> is left off. Counted in
/// <see cref="Domain.CounterName.BoardViews"/>; cached for about a minute. Errors: error.board_week_invalid.</item>
/// <item><c>GET /api/board/hall</c> (public): <see cref="HallDto"/>, closed weeks newest first.</item>
/// <item><c>POST /api/admin/board/exclude</c> (moderators): <see cref="ExcludeRequest"/> → <see cref="BoardExclusionDto"/>;
/// error.board_excluded when the look is already off. <c>DELETE /api/admin/board/exclude/{postId}</c> puts it back;
/// error.board_not_excluded when it was not off.</item>
/// </list>
/// The closer (a hosted service the builder adds) writes <see cref="Domain.WeeklyWinner"/> rows once per week at the
/// close in the board's zone, sends <see cref="Domain.NotificationType.BoardRank"/> with the rank, and catches up after
/// downtime; it reads the clock through <see cref="Services.IClock"/> so a test can put the week where it wants it.
/// </summary>
public static class BoardEndpoints
{
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

    private static IResult GetAsync(HttpContext context, string? week) => Stubs.NotBuilt(context);

    private static IResult HallAsync(HttpContext context) => Stubs.NotBuilt(context);

    private static IResult ExcludeAsync(HttpContext context, ExcludeRequest? request) => Stubs.NotBuilt(context);

    private static IResult IncludeAsync(HttpContext context, Guid postId) => Stubs.NotBuilt(context);
}
