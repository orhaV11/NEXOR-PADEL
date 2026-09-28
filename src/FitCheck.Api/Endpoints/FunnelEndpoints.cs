using FitCheck.Api.Data;
using FitCheck.Api.Services;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Round 20 — <c>POST /api/funnel/pro-opened</c>: the Pro page was opened from a refused compare or from the wardrobe
/// line, a tally the numbers page reads (<see cref="Funnel.ProFromCompare"/>, <see cref="Funnel.ProFromWardrobe"/>).
/// A hash route never reaches the funnel middleware, so the page says so itself, once, on arrival. Both surfaces are
/// signed-in ones, so the route needs a session; it is a client-driven tally by an account, as the board views are:
/// a script can inflate it, it is a moderators' number and never a decision, and a tally is never worth failing a request.
/// Review of Round 20: the session is not enough. The account is loaded like on every other signed-in door, so a
/// suspended account (403) and a cookie that outlived its account (401) are refused and signed out before anything is
/// counted; a locked-out account cannot move the moderators' number.
/// </summary>
public static class FunnelEndpoints
{
    public static IEndpointRouteBuilder MapFunnelEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/funnel/pro-opened", ProOpenedAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ProOpenedAsync(ProOpenedRequest? body, HttpContext context, AppDbContext db, Localizer localizer, CancellationToken ct)
    {
        var (me, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (me is null)
        {
            return failure!;
        }

        var name = Funnel.ProOpenedCounter(body?.From?.Trim().ToLowerInvariant(), DateOnly.FromDateTime(DateTime.UtcNow));
        if (name is null)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(Localizer.Resolve(null, context.Request), "error.invalid_request"));
        }

        try
        {
            await Counters.IncrementAsync(db, name, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // A tally is never worth a page, as in Funnel.Count.
        }

        return Results.NoContent();
    }
}
