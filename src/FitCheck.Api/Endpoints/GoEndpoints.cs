using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Round 20 — distribution that can be counted: the entry links. <c>GET /go/{source}</c> is the short address that goes
/// in a TikTok bio, a WhatsApp group, a campus poster's QR code. It tallies one arrival on the source's own counter row
/// for the day (<see cref="Funnel.SourceArrivals"/>) and redirects into the check screen with the source on the query,
/// <c>/?src={source}#/check</c>, where the client keeps it (invite.js, the same shape as <c>?via</c>) and sends it with
/// the guest check and the signup, so the numbers page can attribute those to the link they followed.
/// <para>
/// The source is an allowlist (Funnel:Sources) and nothing else: an unknown word is redirected to the landing page and
/// counted as nothing (the landing view that follows is a genuine one, and the funnel middleware counts it as such). A
/// link-preview fetcher (WhatsApp, Telegram, Facebook, the crawlers: <see cref="Funnel.CrawlerRegex"/>) is redirected
/// like anyone else but never tallied, because it fetches a pasted link to unfurl it, and counting that would count
/// pastes rather than people. No cookie is set, no address or user agent is stored, and every redirect is
/// <c>no-store</c> so a proxy can never replay a redirect that skipped its count. The source travels as a query, not a
/// cookie, on purpose: the funnel has never set one, and a cookie would be SameSite-fragile on a cross-site hop.
/// </para>
/// <para>
/// Not under <c>/api</c>, so it is a page route by design: outside SecurityFixtures.ApiRoutes, the IDOR enumeration and
/// the CSRF sweep, which enumerate the API only. Do not add an IDOR rule row for it; there is nothing here to own. The
/// security headers still apply, since they are set OnStarting and a redirect starts like any response.
/// </para>
/// </summary>
public static class GoEndpoints
{
    /// <summary>Where an entry link lands: the check screen, with the source on the query where the client reads it.</summary>
    public const string CheckRoute = "#/check";

    /// <summary>Where a word off the allowlist lands, uncounted.</summary>
    public const string Landing = "/landing/";

    public static IEndpointRouteBuilder MapGoEndpoints(this IEndpointRouteBuilder app)
    {
        // No route constraint: the handler decides, so a bad source is the landing redirect and not a 404 page.
        app.MapGet("/go/{source}", GoAsync);
        return app;
    }

    private static async Task<IResult> GoAsync(string source, HttpContext context, AppDbContext db, IOptions<FunnelOptions> funnel, CancellationToken ct)
    {
        // A redirect that skipped its count, or one that counted, must never be replayed from a cache for the next person.
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";

        var code = funnel.Value.Normalize(source);
        if (code is null)
        {
            return Results.Redirect(Landing);
        }

        // A fetcher unfurling a pasted link is a paste, not a person: the same redirect, no tally. The agent is read and forgotten.
        var agent = context.Request.Headers.UserAgent.ToString();
        if (!Funnel.CrawlerRegex().IsMatch(agent))
        {
            try
            {
                await Counters.IncrementAsync(db, Funnel.SourceArrivals(code, DateOnly.FromDateTime(DateTime.UtcNow)), ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // A tally is never worth failing a redirect for.
            }
        }

        // The invite the link may carry rides along, so the next hop (the app's own address) is counted as the invite it
        // is by the funnel middleware, and the client keeps both words. Relative, same host: no Origin() needed.
        var via = PublicPageEndpoints.ViaQuery(context.Request);
        var query = "?src=" + code + (via.Length == 0 ? "" : "&" + via[1..]);
        return Results.Redirect("/" + query + CheckRoute);
    }
}
