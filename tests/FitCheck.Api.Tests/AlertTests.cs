using System.Net;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FitCheck.Api.Tests;

/// <summary>
/// The owner hears it before a user does (<see cref="Alerter"/>): what goes out, down which channel, how often, and
/// what must never be in it. The webhook goes to a recording handler and the mail to the suite's recording sender, so
/// nothing leaves the box; the clock is the fake one, so the hour-long throttle is tested without waiting an hour.
/// </summary>
public class AlertTests
{
    /// <summary>Stands in for Slack: records every post and answers with <see cref="StatusCode"/>.</summary>
    public sealed class RecordingAlertHandler : HttpMessageHandler
    {
        private readonly List<(Uri Url, string Body)> _posts = [];
        private readonly SemaphoreSlim _arrived = new(0);

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public IReadOnlyList<(Uri Url, string Body)> Posts
        {
            get
            {
                lock (_posts)
                {
                    return _posts.ToList();
                }
            }
        }

        /// <summary>The "text" field of every recorded post that has one, in order.</summary>
        public List<string> Texts => Posts
            .Select(p => JsonDocument.Parse(p.Body).RootElement)
            .Where(root => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("text", out _))
            .Select(root => root.GetProperty("text").GetString() ?? "")
            .ToList();

        public List<string> TextsContaining(string fragment) =>
            Texts.Where(t => t.Contains(fragment, StringComparison.Ordinal)).ToList();

        /// <summary>Waits until at least <paramref name="count"/> posts whose text carries the fragment have arrived.</summary>
        public async Task<List<string>> WaitForAsync(string fragment, int count = 1, int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                var matching = TextsContaining(fragment);
                if (matching.Count >= count)
                {
                    return matching;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !await _arrived.WaitAsync(remaining))
                {
                    return TextsContaining(fragment);
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            lock (_posts)
            {
                _posts.Add((request.RequestUri!, body));
            }

            _arrived.Release();
            return new HttpResponseMessage(StatusCode) { Content = new StringContent("ok") };
        }
    }

    /// <summary>The app with its alert webhook pointed at the recorder; mail already goes to the suite's recorder.</summary>
    public sealed class AlertApp : TestApp
    {
        /// <summary>A webhook URL shaped like Slack's, token and all: it must never appear in a message or a log line.</summary>
        public const string WebhookUrl = "https://hooks.test.invalid/services/T00000/B00000/SecretWebhookToken9x";

        public const string OwnerAddress = "owner@test.invalid";

        public RecordingAlertHandler Hook { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
                services.AddHttpClient(Alerter.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Hook));
        }

        public Alerter Alerter => Services.GetRequiredService<Alerter>();
        public SpendMeter Meter => Services.GetRequiredService<SpendMeter>();
    }

    /// <summary>An app with both channels on.</summary>
    private static AlertApp BothChannels(params (string Key, string Value)[] extra)
    {
        var app = new AlertApp
        {
            Settings =
            {
                ["Alerts:Webhook"] = AlertApp.WebhookUrl,
                ["Alerts:Email"] = AlertApp.OwnerAddress,
                // Out of the way unless a test asks for it: the watchdog's first pass runs at start.
                ["Alerts:DiskFreeMb"] = "0"
            }
        };
        foreach (var (key, value) in extra)
        {
            app.Settings[key] = value;
        }

        return app;
    }

    [Fact]
    public async Task One_alert_goes_down_both_channels_and_carries_no_secret()
    {
        using var app = BothChannels();
        Assert.True(await app.Alerter.RaiseAsync(Alerter.Kind.Test, "something worth waking up for."));

        var post = Assert.Single(app.Hook.TextsContaining("something worth waking up for"));
        Assert.StartsWith("OREVOSH: ", post, StringComparison.Ordinal);
        Assert.Equal(AlertApp.WebhookUrl, app.Hook.Posts.Single(p => p.Body.Contains("waking up", StringComparison.Ordinal)).Url.ToString());

        var mail = Assert.Single(app.Email.To(AlertApp.OwnerAddress).Where(m => m.Body.Contains("waking up", StringComparison.Ordinal)));
        Assert.Equal("OREVOSH alert", mail.Subject);
        Assert.Equal(post, mail.Body);

        // Nothing that would hurt in a shared channel: not the webhook's own token, not a key, not a password.
        foreach (var text in app.Hook.Texts.Concat(app.Email.Sent.Select(m => m.Body)))
        {
            Assert.DoesNotContain("SecretWebhookToken", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hooks.test.invalid", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-ant-", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_flapping_check_cannot_spam_one_kind_per_hour_and_no_more()
    {
        using var app = BothChannels();
        app.Clock.Now = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc);
        var alerter = app.Alerter;

        Assert.True(await alerter.RaiseAsync(Alerter.Kind.ReadinessFailing, "down once."));
        Assert.False(await alerter.RaiseAsync(Alerter.Kind.ReadinessFailing, "down twice."));
        Assert.False(await alerter.RaiseAsync(Alerter.Kind.ReadinessFailing, "down again."));
        Assert.Single(app.Hook.TextsContaining("down "));

        // A recovery is a different kind, so it is never swallowed by the failure in front of it.
        Assert.True(await alerter.RaiseAsync(Alerter.Kind.ReadinessOk, "up again."));
        Assert.Single(app.Hook.TextsContaining("up again"));

        // Fifty-nine minutes later the first kind is still quiet; an hour later it speaks again.
        app.Clock.Now = app.Clock.Now!.Value.AddMinutes(59);
        Assert.False(await alerter.RaiseAsync(Alerter.Kind.ReadinessFailing, "down inside the hour."));
        app.Clock.Now = app.Clock.Now!.Value.AddMinutes(2);
        Assert.True(await alerter.RaiseAsync(Alerter.Kind.ReadinessFailing, "down after the hour."));
        Assert.Single(app.Hook.TextsContaining("down after the hour"));
        Assert.Empty(app.Hook.TextsContaining("down inside the hour"));
    }

    [Fact]
    public async Task A_readiness_flip_raises_the_alert_and_a_steady_probe_does_not()
    {
        using var app = BothChannels();
        var readiness = new Readiness { Alerts = app.Alerter };
        var storage = new StorageOptions { Root = app.StorageRoot, Transcode = false };
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transcoder = app.Services.GetRequiredService<Transcoder>();

        // A first probe that is ready says nothing, and neither does the second.
        Assert.True((await readiness.CheckAsync(db, storage, app.Root, transcoder, NullLogger.Instance, CancellationToken.None)).Ok);
        Assert.True((await readiness.CheckAsync(db, storage, app.Root, transcoder, NullLogger.Instance, CancellationToken.None)).Ok);
        Assert.Empty(app.Hook.TextsContaining("readiness"));

        // The photo folder goes away under it: one alert, naming the failing check and nothing else.
        var gone = new StorageOptions { Root = Path.Combine(app.Root, "not-a-folder", "deeper"), Transcode = false };
        Assert.False((await readiness.CheckAsync(db, gone, app.Root, transcoder, NullLogger.Instance, CancellationToken.None)).Ok);
        var down = await app.Hook.WaitForAsync("readiness failed");
        Assert.Single(down);
        Assert.Contains("storage=not writable", down[0], StringComparison.Ordinal);
        Assert.DoesNotContain(app.Root, down[0], StringComparison.Ordinal);

        // A probe that keeps failing says nothing more; the flip back says it is over.
        Assert.False((await readiness.CheckAsync(db, gone, app.Root, transcoder, NullLogger.Instance, CancellationToken.None)).Ok);
        Assert.Single(app.Hook.TextsContaining("readiness failed"));
        Assert.True((await readiness.CheckAsync(db, storage, app.Root, transcoder, NullLogger.Instance, CancellationToken.None)).Ok);
        Assert.Single(await app.Hook.WaitForAsync("readiness is ok again"));
    }

    [Fact]
    public async Task The_spend_ceiling_is_announced_once_however_many_requests_are_refused()
    {
        using var app = BothChannels(("Limits:SpendPerDayUsd", "1"));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var meter = app.Meter;
        await Counters.IncrementAsync(db, SpendMeter.InPrefix + meter.Today, CancellationToken.None, 1_000_000);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(await meter.CeilingReachedAsync(db, CancellationToken.None));
        }

        var raised = await app.Hook.WaitForAsync("spend ceiling is reached");
        Assert.Single(raised);
        Assert.Contains("Limits:SpendPerDayUsd is 1.00", raised[0], StringComparison.Ordinal);
        Assert.Contains("2.00 USD", raised[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_model_alert_waits_for_more_failures_than_the_threshold_inside_ten_minutes()
    {
        using var app = BothChannels(("Alerts:ModelFailuresIn10Min", "3"));
        app.Clock.Now = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc);
        var alerter = app.Alerter;

        for (var i = 0; i < 3; i++)
        {
            await alerter.ModelFailedAsync();
        }

        Assert.Empty(app.Hook.TextsContaining("the stylist failed"));

        // The fourth inside the window is one over the threshold.
        await alerter.ModelFailedAsync();
        var raised = await app.Hook.WaitForAsync("the stylist failed");
        Assert.Single(raised);
        Assert.Contains("4 times in the last ten minutes", raised[0], StringComparison.Ordinal);

        // Failures that have aged out of the window do not count towards the next one.
        app.Clock.Now = app.Clock.Now!.Value.AddMinutes(30);
        for (var i = 0; i < 3; i++)
        {
            await alerter.ModelFailedAsync();
        }

        Assert.Single(app.Hook.TextsContaining("the stylist failed"));
    }

    [Fact]
    public async Task A_model_call_that_failed_at_the_api_feeds_the_window_through_the_meter()
    {
        using var app = BothChannels(("Alerts:ModelFailuresIn10Min", "1"));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var meter = app.Meter;

        await meter.RecordFailedAsync(new VisionUsage(120, 0));
        await meter.RecordFailedAsync(VisionUsage.Unknown);

        Assert.Single(await app.Hook.WaitForAsync("the stylist failed"));
        // Both calls were billed by somebody, so both are on the day's meter; only one carried tokens.
        var today = await meter.TodayAsync(db, CancellationToken.None);
        Assert.Equal(2, today.Calls);
        Assert.Equal(120, today.InputTokens);
    }

    /// <summary>
    /// Round 17 — the outage that was invisible.
    /// <para>
    /// A call that reaches Anthropic and fails there is billed, so it was counted, and the count fed the ten-minute
    /// window that raises the alert. A call that never left the machine — a connection refused, a name that did not
    /// resolve, a handshake that failed — is billed by nobody, so nothing was counted, so the window stayed at zero
    /// while every check in the app answered 502 and /healthz went on saying ok. The owner would have heard it from
    /// a user, or not at all.
    /// </para>
    /// <para>
    /// It is its own alert rather than a bigger number in the window, because counting is the right shape for a model
    /// answering badly and the wrong one for a model not answering. And it still records nothing: a meter that
    /// counted these would lie about a day the app did not spend.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_model_that_cannot_be_reached_is_shouted_about_and_costs_nothing()
    {
        // The window set high enough that it could not possibly be what fires.
        using var app = BothChannels(("Alerts:ModelFailuresIn10Min", "1000"));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var meter = app.Meter;

        await meter.ModelUnreachableAsync("Connection refused (api.anthropic.com:443)");

        var raised = await app.Hook.WaitForAsync("cannot be reached");
        Assert.Single(raised);
        Assert.Contains("502", raised[0], StringComparison.Ordinal);
        // And it says plainly that this one is not costing anything, so nobody goes looking at the bill.
        Assert.Contains("never left the machine", raised[0], StringComparison.Ordinal);

        // Nothing on the day's meter: no call, no tokens.
        var today = await meter.TodayAsync(db, CancellationToken.None);
        Assert.Equal(0, today.Calls);
        Assert.Equal(0, today.InputTokens);
    }

    /// <summary>
    /// Round 17 — a key wiped by a bad deploy. This threw in silence, and not even into the failure window, because
    /// nothing is counted for a call that was never built. One alert on the first check that meets it: a missing key
    /// is not a blip that might pass, and waiting for a count would mean five real people meeting a 502 first.
    /// </summary>
    [Fact]
    public async Task A_missing_api_key_is_shouted_about_once_and_names_the_command()
    {
        using var app = BothChannels(("Alerts:ModelFailuresIn10Min", "1000"));
        var meter = app.Meter;

        await meter.ModelKeyMissingAsync();

        var raised = await app.Hook.WaitForAsync("ANTHROPIC_API_KEY is not set");
        Assert.Single(raised);
        Assert.Contains("fly secrets set", raised[0], StringComparison.Ordinal);

        // A second check inside the quiet hour does not send a second alert: every kind is rate-limited the same way.
        await meter.ModelKeyMissingAsync();
        Assert.Single(app.Hook.TextsContaining("ANTHROPIC_API_KEY is not set"));
    }

    /// <summary>A socket that never opens: what a real unreachable Anthropic looks like from inside the client.</summary>
    private sealed class DeadSocketHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Attempts++;
            throw new HttpRequestException("Connection refused (api.anthropic.test:443)");
        }
    }

    /// <summary>
    /// Round 17 — the same outage through the REAL client, because the wiring is the part that was broken. The alert
    /// existing is not the fix; the client reaching it is. It also has to retry once first: a single reset is common,
    /// costs nothing to repeat, and shouting on the first one would cry wolf all day.
    /// </summary>
    [Fact]
    public async Task The_client_retries_a_dead_socket_once_and_then_raises_the_alert()
    {
        using var app = BothChannels(("Alerts:ModelFailuresIn10Min", "1000"));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var meter = app.Meter;

        Environment.SetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable, "test-key");
        var handler = new DeadSocketHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new AnthropicVisionClient(
            http,
            Microsoft.Extensions.Options.Options.Create(new AnthropicOptions { Model = "claude-sonnet-5", MaxTokens = 1200, BaseUrl = "https://api.anthropic.test/" }),
            NullLogger<AnthropicVisionClient>.Instance,
            meter);

        var request = new VisionRequest(
            OutfitAnalyzer.BuildSystemPrompt("en"),
            OutfitAnalyzer.BuildUserMessage(StyleIntent.Date, "dinner"),
            TestImages.Jpeg(64),
            "image/jpeg",
            OutfitAnalyzer.Tool);

        await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(request, CancellationToken.None));

        Assert.Equal(2, handler.Attempts);
        var raised = await app.Hook.WaitForAsync("cannot be reached");
        Assert.Single(raised);

        // Still nothing billed: two connections that never opened cost nothing and must not show on the meter.
        var today = await meter.TodayAsync(db, CancellationToken.None);
        Assert.Equal(0, today.Calls);
    }

    /// <summary>Round 17 — and the missing key, through the client, before a request is even built.</summary>
    [Fact]
    public async Task The_client_raises_the_missing_key_alert_before_it_builds_a_request()
    {
        using var app = BothChannels(("Alerts:ModelFailuresIn10Min", "1000"));
        var meter = app.Meter;

        Environment.SetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable, null);
        var handler = new DeadSocketHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new AnthropicVisionClient(
            http,
            Microsoft.Extensions.Options.Options.Create(new AnthropicOptions { Model = "claude-sonnet-5", MaxTokens = 1200, BaseUrl = "https://api.anthropic.test/" }),
            NullLogger<AnthropicVisionClient>.Instance,
            meter);

        try
        {
            var request = new VisionRequest(
                OutfitAnalyzer.BuildSystemPrompt("en"),
                OutfitAnalyzer.BuildUserMessage(StyleIntent.Date, "dinner"),
                TestImages.Jpeg(64),
                "image/jpeg",
                OutfitAnalyzer.Tool);

            await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(request, CancellationToken.None));
            Assert.Single(await app.Hook.WaitForAsync("ANTHROPIC_API_KEY is not set"));
            // Nothing was sent: the key is checked before a socket is opened.
            Assert.Equal(0, handler.Attempts);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable, "test-key");
        }
    }

    [Fact]
    public async Task The_app_says_it_started_and_names_its_version()
    {
        using var app = BothChannels();
        // Touching the app builds the host, which starts the watchdog.
        _ = app.Alerter;
        var started = await app.Hook.WaitForAsync("the app started");
        Assert.Single(started);
        Assert.Matches(@"version \S+\.", started[0]);
    }

    [Fact]
    public async Task A_data_volume_under_the_floor_is_shouted_about()
    {
        // A floor nothing on this machine can be above, so the watchdog's first pass raises it.
        using var app = BothChannels(("Alerts:DiskFreeMb", "2147483647"));
        _ = app.Alerter;
        var raised = await app.Hook.WaitForAsync("free, under Alerts:DiskFreeMb");
        Assert.Single(raised);
        Assert.DoesNotContain(app.StorageRoot, raised[0], StringComparison.Ordinal);
        Assert.NotNull(AlertWatchdog.FreeMegabytes(app.Root));
    }

    [Fact]
    public async Task A_channel_that_is_down_never_takes_the_app_with_it()
    {
        using var app = BothChannels();
        app.Hook.StatusCode = HttpStatusCode.InternalServerError;
        app.Email.Fail = true;

        // Both channels fail; the call still returns, and returns true, because the alert was raised.
        Assert.True(await app.Alerter.RaiseAsync(Alerter.Kind.Test, "the channels are both broken."));
        Assert.Single(app.Hook.TextsContaining("both broken"));

        app.Hook.StatusCode = HttpStatusCode.OK;
        app.Email.Fail = false;
        app.Clock.Now = DateTime.UtcNow.AddHours(2);
        Assert.True(await app.Alerter.RaiseAsync(Alerter.Kind.Test, "and now they are not."));
        Assert.Single(app.Email.To(AlertApp.OwnerAddress).Where(m => m.Body.Contains("now they are not", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task With_no_channel_set_an_alert_is_only_a_log_line()
    {
        using var app = new AlertApp();
        var alerter = app.Alerter;
        Assert.False(alerter.AnyChannel);
        Assert.False(alerter.WebhookSet);
        Assert.False(alerter.EmailSet);

        Assert.True(await alerter.RaiseAsync(Alerter.Kind.Test, "nobody is listening."));
        Assert.Empty(app.Hook.Posts);
        Assert.Empty(app.Email.Sent);
    }

    [Fact]
    public async Task An_address_without_a_mail_server_is_not_a_channel()
    {
        using var app = new AlertApp { EmailEnabled = false, Settings = { ["Alerts:Email"] = AlertApp.OwnerAddress } };
        Assert.False(app.Alerter.EmailSet);
        Assert.True(await app.Alerter.RaiseAsync(Alerter.Kind.Test, "into the void."));
        Assert.Empty(app.Email.Sent);
    }

    /// <summary>
    /// The path a process that never built the web host takes: the <c>--backup</c> command when a copy fails, and
    /// <c>--doctor --live</c>'s test alert. It reads the same two settings straight from the configuration.
    /// </summary>
    [Fact]
    public async Task A_process_without_a_host_can_still_shout()
    {
        var handler = new RecordingAlertHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Alerts:Webhook"] = AlertApp.WebhookUrl
        }).Build();

        var (webhook, email) = await Alerter.SendOnceAsync(configuration, "the backup failed: no space left on device. Nothing was copied.", null, handler);
        Assert.True(webhook);
        Assert.False(email);
        var post = Assert.Single(handler.Texts);
        Assert.Equal("OREVOSH: the backup failed: no space left on device. Nothing was copied.", post);

        // A webhook that refuses is reported, not thrown.
        handler.StatusCode = HttpStatusCode.BadGateway;
        var (refused, _) = await Alerter.SendOnceAsync(configuration, "again.", null, handler);
        Assert.False(refused);

        // No channel at all: nothing sent, nothing thrown.
        var silent = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        Assert.Equal((false, false), await Alerter.SendOnceAsync(silent, "nobody home.", null, handler));
    }

    // ---- the doctor's two lines ----

    private static (IConfiguration Configuration, string Root) DoctorSetup(params (string Key, string Value)[] settings)
    {
        var root = Path.Combine(Path.GetTempPath(), "fitcheck-alerts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "storage"));
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(root, "orevosh.db")}",
            ["Storage:Root"] = Path.Combine(root, "storage"),
            ["Storage:Transcode"] = "false",
            // Pinned: AnthropicVisionClientTests and SpendMeterTests set the real variable process-wide, and a --live
            // run that believes it has a key would send its Messages request down this test's own handler.
            ["ANTHROPIC_API_KEY"] = Doctor.StubApiKey
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        return (new ConfigurationBuilder().AddInMemoryCollection(values).Build(), root);
    }

    [Fact]
    public async Task The_doctor_prints_the_prices_in_use_and_warns_when_no_ceiling_is_set()
    {
        var (configuration, root) = DoctorSetup();
        var report = await Doctor.InspectAsync(configuration, root, live: false, stripeOnly: false);
        var spend = report["spend"]!;
        Assert.Equal(DoctorStatus.Warn, spend.Status);
        Assert.Contains("2.00 USD per million input tokens", spend.Detail, StringComparison.Ordinal);
        Assert.Contains("10.00 USD per million output", spend.Detail, StringComparison.Ordinal);
        Assert.Contains("Limits__SpendPerDayUsd is not set", spend.Detail, StringComparison.Ordinal);
        Assert.Contains("5 USD is a sane pilot number", spend.Detail, StringComparison.Ordinal);
        // A warning is the operator's call: neither new line may ever fail a run on its own.
        Assert.DoesNotContain(report.Lines.Where(l => l.Status == DoctorStatus.Fail), l => l.Name is "spend" or "alerts");

        var (withCeiling, root2) = DoctorSetup(("Limits:SpendPerDayUsd", "5"));
        var ok = await Doctor.InspectAsync(withCeiling, root2, live: false, stripeOnly: false);
        Assert.Equal(DoctorStatus.Ok, ok["spend"]!.Status);
        Assert.Contains("the day stops at 5.00 USD", ok["spend"]!.Detail, StringComparison.Ordinal);

        // A price of zero makes the ceiling unreachable, which is worse than no ceiling and is said out loud.
        var (free, root3) = DoctorSetup(("Limits:SpendPerDayUsd", "5"), ("Anthropic:PriceInPerMillion", "0"), ("Anthropic:PriceOutPerMillion", "0"));
        var zero = await Doctor.InspectAsync(free, root3, live: false, stripeOnly: false);
        Assert.Equal(DoctorStatus.Warn, zero["spend"]!.Status);
        Assert.Contains("a price of 0 makes every estimate 0", zero["spend"]!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_doctor_says_whether_a_channel_is_set_and_live_sends_one()
    {
        var (none, root) = DoctorSetup();
        var quiet = await Doctor.InspectAsync(none, root, live: false, stripeOnly: false);
        Assert.Equal(DoctorStatus.Warn, quiet["alerts"]!.Status);
        Assert.Contains("neither Alerts__Webhook nor Alerts__Email is set", quiet["alerts"]!.Detail, StringComparison.Ordinal);

        var (hooked, root2) = DoctorSetup(("Alerts:Webhook", AlertApp.WebhookUrl));
        var set = await Doctor.InspectAsync(hooked, root2, live: false, stripeOnly: false);
        Assert.Equal(DoctorStatus.Ok, set["alerts"]!.Status);
        Assert.Contains("a webhook is set", set["alerts"]!.Detail, StringComparison.Ordinal);
        // Never the URL itself: the checklist ends up in scrollback and screenshots.
        Assert.DoesNotContain("SecretWebhookToken", set["alerts"]!.Detail, StringComparison.OrdinalIgnoreCase);

        // An address with mail off cannot send, and the line says so rather than promising it can.
        var (mailless, root3) = DoctorSetup(("Alerts:Email", AlertApp.OwnerAddress));
        var half = await Doctor.InspectAsync(mailless, root3, live: false, stripeOnly: false);
        Assert.Equal(DoctorStatus.Warn, half["alerts"]!.Status);
        Assert.Contains("mail is off", half["alerts"]!.Detail, StringComparison.Ordinal);

        // --live really posts one, down the recorder.
        var handler = new RecordingAlertHandler();
        var live = await Doctor.InspectAsync(hooked, root2, live: true, stripeOnly: false, handler);
        Assert.Equal(DoctorStatus.Ok, live["alerts-live"]!.Status);
        Assert.Contains("the webhook took it", live["alerts-live"]!.Detail, StringComparison.Ordinal);
        Assert.Contains("this is a test alert", Assert.Single(handler.Texts), StringComparison.Ordinal);

        // With no channel there is nothing to test, which is a skip and never a failure.
        var skipped = await Doctor.InspectAsync(none, root, live: true, stripeOnly: false, new RecordingAlertHandler());
        Assert.Equal(DoctorStatus.Skip, skipped["alerts-live"]!.Status);
    }

    /// <summary>
    /// Round 16 — the webhook the owner will actually paste. Slack's incoming webhook takes { "text": ... } as it is;
    /// Discord answers 400 to it unless the URL carries Discord's own /slack compatibility suffix, and that 400 is a log
    /// line in a log nobody reads, so the channel looks configured and is dead. The suffix is added for a Discord
    /// webhook URL and for nothing else.
    /// </summary>
    [Fact]
    public void A_discord_webhook_url_is_posted_to_its_slack_endpoint_and_no_other_host_is_touched()
    {
        const string discord = "https://discord.com/api/webhooks/123456789/TokenABC";
        Assert.Equal(discord + "/slack", Alerter.SlackShaped(discord));
        Assert.Equal(discord + "/slack", Alerter.SlackShaped(discord + "/"));
        Assert.Equal("https://discordapp.com/api/webhooks/1/t/slack", Alerter.SlackShaped("https://discordapp.com/api/webhooks/1/t"));
        Assert.Equal("https://ptb.discord.com/api/webhooks/1/t/slack", Alerter.SlackShaped("https://ptb.discord.com/api/webhooks/1/t"));

        // Already suffixed, a different Discord route, another host, and something that is not a URL at all: untouched.
        Assert.Equal(discord + "/slack", Alerter.SlackShaped(discord + "/slack"));
        Assert.Equal("https://discord.com/api/channels/1", Alerter.SlackShaped("https://discord.com/api/channels/1"));
        Assert.Equal(AlertApp.WebhookUrl, Alerter.SlackShaped(AlertApp.WebhookUrl));
        Assert.Equal("https://hooks.slack.com/services/T/B/X", Alerter.SlackShaped("https://hooks.slack.com/services/T/B/X"));
        Assert.Equal("not a url", Alerter.SlackShaped("not a url"));
        Assert.Equal("", Alerter.SlackShaped(""));
    }

    /// <summary>The rewrite is what the HTTP client is actually handed, not merely what a helper returns.</summary>
    [Fact]
    public async Task The_post_goes_to_the_rewritten_url()
    {
        using var app = new AlertApp { Settings = { ["Alerts:Webhook"] = "https://discord.com/api/webhooks/42/SecretToken" } };
        _ = app.NewClient();
        Assert.True(await app.Alerter.RaiseAsync(Alerter.Kind.Test, "this is a test alert"));
        await app.Hook.WaitForAsync("this is a test alert");

        var posted = Assert.Single(app.Hook.Posts.Where(p => p.Body.Contains("this is a test alert", StringComparison.Ordinal)));
        Assert.Equal("https://discord.com/api/webhooks/42/SecretToken/slack", posted.Url.ToString());
    }
}
