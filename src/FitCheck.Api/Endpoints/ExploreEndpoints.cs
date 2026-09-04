using FitCheck.Api.Data;
using FitCheck.Api.Services;

namespace FitCheck.Api.Endpoints;

/// <summary>Explore: trending tags, brands to follow, top looks, open challenges; search; tag feeds. See PHASE3.md.</summary>
public static class ExploreEndpoints
{
    public static IEndpointRouteBuilder MapExploreEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/explore", ExploreAsync);
        app.MapGet("/api/search", SearchAsync);
        app.MapGet("/api/tags/{tag}/posts", TagPostsAsync);
        return app;
    }

    private static Task<IResult> ExploreAsync(HttpContext context, AppDbContext db, PostReader reader, CancellationToken ct) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));

    private static Task<IResult> SearchAsync(HttpContext context, AppDbContext db, PostReader reader, Localizer localizer, string? q, CancellationToken ct) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));

    private static Task<IResult> TagPostsAsync(string tag, HttpContext context, AppDbContext db, PostReader reader, int? offset, int? limit, CancellationToken ct) =>
        Task.FromResult(Results.StatusCode(StatusCodes.Status501NotImplemented));
}
