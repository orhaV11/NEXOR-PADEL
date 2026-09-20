using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Services;

/// <summary>How a doctor line came out. Only <see cref="DoctorStatus.Fail"/> decides the exit code.</summary>
public enum DoctorStatus
{
    Ok,
    Warn,
    Fail,

    /// <summary>A check that had nothing to ask (a live call with no key, the Stripe part while the provider is manual). Never an exit code.</summary>
    Skip
}

/// <summary>One line of the checklist: a stable name, a verdict and a sentence the operator can act on.</summary>
public sealed record DoctorLine(DoctorStatus Status, string Name, string Detail);

/// <summary>The whole run, so a test (and a script, through the exit code) can read it without parsing the text.</summary>
public sealed record DoctorReport(IReadOnlyList<DoctorLine> Lines)
{
    public int Failures => Lines.Count(l => l.Status == DoctorStatus.Fail);
    public int Warnings => Lines.Count(l => l.Status == DoctorStatus.Warn);
    public int Passes => Lines.Count(l => l.Status == DoctorStatus.Ok);
    public int Skipped => Lines.Count(l => l.Status == DoctorStatus.Skip);

    /// <summary>0 when nothing failed, 1 when something did. Warnings never fail a run: they are the operator's call.</summary>
    public int ExitCode => Failures == 0 ? 0 : 1;

    /// <summary>The line with that name, or null. Names are the contract; the sentences are free to be rewritten.</summary>
    public DoctorLine? this[string name] => Lines.FirstOrDefault(l => l.Name == name);
}

/// <summary>
/// <c>dotnet FitCheck.Api.dll --doctor</c>: everything that has to be true before a pilot goes live, read from the same
/// configuration the server would start with, printed as one line per check, exit 0 when nothing failed and 1 when
/// something did. Nothing here starts a web host, writes to the database or applies a migration; the worst it does is
/// write one empty file into the photo folder and delete it again.
/// <list type="bullet">
/// <item><c>--doctor</c> reads settings and the box: the public origin, the Anthropic key and base URL, mail, billing,
/// the plan caps against the ceiling, the VAPID keys, the moderators, the board's time zone, the affiliate hosts, the
/// photo folder, the database file and what it still has to migrate, ffmpeg when clips are to be re-encoded, the
/// free space where the data lives, and (Round 13 — money) the prices a model call is estimated at with the day's
/// spend ceiling, and whether any alert channel is set at all.</item>
/// <item><c>--doctor --live</c> adds the calls only the network can answer: a Messages request of five tokens to
/// Anthropic (skipped for the stub key, so a browser test never spends a cent), a read of the Pro price from Stripe
/// (it exists, it is in the same mode as the key, and it is recurring and not archived) and a read of Stripe's webhook
/// endpoints (one is registered for this origin's <see cref="Endpoints.BillingEndpoints.WebhookPath"/>, enabled, and
/// subscribed to <see cref="WebhookEvents"/>). Each reports the HTTP status it got. Round 13 — money: it also sends
/// one test alert down every configured alert channel, so the owner watches it arrive.</item>
/// <item><c>--stripe-check</c> is the Stripe part on its own: the three keys and their prefixes, then those two reads.
/// The go-live runbook runs it after every key rotation. It is two GETs, it writes nothing and it charges nobody.</item>
/// </list>
/// <para>
/// <b>No secret is ever printed.</b> A key is reported by its prefix and its length, a webhook secret as "set", a
/// password never at all. Paths and hosts are printed: this is a shell on the operator's own box, and a path they
/// cannot see is a check they cannot fix. (<c>/readyz</c>, which is public, prints neither; see <see cref="Readiness"/>.)
/// </para>
/// </summary>
public static class Doctor
{
    /// <summary>The key the browser test and the local stub use. A real run that still carries it would 502 on every check.</summary>
    public const string StubApiKey = "stub-key-not-real";

    /// <summary>What a real Anthropic key starts with; anything else is a warning, not a refusal (the prefix may change).</summary>
    public const string ApiKeyPrefix = "sk-ant-";

    /// <summary>Below this, the data directory is out of room: FAIL.</summary>
    public const long FreeSpaceFailBytes = 256L * 1024 * 1024;

    /// <summary>Below this, a pilot's photos and clips will fill the disk soon: WARN.</summary>
    public const long FreeSpaceWarnBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Anthropic's own host: anything else means the checks are going somewhere that is not Anthropic.</summary>
    public static readonly string DefaultAnthropicBaseUrl = new AnthropicOptions().BaseUrl;

    /// <summary>
    /// The events <c>BillingEndpoints.WebhookAsync</c> switches on, in the order that method reads them. An endpoint
    /// registered for fewer than these silently loses part of the subscription: without
    /// <c>customer.subscription.deleted</c> a cancellation never ends Pro, without <c>invoice.paid</c> a renewal never
    /// extends it. Keep this list beside that switch — it is what <c>--stripe-check</c> compares Stripe's
    /// <c>enabled_events</c> against, and what the runbooks quote.
    /// </summary>
    public static readonly IReadOnlyList<string> WebhookEvents =
    [
        "checkout.session.completed",
        "customer.subscription.created",
        "invoice.paid",
        "customer.subscription.updated",
        "customer.subscription.deleted"
    ];

    /// <summary>
    /// The four of <see cref="WebhookEvents"/> that move the plan or its end date. An endpoint missing one of these is a
    /// failure. The fifth, <c>customer.subscription.created</c>, only fills in a subscription id that
    /// <c>checkout.session.completed</c> already records — it matters for a subscription started on Stripe's side, so an
    /// endpoint without it is a warning worth fixing, not a reason to hold the launch.
    /// </summary>
    private static readonly HashSet<string> WebhookEventsThatMovePro = new(StringComparer.Ordinal)
    {
        "checkout.session.completed",
        "invoice.paid",
        "customer.subscription.updated",
        "customer.subscription.deleted"
    };

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LiveTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Runs the checklist, prints it to <paramref name="output"/> and returns the exit code.
    /// </summary>
    /// <param name="configuration">The configuration the server would start with.</param>
    /// <param name="contentRoot">What a relative Storage:Root or database path is anchored to, as in Program.cs.</param>
    /// <param name="live">Also make the network calls.</param>
    /// <param name="stripeOnly"><c>--stripe-check</c>: the billing keys, the price read and the webhook endpoint, nothing else.</param>
    /// <param name="handler">A stand-in for the network, so the live calls can be tested. Null means the real one.</param>
    public static async Task<int> RunAsync(
        IConfiguration configuration, string contentRoot, bool live, bool stripeOnly, TextWriter output,
        HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        var report = await InspectAsync(configuration, contentRoot, live, stripeOnly, handler, ct);
        Print(report, output, stripeOnly);
        return report.ExitCode;
    }

    /// <summary>The checklist without the printing, for the tests and for anything that wants the lines themselves.</summary>
    public static async Task<DoctorReport> InspectAsync(
        IConfiguration configuration, string contentRoot, bool live, bool stripeOnly,
        HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        var lines = new List<DoctorLine>();
        var billing = Bind<BillingOptions>(configuration, BillingOptions.Section, lines, "billing");
        var apiKey = (configuration[AnthropicVisionClient.ApiKeyVariable]
            ?? Environment.GetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable) ?? "").Trim();

        if (stripeOnly)
        {
            var origin = Origin(configuration);
            Billing(lines, billing, origin);
            await StripeLiveAsync(lines, billing, origin, handler, ct);
            return new DoctorReport(lines);
        }

        var email = Bind<EmailOptions>(configuration, EmailOptions.Section, lines, "email");
        var storage = Bind<StorageOptions>(configuration, StorageOptions.Section, lines, "storage");
        var plans = Bind<PlanOptions>(configuration, PlanOptions.Section, lines, "plans");
        var limits = Bind<LimitsOptions>(configuration, LimitsOptions.Section, lines, "plans");
        var push = Bind<PushOptions>(configuration, PushOptions.Section, lines, "push");
        var admin = Bind<AdminOptions>(configuration, AdminOptions.Section, lines, "admin");
        var board = Bind<BoardOptions>(configuration, BoardOptions.Section, lines, "board");
        var affiliate = Bind<AffiliateOptions>(configuration, AffiliateOptions.Section, lines, "affiliate");
        var anthropic = Bind<AnthropicOptions>(configuration, AnthropicOptions.Section, lines, "anthropic");
        // Round 13 — money
        var alerts = Bind<AlertOptions>(configuration, AlertOptions.Section, lines, "alerts");

        var publicOrigin = Origin(configuration);
        PublicOrigin(lines, configuration, publicOrigin);
        AnthropicKey(lines, apiKey);
        AnthropicBaseUrl(lines, anthropic);
        Email(lines, email, publicOrigin);
        Billing(lines, billing, publicOrigin);
        PlanCaps(lines, plans, limits);
        Push(lines, push);
        Admin(lines, admin, configuration, contentRoot);
        Board(lines, board);
        Affiliate(lines, affiliate);
        Storage(lines, storage, contentRoot);
        Database(lines, configuration, contentRoot);
        Ffmpeg(lines, storage);
        FreeSpace(lines, configuration, storage, contentRoot);
        // Round 13 — money: what a model call is priced at here, the day's ceiling, and whether anything would shout.
        Spend(lines, anthropic, limits);
        Alerts(lines, alerts, email);

        if (live)
        {
            await AnthropicLiveAsync(lines, anthropic, apiKey, handler, ct);
            await StripeLiveAsync(lines, billing, publicOrigin, handler, ct);
            // Round 13 — money: one real alert down every configured channel, so the owner sees it arrive.
            await AlertsLiveAsync(lines, configuration, alerts, email, handler, ct);
        }

        return new DoctorReport(lines);
    }

    // ---- the checks ----

    /// <summary>The origin the app puts in mail links and hands Stripe to come back to. Empty means "whatever host asked", which is not a promise to keep on a real server.</summary>
    private static void PublicOrigin(List<DoctorLine> lines, IConfiguration configuration, string origin)
    {
        var email = (configuration["Email:PublicOrigin"] ?? "").Trim();
        var billing = (configuration["Billing:PublicOrigin"] ?? "").Trim();
        if (origin.Length == 0)
        {
            lines.Add(new(DoctorStatus.Warn, "origin",
                "no public origin: mail links and Checkout returns fall back to the host of whatever request arrives. Set Email__PublicOrigin (and Billing__PublicOrigin when Stripe is on)."));
            return;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            lines.Add(new(DoctorStatus.Fail, "origin", $"{origin} is not an absolute http(s) origin."));
            return;
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            lines.Add(new(DoctorStatus.Warn, "origin",
                $"{origin} is http, not https: the session cookie is not marked Secure and every reset link travels in the clear."));
            return;
        }

        if (email.Length > 0 && billing.Length > 0 && !string.Equals(email.TrimEnd('/'), billing.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new(DoctorStatus.Warn, "origin", $"Email__PublicOrigin ({email}) and Billing__PublicOrigin ({billing}) are different origins."));
            return;
        }

        lines.Add(new(DoctorStatus.Ok, "origin", origin));
    }

    private static void AnthropicKey(List<DoctorLine> lines, string apiKey)
    {
        if (apiKey.Length == 0)
        {
            lines.Add(new(DoctorStatus.Fail, "anthropic", $"{AnthropicVisionClient.ApiKeyVariable} is not set: every outfit check answers 502."));
            return;
        }

        if (apiKey == StubApiKey)
        {
            lines.Add(new(DoctorStatus.Fail, "anthropic", $"{AnthropicVisionClient.ApiKeyVariable} is the stub key: every outfit check answers 502."));
            return;
        }

        // The prefix and the length only: enough to tell a real key from a pasted placeholder, nothing anyone can use.
        lines.Add(apiKey.StartsWith(ApiKeyPrefix, StringComparison.Ordinal)
            ? new(DoctorStatus.Ok, "anthropic", $"{AnthropicVisionClient.ApiKeyVariable} is set ({ApiKeyPrefix}…, {apiKey.Length} characters).")
            : new(DoctorStatus.Warn, "anthropic", $"{AnthropicVisionClient.ApiKeyVariable} is set ({apiKey.Length} characters) but does not start with {ApiKeyPrefix}."));
    }

    private static void AnthropicBaseUrl(List<DoctorLine> lines, AnthropicOptions anthropic)
    {
        var url = (anthropic.BaseUrl ?? "").Trim();
        var model = (anthropic.Model ?? "").Trim();
        lines.Add(string.Equals(url.TrimEnd('/'), DefaultAnthropicBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            ? new(DoctorStatus.Ok, "anthropic-url", $"{DefaultAnthropicBaseUrl}, model {model}.")
            : new(DoctorStatus.Warn, "anthropic-url", $"Anthropic__BaseUrl is {url}, not {DefaultAnthropicBaseUrl}: checks go there, not to Anthropic."));
    }

    private static void Email(List<DoctorLine> lines, EmailOptions email, string origin)
    {
        var host = (email.Host ?? "").Trim();
        if (host.Length == 0)
        {
            lines.Add(new(DoctorStatus.Ok, "email", "off (no Email__Host): recovery links are logged, and the client says recovery is off on this server."));
            return;
        }

        if (LogEmailSender.IsLogHost(email))
        {
            lines.Add(new(DoctorStatus.Warn, "email", "Email__Host=log: links are written to the log and nothing is ever sent. Fine for a laptop, not for a pilot."));
            return;
        }

        if (string.IsNullOrWhiteSpace(email.From))
        {
            lines.Add(new(DoctorStatus.Fail, "email", $"Email__Host is {host} but Email__From is empty: mail stays off, so nobody can confirm an address or reset a password."));
            return;
        }

        if (string.IsNullOrWhiteSpace(email.PublicOrigin))
        {
            lines.Add(new(DoctorStatus.Fail, "email",
                "Email__PublicOrigin is required when mail is on: a link is never built from the request's Host header, so on a real host nothing usable is sent."));
            return;
        }

        if (!string.IsNullOrWhiteSpace(email.User) && string.IsNullOrWhiteSpace(email.Password))
        {
            lines.Add(new(DoctorStatus.Warn, "email", $"Email__User is set but Email__Password is empty: {host} will refuse the login."));
            return;
        }

        var starttls = email.UseStartTls ? "STARTTLS" : "implicit TLS";
        lines.Add(new(DoctorStatus.Ok, "email", $"{host}:{email.Port.ToString(CultureInfo.InvariantCulture)} ({starttls}), from {email.From}, links to {origin}."));
    }

    private static void Billing(List<DoctorLine> lines, BillingOptions billing, string origin)
    {
        var provider = (billing.Provider ?? "").Trim();
        if (provider.Equals("manual", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new(DoctorStatus.Ok, "billing", "manual: Pro is granted from a shell with --pro <handle> <months>, and the Pro page offers no checkout."));
            return;
        }

        if (!provider.Equals("stripe", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new(DoctorStatus.Fail, "billing", $"Billing__Provider is \"{provider}\": it must be manual or stripe."));
            return;
        }

        var secret = (billing.StripeSecretKey ?? "").Trim();
        var price = (billing.StripePriceId ?? "").Trim();
        var webhook = (billing.StripeWebhookSecret ?? "").Trim();
        var missing = new List<string>();
        if (secret.Length == 0)
        {
            missing.Add("Billing__StripeSecretKey");
        }

        if (price.Length == 0)
        {
            missing.Add("Billing__StripePriceId");
        }

        if (webhook.Length == 0)
        {
            missing.Add("Billing__StripeWebhookSecret");
        }

        if (missing.Count > 0)
        {
            lines.Add(new(DoctorStatus.Fail, "billing",
                $"Billing__Provider is stripe but {string.Join(", ", missing)} {(missing.Count == 1 ? "is" : "are")} empty: checkout answers 400 and the webhook refuses every event."));
            return;
        }

        var test = secret.StartsWith("sk_test_", StringComparison.Ordinal);
        var live = secret.StartsWith("sk_live_", StringComparison.Ordinal);
        var wrong = new List<string>();
        if (!test && !live)
        {
            wrong.Add("Billing__StripeSecretKey does not start with sk_live_ or sk_test_");
        }

        if (!price.StartsWith("price_", StringComparison.Ordinal))
        {
            // A product id (prod_...) pasted here is the usual mistake, and Checkout answers with a 400 nobody reads.
            wrong.Add($"Billing__StripePriceId is \"{price}\", which does not start with price_");
        }

        if (!webhook.StartsWith("whsec_", StringComparison.Ordinal))
        {
            wrong.Add("Billing__StripeWebhookSecret does not start with whsec_");
        }

        if (wrong.Count > 0)
        {
            lines.Add(new(DoctorStatus.Fail, "billing", string.Join("; ", wrong) + "."));
            return;
        }

        if (test && origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new(DoctorStatus.Warn, "billing",
                $"stripe with a test key (sk_test_…) on {origin}: Checkout opens, no card is ever charged, and the live webhook secret will not match."));
            return;
        }

        lines.Add(new(DoctorStatus.Ok, "billing", $"stripe ({(live ? "sk_live_" : "sk_test_")}…), price {price}, webhook secret set."));
    }

    private static void PlanCaps(List<DoctorLine> lines, PlanOptions plans, LimitsOptions limits)
    {
        var proCap = Plans.ProCap(plans, limits);
        var freeCap = Math.Max(0, Math.Min(plans.FreeChecksPerDay, limits.ChecksPerDay));
        var notes = new List<string>();
        if (plans.ProChecksPerDay > limits.ChecksPerDay)
        {
            notes.Add($"Plans__ProChecksPerDay ({plans.ProChecksPerDay}) is above Limits__ChecksPerDay ({limits.ChecksPerDay}), so Pro really gets {proCap} — the number the Pro page quotes");
        }

        if (plans.FreeChecksPerDay > limits.ChecksPerDay)
        {
            notes.Add($"Plans__FreeChecksPerDay ({plans.FreeChecksPerDay}) is above Limits__ChecksPerDay ({limits.ChecksPerDay})");
        }

        if (proCap <= freeCap)
        {
            notes.Add($"Pro ({proCap} a day) gets no more checks than free ({freeCap}): there is nothing to pay for");
        }

        if (limits.ChecksPerDay > limits.ChecksPerDayGlobal)
        {
            notes.Add($"one account's ceiling ({limits.ChecksPerDay}) is above everybody's ({limits.ChecksPerDayGlobal})");
        }

        lines.Add(notes.Count > 0
            ? new(DoctorStatus.Warn, "plans", string.Join("; ", notes) + ".")
            : new(DoctorStatus.Ok, "plans",
                $"free {freeCap}, pro {proCap}, guest {plans.GuestChecksPerDay} a day; ceiling {limits.ChecksPerDay} per account and {limits.ChecksPerDayGlobal} for everyone."));
    }

    private static void Push(List<DoctorLine> lines, PushOptions push)
    {
        var hasPublic = !string.IsNullOrWhiteSpace(push.PublicKey);
        var hasPrivate = !string.IsNullOrWhiteSpace(push.PrivateKey);
        if (!hasPublic && !hasPrivate)
        {
            lines.Add(new(DoctorStatus.Warn, "push", "no VAPID keys: push is off and the client never offers notifications. Run --vapid, then set Push__PublicKey and Push__PrivateKey."));
            return;
        }

        if (hasPublic != hasPrivate)
        {
            lines.Add(new(DoctorStatus.Fail, "push",
                $"only Push__{(hasPublic ? "PublicKey" : "PrivateKey")} is set: push stays off until both are, and half a pair is usually a copy that went wrong."));
            return;
        }

        var subject = (push.Subject ?? "").Trim();
        if (!subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && !subject.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new(DoctorStatus.Warn, "push", $"Push__Subject is \"{subject}\": the push services want a mailto: or https: contact, and some refuse anything else."));
            return;
        }

        // Neither key is printed: the private one is a secret and the public one is long and says nothing useful here.
        lines.Add(new(DoctorStatus.Ok, "push", $"VAPID key pair set, subject {subject}."));
    }

    private static void Admin(List<DoctorLine> lines, AdminOptions admin, IConfiguration configuration, string contentRoot)
    {
        var handles = (admin.Handles ?? []).Select(h => (h ?? "").Trim()).Where(h => h.Length > 0).ToList();
        // The list is one way in; `--admin <handle>` is the other, and it leaves no trace in the configuration. The runbook
        // promotes the first account that way two steps before it runs the doctor, so the database, when there is one to
        // read, is the word on who can open the queue today.
        var (moderators, unreadable) = CountModerators(configuration, contentRoot);
        var inDatabase = unreadable
            ? "; the database could not be read, so who is a moderator there is unknown (see the database line)"
            : moderators switch
            {
                null => "",
                0 => "; no account is a moderator yet",
                1 => "; 1 moderator account in the database",
                var n => $"; {n} moderator accounts in the database"
            };
        if (handles.Count > 0)
        {
            lines.Add(new(DoctorStatus.Ok, "admin", $"{handles.Count} moderator handle(s): {string.Join(", ", handles)}{inDatabase}."));
        }
        else if (moderators > 0)
        {
            lines.Add(new(DoctorStatus.Ok, "admin", $"Admin__Handles is empty{inDatabase}, made with --admin: the moderation queue has someone."));
        }
        else if (unreadable)
        {
            // The doctor could not look, so it must not claim that nobody is a moderator: --admin may well have made one.
            lines.Add(new(DoctorStatus.Warn, "admin", "Admin__Handles is empty and the database could not be read, so whether any account is a moderator is unknown (see the database line). Fix the database and run the doctor again, or set Admin__Handles__0."));
        }
        else
        {
            lines.Add(new(DoctorStatus.Warn, "admin", "Admin__Handles is empty and no account is a moderator: nobody can open the moderation queue. Set Admin__Handles__0, or run --admin <handle> once that account exists."));
        }
    }

    /// <summary>
    /// How many accounts carry the moderator flag. Count is null when there is no database file to read yet (looking must
    /// never create one); Unreadable is true when there is something at the path but it could not be read (a folder, a
    /// locked or corrupt file: the database line says why), which is not the same as a count of zero. A read-only count;
    /// nothing is applied.
    /// </summary>
    private static (int? Count, bool Unreadable) CountModerators(IConfiguration configuration, string contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(configuration.GetConnectionString("Default") ?? "Data Source=orevosh.db");
        if (string.IsNullOrEmpty(builder.DataSource) || builder.DataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return (null, false);
        }

        if (!Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.Combine(contentRoot, builder.DataSource);
        }

        var path = Path.GetFullPath(builder.DataSource);
        if (Directory.Exists(path))
        {
            return (null, true);
        }

        if (!File.Exists(path))
        {
            return (null, false);
        }

        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(builder.ConnectionString).Options);
        try
        {
            return (db.Users.Count(u => u.IsAdmin), false);
        }
        catch (Exception e) when (e is SqliteException or InvalidOperationException or IOException)
        {
            return (null, true);
        }
        finally
        {
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
        }
    }

    private static void Board(List<DoctorLine> lines, BoardOptions board)
    {
        var zone = (board.TimeZone ?? "").Trim();
        try
        {
            var found = TimeZoneInfo.FindSystemTimeZoneById(zone);
            var offset = found.GetUtcOffset(DateTime.UtcNow);
            lines.Add(new(DoctorStatus.Ok, "board",
                $"week starts {board.WeekStartsOn} in {zone} (UTC{offset.Hours:+00;-00}:{Math.Abs(offset.Minutes):00} today), {board.Size} places."));
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // The image carries tzdata; a zone it does not know is a typo, and the board would throw on its first read.
            lines.Add(new(DoctorStatus.Fail, "board", $"Board__TimeZone \"{zone}\" is not a zone this machine knows: the weekly board cannot cut its week."));
        }
    }

    private static void Affiliate(List<DoctorLine> lines, AffiliateOptions affiliate)
    {
        var hosts = affiliate.Hosts ?? [];
        var empty = hosts.Where(h => string.IsNullOrWhiteSpace(h.Value)).Select(h => h.Key).ToList();
        if (hosts.Count == 0)
        {
            lines.Add(new(DoctorStatus.Ok, "affiliate", "no hosts: store links leave through /api/items/{id}/out exactly as the poster wrote them."));
            return;
        }

        lines.Add(empty.Count > 0
            ? new(DoctorStatus.Warn, "affiliate", $"{string.Join(", ", empty)} listed with no parameters: links there go out unchanged and earn nothing.")
            : new(DoctorStatus.Ok, "affiliate", $"{hosts.Count} host(s): {string.Join(", ", hosts.Keys)}; disclosure {(affiliate.Disclosure ? "on" : "off")}."));
    }

    private static void Storage(List<DoctorLine> lines, StorageOptions storage, string contentRoot)
    {
        var root = Resolve(storage.Root, contentRoot);
        var probe = Path.Combine(root, $".doctor-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            lines.Add(new(DoctorStatus.Ok, "storage", $"{root} is writable."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            lines.Add(new(DoctorStatus.Fail, "storage", $"{root} cannot be written to: {e.Message.TrimEnd('.')}. Every upload would fail."));
        }
    }

    /// <summary>The database file and what it still owes: the count of migrations the next start would apply, read without applying one.</summary>
    private static void Database(List<DoctorLine> lines, IConfiguration configuration, string contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(configuration.GetConnectionString("Default") ?? "Data Source=orevosh.db");
        if (string.IsNullOrEmpty(builder.DataSource) || builder.DataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(new(DoctorStatus.Warn, "database", "in memory: nothing survives a restart. Set ConnectionStrings__Default to a file on the data volume."));
            return;
        }

        if (!Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.Combine(contentRoot, builder.DataSource);
        }

        var path = Path.GetFullPath(builder.DataSource);
        if (Directory.Exists(path))
        {
            // File.Exists is false for a folder, and "does not exist yet" would send the operator to a first start that cannot open it.
            lines.Add(new(DoctorStatus.Fail, "database", $"{path} is a folder, not a database file: nothing can open it. Point ConnectionStrings__Default at a file."));
            return;
        }

        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(builder.ConnectionString).Options);
        var all = db.Database.GetMigrations().ToList();
        if (!File.Exists(path))
        {
            // Opening a connection would create an empty file, so the file is looked at before anything is asked of it.
            lines.Add(new(DoctorStatus.Warn, "database", $"{path} does not exist yet: the first start creates it from {all.Count} migration(s)."));
            return;
        }

        try
        {
            // Reads the history table and compares; it applies nothing.
            var pending = db.Database.GetPendingMigrations().ToList();
            var size = (new FileInfo(path).Length / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
            lines.Add(pending.Count == 0
                ? new(DoctorStatus.Ok, "database", $"{path} ({size} MB), at the current schema ({all.Count} migration(s) applied).")
                : new(DoctorStatus.Warn, "database", $"{path} ({size} MB): {pending.Count} migration(s) pending ({string.Join(", ", pending)}); the next start applies them."));
        }
        catch (Exception e) when (e is SqliteException or InvalidOperationException or IOException)
        {
            lines.Add(new(DoctorStatus.Fail, "database", $"{path} could not be read: {e.Message.TrimEnd('.')}."));
        }
        finally
        {
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
        }
    }

    private static void Ffmpeg(List<DoctorLine> lines, StorageOptions storage)
    {
        if (!storage.Transcode)
        {
            lines.Add(new(DoctorStatus.Ok, "ffmpeg", "Storage__Transcode is off: clips are served exactly as they were uploaded."));
            return;
        }

        var ffmpeg = Executable(storage.FfmpegPath, "ffmpeg");
        var version = FirstLineOf(ffmpeg);
        if (version is null)
        {
            lines.Add(new(DoctorStatus.Fail, "ffmpeg",
                $"Storage__Transcode is on but ffmpeg did not answer at \"{ffmpeg}\": a WebM clip from an Android phone will not play on an older iPhone. Install ffmpeg or set Storage__FfmpegPath."));
            return;
        }

        var ffprobe = Executable(storage.FfmpegPath, "ffprobe");
        if (FirstLineOf(ffprobe) is null)
        {
            lines.Add(new(DoctorStatus.Fail, "ffmpeg", $"ffmpeg answered but ffprobe did not, at \"{ffprobe}\": both are needed, and they ship together."));
            return;
        }

        lines.Add(new(DoctorStatus.Ok, "ffmpeg", $"{version} at {ffmpeg}."));
    }

    private static void FreeSpace(List<DoctorLine> lines, IConfiguration configuration, StorageOptions storage, string contentRoot)
    {
        var connection = new SqliteConnectionStringBuilder(configuration.GetConnectionString("Default") ?? "Data Source=orevosh.db");
        var data = !string.IsNullOrEmpty(connection.DataSource) && !connection.DataSource.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Resolve(connection.DataSource, contentRoot)) ?? contentRoot
            : Resolve(storage.Root, contentRoot);

        var drive = DriveFor(data);
        if (drive is null)
        {
            lines.Add(new(DoctorStatus.Warn, "disk", $"the free space at {data} could not be read on this machine."));
            return;
        }

        var free = drive.AvailableFreeSpace;
        var gb = (free / 1024.0 / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
        var where = $"{gb} GB free on {drive.Name} (the data lives at {data})";
        lines.Add(free switch
        {
            < FreeSpaceFailBytes => new(DoctorStatus.Fail, "disk", $"{where}: the next upload, backup or WAL checkpoint can fail."),
            < FreeSpaceWarnBytes => new(DoctorStatus.Warn, "disk", $"{where}: photos and clips fill this faster than you think."),
            _ => new(DoctorStatus.Ok, "disk", where + ".")
        });
    }

    // ---- the two calls only the network can answer ----

    /// <summary>A Messages request of five tokens: does this key work against this base URL, right now. The answer is the status code and nothing else.</summary>
    private static async Task<DoctorLine> AnthropicLiveAsync(List<DoctorLine> lines, AnthropicOptions anthropic, string apiKey, HttpMessageHandler? handler, CancellationToken ct)
    {
        if (apiKey.Length == 0 || apiKey == StubApiKey)
        {
            return Add(lines, new(DoctorStatus.Skip, "anthropic-live", $"not called: {AnthropicVisionClient.ApiKeyVariable} is missing or the stub key."));
        }

        var body = JsonSerializer.Serialize(new
        {
            model = (anthropic.Model ?? "").Trim(),
            max_tokens = 5,
            // The content block form the real checks use, so a local stand-in for Anthropic reads it the same way.
            messages = new[] { new { role = "user", content = new[] { new { type = "text", text = "ping" } } } }
        });
        using var http = Client(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, (anthropic.BaseUrl ?? "").TrimEnd('/') + "/v1/messages")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        try
        {
            using var response = await http.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            return Add(lines, status switch
            {
                200 => new(DoctorStatus.Ok, "anthropic-live", $"HTTP 200 from {anthropic.BaseUrl}: the key works and {anthropic.Model} answered."),
                401 or 403 => new(DoctorStatus.Fail, "anthropic-live", $"HTTP {status}: the key was refused. Rotate it in the Anthropic console and set {AnthropicVisionClient.ApiKeyVariable} again."),
                404 => new(DoctorStatus.Fail, "anthropic-live", $"HTTP 404: no model named \"{anthropic.Model}\". Set Anthropic__Model to one that takes a forced tool call."),
                429 => new(DoctorStatus.Warn, "anthropic-live", "HTTP 429: the key works but the account is rate limited right now."),
                _ => new(DoctorStatus.Fail, "anthropic-live", $"HTTP {status} from {anthropic.BaseUrl}.")
            });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Add(lines, new(DoctorStatus.Fail, "anthropic-live", $"no answer from {anthropic.BaseUrl}: {e.Message.TrimEnd('.')}."));
        }
    }

    /// <summary>
    /// The Stripe half of a live run: the price the app was given, then the webhook endpoint Stripe is supposed to post
    /// back to. Two GETs, nothing written, nothing charged. Both lines are always added, skipped or not, so a run always
    /// prints the same names.
    /// </summary>
    private static async Task StripeLiveAsync(List<DoctorLine> lines, BillingOptions billing, string origin, HttpMessageHandler? handler, CancellationToken ct)
    {
        await StripePriceAsync(lines, billing, handler, ct);
        await StripeWebhookAsync(lines, billing, origin, handler, ct);
    }

    /// <summary>Reads the Pro price back from Stripe: the key is accepted, the price exists, it is in the same mode as the key, and it is a recurring price (Checkout runs in subscription mode and refuses a one-time one).</summary>
    private static async Task<DoctorLine> StripePriceAsync(List<DoctorLine> lines, BillingOptions billing, HttpMessageHandler? handler, CancellationToken ct)
    {
        var secret = (billing.StripeSecretKey ?? "").Trim();
        var price = (billing.StripePriceId ?? "").Trim();
        if (!(billing.Provider ?? "").Trim().Equals("stripe", StringComparison.OrdinalIgnoreCase))
        {
            return Add(lines, new(DoctorStatus.Skip, "stripe-live", "not called: Billing__Provider is not stripe."));
        }

        if (secret.Length == 0 || price.Length == 0)
        {
            return Add(lines, new(DoctorStatus.Skip, "stripe-live", "not called: Billing__StripeSecretKey or Billing__StripePriceId is empty."));
        }

        using var http = Client(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, StripeClient.BaseUrl + "v1/prices/" + Uri.EscapeDataString(price));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        try
        {
            using var response = await http.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (status != 200)
            {
                return Add(lines, status switch
                {
                    401 => new(DoctorStatus.Fail, "stripe-live", "HTTP 401: Stripe refused the secret key. Roll it in the Stripe dashboard and set Billing__StripeSecretKey again."),
                    404 => new(DoctorStatus.Fail, "stripe-live", $"HTTP 404: no price {price} for this key. A live key cannot see a test price, or the other way round."),
                    _ => new(DoctorStatus.Fail, "stripe-live", $"HTTP {status} from Stripe.")
                });
            }

            // The recurring block is what makes a price usable by a subscription Checkout; a one-time price is accepted
            // here and refused at the moment someone presses Go Pro, which is the worst place to find out.
            var body = await response.Content.ReadAsStringAsync(ct);
            if (Json(body) is not { } document)
            {
                return Add(lines, new(DoctorStatus.Warn, "stripe-live",
                    $"HTTP 200: the key is accepted and {price} exists, but Stripe's answer could not be read, so whether it is recurring is unknown."));
            }

            using (document)
            {
                var root = document.RootElement;
                var recurring = root.TryGetProperty("recurring", out var block) && block.ValueKind == JsonValueKind.Object ? block : (JsonElement?)null;
                var kind = root.TryGetProperty("type", out var typed) && typed.ValueKind == JsonValueKind.String ? typed.GetString() ?? "" : "";
                if (recurring is null)
                {
                    return Add(lines, kind.Length == 0
                        ? new(DoctorStatus.Warn, "stripe-live",
                            $"HTTP 200: the key is accepted and {price} exists, but Stripe's answer named neither a type nor a recurring block, so whether it is recurring is unknown.")
                        : new(DoctorStatus.Fail, "stripe-live",
                            $"HTTP 200, but {price} is a {kind.Replace('_', '-')} price. Checkout opens in subscription mode and refuses it: make a recurring price in Stripe and set Billing__StripePriceId to that one."));
                }

                var interval = recurring.Value.TryGetProperty("interval", out var every) && every.ValueKind == JsonValueKind.String ? every.GetString() ?? "" : "";
                var count = recurring.Value.TryGetProperty("interval_count", out var many) && many.ValueKind == JsonValueKind.Number && many.TryGetInt32(out var parsed) ? parsed : 1;
                var cadence = interval.Length == 0 ? "recurring" : count == 1 ? $"every {interval}" : $"every {count.ToString(CultureInfo.InvariantCulture)} {interval}s";
                var active = !root.TryGetProperty("active", out var enabled) || enabled.ValueKind != JsonValueKind.False;
                return Add(lines, active
                    ? new(DoctorStatus.Ok, "stripe-live", $"HTTP 200: the key is accepted and {price} exists, {cadence}.")
                    : new(DoctorStatus.Fail, "stripe-live", $"HTTP 200: {price} is {cadence} but archived in Stripe (active is false). Checkout will refuse it."));
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Add(lines, new(DoctorStatus.Fail, "stripe-live", $"no answer from Stripe: {e.Message.TrimEnd('.')}."));
        }
    }

    /// <summary>
    /// Lists the webhook endpoints on the account and looks for the one this app is behind: the configured origin plus
    /// <see cref="Endpoints.BillingEndpoints.WebhookPath"/>, enabled, and subscribed to <see cref="WebhookEvents"/> — the
    /// events <c>BillingEndpoints.WebhookAsync</c> actually switches on. A registered url is not a secret (it is a public
    /// route on the operator's own domain); the signing secret is never read here at all.
    /// </summary>
    private static async Task<DoctorLine> StripeWebhookAsync(List<DoctorLine> lines, BillingOptions billing, string origin, HttpMessageHandler? handler, CancellationToken ct)
    {
        var secret = (billing.StripeSecretKey ?? "").Trim();
        if (!(billing.Provider ?? "").Trim().Equals("stripe", StringComparison.OrdinalIgnoreCase))
        {
            return Add(lines, new(DoctorStatus.Skip, "stripe-webhook", "not called: Billing__Provider is not stripe."));
        }

        if (secret.Length == 0)
        {
            return Add(lines, new(DoctorStatus.Skip, "stripe-webhook", "not called: Billing__StripeSecretKey is empty."));
        }

        if (origin.Length == 0)
        {
            return Add(lines, new(DoctorStatus.Skip, "stripe-webhook",
                "not called: with no Billing__PublicOrigin (or Email__PublicOrigin) there is no url to look for in Stripe's list."));
        }

        var wanted = origin.TrimEnd('/') + Endpoints.BillingEndpoints.WebhookPath;
        using var http = Client(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, StripeClient.BaseUrl + "v1/webhook_endpoints?limit=100");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        try
        {
            using var response = await http.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            if (status != 200)
            {
                return Add(lines, status switch
                {
                    401 => new(DoctorStatus.Fail, "stripe-webhook", "HTTP 401: Stripe refused the secret key, so the endpoint list could not be read."),
                    403 => new(DoctorStatus.Warn, "stripe-webhook",
                        "HTTP 403: this key may not list webhook endpoints (a restricted key). Check the endpoint by hand in Stripe, under Developers and then Webhooks."),
                    _ => new(DoctorStatus.Fail, "stripe-webhook", $"HTTP {status} from Stripe.")
                });
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (Json(body) is not { } document)
            {
                return Add(lines, new(DoctorStatus.Warn, "stripe-webhook", "HTTP 200, but Stripe's list of endpoints could not be read."));
            }

            using (document)
            {
                if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    return Add(lines, new(DoctorStatus.Warn, "stripe-webhook", "HTTP 200, but Stripe's answer carried no list of endpoints."));
                }

                var others = new List<string>();
                var sameRoute = new List<string>();
                foreach (var endpoint in data.EnumerateArray())
                {
                    var url = endpoint.TryGetProperty("url", out var configured) && configured.ValueKind == JsonValueKind.String
                        ? (configured.GetString() ?? "").Trim()
                        : "";
                    if (!string.Equals(url.TrimEnd('/'), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        if (url.Length > 0)
                        {
                            others.Add(url);
                            // The app can answer on more than one name (a fly.dev address and the custom domain), and the
                            // webhook only has to reach the route, not the origin Checkout returns to. Same path, other
                            // host: worth naming, not worth failing on.
                            if (Uri.TryCreate(url, UriKind.Absolute, out var elsewhere)
                                && elsewhere.AbsolutePath.TrimEnd('/').Equals(Endpoints.BillingEndpoints.WebhookPath, StringComparison.OrdinalIgnoreCase))
                            {
                                sameRoute.Add(url);
                            }
                        }

                        continue;
                    }

                    var state = endpoint.TryGetProperty("status", out var reported) && reported.ValueKind == JsonValueKind.String ? reported.GetString() ?? "" : "";
                    if (state.Length > 0 && !state.Equals("enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        return Add(lines, new(DoctorStatus.Fail, "stripe-webhook",
                            $"{wanted} is registered but {state} in Stripe: no event ever reaches the app. Enable it."));
                    }

                    var subscribed = new List<string>();
                    if (endpoint.TryGetProperty("enabled_events", out var events) && events.ValueKind == JsonValueKind.Array)
                    {
                        subscribed.AddRange(events.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String)
                            .Select(e => e.GetString() ?? ""));
                    }

                    if (subscribed.Contains("*", StringComparer.Ordinal))
                    {
                        return Add(lines, new(DoctorStatus.Ok, "stripe-webhook",
                            $"{wanted} is registered for every event (*), which covers the {WebhookEvents.Count.ToString(CultureInfo.InvariantCulture)} the app reads."));
                    }

                    var missing = WebhookEvents.Where(e => !subscribed.Contains(e, StringComparer.Ordinal)).ToList();
                    if (missing.Count == 0)
                    {
                        return Add(lines, new(DoctorStatus.Ok, "stripe-webhook",
                            $"{wanted} is registered for all {WebhookEvents.Count.ToString(CultureInfo.InvariantCulture)} events the app reads."));
                    }

                    var what = $"{wanted} is registered, but not for {string.Join(", ", missing)}: add {(missing.Count == 1 ? "that event" : "those events")} to the endpoint in Stripe";
                    return Add(lines, missing.Any(WebhookEventsThatMovePro.Contains)
                        ? new(DoctorStatus.Fail, "stripe-webhook", what + ", or Pro will not follow what people do.")
                        : new(DoctorStatus.Warn, "stripe-webhook",
                            what + ". Checkout still grants and ends Pro; what is lost is a subscription started on Stripe's own side."));
                }

                if (sameRoute.Count > 0)
                {
                    return Add(lines, new(DoctorStatus.Warn, "stripe-webhook",
                        $"no endpoint for {wanted}, but {string.Join(", ", sameRoute.Take(3))} posts to the same route on another name. That works while that name reaches this app; if it does not, point it here."));
                }

                var seen = others.Count == 0
                    ? "this account has no webhook endpoint at all"
                    : $"the {others.Count.ToString(CultureInfo.InvariantCulture)} endpoint(s) registered point elsewhere ({string.Join(", ", others.Take(3))}{(others.Count > 3 ? ", …" : "")})";
                return Add(lines, new(DoctorStatus.Fail, "stripe-webhook",
                    $"nothing posts to {wanted}: {seen}. Add it in Stripe (Developers, then Webhooks) with {string.Join(", ", WebhookEvents)}, and put its whsec_ in Billing__StripeWebhookSecret."));
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return Add(lines, new(DoctorStatus.Fail, "stripe-webhook", $"no answer from Stripe: {e.Message.TrimEnd('.')}."));
        }
    }

    /// <summary>A body Stripe answered with, or null when it is not JSON. A check never takes the run down over a body.</summary>
    private static JsonDocument? Json(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---- printing and plumbing ----

    private static void Print(DoctorReport report, TextWriter output, bool stripeOnly)
    {
        output.WriteLine(stripeOnly ? "OREVOSH doctor: the Stripe part" : "OREVOSH doctor");
        var width = report.Lines.Count == 0 ? 0 : report.Lines.Max(l => l.Name.Length);
        foreach (var line in report.Lines)
        {
            output.WriteLine($"  {Token(line.Status)} {line.Name.PadRight(width)}  {line.Detail}");
        }

        output.WriteLine();
        var skipped = report.Skipped == 0 ? "" : $", {report.Skipped} skipped";
        output.WriteLine(
            $"doctor: {report.Passes} ok, {report.Warnings} warning{(report.Warnings == 1 ? "" : "s")}, {report.Failures} failure{(report.Failures == 1 ? "" : "s")}{skipped}");
        output.WriteLine(report.Failures == 0
            ? report.Warnings == 0 ? "Ready." : "Ready, with warnings to read."
            : "Not ready: fix the failures above and run it again.");
    }

    /// <summary>The four-character verdict at the start of a line, so a script can grep for FAIL.</summary>
    private static string Token(DoctorStatus status) => status switch
    {
        DoctorStatus.Ok => "OK  ",
        DoctorStatus.Warn => "WARN",
        DoctorStatus.Fail => "FAIL",
        _ => "SKIP"
    };

    private static DoctorLine Add(List<DoctorLine> lines, DoctorLine line)
    {
        lines.Add(line);
        return line;
    }

    /// <summary>The section as the app would see it (defaults, then the overrides). A section that cannot be bound is that check's failure, not a crash.</summary>
    private static T Bind<T>(IConfiguration configuration, string section, List<DoctorLine> lines, string name) where T : new()
    {
        var options = new T();
        try
        {
            configuration.GetSection(section).Bind(options);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            lines.Add(new(DoctorStatus.Fail, name + "-config", $"the {section} section could not be read: {e.Message.TrimEnd('.')}. The server would not start."));
            return new T();
        }

        return options;
    }

    /// <summary>Email:PublicOrigin, or Billing:PublicOrigin when only that one is set; trailing slash trimmed.</summary>
    private static string Origin(IConfiguration configuration)
    {
        var email = (configuration["Email:PublicOrigin"] ?? "").Trim();
        var billing = (configuration["Billing:PublicOrigin"] ?? "").Trim();
        return (email.Length > 0 ? email : billing).TrimEnd('/');
    }

    /// <summary>The same rule Program.cs and DiskImageStore use: a relative path is anchored to the content root, never to the working directory.</summary>
    private static string Resolve(string path, string contentRoot) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path));

    /// <summary>Storage:FfmpegPath names either the binary or the folder it is in; empty means look on PATH, as the transcoder does.</summary>
    private static string Executable(string configured, string name)
    {
        var text = (configured ?? "").Trim();
        if (text.Length == 0)
        {
            return name;
        }

        return Directory.Exists(text) ? Path.Combine(text, name) : Path.Combine(Path.GetDirectoryName(text) ?? "", name);
    }

    /// <summary>
    /// The first line of <c>&lt;binary&gt; -version</c> up to the copyright, or null when the binary is not there, is not
    /// runnable, or does not answer in time. A bare name is looked up on PATH, as the transcoder looks it up.
    /// Any failure at all is "no answer": the only question here is whether the binary works, and a doctor check must
    /// never take the run down with it.
    /// </summary>
    private static string? FirstLineOf(string binary)
    {
        var info = new ProcessStartInfo(binary)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-version");
        try
        {
            using var process = new Process { StartInfo = info };
            process.Start();
            // Read while it runs: a full pipe nobody is reading would keep the child from ever exiting.
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            Task.WaitAll([output, error], ProcessTimeout);
            if (process.ExitCode != 0 || !output.IsCompletedSuccessfully)
            {
                return null;
            }

            var line = output.Result.Split('\n', 2)[0].Trim();
            var copyright = line.IndexOf(" Copyright", StringComparison.OrdinalIgnoreCase);
            return line.Length == 0 ? null : copyright > 0 ? line[..copyright] : line;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The mount the path really sits on: the longest root that is a prefix of it, so /data is not reported as /.</summary>
    private static DriveInfo? DriveFor(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            DriveInfo? best = null;
            foreach (var drive in DriveInfo.GetDrives())
            {
                var root = drive.RootDirectory.FullName;
                var covers = full.Equals(root, StringComparison.Ordinal)
                    || full.StartsWith(root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                if (covers && drive.IsReady && (best is null || root.Length > best.RootDirectory.FullName.Length))
                {
                    best = drive;
                }
            }

            return best;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // ---- Round 13 — money: the spend meter, the daily ceiling and the alerts ----

    /// <summary>
    /// The prices every estimate on the numbers page and the daily ceiling are built on, and whether there is a ceiling
    /// at all. WARN when <c>Limits__SpendPerDayUsd</c> is 0: nothing then stops a day of model calls costing whatever it
    /// costs (the count-based <c>Limits__ChecksPerDayGlobal</c> is still there, and is named, so the warning is honest).
    /// WARN too when a price is 0 or negative, because an estimate of nothing can never reach a ceiling.
    /// </summary>
    private static void Spend(List<DoctorLine> lines, AnthropicOptions anthropic, LimitsOptions limits)
    {
        var priceIn = anthropic.PriceInPerMillion;
        var priceOut = anthropic.PriceOutPerMillion;
        var ceiling = limits.SpendPerDayUsd;
        var prices = $"{Money(priceIn)} per million input tokens and {Money(priceOut)} per million output, for model {anthropic.Model}";
        var notes = new List<string>();
        if (priceIn <= 0 || priceOut <= 0)
        {
            notes.Add("a price of 0 makes every estimate 0, so the ceiling can never close and the money tiles read zero");
        }

        if (ceiling <= 0)
        {
            notes.Add($"Limits__SpendPerDayUsd is not set, so there is no ceiling on a day's model spend (Limits__ChecksPerDayGlobal still caps the number of calls at {limits.ChecksPerDayGlobal.ToString(CultureInfo.InvariantCulture)}); 5 USD is a sane pilot number");
        }

        lines.Add(notes.Count > 0
            ? new(DoctorStatus.Warn, "spend", $"{prices}. " + string.Join("; ", notes) + ". These are settings, not Anthropic's invoice: set them to your contract's prices.")
            : new(DoctorStatus.Ok, "spend",
                $"{prices}; the day stops at {Money(ceiling)} (Limits__SpendPerDayUsd). An estimate, not an invoice: set the prices to your contract's."));
    }

    /// <summary>
    /// Whether anything would shout. Neither channel set is a WARN: a readiness flip, the spend ceiling, a run of model
    /// failures and a full disk would then only be log lines nobody is watching. The URL and the address are never
    /// printed — the webhook URL is a secret and the address is a person.
    /// </summary>
    private static void Alerts(List<DoctorLine> lines, AlertOptions alerts, EmailOptions email)
    {
        var channels = new List<string>();
        if (!string.IsNullOrWhiteSpace(alerts.Webhook))
        {
            channels.Add(Uri.TryCreate(alerts.Webhook.Trim(), UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
                ? "a webhook is set"
                : "Alerts__Webhook is set but is not an absolute http(s) URL, so nothing can be posted to it");
        }

        var hasAddress = !string.IsNullOrWhiteSpace(alerts.Email);
        if (hasAddress)
        {
            channels.Add(email.Enabled ? "an address is set" : "Alerts__Email is set but mail is off (Email__Host, Email__From), so no alert mail can go out");
        }

        var thresholds = $"model failures over {alerts.ModelFailuresIn10Min.ToString(CultureInfo.InvariantCulture)} in ten minutes, free space under {alerts.DiskFreeMb.ToString(CultureInfo.InvariantCulture)} MB";
        var broken = channels.Any(c => c.Contains("but", StringComparison.Ordinal));
        lines.Add(channels.Count == 0
            ? new(DoctorStatus.Warn, "alerts",
                "neither Alerts__Webhook nor Alerts__Email is set: a readiness flip, the spend ceiling, a run of model failures, a full disk and a failed backup would only be log lines. Set one before the pilot.")
            : broken
                ? new(DoctorStatus.Warn, "alerts", string.Join("; ", channels) + $". {thresholds}.")
                : new(DoctorStatus.Ok, "alerts", string.Join(" and ", channels) + $"; {thresholds}. Run --doctor --live to send one."));
    }

    /// <summary>
    /// <c>--doctor --live</c>: one real alert down every configured channel, so the owner sees it land rather than
    /// trusting a setting. Skipped when no channel is set. It is a test line and says so; no secret travels with it.
    /// </summary>
    private static async Task<DoctorLine> AlertsLiveAsync(
        List<DoctorLine> lines, IConfiguration configuration, AlertOptions alerts, EmailOptions email, HttpMessageHandler? handler, CancellationToken ct)
    {
        if (!alerts.Enabled)
        {
            return Add(lines, new(DoctorStatus.Skip, "alerts-live", "no channel is set, so there is nothing to send a test to."));
        }

        var (webhook, sent) = await Alerter.SendOnceAsync(
            configuration, "this is a test alert from --doctor --live. Nothing is wrong.", null, handler, ct);
        var results = new List<string>();
        if (!string.IsNullOrWhiteSpace(alerts.Webhook))
        {
            results.Add(webhook ? "the webhook took it" : "the webhook did not take it (see the log line above for the status)");
        }

        if (!string.IsNullOrWhiteSpace(alerts.Email))
        {
            results.Add(!email.Enabled ? "mail is off, so no alert mail was sent"
                : LogEmailSender.IsLogHost(email) ? "Email__Host=log, so the alert mail went to the log"
                : sent ? "the alert mail went out" : "the alert mail could not be sent");
        }

        var bad = !webhook && !string.IsNullOrWhiteSpace(alerts.Webhook);
        return Add(lines, new(bad ? DoctorStatus.Fail : DoctorStatus.Ok, "alerts-live", string.Join("; ", results) + "."));
    }

    /// <summary>A USD amount as the checklist prints it: two decimals at least, never a currency the owner did not set.</summary>
    private static string Money(decimal amount) => amount.ToString("0.00##", CultureInfo.InvariantCulture) + " USD";

    /// <summary>A client for the live calls; <paramref name="handler"/> is the tests' stand-in for the network and is theirs to dispose.</summary>
    private static HttpClient Client(HttpMessageHandler? handler)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = LiveTimeout;
        return client;
    }
}
