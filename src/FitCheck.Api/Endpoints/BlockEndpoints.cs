namespace FitCheck.Api.Endpoints;

/// <summary>
/// Blocking (Round 11): one account shuts another out. Skeleton: every route answers 501 until the block builder fills
/// it. The contract:
/// <list type="bullet">
/// <item><c>POST /api/users/{handle}/block</c> (session): writes a <see cref="Domain.Block"/> row, ends the follow in
/// both directions, and answers 200 <see cref="BlockDto"/>. Errors: error.user_not_found (404; a suspended account
/// answers like a missing one, as the profile does), error.cannot_block_self (400), error.already_blocked (409).</item>
/// <item><c>DELETE /api/users/{handle}/block</c> (session): removes the row; 204. error.not_blocked (404) when there was
/// none; the follow does not come back.</item>
/// <item><c>GET /api/users/me/blocks</c> (session): <see cref="BlocksDto"/>, the accounts the caller blocked, newest
/// first, whole (a person blocks a handful, not a feed).</item>
/// </list>
/// What a block does elsewhere is the same builder's, behind one predicate over the pair in either direction: the two
/// accounts' looks leave each other's feeds, explore, search, the board's lists and the profile grids; a comment, a fire,
/// a save, a follow, a mention, a vote or a feature that targets the other is refused with error.blocked (403); the
/// blocked person's profile still opens for the blocker (with <see cref="ViewerProfileDto.Blocked"/> true, so the menu
/// says Unblock) and the blocker's profile opens for the blocked person as a profile with nothing on it; no notification
/// crosses the pair. Nothing tells the blocked person: no BlockedBy anywhere, no distinct error, no empty state that
/// differs from a quiet account.
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

    private static IResult ListAsync(HttpContext context) => Stubs.NotBuilt(context);

    private static IResult BlockAsync(HttpContext context, string handle) => Stubs.NotBuilt(context);

    private static IResult UnblockAsync(HttpContext context, string handle) => Stubs.NotBuilt(context);
}
