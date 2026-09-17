using System.Diagnostics;

namespace FitCheck.Api.Services;

/// <summary>
/// One line per <c>/api</c> request, for the days after a deploy when "is it the app or the phone?" is the whole question:
/// <code>api POST /api/checks 201 842ms 0f3a9c7e4b1d4a5e8c2f6b0d9a3e7c15</code>
/// the method, the path with its query cut off, the status, the milliseconds, and the account when the request carried a
/// session (a bare <c>-</c> when it did not). Nothing else: no bodies, no headers, no cookies, no tokens, no query
/// string — a reset link and a Stripe signature both travel in places a request log would otherwise keep forever.
/// <para>
/// Off unless <c>Logging:Requests</c> is true, and off means not registered at all, so a pilot pays nothing for it.
/// Static files, the landing pages, <c>/healthz</c> and <c>/readyz</c> are not logged: a probe every few seconds would
/// bury the lines worth reading.
/// </para>
/// <para>
/// The line is written after the response, so the status is the one that went out — a 403 from the CSRF check and a 429
/// from a limiter are logged like any other answer. The account is read after the pipeline too, which is when
/// authentication has put the principal on the context.
/// </para>
/// </summary>
public static class RequestLog
{
    /// <summary>Configuration key: <c>Logging:Requests=true</c> (environment: <c>Logging__Requests=true</c>).</summary>
    public const string SettingKey = "Logging:Requests";

    /// <summary>The logger category, so a deployment can raise or lower this one line: <c>Logging__LogLevel__FitCheck.Api.Services.RequestLog</c>.</summary>
    public const string Category = "FitCheck.Api.Services.RequestLog";

    /// <summary>
    /// Registers the line when <c>Logging:Requests</c> is on, and does nothing at all when it is not. Called early in the
    /// pipeline so the milliseconds cover the whole request, the response the caller really got.
    /// </summary>
    public static IApplicationBuilder UseRequestLog(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>(SettingKey))
        {
            return app;
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Category);
        logger.LogInformation("{Setting} is on: one line per /api request (method, path, status, ms, account). Bodies, queries and headers are never logged.", SettingKey);
        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await next(context);
                return;
            }

            var started = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                // Request.Path only: the query string is deliberately dropped, and PathBase is empty behind the tunnel.
                logger.LogInformation("api {Method} {Path} {Status} {ElapsedMs}ms {Account}",
                    context.Request.Method,
                    context.Request.Path.Value ?? "/",
                    context.Response.StatusCode,
                    (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    Sessions.UserId(context.User) is { } userId ? userId.ToString("N") : "-");
            }
        });
    }
}
