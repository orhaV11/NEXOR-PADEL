using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Readiness (Round 11), next to the liveness line <c>/healthz</c> in Program.cs, which stays as it is (200 "ok" when the
/// database answers, 503 otherwise; the Dockerfile, fly.toml and the uptime checkers point at it). The contract:
/// <list type="bullet">
/// <item><c>GET /readyz</c> (public, never cached: <c>Cache-Control: no-store</c>): <see cref="ReadyDto"/> as JSON.
/// Checks, each "ok" or a short reason: <c>db</c> (a query answers and the schema is at the current migration),
/// <c>storage</c> (Storage:Root exists and a file can be written and removed there), and <c>ffmpeg</c> only while
/// Storage:Transcode is on (the binary is found; <see cref="Services.Transcoder.Available"/>). 200 with Ok true when
/// every check is ok, 503 with Ok false and the failing checks named otherwise. No secrets, no paths, no versions in the
/// reasons: the line is public.</item>
/// </list>
/// A deploy waits on /readyz (DEPLOY.md); the proxy keeps polling /healthz. The checks themselves, and the one line
/// logged when the verdict flips, are <see cref="Readiness"/>; the instance is held here, one per app, so it can tell a
/// flip from a poll.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var readiness = new Readiness();
        app.MapGet("/readyz", (HttpContext context, AppDbContext db, IOptions<StorageOptions> storage, IHostEnvironment environment,
                Transcoder transcoder, ILoggerFactory loggers, CancellationToken ct) =>
            ReadyAsync(readiness, context, db, storage.Value, environment.ContentRootPath, transcoder, loggers, ct));
        return app;
    }

    private static async Task<IResult> ReadyAsync(Readiness readiness, HttpContext context, AppDbContext db, StorageOptions storage,
        string contentRoot, Transcoder transcoder, ILoggerFactory loggers, CancellationToken ct)
    {
        // A cached readiness answer is worse than none: the point is what this instance can do right now.
        context.Response.Headers.CacheControl = "no-store";
        var ready = await readiness.CheckAsync(db, storage, contentRoot, transcoder, loggers.CreateLogger<Readiness>(), ct);
        return Results.Json(ready, AppJson.Options, statusCode: ready.Ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }
}
