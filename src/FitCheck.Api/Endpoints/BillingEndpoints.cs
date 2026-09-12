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
/// </summary>
public static class BillingEndpoints
{
    public const string WebhookPath = "/api/billing/webhook";

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
        // Anonymous, and exempt from the CSRF header in Program.cs: Stripe cannot send it. The signature is the guard.
        group.MapPost("/webhook", WebhookAsync);
        return app;
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

                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("Account {Handle} is Pro until {Until:u} (checkout completed).", user.Handle, user.ProUntil);
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

                    // The end date moves to now rather than the plan to free: the row still says a subscription existed.
                    user.ProUntil = now;
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

    /// <summary>Where Checkout returns to: Billing:PublicOrigin when set (behind a tunnel or a proxy), else this request's origin.</summary>
    private static string Origin(HttpRequest request, BillingOptions options)
    {
        var configured = options.PublicOrigin.Trim().TrimEnd('/');
        return configured.Length > 0 ? configured : $"{request.Scheme}://{request.Host}";
    }
}
