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
/// <para>
/// Round 13 — money: the webhook also reads <c>charge.refunded</c> and <c>charge.dispute.created</c>, which it used to
/// ignore. Either one ends Pro on the matching customer's account (the end date moves to now, as for a deleted
/// subscription), writes a warning, and raises a <see cref="Services.Alerter"/> alert, because a dispute has a deadline
/// and a fee and the owner has to answer it. The endpoint must be subscribed to those two events in Stripe for them to
/// arrive, and since Round 17 <c>--stripe-check</c> requires them: it used to print green on an endpoint that would
/// never hear about a chargeback. Round 17 also stopped a PARTIAL refund from ending Pro — see
/// <see cref="FullyRefunded"/>, and the reason there.
/// </para>
/// </summary>
public static class BillingEndpoints
{
    public const string WebhookPath = "/api/billing/webhook";

    /// <summary>Round 20: the one value <c>POST /api/billing/checkout?return=</c> accepts; the Pro page reads it back off the return URL.</summary>
    public const string ReturnCompare = "compare";

    /// <summary>Where a Billing Portal session posts (the test recorder answers it with <c>PortalResponse</c>).</summary>
    public const string PortalSessionsPath = "v1/billing_portal/sessions";

    /// <summary>Round 13: where a subscription is ended when the account that owns it is deleted.</summary>
    public const string SubscriptionsPath = "v1/subscriptions";

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

    /// <summary>
    /// Round 20: what a completed Checkout for the yearly price grants: a year plus <see cref="RenewalSlack"/>, for the
    /// same reason <see cref="PaidPeriod"/> is 35 days and not a month. From then on the end date follows the invoices.
    /// </summary>
    public static readonly TimeSpan PaidYear = TimeSpan.FromDays(368);

    /// <summary>Round 20: the StripeEvents column's length; a longer event id is cut, and the cut id still compares equal to itself next time.</summary>
    public const int EventIdMaxLength = 64;

    /// <summary>
    /// Round 20: how long a handled event's id is kept. Stripe retries a delivery for three days at most, so a month
    /// covers every retry and every replay from its dashboard that anyone would make; older rows are pruned by the
    /// renewal recap's hourly run, and the monotonic rules below are the second line of defence past that.
    /// </summary>
    public static readonly TimeSpan StripeEventKeep = TimeSpan.FromDays(30);

    /// <summary>
    /// Round 20: one delivery at a time. Stripe can post the same event twice within a second, and the replay check
    /// reads the table before the handler and records after it, so without this two deliveries of one event could
    /// both find nothing and both do the work. A single server with one SQLite file has one process, so a process-wide
    /// gate is exactly the serialisation needed, and webhook traffic is a few events a day.
    /// </summary>
    private static readonly SemaphoreSlim WebhookGate = new(1, 1);

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
        return Results.Json(new BillingStateDto(plan, proUntil, billing.Value.StripeEnabled, plans.Value.ProPriceText,
            billing.Value.YearlyEnabled, TrialDaysFor(user, plans.Value, billing.Value)), AppJson.Options);
    }

    /// <summary>
    /// Round 20: how many free days Checkout would open for this account: Plans:ProTrialDays (clamped to Stripe's range)
    /// while Stripe is live and the account has never been a Stripe customer here. The customer id is what the first
    /// completed Checkout stores, so a second subscription on the same account never trials again; Stripe itself does
    /// not stop re-trials, and a --pro grant leaves nothing on Stripe's side, so a gifted person can still trial once.
    /// </summary>
    public static int TrialDaysFor(AppUser user, PlanOptions plans, BillingOptions billing)
    {
        var days = Math.Clamp(plans.ProTrialDays, 0, 730);
        return billing.StripeEnabled && days > 0 && string.IsNullOrWhiteSpace(user.BillingCustomerId) ? days : 0;
    }

    /// <summary>
    /// Round 17. The currency to charge in: the one the Pro page quoted this person, if the server really has a price
    /// in it, and the server's own fallback otherwise. It comes from the browser because the browser is the only thing
    /// that knows where the reader is (the page picks it with Intl from the reader's region) — but it is never trusted:
    /// a currency outside <see cref="PlanOptions.PriceTable"/> is a currency no page ever showed, and it is dropped.
    /// This is what ties the number on the screen to the number on the card.
    /// </summary>
    private static string QuotedCurrency(HttpRequest request, PlanOptions plans)
    {
        var table = plans.PriceTable();
        var asked = (request.Query["currency"].ToString() ?? "").Trim().ToUpperInvariant();
        if (asked.Length == 3 && table.ContainsKey(asked))
        {
            return asked;
        }

        var fallback = plans.FallbackCurrency();
        return fallback.Length == 3 && table.ContainsKey(fallback) ? fallback : "";
    }

    /// <summary>
    /// Round 20: the currency of a yearly Checkout, against <see cref="PlanOptions.YearlyPriceTable"/>, or null when
    /// the year cannot be sold in it. Stricter than the monthly rule on purpose: a quoted currency the yearly table lacks
    /// is not swapped for the fallback, because the page never showed a year in that currency and the person who asked
    /// for one must be refused rather than charged a different number.
    /// </summary>
    private static string? YearlyCurrency(HttpRequest request, PlanOptions plans)
    {
        var table = plans.YearlyPriceTable();
        var asked = (request.Query["currency"].ToString() ?? "").Trim().ToUpperInvariant();
        if (asked.Length == 3)
        {
            return table.ContainsKey(asked) ? asked : null;
        }

        var fallback = plans.FallbackCurrency();
        return fallback.Length == 3 && table.ContainsKey(fallback) ? fallback : null;
    }

    private static async Task<IResult> CheckoutAsync(
        HttpContext context, AppDbContext db, Localizer localizer, IOptions<BillingOptions> billing,
        IOptions<PlanOptions> plans, StripeClient stripe, CancellationToken ct)
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

        // Round 20: ?interval=year buys the yearly price, and only where the page could have offered it (a yearly price
        // id AND a yearly amount in the quoted currency). Anything else that is not a month is refused, never quietly
        // sold as a month: a person who read "a year" must not be charged a month.
        var interval = (context.Request.Query["interval"].ToString() ?? "").Trim().ToLowerInvariant();
        string currency;
        if (interval is "" or "month")
        {
            interval = "month";
            currency = QuotedCurrency(context.Request, plans.Value);
        }
        else if (interval == "year" && billing.Value.YearlyEnabled && YearlyCurrency(context.Request, plans.Value) is { } yearly)
        {
            currency = yearly;
        }
        else
        {
            return UserEndpoints.Error(StatusCodes.Status400BadRequest, localizer.Get(user.PreferredLanguage, "error.billing_interval"));
        }

        var origin = Origin(context.Request, billing.Value);
        // Round 20: ?return=compare asks Checkout to land the person back where they came from - the "which one?" screen,
        // with the camera ready - once the plan has flipped. An allowlist of one: anything else in ?return is dropped
        // silently, so the URLs Stripe is handed can only ever be these two shapes.
        var returnTo = string.Equals(context.Request.Query["return"].ToString(), ReturnCompare, StringComparison.Ordinal) ? "&return=" + ReturnCompare : "";
        var request = new CheckoutSessionRequest(
            user.Id,
            user.BillingCustomerId,
            // Only an address the person confirmed: a typo'd one would follow them onto the receipt.
            user.EmailVerifiedAt is not null ? user.Email : null,
            $"{origin}/#/pro?checkout=success{returnTo}",
            $"{origin}/#/pro?checkout=cancel{returnTo}",
            currency,
            interval,
            // Decided here, never refused: an account that is not eligible simply pays from the first day.
            TrialDaysFor(user, plans.Value, billing.Value));
        var url = await stripe.CreateCheckoutSessionAsync(request, ct);
        if (url is null)
        {
            return UserEndpoints.Error(StatusCodes.Status502BadGateway, localizer.Get(user.PreferredLanguage, "error.billing_failed"));
        }

        return Results.Json(new CheckoutDto(url), AppJson.Options);
    }

    /// <summary>
    /// Stripe's events. Round 20: a delivery is told from a replay by the event's own id (<see cref="StripeEvent"/>):
    /// an id already on record answers 200 <c>{ received, replayed }</c> and does nothing, so a retried or replayed
    /// checkout.session.completed no longer stacks one more period. The id is written in the same SaveChanges as the
    /// handler's own work (review of Round 20: it used to be a second save after it, so a delivery that died between
    /// the two granted twice on Stripe's retry), so a handler that threw before saving (500) leaves no row and Stripe's
    /// retry is handled, not ignored, and a grant is never on record without its id. Past the gate the work runs to
    /// its end whatever the connection does: a delivery Stripe gave up on is then on record, and its retry is answered
    /// as a replay. The two deliveries of one event that could both find no row are serialised by
    /// <see cref="WebhookGate"/>. Rows older than
    /// <see cref="StripeEventKeep"/> are pruned, and past that the monotonic rules stay as the second line of defence
    /// (a repeated invoice.paid or customer.subscription.updated names the same period end and changes nothing, a
    /// repeated subscription.deleted ends what already ended). An event without an id (a hand-made one) is handled every
    /// time, as before. Stripe retries until it sees a 2xx, so a handler that cannot find the account still answers
    /// 200 rather than asking for the same event again. A bad signature is 400 so the dashboard shows it.
    /// <para>
    /// Round 20 also reads what the Checkout Session sold from its metadata: interval=year grants <see cref="PaidYear"/>,
    /// and a session with payment_status no_payment_required and metadata.trialDays grants the trial's days plus the
    /// slack rather than a paid period, because a trial that asked for no card must not hand out a month of Pro.
    /// </para>
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
        HttpContext context, AppDbContext db, Localizer localizer, IOptions<BillingOptions> billing, ILogger<StripeClient> logger,
        Alerter alerter, IEmailSender email, CancellationToken ct)
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
            var eventId = SubscriptionId(root, "id");

            await WebhookGate.WaitAsync(ct);
            try
            {
                if (eventId is not null && await db.StripeEvents.AnyAsync(e => e.Id == eventId, ct))
                {
                    logger.LogInformation("Stripe event {Id} ({Type}) was already handled; ignored.", eventId, type);
                    return Results.Json(new { received = true, replayed = true }, AppJson.Options);
                }

                if (eventId is not null)
                {
                    db.StripeEvents.Add(new StripeEvent
                    {
                        Id = eventId,
                        Type = type.Length <= EventIdMaxLength ? type : type[..EventIdMaxLength],
                        ReceivedAt = DateTime.UtcNow
                    });
                }

                await HandleAsync(type, payload, db, localizer, billing.Value, logger, alerter, email, context.Request, CancellationToken.None);
                await SaveTheRestAsync(db, eventId, type, logger);
            }
            finally
            {
                WebhookGate.Release();
            }
        }

        return Results.Json(new { received = true }, AppJson.Options);
    }

    /// <summary>
    /// What is still unsaved once the handler returns: the event's row when the handler wrote nothing of its own (an
    /// event for nobody, one it ignores), and anything a handler left for here. A handler that saved took the row with
    /// its work. A duplicate key (another process, or a hand-made pair) is swallowed: the work it stands for was done,
    /// and Stripe gets its 200.
    /// </summary>
    private static async Task SaveTheRestAsync(AppDbContext db, string? eventId, string type, ILogger logger)
    {
        if (!db.ChangeTracker.HasChanges())
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException) when (eventId is not null)
        {
            logger.LogInformation("Stripe event {Id} ({Type}) was recorded by another delivery meanwhile.", eventId, type);
        }
    }

    /// <summary>The event handlers proper, one case per event the endpoint is subscribed to.</summary>
    private static async Task HandleAsync(
        string type, JsonElement payload, AppDbContext db, Localizer localizer, BillingOptions billing, ILogger logger,
        Alerter alerter, IEmailSender email, HttpRequest request, CancellationToken ct)
    {
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
                // catch): paying never cuts what the account already had. Round 20: what is added is what the
                // session sold - a year for the yearly price, and for a no-card trial the trial's days and the slack,
                // never a paid period; a session without metadata (hand-made, or from before) is a month, as before.
                user.Plan = Plans.Pro;
                user.ProUntil = Later(user.ProUntil, now) + GrantedBy(payload);
                // Review of Round 20: what the renewal recap reads. The first charge is one period from now whatever was
                // under the grant, and a trial that asked for no card has nothing to charge; the subscription's own events
                // correct both (the account usually learns its customer id only here, so the earlier ones found nobody).
                user.BillingRenews = TrialDays(payload) == 0;
                user.BillingPeriodEnd = FirstPeriodEnd(payload, now);
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

                if (user.BillingSubscriptionId is not null && user.BillingSubscriptionId != created)
                {
                    logger.LogWarning("Stripe created subscription {Subscription} for account {Handle}, which already follows {Current}; ignored.",
                        created, user.Handle, user.BillingSubscriptionId);
                    break;
                }

                if (user.BillingSubscriptionId is null)
                {
                    user.BillingSubscriptionId = created;
                    logger.LogInformation("Account {Handle} follows subscription {Subscription} (created).", user.Handle, created);
                }

                // Review of Round 20: the followed subscription's period and whether it will charge, for the recap.
                NoteRenewal(user, payload);
                await db.SaveChangesAsync(ct);
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
                var named = PeriodEnd(payload);
                var paidUntil = named is { } periodEnd ? periodEnd + RenewalSlack : now + PaidPeriod;
                user.Plan = Plans.Pro;
                user.ProUntil = Later(user.ProUntil, paidUntil);
                // The next charge is the end of the period just paid for (review of Round 20); never moved back by a late one.
                if (named is { } paidThrough)
                {
                    user.BillingPeriodEnd = Later(user.BillingPeriodEnd, paidThrough);
                }

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

                // Review of Round 20: the period and whether it will charge, for the recap; saved with the end date below,
                // or, where the status leaves the end date alone, by the webhook after the handler, like an adopted id.
                NoteRenewal(user, payload);

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
                    // Round 17: and TELL them. Until now a declined card moved a paying person to three days from
                    // Pro in silence — they would simply find the app smaller one morning, having done nothing
                    // wrong and been asked for nothing. A card expires; that is not a decision to cancel, and
                    // treating it as one loses a subscriber who wanted to stay.
                    await TellAboutTheCardAsync(email, localizer, billing, request, user, cutoff, logger, ct);
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
                var wasPro = Plans.IsPro(user, now);
                user.ProUntil = now;
                user.BillingSubscriptionId = null;
                user.BillingRenews = null;
                user.BillingPeriodEnd = null;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Account {Handle} left Pro (subscription deleted).", user.Handle);
                // Round 17: only when they actually had it to lose. Stripe deletes a subscription for a cancellation
                // they asked for AND for one that quietly ran out of retries, and the two feel identical from the
                // inside - the app simply gets smaller. A line saying so, and that the wardrobe and the looks are
                // still there, is the difference between a lapse and a loss.
                if (wasPro)
                {
                    await TellProEndedAsync(email, localizer, billing, request, user, logger, ct);
                }

                break;
            }

            // ---- Round 13 — money: a payment that went backwards. Stripe sends these on the charge, not the
            // subscription, and nothing here used to read them: a refunded or disputed month left the account Pro.
            // Both end Pro now, log it, and alert the owner, who has to decide what to do about the person. ----
            case "charge.refunded":
            case "charge.dispute.created":
            {
                var user = await FindByCustomerAsync(db, payload, logger, ct);
                if (user is null)
                {
                    break;
                }

                var reversal = type == "charge.refunded" ? "refunded" : "disputed";

                // Round 17. charge.refunded fires for ANY refund, and most refunds are not the whole month: a
                // goodwill five shekels back, a proration, a duplicate line. Taking Pro away for one of those is
                // the worst outcome available - the person keeps being billed by Stripe (a refund does not cancel
                // a subscription) and loses what they are paying for. So only a charge refunded IN FULL ends Pro.
                // A partial one still reaches the owner, because a refund he did not issue is worth knowing about.
                // The Charge carries both answers (amount, amount_refunded, refunded); a dispute has neither and
                // is always the whole charge.
                var partial = type == "charge.refunded" && !FullyRefunded(payload);
                if (partial)
                {
                    logger.LogWarning("Account {Handle}: a Stripe charge was refunded in part. Pro was left alone.", user.Handle);
                    await alerter.RaiseAsync(Alerter.Kind.BillingReversed,
                        $"part of a Stripe charge was refunded for one account ({type}). Pro was NOT removed, because the charge was not refunded in full and the subscription is still billing. Check the Stripe dashboard if this was not you.", ct);
                    break;
                }

                if (!Plans.IsPro(user, now))
                {
                    logger.LogInformation("Stripe {Type} for account {Handle}, which is not Pro; nothing to remove.", type, user.Handle);
                    break;
                }

                // Same shape as a deleted subscription: the end date moves to now rather than the plan to free, so
                // the row still says a subscription existed. The subscription id is left alone — a dispute does not
                // cancel the subscription, and Stripe will send customer.subscription.deleted if it ends too.
                user.ProUntil = now;
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Account {Handle} left Pro: a charge was {Reversal} ({Type}).", user.Handle, reversal, type);
                await alerter.RaiseAsync(Alerter.Kind.BillingReversed,
                    $"a Stripe charge was {reversal} ({type}) and Pro was removed from one account. Check the Stripe dashboard: a dispute has a deadline and a fee.", ct);
                break;
            }

            default:
                // Not ours to handle; 200 so Stripe stops sending it.
                break;
        }
    }

    /// <summary>
    /// Round 20: the period a completed Checkout grants, read from the session. A no-card trial (payment_status
    /// no_payment_required with a trialDays the app wrote into the metadata) is worth its days plus the slack; a
    /// session sold as a year is worth <see cref="PaidYear"/>; everything else, a month as before.
    /// </summary>
    private static TimeSpan GrantedBy(JsonElement session) =>
        TrialDays(session) is > 0 and var days ? TimeSpan.FromDays(days) + RenewalSlack : SoldAYear(session) ? PaidYear : PaidPeriod;

    /// <summary>
    /// Review of Round 20: when a completed Checkout's subscription charges first: the trial's end, or one calendar
    /// month or year from now, which is where Stripe anchors a subscription opened without a billing anchor.
    /// </summary>
    private static DateTime FirstPeriodEnd(JsonElement session, DateTime now) =>
        TrialDays(session) is > 0 and var days ? now.AddDays(days) : SoldAYear(session) ? now.AddYears(1) : now.AddMonths(1);

    /// <summary>The days of a no-card trial the session opened (payment_status no_payment_required and metadata.trialDays, capped at 730), or 0.</summary>
    private static int TrialDays(JsonElement session)
    {
        var trialDays = int.TryParse(StringOrId(Metadata(session), "trialDays"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var days) ? days : 0;
        return StringOrId(session, "payment_status") == "no_payment_required" && trialDays > 0 ? Math.Min(trialDays, 730) : 0;
    }

    private static bool SoldAYear(JsonElement session) => StringOrId(Metadata(session), "interval") == "year";

    private static JsonElement Metadata(JsonElement session) =>
        session.ValueKind == JsonValueKind.Object && session.TryGetProperty("metadata", out var block) && block.ValueKind == JsonValueKind.Object ? block : default;

    /// <summary>
    /// Review of Round 20: what the renewal recap needs from an event about the followed subscription. The period end
    /// when the event names one, and whether the subscription will charge at it: active or trialing, not set to cancel
    /// by then (cancel_at_period_end, or a cancel_at no later than the period end), and, while it trials, with a card on
    /// it; past due, unpaid, paused or anything else will not. A card kept only on the customer is not in the event, so
    /// such a trial reads as not renewing: the safe side, where the recap stays quiet rather than promise a renewal.
    /// "incomplete" (a first payment still being confirmed) says nothing either way and leaves the flag as it was: the
    /// event that follows it does, and Stripe does not promise the order they arrive in.
    /// </summary>
    private static void NoteRenewal(AppUser user, JsonElement subscription)
    {
        var end = CurrentPeriodEnd(subscription);
        if (end is not null)
        {
            user.BillingPeriodEnd = end;
        }

        if (WillRenew(subscription, end) is { } renews)
        {
            user.BillingRenews = renews;
        }
    }

    private static bool? WillRenew(JsonElement subscription, DateTime? periodEnd)
    {
        var status = StringOrId(subscription, "status");
        if (status == "incomplete")
        {
            return null;
        }

        if (status is not ("active" or "trialing"))
        {
            return false;
        }

        if (subscription.TryGetProperty("cancel_at_period_end", out var cancelling) && cancelling.ValueKind == JsonValueKind.True)
        {
            return false;
        }

        if (UnixTime(subscription, "cancel_at") is { } cancelAt && (periodEnd is null || cancelAt <= periodEnd))
        {
            return false;
        }

        return status == "active"
            || !string.IsNullOrWhiteSpace(StringOrId(subscription, "default_payment_method"))
            || !string.IsNullOrWhiteSpace(StringOrId(subscription, "default_source"));
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

    /// <summary>
    /// Round 17 — the letter a declined card earns. Sent only to an address the person confirmed: an unverified one
    /// is a typo as often as it is a mailbox, and billing news must not go to a stranger. A failure to send is logged
    /// and swallowed: Stripe must still get its 200, or it retries the event and the work above runs twice.
    /// </summary>
    private static async Task TellAboutTheCardAsync(
        IEmailSender email, Localizer localizer, BillingOptions billing, HttpRequest request, AppUser user,
        DateTime until, ILogger logger, CancellationToken ct)
    {
        if (!email.Enabled || user.EmailVerifiedAt is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        try
        {
            // Settings is where the "Manage subscription" button lives, which is the portal, which is where a card is
            // changed. Linking straight at Stripe is impossible: a portal session is minted per person, on demand.
            var link = Origin(request, billing) + PortalReturnPath;
            var language = user.PreferredLanguage;
            await email.SendAsync(new EmailMessage(
                user.Email!,
                localizer.Get(language, "email.billing_problem_subject"),
                localizer.Get(language, "email.billing_problem_body", user.Handle, Localizer.Day(until, language), link)), ct);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not tell account {Handle} that their card was declined.", user.Handle);
        }
    }

    /// <summary>Round 17 — Pro has ended and the person should hear it from the app, not notice it. Same rules as above.</summary>
    private static async Task TellProEndedAsync(
        IEmailSender email, Localizer localizer, BillingOptions billing, HttpRequest request, AppUser user,
        ILogger logger, CancellationToken ct)
    {
        if (!email.Enabled || user.EmailVerifiedAt is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        try
        {
            var link = Origin(request, billing) + "/#/pro";
            var language = user.PreferredLanguage;
            await email.SendAsync(new EmailMessage(
                user.Email!,
                localizer.Get(language, "email.billing_ended_subject"),
                localizer.Get(language, "email.billing_ended_body", user.Handle, link)), ct);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not tell account {Handle} that Pro ended.", user.Handle);
        }
    }

    /// <summary>
    /// Round 17. Was this charge refunded in full? Stripe answers twice and the two can disagree while a refund is
    /// still settling, so both have to say yes: <c>refunded</c> is the flag Stripe sets when nothing is left, and
    /// <c>amount_refunded</c> against <c>amount</c> is the arithmetic behind it. A payload that carries neither is
    /// not evidence of a full refund and is treated as partial - the safe direction, because the cost of being wrong
    /// here is taking Pro from somebody who is still paying for it.
    /// </summary>
    private static bool FullyRefunded(JsonElement charge)
    {
        if (charge.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var flag = charge.TryGetProperty("refunded", out var refunded) && refunded.ValueKind == JsonValueKind.True;
        var amount = Amount(charge, "amount");
        var back = Amount(charge, "amount_refunded");
        var arithmetic = amount is { } total && total > 0 && back is { } given && given >= total;
        return flag && arithmetic;
    }

    /// <summary>A minor-units amount from the payload, or null when it is missing or not a whole non-negative number.</summary>
    private static long? Amount(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var minor) || minor < 0)
        {
            return null;
        }

        return minor;
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
