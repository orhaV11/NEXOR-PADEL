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

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<LimitsOptions>(builder.Configuration.GetSection(LimitsOptions.Section));

var maxImageBytes = builder.Configuration.GetValue<long?>("Storage:MaxImageBytes") ?? new StorageOptions().MaxImageBytes;
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxImageBytes + 256 * 1024;
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
app.UseStaticFiles();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapCheckEndpoints();
app.MapPostEndpoints();
app.MapFeedEndpoints();
app.MapExploreEndpoints();
app.MapChallengeEndpoints();
app.MapNotificationEndpoints();
app.MapMetricsEndpoints();

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
