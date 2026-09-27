namespace FitCheck.Api.Endpoints;

/// <summary>
/// Round 20 — the entry links: <c>GET /go/{source}</c> counts an arrival per allowlisted source (Funnel:Sources) and
/// redirects into the check with the source on the query, so the guest check and the signup can carry it. The
/// skeleton; the builder maps the route from the brief.
/// </summary>
public static class GoEndpoints
{
    public static IEndpointRouteBuilder MapGoEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
