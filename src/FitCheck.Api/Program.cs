using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using FitCheck.Api.Services.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Maintenance commands share the process with the server but never start it: `--vapid`, `--doctor [--live]`,
// `--stripe-check`, `--backup <dir> [--keep <n>]`, `--admin <handle>`, `--unadmin <handle>`, `--verify <handle>`,
// `--unverify <handle>` and `--pro <handle> <months|off>`. Each is found by position, so extra arguments (a --urls, the
// design-time tooling's own flags) do not turn a command into a server start.
static string? ArgumentAfter(string[] args, string flag)
{
    var index = Array.IndexOf(args, flag);
    return index < 0 ? null : index + 1 < args.Length ? args[index + 1] : "";
}

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
    return 0;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.Section));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.Configure<LimitsOptions>(builder.Configuration.GetSection(LimitsOptions.Section));
builder.Services.Configure<PushOptions>(builder.Configuration.GetSection(PushOptions.Section));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.Section));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.Section));
builder.Services.Configure<PlanOptions>(builder.Configuration.GetSection(PlanOptions.Section));
builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection(BillingOptions.Section));
builder.Services.Configure<BoardOptions>(builder.Configuration.GetSection(BoardOptions.Section));
builder.Services.Configure<AffiliateOptions>(builder.Configuration.GetSection(AffiliateOptions.Section));

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

// Maintenance, no web host: `dotnet FitCheck.Api.dll --backup <dir>` writes a consistent copy of the database and the photo
// folder into <dir> and exits. `--keep <n>` then removes the older copies in that folder, newest n of each kept, only once
// the new copy is complete. Local only (no auth: it is a shell on the box, see scripts/backup.sh, tools/backup.sh and DEPLOY.md).
if (ArgumentAfter(args, "--backup") is { } backupDir)
{
    if (backupDir.Length == 0)
    {
        Console.Error.WriteLine("Usage: --backup <directory> [--keep <n>]");
        return 2;
    }

    // No --keep means keep everything (0); Prune refuses anything below 1, so the flag can never empty the folder.
    var keepText = ArgumentAfter(args, "--keep") ?? "";
    var keep = 0;
    if (keepText.Length > 0 && !(int.TryParse(keepText, NumberStyles.None, CultureInfo.InvariantCulture, out keep) && keep >= 1))
    {
        Console.Error.WriteLine("Usage: --backup <directory> [--keep <n>]  (n is 1 or more; leave it out to keep every copy)");
        return 2;
    }

    var configuredRoot = builder.Configuration.GetValue<string>("Storage:Root") ?? storageDefaults.Root;
    var storageRoot = Path.IsPathRooted(configuredRoot) ? configuredRoot : Path.Combine(builder.Environment.ContentRootPath, configuredRoot);
    var (databaseCopy, storageCopy) = DatabaseSetup.Backup(connection.ConnectionString, storageRoot, backupDir);
    Console.WriteLine($"database: {databaseCopy}");
    Console.WriteLine($"storage: {storageCopy ?? "none"}");
    foreach (var gone in DatabaseSetup.Prune(backupDir, keep))
    {
        Console.WriteLine($"removed: {gone}");
    }

    return 0;
}

// `--doctor` prints the go-live checklist from the configuration this process would start with and exits 0 when nothing
// failed, 1 when something did; `--doctor --live` adds one small call to Anthropic and one to Stripe; `--stripe-check` is
// the Stripe part on its own. Nothing starts, nothing is written, no secret is printed (Services/Doctor.cs).
if (args.Contains("--doctor") || args.Contains("--stripe-check"))
{
    return await Doctor.RunAsync(
        builder.Configuration, builder.Environment.ContentRootPath,
        live: args.Contains("--live") || args.Contains("--stripe-check"),
        stripeOnly: args.Contains("--stripe-check") && !args.Contains("--doctor"),
        Console.Out);
}

// `--admin <handle>` makes an existing account a moderator, `--unadmin <handle>` takes that away. The flag is on the row
// (AppUser.IsAdmin): nothing a cookie carries can fake it, and nothing but these two commands and the Admin:Handles sync
// at start writes it. Exit code 1 when there is no such account, so a script notices.
var makeAdmin = ArgumentAfter(args, "--admin");
var dropAdmin = ArgumentAfter(args, "--unadmin");
if (makeAdmin is not null || dropAdmin is not null)
{
    var promote = makeAdmin is not null;
    var handle = (promote ? makeAdmin : dropAdmin)!;
    if (handle.Length == 0)
    {
        Console.Error.WriteLine(promote ? "Usage: --admin <handle>" : "Usage: --unadmin <handle>");
        return 2;
    }

    AdminChange change;
    try
    {
        change = await AdminSync.SetAdminAsync(connection.ConnectionString, handle, promote);
    }
    catch (SqliteException e)
    {
        Console.Error.WriteLine($"Could not open the database {connection.DataSource}: {e.Message.TrimEnd('.')}. Start the app once first.");
        return 1;
    }

    switch (change)
    {
        case AdminChange.NotFound:
            Console.Error.WriteLine($"No account has the handle {handle}. Sign up with it first, then run this again.");
            return 1;
        case AdminChange.Unchanged:
            Console.WriteLine(promote ? $"{handle} was already a moderator." : $"{handle} was not a moderator.");
            return 0;
        default:
            Console.WriteLine(promote
                ? $"{handle} is now a moderator."
                : $"{handle} is no longer a moderator. The account stays; its next request answers like anyone else's.");
            return 0;
    }
}

// `--verify <handle>` marks an existing account as a verified brand (the check next to its brand mark, everywhere the
// account appears), `--unverify <handle>` takes that away. Only these two commands write the flag (AppUser.Verified):
// the owner confirms by hand who is behind a brand account. Exit code 1 when there is no such account, so a script notices.
var verifyHandle = ArgumentAfter(args, "--verify");
var unverifyHandle = ArgumentAfter(args, "--unverify");
if (verifyHandle is not null || unverifyHandle is not null)
{
    var verify = verifyHandle is not null;
    var handle = (verify ? verifyHandle : unverifyHandle)!;
    if (handle.Length == 0)
    {
        Console.Error.WriteLine(verify ? "Usage: --verify <handle>" : "Usage: --unverify <handle>");
        return 2;
    }

    AdminChange change;
    try
    {
        change = await AdminSync.SetVerifiedAsync(connection.ConnectionString, handle, verify);
    }
    catch (SqliteException e)
    {
        Console.Error.WriteLine($"Could not open the database {connection.DataSource}: {e.Message.TrimEnd('.')}. Start the app once first.");
        return 1;
    }

    switch (change)
    {
        case AdminChange.NotFound:
            Console.Error.WriteLine($"No account has the handle {handle}. Sign up with it first, then run this again.");
            return 1;
        case AdminChange.Unchanged:
            Console.WriteLine(verify ? $"{handle} was already verified." : $"{handle} was not verified.");
            return 0;
        default:
            Console.WriteLine(verify
                ? $"{handle} is now a verified brand. The check shows next to its brand mark from its next request."
                : $"{handle} is no longer verified.");
            return 0;
    }
}

// `--pro <handle> <months>` puts an existing account on Pro for that many months (31 days each, from now) and
// `--pro <handle> off` takes it back to free: the same two fields Checkout and its webhook write when Stripe is on
// (AppUser.Plan, AppUser.ProUntil), from a shell on the box, while the app runs or not. Exit code 1 when there is no
// such account, so a script notices.
if (ArgumentAfter(args, "--pro") is { } proHandle)
{
    var proIndex = Array.IndexOf(args, "--pro");
    var proValue = proIndex + 2 < args.Length ? args[proIndex + 2] : "";
    var proOff = string.Equals(proValue, "off", StringComparison.OrdinalIgnoreCase);
    var proMonths = 0;
    if (proHandle.Length == 0 || (!proOff && !(int.TryParse(proValue, NumberStyles.None, CultureInfo.InvariantCulture, out proMonths) && proMonths is >= 1 and <= 120)))
    {
        Console.Error.WriteLine("Usage: --pro <handle> <months|off>");
        return 2;
    }

    var proUntil = proOff ? (DateTime?)null : DateTime.UtcNow.AddDays(31.0 * proMonths);
    AdminChange proChange;
    try
    {
        proChange = await AdminSync.SetProAsync(connection.ConnectionString, proHandle, proUntil);
    }
    catch (SqliteException e)
    {
        Console.Error.WriteLine($"Could not open the database {connection.DataSource}: {e.Message.TrimEnd('.')}. Start the app once first.");
        return 1;
    }

    switch (proChange)
    {
        case AdminChange.NotFound:
            Console.Error.WriteLine($"No account has the handle {proHandle}. Sign up with it first, then run this again.");
            return 1;
        case AdminChange.Unchanged:
            Console.WriteLine($"{proHandle} was not on Pro.");
            return 0;
        default:
            Console.WriteLine(proOff
                ? $"{proHandle} is back on Free."
                : $"{proHandle} is on Pro until {proUntil:yyyy-MM-dd} (UTC). Run --pro {proHandle} off to end it early.");
            return 0;
    }
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection.ConnectionString));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<Localizer>();
// The board's clock (Round 10): the week's window and the closer read it, so a test can move "now".
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IImageStore, DiskImageStore>();
builder.Services.AddSingleton<CheckCapacity>();
// The per-address count of a guest's looks (Plans:GuestChecksPerDay), kept in memory next to the in-flight reservations.
builder.Services.AddSingleton<GuestAddressCounter>();
builder.Services.AddScoped<OutfitAnalyzer>();
builder.Services.AddScoped<Notifier>();
builder.Services.AddScoped<PostReader>();
// Round 11: the pair predicate behind every block filter, read once per request.
builder.Services.AddScoped<Blocks>();
// Mail: SMTP when Email:Host and Email:From are set, otherwise the log. Email:Host=log keeps mail "on" (links are minted
// and the client offers recovery) while every message goes to the log instead of a server: local runs and the browser test.
builder.Services.AddSingleton<IEmailSender>(provider =>
{
    var email = provider.GetRequiredService<IOptions<EmailOptions>>();
    return email.Value.Enabled && !LogEmailSender.IsLogHost(email.Value)
        ? new SmtpEmailSender(email, provider.GetRequiredService<ILogger<SmtpEmailSender>>())
        : new LogEmailSender(provider.GetRequiredService<ILogger<LogEmailSender>>(), email);
});
// Clips are re-encoded to H.264 MP4 by one background worker when ffmpeg is there (Storage:Transcode); /api/config says whether.
builder.Services.AddSingleton<Transcoder>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<Transcoder>());
// A visitor's unclaimed check expires with its cookie: the sweeper removes day-old guest rows and their files, hourly and once at start.
builder.Services.AddSingleton<GuestCheckSweeper>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<GuestCheckSweeper>());
// The weekly board (Round 10): the window math, the rules and the minute's cache in one instance; the closer writes the
// archive rows every five minutes for any week that is over, and catches up after downtime.
builder.Services.AddSingleton<Board>();
builder.Services.AddSingleton<BoardCloser>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<BoardCloser>());
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
// Round 13 — security: the per-account sign-in brake and the server-side session revocation (both in memory over one
// process, the revocation rows persisted in Counters). Services/Security/.
builder.Services.AddSingleton<AccountBrake>();
builder.Services.AddSingleton<SessionRevocation>();
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

// Stripe, when Billing:Provider is stripe: one named client for Checkout Sessions (the webhook needs none). Nothing is
// sent while the provider is manual; the routes answer 400 instead.
builder.Services.AddHttpClient(StripeClient.HttpClientName, client =>
    {
        client.BaseAddress = new Uri(StripeClient.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(20);
    })
    // The secret key travels as a bearer token; trace-level logging must not print it.
    .RedactLoggedHeaders(["Authorization"]);
builder.Services.AddSingleton<StripeClient>();

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
        // Round 13 — security: a ticket is refused once its session was signed out on the server (logout) or the account's
        // password was reset (Services/Security/SessionRevocation.cs); a ticket without a session id is refused too.
        options.Events.OnValidatePrincipal = SessionRevocation.ValidatePrincipalAsync;
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
// Recovery mail (forgot password, a verification link again) is a way to make this server spam an inbox; a few an hour is plenty.
var recoveryPerHour = builder.Configuration.GetValue<int?>("Limits:RecoveryPerHourPerIp") ?? AuthEndpoints.RecoveryPerHourPerIpDefault;
// Comments and reports are a brake on one account flooding a thread or burying the moderation queue: an hour's window per
// signed-in account, so two people behind one router never share a bucket. The limiter runs after authentication (below)
// so the cookie's principal is there to read; an unsigned call is refused by authorization before it reaches a limiter,
// and the address is only the fallback for a route that is limited without being protected.
var commentsPerHour = builder.Configuration.GetValue<int?>("Limits:CommentsPerHour") ?? limitDefaults.CommentsPerHour;
var reportsPerHour = builder.Configuration.GetValue<int?>("Limits:ReportsPerHour") ?? limitDefaults.ReportsPerHour;
// The anonymous check path's abuse brake: Plans:GuestAttemptsPerDay attempts per client address per day, whatever they
// come to. A fixed window never hands a permit back, so this is not the guest's cap (a refused photo or a model outage
// would spend it): the look itself, Plans:GuestChecksPerDay per cookie and per address, is counted by the handler from the
// checks it actually stored. A signed-in call is not limited here; its plan is.
var guestAttemptsPerDay = builder.Configuration.GetValue<int?>("Plans:GuestAttemptsPerDay") ?? new PlanOptions().GuestAttemptsPerDay;
// Round 13 — security: the two token routes (reset, verify-email) get a generous quarter hour per address.
var tokenAttemptsPerQuarterHour = builder.Configuration.GetValue<int?>("Limits:TokenAttemptsPerQuarterHourPerIp") ?? AuthEndpoints.TokenAttemptsPerQuarterHourPerIpDefault;
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Round 13 — security
    options.AddPolicy(AuthEndpoints.TokenPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, tokenAttemptsPerQuarterHour), Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    options.AddPolicy(AuthEndpoints.SignupPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = signupsPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy(AuthEndpoints.LoginPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginsPerQuarterHour, Window = TimeSpan.FromMinutes(15), QueueLimit = 0 }));
    options.AddPolicy(AuthEndpoints.RecoveryPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = recoveryPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy(PostEndpoints.CommentsPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        AccountOrAddress(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = commentsPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy(PostEndpoints.ReportsPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        AccountOrAddress(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = reportsPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy(CheckEndpoints.GuestPolicy, context => Sessions.UserId(context.User) is not null
        ? RateLimitPartition.GetNoLimiter("signed-in")
        : RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, guestAttemptsPerDay), Window = TimeSpan.FromHours(24), QueueLimit = 0 }));
    // Store links leave through /api/items/{id}/out: a minute's window per client address keeps a script from running the tally up.
    options.AddPolicy(ItemEndpoints.OutPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = ItemEndpoints.OutsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // The data export (Round 11) reads every table at once: a few an hour per account is plenty for a person and a brake for a script.
    options.AddPolicy(ExportEndpoints.Policy, context => RateLimitPartition.GetFixedWindowLimiter(
        AccountOrAddress(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = ExportEndpoints.ExportsPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    // A share video is made on the phone; POST /api/checks/{id}/shared-video only counts one. A few dozen an hour per account
    // (or address, for a guest) is more than a person makes and a brake on a script running the tally up.
    options.AddPolicy(CheckEndpoints.SharedVideoPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        AccountOrAddress(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = CheckEndpoints.SharedVideosPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
    {
        var http = context.HttpContext;
        var localizer = http.RequestServices.GetRequiredService<Localizer>();
        var path = http.Request.Path;
        var key = path.StartsWithSegments("/api/auth/login") ? "error.login_limited"
            : path.StartsWithSegments("/api/auth/signup") ? "error.signup_limited"
            : path.StartsWithSegments("/api/auth/forgot") || path.StartsWithSegments("/api/users/me/email") ? "error.recovery_limited"
            // The "guest" policy on /api/checks is only the brake on attempts: error.guest_limit is the handler's, for a look
            // that was actually given.
            : "error.too_fast";
        // The window limiters say when the next permit frees up; the client can show it or wait it out.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await http.Response.WriteAsJsonAsync(
            new ErrorDto(localizer.Get(Localizer.Resolve(null, http.Request), key)), AppJson.Options, ct);
    };
});

// ---- Round 13: the verdict's own verdict, languages shipped only when real ----
// Languages:Enabled (default en, he): what the client offers and the stylist is asked for; /api/config publishes the list.
builder.Services.Configure<LanguagesOptions>(builder.Configuration.GetSection(LanguagesOptions.Section));
// "Did the tip land?" is a tally like the share video's: a few dozen an hour per account (or address, for a guest) is more
// than a person taps and a brake on a script. Registered as a further RateLimiterOptions configuration, so the block above stays as it is.
builder.Services.Configure<RateLimiterOptions>(options =>
    options.AddPolicy(FeedbackEndpoints.Policy, context => RateLimitPartition.GetFixedWindowLimiter(
        AccountOrAddress(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = FeedbackEndpoints.PerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 })));

// ---- Round 13 — the growth loop: the public look page, the invite, the weekly mail, the funnel ----
// Digest:Enabled, Digest:Hour (Sunday morning, local to Board:TimeZone) and Digest:Secret (environment only: it keys the
// unsubscribe links). The weekly mail and the welcome go out from one hosted service that wakes every hour; the tokens
// are their own tiny service so the unsubscribe route can ask for one. Services/Digest.cs says what each message is.
builder.Services.Configure<DigestOptions>(builder.Configuration.GetSection(DigestOptions.Section));
builder.Services.AddSingleton<DigestTokens>();
builder.Services.AddSingleton<Digest>();
builder.Services.AddHostedService<DigestService>();

var app = builder.Build();

// The schema is versioned by EF Core migrations (Data/Migrations). Every start creates a new file, migrates an existing
// one, or upgrades a pilot file from the rounds before migrations, keeping its rows; see DatabaseSetup.
DatabaseSetup.Apply(app.Services, app.Logger);
// Then the moderators: every existing account whose handle is in Admin:Handles gets the flag (never the other way round).
await AdminSync.ApplyAsync(app.Services, app.Logger);

{
    var mail = app.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
    if (mail.Enabled && string.IsNullOrWhiteSpace(mail.PublicOrigin))
    {
        app.Logger.LogWarning("Email is on but Email:PublicOrigin is not set: confirmation and reset links are only built for localhost hosts. Set it to the app's public https origin.");
    }
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable)))
{
    app.Logger.LogWarning("{Variable} is not set: every outfit check will fail with 502 until it is.", AnthropicVisionClient.ApiKeyVariable);
}

{
    var planOptions = app.Services.GetRequiredService<IOptions<PlanOptions>>().Value;
    var limitOptions = app.Services.GetRequiredService<IOptions<LimitsOptions>>().Value;
    if (planOptions.ProChecksPerDay > limitOptions.ChecksPerDay)
    {
        app.Logger.LogWarning(
            "Plans:ProChecksPerDay ({ProCap}) is above Limits:ChecksPerDay ({Ceiling}): a Pro account gets the ceiling, and the ceiling is the number the Pro page and the cap message quote.",
            planOptions.ProChecksPerDay, limitOptions.ChecksPerDay);
    }
}

app.UseForwardedHeaders();

// One line per /api request when Logging:Requests is on, and nothing at all when it is not (the default). First in the
// pipeline, so the milliseconds cover the whole request and the status is the one the caller really got; the account is
// read on the way out, once authentication has put it on the context. Never a body, a query string or a header.
app.UseRequestLog();

// Round 13 — security: the headers every response carries (Content-Security-Policy, nosniff, the referrer and permissions
// policies, no framing, HSTS over https, no-store and same-origin-only on /api answers), set when the response starts so
// nothing downstream (the exception handler clears the response) drops them. The policy and the reasons for each line
// are in Services/Security/SecurityHeaders.cs; SECURITY.md says what they protect.
app.Use(SecurityHeaders.Apply);

// Round 13 — security: Email:Host=log prints every mailed link (the token in it) to the log. That is the laptop's and the
// browser test's mode; on a server it would leave reset links in the log for as long as the log is kept.
if (!app.Environment.IsDevelopment() && LogEmailSender.IsLogHost(app.Services.GetRequiredService<IOptions<EmailOptions>>().Value))
{
    app.Logger.LogWarning("Email:Host is \"log\": confirmation and reset links are written to this log instead of sent. Set a real mail host before anyone but you signs up.");
}

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
        // Stripe posts its events without our header; the webhook's own signature check stands in for it (BillingEndpoints).
        && !context.Request.Path.Equals(BillingEndpoints.WebhookPath, StringComparison.OrdinalIgnoreCase)
        && context.Request.Headers[Sessions.RequestHeader] != Sessions.RequestHeaderValue)
    {
        await WriteAuthError(context, StatusCodes.Status403Forbidden, "error.forbidden");
        return;
    }

    await next();
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
// After authentication, so the per-account policies (comments, reports) can read the cookie's principal; after
// authorization, so an unsigned call to a protected route is a 401 that never spends a permit.
app.UseRateLimiter();

// Round 13 — the growth loop: one tally per landing-page view and per arrival carrying an invite's ?via=<handle>, per
// day, in Counter rows (Services/Funnel.cs). Before the static files, since a landing page is one and would otherwise
// never reach a handler. No cookie, no address, nothing off this machine: a day's number and nothing that names a person.
app.Use(Funnel.Count);

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
app.MapCompareEndpoints();
app.MapBillingEndpoints();
app.MapInsightsEndpoints();
app.MapTodayEndpoints();
app.MapItemEndpoints();
app.MapBoardEndpoints();
// Round 11 (skeleton, 501 until built): blocking, the data export, readiness.
app.MapBlockEndpoints();
app.MapExportEndpoints();
app.MapHealthEndpoints();
// Round 13: POST /api/checks/{id}/useful, the verdict's own verdict.
app.MapFeedbackEndpoints();
// Round 13 — the growth loop: the public pages a share lands on (GET /look/{id}, /look/{id}/image, /u/{handle}) and the
// weekly mail's signed unsubscribe (GET /digest/off/{token}). Server-rendered HTML, no session, no client script.
app.MapPublicPageEndpoints();

// What the client needs before it does anything: upload limits and the push public key. No secrets, no auth. The key is
// published only when the sender accepted the pair: a public key nobody can sign for would make every browser subscribe
// to pings that never come.
app.MapGet("/api/config", (IOptions<StorageOptions> storage, IOptions<PushOptions> push, PushSender sender, IEmailSender email, Transcoder transcoder,
        IOptions<PlanOptions> plans, IOptions<LimitsOptions> limits, IOptions<BillingOptions> billing, IOptions<AffiliateOptions> affiliate, IConfiguration configuration,
        IOptions<LanguagesOptions> languages) =>
    Results.Json(new ConfigDto(storage.Value.MaxImageBytes, storage.Value.MaxVideoBytes, storage.Value.MaxVideoSeconds,
        sender.Enabled ? push.Value.PublicKey : null, email.Enabled, transcoder.Available,
        // The Pro cap as a Pro account really gets it (clamped to Limits:ChecksPerDay): what the Pro page promises.
        new PlansDto(plans.Value.FreeChecksPerDay, Plans.ProCap(plans.Value, limits.Value), plans.Value.GuestChecksPerDay, plans.Value.ProPriceText,
            plans.Value.CompareNeedsPro, billing.Value.StripeEnabled),
        // Whether the item sheet says a store link may earn a commission (Affiliate:Disclosure); the hosts stay here.
        new AffiliateConfigDto(affiliate.Value.Disclosure),
        // The site's own address (Email:PublicOrigin, else Billing:PublicOrigin): a shared video's end card names it, and a
        // client on localhost or a bare IP prints the wordmark alone rather than guess. Empty when neither is set.
        PublicOrigin: string.IsNullOrWhiteSpace(configuration["Email:PublicOrigin"]) ? (string.IsNullOrWhiteSpace(configuration["Billing:PublicOrigin"]) ? null : configuration["Billing:PublicOrigin"]!.Trim()) : configuration["Email:PublicOrigin"]!.Trim(),
        // Round 13: the UI languages that are live (Languages:Enabled); the client's switcher and detection read this, never the file list.
        Languages: languages.Value.List.ToList()), AppJson.Options));

// For the reverse proxy and uptime checks: 200 when the database answers, 503 otherwise. Never cached. GET or HEAD: an
// uptime checker (and `curl -I`) may probe with either, and a 405 on HEAD would read as the site being down.
app.MapMethods("/healthz", [HttpMethods.Get, HttpMethods.Head], async (AppDbContext db, HttpContext context, CancellationToken ct) =>
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
return 0;

/// <summary>Partition for the per-account limiters: the signed-in account, or the client address when there is none.</summary>
static string AccountOrAddress(HttpContext context) =>
    Sessions.UserId(context.User) is { } userId ? $"user:{userId:N}" : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

static Task WriteAuthError(HttpContext context, int status, string key)
{
    var localizer = context.RequestServices.GetRequiredService<Localizer>();
    context.Response.StatusCode = status;
    return context.Response.WriteAsJsonAsync(
        new ErrorDto(localizer.Get(Localizer.Resolve(null, context.Request), key)), AppJson.Options);
}

/// <summary>Exposed so the test project can host the app with WebApplicationFactory.</summary>
public partial class Program;
