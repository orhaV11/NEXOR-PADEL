using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// Round 13 — the growth loop: the week comes back to the person by mail. Two messages, both in the account's own
/// language, both plain text, both three or four lines with one thing to tap:
/// <list type="bullet">
/// <item>a welcome, once per account, as soon as there is an address the server may write to (a confirmed one — an
/// address nobody confirmed is somebody's typo or somebody else's inbox). Signup takes no address, so the first pass of
/// <see cref="DigestService"/> after the confirmation is what sends it; only accounts created in the last
/// <see cref="WelcomeWithin"/> get one, so switching mail on for an old pilot never blasts everyone.</item>
/// <item>the weekly digest, on Sunday morning at <see cref="DigestOptions.Hour"/> in Board:TimeZone, to accounts with
/// <see cref="AppUser.DigestOn"/>, a confirmed address and at least one look or one check in the week: what the week's
/// fires and comments came to, the week's top look on the board with its public link, and "check a look". Nothing else:
/// no digest is worth being the mail somebody dreads.</item>
/// </list>
/// <para>
/// Every message carries the unsubscribe link, signed (<see cref="DigestTokens"/>) so a link can turn off exactly one
/// account's mail and nobody else's; <c>GET /digest/off/{token}</c> flips <see cref="AppUser.DigestOn"/> with no login.
/// <see cref="AppUser.LastDigestAt"/> is what makes the send idempotent: it is stamped as each message goes, so a
/// restart in the middle of a run finishes the rest and never writes to the same inbox twice.
/// </para>
/// <para>
/// Nothing is sent when mail is off (<see cref="EmailOptions.Enabled"/>) or when no public origin is configured: every
/// line of both messages is a link, and a link must never be built from a request's Host header — this runs without a
/// request at all. One line per run goes to the log, whatever happened.
/// </para>
/// </summary>
public sealed class Digest(
    IServiceScopeFactory scopes,
    Board board,
    IClock clock,
    IEmailSender email,
    IOptions<EmailOptions> emailOptions,
    IOptions<BillingOptions> billingOptions,
    IOptions<DigestOptions> options,
    DigestTokens tokens,
    Localizer localizer,
    ILogger<Digest> logger)
{
    /// <summary>How long after Sunday morning a digest may still go out. Past it the week is stale and the run is quiet.</summary>
    public static readonly TimeSpan SendWindow = TimeSpan.FromHours(24);

    /// <summary>Only an account this young gets a welcome, so turning mail on for an existing pilot mails nobody old.</summary>
    public static readonly TimeSpan WelcomeWithin = TimeSpan.FromDays(7);

    /// <summary>The day the digest goes out. Sunday: the week on the board closed at midnight and the person has a morning.</summary>
    public const DayOfWeek SendDay = DayOfWeek.Sunday;

    /// <summary>What one run did, for the log line and for the tests.</summary>
    public sealed record Run(int Digests, int Welcomes, int Skipped);

    /// <summary>
    /// One pass: the welcomes that are due, then the digests, if this moment is inside the Sunday window. Safe to call
    /// on a schedule or from a test; never throws for one person's failed send.
    /// </summary>
    public async Task<Run> RunAsync(CancellationToken ct)
    {
        if (!email.Enabled)
        {
            logger.LogInformation("Digest: mail is off, nothing sent");
            return new Run(0, 0, 0);
        }

        if (!options.Value.Enabled)
        {
            logger.LogInformation("Digest: Digest:Enabled is false, nothing sent");
            return new Run(0, 0, 0);
        }

        if (Origin() is not { Length: > 0 } origin)
        {
            logger.LogWarning("Digest: no Email:PublicOrigin (or Billing:PublicOrigin), so no link can be built and no mail goes out");
            return new Run(0, 0, 0);
        }

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.UtcNow;

        var welcomes = await SendWelcomesAsync(db, origin, now, ct);
        var (digests, skipped) = DueAtUtc(now) is { } dueAt
            ? await SendDigestsAsync(db, origin, dueAt, now, ct)
            : (0, 0);

        logger.LogInformation("Digest: run at {Now:o}, {Digests} digests, {Welcomes} welcomes, {Skipped} with nothing to say", now, digests, welcomes, skipped);
        return new Run(digests, welcomes, skipped);
    }

    /// <summary>
    /// The most recent Sunday morning at <see cref="DigestOptions.Hour"/> in Board:TimeZone, as a UTC instant — or null
    /// when that moment is more than <see cref="SendWindow"/> ago, which is every hour of the week but the first day of
    /// it. So a deploy on a Wednesday sends nothing, and a server that was down on Sunday morning still catches up when
    /// it comes back the same day.
    /// </summary>
    public DateTime? DueAtUtc(DateTime nowUtc)
    {
        var zone = board.Zone;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), zone);
        var hour = Math.Clamp(options.Value.Hour, 0, 23);
        var day = DateOnly.FromDateTime(local);
        var back = ((int)day.DayOfWeek - (int)SendDay + 7) % 7;
        var sunday = day.AddDays(-back);
        var due = LocalToUtc(sunday, hour, zone);
        if (due > nowUtc)
        {
            // Sunday, but before the hour: the week that is due is the one before.
            due = LocalToUtc(sunday.AddDays(-7), hour, zone);
        }

        return nowUtc - due <= SendWindow ? due : null;
    }

    /// <summary>A local date and hour in the board's zone as a UTC instant, the ambiguous and skipped hours of a DST change included.</summary>
    private static DateTime LocalToUtc(DateOnly day, int hour, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);
        // A clock that jumps forward skips the hour entirely; the next one that exists is when the mail goes.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>Email:PublicOrigin, else Billing:PublicOrigin, else empty: a mailed link is never built from a request.</summary>
    public string Origin()
    {
        var configured = emailOptions.Value.PublicOrigin.Trim().TrimEnd('/');
        return configured.Length > 0 ? configured : billingOptions.Value.PublicOrigin.Trim().TrimEnd('/');
    }

    // ---------- the welcome ----------

    private async Task<int> SendWelcomesAsync(AppDbContext db, string origin, DateTime now, CancellationToken ct)
    {
        var since = now - WelcomeWithin;
        var candidates = await db.Users
            .Where(u => !u.Suspended && u.Email != null && u.EmailVerifiedAt != null && u.CreatedAt >= since)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var user in candidates)
        {
            if (await Counters.ReadAsync(db, WelcomeName(user.Id), ct) > 0)
            {
                continue;
            }

            var language = Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : Localizer.DefaultLocale;
            var body = localizer.Get(language, "mail.welcome_body", user.Name, origin + "/#/check", UnsubscribeUrl(origin, user));
            try
            {
                await email.SendAsync(new EmailMessage(user.Email!, localizer.Get(language, "mail.welcome_subject"), body), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Digest: the welcome to {Handle} did not go out; it is tried again next hour", user.Handle);
                continue;
            }

            // Stamped only after the send, and counted rather than stored on the row: one welcome per account, ever.
            await Counters.IncrementAsync(db, WelcomeName(user.Id), ct);
            sent++;
        }

        return sent;
    }

    /// <summary>The Counter row that says this account has had its welcome. A tally of one; the name is the account.</summary>
    public static string WelcomeName(Guid userId) => $"welcome:{userId:N}";

    // ---------- the week ----------

    private async Task<(int Sent, int Skipped)> SendDigestsAsync(AppDbContext db, string origin, DateTime dueAt, DateTime now, CancellationToken ct)
    {
        var candidates = await db.Users
            .Where(u => u.DigestOn && !u.Suspended && u.Email != null && u.EmailVerifiedAt != null
                && (u.LastDigestAt == null || u.LastDigestAt < dueAt))
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return (0, 0);
        }

        var weekStart = dueAt - TimeSpan.FromDays(7);
        // The week that just closed on the board: its top look is the one the mail names, with its public address.
        var week = board.Previous(board.WeekOf(now));
        string? topLine = null;
        try
        {
            var result = await board.ComputeAsync(db, week, ct);
            if (result.Looks.FirstOrDefault()?.PostId is { } topPostId)
            {
                topLine = PublicPageEndpoints.LookUrl(origin, topPostId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Digest: the week's board could not be read; the mail goes without the top look");
        }

        var sent = 0;
        var skipped = 0;
        foreach (var user in candidates)
        {
            var posts = await db.Posts.Where(p => p.UserId == user.Id && !p.Hidden).Select(p => p.Id).ToListAsync(ct);
            var postedThisWeek = await db.Posts.AnyAsync(p => p.UserId == user.Id && p.CreatedAt >= weekStart && p.CreatedAt < dueAt, ct);
            var checkedThisWeek = await db.Checks.AnyAsync(c => c.UserId == user.Id && c.CreatedAt >= weekStart && c.CreatedAt < dueAt, ct);
            if (!postedThisWeek && !checkedThisWeek)
            {
                // Nothing happened for this person this week. A mail saying so is a mail nobody asked for.
                skipped++;
                continue;
            }

            var fires = await db.Fires.CountAsync(f => posts.Contains(f.PostId) && f.CreatedAt >= weekStart && f.CreatedAt < dueAt, ct);
            var comments = await db.Comments.CountAsync(c => posts.Contains(c.PostId) && !c.Hidden && c.UserId != user.Id && c.CreatedAt >= weekStart && c.CreatedAt < dueAt, ct);

            var language = Localizer.IsSupported(user.PreferredLanguage) ? user.PreferredLanguage : Localizer.DefaultLocale;
            var top = topLine is null ? "" : localizer.Get(language, "mail.digest_top", topLine) + "\n\n";
            var body = localizer.Get(language, "mail.digest_body",
                user.Name,
                fires.ToString(CultureInfo.InvariantCulture),
                comments.ToString(CultureInfo.InvariantCulture),
                top,
                origin + "/#/check",
                UnsubscribeUrl(origin, user));
            try
            {
                await email.SendAsync(new EmailMessage(user.Email!, localizer.Get(language, "mail.digest_subject"), body), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Digest: the weekly mail to {Handle} did not go out; it is tried again next hour", user.Handle);
                continue;
            }

            // Saved per person, right after the send: a crash in the middle of a run never mails the same inbox twice.
            user.LastDigestAt = now;
            await db.SaveChangesAsync(ct);
            sent++;
        }

        return (sent, skipped);
    }

    /// <summary>The signed one-tap unsubscribe, as every message carries it.</summary>
    public string UnsubscribeUrl(string origin, AppUser user) => $"{origin}{PublicPageEndpoints.DigestOffPath}/{tokens.For(user)}";
}

/// <summary>
/// The weekly mail's settings. <c>Digest:Secret</c> is the one that matters on a server: it keys the unsubscribe links,
/// and while it is empty they are keyed off the account's stored password hash instead — which works, and costs the
/// outstanding links of anyone who resets their password. Set it once (<c>Digest__Secret</c>) and it never changes.
/// </summary>
public sealed class DigestOptions
{
    public const string Section = "Digest";

    /// <summary>Off turns the weekly mail and the welcome off without touching the mail settings.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The hour of Sunday morning, local to Board:TimeZone, the digest goes out at.</summary>
    public int Hour { get; set; } = 9;

    /// <summary>Environment only (<c>Digest__Secret</c>). Any string; 32 random characters is plenty.</summary>
    public string Secret { get; set; } = "";
}

/// <summary>
/// The unsubscribe link's signature: <c>{accountId:N}.{base64url HMAC-SHA256}</c> over the account id, so a link turns
/// off exactly one account's mail and guessing another's is guessing a 256-bit tag. The key is <c>Digest:Secret</c>
/// when the owner set one, and otherwise the account's stored password hash — a secret the server already has, which
/// keeps the link working across restarts with nothing configured, at the price of voiding open links when that account
/// resets its password. Recovery links (RecoveryTokens) stay what they are: one-time rows, because those hand out
/// access; this one only flips a flag, so it needs no row and never expires.
/// </summary>
public sealed class DigestTokens(IOptions<DigestOptions> options)
{
    private const string Purpose = "digest-off:";

    /// <summary>The token for this account's link.</summary>
    public string For(AppUser user) => $"{user.Id:N}.{Sign(user)}";

    /// <summary>The account a token names, or null: a bad shape, an unknown id or a tag that does not match, told apart by nothing.</summary>
    public async Task<AppUser?> FindAsync(AppDbContext db, string? token, CancellationToken ct)
    {
        var text = (token ?? "").Trim();
        var dot = text.IndexOf('.');
        if (dot <= 0 || dot == text.Length - 1 || !Guid.TryParseExact(text[..dot], "N", out var userId))
        {
            return null;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return null;
        }

        var given = Encoding.ASCII.GetBytes(text[(dot + 1)..]);
        var expected = Encoding.ASCII.GetBytes(Sign(user));
        return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected) ? user : null;
    }

    private string Sign(AppUser user)
    {
        var configured = options.Value.Secret.Trim();
        var key = Encoding.UTF8.GetBytes(configured.Length > 0 ? configured : user.PasswordHash);
        var tag = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(Purpose + user.Id.ToString("N")));
        return Convert.ToBase64String(tag).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>
/// Wakes every hour, runs <see cref="Digest.RunAsync"/> and goes back to sleep. Hourly rather than at one moment, so a
/// server that was asleep at nine on Sunday still sends that morning's mail when it wakes, and so a run that failed is
/// simply tried again; the idempotence is <see cref="AppUser.LastDigestAt"/>, not the schedule.
/// </summary>
public sealed class DigestService(Digest digest, ILogger<DigestService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunSafelyAsync(stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task RunSafelyAsync(CancellationToken ct)
    {
        try
        {
            await digest.RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Digest: the run failed; it runs again in an hour");
        }
    }
}
