namespace FitCheck.Api.Endpoints;

/// <summary>
/// Readiness (Round 11), next to the liveness line <c>/healthz</c> in Program.cs, which stays as it is (200 "ok" when the
/// database answers, 503 otherwise; the Dockerfile, fly.toml and the uptime checkers point at it). Skeleton: the route
/// answers 501 until the ops builder fills it. The contract:
/// <list type="bullet">
/// <item><c>GET /readyz</c> (public, never cached: <c>Cache-Control: no-store</c>): <see cref="ReadyDto"/> as JSON.
/// Checks, each "ok" or a short reason: <c>db</c> (a query answers and the schema is at the current migration),
/// <c>storage</c> (Storage:Root exists and a file can be written and removed there), and <c>ffmpeg</c> only while
/// Storage:Transcode is on (the binary is found; <see cref="Services.Transcoder.Available"/>). 200 with Ok true when
/// every check is ok, 503 with Ok false and the failing checks named otherwise. No secrets, no paths, no versions in the
/// reasons: the line is public.</item>
/// </list>
/// A deploy waits on /readyz (DEPLOY.md, the docs builder); the proxy keeps polling /healthz.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/readyz", ReadyAsync);
        return app;
    }

    private static IResult ReadyAsync(HttpContext context) => Stubs.NotBuilt(context);
}
