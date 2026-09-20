using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FitCheck.Api.Tests;

/// <summary>
/// The operator's tools: the <c>--doctor</c> checklist and its exit code, the readiness line, the optional request log,
/// and the retention <c>--backup --keep</c> applies. Nothing here is a feature anyone using the app can see; all of it is
/// what the person deploying it reads before and after a release.
/// </summary>
public class DoctorTests : IDisposable
{
    private const string LiveKey = "sk-ant-api03-not-a-real-key-000000";
    private const string LiveSecret = "sk_live_51NotARealStripeKey";
    private const string WebhookSecret = "whsec_NotARealWebhookSecret";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fitcheck-tests", "ops-" + Guid.NewGuid().ToString("N"));

    public DoctorTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "storage"));
    }

    /// <summary>A server that is ready: every setting filled the way DEPLOY.md's go-live section says to fill it.</summary>
    private Dictionary<string, string> Healthy() => new()
    {
        ["ANTHROPIC_API_KEY"] = LiveKey,
        ["ConnectionStrings:Default"] = $"Data Source={Path.Combine(_root, "orevosh.db")}",
        ["Storage:Root"] = Path.Combine(_root, "storage"),
        ["Storage:Transcode"] = "false",
        ["Email:Host"] = "smtp.example.net",
        ["Email:From"] = "OREVOSH <hello@orevosh.example>",
        ["Email:User"] = "apikey",
        ["Email:Password"] = "s3cr3t-mail-password",
        ["Email:PublicOrigin"] = "https://orevosh.example",
        ["Billing:Provider"] = "stripe",
        ["Billing:StripeSecretKey"] = LiveSecret,
        ["Billing:StripePriceId"] = "price_1NotAReal",
        ["Billing:StripeWebhookSecret"] = WebhookSecret,
        ["Billing:PublicOrigin"] = "https://orevosh.example",
        ["Push:PublicKey"] = "BPublicKeyThatIsPublicAnyway",
        ["Push:PrivateKey"] = "PrivateKeyNobodyMaySee",
        ["Push:Subject"] = "mailto:hello@orevosh.example",
        ["Admin:Handles:0"] = "orhav",
        ["Affiliate:Hosts:amazon.com"] = "tag=orevosh-20"
    };

    private static IConfiguration Config(Dictionary<string, string> settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private Task<DoctorReport> Inspect(Dictionary<string, string> settings, bool live = false, bool stripeOnly = false, HttpMessageHandler? handler = null) =>
        Doctor.InspectAsync(Config(settings), _root, live, stripeOnly, handler);

    private async Task<(int Exit, string Text)> Run(Dictionary<string, string> settings, bool live = false, bool stripeOnly = false, HttpMessageHandler? handler = null)
    {
        var writer = new StringWriter();
        var exit = await Doctor.RunAsync(Config(settings), _root, live, stripeOnly, writer, handler);
        return (exit, writer.ToString());
    }

    [Fact]
    public async Task A_server_that_is_ready_passes_every_check_and_exits_0()
    {
        var settings = Healthy();
        var report = await Inspect(settings);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(0, report.Failures);
        // The database file does not exist yet, which is a warning and not a failure: the first start creates it.
        Assert.Equal(DoctorStatus.Warn, report["database"]!.Status);
        Assert.Contains("does not exist yet", report["database"]!.Detail);

        foreach (var name in new[] { "origin", "anthropic", "anthropic-url", "email", "billing", "plans", "push", "admin", "board", "affiliate", "storage", "ffmpeg", "disk" })
        {
            Assert.Equal(DoctorStatus.Ok, report[name]?.Status);
        }

        var (exit, text) = await Run(settings);
        Assert.Equal(0, exit);
        Assert.Contains("0 failures", text);
        Assert.Contains("Ready", text);
        Assert.DoesNotContain("FAIL", text);
    }

    /// <summary>The whole point of the tool: a key, a password or a webhook secret must never reach the operator's scrollback, a CI log or a screenshot.</summary>
    [Fact]
    public async Task No_secret_is_ever_printed()
    {
        var settings = Healthy();
        var (_, text) = await Run(settings, live: true, handler: new CannedHandler(HttpStatusCode.OK));
        foreach (var secret in new[] { LiveKey, LiveSecret, WebhookSecret, "s3cr3t-mail-password", "PrivateKeyNobodyMaySee", "BPublicKeyThatIsPublicAnyway" })
        {
            Assert.DoesNotContain(secret, text);
        }

        // The shape of a key is reported instead: enough to catch a placeholder, useless to anyone reading over a shoulder.
        Assert.Contains("sk-ant-…", text);
        Assert.Contains("sk_live_…", text);
        Assert.Contains("webhook secret set", text);
    }

    [Fact]
    public async Task A_missing_or_stub_anthropic_key_fails_the_run()
    {
        var missing = Healthy();
        missing["ANTHROPIC_API_KEY"] = "";
        var (exit, text) = await Run(missing);
        Assert.Equal(1, exit);
        Assert.Contains("FAIL", text);
        Assert.Contains("is not set", (await Inspect(missing))["anthropic"]!.Detail);

        var stub = Healthy();
        stub["ANTHROPIC_API_KEY"] = Doctor.StubApiKey;
        var report = await Inspect(stub);
        Assert.Equal(DoctorStatus.Fail, report["anthropic"]!.Status);
        Assert.Contains("stub key", report["anthropic"]!.Detail);
        Assert.Equal(1, report.ExitCode);

        // A key of an unexpected shape is a warning, not a refusal: the prefix is Anthropic's to change, not ours.
        var odd = Healthy();
        odd["ANTHROPIC_API_KEY"] = "some-other-shape";
        Assert.Equal(DoctorStatus.Warn, (await Inspect(odd))["anthropic"]!.Status);
    }

    [Fact]
    public async Task A_base_url_that_is_not_anthropic_is_a_warning_and_http_origins_are_too()
    {
        var stubbed = Healthy();
        stubbed["Anthropic:BaseUrl"] = "http://127.0.0.1:6041";
        var report = await Inspect(stubbed);
        Assert.Equal(DoctorStatus.Warn, report["anthropic-url"]!.Status);
        Assert.Equal(0, report.ExitCode);

        var plain = Healthy();
        plain["Email:PublicOrigin"] = "http://orevosh.example";
        plain["Billing:PublicOrigin"] = "http://orevosh.example";
        var http = await Inspect(plain);
        Assert.Equal(DoctorStatus.Warn, http["origin"]!.Status);
        Assert.Contains("is http, not https", http["origin"]!.Detail);

        var none = Healthy();
        none["Email:PublicOrigin"] = "";
        none["Billing:PublicOrigin"] = "";
        // Mail is on, so a missing origin is a failure there even though the origin line itself only warns.
        var quiet = await Inspect(none);
        Assert.Equal(DoctorStatus.Warn, quiet["origin"]!.Status);
        Assert.Equal(DoctorStatus.Fail, quiet["email"]!.Status);
        Assert.Contains("Email__PublicOrigin is required", quiet["email"]!.Detail);
    }

    [Fact]
    public async Task Stripe_needs_three_keys_with_the_right_prefixes()
    {
        var half = Healthy();
        half["Billing:StripeWebhookSecret"] = "";
        var missing = await Inspect(half);
        Assert.Equal(DoctorStatus.Fail, missing["billing"]!.Status);
        Assert.Contains("Billing__StripeWebhookSecret", missing["billing"]!.Detail);

        var wrong = Healthy();
        wrong["Billing:StripeSecretKey"] = "pk_live_thisIsThePublishableOne";
        wrong["Billing:StripePriceId"] = "prod_NotAPrice";
        wrong["Billing:StripeWebhookSecret"] = "sk_live_wrongPlace";
        var prefixes = await Inspect(wrong);
        Assert.Equal(DoctorStatus.Fail, prefixes["billing"]!.Status);
        Assert.Contains("sk_live_ or sk_test_", prefixes["billing"]!.Detail);
        Assert.Contains("does not start with price_", prefixes["billing"]!.Detail);
        Assert.Contains("does not start with whsec_", prefixes["billing"]!.Detail);

        // A test key against an https origin runs, and says so: nothing is ever charged.
        var test = Healthy();
        test["Billing:StripeSecretKey"] = "sk_test_51NotAReal";
        var warned = await Inspect(test);
        Assert.Equal(DoctorStatus.Warn, warned["billing"]!.Status);
        Assert.Contains("no card is ever charged", warned["billing"]!.Detail);
        Assert.Equal(0, warned.ExitCode);

        var manual = Healthy();
        manual["Billing:Provider"] = "manual";
        Assert.Equal(DoctorStatus.Ok, (await Inspect(manual))["billing"]!.Status);

        var nonsense = Healthy();
        nonsense["Billing:Provider"] = "paypal";
        Assert.Equal(DoctorStatus.Fail, (await Inspect(nonsense))["billing"]!.Status);
    }

    [Fact]
    public async Task The_caps_the_board_the_moderators_and_push_are_read_as_the_app_reads_them()
    {
        var overshoot = Healthy();
        overshoot["Plans:ProChecksPerDay"] = "500";
        overshoot["Limits:ChecksPerDay"] = "30";
        var caps = await Inspect(overshoot);
        Assert.Equal(DoctorStatus.Warn, caps["plans"]!.Status);
        Assert.Contains("really gets 30", caps["plans"]!.Detail);

        var pointless = Healthy();
        pointless["Plans:FreeChecksPerDay"] = "30";
        pointless["Plans:ProChecksPerDay"] = "30";
        Assert.Contains("nothing to pay for", (await Inspect(pointless))["plans"]!.Detail);

        var zone = Healthy();
        zone["Board:TimeZone"] = "Middle/Earth";
        var board = await Inspect(zone);
        Assert.Equal(DoctorStatus.Fail, board["board"]!.Status);
        Assert.Equal(1, board.ExitCode);

        var nobody = Healthy();
        nobody["Admin:Handles:0"] = "";
        Assert.Equal(DoctorStatus.Warn, (await Inspect(nobody))["admin"]!.Status);

        var halfPair = Healthy();
        halfPair["Push:PrivateKey"] = "";
        var push = await Inspect(halfPair);
        Assert.Equal(DoctorStatus.Fail, push["push"]!.Status);
        Assert.Contains("only Push__PublicKey is set", push["push"]!.Detail);

        var noPush = Healthy();
        noPush["Push:PublicKey"] = "";
        noPush["Push:PrivateKey"] = "";
        var off = await Inspect(noPush);
        Assert.Equal(DoctorStatus.Warn, off["push"]!.Status);
        Assert.Contains("--vapid", off["push"]!.Detail);
    }

    [Fact]
    public async Task A_moderator_made_with_admin_counts_even_when_the_handle_list_is_empty()
    {
        // LAUNCH.md 1.8 promotes the first account with --admin, which leaves nothing in the configuration, and runs the
        // doctor two steps later: the database is the word on who can open the queue, so the doctor reads it (never creates it).
        var path = Path.Combine(_root, "mods.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options))
        {
            db.Database.Migrate();
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), Handle = "orhav", HandleLower = "orhav", PasswordHash = "x", CreatedAt = DateTime.UtcNow, IsAdmin = true });
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), Handle = "noa", HandleLower = "noa", PasswordHash = "x", CreatedAt = DateTime.UtcNow });
            db.SaveChanges();
        }

        SqliteConnection.ClearAllPools();
        var promoted = Healthy();
        promoted["Admin:Handles:0"] = "";
        promoted["ConnectionStrings:Default"] = $"Data Source={path}";
        var line = (await Inspect(promoted))["admin"]!;
        Assert.Equal(DoctorStatus.Ok, line.Status);
        Assert.Contains("1 moderator account in the database", line.Detail);

        // Listed handles stay first, with the database's count after them.
        var listed = Healthy();
        listed["ConnectionStrings:Default"] = $"Data Source={path}";
        Assert.Contains("orhav; 1 moderator account", (await Inspect(listed))["admin"]!.Detail);

        // No list and no file yet: the warning as before, and looking created no file.
        var absent = Path.Combine(_root, "absent.db");
        var nobody = Healthy();
        nobody["Admin:Handles:0"] = "";
        nobody["ConnectionStrings:Default"] = $"Data Source={absent}";
        var warned = (await Inspect(nobody))["admin"]!;
        Assert.Equal(DoctorStatus.Warn, warned.Status);
        Assert.Contains("nobody can open the moderation queue", warned.Detail);
        Assert.False(File.Exists(absent));
    }

    [Fact]
    public async Task An_unreadable_database_never_lets_the_doctor_claim_that_nobody_is_a_moderator()
    {
        // A folder where the file should be, and a file that is not a database: in both the doctor could not look, and
        // --admin may well have made a moderator in the real file, so it says it could not read and points at the database
        // line instead of asserting that no moderator exists. Neither is touched by the looking.
        var folder = Path.Combine(_root, "folder.db");
        Directory.CreateDirectory(folder);
        var garbage = Path.Combine(_root, "garbage.db");
        var noise = string.Concat(Enumerable.Repeat("this is not a database, and never was. ", 200));
        File.WriteAllText(garbage, noise);

        foreach (var path in new[] { folder, garbage })
        {
            var nobody = Healthy();
            nobody["Admin:Handles:0"] = "";
            nobody["ConnectionStrings:Default"] = $"Data Source={path}";
            var report = await Inspect(nobody);
            var admin = report["admin"]!;
            Assert.Equal(DoctorStatus.Warn, admin.Status);
            Assert.Contains("Admin__Handles is empty and the database could not be read", admin.Detail);
            Assert.Contains("see the database line", admin.Detail);
            Assert.DoesNotContain("no account is a moderator", admin.Detail);
            Assert.Equal(DoctorStatus.Fail, report["database"]!.Status);

            // With handles listed the line stays ok, and still does not pretend to have counted anything.
            var listed = Healthy();
            listed["ConnectionStrings:Default"] = $"Data Source={path}";
            var line = (await Inspect(listed))["admin"]!;
            Assert.Equal(DoctorStatus.Ok, line.Status);
            Assert.Contains("orhav; the database could not be read", line.Detail);
            Assert.DoesNotContain("moderator account", line.Detail);
        }

        Assert.True(Directory.Exists(folder));
        Assert.Equal(noise, File.ReadAllText(garbage));
    }

    [Fact]
    public async Task The_storage_root_the_database_and_ffmpeg_are_read_from_the_box()
    {
        // A root that is really a file: nothing can be written inside it, whoever is running.
        var blocked = Healthy();
        var file = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(file, "");
        blocked["Storage:Root"] = Path.Combine(file, "storage");
        var storage = await Inspect(blocked);
        Assert.Equal(DoctorStatus.Fail, storage["storage"]!.Status);
        Assert.Contains("cannot be written to", storage["storage"]!.Detail);

        // A database that exists and is at the current schema. The doctor opens it read-only: no migration is applied.
        var path = Path.Combine(_root, "made.db");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options))
        {
            db.Database.Migrate();
        }

        SqliteConnection.ClearAllPools();
        var made = Healthy();
        made["ConnectionStrings:Default"] = $"Data Source={path}";
        var current = await Inspect(made);
        Assert.Equal(DoctorStatus.Ok, current["database"]!.Status);
        Assert.Contains("at the current schema", current["database"]!.Detail);

        // A file the doctor never touched stays absent: looking at it must not create it.
        var absent = Path.Combine(_root, "never.db");
        var fresh = Healthy();
        fresh["ConnectionStrings:Default"] = $"Data Source={absent}";
        Assert.Equal(DoctorStatus.Warn, (await Inspect(fresh))["database"]!.Status);
        Assert.False(File.Exists(absent));

        // ffmpeg is only asked about when clips are meant to be re-encoded.
        var quiet = Healthy();
        Assert.Contains("Storage__Transcode is off", (await Inspect(quiet))["ffmpeg"]!.Detail);

        var wanted = Healthy();
        wanted["Storage:Transcode"] = "true";
        wanted["Storage:FfmpegPath"] = Path.Combine(_root, "no-such-ffmpeg");
        var ffmpeg = await Inspect(wanted);
        Assert.Equal(DoctorStatus.Fail, ffmpeg["ffmpeg"]!.Status);
        Assert.Contains("Storage__FfmpegPath", ffmpeg["ffmpeg"]!.Detail);
    }

    [Fact]
    public async Task The_live_calls_go_where_they_should_and_report_the_status_they_got()
    {
        var handler = new CannedHandler(HttpStatusCode.OK);
        var report = await Inspect(Healthy(), live: true, handler: handler);
        Assert.Equal(DoctorStatus.Ok, report["anthropic-live"]!.Status);
        Assert.Contains("HTTP 200", report["anthropic-live"]!.Detail);
        Assert.Equal(DoctorStatus.Ok, report["stripe-live"]!.Status);

        var anthropic = Assert.Single(handler.Requests, r => r.Uri.AbsolutePath == "/v1/messages");
        Assert.Equal(HttpMethod.Post, anthropic.Method);
        Assert.Equal("api.anthropic.com", anthropic.Uri.Host);
        // One cheap call: five tokens is a handshake, not a check.
        using var body = JsonDocument.Parse(anthropic.Body);
        Assert.Equal(5, body.RootElement.GetProperty("max_tokens").GetInt32());

        var stripe = Assert.Single(handler.Requests, r => r.Uri.AbsolutePath.StartsWith("/v1/prices/", StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Get, stripe.Method);
        Assert.Equal("/v1/prices/price_1NotAReal", stripe.Uri.AbsolutePath);

        // The webhook endpoint is the second Stripe read, and it is a GET of the list, not a write of anything.
        var webhook = Assert.Single(handler.Requests, r => r.Uri.AbsolutePath == "/v1/webhook_endpoints");
        Assert.Equal(HttpMethod.Get, webhook.Method);
        Assert.Equal("api.stripe.com", webhook.Uri.Host);
        Assert.Equal(DoctorStatus.Ok, report["stripe-webhook"]!.Status);

        // A key Anthropic refuses, and a price Stripe cannot find, both fail the run and name the thing to fix.
        var refused = await Inspect(Healthy(), live: true, handler: new CannedHandler(HttpStatusCode.Unauthorized));
        Assert.Equal(DoctorStatus.Fail, refused["anthropic-live"]!.Status);
        Assert.Equal(DoctorStatus.Fail, refused["stripe-live"]!.Status);
        Assert.Equal(DoctorStatus.Fail, refused["stripe-webhook"]!.Status);
        Assert.Equal(1, refused.ExitCode);

        var gone = await Inspect(Healthy(), live: true, handler: new CannedHandler(HttpStatusCode.NotFound));
        Assert.Contains("no price price_1NotAReal", gone["stripe-live"]!.Detail);
    }

    /// <summary>
    /// The price half of <c>--stripe-check</c>: Checkout runs in subscription mode, so a one-time or an archived price
    /// is a failure now rather than a 400 the first person to press Go Pro meets.
    /// </summary>
    [Fact]
    public async Task The_price_has_to_be_recurring_and_not_archived()
    {
        var recurring = await Inspect(Healthy(), live: true, stripeOnly: true, handler: new CannedHandler(HttpStatusCode.OK));
        Assert.Equal(DoctorStatus.Ok, recurring["stripe-live"]!.Status);
        Assert.Contains("every month", recurring["stripe-live"]!.Detail);

        var once = new CannedHandler(HttpStatusCode.OK)
        {
            PriceBody = """{"id":"price_1NotAReal","object":"price","active":true,"type":"one_time"}"""
        };
        var report = await Inspect(Healthy(), live: true, stripeOnly: true, handler: once);
        Assert.Equal(DoctorStatus.Fail, report["stripe-live"]!.Status);
        Assert.Contains("one-time", report["stripe-live"]!.Detail);
        Assert.Equal(1, report.ExitCode);

        var archived = new CannedHandler(HttpStatusCode.OK)
        {
            PriceBody = """{"id":"price_1NotAReal","object":"price","active":false,"type":"recurring","recurring":{"interval":"year","interval_count":1}}"""
        };
        var gone = await Inspect(Healthy(), live: true, stripeOnly: true, handler: archived);
        Assert.Equal(DoctorStatus.Fail, gone["stripe-live"]!.Status);
        Assert.Contains("archived", gone["stripe-live"]!.Detail);

        // An answer that says nothing either way is a warning, never a pass dressed up as one.
        var quiet = new CannedHandler(HttpStatusCode.OK) { PriceBody = "{}" };
        var unknown = await Inspect(Healthy(), live: true, stripeOnly: true, handler: quiet);
        Assert.Equal(DoctorStatus.Warn, unknown["stripe-live"]!.Status);
        Assert.Equal(0, unknown.ExitCode);
    }

    /// <summary>
    /// The webhook half: an endpoint has to be registered for this origin's own path, enabled, and subscribed to every
    /// event <c>BillingEndpoints.WebhookAsync</c> switches on. The two softer cases are deliberate — the same route
    /// under another name the app answers to, and the four-event endpoint an older runbook asked for, are warnings that
    /// name what is off rather than failures that stop a launch. Nothing in the line is a secret: the url is a public
    /// route on the operator's domain, and the signing secret is never read here.
    /// </summary>
    [Fact]
    public async Task The_webhook_endpoint_has_to_be_registered_for_this_origin_with_the_events_the_app_reads()
    {
        var report = await Inspect(Healthy(), live: true, stripeOnly: true, handler: new CannedHandler(HttpStatusCode.OK));
        Assert.Equal(DoctorStatus.Ok, report["stripe-webhook"]!.Status);
        Assert.Contains("https://orevosh.example/api/billing/webhook", report["stripe-webhook"]!.Detail);

        // Registered on a route that is not the app's at all: nothing this app serves would ever be posted to.
        var elsewhere = new CannedHandler(HttpStatusCode.OK)
        {
            WebhookBody = CannedHandler.Endpoints("https://example.test/hooks/stripe", [.. Doctor.WebhookEvents])
        };
        var wrong = await Inspect(Healthy(), live: true, stripeOnly: true, handler: elsewhere);
        Assert.Equal(DoctorStatus.Fail, wrong["stripe-webhook"]!.Status);
        Assert.Contains("example.test/hooks/stripe", wrong["stripe-webhook"]!.Detail);
        Assert.Equal(1, wrong.ExitCode);

        // The app's own route on another name it answers to (a fly.dev address beside the custom domain). The webhook
        // only has to reach the route, so this is a warning that names it, not a failure that stops a launch.
        var otherName = new CannedHandler(HttpStatusCode.OK)
        {
            WebhookBody = CannedHandler.Endpoints("https://orevosh-pilot.fly.dev/api/billing/webhook", [.. Doctor.WebhookEvents])
        };
        var second = await Inspect(Healthy(), live: true, stripeOnly: true, handler: otherName);
        Assert.Equal(DoctorStatus.Warn, second["stripe-webhook"]!.Status);
        Assert.Contains("orevosh-pilot.fly.dev", second["stripe-webhook"]!.Detail);
        Assert.Equal(0, second.ExitCode);

        // Registered here, but only for the event that grants Pro: nothing would ever end it.
        var partial = new CannedHandler(HttpStatusCode.OK)
        {
            WebhookBody = CannedHandler.Endpoints(CannedHandler.StripeOrigin + "/api/billing/webhook", "checkout.session.completed")
        };
        var missing = await Inspect(Healthy(), live: true, stripeOnly: true, handler: partial);
        Assert.Equal(DoctorStatus.Fail, missing["stripe-webhook"]!.Status);
        Assert.Contains("customer.subscription.deleted", missing["stripe-webhook"]!.Detail);
        Assert.DoesNotContain("checkout.session.completed", missing["stripe-webhook"]!.Detail);

        // The endpoint an older runbook told people to build: the four events that move the plan, and not the fifth.
        // Nothing a paying person does is lost, so it is a warning and the run still exits 0.
        var four = new CannedHandler(HttpStatusCode.OK)
        {
            WebhookBody = CannedHandler.Endpoints(
                CannedHandler.StripeOrigin + "/api/billing/webhook",
                "checkout.session.completed", "invoice.paid", "customer.subscription.updated", "customer.subscription.deleted")
        };
        var older = await Inspect(Healthy(), live: true, stripeOnly: true, handler: four);
        Assert.Equal(DoctorStatus.Warn, older["stripe-webhook"]!.Status);
        Assert.Contains("customer.subscription.created", older["stripe-webhook"]!.Detail);
        Assert.Equal(0, older.ExitCode);

        // A wildcard endpoint covers everything the app reads.
        var everything = new CannedHandler(HttpStatusCode.OK)
        {
            WebhookBody = CannedHandler.Endpoints(CannedHandler.StripeOrigin + "/api/billing/webhook", "*")
        };
        var all = await Inspect(Healthy(), live: true, stripeOnly: true, handler: everything);
        Assert.Equal(DoctorStatus.Ok, all["stripe-webhook"]!.Status);

        // Registered and subscribed, but switched off in Stripe: no event ever arrives.
        var off = new CannedHandler(HttpStatusCode.OK)
        {
            WebhookBody = CannedHandler
                .Endpoints(CannedHandler.StripeOrigin + "/api/billing/webhook", [.. Doctor.WebhookEvents])
                .Replace("\"status\":\"enabled\"", "\"status\":\"disabled\"", StringComparison.Ordinal)
        };
        var disabled = await Inspect(Healthy(), live: true, stripeOnly: true, handler: off);
        Assert.Equal(DoctorStatus.Fail, disabled["stripe-webhook"]!.Status);
        Assert.Contains("disabled", disabled["stripe-webhook"]!.Detail);

        // With no origin configured there is no url to look for: a skip, never a guess.
        var homeless = Healthy();
        homeless["Email:PublicOrigin"] = "";
        homeless["Billing:PublicOrigin"] = "";
        var skipped = await Inspect(homeless, live: true, stripeOnly: true, handler: new CannedHandler(HttpStatusCode.OK));
        Assert.Equal(DoctorStatus.Skip, skipped["stripe-webhook"]!.Status);
        Assert.Equal(0, skipped.ExitCode);

        // The events the check compares against are the ones the webhook handler switches on, in its order.
        Assert.Equal(
            ["checkout.session.completed", "customer.subscription.created", "invoice.paid", "customer.subscription.updated", "customer.subscription.deleted"],
            Doctor.WebhookEvents.ToArray());
    }

    [Fact]
    public async Task The_stub_key_is_never_spent_and_a_manual_provider_is_never_called()
    {
        var stub = Healthy();
        stub["ANTHROPIC_API_KEY"] = Doctor.StubApiKey;
        stub["Billing:Provider"] = "manual";
        var handler = new CannedHandler(HttpStatusCode.OK);
        var report = await Inspect(stub, live: true, handler: handler);
        Assert.Equal(DoctorStatus.Skip, report["anthropic-live"]!.Status);
        Assert.Equal(DoctorStatus.Skip, report["stripe-live"]!.Status);
        Assert.Equal(DoctorStatus.Skip, report["stripe-webhook"]!.Status);
        Assert.Empty(handler.Requests);
        // Skipped calls are neither a pass nor a failure; the stub key itself is what fails the run.
        // Round 13 — money: the fourth skip is alerts-live, which has no channel to send its test alert down.
        Assert.Equal(DoctorStatus.Skip, report["alerts-live"]!.Status);
        Assert.Equal(4, report.Skipped);
        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public async Task Stripe_check_is_the_stripe_part_on_its_own()
    {
        var handler = new CannedHandler(HttpStatusCode.OK);
        var report = await Inspect(Healthy(), live: true, stripeOnly: true, handler: handler);
        Assert.Equal(["billing", "stripe-live", "stripe-webhook"], report.Lines.Select(l => l.Name).ToArray());
        Assert.Equal(0, report.ExitCode);
        // Only Stripe was called: no Anthropic request, no key spent. Two reads, both GETs, nothing written.
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.All(handler.Requests, r => Assert.Equal("api.stripe.com", r.Uri.Host));
        Assert.StartsWith("/v1/prices/", handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("/v1/webhook_endpoints", handler.Requests[1].Uri.AbsolutePath);

        var (exit, text) = await Run(Healthy(), live: true, stripeOnly: true, handler: new CannedHandler(HttpStatusCode.Unauthorized));
        Assert.Equal(1, exit);
        Assert.Contains("the Stripe part", text);
        Assert.Contains("HTTP 401", text);
        // Not one character of the key, the price secret or the webhook secret reaches the printed page.
        Assert.DoesNotContain(LiveSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookSecret, text, StringComparison.Ordinal);
    }

    /// <summary>The printed form: one line per check, a four-character verdict a script can grep, and a summary that names the exit code's reason.</summary>
    [Fact]
    public async Task The_printed_checklist_has_one_greppable_line_per_check()
    {
        var broken = Healthy();
        broken["ANTHROPIC_API_KEY"] = "";
        var (exit, text) = await Run(broken);
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Equal("OREVOSH doctor", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("  FAIL anthropic ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("  OK   billing ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("  WARN database ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("doctor: ", StringComparison.Ordinal) && l.Contains("1 failure"));
        Assert.Contains("Not ready: fix the failures above and run it again.", lines);
        Assert.Equal(1, exit);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: temp folders are not worth a failing test.
        }
    }
}

/// <summary>What <c>GET /readyz</c> answers, and the one line it logs when the verdict flips.</summary>
public class ReadinessTests
{
    private static async Task<(HttpStatusCode Status, JsonElement Body, string? CacheControl)> ReadyAsync(HttpClient client)
    {
        var response = await client.GetAsync("/readyz");
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>(), response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task A_healthy_instance_answers_200_with_every_check_ok_and_is_never_cached()
    {
        using var app = new TestApp();
        var (status, body, cache) = await ReadyAsync(app.NewClient());
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("ok").GetBoolean());
        var checks = body.GetProperty("checks");
        Assert.Equal(["db", "storage"], checks.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("ok", checks.GetProperty("db").GetString());
        Assert.Equal("ok", checks.GetProperty("storage").GetString());
        Assert.Equal("no-store", cache);

        // The liveness line is a different thing and stays plain text.
        var health = await app.NewClient().GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("ok", await health.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_is_public_and_carries_no_path_no_version_and_no_setting()
    {
        using var app = new TestApp();
        // No session, no CSRF header: a load balancer has neither.
        var response = await app.BareClient().GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(app.StorageRoot, text);
        Assert.DoesNotContain(app.DatabasePath, text);
        Assert.Equal("""{"ok":true,"checks":{"db":"ok","storage":"ok"}}""", text);
    }

    [Fact]
    public async Task A_storage_root_that_went_away_is_503_with_that_check_named_and_one_warning_in_the_log()
    {
        var log = new OpsLogRecorder();
        using var app = new TestApp();
        using var factory = app.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(log)));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await ReadyAsync(client)).Status);
        Assert.Empty(log.Lines(LogLevel.Warning).Where(l => l.Contains("Readiness")));

        // The volume goes away under the running app, which is what a bad mount looks like from inside.
        Directory.Delete(app.StorageRoot, recursive: true);
        var (status, body, cache) = await ReadyAsync(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("ok", body.GetProperty("checks").GetProperty("db").GetString());
        Assert.Equal("not writable", body.GetProperty("checks").GetProperty("storage").GetString());
        Assert.Equal("no-store", cache);

        // One line for the flip, not one per poll.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ReadyAsync(client)).Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ReadyAsync(client)).Status);
        var warnings = log.Lines(LogLevel.Warning).Where(l => l.Contains("Readiness")).ToList();
        Assert.Single(warnings);
        Assert.Contains("storage=not writable", warnings[0]);

        // And one line when it comes back.
        Directory.CreateDirectory(app.StorageRoot);
        Assert.Equal(HttpStatusCode.OK, (await ReadyAsync(client)).Status);
        Assert.Contains(log.Lines(LogLevel.Information), l => l.Contains("Readiness is ok again"));
    }

    [Fact]
    public async Task Ffmpeg_is_a_check_only_while_clips_are_meant_to_be_re_encoded()
    {
        using var on = new TestApp { Transcode = true };
        var ready = await ReadyAsync(on.NewClient());
        Assert.Equal(["db", "storage", "ffmpeg"], ready.Body.GetProperty("checks").EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("ok", ready.Body.GetProperty("checks").GetProperty("ffmpeg").GetString());
        Assert.Equal(HttpStatusCode.OK, ready.Status);

        using var missing = new TestApp { Transcode = true, Settings = { ["Storage:FfmpegPath"] = "/no/such/ffmpeg" } };
        var without = await ReadyAsync(missing.NewClient());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, without.Status);
        Assert.Equal("not found", without.Body.GetProperty("checks").GetProperty("ffmpeg").GetString());
    }

    [Fact]
    public async Task Head_answers_both_health_routes_like_get_so_a_checker_may_probe_with_either()
    {
        // The first dry run of LAUNCH.md 1.8 found `curl -I` on the health routes answering 405: an uptime checker that
        // probes with HEAD would have paged the owner about a site that was up.
        using var app = new TestApp();
        var client = app.BareClient();
        foreach (var path in new[] { "/healthz", "/readyz" })
        {
            var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }

        // A method neither route ever meant is still refused.
        var post = await client.PostAsync("/healthz", null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }
}

/// <summary>The one line per /api request, and the silence when it is off.</summary>
public class RequestLogTests
{
    [Fact]
    public async Task Nothing_is_logged_per_request_by_default()
    {
        var log = new OpsLogRecorder();
        using var app = new TestApp();
        using var factory = app.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(log)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(Sessions.RequestHeader, Sessions.RequestHeaderValue);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/config")).StatusCode);
        Assert.Empty(log.Lines(RequestLog.Category));
    }

    [Fact]
    public async Task One_line_per_api_request_when_it_is_on_with_the_account_and_never_a_query_or_a_body()
    {
        var log = new OpsLogRecorder();
        using var app = new TestApp { Settings = { [RequestLog.SettingKey] = "true" } };
        using var factory = app.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(log)));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(Sessions.RequestHeader, Sessions.RequestHeaderValue);

        // Signed out: the account is a bare dash.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/config")).StatusCode);
        var anonymous = Assert.Single(log.Lines(RequestLog.Category), l => l.Contains("/api/config"));
        Assert.Matches(@"^api GET /api/config 200 \d+ms -$", anonymous);

        // Signed in: the account id, and nothing of the password that was posted to get there.
        var me = await app.SignupAsync(client, "ops_logged");
        var id = me.GetProperty("id").GetGuid().ToString("N");
        var signup = Assert.Single(log.Lines(RequestLog.Category), l => l.Contains("/api/auth/signup"));
        Assert.Matches(@"^api POST /api/auth/signup 201 \d+ms -$", signup);
        Assert.DoesNotContain("password", signup, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        var mine = Assert.Single(log.Lines(RequestLog.Category), l => l.Contains("/api/auth/me"));
        Assert.Matches($@"^api GET /api/auth/me 200 \d+ms {id}$", mine);

        // A query string is cut off, and a refusal is logged with the status that went out.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/users/nobody_at_all?from=somewhere&token=abc")).StatusCode);
        var refused = Assert.Single(log.Lines(RequestLog.Category), l => l.Contains("/api/users/nobody_at_all"));
        Assert.Matches($@"^api GET /api/users/nobody_at_all 404 \d+ms {id}$", refused);
        Assert.DoesNotContain("token", refused);
        Assert.DoesNotContain("?", refused);

        // Only /api: the client's own files and the two probes are not worth a line each.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/readyz")).StatusCode);
        Assert.DoesNotContain(log.Lines(RequestLog.Category), l => l.Contains("healthz") || l.Contains("readyz"));
    }
}

/// <summary>The retention <c>--backup &lt;dir&gt; --keep &lt;n&gt;</c> applies once the new copy is complete.</summary>
public class BackupRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fitcheck-tests", "keep-" + Guid.NewGuid().ToString("N"));

    public BackupRetentionTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Keep_removes_the_older_copies_and_counts_databases_and_storage_apart()
    {
        var backups = Path.Combine(_root, "backups");
        Directory.CreateDirectory(backups);
        foreach (var stamp in new[] { "20260901000000", "20260902000000", "20260903000000", "20260904000000" })
        {
            File.WriteAllText(Path.Combine(backups, $"orevosh-{stamp}.db"), stamp);
            Directory.CreateDirectory(Path.Combine(backups, $"storage-{stamp}"));
            File.WriteAllText(Path.Combine(backups, $"storage-{stamp}", "a.jpg"), stamp);
        }

        // Something the operator left there by hand, and a copy of an older round: neither is ours to delete.
        File.WriteAllText(Path.Combine(backups, "notes.txt"), "keep me");
        File.WriteAllText(Path.Combine(backups, "orevosh.db.bak-20250101000000"), "keep me");

        var removed = DatabaseSetup.Prune(backups, 2);
        Assert.Equal(
            [
                Path.Combine(backups, "orevosh-20260902000000.db"),
                Path.Combine(backups, "orevosh-20260901000000.db"),
                Path.Combine(backups, "storage-20260902000000"),
                Path.Combine(backups, "storage-20260901000000")
            ],
            removed);
        Assert.Equal(["orevosh-20260903000000.db", "orevosh-20260904000000.db"],
            Directory.GetFiles(backups, "orevosh-*.db").Select(Path.GetFileName).Order().ToArray());
        Assert.Equal(["storage-20260903000000", "storage-20260904000000"],
            Directory.GetDirectories(backups, "storage-*").Select(Path.GetFileName).Order().ToArray());
        Assert.True(File.Exists(Path.Combine(backups, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(backups, "orevosh.db.bak-20250101000000")));

        // Running it again changes nothing, and a keep below 1 (no --keep at all) removes nothing.
        Assert.Empty(DatabaseSetup.Prune(backups, 2));
        Assert.Empty(DatabaseSetup.Prune(backups, 0));
        Assert.Empty(DatabaseSetup.Prune(Path.Combine(_root, "no-such-folder"), 1));
        Assert.Equal(2, Directory.GetFiles(backups, "orevosh-*.db").Length);
    }

    /// <summary>The names <c>--backup</c> really writes are the names retention sorts, which is the whole contract between them.</summary>
    [Fact]
    public void A_real_backup_survives_a_prune_that_removes_the_older_copies()
    {
        var path = Path.Combine(_root, "orevosh.db");
        var storage = Path.Combine(_root, "storage");
        Directory.CreateDirectory(storage);
        File.WriteAllText(Path.Combine(storage, "look.jpg"), "photo");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options))
        {
            db.Database.Migrate();
        }

        SqliteConnection.ClearAllPools();
        var backups = Path.Combine(_root, "backups");
        var (database, copy) = DatabaseSetup.Backup($"Data Source={path}", storage, backups);
        Assert.NotNull(copy);

        // Two copies from earlier runs, named the way a real one is named a day before.
        File.WriteAllText(Path.Combine(backups, "orevosh-20250101000000.db"), "old");
        Directory.CreateDirectory(Path.Combine(backups, "storage-20250101000000"));

        var removed = DatabaseSetup.Prune(backups, 1);
        Assert.Equal(
            [Path.Combine(backups, "orevosh-20250101000000.db"), Path.Combine(backups, "storage-20250101000000")],
            removed);
        Assert.True(File.Exists(database));
        Assert.True(Directory.Exists(copy));
        Assert.True(File.Exists(Path.Combine(copy!, "look.jpg")));
    }

    /// <summary>
    /// Two backups inside one second used to crash on SQLite's "output file already exists": the name is only precise to
    /// the second and VACUUM INTO refuses a target that is there. Both must now write a copy, and the second name must
    /// keep the fixed width that retention sorts by.
    /// </summary>
    [Fact]
    public void Two_backups_in_the_same_second_both_write_a_copy()
    {
        var path = Path.Combine(_root, "orevosh.db");
        var storage = Path.Combine(_root, "storage");
        Directory.CreateDirectory(storage);
        File.WriteAllText(Path.Combine(storage, "look.jpg"), "photo");
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options))
        {
            db.Database.Migrate();
        }

        SqliteConnection.ClearAllPools();
        var backups = Path.Combine(_root, "backups");

        // Force the collision rather than hope two calls land in the same second: seed this second's two names, so the
        // first backup MUST walk forward and the second MUST walk past it again.
        Directory.CreateDirectory(backups);
        var taken = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        File.WriteAllText(Path.Combine(backups, $"orevosh-{taken}.db"), "in the way");
        Directory.CreateDirectory(Path.Combine(backups, $"storage-{taken}"));

        var first = DatabaseSetup.Backup($"Data Source={path}", storage, backups);
        var second = DatabaseSetup.Backup($"Data Source={path}", storage, backups);

        Assert.DoesNotContain(taken, Path.GetFileName(first.Database));
        Assert.NotEqual(first.Database, second.Database);
        Assert.NotEqual(first.Storage, second.Storage);
        Assert.True(File.Exists(first.Database));
        Assert.True(File.Exists(second.Database));
        Assert.True(Directory.Exists(first.Storage));
        Assert.True(Directory.Exists(second.Storage));
        Assert.Equal(3, Directory.GetFiles(backups, "orevosh-*.db").Length);   // the seeded one plus the two real copies

        // Still orevosh-<14 digits>.db, so sorting by name is still sorting by time and Prune sees both.
        Assert.Matches(@"^orevosh-\d{14}\.db$", Path.GetFileName(first.Database));
        Assert.Matches(@"^orevosh-\d{14}\.db$", Path.GetFileName(second.Database));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: temp folders are not worth a failing test.
        }
    }
}

/// <summary>One recorded request as the network would have seen it, body included (the doctor's live calls are tiny).</summary>
public sealed record CannedRequest(HttpMethod Method, Uri Uri, string Body);

/// <summary>
/// Stands in for api.anthropic.com and api.stripe.com at once: records every request and answers with one status.
/// The two Stripe reads the doctor makes are answered with the shape Stripe really returns — a recurring price, and a
/// list with one webhook endpoint for <see cref="StripeOrigin"/> subscribed to every event the app reads — so a test
/// that wants a broken account overrides one of them rather than getting an empty object.
/// </summary>
public sealed class CannedHandler(HttpStatusCode status) : HttpMessageHandler
{
    /// <summary>The origin <c>DoctorTests.Healthy()</c> configures, and so the url a registered endpoint has to carry.</summary>
    public const string StripeOrigin = "https://orevosh.example";

    private readonly List<CannedRequest> _requests = [];

    public IReadOnlyList<CannedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    /// <summary>The body for <c>GET /v1/prices/…</c>; null keeps the recurring monthly price below.</summary>
    public string? PriceBody { get; set; }

    /// <summary>The body for <c>GET /v1/webhook_endpoints</c>; null keeps the one matching, fully subscribed endpoint below.</summary>
    public string? WebhookBody { get; set; }

    /// <summary>A webhook endpoint list Stripe would answer with, for the url and events given.</summary>
    public static string Endpoints(string url, params string[] events) =>
        $$"""{"object":"list","data":[{"id":"we_1","object":"webhook_endpoint","status":"enabled","url":"{{url}}","enabled_events":[{{string.Join(",", events.Select(e => $"\"{e}\""))}}]}]}""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_requests)
        {
            _requests.Add(new(request.Method, request.RequestUri!, body));
        }

        var path = request.RequestUri!.AbsolutePath;
        var answer = "{}";
        if (path.StartsWith("/v1/prices/", StringComparison.Ordinal))
        {
            answer = PriceBody
                ?? """{"id":"price_1NotAReal","object":"price","active":true,"type":"recurring","recurring":{"interval":"month","interval_count":1}}""";
        }
        else if (path == "/v1/webhook_endpoints")
        {
            answer = WebhookBody ?? Endpoints(StripeOrigin + "/api/billing/webhook", [.. Doctor.WebhookEvents]);
        }

        return new HttpResponseMessage(status) { Content = new StringContent(answer, System.Text.Encoding.UTF8, "application/json") };
    }
}

/// <summary>Keeps every log line the host writes, so a test can see the readiness flip and the request line as an operator would.</summary>
public sealed class OpsLogRecorder : ILoggerProvider
{
    private readonly List<(string Category, LogLevel Level, string Message)> _lines = [];

    public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

    /// <summary>Every message written under that category, in order.</summary>
    public List<string> Lines(string category)
    {
        lock (_lines)
        {
            return _lines.Where(l => l.Category == category).Select(l => l.Message).ToList();
        }
    }

    /// <summary>Every message at that level, whatever wrote it.</summary>
    public List<string> Lines(LogLevel level)
    {
        lock (_lines)
        {
            return _lines.Where(l => l.Level == level).Select(l => l.Message).ToList();
        }
    }

    public void Dispose()
    {
    }

    private sealed class Recorder(OpsLogRecorder owner, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines)
            {
                owner._lines.Add((category, logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
