using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// `dotnet run -- --vapid` prints a fresh VAPID key pair for Web Push and exits; nothing else starts.
if (args.Contains("--vapid"))
{
    var (publicKey, privateKey) = PushSender.GenerateVapidKeys();
    Console.WriteLine("VAPID key pair for Web Push (NIST P-256). The private key is a secret: environment variables only, never appsettings.");
    Console.WriteLine();
    Console.WriteLine($"PUBLIC  {publicKey}");
    Console.WriteLine($"PRIVATE {privateKey}");
    Console.WriteLine();
    Console.WriteLine("Set these before starting the server (PowerShell: $env:Push__PublicKey=\"...\"; bash: export Push__PublicKey=...):");
    Console.WriteLine("  Push__PublicKey   = the PUBLIC line");
    Console.WriteLine("  Push__PrivateKey  = the PRIVATE line");
    Console.WriteLine("  Push__Subject     = mailto:you@example.com (a contact for the push services; optional)");
    Console.WriteLine("Push is off until both keys are set. Changing them later drops every existing subscription; people turn notifications on again in Settings.");
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<LimitsOptions>(builder.Configuration.GetSection(LimitsOptions.Section));
builder.Services.Configure<PushOptions>(builder.Configuration.GetSection(PushOptions.Section));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.Section));

// A check upload is a still plus, optionally, a clip; the form limit covers both and the per-request limit in
// CheckEndpoints tightens it to what that request actually declares.
var storageDefaults = new StorageOptions();
var maxImageBytes = builder.Configuration.GetValue<long?>("Storage:MaxImageBytes") ?? storageDefaults.MaxImageBytes;
var maxVideoBytes = builder.Configuration.GetValue<long?>("Storage:MaxVideoBytes") ?? storageDefaults.MaxVideoBytes;
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxImageBytes + maxVideoBytes + 256 * 1024;
});

// Same rule as the photo root: a relative database path is anchored to the content root, so a restart from a
// different working directory finds the same data instead of silently starting a new pilot.
var connection = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default") ?? "Data Source=orevosh.db");
if (!string.IsNullOrEmpty(connection.DataSource) && connection.DataSource != ":memory:" && !Path.IsPathRooted(connection.DataSource))
{
    connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection.ConnectionString));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<Localizer>();
builder.Services.AddSingleton<IImageStore, DiskImageStore>();
builder.Services.AddSingleton<CheckCapacity>();
builder.Services.AddScoped<OutfitAnalyzer>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddScoped<PostReader>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddHttpClient<IOutfitVisionClient, AnthropicVisionClient>(client =>
    {
        // A vision call that takes longer than this is not a 10-second outfit check; fail and let the user retry.
        client.Timeout = TimeSpan.FromSeconds(60);
    })
    // Trace-level HttpClient logging prints request headers; the key must never reach a log line.
    .RedactLoggedHeaders(["x-api-key"]);

// Web Push: a named client for the push services and one background sender. Nothing is queued without VAPID keys.
builder.Services.AddHttpClient(PushSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<PushSender>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<PushSender>());

// Cookie sessions: HttpOnly, SameSite=Strict, Secure whenever the request came in over https (the tunnel does).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = Sessions.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(90);
        options.SlidingExpiration = true;
        // An API never redirects to a login page; it answers with the same { error } shape as everything else.
        options.Events.OnRedirectToLogin = context => WriteAuthError(context.HttpContext, StatusCodes.Status401Unauthorized, "error.sign_in_required");
        options.Events.OnRedirectToAccessDenied = context => WriteAuthError(context.HttpContext, StatusCodes.Status403Forbidden, "error.forbidden");
    });
builder.Services.AddAuthorization();

// The app is meant to sit behind a tunnel, so the client address comes from X-Forwarded-For.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Minting fresh accounts is the obvious way around the per-user cap, and password guessing needs a brake.
var limitDefaults = new LimitsOptions();
var signupsPerHour = builder.Configuration.GetValue<int?>("Limits:SignupsPerHourPerIp") ?? limitDefaults.SignupsPerHourPerIp;
var loginsPerQuarterHour = builder.Configuration.GetValue<int?>("Limits:LoginsPerQuarterHourPerIp") ?? limitDefaults.LoginsPerQuarterHourPerIp;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.SignupPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = signupsPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy(AuthEndpoints.LoginPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginsPerQuarterHour, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
    {
        var localizer = context.HttpContext.RequestServices.GetRequiredService<Localizer>();
        var key = context.HttpContext.Request.Path.StartsWithSegments("/api/auth/login") ? "error.login_limited" : "error.signup_limited";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ErrorDto(localizer.Get(Localizer.Resolve(null, context.HttpContext.Request), key)), AppJson.Options, ct);
    };
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // No migrations in this phase: the schema is created on first run. Delete the file to reset the pilot.
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable)))
{
    app.Logger.LogWarning("{Variable} is not set: every outfit check will fail with 502 until it is.", AnthropicVisionClient.ApiKeyVariable);
}

app.UseForwardedHeaders();

// Unhandled exceptions become the same { error } shape as every other failure, in the caller's language.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var localizer = context.RequestServices.GetRequiredService<Localizer>();
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    app.Logger.LogError(feature?.Error, "Unhandled exception for {Path}", context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(
        new ErrorDto(localizer.Get(Localizer.Resolve(null, context.Request), "error.server")), AppJson.Options);
}));

// CSRF: a cross-site form can post to the API with the session cookie attached, but it cannot set a custom
// header. Every state-changing call must carry one, or it is refused before any handler runs.
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    if (context.Request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method)
        && context.Request.Headers[Sessions.RequestHeader] != Sessions.RequestHeaderValue)
    {
        await WriteAuthError(context, StatusCodes.Status403Forbidden, "error.forbidden");
        return;
    }

    await next();
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Only wwwroot is served. Photos live under Storage:Root, which is outside it; a post is the only door to one.
app.UseDefaultFiles();
// The client is versionless files; browsers (and the service worker) revalidate them on every load so a deploy never
// leaves an old core.js running next to a new screen. ETags keep the revalidation cheap.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapCheckEndpoints();
app.MapPostEndpoints();
app.MapFeedEndpoints();
app.MapExploreEndpoints();
app.MapChallengeEndpoints();
app.MapNotificationEndpoints();
app.MapMetricsEndpoints();
app.MapPushEndpoints();
app.MapAdminEndpoints();

// What the client needs before it does anything: upload limits and the push public key. No secrets, no auth.
app.MapGet("/api/config", (IOptions<StorageOptions> storage, IOptions<PushOptions> push) =>
    Results.Json(new ConfigDto(storage.Value.MaxImageBytes, storage.Value.MaxVideoBytes, storage.Value.MaxVideoSeconds,
        push.Value.Enabled ? push.Value.PublicKey : null), AppJson.Options));

// For the reverse proxy and uptime checks: 200 when the database answers, 503 otherwise. Never cached.
app.MapGet("/healthz", async (AppDbContext db, HttpContext context, CancellationToken ct) =>
{
    context.Response.Headers.CacheControl = "no-store";
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        return Results.Text("ok");
    }
    catch (Exception)
    {
        return Results.Text("db unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

static Task WriteAuthError(HttpContext context, int status, string key)
{
    var localizer = context.RequestServices.GetRequiredService<Localizer>();
    context.Response.StatusCode = status;
    return context.Response.WriteAsJsonAsync(
        new ErrorDto(localizer.Get(Localizer.Resolve(null, context.Request), key)), AppJson.Options);
}

/// <summary>Exposed so the test project can host the app with WebApplicationFactory.</summary>
public partial class Program;
