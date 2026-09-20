namespace FitCheck.Api.Services.Security;

/// <summary>
/// The response headers every answer carries (Round 13), set when the response starts so nothing downstream (the
/// exception handler clears the response) drops them. One place, one test: <c>SecurityTests</c> reads them off "/",
/// "/landing/", an API answer and an error.
/// <para>
/// The Content-Security-Policy is written for what the client is: same-origin ES modules (<c>script-src 'self'</c>,
/// no inline script anywhere in wwwroot), a design system that sets <c>style</c> attributes from code and appends one
/// <c>&lt;style&gt;</c> per view (<c>'unsafe-inline'</c> for styles: a nonce cannot cover attributes and a hash cannot
/// cover a computed width; CSS injection is not code execution and every user string reaches the page as text), Google
/// Fonts from index.html and the landing pages (the two font hosts, nothing else off-origin), photos and the share card
/// through blob: URLs, the share video through a blob: media source, the API over same-origin fetch, the service
/// worker and the manifest from here. Nothing may frame the app, no plugins, no base-tag tricks, no form leaves.
/// </para>
/// <para>
/// HSTS only over https, which behind the proxy means X-Forwarded-Proto (read by the forwarded-headers middleware
/// first); a plain http://localhost run never pins itself, and the pin covers this host only: the owner may run the
/// app on a bare domain whose other subdomains are not ours to promise https for. A route may set Referrer-Policy or
/// Cache-Control before the response starts (the store-link door sends no referrer; photos and avatars set their own
/// cache rule); the defaults here are only defaults. API answers are <c>no-store</c> unless a route said otherwise
/// (a person's own /me or export must not sit in a browser or proxy cache) and may be loaded by this origin only
/// (<c>Cross-Origin-Resource-Policy</c>: a look's photo cannot be hot-linked from another site).
/// </para>
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' blob: data:; " +
        "media-src 'self' blob:; " +
        "connect-src 'self'; " +
        "worker-src 'self'; " +
        "manifest-src 'self'; " +
        "frame-src 'none'; " +
        "frame-ancestors 'none'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";

    public const string PermissionsPolicy = "camera=(self), microphone=(self), geolocation=()";
    public const string ReferrerPolicy = "strict-origin-when-cross-origin";
    public const string StrictTransportSecurity = "max-age=31536000";

    /// <summary>The middleware: registers the headers on every response of the request.</summary>
    public static Task Apply(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(() =>
        {
            Set(context);
            return Task.CompletedTask;
        });
        return next(context);
    }

    /// <summary>Writes the headers onto the response as it is about to start.</summary>
    public static void Set(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        if (!headers.ContainsKey("Referrer-Policy"))
        {
            headers["Referrer-Policy"] = ReferrerPolicy;
        }

        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["X-Frame-Options"] = "DENY";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";
        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = StrictTransportSecurity;
        }

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            if (!headers.ContainsKey("Cache-Control"))
            {
                headers["Cache-Control"] = "no-store";
            }
        }
    }
}
