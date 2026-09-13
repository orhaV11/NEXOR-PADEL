namespace FitCheck.Api.Endpoints;

/// <summary>
/// The data export (Round 11): everything an account wrote, as one file. Skeleton: the route answers 501 until the
/// ops builder fills it. The contract:
/// <list type="bullet">
/// <item><c>GET /api/users/me/export</c> (session): 200 with an <see cref="ExportDto"/> body serialised with
/// <see cref="Services.AppJson.Options"/> (camelCase, enums as names, nulls left out), <c>Content-Type:
/// application/json</c>, <c>Content-Disposition: attachment; filename="orevosh-&lt;handle&gt;-&lt;yyyyMMdd&gt;.json"</c>
/// and <c>Cache-Control: no-store</c>, so the browser saves it and no cache keeps it. Built in one read per table, the
/// account's own rows only (checks, looks, comments, follows both ways, comparisons, blocks, notifications), newest
/// first; hidden looks and hidden comments included (they are the person's words). No photo, no clip, no birth date,
/// no password hash, no billing ids. Errors: error.export_failed (500) when a read fails half-way; nothing is written
/// anywhere, so a retry is safe.</item>
/// </list>
/// The client side (settings.export, export.hint, export.ready in the i18n files) opens the URL in a new tab or fetches
/// it and hands the blob to the browser; both belong to the same builder.
/// </summary>
public static class ExportEndpoints
{
    public static IEndpointRouteBuilder MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users/me/export", ExportAsync).RequireAuthorization();
        return app;
    }

    public static IResult Error(int status, string message) => AuthEndpoints.Error(status, message);

    private static IResult ExportAsync(HttpContext context) => Stubs.NotBuilt(context);
}
