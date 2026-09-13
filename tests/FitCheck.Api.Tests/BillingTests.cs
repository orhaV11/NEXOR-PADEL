using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;

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
/// Plans and billing: the state route for free and Pro, Checkout refused without Stripe (and for an account that is Pro
/// already) and the exact request that goes out with it, the webhook (signature, the four events, unknown ones) and its
/// CSRF exemption, the --pro command through AdminSync, the plan fields on "me", and the plans block on /api/config.
/// </summary>
public class BillingTests : IClassFixture<ManualBillingApp>, IClassFixture<StripeBillingApp>
{
    private readonly ManualBillingApp _manual;
    private readonly StripeBillingApp _stripe;

    public BillingTests(ManualBillingApp manual, StripeBillingApp stripe)
    {
        _manual = manual;
        _stripe = stripe;
        _manual.Vision.Handler = _ => Payloads.Ok();
        _stripe.Vision.Handler = _ => Payloads.Ok();
    }

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

    private static object CheckoutCompleted(string? reference, string? metadataUserId, string? customer) => new
    {
        id = "evt_checkout",
        type = "checkout.session.completed",
        data = new { @object = new { id = "cs_1", @object = "checkout.session", client_reference_id = reference, customer, metadata = new { userId = metadataUserId } } }
    };

    /// <summary>A renewal invoice as Stripe posts it: the customer, the reason and, with <paramref name="periodEnd"/>, one line naming the paid period.</summary>
    private static object InvoicePaid(string customer, string billingReason = "subscription_cycle", DateTimeOffset? periodEnd = null) => new
    {
        id = "evt_invoice",
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
    private static object SubscriptionUpdated(string customer, string status, DateTimeOffset? periodEnd = null, bool onItems = false) => new
    {
        id = "evt_sub_updated",
        type = "customer.subscription.updated",
        data = new
        {
            @object = new
            {
                id = "sub_1", @object = "subscription", customer, status,
                current_period_end = periodEnd is { } end && !onItems ? end.ToUnixTimeSeconds() : (long?)null,
                items = new
                {
                    @object = "list",
                    data = new object[] { new { id = "si_1", @object = "subscription_item", current_period_end = periodEnd is { } itemEnd && onItems ? itemEnd.ToUnixTimeSeconds() : (long?)null } }
                }
            }
        }
    };

    private static object SubscriptionDeleted(string customer) => new
    {
        id = "evt_sub",
        type = "customer.subscription.deleted",
        data = new { @object = new { id = "sub_1", @object = "subscription", customer, status = "canceled" } }
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
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_renew", periodEnd: periodEnd))).StatusCode);
        AssertAround(UserOf(_stripe, id).ProUntil, DateTime.UtcNow.AddDays(33));

        // The same event again (Stripe retries until it sees a 2xx) names the same period: nothing stacks.
        Assert.Equal(HttpStatusCode.OK, (await PostEventAsync(_stripe, InvoicePaid("cus_renew", periodEnd: periodEnd))).StatusCode);
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
        Assert.Equal(2, me.GetProperty("checksToday").GetInt32());
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
}
