using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Endpoints;

/// <summary>
/// Plans and billing: the Pro state, Stripe Checkout and its webhook. Pro is a flag plus an end date on the account
/// (<see cref="AppUser.Plan"/>, <see cref="AppUser.ProUntil"/>); Checkout starts a subscription, the webhook moves the
/// end date, and the <c>--pro</c> command (Program.cs, Data/AdminSync.cs) does the same by hand when Stripe is off.
/// <para>
/// Round 11: <c>POST /api/billing/portal</c> (session) opens a Stripe Billing Portal session for the account's
/// <see cref="AppUser.BillingCustomerId"/> (a form-encoded POST to <c>v1/billing_portal/sessions</c> with <c>customer</c>
/// and <c>return_url</c> = origin + <c>/#/settings</c>, through <see cref="StripeClient"/>'s named client, so the test
/// recorder sees it) and answers 200 <see cref="PortalDto"/>; the client sends the person there in the same tab. Errors:
/// error.portal_unavailable (404) while the provider is manual or the account has no customer id (a Pro granted by
/// <c>--pro</c> has nothing on Stripe's side; the Pro page and the settings row show billing.manual_hint instead of the
/// button); error.portal_failed (502) when Stripe does not answer with a url. Cancelling happens on Stripe's page, never
/// here: the webhook is what ends Pro, and it now tells subscriptions apart by <see cref="AppUser.BillingSubscriptionId"/>
/// (see <see cref="WebhookAsync"/>).
/// </para>
/// </summary>
public static class BillingEndpoints
{
    public const string WebhookPath = "/api/billing/webhook";

    /// <summary>Where a Billing Portal session posts (the test recorder answers it with <c>PortalResponse</c>).</summary>
    public const string PortalSessionsPath = "v1/billing_portal/sessions";

    /// <summary>Where the portal sends the person back: the settings screen, where the plan row is.</summary>
    public const string PortalReturnPath = "/#/settings";

    /// <summary>The column's length (AppDbContext); a longer id from an event is cut, and a cut id still compares equal to itself next time.</summary>
    public const int SubscriptionIdMaxLength = 64;

    /// <summary>
    /// What a completed Checkout grants: 35 days, not a month. Stripe bills every calendar month and the events can lag
    /// by hours, so a few days of slack keeps a paying person from dropping to free on a slow renewal. From then on the
    /// end date follows the period Stripe names (the invoice's lines, the subscription's current period) plus
    /// <see cref="RenewalSlack"/>, never the previous end date, so nothing accrues; a missed renewal still lapses.
    /// </summary>
    public static readonly TimeSpan PaidPeriod = TimeSpan.FromDays(35);

    /// <summary>Added to the period end Stripe names, and all that is left once collection has stopped (past due, unpaid, paused).</summary>
    public static readonly TimeSpan RenewalSlack = TimeSpan.FromDays(3);

    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing");
        group.MapGet("/state", StateAsync).RequireAuthorization();
        group.MapPost("/checkout", CheckoutAsync).RequireAuthorization();
        // Round 11: the Billing Portal (change the card, cancel), a link to Stripe's page.
        group.MapPost("/portal", PortalAsync).RequireAuthorization();
        // Anonymous, and exempt from the CSRF header in Program.cs: Stripe cannot send it. The signature is the guard.
        group.MapPost("/webhook", WebhookAsync);
        return app;
    }

    /// <summary>
    /// A Billing Portal session for the signed-in account: 200 { url } to send the person to, 404 error.portal_unavailable
    /// while Stripe is off or the account never went through Checkout (nothing to manage on Stripe's side: a --pro grant,
    /// or a free account), 502 error.portal_failed when Stripe did not answer with a page. Nothing is written here: what
    /// the person does on the portal comes back through the webhook.
    /// </summary>
    private static async Task<IResult> PortalAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IOptions<BillingOptions> billing, StripeClient stripe, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        if (!billing.Value.StripeEnabled || string.IsNullOrWhiteSpace(user.BillingCustomerId))
        {
            return UserEndpoints.Error(StatusCodes.Status404NotFound, localizer.Get(user.PreferredLanguage, "error.portal_unavailable"));
        }

        var url = await stripe.CreatePortalSessionAsync(user.Id, user.BillingCustomerId, Origin(context.Request, billing.Value) + PortalReturnPath, ct);
        if (url is null)
        {
            return UserEndpoints.Error(StatusCodes.Status502BadGateway, localizer.Get(user.PreferredLanguage, "error.portal_failed"));
        }

        return Results.Json(new PortalDto(url), AppJson.Options);
    }

    /// <summary>The plan as the client should read it: "pro" only while the paid period runs, and the end date only then.</summary>
    public static (string Plan, DateTime? ProUntil) EffectivePlan(AppUser user, DateTime now)
    {
        if (!Plans.IsPro(user, now))
        {
            return (Plans.Free, null);
        }

        return (Plans.Pro, user.ProUntil is { } until ? DateTime.SpecifyKind(until, DateTimeKind.Utc) : null);
    }

    private static async Task<IResult> StateAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IOptions<PlanOptions> plans, IOptions<BillingOptions> billing, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        var (plan, proUntil) = EffectivePlan(user, DateTime.UtcNow);
        return Results.Json(new BillingStateDto(plan, proUntil, billing.Value.StripeEnabled, plans.Value.ProPriceText), AppJson.Options);
    }

    private static async Task<IResult> CheckoutAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IOptions<BillingOptions> billing, StripeClient stripe, CancellationToken ct)
    {
        var (user, failure) = await UserEndpoints.RequireUserAsync(context, db, localizer, ct);
        if (user is null)
        {
            return failure!;
        }

        if (!billing.Value.StripeEnabled)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.billing_disabled"));
        }

        // One subscription per account. A tab that still says "Go Pro" after the webhook flipped the plan must not open a
        // second one on the same customer: Stripe would take it, both would bill, and cancelling either would end Pro here
        // while the other keeps charging.
        if (Plans.IsPro(user, DateTime.UtcNow))
        {
            return UserEndpoints.Error(StatusCodes.Status409Conflict, localizer.Get(user.PreferredLanguage, "error.already_pro"));
        }

        var origin = Origin(context.Request, billing.Value);
        var request = new CheckoutSessionRequest(
            user.Id,
            user.BillingCustomerId,
            // Only an address the person confirmed: a typo'd one would follow them onto the receipt.
            user.EmailVerifiedAt is not null ? user.Email : null,
            $"{origin}/#/pro?checkout=success",
            $"{origin}/#/pro?checkout=cancel");
        var url = await stripe.CreateCheckoutSessionAsync(request, ct);
        if (url is null)
        {
            return UserEndpoints.Error(StatusCodes.Status502BadGateway, localizer.Get(user.PreferredLanguage, "error.billing_failed"));
        }

        return Results.Json(new CheckoutDto(url), AppJson.Options);
    }

    /// <summary>
    /// Stripe's events. Nothing is stored per event and no event id is checked: every update here is monotonic enough
    /// for a pilot (a repeated checkout.session.completed stacks one more period, a repeated invoice.paid or
    /// customer.subscription.updated names the same period end and changes nothing, a repeated subscription.deleted ends
    /// what already ended), and Stripe retries until it sees a 2xx, so a handler that cannot find the account still
    /// answers 200 rather than asking for the same event again. A bad signature is 400 so the dashboard shows it.
    /// <para>
    /// Round 11: the account remembers which subscription it paid for (<see cref="AppUser.BillingSubscriptionId"/>).
    /// checkout.session.completed stores the session's subscription (the one the person just paid for; a different one
    /// already there is replaced and logged); customer.subscription.created and .updated fill it in only while it is
    /// empty (an account from before this round, or one that subscribed again on Stripe's side), and an updated event for
    /// another subscription on the same customer is logged and ignored; customer.subscription.deleted ends Pro only for
    /// the stored subscription (or, with none stored, for the customer as before) and clears the id so a later Checkout
    /// starts clean. So a stale tab's second subscription, or a re-subscribe on Stripe's side, can no longer end the
    /// Pro the paid one still bills for.
    /// </para>
    /// </summary>
    private static async Task<IResult> WebhookAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IOptions<BillingOptions> billing, ILogger<StripeClient> logger, CancellationToken ct)
    {
        var language = Localizer.Resolve(null, context.Request);
        string body;
        using (var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(ct);
        }

        var secret = billing.Value.StripeWebhookSecret;
        if (!billing.Value.StripeEnabled || !StripeClient.VerifySignature(context.Request.Headers[StripeClient.SignatureHeader], body, secret, DateTimeOffset.UtcNow))
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.billing_signature"));
        }

        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(language, "error.billing_signature"));
        }

        using (json)
        {
            var root = json.RootElement;
            var type = root.TryGetProperty("type", out var typeProperty) && typeProperty.ValueKind == JsonValueKind.String ? typeProperty.GetString() ?? "" : "";
            var payload = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("object", out var obj) && obj.ValueKind == JsonValueKind.Object ? obj : default;
            var now = DateTime.UtcNow;

            switch (type)
            {
                case "checkout.session.completed":
                {
                    var user = await FindByReferenceAsync(db, payload, ct);
                    if (user is null)
                    {
                        logger.LogWarning("Stripe checkout.session.completed names no account we have (client_reference_id/metadata.userId); ignored.");
                        break;
                    }

                    // On top of a period still running (a --pro grant, or a stale tab's Checkout the 409 above did not
                    // catch): paying never cuts what the account already had.
                    user.Plan = Plans.Pro;
                    user.ProUntil = Later(user.ProUntil, now) + PaidPeriod;
                    var customer = StringOrId(payload, "customer");
                    if (!string.IsNullOrWhiteSpace(customer))
                    {
                        user.BillingCustomerId = customer;
                    }

                    // The subscription this Checkout paid for: the one the events below answer to from now on. A different id
                    // already there (a second Checkout on the same account) is replaced, since this is the one just paid for.
                    var subscription = SubscriptionId(payload, "subscription");
                    if (subscription is not null)
                    {
                        if (user.BillingSubscriptionId is not null && user.BillingSubscriptionId != subscription)
                        {
                            logger.LogWarning("Account {Handle} paid for subscription {Subscription} while {Previous} was on record; the new one is followed now.",
                                user.Handle, subscription, user.BillingSubscriptionId);
                        }

                        user.BillingSubscriptionId = subscription;
                    }

                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Account {Handle} is Pro until {Until:u} (checkout completed).", user.Handle, user.ProUntil);
                    break;
                }

                case "customer.subscription.created":
                {
                    // Nothing is granted here (the checkout event does that): the account only learns its subscription id when
                    // it has none, so a subscription that started on Stripe's side, or one from before this round, is followed too.
                    var user = await FindByCustomerAsync(db, payload, logger, ct);
                    if (user is null || SubscriptionId(payload, "id") is not { } created)
                    {
                        break;
                    }

                    if (user.BillingSubscriptionId is null)
                    {
                        user.BillingSubscriptionId = created;
                        await db.SaveChangesAsync(ct);
                        logger.LogInformation("Account {Handle} follows subscription {Subscription} (created).", user.Handle, created);
                    }
                    else if (user.BillingSubscriptionId != created)
                    {
                        logger.LogWarning("Stripe created subscription {Subscription} for account {Handle}, which already follows {Current}; ignored.",
                            created, user.Handle, user.BillingSubscriptionId);
                    }

                    break;
                }

                case "invoice.paid":
                {
                    // The first invoice of a subscription is the checkout above; extending on it as well would give the
                    // first period twice. Renewals (subscription_cycle) and anything without a reason extend.
                    if (StringOrId(payload, "billing_reason") == "subscription_create")
                    {
                        break;
                    }

                    var user = await FindByCustomerAsync(db, payload, logger, ct);
                    if (user is null)
                    {
                        break;
                    }

                    // Paid through the end of the period the invoice's lines name, plus slack; an invoice naming none is
                    // worth a period from now. Never counted from the previous end date: a renewal every ~30 days would
                    // otherwise run ahead by the difference each time. Never shorter than what is there.
                    var paidUntil = PeriodEnd(payload) is { } periodEnd ? periodEnd + RenewalSlack : now + PaidPeriod;
                    user.Plan = Plans.Pro;
                    user.ProUntil = Later(user.ProUntil, paidUntil);
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Account {Handle} is Pro until {Until:u} (invoice paid).", user.Handle, user.ProUntil);
                    break;
                }

                case "customer.subscription.updated":
                {
                    var user = await FindByCustomerAsync(db, payload, logger, ct);
                    if (user is null)
                    {
                        break;
                    }

                    // Only the subscription the account follows moves the end date; another on the same customer is logged and
                    // ignored. An account that follows none yet (from before Round 11) adopts this one and keeps the customer-only
                    // matching it had.
                    if (!FollowsOrAdopts(user, payload, logger, "updated"))
                    {
                        break;
                    }

                    // An adopted id is kept whatever the status below does to the end date.
                    if (db.ChangeTracker.HasChanges())
                    {
                        await db.SaveChangesAsync(ct);
                    }

                    var status = StringOrId(payload, "status");
                    if (status is "active" or "trialing")
                    {
                        // Paid through the subscription's current period, plus slack; never shorter than what is there.
                        if (CurrentPeriodEnd(payload) is not { } periodEnd)
                        {
                            break;
                        }

                        var until = periodEnd + RenewalSlack;
                        if (user.ProUntil is { } running && running >= until)
                        {
                            break;
                        }

                        user.Plan = Plans.Pro;
                        user.ProUntil = until;
                        await db.SaveChangesAsync(ct);
                        logger.LogInformation("Account {Handle} is Pro until {Until:u} (subscription {Status}).", user.Handle, user.ProUntil, status);
                    }
                    else if (status is "past_due" or "unpaid" or "paused")
                    {
                        // Collection stopped without the subscription ending (Stripe's dunning can leave it like this for
                        // good): what is left is the slack, not an end date that was granted on the promise of a payment.
                        var cutoff = now + RenewalSlack;
                        if (!Plans.IsPro(user, now) || (user.ProUntil is { } remaining && remaining <= cutoff))
                        {
                            break;
                        }

                        user.ProUntil = cutoff;
                        await db.SaveChangesAsync(ct);
                        logger.LogInformation("Account {Handle} is Pro until {Until:u} (subscription {Status}).", user.Handle, user.ProUntil, status);
                    }

                    break;
                }

                case "customer.subscription.deleted":
                {
                    var user = await FindByCustomerAsync(db, payload, logger, ct);
                    if (user is null)
                    {
                        break;
                    }

                    // A deleted subscription that is not the one the account follows must not end Pro: the followed one still
                    // bills. With none followed (an account from before Round 11) the customer match is all there is, as before.
                    var deleted = SubscriptionId(payload, "id");
                    if (user.BillingSubscriptionId is not null && deleted is not null && user.BillingSubscriptionId != deleted)
                    {
                        logger.LogWarning("Stripe deleted subscription {Subscription} for account {Handle}, which follows {Current}; Pro stays.",
                            deleted, user.Handle, user.BillingSubscriptionId);
                        break;
                    }

                    // The end date moves to now rather than the plan to free: the row still says a subscription existed. The
                    // id is cleared so the next Checkout starts clean.
                    user.ProUntil = now;
                    user.BillingSubscriptionId = null;
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Account {Handle} left Pro (subscription deleted).", user.Handle);
                    break;
                }

                default:
                    // Not ours to handle; 200 so Stripe stops sending it.
                    break;
            }
        }

        return Results.Json(new { received = true }, AppJson.Options);
    }

    /// <summary>The account a Checkout Session was opened for: client_reference_id first, metadata.userId as the fallback.</summary>
    private static async Task<AppUser?> FindByReferenceAsync(AppDbContext db, JsonElement payload, CancellationToken ct)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var reference = StringOrId(payload, "client_reference_id");
        if (string.IsNullOrWhiteSpace(reference) && payload.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            reference = StringOrId(metadata, "userId");
        }

        return Guid.TryParse(reference, out var userId) ? await db.Users.FindAsync([userId], ct) : null;
    }

    private static async Task<AppUser?> FindByCustomerAsync(AppDbContext db, JsonElement payload, ILogger logger, CancellationToken ct)
    {
        var customer = payload.ValueKind == JsonValueKind.Object ? StringOrId(payload, "customer") : null;
        if (string.IsNullOrWhiteSpace(customer))
        {
            logger.LogWarning("Stripe event carries no customer; ignored.");
            return null;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.BillingCustomerId == customer, ct);
        if (user is null)
        {
            logger.LogWarning("Stripe event for customer {Customer} matches no account; ignored.", customer);
        }

        return user;
    }

    /// <summary>
    /// Whether an updated subscription event is about the subscription the account follows. True when the ids match, or
    /// when the account follows none yet and adopts this one (set on the row, saved by the caller); false, logged, for
    /// another subscription on the same customer. An event without an id is taken as the followed one (a hand-made event).
    /// </summary>
    private static bool FollowsOrAdopts(AppUser user, JsonElement payload, ILogger logger, string what)
    {
        var id = SubscriptionId(payload, "id");
        if (id is null || user.BillingSubscriptionId == id)
        {
            return true;
        }

        if (user.BillingSubscriptionId is null)
        {
            user.BillingSubscriptionId = id;
            logger.LogInformation("Account {Handle} follows subscription {Subscription} ({What}).", user.Handle, id, what);
            return true;
        }

        logger.LogWarning("Stripe {What} subscription {Subscription} for account {Handle}, which follows {Current}; ignored.",
            what, id, user.Handle, user.BillingSubscriptionId);
        return false;
    }

    /// <summary>A subscription id field (a string, or an expanded object's id), trimmed and cut to the column's length; null when missing or blank.</summary>
    private static string? SubscriptionId(JsonElement obj, string name)
    {
        var id = StringOrId(obj, name)?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return id.Length <= SubscriptionIdMaxLength ? id : id[..SubscriptionIdMaxLength];
    }

    /// <summary>The later of the end date on the row (null for never Pro) and a candidate.</summary>
    private static DateTime Later(DateTime? current, DateTime candidate) =>
        current is { } value && value > candidate ? value : candidate;

    /// <summary>The end of the latest period an invoice's lines name (lines.data[*].period.end), or null without one.</summary>
    private static DateTime? PeriodEnd(JsonElement invoice)
    {
        DateTime? end = null;
        foreach (var line in ListItems(invoice, "lines"))
        {
            if (line.TryGetProperty("period", out var period) && UnixTime(period, "end") is { } lineEnd && (end is null || lineEnd > end))
            {
                end = lineEnd;
            }
        }

        return end;
    }

    /// <summary>
    /// A subscription's current_period_end: on the subscription itself up to Stripe's 2025-02 API versions, on each of its
    /// items since (the latest counts). Null when the event carries neither.
    /// </summary>
    private static DateTime? CurrentPeriodEnd(JsonElement subscription)
    {
        var end = UnixTime(subscription, "current_period_end");
        foreach (var item in ListItems(subscription, "items"))
        {
            if (UnixTime(item, "current_period_end") is { } itemEnd && (end is null || itemEnd > end))
            {
                end = itemEnd;
            }
        }

        return end;
    }

    /// <summary>The objects of a Stripe list field (<c>{ "object": "list", "data": [...] }</c>); none when the field is missing or not a list.</summary>
    private static IEnumerable<JsonElement> ListItems(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Object
            || !list.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                yield return item;
            }
        }
    }

    /// <summary>A Unix-seconds field as a UTC time, or null when it is missing, not a number, or not a time at all.</summary>
    private static DateTime? UnixTime(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var seconds) || seconds < 0 || seconds > 253402300799)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }

    /// <summary>A string field, or the id of an expanded object in its place (Stripe expands "customer" on request); null off a non-object.</summary>
    private static string? StringOrId(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Object when value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String => id.GetString(),
            _ => null
        };
    }

    /// <summary>Where Checkout and the portal return to: Billing:PublicOrigin when set (behind a tunnel or a proxy), else this request's origin.</summary>
    private static string Origin(HttpRequest request, BillingOptions options)
    {
        var configured = options.PublicOrigin.Trim().TrimEnd('/');
        return configured.Length > 0 ? configured : $"{request.Scheme}://{request.Host}";
    }
}
