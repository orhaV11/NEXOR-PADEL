using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Domain;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// The owner hears it before a user does. One sentence, to a webhook (<c>Alerts:Webhook</c>, Slack/Discord-shaped
/// <c>{ "text": "..." }</c>) and/or to one address (<c>Alerts:Email</c>, through the app's own
/// <see cref="IEmailSender"/>), and always to the log — with neither channel set the log line is the whole alert, which
/// is what the doctor says.
/// <para>
/// <b>At most one alert per kind per hour.</b> A check that flaps (ready, not ready, ready) would otherwise write a
/// message every few seconds; the throttle is per <see cref="Kind"/>, in memory, over one process, and "readiness went
/// down" and "readiness came back" are different kinds on purpose, so a recovery is never swallowed by the failure
/// before it. A suppressed alert is still logged at Debug, so the log remains the full record.
/// </para>
/// <para>
/// <b>Never a secret.</b> Callers pass a sentence that names what happened, a number and, at most, a setting's NAME.
/// Nothing here reads a key, a password, a token or a person's data, and the webhook URL itself (a secret: whoever has
/// it can post to the channel) is never written into a message or a log line.
/// </para>
/// </summary>
public sealed class Alerter(
    IHttpClientFactory http,
    IEmailSender email,
    IOptions<AlertOptions> options,
    IOptions<EmailOptions> mail,
    IClock clock,
    ILogger<Alerter> logger)
{
    /// <summary>The named client the webhook posts go through, so a test can put a recording handler behind it.</summary>
    public const string HttpClientName = "alerts";

    /// <summary>One alert of a kind per this long. Not configurable: it is a floor under the noise, not a product knob.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromHours(1);

    /// <summary>How far back the model-failure window looks.</summary>
    public static readonly TimeSpan ModelFailureWindow = TimeSpan.FromMinutes(10);

    /// <summary>The prefix every message carries, so an alert in a shared channel says which app it is.</summary>
    public const string Prefix = "OREVOSH";

    /// <summary>The event kinds. The string is the throttle's key and the log's; keep them stable, they end up in runbooks.</summary>
    public static class Kind
    {
        /// <summary>The app started (one per process, with the build's version).</summary>
        public const string Started = "started";

        /// <summary>/readyz flipped to failing.</summary>
        public const string ReadinessFailing = "readiness.failing";

        /// <summary>/readyz flipped back to ready.</summary>
        public const string ReadinessOk = "readiness.ok";

        /// <summary>Today's estimated spend reached Limits:SpendPerDayUsd and the stylist is resting.</summary>
        public const string SpendCeiling = "spend.ceiling";

        /// <summary>More than Alerts:ModelFailuresIn10Min failed model calls in ten minutes.</summary>
        public const string ModelFailing = "model.failing";

        /// <summary>Free space on the data volume is below Alerts:DiskFreeMb.</summary>
        public const string DiskLow = "disk.low";

        /// <summary>A --backup run did not write a copy.</summary>
        public const string BackupFailed = "backup.failed";

        /// <summary>Stripe reversed a payment (a refund or a dispute) and Pro was removed.</summary>
        public const string BillingReversed = "billing.reversed";

        /// <summary>What <c>--doctor --live</c> sends to prove the channels work.</summary>
        public const string Test = "test";
    }

    private readonly ConcurrentDictionary<string, DateTime> _lastSent = new(StringComparer.Ordinal);
    private readonly object _failures = new();
    private readonly List<DateTime> _modelFailures = [];

    /// <summary>Whether anything would actually leave the box, as the doctor and the metrics report it.</summary>
    public bool WebhookSet => !string.IsNullOrWhiteSpace(options.Value.Webhook);

    /// <summary>An address is configured AND mail itself is on; an address without a mail server sends nothing.</summary>
    public bool EmailSet => !string.IsNullOrWhiteSpace(options.Value.Email) && email.Enabled;

    public bool AnyChannel => WebhookSet || EmailSet;

    /// <summary>
    /// Raises one alert, unless the same kind was raised less than <see cref="Quiet"/> ago. Never throws: an alert that
    /// cannot be delivered is logged and the caller carries on — nothing in the app may fail because the channel is down.
    /// Returns true when this call actually sent (or tried to send) rather than being swallowed by the throttle.
    /// </summary>
    public async Task<bool> RaiseAsync(string kind, string text, CancellationToken ct = default)
    {
        if (!Allow(kind))
        {
            logger.LogDebug("Alert {Kind} suppressed (one per {Quiet} minutes): {Text}", kind, Quiet.TotalMinutes, text);
            return false;
        }

        var message = $"{Prefix}: {text}";
        logger.LogWarning("Alert {Kind}: {Text}", kind, text);
        await SendAsync(options.Value, mail.Value, message, ct);
        return true;
    }

    /// <summary>
    /// The same, for a caller that cannot await (a readiness probe, a background loop). The send runs on its own and its
    /// failures are logged, never rethrown into the caller.
    /// </summary>
    public void Raise(string kind, string text)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await RaiseAsync(kind, text, CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Alert {Kind} could not be raised.", kind);
            }
        });
    }

    /// <summary>
    /// One failed model call. When more than <c>Alerts:ModelFailuresIn10Min</c> of them land inside
    /// <see cref="ModelFailureWindow"/>, the model alert goes out (and then not again for an hour). The window is in
    /// memory over one process: two instances each count their own, which is the honest thing a box can say by itself.
    /// </summary>
    public Task ModelFailedAsync(CancellationToken ct = default)
    {
        var threshold = options.Value.ModelFailuresIn10Min;
        if (threshold <= 0)
        {
            return Task.CompletedTask;
        }

        var now = clock.UtcNow;
        int count;
        lock (_failures)
        {
            _modelFailures.RemoveAll(at => at < now - ModelFailureWindow);
            _modelFailures.Add(now);
            count = _modelFailures.Count;
        }

        return count > threshold
            ? RaiseAsync(Kind.ModelFailing,
                $"the stylist failed {count.ToString(CultureInfo.InvariantCulture)} times in the last ten minutes (Alerts:ModelFailuresIn10Min is {threshold.ToString(CultureInfo.InvariantCulture)}). Checks are answering 502.", ct)
            : Task.CompletedTask;
    }

    /// <summary>The version this build reports, for the "started" line. Never a path, never a commit nobody can read.</summary>
    public static string Version =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>Whether this kind may speak now, and if so, that it has. One dictionary entry per kind, set on the way past.</summary>
    private bool Allow(string kind)
    {
        var now = clock.UtcNow;
        var allowed = true;
        _lastSent.AddOrUpdate(kind, now, (_, last) =>
        {
            if (now - last < Quiet)
            {
                allowed = false;
                return last;
            }

            return now;
        });

        return allowed;
    }

    private async Task SendAsync(AlertOptions alerts, EmailOptions mailOptions, string message, CancellationToken ct)
    {
        var webhook = alerts.Webhook.Trim();
        if (webhook.Length > 0)
        {
            try
            {
                using var client = http.CreateClient(HttpClientName);
                var body = JsonSerializer.Serialize(new { text = message });
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(webhook, content, ct);
                if (!response.IsSuccessStatusCode)
                {
                    // The URL is a secret, so the status is all that goes in the log.
                    logger.LogWarning("The alert webhook answered {Status}.", (int)response.StatusCode);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "The alert webhook could not be reached.");
            }
        }

        var address = alerts.Email.Trim();
        if (address.Length > 0 && mailOptions.Enabled)
        {
            try
            {
                await email.SendAsync(new EmailMessage(address, Prefix + " alert", message), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "The alert mail to the owner's address could not be sent.");
            }
        }
    }

    /// <summary>
    /// One alert from a process that never built the web host: the <c>--backup</c> command and <c>--doctor --live</c>'s
    /// test alert. Reads the same two settings straight from the configuration, posts the webhook and sends the mail,
    /// and returns which channels answered. Nothing is thrown at the caller and no secret is printed.
    /// </summary>
    public static async Task<(bool Webhook, bool Email)> SendOnceAsync(
        IConfiguration configuration, string text, ILogger? logger = null, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        var alerts = new AlertOptions();
        configuration.GetSection(AlertOptions.Section).Bind(alerts);
        var mailOptions = new EmailOptions();
        configuration.GetSection(EmailOptions.Section).Bind(mailOptions);
        var message = $"{Prefix}: {text}";
        var sentWebhook = false;
        var sentEmail = false;

        var webhook = alerts.Webhook.Trim();
        if (webhook.Length > 0)
        {
            try
            {
                using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
                client.Timeout = TimeSpan.FromSeconds(10);
                using var content = new StringContent(JsonSerializer.Serialize(new { text = message }), Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(webhook, content, ct);
                sentWebhook = response.IsSuccessStatusCode;
                if (!sentWebhook)
                {
                    logger?.LogWarning("The alert webhook answered {Status}.", (int)response.StatusCode);
                }
            }
            catch (Exception e)
            {
                logger?.LogWarning(e, "The alert webhook could not be reached.");
            }
        }

        var address = alerts.Email.Trim();
        if (address.Length > 0 && mailOptions.Enabled && !LogEmailSender.IsLogHost(mailOptions))
        {
            try
            {
                var sender = new SmtpEmailSender(Options.Create(mailOptions), LoggerOf<SmtpEmailSender>(logger));
                await sender.SendAsync(new EmailMessage(address, Prefix + " alert", message), ct);
                sentEmail = true;
            }
            catch (Exception e)
            {
                logger?.LogWarning(e, "The alert mail to the owner's address could not be sent.");
            }
        }

        return (sentWebhook, sentEmail);
    }

    private static ILogger<T> LoggerOf<T>(ILogger? logger) =>
        logger as ILogger<T> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
}

/// <summary>
/// The two alerts nothing else would ever raise: one "the app started" line with the version, once, at boot, and the
/// free space on the data volume, every <see cref="Interval"/> after that. Everything else is raised where it happens
/// (a readiness flip, the spend ceiling, a run of model failures, a failed backup).
/// </summary>
public sealed class AlertWatchdog(
    Alerter alerter,
    IOptions<AlertOptions> options,
    IOptions<StorageOptions> storage,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AlertWatchdog> logger) : BackgroundService
{
    /// <summary>How often the free space is looked at. Often enough to warn before a disk fills, far too rare to cost anything.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await alerter.RaiseAsync(Alerter.Kind.Started, $"the app started, version {Alerter.Version}.", stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDiskAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "The disk-space watch failed; it will try again.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task CheckDiskAsync(CancellationToken ct)
    {
        var floor = options.Value.DiskFreeMb;
        if (floor <= 0)
        {
            return;
        }

        var free = FreeMegabytes(DataPath());
        if (free is { } megabytes && megabytes < floor)
        {
            await alerter.RaiseAsync(Alerter.Kind.DiskLow,
                $"the data volume has {megabytes.ToString(CultureInfo.InvariantCulture)} MB free, under Alerts:DiskFreeMb ({floor.ToString(CultureInfo.InvariantCulture)} MB). Uploads, backups and the database's journal all need room.",
                ct);
        }
    }

    /// <summary>The photo folder's volume: that is where uploads, the database and the backups all land.</summary>
    private string DataPath()
    {
        var root = storage.Value.Root;
        var path = Path.IsPathRooted(root) ? root : Path.Combine(environment.ContentRootPath, root);
        // The folder may not exist yet on a first start; the nearest existing parent sits on the same volume.
        while (!Directory.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent) || parent == path)
            {
                return environment.ContentRootPath;
            }

            path = parent;
        }

        _ = configuration;
        return path;
    }

    /// <summary>Free megabytes on the volume the path sits on, or null when this machine will not say.</summary>
    public static long? FreeMegabytes(string path)
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

            return best is null ? null : best.AvailableFreeSpace / (1024 * 1024);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
