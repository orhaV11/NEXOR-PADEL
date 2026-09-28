using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FitCheck.Api.Tests;

/// <summary>Billing:Provider manual (the default): Pro comes from --pro, Checkout answers 400. Limits:ChecksPerDay at the real ceiling.</summary>
public sealed class ManualBillingApp : TestApp
{
    public ManualBillingApp()
    {
        ChecksPerDay = 30;
        FreeChecksPerDay = 3;
    }
}

/// <summary>Billing:Provider stripe with a test key, a price and a webhook secret, against the recorded Stripe.</summary>
public sealed class StripeBillingApp : TestApp
{
    public const string SecretKey = "sk_test_recorded_secret";
    public const string PriceId = "price_pro_monthly";
    public const string WebhookSecret = "whsec_recorded_webhook_secret";

    public StripeBillingApp()
    {
        ChecksPerDay = 30;
        FreeChecksPerDay = 3;
        BillingProvider = "stripe";
        StripeSecretKey = SecretKey;
        StripePriceId = PriceId;
        StripeWebhookSecret = WebhookSecret;
    }
}

/// <summary>
/// Round 20: the Stripe app with a second, yearly price beside the monthly one, priced in shekels with a euro monthly
/// price that has no yearly twin - the case where a year must be refused for a currency the page could not offer it in.
/// </summary>
public sealed class YearlyStripeBillingApp : TestApp
{
    public const string YearlyPriceId = "price_pro_yearly";

    public YearlyStripeBillingApp()
    {
        ChecksPerDay = 30;
        FreeChecksPerDay = 3;
        BillingProvider = "stripe";
        StripeSecretKey = StripeBillingApp.SecretKey;
        StripePriceId = StripeBillingApp.PriceId;
        StripeWebhookSecret = StripeBillingApp.WebhookSecret;
        StripeYearlyPriceId = YearlyPriceId;
        Settings["Plans:ProPriceAmount"] = "29";
        Settings["Plans:ProPriceCurrency"] = "ILS";
        Settings["Plans:ProPrices:EUR"] = "9.90";
        Settings["Plans:ProYearlyPriceAmount"] = "290";
    }
}

/// <summary>
/// Review of Round 20: the Stripe app whose database refuses, once when asked, the save that would write a webhook
/// event's row - a delivery that died at the worst moment, between the work and the record of it.
/// </summary>
public sealed class FlakyStripeBillingApp : TestApp
{
    public FailingEventSave Interceptor { get; } = new();

    public FlakyStripeBillingApp()
    {
        BillingProvider = "stripe";
        StripeSecretKey = StripeBillingApp.SecretKey;
        StripePriceId = StripeBillingApp.PriceId;
        StripeWebhookSecret = StripeBillingApp.WebhookSecret;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(ConnectionString).AddInterceptors(Interceptor));
        });
    }
}

/// <summary>Throws from the next save that carries a new <see cref="StripeEvent"/> row, once, as a dropped connection would.</summary>
public sealed class FailingEventSave : SaveChangesInterceptor
{
    public bool FailNext { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailNext && eventData.Context is { } context && context.ChangeTracker.Entries<StripeEvent>().Any(e => e.State == EntityState.Added))
        {
            FailNext = false;
            throw new IOException("the connection went away");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>
/// Plans and billing: the state route for free and Pro, Checkout refused without Stripe (and for an account that is Pro
/// already) and the exact request that goes out with it, the Billing Portal (Round 11: 404 on the manual provider or
/// without a customer, the exact request, 502 when Stripe refuses), the webhook (signature, the seven events, unknown
/// ones, and the subscription id that tells one subscription on a customer from another) and its CSRF exemption, the
/// --pro command through AdminSync, the plan fields on "me", and the plans block on /api/config.
/// </summary>
public class BillingTests : IClassFixture<ManualBillingApp>, IClassFixture<StripeBillingApp>, IClassFixture<YearlyStripeBillingApp>
{
    private readonly ManualBillingApp _manual;
    private readonly StripeBillingApp _stripe;
    private readonly YearlyStripeBillingApp _yearly;

    public BillingTests(ManualBillingApp manual, StripeBillingApp stripe, YearlyStripeBillingApp yearly)
    {
        _manual = manual;
        _stripe = stripe;
        _yearly = yearly;
        _manual.Vision.Handler = _ => Payloads.Ok();
        _stripe.Vision.Handler = _ => Payloads.Ok();
        _yearly.Vision.Handler = _ => Payloads.Ok();
    }

    /// <summary>
    /// Round 20: every factory below stamps a fresh event id unless the test gives one, because the webhook now ignores
    /// an id it has seen. A test that means "the same event again" passes the same id twice; one that posts several
    /// events of one kind means several events, which is what distinct ids say.
    /// </summary>
    private static string NewEventId() => "evt_" + Guid.NewGuid().ToString("N")[..12];

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> ErrorOf(HttpResponseMessage response) => (await Json(response)).GetProperty("error").GetString()!;

    private static void WithDb(TestApp app, Action<AppDbContext> action)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        action(db);
        db.SaveChanges();
    }

    private static AppUser UserOf(TestApp app, Guid id)
    {
        using var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Single(u => u.Id == id);
    }

    private static void AssertAround(DateTime? actual, DateTime expected)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual!.Value.Ticks, expected.AddMinutes(-2).Ticks, expected.AddMinutes(2).Ticks);
    }

    /// <summary>Posts one event the way Stripe does: no CSRF header, a Stripe-Signature over the raw body with the webhook secret.</summary>
    private static async Task<HttpResponseMessage> PostEventAsync(TestApp app, object payload, string secret = StripeBillingApp.WebhookSecret, long? timestamp = null, string? signature = null)
    {
        var body = JsonSerializer.Serialize(payload);
        var header = signature ?? StripeClient.SignatureHeaderValue(secret, timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(), body);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation(StripeClient.SignatureHeader, header);
        return await app.BareClient().SendAsync(request);
    }

    /// <summary>A completed Checkout Session; <paramref name="subscription"/> is the id Stripe puts on it (a string, or the expanded object).</summary>
    private static object CheckoutCompleted(string? reference, string? metadataUserId, string? customer, object? subscription = null, string? id = null) => new
    {
        id = id ?? NewEventId(),
        type = "checkout.session.completed",
        data = new { @object = new { id = "cs_1", @object = "checkout.session", client_reference_id = reference, customer, subscription, metadata = new { userId = metadataUserId } } }
    };

    /// <summary>Round 20: a completed session as Checkout writes it, with what it sold in the metadata and how it was paid.</summary>
    private static object CheckoutCompletedWith(string reference, string customer, string subscription, string? interval, string? trialDays, string paymentStatus, string? id = null) => new
    {
        id = id ?? NewEventId(),
        type = "checkout.session.completed",
        data = new
        {
            @object = new
            {
                id = "cs_1", @object = "checkout.session", client_reference_id = reference, customer, subscription, payment_status = paymentStatus,
                metadata = new { userId = reference, interval, trialDays }
            }
        }
    };

    private static object SubscriptionCreated(string customer, string id) => new
    {
        id = NewEventId(),
        type = "customer.subscription.created",
        data = new { @object = new { id, @object = "subscription", customer, status = "active" } }
    };

    /// <summary>A renewal invoice as Stripe posts it: the customer, the reason and, with <paramref name="periodEnd"/>, one line naming the paid period.</summary>
    private static object InvoicePaid(string customer, string billingReason = "subscription_cycle", DateTimeOffset? periodEnd = null, string? eventId = null) => new
    {
        id = eventId ?? NewEventId(),
        type = "invoice.paid",
        data = new
        {
            @object = new
            {
                id = "in_1", @object = "invoice", customer, billing_reason = billingReason,
                lines = periodEnd is { } end
                    ? new { @object = "list", data = new object[] { new { id = "il_1", @object = "line_item", period = new { start = end.AddDays(-30).ToUnixTimeSeconds(), end = end.ToUnixTimeSeconds() } } } }
                    : null
            }
        }
    };

    /// <summary>
    /// A subscription as customer.subscription.updated carries it: the status and, when given, the current period end on
    /// the subscription itself (API versions before 2025-03-31) or on its item (since).
    /// </summary>
    private static object SubscriptionUpdated(string customer, string status, DateTimeOffset? periodEnd = null, bool onItems = false, string id = "sub_1", string? eventId = null) => new
    {
        id = eventId ?? NewEventId(),
        type = "customer.subscription.updated",
        data = new
        {
            @object = new
            {
                id, @object = "subscription", customer, status,
                current_period_end = periodEnd is { } end && !onItems ? end.ToUnixTimeSeconds() : (long?)null,
                items = new
                {
                    @object = "list",
                    data = new object[] { new { id = "si_1", @object = "subscription_item", current_period_end = periodEnd is { } itemEnd && onItems ? itemEnd.ToUnixTimeSeconds() : (long?)null } }
                }
            }
        }
    };

    private static object SubscriptionDeleted(string customer, string id = "sub_1", string? eventId = null) => new
    {
        id = eventId ?? NewEventId(),
        type = "customer.subscription.deleted",
        data = new { @object = new { id, @object = "subscription", customer, status = "canceled" } }
    };

    // ---- state ----

    [Fact]
    public async Task State_is_free_then_pro_with_the_end_date()
    {
        var (client, id, handle) = await _manual.NewUserAsync("bill_state");

        var free = await Json(await client.GetAsync("/api/billing/state"));
        Assert.Equal("free", free.GetProperty("plan").GetString());
        Assert.False(free.TryGetProperty("proUntil", out _));
        Assert.False(free.GetProperty("billing").GetBoolean());
        Assert.Equal("", free.GetProperty("proPriceText").GetString());

        var until = DateTime.UtcNow.AddDays(30);
        Assert.Equal(AdminChange.Changed, await AdminSync.SetProAsync(_manual.ConnectionString, handle, until));

        var pro = await Json(await client.GetAsync("/api/billing/state"));
        Assert.Equal("pro", pro.GetProperty("plan").GetString());
        AssertAround(pro.GetProperty("proUntil").GetDateTime(), until);
        Assert.False(pro.GetProperty("billing").GetBoolean());

        // A lapsed Pro reads as free again, with no stale end date.
        WithDb(_manual, db => db.Users.Single(u => u.Id == id).ProUntil = DateTime.UtcNow.AddMinutes(-1));
        var lapsed = await Json(await client.GetAsync("/api/billing/state"));
        Assert.Equal("free", lapsed.GetProperty("plan").GetString());
        Assert.False(lapsed.TryGetProperty("proUntil", out _));

        Assert.Equal(HttpStatusCode.Unauthorized, (await _manual.NewClient().GetAsync("/api/billing/state")).StatusCode);
    }

    // ---- checkout ----

    [Fact]
    public async Task Checkout_is_refused_while_billing_is_manual()
    {
        var (client, _, _) = await _manual.NewUserAsync("bill_manual");
        var response = await client.PostAsync("/api/billing/checkout", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Payments aren't set up on this server yet.", await ErrorOf(response));
        Assert.Empty(_manual.StripeHandler.Requests);
    }

    [Fact]
    public async Task Checkout_opens_a_subscription_session_for_the_signed_in_account()
    {
        var (client, id, _) = await _stripe.NewUserAsync("bill_checkout");
        _stripe.StripeHandler.Clear();

        var response = await client.PostAsync("/api/billing/checkout", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(RecordingStripeHandler.DefaultCheckoutUrl, (await Json(response)).GetProperty("url").GetString());

        var request = Assert.Single(_stripe.StripeHandler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.stripe.com/v1/checkout/sessions", request.Uri.ToString());
        Assert.Equal("Bearer " + StripeBillingApp.SecretKey, request.Authorization);
        Assert.Equal("subscription", request["mode"]);
        Assert.Equal(StripeBillingApp.PriceId, request["line_items[0][price]"]);
        Assert.Equal("1", request["line_items[0][quantity]"]);
        Assert.Equal(id.ToString("N"), request["client_reference_id"]);
        Assert.Equal(id.ToString("N"), request["metadata[userId]"]);
        Assert.Equal("http://localhost/#/pro?checkout=success", request["success_url"]);
        Assert.Equal("http://localhost/#/pro?checkout=cancel", request["cancel_url"]);
        // No address on the account and no customer yet: neither field goes out.
        Assert.Null(request["customer"]);
        Assert.Null(request["customer_email"]);
        // Round 20: a month by default, and no trial fields while Plans:ProTrialDays is 0.
        Assert.Equal("month", request["metadata[interval]"]);
        Assert.Null(request["subscription_data[trial_period_days]"]);
        Assert.Null(request["payment_method_collection"]);
        Assert.Null(request["metadata[trialDays]"]);

        // A confirmed address is prefilled; an unconfirmed one is not.
        WithDb(_stripe, db => { var u = db.Users.Single(x => x.Id == id); u.Email = "checkout@example.test"; u.EmailVerifiedAt = null; });
        _stripe.StripeHandler.Clear();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout", null)).StatusCode);
        Assert.Null(Assert.Single(_stripe.StripeHandler.Requests)["customer_email"]);

        WithDb(_stripe, db => db.Users.Single(x => x.Id == id).EmailVerifiedAt = DateTime.UtcNow);
        _stripe.StripeHandler.Clear();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout", null)).StatusCode);
        Assert.Equal("checkout@example.test", Assert.Single(_stripe.StripeHandler.Requests)["customer_email"]);

        // A known customer wins over the address.
        WithDb(_stripe, db => db.Users.Single(x => x.Id == id).BillingCustomerId = "cus_known");
        _stripe.StripeHandler.Clear();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout", null)).StatusCode);
        var returning = Assert.Single(_stripe.StripeHandler.Requests);
        Assert.Equal("cus_known", returning["customer"]);
        Assert.Null(returning["customer_email"]);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _stripe.NewClient().PostAsync("/api/billing/checkout", null)).StatusCode);
    }

    [Fact]
    public async Task Checkout_returns_to_the_compare_screen_when_asked_and_drops_anything_else()
    {
        var (client, _, _) = await _stripe.NewUserAsync("bill_return");

        // Round 20: ?return=compare, the one value the allowlist knows, rides on both URLs Stripe is handed.
        _stripe.StripeHandler.Clear();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout?return=compare", null)).StatusCode);
        var asked = Assert.Single(_stripe.StripeHandler.Requests);
        Assert.Equal("http://localhost/#/pro?checkout=success&return=compare", asked["success_url"]);
        Assert.Equal("http://localhost/#/pro?checkout=cancel&return=compare", asked["cancel_url"]);

        // Anything else is dropped silently: the plain URLs, as with no parameter at all.
        foreach (var value in new[] { "evil", "Compare", "compare%20", "wardrobe", "" })
        {
            _stripe.StripeHandler.Clear();
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout?return=" + value + "&currency=USD", null)).StatusCode);
            var plain = Assert.Single(_stripe.StripeHandler.Requests);
            Assert.Equal("http://localhost/#/pro?checkout=success", plain["success_url"]);
            Assert.Equal("http://localhost/#/pro?checkout=cancel", plain["cancel_url"]);
        }
    }

    [Fact]
    public async Task Checkout_is_refused_for_an_account_that_is_already_pro()
    {
        var (client, id, handle) = await _stripe.NewUserAsync("bill_twice");
        Assert.Equal(AdminChange.Changed, await AdminSync.SetProAsync(_stripe.ConnectionString, handle, DateTime.UtcNow.AddDays(20)));
        _stripe.StripeHandler.Clear();

        // A tab that still shows "Go Pro" cannot open a second subscription: 409, and nothing reaches Stripe.
        var response = await client.PostAsync("/api/billing/checkout", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("You're already on Pro.", await ErrorOf(response));
        Assert.Empty(_stripe.StripeHandler.Requests);

        // A lapsed Pro is free again and may subscribe.
        WithDb(_stripe, db => db.Users.Single(u => u.Id == id).ProUntil = DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout", null)).StatusCode);
        Assert.Single(_stripe.StripeHandler.Requests);
    }

    [Fact]
    public async Task Checkout_answers_502_when_stripe_refuses()
    {
        var (client, _, _) = await _stripe.NewUserAsync("bill_refused");
        _stripe.StripeHandler.StatusCode = HttpStatusCode.PaymentRequired;
        _stripe.StripeHandler.Response = """{ "error": { "type": "invalid_request_error", "message": "No such price" } }""";
        try
        {
            var response = await client.PostAsync("/api/billing/checkout", null);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal("Checkout didn't open. Try again in a moment.", await ErrorOf(response));
        }
        finally
        {
            _stripe.StripeHandler.StatusCode = HttpStatusCode.OK;
            _stripe.StripeHandler.Response = new RecordingStripeHandler().Response;
        }
    }

    // ---- the portal (Round 11) ----

    [Fact]
    public async Task Portal_is_404_while_billing_is_manual_or_the_account_never_went_through_checkout()
    {
        // Manual: a Pro granted by hand has nothing on Stripe's side, in the account's language.
        var (manual, _, handle) = await _manual.NewUserAsync("bill_portal_manual", language: "he");
        Assert.Equal(AdminChange.Changed, await AdminSync.SetProAsync(_manual.ConnectionString, handle, DateTime.UtcNow.AddDays(30)));
        var response = await manual.PostAsync("/api/billing/portal", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("כדי לנהל את התוכנית, כותבים לנו.", await ErrorOf(response));
        Assert.Empty(_manual.StripeHandler.PortalRequests);

        // Stripe live, but this account has no customer id (free, or Pro by --pro): the same 404, and nothing is sent.
        var (stripe, _, _) = await _stripe.NewUserAsync("bill_portal_nocust");
        _stripe.StripeHandler.Clear();
        var noCustomer = await stripe.PostAsync("/api/billing/portal", null);
        Assert.Equal(HttpStatusCode.NotFound, noCustomer.StatusCode);
        Assert.Equal("Manage your plan by writing to us.", await ErrorOf(noCustomer));
        Assert.Empty(_stripe.StripeHandler.Requests);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _stripe.NewClient().PostAsync("/api/billing/portal", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _stripe.BareClient().PostAsync("/api/billing/portal", null)).StatusCode);
    }

    /// <summary>
    /// A deleted account must not keep paying. Nothing on Stripe's side belongs to a row that no longer exists: the
    /// portal needs a session, the webhook can find no owner, and the card would be charged every month until the person
    /// met it on a statement. So the subscription is ended first, and only then is the account gone.
    /// </summary>
    [Fact]
    public async Task Deleting_an_account_ends_its_subscription_before_the_row_goes()
    {
        var (client, id, _) = await _stripe.NewUserAsync("bill_delete");
        WithDb(_stripe, db =>
        {
            var u = db.Users.Single(x => x.Id == id);
            u.Plan = "pro";
            u.ProUntil = DateTime.UtcNow.AddDays(20);
            u.BillingCustomerId = "cus_gone";
            u.BillingSubscriptionId = "sub_gone";
        });
        _stripe.StripeHandler.Clear();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/users/me")).StatusCode);

        var cancel = Assert.Single(_stripe.StripeHandler.CancelRequests);
        Assert.Equal(HttpMethod.Delete, cancel.Method);
        Assert.Equal("https://api.stripe.com/v1/subscriptions/sub_gone", cancel.Uri.ToString());
        Assert.Equal($"Bearer {StripeBillingApp.SecretKey}", cancel.Authorization);
        WithDb(_stripe, db => Assert.Null(db.Users.SingleOrDefault(x => x.Id == id)));
    }

    /// <summary>
    /// And when Stripe will not confirm it, nothing is deleted: half a deletion is the case this exists to prevent, so
    /// the person keeps the account, keeps the portal, and can try again.
    /// </summary>
    [Fact]
    public async Task A_refused_cancel_stops_the_deletion_and_the_account_stands()
    {
        var (client, id, _) = await _stripe.NewUserAsync("bill_delete_refused");
        WithDb(_stripe, db =>
        {
            var u = db.Users.Single(x => x.Id == id);
            u.Plan = "pro";
            u.ProUntil = DateTime.UtcNow.AddDays(20);
            u.BillingCustomerId = "cus_stays";
            u.BillingSubscriptionId = "sub_stays";
        });
        _stripe.StripeHandler.Clear();
        _stripe.StripeHandler.CancelStatusCode = HttpStatusCode.InternalServerError;
        try
        {
            var refused = await client.DeleteAsync("/api/users/me");
            Assert.Equal(HttpStatusCode.BadGateway, refused.StatusCode);
            Assert.Equal("We could not end your subscription with the payment provider, so nothing was deleted. Try again in a few minutes.", await ErrorOf(refused));
            Assert.Single(_stripe.StripeHandler.CancelRequests);
            WithDb(_stripe, db =>
            {
                var still = db.Users.Single(x => x.Id == id);
                Assert.Equal("sub_stays", still.BillingSubscriptionId);
            });
        }
        finally
        {
            _stripe.StripeHandler.CancelStatusCode = null;
        }
    }

    [Fact]
    public async Task Portal_opens_a_session_for_the_accounts_customer_that_returns_to_settings()
    {
        var (client, id, _) = await _stripe.NewUserAsync("bill_portal");
        WithDb(_stripe, db => { var u = db.Users.Single(x => x.Id == id); u.Plan = "pro"; u.ProUntil = DateTime.UtcNow.AddDays(20); u.BillingCustomerId = "cus_portal"; });
        _stripe.StripeHandler.Clear();

        var response = await client.PostAsync("/api/billing/portal", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(RecordingStripeHandler.DefaultPortalUrl, (await Json(response)).GetProperty("url").GetString());

        var request = Assert.Single(_stripe.StripeHandler.PortalRequests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.stripe.com/v1/billing_portal/sessions", request.Uri.ToString());
        Assert.Equal("Bearer " + StripeBillingApp.SecretKey, request.Authorization);
        Assert.Equal("cus_portal", request["customer"]);
        Assert.Equal("http://localhost/#/settings", request["return_url"]);
        Assert.Equal(2, request.Form.Count);
        // Nothing changes on the row: what the person does on the portal comes back through the webhook.
        Assert.Equal("pro", UserOf(_stripe, id).Plan);

        // A lapsed account with a customer still gets the portal: the card can be fixed there.
        WithDb(_stripe, db => db.Users.Single(x => x.Id == id).ProUntil = DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/portal", null)).StatusCode);

        // Behind a tunnel or a proxy the configured origin is the one Stripe returns to.
        using var tunnelled = new TestApp
        {
            BillingProvider = "stripe", StripeSecretKey = StripeBillingApp.SecretKey, StripePriceId = StripeBillingApp.PriceId, StripeWebhookSecret = StripeBillingApp.WebhookSecret,
            Settings = { ["Billing:PublicOrigin"] = "https://orevosh.example/" }
        };
        var (far, farId, _) = await tunnelled.NewUserAsync("bill_portal_far");
        WithDb(tunnelled, db => db.Users.Single(x => x.Id == farId).BillingCustomerId = "cus_far");
        Assert.Equal(HttpStatusCode.OK, (await far.PostAsync("/api/billing/portal", null)).StatusCode);
        Assert.Equal("https://orevosh.example/#/settings", Assert.Single(tunnelled.StripeHandler.PortalRequests)["return_url"]);
    }

    [Fact]
    public async Task Portal_answers_502_when_stripe_refuses_or_names_no_page()
    {
        var (client, id, _) = await _stripe.NewUserAsync("bill_portal_refused", language: "ru");
        WithDb(_stripe, db => db.Users.Single(x => x.Id == id).BillingCustomerId = "cus_refused");
        _stripe.StripeHandler.StatusCode = HttpStatusCode.BadRequest;
        _stripe.StripeHandler.PortalResponse = """{ "error": { "type": "invalid_request_error", "message": "No such customer" } }""";
        try
        {
            var response = await client.PostAsync("/api/billing/portal", null);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal("Страница оплаты не открылась. Попробуй через минуту.", await ErrorOf(response));

            _stripe.StripeHandler.StatusCode = HttpStatusCode.OK;
            _stripe.StripeHandler.PortalResponse = """{ "id": "bps_no_url", "object": "billing_portal.session" }""";
            Assert.Equal(HttpStatusCode.BadGateway, (await client.PostAsync("/api/billing/portal", null)).StatusCode);
        }
        finally
        {
            _stripe.StripeHandler.StatusCode = HttpStatusCode.OK;
            _stripe.StripeHandler.PortalResponse = new RecordingStripeHandler().PortalResponse;
        }
    }

    // ---- the subscription id (Round 11) ----

    [Fact]
    public async Task Webhook_checkout_completed_stores_the_subscription_and_deleted_ends_pro_only_for_that_one()
    {
        var (client, id, _) = await _stripe.NewUserAsync("bill_subid");

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_subid", "sub_paid"))).StatusCode);
        var user = UserOf(_stripe, id);
        Assert.Equal("pro", user.Plan);
        Assert.Equal("cus_subid", user.BillingCustomerId);
        Assert.Equal("sub_paid", user.BillingSubscriptionId);

        // Another subscription on the same customer ends (a stale tab's, cancelled on Stripe's side): Pro stays, the id stays.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionDeleted("cus_subid", "sub_other"))).StatusCode);
        user = UserOf(_stripe, id);
        AssertAround(user.ProUntil, DateTime.UtcNow.AddDays(35));
        Assert.Equal("sub_paid", user.BillingSubscriptionId);
        Assert.Equal("pro", (await Json(await client.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());

        // The paid one ends: Pro ends now and the id is cleared, so the next Checkout starts clean.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionDeleted("cus_subid", "sub_paid"))).StatusCode);
        user = UserOf(_stripe, id);
        Assert.True(user.ProUntil <= DateTime.UtcNow.AddSeconds(1));
        Assert.Null(user.BillingSubscriptionId);
        Assert.Equal("cus_subid", user.BillingCustomerId);
        Assert.Equal("free", (await Json(await client.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());

        // A new Checkout: the expanded subscription object yields its id, and a later one replaces an earlier one (logged).
        var expanded = new { id = "sub_expanded", @object = "subscription" };
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_subid", expanded))).StatusCode);
        Assert.Equal("sub_expanded", UserOf(_stripe, id).BillingSubscriptionId);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_subid", "sub_again"))).StatusCode);
        Assert.Equal("sub_again", UserOf(_stripe, id).BillingSubscriptionId);

        // An id longer than the column is cut, and the cut id still matches the event that names it in full.
        var longId = "sub_" + new string('x', 80);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_subid", longId))).StatusCode);
        Assert.Equal(longId[..64], UserOf(_stripe, id).BillingSubscriptionId);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionDeleted("cus_subid", longId))).StatusCode);
        Assert.Null(UserOf(_stripe, id).BillingSubscriptionId);

        // A Checkout event without a subscription (a hand-made one) grants as before and stores nothing.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_subid"))).StatusCode);
        Assert.Equal("pro", UserOf(_stripe, id).Plan);
        Assert.Null(UserOf(_stripe, id).BillingSubscriptionId);
    }

    /// <summary>
    /// Round 17. A declined card used to move a paying person to three days from the end of Pro in silence: they
    /// would find the app smaller one morning, having done nothing wrong and having been asked for nothing. A card
    /// expires — that is not a decision to cancel, and treating it as one loses a subscriber who wanted to stay.
    /// <para>
    /// Billing news goes only to an address the person CONFIRMED. An unverified address is a typo as often as it is a
    /// mailbox, and a letter about somebody's card must not reach a stranger.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Webhook_tells_a_subscriber_when_the_card_fails_and_when_pro_ends()
    {
        // Counted per address rather than by clearing the recorder: these tests share one app and run in parallel, so
        // a recorder somebody else is waiting on must never be wiped. Every account here has an address of its own.
        const string address = "dunning@example.test";
        var (_, id, handle) = await _stripe.NewUserAsync("bill_notice");
        WithDb(_stripe, db =>
        {
            var u = db.Users.Single(x => x.Id == id);
            u.Email = address;
            u.EmailVerifiedAt = DateTime.UtcNow;
        });
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_notice", "sub_notice"))).StatusCode);

        // The card is declined. Pro is cut to the slack — and the person is told, with the date and a way to fix it.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_notice", "past_due", id: "sub_notice"))).StatusCode);
        var warned = Assert.Single(_stripe.Email.To(address));
        Assert.Contains("didn't go through", warned.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(handle, warned.Body, StringComparison.Ordinal);
        // The link goes to Settings, where "Manage subscription" opens the portal: a portal url cannot be linked to
        // directly, because Stripe mints one per person on demand.
        Assert.Contains("/#/settings", warned.Body, StringComparison.Ordinal);

        // Pro ends. A second, different letter — and it says the account and everything in it is still there.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionDeleted("cus_notice", "sub_notice"))).StatusCode);
        var sent = _stripe.Email.To(address);
        Assert.Equal(2, sent.Count);
        Assert.Contains("ended", sent[1].Subject, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(warned.Subject, sent[1].Subject);

        // A subscription deleted for somebody who was not Pro anyway says nothing: there was nothing to lose.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionDeleted("cus_notice", "sub_notice"))).StatusCode);
        Assert.Equal(2, _stripe.Email.To(address).Count);

        // An UNVERIFIED address hears nothing at all: it is as likely to be a typo as a mailbox.
        const string typo = "typo@example.test";
        var (_, unverifiedId, _) = await _stripe.NewUserAsync("bill_notice_typo");
        WithDb(_stripe, db =>
        {
            var u = db.Users.Single(x => x.Id == unverifiedId);
            u.Email = typo;
            u.EmailVerifiedAt = null;
        });
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(unverifiedId.ToString("N"), null, "cus_notice_typo", "sub_notice_typo"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_notice_typo", "past_due", id: "sub_notice_typo"))).StatusCode);
        Assert.Empty(_stripe.Email.To(typo));
        // And Pro was still cut to the slack: the letter is a courtesy, never the mechanism.
        Assert.True(UserOf(_stripe, unverifiedId).ProUntil <= DateTime.UtcNow.AddDays(3).AddSeconds(1));
    }

    /// <summary>
    /// Round 17. Mail that throws must not fail the webhook: Stripe retries a non-200 and the work beside the letter
    /// would run twice. Its own app, because making the shared recorder throw would break everything running beside it.
    /// </summary>
    [Fact]
    public async Task Webhook_still_answers_stripe_when_the_letter_cannot_be_sent()
    {
        await using var app = new StripeBillingApp();
        var (_, id, _) = await app.NewUserAsync("bill_mailbroken");
        WithDb(app, db =>
        {
            var u = db.Users.Single(x => x.Id == id);
            u.Email = "broken@example.test";
            u.EmailVerifiedAt = DateTime.UtcNow;
        });
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(app, CheckoutCompleted(id.ToString("N"), null, "cus_broken", "sub_broken"))).StatusCode);

        app.Email.Fail = true;
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(app, SubscriptionUpdated("cus_broken", "past_due", id: "sub_broken"))).StatusCode);
        Assert.True(UserOf(app, id).ProUntil <= DateTime.UtcNow.AddDays(3).AddSeconds(1));
    }

    /// <summary>A charge Stripe refunded, in full or in part. A dispute carries neither field and is always the whole charge.</summary>
    private static object ChargeRefunded(string customer, long amount, long refunded) => new
    {
        id = NewEventId(),
        type = "charge.refunded",
        data = new { @object = new { id = "ch_1", @object = "charge", customer, amount, amount_refunded = refunded, refunded = refunded >= amount } }
    };

    private static object ChargeDisputed(string customer) => new
    {
        id = NewEventId(),
        type = "charge.dispute.created",
        data = new { @object = new { id = "dp_1", @object = "dispute", customer, amount = 1990L } }
    };

    /// <summary>
    /// Round 17. A refund does not cancel a subscription — Stripe goes on billing — so taking Pro away for a PARTIAL
    /// one is the worst outcome available: the person keeps paying and loses what they are paying for. Five shekels
    /// back as an apology used to end a subscription. Only a charge refunded in full ends Pro now; a partial one
    /// still reaches the owner, because a refund he did not issue is worth knowing about either way.
    ///
    /// None of this had a test before Round 17 — the handler shipped in Round 13 and nothing ever exercised it.
    /// </summary>
    [Fact]
    public async Task Webhook_only_a_full_refund_ends_pro_and_a_dispute_always_does()
    {
        // A partial refund on a paying account: Pro is untouched.
        var (client, id, _) = await _stripe.NewUserAsync("bill_partial");
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_partial", "sub_partial"))).StatusCode);
        Assert.Equal("pro", UserOf(_stripe, id).Plan);
        var granted = UserOf(_stripe, id).ProUntil;

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, ChargeRefunded("cus_partial", 1990, 500))).StatusCode);
        var after = UserOf(_stripe, id);
        Assert.Equal(granted, after.ProUntil);
        Assert.Equal("pro", (await Json(await client.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());
        // And the subscription id is left alone: the subscription is still live and still billing.
        Assert.Equal("sub_partial", after.BillingSubscriptionId);

        // A refund of everything but one minor unit is still not everything.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, ChargeRefunded("cus_partial", 1990, 1989))).StatusCode);
        Assert.Equal("pro", UserOf(_stripe, id).Plan);

        // The whole charge back: Pro ends now.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, ChargeRefunded("cus_partial", 1990, 1990))).StatusCode);
        Assert.True(UserOf(_stripe, id).ProUntil <= DateTime.UtcNow.AddSeconds(1));
        Assert.Equal("free", (await Json(await client.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());

        // A dispute names no amounts at all and is always the whole charge: it ends Pro on its own.
        var (disputed, disputedId, _) = await _stripe.NewUserAsync("bill_dispute");
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(disputedId.ToString("N"), null, "cus_dispute", "sub_dispute"))).StatusCode);
        Assert.Equal("pro", UserOf(_stripe, disputedId).Plan);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, ChargeDisputed("cus_dispute"))).StatusCode);
        Assert.True(UserOf(_stripe, disputedId).ProUntil <= DateTime.UtcNow.AddSeconds(1));
        Assert.Equal("free", (await Json(await disputed.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());

        // A payload with no amounts on a refund is not evidence of a full one, so it leaves Pro alone.
        var (_, quietId, _) = await _stripe.NewUserAsync("bill_quiet");
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(quietId.ToString("N"), null, "cus_quiet", "sub_quiet"))).StatusCode);
        var shapeless = new { id = "evt_bare", type = "charge.refunded", data = new { @object = new { id = "ch_2", @object = "charge", customer = "cus_quiet" } } };
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, shapeless)).StatusCode);
        Assert.Equal("pro", UserOf(_stripe, quietId).Plan);
    }

    [Fact]
    public async Task Webhook_subscription_deleted_with_no_id_stored_keeps_the_customer_only_matching()
    {
        // An account from before Round 11: Pro from a Checkout that stored no id. Its cancellation still ends Pro.
        var (_, id, _) = await _stripe.NewUserAsync("bill_legacy_cancel");
        WithDb(_stripe, db => { var u = db.Users.Single(x => x.Id == id); u.Plan = "pro"; u.ProUntil = DateTime.UtcNow.AddDays(20); u.BillingCustomerId = "cus_legacy"; u.BillingSubscriptionId = null; });

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionDeleted("cus_legacy", "sub_whatever"))).StatusCode);
        var user = UserOf(_stripe, id);
        Assert.True(user.ProUntil <= DateTime.UtcNow.AddSeconds(1));
        Assert.Null(user.BillingSubscriptionId);
    }

    [Fact]
    public async Task Webhook_subscription_updated_ignores_another_subscription_and_adopts_one_when_none_is_stored()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_sub_updated");
        var (_, legacyId, _) = await _stripe.NewUserAsync("bill_sub_legacy");
        WithDb(_stripe, db =>
        {
            var u = db.Users.Single(x => x.Id == id);
            u.Plan = "pro"; u.ProUntil = DateTime.UtcNow.AddDays(10); u.BillingCustomerId = "cus_upd"; u.BillingSubscriptionId = "sub_paid";
            var legacy = db.Users.Single(x => x.Id == legacyId);
            legacy.Plan = "pro"; legacy.ProUntil = DateTime.UtcNow.AddDays(10); legacy.BillingCustomerId = "cus_upd_legacy"; legacy.BillingSubscriptionId = null;
        });

        // Another subscription's period, or its dunning, moves nothing.
        var periodEnd = DateTimeOffset.UtcNow.AddDays(40);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_upd", "active", periodEnd, id: "sub_other"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(10));
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_upd", "past_due", id: "sub_other"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(10));
        Assert.Equal("sub_paid", UserOf(_stripe, id).BillingSubscriptionId);

        // The paid one does.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_upd", "active", periodEnd, id: "sub_paid"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(43));

        // An account that stored no id adopts the first subscription it hears of, even when the status moves nothing.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_upd_legacy", "incomplete", id: "sub_adopted"))).StatusCode);
        Assert.Equal("sub_adopted", UserOf(_stripe, legacyId).BillingSubscriptionId);
        AssertAround(UserOf(_stripe, legacyId).ProUntil, DateTime.UtcNow.AddDays(10));
        // ... and from then on tells the others apart.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_upd_legacy", "active", periodEnd, id: "sub_stranger"))).StatusCode);
        AssertAround(UserOf(_stripe, legacyId).ProUntil, DateTime.UtcNow.AddDays(10));
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_upd_legacy", "active", periodEnd, id: "sub_adopted"))).StatusCode);
        AssertAround(UserOf(_stripe, legacyId).ProUntil, DateTime.UtcNow.AddDays(43));
    }

    [Fact]
    public async Task Webhook_subscription_created_fills_an_empty_id_and_grants_nothing()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_sub_created");
        WithDb(_stripe, db => db.Users.Single(x => x.Id == id).BillingCustomerId = "cus_created");

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionCreated("cus_created", "sub_first"))).StatusCode);
        var user = UserOf(_stripe, id);
        Assert.Equal("sub_first", user.BillingSubscriptionId);
        Assert.Equal("free", user.Plan);   // the checkout event is what grants

        // A second one on the same customer is not adopted; the first stays.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionCreated("cus_created", "sub_second"))).StatusCode);
        Assert.Equal("sub_first", UserOf(_stripe, id).BillingSubscriptionId);

        // The same id again, or an unknown customer: nothing changes, 200.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionCreated("cus_created", "sub_first"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionCreated("cus_nobody", "sub_x"))).StatusCode);
        Assert.Equal("sub_first", UserOf(_stripe, id).BillingSubscriptionId);
    }

    // ---- webhook ----

    [Fact]
    public async Task Webhook_refuses_a_missing_wrong_or_stale_signature()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_badsig");
        var payload = CheckoutCompleted(id.ToString("N"), null, "cus_badsig");

        Assert.Equal(HttpStatusCode.BadRequest, (await PostEventAsync(_stripe, payload, signature: "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostEventAsync(_stripe, payload, secret: "whsec_somebody_else")).StatusCode);
        var stale = DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds();
        Assert.Equal(HttpStatusCode.BadRequest, (await PostEventAsync(_stripe, payload, timestamp: stale)).StatusCode);
        // A signature over another body does not cover this one.
        var other = StripeClient.SignatureHeaderValue(StripeBillingApp.WebhookSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "{}");
        var refused = await PostEventAsync(_stripe, payload, signature: other);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("That event isn't signed by Stripe.", await ErrorOf(refused));

        Assert.Equal("free", UserOf(_stripe, id).Plan);
        Assert.Null(UserOf(_stripe, id).BillingCustomerId);

        // With billing off there is no secret to check against: everything is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await PostEventAsync(_manual, payload)).StatusCode);
    }

    [Fact]
    public void Signature_check_accepts_any_of_the_rotating_v1_values_within_the_window()
    {
        const string secret = "whsec_unit";
        const string body = """{"id":"evt_unit","type":"ping"}""";
        var now = DateTimeOffset.UtcNow;
        var t = now.ToUnixTimeSeconds();
        var good = StripeClient.Sign(secret, t, body);

        Assert.True(StripeClient.VerifySignature($"t={t},v1={good}", body, secret, now));
        Assert.True(StripeClient.VerifySignature($"t={t},v1={new string('0', 64)},v1={good}", body, secret, now));
        Assert.True(StripeClient.VerifySignature($"t={t},v1={good}", body, secret, now.AddMinutes(4)));
        Assert.False(StripeClient.VerifySignature($"t={t},v1={good}", body, secret, now.AddMinutes(6)));
        Assert.False(StripeClient.VerifySignature($"t={t},v1={good}", body, secret, now.AddMinutes(-6)));
        Assert.False(StripeClient.VerifySignature($"t={t},v1={good}", body + " ", secret, now));
        Assert.False(StripeClient.VerifySignature($"t={t},v1={good}", body, "whsec_other", now));
        Assert.False(StripeClient.VerifySignature($"v1={good}", body, secret, now));
        Assert.False(StripeClient.VerifySignature($"t={t}", body, secret, now));
        Assert.False(StripeClient.VerifySignature(null, body, secret, now));
        Assert.False(StripeClient.VerifySignature($"t={t},v1={good}", body, "", now));
    }

    [Fact]
    public async Task Webhook_checkout_completed_promotes_the_referenced_account_and_keeps_the_customer()
    {
        var (client, id, _) = await _stripe.NewUserAsync("bill_promote");
        var (otherClient, otherId, _) = await _stripe.NewUserAsync("bill_bystander");

        var response = await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_promote"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = UserOf(_stripe, id);
        Assert.Equal("pro", user.Plan);
        AssertAround(user.ProUntil, DateTime.UtcNow.AddDays(35));
        Assert.Equal("cus_promote", user.BillingCustomerId);
        Assert.Equal("free", UserOf(_stripe, otherId).Plan);

        var me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.Equal("pro", me.GetProperty("plan").GetString());
        AssertAround(me.GetProperty("proUntil").GetDateTime(), DateTime.UtcNow.AddDays(35));
        Assert.Equal(30, me.GetProperty("checksPerDay").GetInt32());

        // metadata.userId alone (no client_reference_id) finds the account too, and an expanded customer object still yields its id.
        var expanded = new
        {
            id = "evt_checkout2",
            type = "checkout.session.completed",
            data = new { @object = new { id = "cs_2", client_reference_id = (string?)null, customer = new { id = "cus_bystander", @object = "customer" }, metadata = new { userId = otherId.ToString("D") } } }
        };
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, expanded)).StatusCode);
        var other = UserOf(_stripe, otherId);
        Assert.Equal("pro", other.Plan);
        Assert.Equal("cus_bystander", other.BillingCustomerId);
        Assert.Equal("pro", (await Json(await otherClient.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());

        // Paying on top of a period still running (a --pro grant) adds the 35 days to its end; it never cuts it.
        WithDb(_stripe, db => db.Users.Single(u => u.Id == id).ProUntil = DateTime.UtcNow.AddDays(100));
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_promote"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(135));

        // An account that does not exist is not an error for Stripe: 200, nothing changes.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(Guid.NewGuid().ToString("N"), null, "cus_nobody"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(null, null, "cus_nobody"))).StatusCode);
    }

    [Fact]
    public async Task Webhook_invoice_paid_sets_the_end_from_the_invoice_period_and_never_stacks()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_renew");
        var (_, lapsedId, _) = await _stripe.NewUserAsync("bill_lapsed");
        var (_, giftedId, _) = await _stripe.NewUserAsync("bill_gifted");
        WithDb(_stripe, db =>
        {
            var running = db.Users.Single(u => u.Id == id);
            running.Plan = "pro"; running.ProUntil = DateTime.UtcNow.AddDays(10); running.BillingCustomerId = "cus_renew";
            var lapsed = db.Users.Single(u => u.Id == lapsedId);
            lapsed.Plan = "pro"; lapsed.ProUntil = DateTime.UtcNow.AddDays(-5); lapsed.BillingCustomerId = "cus_lapsed";
            var gifted = db.Users.Single(u => u.Id == giftedId);
            gifted.Plan = "pro"; gifted.ProUntil = DateTime.UtcNow.AddDays(100); gifted.BillingCustomerId = "cus_gifted";
        });

        // A renewal is paid through the period its line names, plus three days of slack: read from the invoice, not
        // counted from the previous end date, so a cycle every ~30 days does not run ahead by the difference each time.
        var periodEnd = DateTimeOffset.UtcNow.AddDays(30);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_renew", periodEnd: periodEnd, eventId: "evt_invoice_renew"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(33));

        // The same event again (Stripe retries until it sees a 2xx): Round 20 ignores it by id, and it named the same
        // period anyway. Nothing stacks either way.
        var replayed = await PostEventAsync(_stripe, InvoicePaid("cus_renew", periodEnd: periodEnd, eventId: "evt_invoice_renew"));
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.True((await Json(replayed)).GetProperty("replayed").GetBoolean());
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(33));

        // The next cycle moves the end to its own period.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_renew", periodEnd: periodEnd.AddDays(30)))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(63));

        // A lapsed account comes back for the period paid.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_lapsed", periodEnd: periodEnd))).StatusCode);
        var lapsedUser = UserOf(_stripe, lapsedId);
        Assert.Equal("pro", lapsedUser.Plan);
        AssertAround(lapsedUser.ProUntil, DateTime.UtcNow.AddDays(33));

        // A payment never shortens what is there: a --pro grant that runs past the period stays.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_gifted", periodEnd: periodEnd))).StatusCode);
        AssertAround(UserOf(_stripe, giftedId).ProUntil, DateTime.UtcNow.AddDays(100));

        // An invoice naming no period (a hand-made event) is worth one period from now, not from the previous end.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_lapsed"))).StatusCode);
        AssertAround(UserOf(_stripe, lapsedId).ProUntil, DateTime.UtcNow.AddDays(35));

        // The subscription's first invoice is the checkout that already granted the period: no second helping.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_renew", "subscription_create", periodEnd.AddDays(60)))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(63));

        // An unknown customer is 200 and changes nothing.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_unknown", periodEnd: periodEnd))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(63));
    }

    [Fact]
    public async Task Webhook_subscription_updated_follows_the_status()
    {
        var (client, id, _) = await _stripe.NewUserAsync("bill_dunning");
        var (_, freeId, _) = await _stripe.NewUserAsync("bill_dunning_free");
        WithDb(_stripe, db =>
        {
            var u = db.Users.Single(x => x.Id == id);
            u.Plan = "pro"; u.ProUntil = DateTime.UtcNow.AddDays(40); u.BillingCustomerId = "cus_dunning";
            db.Users.Single(x => x.Id == freeId).BillingCustomerId = "cus_dunning_free";
        });

        // A renewal that failed: collection stopped, and what is left is three days, not the forty granted on the promise of it.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning", "past_due"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(3));
        Assert.Equal("pro", (await Json(await client.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());

        // Unpaid after the retries, with less than the slack left: nothing moves.
        WithDb(_stripe, db => db.Users.Single(x => x.Id == id).ProUntil = DateTime.UtcNow.AddDays(1));
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning", "unpaid"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(1));

        // The card was fixed: active again, paid through the subscription's current period plus the slack.
        var periodEnd = DateTimeOffset.UtcNow.AddDays(25);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning", "active", periodEnd))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(28));

        // Never shorter: an older period (a replayed or out-of-order event) changes nothing, nor does an active event naming none.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning", "active", periodEnd.AddDays(-10)))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(28));
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning", "active"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(28));

        // Paused: three days from now, however far the end date was.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning", "paused"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(3));

        // Since Stripe's 2025-03-31 API version the period sits on the subscription's items: read there too.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning", "active", periodEnd.AddDays(30), onItems: true))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(58));

        // A free account with a customer of its own is not made Pro by a failure; an active period does make it Pro.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning_free", "past_due"))).StatusCode);
        Assert.Equal("free", UserOf(_stripe, freeId).Plan);
        Assert.Null(UserOf(_stripe, freeId).ProUntil);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning_free", "active", periodEnd))).StatusCode);
        Assert.Equal("pro", UserOf(_stripe, freeId).Plan);
        AssertAround(UserOf(_stripe, freeId).ProUntil, DateTime.UtcNow.AddDays(28));

        // Other statuses (incomplete here; a cancellation arrives as the deleted event) and unknown customers change nothing.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_dunning", "incomplete"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(58));
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_nobody", "past_due"))).StatusCode);
    }

    [Fact]
    public async Task Webhook_subscription_deleted_ends_pro_now()
    {
        var (client, id, _) = await _stripe.NewUserAsync("bill_cancel");
        WithDb(_stripe, db =>
        {
            var u = db.Users.Single(x => x.Id == id);
            u.Plan = "pro"; u.ProUntil = DateTime.UtcNow.AddDays(20); u.BillingCustomerId = "cus_cancel";
        });
        Assert.Equal("pro", (await Json(await client.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionDeleted("cus_cancel"))).StatusCode);
        var user = UserOf(_stripe, id);
        AssertAround(user.ProUntil, DateTime.UtcNow);
        Assert.True(user.ProUntil <= DateTime.UtcNow.AddSeconds(1));
        Assert.Equal("cus_cancel", user.BillingCustomerId);
        Assert.Null(user.BillingSubscriptionId);

        var me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.Equal("free", me.GetProperty("plan").GetString());
        Assert.Equal(3, me.GetProperty("checksPerDay").GetInt32());
        Assert.Equal("free", (await Json(await client.GetAsync("/api/billing/state"))).GetProperty("plan").GetString());
    }

    [Fact]
    public async Task Webhook_answers_200_to_events_it_does_not_handle_and_400_to_no_json()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_unknown");
        var response = await PostEventAsync(_stripe, new { id = "evt_x", type = "customer.created", data = new { @object = new { id = "cus_x", client_reference_id = id.ToString("N") } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("received").GetBoolean());
        Assert.Equal("free", UserOf(_stripe, id).Plan);

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, new { id = "evt_y", type = "payment_intent.succeeded" })).StatusCode);

        var notJson = "this is not json";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(notJson, Encoding.UTF8, "text/plain") };
        request.Headers.TryAddWithoutValidation(StripeClient.SignatureHeader, StripeClient.SignatureHeaderValue(StripeBillingApp.WebhookSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), notJson));
        Assert.Equal(HttpStatusCode.BadRequest, (await _stripe.BareClient().SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Csrf_header_is_waived_for_the_webhook_path_only()
    {
        // The webhook reaches its handler without the header (and is then refused for its signature, not for CSRF).
        var response = await PostEventAsync(_stripe, new { id = "evt_csrf", type = "ping" }, signature: "t=1,v1=00");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostEventAsync(_manual, new { id = "evt_csrf", type = "ping" }, signature: "t=1,v1=00")).StatusCode);

        // Every other state change still needs it, checkout included.
        var (client, _, _) = await _stripe.NewUserAsync("bill_csrf");
        var bare = _stripe.BareClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.PostAsync("/api/billing/checkout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.PostAsync("/api/billing/webhooks", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout", null)).StatusCode);
    }

    // ---- the --pro command ----

    [Fact]
    public async Task Pro_command_turns_pro_on_until_a_date_and_off_again()
    {
        var (client, id, _) = await _manual.NewUserAsync("bill_cmd");
        var until = DateTime.UtcNow.AddDays(62);

        // Case-insensitive, like --admin; a leading @ is fine too.
        Assert.Equal(AdminChange.Changed, await AdminSync.SetProAsync(_manual.ConnectionString, "@BILL_cmd", until));
        var user = UserOf(_manual, id);
        Assert.Equal("pro", user.Plan);
        AssertAround(user.ProUntil, until);
        var me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.Equal("pro", me.GetProperty("plan").GetString());
        AssertAround(me.GetProperty("proUntil").GetDateTime(), until);
        Assert.Equal(30, me.GetProperty("checksPerDay").GetInt32());

        Assert.Equal(AdminChange.Changed, await AdminSync.SetProAsync(_manual.ConnectionString, "bill_cmd", null));
        user = UserOf(_manual, id);
        Assert.Equal("free", user.Plan);
        Assert.Null(user.ProUntil);
        me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.Equal("free", me.GetProperty("plan").GetString());
        Assert.False(me.TryGetProperty("proUntil", out _));
        Assert.Equal(3, me.GetProperty("checksPerDay").GetInt32());

        Assert.Equal(AdminChange.Unchanged, await AdminSync.SetProAsync(_manual.ConnectionString, "bill_cmd", null));
        Assert.Equal(AdminChange.NotFound, await AdminSync.SetProAsync(_manual.ConnectionString, "nobody_here", until));
        Assert.Equal(AdminChange.NotFound, await AdminSync.SetProAsync(_manual.ConnectionString, "nobody_here", null));
    }

    // ---- me and config ----

    [Fact]
    public async Task Me_carries_the_plan_the_days_checks_and_the_cap()
    {
        var (client, id, handle) = await _manual.NewUserAsync("bill_me");

        var me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.Equal("free", me.GetProperty("plan").GetString());
        Assert.False(me.GetProperty("verified").GetBoolean());
        Assert.Equal(0, me.GetProperty("checksToday").GetInt32());
        Assert.Equal(3, me.GetProperty("checksPerDay").GetInt32());

        await _manual.CheckAsync(client);
        me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.Equal(1, me.GetProperty("checksToday").GetInt32());

        // A comparison spends the same allowance; a failed call and yesterday's check do not.
        WithDb(_manual, db =>
        {
            db.Comparisons.Add(new OutfitComparison { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Ok, CreatedAt = DateTime.UtcNow });
            db.Checks.Add(new OutfitCheck { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Error, CreatedAt = DateTime.UtcNow });
            db.Checks.Add(new OutfitCheck { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Ok, CreatedAt = DateTime.UtcNow.AddHours(-25) });
        });
        me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.Equal(2, me.GetProperty("checksToday").GetInt32());

        Assert.Equal(AdminChange.Changed, await AdminSync.SetProAsync(_manual.ConnectionString, handle, DateTime.UtcNow.AddDays(31)));
        me = await Json(await client.GetAsync("/api/auth/me"));
        Assert.Equal("pro", me.GetProperty("plan").GetString());
        // Round 14: on Pro the day is two buckets, and this is the CHECK one — the comparison above moved to its own
        // allowance (Plans:ProComparesPerDay, the compare route), so it no longer eats a check. PlansTests locks it.
        Assert.Equal(1, me.GetProperty("checksToday").GetInt32());
        Assert.Equal(30, me.GetProperty("checksPerDay").GetInt32());

        // Verified is the row's flag, as --verify sets it.
        WithDb(_manual, db => db.Users.Single(u => u.Id == id).Verified = true);
        Assert.True((await Json(await client.GetAsync("/api/auth/me"))).GetProperty("verified").GetBoolean());
    }

    [Fact]
    public async Task Config_carries_the_plans_and_whether_billing_is_live()
    {
        var manual = (await _manual.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans");
        Assert.Equal(3, manual.GetProperty("freeChecksPerDay").GetInt32());
        Assert.Equal(30, manual.GetProperty("proChecksPerDay").GetInt32());
        Assert.Equal(1, manual.GetProperty("guestChecksPerDay").GetInt32());
        Assert.Equal("", manual.GetProperty("proPriceText").GetString());
        Assert.False(manual.GetProperty("compareNeedsPro").GetBoolean());
        Assert.False(manual.GetProperty("billing").GetBoolean());

        var stripe = (await _stripe.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans");
        Assert.True(stripe.GetProperty("billing").GetBoolean());

        // Round 20: the yearly price and the trial. Yearly is false without a yearly price id, whatever the amounts say;
        // the trial is the clamped setting and 0 by default.
        Assert.False(manual.GetProperty("yearly").GetBoolean());
        Assert.Equal(0m, manual.GetProperty("proYearlyPriceAmount").GetDecimal());
        Assert.Equal(0, manual.GetProperty("proTrialDays").GetInt32());
        Assert.False(stripe.GetProperty("yearly").GetBoolean());
        Assert.Empty(stripe.GetProperty("proYearlyPrices").EnumerateObject());
        var yearly = (await _yearly.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans");
        Assert.True(yearly.GetProperty("yearly").GetBoolean());
        Assert.Equal(290m, yearly.GetProperty("proYearlyPriceAmount").GetDecimal());
        Assert.Equal(290m, yearly.GetProperty("proYearlyPrices").GetProperty("ILS").GetDecimal());
        Assert.False(yearly.GetProperty("proYearlyPrices").TryGetProperty("EUR", out _));
        Assert.Equal(0, yearly.GetProperty("proTrialDays").GetInt32());
        using var trial = new TestApp { Settings = { ["Plans:ProTrialDays"] = "900" } };
        Assert.Equal(730, (await trial.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans").GetProperty("proTrialDays").GetInt32());
        using var amountOnly = new TestApp { BillingProvider = "stripe", StripeSecretKey = StripeBillingApp.SecretKey, StripePriceId = StripeBillingApp.PriceId, StripeWebhookSecret = StripeBillingApp.WebhookSecret, Settings = { ["Plans:ProYearlyPriceAmount"] = "290", ["Plans:ProPriceCurrency"] = "ILS" } };
        var amountOnlyPlans = (await amountOnly.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans");
        Assert.False(amountOnlyPlans.GetProperty("yearly").GetBoolean());
        Assert.Equal(290m, amountOnlyPlans.GetProperty("proYearlyPriceAmount").GetDecimal());

        // The Pro number published is what a Pro account really gets: Plans:ProChecksPerDay clamped to Limits:ChecksPerDay.
        using var clamped = new TestApp { ChecksPerDay = 12, FreeChecksPerDay = 3 };
        var clampedPlans = (await clamped.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans");
        Assert.Equal(12, clampedPlans.GetProperty("proChecksPerDay").GetInt32());
        Assert.Equal(3, clampedPlans.GetProperty("freeChecksPerDay").GetInt32());

        // The provider alone is not enough: without the three settings Stripe stays off.
        using var half = new TestApp { BillingProvider = "stripe", StripeSecretKey = "sk_test_only" };
        Assert.False((await half.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("plans").GetProperty("billing").GetBoolean());
        var (client, _, _) = await half.NewUserAsync("bill_half");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/billing/checkout", null)).StatusCode);
    }

    // ---- Round 20 — the wedge: replays, the yearly price, the no-card trial ----

    private static int EventRows(TestApp app, string id)
    {
        using var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().StripeEvents.Count(e => e.Id == id);
    }

    /// <summary>
    /// Stripe retries until it sees a 2xx and its dashboard can resend any event by hand; before Round 20 a resent
    /// checkout.session.completed stacked one more period each time. The event's own id is the key, not the session's.
    /// </summary>
    [Fact]
    public async Task Webhook_replays_of_one_event_are_ignored()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_replay");
        var first = await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_replay", "sub_replay", id: "evt_replay_1"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False((await Json(first)).TryGetProperty("replayed", out _));
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(35));

        var again = await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_replay", "sub_replay", id: "evt_replay_1"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.True((await Json(again)).GetProperty("replayed").GetBoolean());
        // A fresh signature timestamp is still the same event.
        var later = await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_replay", "sub_replay", id: "evt_replay_1"), timestamp: DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds());
        Assert.True((await Json(later)).GetProperty("replayed").GetBoolean());
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(35));

        WithDb(_stripe, db =>
        {
            var row = Assert.Single(db.StripeEvents.Where(e => e.Id == "evt_replay_1"));
            Assert.Equal("checkout.session.completed", row.Type);
            Assert.InRange(row.ReceivedAt, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(2));
        });

        // Another event id for the same session is another event: it stacks, as a second Checkout always did.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(id.ToString("N"), null, "cus_replay", "sub_replay", id: "evt_replay_2"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(70));

        // An id longer than the column is cut, and the cut id is still the same event next time.
        var longId = "evt_" + new string('y', 80);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, new { id = longId, type = "ping" })).StatusCode);
        Assert.Equal(1, EventRows(_stripe, longId[..64]));
        Assert.True((await Json(await PostEventAsync(_stripe, new { id = longId, type = "ping" }))).GetProperty("replayed").GetBoolean());
    }

    /// <summary>
    /// The id is recorded with the handler's work, so a handler that failed leaves no row and Stripe's retry is handled;
    /// an event with no id at all (a hand-made one) is handled every time and recorded never; and two deliveries of
    /// one id that arrive together are handled once, because the check-handle-record run is serialised.
    /// </summary>
    [Fact]
    public async Task Webhook_records_an_event_only_after_it_was_handled()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_noid");
        var bare = new
        {
            type = "checkout.session.completed",
            data = new { @object = new { id = "cs_bare", @object = "checkout.session", client_reference_id = id.ToString("N"), customer = "cus_bare" } }
        };
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, bare)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, bare)).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(70));
        WithDb(_stripe, db => Assert.DoesNotContain(db.StripeEvents.ToList(), e => e.Type == "checkout.session.completed" && e.Id.Length == 0));

        // A burst of the same event: one period, one row, and every delivery answered 200.
        var (_, burstId, _) = await _stripe.NewUserAsync("bill_burst");
        var burst = CheckoutCompleted(burstId.ToString("N"), null, "cus_burst", "sub_burst", id: "evt_burst");
        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostEventAsync(_stripe, burst)));
        Assert.All(answers, a => Assert.Equal(HttpStatusCode.OK, a.StatusCode));
        var replays = 0;
        foreach (var answer in answers)
        {
            if ((await Json(answer)).TryGetProperty("replayed", out _))
            {
                replays++;
            }
        }

        Assert.Equal(3, replays);
        AssertAround(UserOf(_stripe, burstId).ProUntil, DateTime.UtcNow.AddDays(35));
        Assert.Equal(1, EventRows(_stripe, "evt_burst"));
    }

    /// <summary>
    /// Review of Round 20: the event's row is written in the same save as the grant. It used to be a second save after
    /// it, so a delivery that died between the two left a grant with no record, and Stripe's retry granted it again.
    /// </summary>
    [Fact]
    public async Task Webhook_a_grant_and_its_event_row_are_saved_together()
    {
        await using var app = new FlakyStripeBillingApp();
        var (_, id, _) = await app.NewUserAsync("bill_atomic");
        var paid = CheckoutCompleted(id.ToString("N"), null, "cus_atomic", "sub_atomic", id: "evt_atomic");

        // The save that carries the event's row fails: nothing of the delivery stands, and Stripe will retry.
        app.Interceptor.FailNext = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await PostEventAsync(app, paid)).StatusCode);
        Assert.False(app.Interceptor.FailNext);
        var untouched = UserOf(app, id);
        Assert.Equal("free", untouched.Plan);
        Assert.Null(untouched.ProUntil);
        Assert.Equal(0, EventRows(app, "evt_atomic"));

        // The retry grants one period and records it; the next one is a replay.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(app, paid)).StatusCode);
        AssertAround(UserOf(app, id).ProUntil, DateTime.UtcNow.AddDays(35));
        Assert.Equal(1, EventRows(app, "evt_atomic"));
        Assert.True((await Json(await PostEventAsync(app, paid))).GetProperty("replayed").GetBoolean());
        AssertAround(UserOf(app, id).ProUntil, DateTime.UtcNow.AddDays(35));

        // An event no handler writes for still gets its row, in the save after the handler.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(app, new { id = "evt_atomic_ping", type = "ping" })).StatusCode);
        Assert.Equal(1, EventRows(app, "evt_atomic_ping"));
    }

    /// <summary>
    /// Review of Round 20: what the renewal recap reads off the events - when the followed subscription charges next,
    /// and whether it will: a Checkout's own period (a month, a year, a trial's days from now), Stripe's period once an
    /// event names it, and no renewal for a cancel at the period end (or a cancel_at by then), a trial with no card, a
    /// stopped collection; another subscription's event changes nothing, and a deleted one clears both.
    /// </summary>
    [Fact]
    public async Task Webhook_notes_when_the_followed_subscription_charges_and_whether_it_will()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_renews");
        var (_, yearId, _) = await _stripe.NewUserAsync("bill_renews_year");
        var (_, trialId, _) = await _stripe.NewUserAsync("bill_renews_trial");
        var before = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompletedWith(id.ToString("N"), "cus_renews", "sub_renews", "month", null, "paid"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompletedWith(yearId.ToString("N"), "cus_renews_y", "sub_renews_y", "year", null, "paid"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompletedWith(trialId.ToString("N"), "cus_renews_t", "sub_renews_t", "month", "7", "no_payment_required"))).StatusCode);
        var month = UserOf(_stripe, id);
        Assert.True(month.BillingRenews);
        AssertAround(month.BillingPeriodEnd, before.AddMonths(1));
        Assert.True(UserOf(_stripe, yearId).BillingRenews);
        AssertAround(UserOf(_stripe, yearId).BillingPeriodEnd, before.AddYears(1));
        Assert.False(UserOf(_stripe, trialId).BillingRenews);
        AssertAround(UserOf(_stripe, trialId).BillingPeriodEnd, before.AddDays(7));

        var periodEnd = DateTimeOffset.UtcNow.AddDays(29);
        object Updated(string status, object extra) => new
        {
            id = NewEventId(),
            type = "customer.subscription.updated",
            data = new { @object = Merge(new { id = "sub_renews", @object = "subscription", customer = "cus_renews", status, current_period_end = periodEnd.ToUnixTimeSeconds() }, extra) }
        };

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("active", new { cancel_at_period_end = true }))).StatusCode);
        var cancelling = UserOf(_stripe, id);
        Assert.False(cancelling.BillingRenews);
        AssertAround(cancelling.BillingPeriodEnd, periodEnd.UtcDateTime);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("active", new { cancel_at = periodEnd.AddDays(60).ToUnixTimeSeconds() }))).StatusCode);
        Assert.True(UserOf(_stripe, id).BillingRenews);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("active", new { cancel_at = periodEnd.ToUnixTimeSeconds() }))).StatusCode);
        Assert.False(UserOf(_stripe, id).BillingRenews);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("active", new { cancel_at_period_end = false }))).StatusCode);
        Assert.True(UserOf(_stripe, id).BillingRenews);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("trialing", new { default_payment_method = (string?)null }))).StatusCode);
        Assert.False(UserOf(_stripe, id).BillingRenews);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("trialing", new { default_payment_method = "pm_card" }))).StatusCode);
        Assert.True(UserOf(_stripe, id).BillingRenews);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("past_due", new { }))).StatusCode);
        Assert.False(UserOf(_stripe, id).BillingRenews);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("active", new { }))).StatusCode);
        Assert.True(UserOf(_stripe, id).BillingRenews);
        // A first payment still being confirmed says nothing either way: the flag stays as the last word left it.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("incomplete", new { }))).StatusCode);
        Assert.True(UserOf(_stripe, id).BillingRenews);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("unpaid", new { }))).StatusCode);
        Assert.False(UserOf(_stripe, id).BillingRenews);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated("active", new { }))).StatusCode);
        Assert.True(UserOf(_stripe, id).BillingRenews);

        // Another subscription on the same customer says nothing about the followed one.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_renews", "past_due", periodEnd.AddDays(5), id: "sub_stranger"))).StatusCode);
        Assert.True(UserOf(_stripe, id).BillingRenews);
        AssertAround(UserOf(_stripe, id).BillingPeriodEnd, periodEnd.UtcDateTime);

        // A paid invoice moves the next charge to the end of what it paid for, never back.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_renews", periodEnd: periodEnd.AddDays(30)))).StatusCode);
        AssertAround(UserOf(_stripe, id).BillingPeriodEnd, periodEnd.AddDays(30).UtcDateTime);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_renews", periodEnd: periodEnd))).StatusCode);
        AssertAround(UserOf(_stripe, id).BillingPeriodEnd, periodEnd.AddDays(30).UtcDateTime);

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionDeleted("cus_renews", "sub_renews"))).StatusCode);
        var ended = UserOf(_stripe, id);
        Assert.Null(ended.BillingRenews);
        Assert.Null(ended.BillingPeriodEnd);
    }

    /// <summary>
    /// Review of Round 21: Stripe promises no order and retries a failed delivery for days, and each subscription event is
    /// a snapshot of the moment it was created. A snapshot older than the one on the row moves neither the next charge
    /// nor the renewal: a card update retried after the renewal cannot put the charge back in the past (the recap would
    /// skip it), and a "renewing" that arrives after the cancel cannot promise a charge that will not happen. A newer
    /// one, or one of the same second, still does; and the deletion is the newest word, so a late update notes nothing.
    /// </summary>
    [Fact]
    public async Task A_stale_subscription_event_moves_neither_the_charge_nor_the_renewal()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_stale");
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompletedWith(id.ToString("N"), "cus_stale", "sub_stale", "month", null, "paid"))).StatusCode);
        var now = DateTimeOffset.UtcNow;
        object Updated(DateTimeOffset created, DateTimeOffset periodEnd, bool cancelling) => Merge(new
        {
            id = NewEventId(),
            type = "customer.subscription.updated",
            data = new
            {
                @object = new
                {
                    id = "sub_stale", @object = "subscription", customer = "cus_stale", status = "active",
                    current_period_end = periodEnd.ToUnixTimeSeconds(), cancel_at_period_end = cancelling
                }
            }
        }, new { created = created.ToUnixTimeSeconds() });

        // The renewal's own update (the period now ends in thirty days), then the card update from the day before it,
        // retried late: it still names the period that has just ended.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated(now.AddMinutes(-30), now.AddDays(30), false))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated(now.AddDays(-1), now.AddHours(-1), false))).StatusCode);
        AssertAround(UserOf(_stripe, id).BillingPeriodEnd, now.AddDays(30).UtcDateTime);
        Assert.True(UserOf(_stripe, id).BillingRenews);

        // The cancel (five minutes ago) arrives before the update it followed (ten minutes ago): the cancel stands.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated(now.AddMinutes(-5), now.AddDays(30), true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated(now.AddMinutes(-10), now.AddDays(30), false))).StatusCode);
        Assert.False(UserOf(_stripe, id).BillingRenews);
        // The same second is not older: the later delivery wins. And a newer event (the person resumed) moves both.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated(now.AddMinutes(-5), now.AddDays(30), false))).StatusCode);
        Assert.True(UserOf(_stripe, id).BillingRenews);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated(now.AddMinutes(-1), now.AddDays(31), true))).StatusCode);
        Assert.False(UserOf(_stripe, id).BillingRenews);
        AssertAround(UserOf(_stripe, id).BillingPeriodEnd, now.AddDays(31).UtcDateTime);

        // Deleted now; an update from before the deletion, delivered after it, promises nothing.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Merge(SubscriptionDeleted("cus_stale", "sub_stale"), new { created = now.ToUnixTimeSeconds() }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, Updated(now.AddMinutes(-2), now.AddDays(30), false))).StatusCode);
        Assert.Null(UserOf(_stripe, id).BillingRenews);
        Assert.Null(UserOf(_stripe, id).BillingPeriodEnd);
    }

    /// <summary>
    /// Review of Round 21: the first charge a Checkout promises is counted from when the Checkout completed (the event's
    /// own time), not from when the delivery landed: a completion Stripe could only deliver two days later still charges
    /// a month after the Checkout, and the recap must say that day, not two days after it.
    /// </summary>
    [Fact]
    public async Task A_late_checkout_counts_the_first_charge_from_the_checkout()
    {
        var (_, id, _) = await _stripe.NewUserAsync("bill_late_checkout");
        var completed = DateTimeOffset.UtcNow.AddDays(-2);
        var late = Merge(CheckoutCompletedWith(id.ToString("N"), "cus_late", "sub_late", "month", null, "paid"), new { created = completed.ToUnixTimeSeconds() });
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, late)).StatusCode);
        AssertAround(UserOf(_stripe, id).BillingPeriodEnd, completed.UtcDateTime.AddMonths(1));
        // Pro itself still runs from the delivery: paying late never costs the person days they paid for.
        Assert.True(UserOf(_stripe, id).ProUntil > DateTime.UtcNow.AddDays(34));
    }

    /// <summary>Two anonymous objects as one JSON object, the second's fields last.</summary>
    private static Dictionary<string, object?> Merge(object first, object second)
    {
        var merged = new Dictionary<string, object?>();
        foreach (var part in new[] { first, second })
        {
            foreach (var property in part.GetType().GetProperties())
            {
                merged[property.Name] = property.GetValue(part);
            }
        }

        return merged;
    }

    [Fact]
    public async Task Checkout_yearly_sends_the_yearly_price_and_a_month_stays_the_default()
    {
        var (client, id, _) = await _yearly.NewUserAsync("bill_year");
        _yearly.StripeHandler.Clear();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout?interval=year", null)).StatusCode);
        var year = Assert.Single(_yearly.StripeHandler.Requests);
        Assert.Equal(YearlyStripeBillingApp.YearlyPriceId, year["line_items[0][price]"]);
        Assert.Equal("year", year["metadata[interval]"]);
        Assert.Equal("ils", year["currency"]);
        Assert.Equal(id.ToString("N"), year["metadata[userId]"]);

        _yearly.StripeHandler.Clear();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout", null)).StatusCode);
        var month = Assert.Single(_yearly.StripeHandler.Requests);
        Assert.Equal(StripeBillingApp.PriceId, month["line_items[0][price]"]);
        Assert.Equal("month", month["metadata[interval]"]);

        _yearly.StripeHandler.Clear();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/billing/checkout?interval=month&currency=EUR", null)).StatusCode);
        var euro = Assert.Single(_yearly.StripeHandler.Requests);
        Assert.Equal(StripeBillingApp.PriceId, euro["line_items[0][price]"]);
        Assert.Equal("month", euro["metadata[interval]"]);
        Assert.Equal("eur", euro["currency"]);

        // Anything else is refused, and nothing reaches Stripe: a person who read one cadence is never sold another.
        _yearly.StripeHandler.Clear();
        var weekly = await client.PostAsync("/api/billing/checkout?interval=weekly", null);
        Assert.Equal(HttpStatusCode.BadRequest, weekly.StatusCode);
        Assert.Equal("Yearly billing isn't available here yet.", await ErrorOf(weekly));
        Assert.Empty(_yearly.StripeHandler.Requests);
    }

    [Fact]
    public async Task Checkout_yearly_is_refused_where_the_page_could_not_offer_it()
    {
        // No yearly price id: the page never showed a year, so a year is not for sale.
        var (plain, _, _) = await _stripe.NewUserAsync("bill_year_none");
        _stripe.StripeHandler.Clear();
        var refused = await plain.PostAsync("/api/billing/checkout?interval=year", null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("Yearly billing isn't available here yet.", await ErrorOf(refused));
        Assert.Empty(_stripe.StripeHandler.Requests);

        // A yearly id, but the quoted currency has a monthly price only: refused rather than swapped for the fallback.
        var (client, _, _) = await _yearly.NewUserAsync("bill_year_eur", language: "he");
        _yearly.StripeHandler.Clear();
        var euro = await client.PostAsync("/api/billing/checkout?interval=year&currency=EUR", null);
        Assert.Equal(HttpStatusCode.BadRequest, euro.StatusCode);
        Assert.Equal("חיוב שנתי עדיין לא זמין כאן.", await ErrorOf(euro));
        Assert.Empty(_yearly.StripeHandler.Requests);

        // With a euro yearly price too, the form carries the currency and the yearly price.
        await using var both = new TestApp
        {
            BillingProvider = "stripe", StripeSecretKey = StripeBillingApp.SecretKey, StripePriceId = StripeBillingApp.PriceId, StripeWebhookSecret = StripeBillingApp.WebhookSecret,
            StripeYearlyPriceId = YearlyStripeBillingApp.YearlyPriceId,
            Settings =
            {
                ["Plans:ProPriceAmount"] = "29", ["Plans:ProPriceCurrency"] = "ILS", ["Plans:ProPrices:EUR"] = "9.90",
                ["Plans:ProYearlyPriceAmount"] = "290", ["Plans:ProYearlyPrices:EUR"] = "99"
            }
        };
        var (twice, _, _) = await both.NewUserAsync("bill_year_both");
        Assert.Equal(HttpStatusCode.OK, (await twice.PostAsync("/api/billing/checkout?interval=year&currency=eur", null)).StatusCode);
        var request = Assert.Single(both.StripeHandler.Requests);
        Assert.Equal("eur", request["currency"]);
        Assert.Equal(YearlyStripeBillingApp.YearlyPriceId, request["line_items[0][price]"]);
        Assert.Equal("year", request["metadata[interval]"]);
    }

    [Fact]
    public async Task Webhook_checkout_completed_grants_a_year_for_a_yearly_session()
    {
        var (_, yearId, _) = await _stripe.NewUserAsync("bill_grant_year");
        var (_, monthId, _) = await _stripe.NewUserAsync("bill_grant_month");
        var (_, bareId, _) = await _stripe.NewUserAsync("bill_grant_bare");
        var (_, giftedId, _) = await _stripe.NewUserAsync("bill_grant_gifted");
        WithDb(_stripe, db => { var g = db.Users.Single(u => u.Id == giftedId); g.Plan = "pro"; g.ProUntil = DateTime.UtcNow.AddDays(100); });

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompletedWith(yearId.ToString("N"), "cus_gy", "sub_gy", "year", null, "paid"))).StatusCode);
        var year = UserOf(_stripe, yearId);
        Assert.Equal("pro", year.Plan);
        AssertAround(year.ProUntil, DateTime.UtcNow.AddDays(368));
        Assert.Equal("sub_gy", year.BillingSubscriptionId);

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompletedWith(monthId.ToString("N"), "cus_gm", "sub_gm", "month", null, "paid"))).StatusCode);
        AssertAround(UserOf(_stripe, monthId).ProUntil, DateTime.UtcNow.AddDays(35));

        // No interval at all (a hand-made session, or one from before this round): a month, as before.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompleted(bareId.ToString("N"), null, "cus_gb", "sub_gb"))).StatusCode);
        AssertAround(UserOf(_stripe, bareId).ProUntil, DateTime.UtcNow.AddDays(35));

        // On top of a running --pro grant the year is added to its end; paying never cuts what is there.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompletedWith(giftedId.ToString("N"), "cus_gg", "sub_gg", "year", null, "paid"))).StatusCode);
        AssertAround(UserOf(_stripe, giftedId).ProUntil, DateTime.UtcNow.AddDays(468));
    }

    /// <summary>
    /// The trial is Checkout's and the eligibility is the app's: once per account, to one that has never been a Stripe
    /// customer here. Stripe would happily trial the same person again; the stored customer id is what stops it.
    /// </summary>
    [Fact]
    public async Task Checkout_offers_the_trial_once_and_only_without_a_customer()
    {
        await using var app = new StripeBillingApp { Settings = { ["Plans:ProTrialDays"] = "7" } };
        var (fresh, freshId, _) = await app.NewUserAsync("bill_trial_fresh");
        var (known, knownId, _) = await app.NewUserAsync("bill_trial_known");
        WithDb(app, db => db.Users.Single(u => u.Id == knownId).BillingCustomerId = "cus_known");

        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsync("/api/billing/checkout", null)).StatusCode);
        var trial = Assert.Single(app.StripeHandler.Requests);
        Assert.Equal("7", trial["subscription_data[trial_period_days]"]);
        Assert.Equal("if_required", trial["payment_method_collection"]);
        Assert.Equal("cancel", trial["subscription_data[trial_settings][end_behavior][missing_payment_method]"]);
        Assert.Equal("7", trial["metadata[trialDays]"]);
        Assert.Equal(freshId.ToString("N"), trial["metadata[userId]"]);

        app.StripeHandler.Clear();
        Assert.Equal(HttpStatusCode.OK, (await known.PostAsync("/api/billing/checkout", null)).StatusCode);
        var paid = Assert.Single(app.StripeHandler.Requests);
        Assert.Equal("cus_known", paid["customer"]);
        Assert.Null(paid["subscription_data[trial_period_days]"]);
        Assert.Null(paid["payment_method_collection"]);
        Assert.Null(paid["subscription_data[trial_settings][end_behavior][missing_payment_method]"]);
        Assert.Null(paid["metadata[trialDays]"]);

        // The state route says what the page may promise: 7 for the fresh account, 0 for the known customer.
        Assert.Equal(7, (await Json(await fresh.GetAsync("/api/billing/state"))).GetProperty("trialDays").GetInt32());
        Assert.Equal(0, (await Json(await known.GetAsync("/api/billing/state"))).GetProperty("trialDays").GetInt32());

        // Without the setting there is never a trial field, and the manual provider never publishes one.
        var (plain, _, _) = await _stripe.NewUserAsync("bill_trial_off");
        _stripe.StripeHandler.Clear();
        Assert.Equal(HttpStatusCode.OK, (await plain.PostAsync("/api/billing/checkout", null)).StatusCode);
        Assert.Null(Assert.Single(_stripe.StripeHandler.Requests)["subscription_data[trial_period_days]"]);
        Assert.Equal(0, (await Json(await plain.GetAsync("/api/billing/state"))).GetProperty("trialDays").GetInt32());
        await using var manualTrial = new TestApp { Settings = { ["Plans:ProTrialDays"] = "7" } };
        var (manual, _, _) = await manualTrial.NewUserAsync("bill_trial_manual");
        Assert.Equal(0, (await Json(await manual.GetAsync("/api/billing/state"))).GetProperty("trialDays").GetInt32());
    }

    /// <summary>
    /// The guard on the whole trial: a session that asked for no card must grant the trial's days, never the 35 days a
    /// payment earns, or a trial would hand a month of Pro to anyone with an email address.
    /// </summary>
    [Fact]
    public async Task Webhook_a_no_card_trial_grants_the_trial_days_not_a_paid_period()
    {
        const string address = "trial@example.test";
        var (client, id, _) = await _stripe.NewUserAsync("bill_trial_grant");
        WithDb(_stripe, db => { var u = db.Users.Single(x => x.Id == id); u.Email = address; u.EmailVerifiedAt = DateTime.UtcNow; });

        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompletedWith(id.ToString("N"), "cus_trial", "sub_trial", "month", "7", "no_payment_required"))).StatusCode);
        var user = UserOf(_stripe, id);
        Assert.Equal("pro", user.Plan);
        AssertAround(user.ProUntil, DateTime.UtcNow.AddDays(10));
        Assert.Equal("cus_trial", user.BillingCustomerId);
        Assert.Equal("sub_trial", user.BillingSubscriptionId);
        Assert.Equal("pro", (await Json(await client.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());
        // And having been a customer once, the account is never offered a second trial.
        Assert.Equal(0, (await Json(await client.GetAsync("/api/billing/state"))).GetProperty("trialDays").GetInt32());

        // The same session paid for: a month. (Another account, so nothing stacks on the trial above.)
        var (_, paidId, _) = await _stripe.NewUserAsync("bill_trial_paid");
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, CheckoutCompletedWith(paidId.ToString("N"), "cus_trial_paid", "sub_trial_paid", "month", "7", "paid"))).StatusCode);
        AssertAround(UserOf(_stripe, paidId).ProUntil, DateTime.UtcNow.AddDays(35));

        // Stripe's own trialing event names the trial end: the end date lands on it plus the slack (the Round 11 branch).
        var trialEnd = DateTimeOffset.UtcNow.AddDays(9);
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionUpdated("cus_trial", "trialing", trialEnd, id: "sub_trial"))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(12));

        // A trial that ends with no card is cancelled by Stripe: Pro ends now, and the Round 17 letter goes.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, SubscriptionDeleted("cus_trial", "sub_trial"))).StatusCode);
        Assert.True(UserOf(_stripe, id).ProUntil <= DateTime.UtcNow.AddSeconds(1));
        Assert.Equal("free", (await Json(await client.GetAsync("/api/auth/me"))).GetProperty("plan").GetString());
        var ended = Assert.Single(_stripe.Email.To(address));
        Assert.Contains("ended", ended.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task State_carries_yearly_and_the_trial()
    {
        var (yearly, _, _) = await _yearly.NewUserAsync("bill_state_year");
        var state = await Json(await yearly.GetAsync("/api/billing/state"));
        Assert.True(state.GetProperty("yearly").GetBoolean());
        Assert.Equal(0, state.GetProperty("trialDays").GetInt32());
        Assert.True(state.GetProperty("billing").GetBoolean());

        var (stripe, _, _) = await _stripe.NewUserAsync("bill_state_stripe");
        Assert.False((await Json(await stripe.GetAsync("/api/billing/state"))).GetProperty("yearly").GetBoolean());

        var (manual, _, _) = await _manual.NewUserAsync("bill_state_manual");
        var off = await Json(await manual.GetAsync("/api/billing/state"));
        Assert.False(off.GetProperty("yearly").GetBoolean());
        Assert.Equal(0, off.GetProperty("trialDays").GetInt32());
    }
}
