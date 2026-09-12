using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace FitCheck.Api.Services;

/// <summary>
/// One push to send: the same facts as an activity row. The worker turns them into text in the recipient's language.
/// <paramref name="NotificationId"/> is the activity row the job announces; the worker waits for it to be committed and drops
/// the job if it never is. Null for a push with no row behind it (the test ping).
/// </summary>
/// <summary>Rank: the place on the board for a board_rank push, where the line reads it instead of the actor's name.</summary>
public sealed record PushJob(Guid UserId, string Type, string ActorHandle, Guid? PostId, Guid? ChallengeId, Guid? NotificationId = null, int? Rank = null);

/// <summary>
/// Sends Web Push messages in the background. <see cref="Notifier"/> drops a <see cref="PushJob"/> on the queue next to
/// every activity row it writes; this worker picks it up after the request has moved on, so a slow or failing push
/// service can never delay or fail a request. The job is queued before the row is committed (the request saves after the
/// notifier returns), so the worker first confirms the row exists, polling for a few seconds, and lets the job go when the
/// request rolled back. Nothing is queued when the VAPID keys are not configured.
/// </summary>
public sealed class PushSender : BackgroundService
{
    /// <summary>The named HttpClient the push requests go through; tests swap its handler.</summary>
    public const string HttpClientName = "push";

    public const string Title = "OREVOSH";

    /// <summary>A day: the phone is usually back online by then; older pings are stale anyway.</summary>
    public const int TimeToLiveSeconds = 86400;

    /// <summary>How long the worker gives a request to commit its activity row: 20 looks, 250 ms apart, five seconds in all.</summary>
    public const int ConfirmAttempts = 20;
    public const int ConfirmIntervalMs = 250;

    private const int QueueCapacity = 2000;

    private readonly Channel<PushJob> _queue = Channel.CreateBounded<PushJob>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest
    });

    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _httpClients;
    private readonly Localizer _localizer;
    private readonly ILogger<PushSender> _logger;
    private readonly PushOptions _options;
    private readonly VapidAuthentication? _vapid;
    private readonly int _confirmAttempts;
    private readonly TimeSpan _confirmInterval;

    /// <summary>The last two parameters shorten the confirmation window for tests; the container leaves them at their defaults.</summary>
    public PushSender(
        IServiceScopeFactory scopes, IHttpClientFactory httpClients, Localizer localizer, IOptions<PushOptions> options, ILogger<PushSender> logger,
        int confirmAttempts = ConfirmAttempts, int confirmIntervalMs = ConfirmIntervalMs)
    {
        _scopes = scopes;
        _httpClients = httpClients;
        _localizer = localizer;
        _logger = logger;
        _options = options.Value;
        _confirmAttempts = Math.Max(1, confirmAttempts);
        _confirmInterval = TimeSpan.FromMilliseconds(Math.Max(0, confirmIntervalMs));
        if (_options.Enabled)
        {
            try
            {
                _vapid = new VapidAuthentication(_options.PublicKey, _options.PrivateKey) { Subject = _options.Subject };
            }
            catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
            {
                // A typo in an environment variable must not take the app down; push simply stays off and the log says why.
                _logger.LogError(e, "Push:PublicKey / Push:PrivateKey are not a valid VAPID key pair (run with --vapid to generate one); push is off.");
            }
        }
    }

    /// <summary>True when the keys are configured and usable.</summary>
    public bool Enabled => _vapid is not null;

    /// <summary>Queues a push. Never blocks, never throws: when the queue is full the oldest job is dropped.</summary>
    public void Enqueue(PushJob job)
    {
        if (Enabled)
        {
            _queue.Writer.TryWrite(job);
        }
    }

    /// <summary>
    /// A fresh VAPID key pair (NIST P-256) in the form Web Push wants: the public key is the base64url of the 65-byte
    /// uncompressed point (what pushManager.subscribe takes), the private key the base64url of the 32-byte scalar.
    /// </summary>
    public static (string PublicKey, string PrivateKey) GenerateVapidKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: true);
        var point = new byte[65];
        point[0] = 0x04;
        parameters.Q.X!.CopyTo(point, 1);
        parameters.Q.Y!.CopyTo(point, 33);
        return (Base64Url(point), Base64Url(parameters.D!));
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes base64url (padding optional). Null when the text is not base64url at all.</summary>
    public static byte[]? FromBase64Url(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var base64 = text.Trim().Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Where a tap on the notification lands: the look, the challenge, the person, or the activity list.</summary>
    public static string UrlFor(PushJob job)
    {
        switch (job.Type)
        {
            case NotificationType.Entry:
            case NotificationType.Ended:
            case NotificationType.Won:
                if (job.ChallengeId is { } challengeId)
                {
                    return $"/#/challenge/{challengeId}";
                }

                break;
            case NotificationType.Follow:
                return $"/#/u/{Uri.EscapeDataString(job.ActorHandle)}";
            case NotificationType.BoardRank:
                return "/#/board";
        }

        if (job.PostId is { } postId)
        {
            return $"/#/post/{postId}";
        }

        if (job.ChallengeId is { } otherChallengeId)
        {
            return $"/#/challenge/{otherChallengeId}";
        }

        return "/#/activity";
    }

    /// <summary>Repeats about the same thing replace each other on the phone (notification tag) and at the push service (topic).</summary>
    public static string TagFor(PushJob job)
    {
        var id = job.PostId ?? job.ChallengeId;
        return id is null ? $"{job.Type}:{job.ActorHandle.ToLowerInvariant()}" : $"{job.Type}:{id.Value:N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // A push is best effort; the activity row is already there for the next open.
                _logger.LogInformation(e, "Push {Type} to {UserId} failed", job.Type, job.UserId);
            }
        }
    }

    /// <summary>
    /// Whether the activity row the job announces has been committed. The request that queued the job usually saves within
    /// milliseconds; a request that failed after queuing never does, and its push must not go out. A fresh scope each look, so
    /// every read is a new query rather than a cached miss.
    /// </summary>
    private async Task<bool> NotificationCommittedAsync(Guid notificationId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (await db.Notifications.AnyAsync(n => n.Id == notificationId, ct))
                {
                    return true;
                }
            }

            if (attempt >= _confirmAttempts)
            {
                return false;
            }

            await Task.Delay(_confirmInterval, ct);
        }
    }

    private async Task SendAsync(PushJob job, CancellationToken ct)
    {
        if (_vapid is null)
        {
            return;
        }

        if (job.NotificationId is { } notificationId && !await NotificationCommittedAsync(notificationId, ct))
        {
            _logger.LogInformation("Push {Type} to {UserId} dropped: its activity row {NotificationId} was never committed", job.Type, job.UserId, notificationId);
            return;
        }

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var recipient = await db.Users.Where(u => u.Id == job.UserId).Select(u => new { u.PreferredLanguage }).FirstOrDefaultAsync(ct);
        if (recipient is null)
        {
            return;
        }

        var subscriptions = await db.PushSubscriptions.Where(s => s.UserId == job.UserId).ToListAsync(ct);
        if (subscriptions.Count == 0)
        {
            return;
        }

        // The actor's display name is looked up now, like the activity list does, so a rename shows through.
        var actorLower = job.ActorHandle.ToLowerInvariant();
        var actor = await db.Users.Where(u => u.HandleLower == actorLower).Select(u => new { u.Handle, u.DisplayName }).FirstOrDefaultAsync(ct);
        var actorName = actor is null ? job.ActorHandle : PostReader.NameOf(actor.Handle, actor.DisplayName);

        // "You finished #{0} this week" takes the rank where every other line takes the actor.
        var argument = job.Type == NotificationType.BoardRank ? (job.Rank?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "") : actorName;
        var payload = JsonSerializer.Serialize(new PushPayload(
            Title, _localizer.Get(recipient.PreferredLanguage, "push." + job.Type, argument), UrlFor(job), TagFor(job), job.Type), AppJson.Options);

        var client = new PushServiceClient(_httpClients.CreateClient(HttpClientName))
        {
            DefaultAuthentication = _vapid,
            DefaultTimeToLive = TimeToLiveSeconds,
            AutoRetryAfter = false
        };

        foreach (var subscription in subscriptions)
        {
            var target = new WebPushSubscription { Endpoint = subscription.Endpoint };
            target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
            target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
            var message = new PushMessage(payload) { Urgency = PushMessageUrgency.Normal, Topic = TopicFor(job) };
            try
            {
                await client.RequestPushMessageDeliveryAsync(target, message, ct);
                var now = DateTime.UtcNow;
                await db.PushSubscriptions.Where(s => s.Id == subscription.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, now), ct);
            }
            catch (PushServiceClientException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // The browser dropped the subscription (or the person revoked it): forget it.
                await db.PushSubscriptions.Where(s => s.Id == subscription.Id).ExecuteDeleteAsync(ct);
                _logger.LogInformation("Push subscription {Id} is gone ({Status}); removed", subscription.Id, (int)e.StatusCode);
            }
            catch (PushServiceClientException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // The subscription was made against other VAPID keys (the pair was regenerated): it will never accept ours.
                // Forget it; the client sees "not subscribed" and subscribes again with the current public key.
                await db.PushSubscriptions.Where(s => s.Id == subscription.Id).ExecuteDeleteAsync(ct);
                _logger.LogInformation("Push subscription {Id} refuses our VAPID keys ({Status}); removed", subscription.Id, (int)e.StatusCode);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                // A push service that is down or a key it will not take: one line, no stack trace, and on to the next browser.
                _logger.LogInformation("Push {Type} to subscription {Id} failed: {Reason}", job.Type, subscription.Id, e.Message);
            }
        }
    }

    /// <summary>The push service's own collapse key: at most 32 base64url characters, so the id is shortened.</summary>
    private static string? TopicFor(PushJob job)
    {
        var id = job.PostId ?? job.ChallengeId;
        return id is null ? null : $"{job.Type}-{id.Value:N}"[..Math.Min(32, job.Type.Length + 33)];
    }

    public override void Dispose()
    {
        _vapid?.Dispose();
        base.Dispose();
    }

    /// <summary>What the service worker reads: the title, the sentence, where a tap goes, and the tag that collapses repeats.</summary>
    private sealed record PushPayload(string Title, string Body, string Url, string Tag, string Type);
}
