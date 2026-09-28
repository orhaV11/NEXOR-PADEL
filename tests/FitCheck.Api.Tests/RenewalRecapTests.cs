using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 20 — the pre-renewal recap: three days before a paying Pro's charge, one mail with three numbers computed
/// from the database and the way to the portal; once per period, transactional (it ignores the digest switch), retried
/// on a failed send, and silent with mail off or without a public origin - while the same run always prunes the
/// handled Stripe events older than a month.
/// <para>The clock is the fake one, parked, so "six days before the end date" is one instant a test can name.</para>
/// </summary>
public class RenewalRecapTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);

    private const string Origin = "https://looks.test";

    private static TestApp NewApp(bool emailEnabled = true, bool withOrigin = true)
    {
        var app = new TestApp
        {
            EmailEnabled = emailEnabled,
            Settings = withOrigin ? new() { ["Email:PublicOrigin"] = Origin } : new()
        };
        app.Clock.Now = Now;
        return app;
    }

    private static RenewalRecap Worker(TestApp app) => app.Services.GetRequiredService<RenewalRecap>();

    private static async Task WithDbAsync(TestApp app, Func<AppDbContext, Task> action)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
        await db.SaveChangesAsync();
    }

    private static async Task<AppUser> UserOf(TestApp app, Guid id)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == id);
    }

    /// <summary>
    /// A paying subscriber: Pro with a subscription and a customer on Stripe's side, a confirmed address, the charge
    /// three days out (the period Stripe named, and it will renew) and the end date three days after it (the slack).
    /// </summary>
    private static async Task<Guid> SubscriberAsync(TestApp app, string handle, string address, Action<AppUser>? shape = null)
    {
        var (_, id, _) = await app.NewUserAsync(handle);
        await WithDbAsync(app, async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == id);
            user.Plan = Plans.Pro;
            user.ProUntil = Now.AddDays(6);
            user.BillingPeriodEnd = Now.AddDays(3);
            user.BillingRenews = true;
            user.BillingCustomerId = "cus_" + handle;
            user.BillingSubscriptionId = "sub_" + handle;
            user.Email = address;
            user.EmailVerifiedAt = Now.AddDays(-40);
            user.DigestOn = true;
            shape?.Invoke(user);
        });
        return id;
    }

    private static string Feedback(string tip) => JsonSerializer.Serialize(new OutfitFeedback { OneTip = tip }, AppJson.Options);

    // ---- Review of Round 20: the recap driven by Stripe's own events, through the real webhook ----

    /// <summary>A Stripe app with mail on and a public origin; its clock stays real until a test parks it, because the webhook reads the real one.</summary>
    private static TestApp StripeApp() => new()
    {
        BillingProvider = "stripe",
        StripeSecretKey = StripeBillingApp.SecretKey,
        StripePriceId = StripeBillingApp.PriceId,
        StripeWebhookSecret = StripeBillingApp.WebhookSecret,
        Settings = new() { ["Email:PublicOrigin"] = Origin }
    };

    /// <summary>An account with a confirmed address, in a language.</summary>
    private static async Task<Guid> PersonAsync(TestApp app, string handle, string address, string language = "en")
    {
        var (_, id, _) = await app.NewUserAsync(handle);
        await WithDbAsync(app, async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == id);
            user.Email = address;
            user.EmailVerifiedAt = DateTime.UtcNow.AddDays(-1);
            user.PreferredLanguage = language;
        });
        return id;
    }

    /// <summary>Posts one signed event to the real webhook, the way Stripe does, and expects its 200.</summary>
    private static async Task PostEventAsync(TestApp app, object payload)
    {
        var body = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, BillingEndpoints.WebhookPath) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation(StripeClient.SignatureHeader,
            StripeClient.SignatureHeaderValue(StripeBillingApp.WebhookSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), body));
        Assert.Equal(HttpStatusCode.OK, (await app.BareClient().SendAsync(request)).StatusCode);
    }

    private static string EventId() => "evt_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>A completed Checkout as the app opens it: monthly and paid, or a no-card trial.</summary>
    private static object Checkout(Guid id, string customer, string subscription, string? trialDays = null) => new
    {
        id = EventId(),
        type = "checkout.session.completed",
        data = new
        {
            @object = new
            {
                id = "cs_" + subscription, @object = "checkout.session", client_reference_id = id.ToString("N"), customer, subscription,
                payment_status = trialDays is null ? "paid" : "no_payment_required",
                metadata = new { userId = id.ToString("N"), interval = "month", trialDays }
            }
        }
    };

    /// <summary>customer.subscription.updated with what the recap reads: the status, the period end, a cancel at the period end, a card.</summary>
    private static object Updated(string customer, string subscription, string status, DateTime? periodEnd, bool cancelAtPeriodEnd = false, string? card = null) => new
    {
        id = EventId(),
        type = "customer.subscription.updated",
        data = new
        {
            @object = new
            {
                id = subscription, @object = "subscription", customer, status,
                current_period_end = periodEnd is { } end ? new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Utc)).ToUnixTimeSeconds() : (long?)null,
                cancel_at_period_end = cancelAtPeriodEnd,
                default_payment_method = card
            }
        }
    };

    private static List<EmailMessage> Recaps(TestApp app, string address) =>
        app.Email.To(address).Where(m => m.Subject.StartsWith("Your OREVOSH Pro renews", StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task Three_days_before_the_charge_one_mail_goes_with_the_three_numbers_and_the_portal_path()
    {
        await using var app = NewApp();
        const string address = "recap@example.test";
        var id = await SubscriberAsync(app, "recap_one", address);
        await WithDbAsync(app, async db =>
        {
            var inWindow = Now.AddDays(-10);
            // Two comparisons decided (and one that failed, and one from before the window: neither counts).
            db.Comparisons.Add(new OutfitComparison { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Ok, CreatedAt = inWindow });
            db.Comparisons.Add(new OutfitComparison { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Ok, CreatedAt = inWindow.AddDays(1) });
            db.Comparisons.Add(new OutfitComparison { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Error, CreatedAt = inWindow });
            db.Comparisons.Add(new OutfitComparison { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Ok, CreatedAt = Now.AddDays(-45) });
            // Three planned, two worn: one by the "it worked" answer, one by the check made in it (a real row: the column is a key).
            var wornIn = new OutfitCheck { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Ok, CreatedAt = inWindow, FeedbackJson = Feedback("Roll the sleeves once.") };
            db.Checks.Add(wornIn);
            db.Suggestions.Add(new OutfitSuggestion { Id = Guid.NewGuid(), UserId = id, Status = CheckStatus.Ok, CreatedAt = inWindow, ForDate = DateOnly.FromDateTime(inWindow), WornCheckId = wornIn.Id });
            db.Suggestions.Add(new OutfitSuggestion { Id = Guid.NewGuid(), UserId = id, Status = CheckStatus.Ok, CreatedAt = inWindow, ForDate = DateOnly.FromDateTime(inWindow), UsefulReason = TipReason.Worked });
            db.Suggestions.Add(new OutfitSuggestion { Id = Guid.NewGuid(), UserId = id, Status = CheckStatus.Ok, CreatedAt = inWindow, ForDate = DateOnly.FromDateTime(inWindow) });
            db.Suggestions.Add(new OutfitSuggestion { Id = Guid.NewGuid(), UserId = id, Status = CheckStatus.Error, CreatedAt = inWindow, ForDate = DateOnly.FromDateTime(inWindow), UsefulReason = TipReason.Worked });
            // Two kept pieces: the brown boots were kept BEFORE the check whose tip names them; the scarf was kept after.
            db.WardrobeItems.Add(new WardrobeItem { Id = Guid.NewGuid(), UserId = id, Name = "Brown boots", NameKey = "brown boots", Category = "shoes", CreatedAt = inWindow.AddDays(-2), LastSeenAt = inWindow });
            db.WardrobeItems.Add(new WardrobeItem { Id = Guid.NewGuid(), UserId = id, Name = "Red scarf", NameKey = "red scarf", Category = "accessory", CreatedAt = inWindow.AddDays(2), LastSeenAt = inWindow });
            db.Checks.Add(new OutfitCheck { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Ok, CreatedAt = inWindow, FeedbackJson = Feedback("Swap the sneakers for the Brown Boots you already own.") });
            db.Checks.Add(new OutfitCheck { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Ok, CreatedAt = inWindow, FeedbackJson = Feedback("Add the red scarf for colour.") });
        });

        // The numbers on their own, without a mail.
        await WithDbAsync(app, async db =>
        {
            var numbers = await RenewalRecap.NumbersAsync(db, id, Now, CancellationToken.None);
            Assert.Equal(new RenewalRecap.Numbers(2, 3, 2, 1), numbers);
        });

        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(1, run.Sent);
        Assert.Equal(0, run.Skipped);

        var mail = Assert.Single(app.Email.To(address));
        // The charge is the period's end Stripe named: the 11th, three days before the end date on the 14th.
        Assert.Equal("Your OREVOSH Pro renews on 11 October", mail.Subject);
        Assert.Contains("@recap_one", mail.Body, StringComparison.Ordinal);
        Assert.Contains("renews on 11 October", mail.Body, StringComparison.Ordinal);
        // Each count after its noun, so a one reads as well as a two (it used to say "1 tips").
        Assert.Contains("Comparisons decided: 2.", mail.Body, StringComparison.Ordinal);
        Assert.Contains("Outfits planned: 3, worn: 2.", mail.Body, StringComparison.Ordinal);
        Assert.Contains("Tips that named something you already own: 1.", mail.Body, StringComparison.Ordinal);
        Assert.Contains(Origin + BillingEndpoints.PortalReturnPath, mail.Body, StringComparison.Ordinal);
        // Transactional: no unsubscribe link, no "digest" anything.
        Assert.DoesNotContain("/digest/off/", mail.Body, StringComparison.Ordinal);

        var user = await UserOf(app, id);
        Assert.Equal(user.BillingPeriodEnd, user.RenewalRecapUntil);
    }

    [Fact]
    public async Task Once_per_period_and_again_after_a_renewal()
    {
        await using var app = NewApp();
        const string address = "again@example.test";
        var id = await SubscriberAsync(app, "recap_again", address);

        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Single(app.Email.To(address));

        // The renewal moves the period and the end date a month on (what invoice.paid does); five days before the next
        // charge is too early.
        var charge = Now.AddDays(33);
        await WithDbAsync(app, async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == id);
            user.BillingPeriodEnd = charge;
            user.ProUntil = charge + BillingEndpoints.RenewalSlack;
        });
        app.Clock.Now = charge.AddDays(-5);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        // Three days before it, the next period's mail goes, and only once.
        app.Clock.Now = charge.AddDays(-3);
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Equal(2, app.Email.To(address).Count);
        Assert.Equal(charge, (await UserOf(app, id)).RenewalRecapUntil);
    }

    [Fact]
    public async Task Who_is_left_out()
    {
        await using var app = NewApp();
        await SubscriberAsync(app, "recap_gift", "gift@example.test", u => u.BillingSubscriptionId = null);
        await SubscriberAsync(app, "recap_typo", "typo@example.test", u => u.EmailVerifiedAt = null);
        await SubscriberAsync(app, "recap_quiet", "quiet@example.test", u => u.DigestOn = false);
        await SubscriberAsync(app, "recap_susp", "susp@example.test", u => u.Suspended = true);
        await SubscriberAsync(app, "recap_free", "free@example.test", u => { u.Plan = Plans.Free; u.ProUntil = null; });
        await SubscriberAsync(app, "recap_lapsed", "lapsed@example.test", u => u.ProUntil = Now.AddDays(-1));
        await SubscriberAsync(app, "recap_far", "far@example.test", u => { u.ProUntil = Now.AddDays(20); u.BillingPeriodEnd = Now.AddDays(17); });
        // Review of Round 20: a subscription that will not charge (cancelled at the period end, a trial with no card,
        // a declined renewal) gets no "renews on" mail, and neither does a charge that is already behind us.
        await SubscriberAsync(app, "recap_ending", "ending@example.test", u => u.BillingRenews = false);
        await SubscriberAsync(app, "recap_passed", "passed@example.test", u => u.BillingPeriodEnd = Now.AddHours(-1));

        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(1, run.Sent);
        // Money mail is transactional: the digest switch does not silence it.
        Assert.Single(app.Email.To("quiet@example.test"));
        foreach (var address in new[] { "gift@example.test", "typo@example.test", "susp@example.test", "free@example.test", "lapsed@example.test", "far@example.test", "ending@example.test", "passed@example.test" })
        {
            Assert.Empty(app.Email.To(address));
        }
    }

    [Fact]
    public async Task Old_event_rows_are_pruned_after_thirty_days()
    {
        await using var app = NewApp();
        await WithDbAsync(app, db =>
        {
            db.StripeEvents.Add(new StripeEvent { Id = "evt_old", Type = "invoice.paid", ReceivedAt = Now.AddDays(-31) });
            db.StripeEvents.Add(new StripeEvent { Id = "evt_recent", Type = "invoice.paid", ReceivedAt = Now.AddDays(-29) });
            return Task.CompletedTask;
        });

        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(1, run.Pruned);
        await WithDbAsync(app, async db =>
        {
            var left = await db.StripeEvents.Select(e => e.Id).ToListAsync();
            Assert.Equal(["evt_recent"], left);
        });
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Pruned);
    }

    [Fact]
    public async Task With_mail_off_or_no_origin_nothing_is_sent_but_events_are_still_pruned()
    {
        await using var off = NewApp(emailEnabled: false);
        await SubscriberAsync(off, "recap_off", "off@example.test");
        await WithDbAsync(off, db => { db.StripeEvents.Add(new StripeEvent { Id = "evt_off", Type = "ping", ReceivedAt = Now.AddDays(-31) }); return Task.CompletedTask; });
        var quiet = await Worker(off).RunAsync(CancellationToken.None);
        Assert.Equal(0, quiet.Sent);
        Assert.Equal(1, quiet.Pruned);
        Assert.Empty(off.Email.Sent);
        Assert.Null((await UserOf(off, (await off.NewUserAsync("recap_off_probe")).Id)).RenewalRecapUntil);

        await using var noOrigin = NewApp(withOrigin: false);
        var id = await SubscriberAsync(noOrigin, "recap_noorigin", "noorigin@example.test");
        await WithDbAsync(noOrigin, db => { db.StripeEvents.Add(new StripeEvent { Id = "evt_noorigin", Type = "ping", ReceivedAt = Now.AddDays(-31) }); return Task.CompletedTask; });
        var silent = await Worker(noOrigin).RunAsync(CancellationToken.None);
        Assert.Equal(0, silent.Sent);
        Assert.Equal(1, silent.Pruned);
        Assert.Empty(noOrigin.Email.Sent);
        Assert.Null((await UserOf(noOrigin, id)).RenewalRecapUntil);
    }

    [Fact]
    public async Task A_failed_send_is_retried_next_hour()
    {
        await using var app = NewApp();
        const string address = "retry@example.test";
        var id = await SubscriberAsync(app, "recap_retry", address);

        app.Email.Fail = true;
        var failed = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(0, failed.Sent);
        Assert.Equal(1, failed.Skipped);
        Assert.Null((await UserOf(app, id)).RenewalRecapUntil);

        app.Email.Fail = false;
        var sent = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(1, sent.Sent);
        Assert.Single(app.Email.To(address));
        Assert.Equal(Now.AddDays(3), (await UserOf(app, id)).RenewalRecapUntil);
    }

    /// <summary>
    /// Review of Round 20 (F2/F8): the recap says "renews ... nothing to do", so it goes only where Stripe will really
    /// charge. A portal cancel (cancel at the period end, still active) gets none, and gets it again once undone; a
    /// no-card trial gets none, even after Stripe's own trialing event, until a card is on it.
    /// </summary>
    [Fact]
    public async Task A_subscription_set_to_cancel_or_a_trial_with_no_card_gets_no_renewal_mail()
    {
        await using var app = StripeApp();
        const string leaving = "leaving@example.test";
        const string trying = "trying@example.test";
        var leavingId = await PersonAsync(app, "recap_leaving", leaving);
        var tryingId = await PersonAsync(app, "recap_trying", trying);

        await PostEventAsync(app, Checkout(leavingId, "cus_leaving", "sub_leaving"));
        await PostEventAsync(app, Checkout(tryingId, "cus_trying", "sub_trying", trialDays: "7"));
        var paid = await UserOf(app, leavingId);
        var trial = await UserOf(app, tryingId);
        Assert.True(paid.BillingRenews);
        Assert.False(trial.BillingRenews);
        var periodEnd = paid.BillingPeriodEnd!.Value;
        var trialEnd = trial.BillingPeriodEnd!.Value;
        Assert.InRange(trialEnd, DateTime.UtcNow.AddDays(7).AddMinutes(-2), DateTime.UtcNow.AddDays(7).AddMinutes(2));

        // Cancelled in the portal: Stripe's default is the end of the period, so the subscription stays active.
        await PostEventAsync(app, Updated("cus_leaving", "sub_leaving", "active", periodEnd, cancelAtPeriodEnd: true));
        // Stripe's own trialing event names the trial's end and no card.
        await PostEventAsync(app, Updated("cus_trying", "sub_trying", "trialing", trialEnd));

        app.Clock.Now = trialEnd.AddDays(-2);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        app.Clock.Now = periodEnd.AddDays(-2);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Empty(app.Email.To(leaving));
        Assert.Empty(app.Email.To(trying));

        // The cancel undone: the mail goes, naming the day Stripe charges.
        await PostEventAsync(app, Updated("cus_leaving", "sub_leaving", "active", periodEnd));
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        var recap = Assert.Single(Recaps(app, leaving));
        Assert.Equal("Your OREVOSH Pro renews on " + Localizer.Day(periodEnd, "en"), recap.Subject);

        // A card on the trial: its end is a charge now, and three days before it the mail goes.
        await PostEventAsync(app, Updated("cus_trying", "sub_trying", "trialing", trialEnd, card: "pm_card_visa"));
        app.Clock.Now = trialEnd.AddDays(-2);
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Equal("Your OREVOSH Pro renews on " + Localizer.Day(trialEnd, "en"), Assert.Single(Recaps(app, trying)).Subject);
    }

    /// <summary>
    /// Review of Round 20 (F2): a declined renewal gets the card letter and no "renews ... nothing to do" after it, both
    /// for an account whose period Stripe named and for one from before that column, whose Round 20 stamp was the old
    /// end date the decline just moved. The letter's date is in the person's language (F18).
    /// </summary>
    [Fact]
    public async Task A_declined_renewal_gets_the_card_letter_and_no_renewal_mail()
    {
        await using var app = StripeApp();
        const string named = "declined@example.test";
        const string veteran = "veteran@example.test";
        var namedId = await PersonAsync(app, "recap_declined", named);
        var veteranId = await PersonAsync(app, "recap_veteran", veteran, "ru");
        var charge = DateTime.UtcNow.AddDays(2);

        // A subscriber whose charge is two days out gets the recap, once.
        await PostEventAsync(app, Checkout(namedId, "cus_declined", "sub_declined"));
        await PostEventAsync(app, Updated("cus_declined", "sub_declined", "active", charge));
        // One from before the columns: Round 20 mailed it for this period and stamped its end date.
        var veteranEnd = DateTime.UtcNow.AddDays(5);
        await WithDbAsync(app, async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == veteranId);
            user.Plan = Plans.Pro;
            user.ProUntil = veteranEnd;
            user.RenewalRecapUntil = veteranEnd;
            user.BillingCustomerId = "cus_veteran";
            user.BillingSubscriptionId = "sub_veteran";
        });
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Single(Recaps(app, named));
        Assert.Empty(app.Email.To(veteran));

        // The card is declined: past due, the next period named for one, none for the other. The end date moves to
        // three days from now, which Round 20 read as a new period to announce.
        await PostEventAsync(app, Updated("cus_declined", "sub_declined", "past_due", charge.AddMonths(1)));
        await PostEventAsync(app, Updated("cus_veteran", "sub_veteran", "past_due", null));
        Assert.False((await UserOf(app, namedId)).BillingRenews);
        var cutoff = (await UserOf(app, veteranId)).ProUntil!.Value;
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        app.Clock.Now = charge.AddHours(1);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Single(Recaps(app, named));
        Assert.Empty(Recaps(app, veteran));

        // The card letters went, and the Russian one names the day in Russian.
        Assert.Single(app.Email.To(named), m => m.Subject == "Your OREVOSH Pro payment didn't go through");
        var letter = Assert.Single(app.Email.To(veteran));
        Assert.Contains(Localizer.Day(cutoff, "ru"), letter.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(cutoff.ToString("MMMM", CultureInfo.InvariantCulture), letter.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review of Round 20 (F3): the charge is the period Stripe bills, not the end date. A monthly Checkout grants 35 days
    /// while Stripe charges one calendar month later, so the mail named a day up to four days late and went too late
    /// to cancel in time; on top of a gift it went a month or more after the first charge.
    /// </summary>
    [Fact]
    public async Task The_mail_names_the_day_stripe_charges_not_the_end_of_the_grant()
    {
        await using var app = StripeApp();
        const string monthly = "monthly@example.test";
        const string gifted = "gifted@example.test";
        var monthlyId = await PersonAsync(app, "recap_monthly", monthly);
        var giftedId = await PersonAsync(app, "recap_gifted", gifted);
        await WithDbAsync(app, async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == giftedId);
            user.Plan = Plans.Pro;
            user.ProUntil = DateTime.UtcNow.AddDays(100);
        });

        var before = DateTime.UtcNow;
        await PostEventAsync(app, Checkout(monthlyId, "cus_monthly", "sub_monthly"));
        await PostEventAsync(app, Checkout(giftedId, "cus_gifted", "sub_gifted"));
        var first = await UserOf(app, monthlyId);
        var second = await UserOf(app, giftedId);
        // One calendar month from the Checkout, whatever the end date carries.
        Assert.InRange(first.BillingPeriodEnd!.Value, before.AddMonths(1).AddSeconds(-1), DateTime.UtcNow.AddMonths(1).AddSeconds(1));
        Assert.InRange(second.BillingPeriodEnd!.Value, before.AddMonths(1).AddSeconds(-1), DateTime.UtcNow.AddMonths(1).AddSeconds(1));
        Assert.True(first.ProUntil > first.BillingPeriodEnd!.Value.AddDays(3));
        Assert.True(second.ProUntil > second.BillingPeriodEnd!.Value.AddDays(60));

        var charge = new[] { first.BillingPeriodEnd!.Value, second.BillingPeriodEnd!.Value }.Max();
        app.Clock.Now = charge.AddDays(-4);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        app.Clock.Now = charge.AddDays(-3).AddMinutes(1);
        Assert.Equal(2, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Equal("Your OREVOSH Pro renews on " + Localizer.Day(first.BillingPeriodEnd!.Value, "en"), Assert.Single(Recaps(app, monthly)).Subject);
        Assert.Equal("Your OREVOSH Pro renews on " + Localizer.Day(second.BillingPeriodEnd!.Value, "en"), Assert.Single(Recaps(app, gifted)).Subject);

        // Stripe's own invoice then names the next period, and the next mail is due three days before that.
        var next = first.BillingPeriodEnd!.Value.AddMonths(1);
        await PostEventAsync(app, new
        {
            id = EventId(),
            type = "invoice.paid",
            data = new
            {
                @object = new
                {
                    id = "in_monthly", @object = "invoice", customer = "cus_monthly", billing_reason = "subscription_cycle",
                    lines = new
                    {
                        @object = "list",
                        data = new object[]
                        {
                            new { id = "il_monthly", @object = "line_item", period = new { start = new DateTimeOffset(DateTime.SpecifyKind(first.BillingPeriodEnd!.Value, DateTimeKind.Utc)).ToUnixTimeSeconds(), end = new DateTimeOffset(DateTime.SpecifyKind(next, DateTimeKind.Utc)).ToUnixTimeSeconds() } }
                        }
                    }
                }
            }
        });
        var renewed = (await UserOf(app, monthlyId)).BillingPeriodEnd!.Value;
        Assert.InRange(renewed, next.AddSeconds(-1), next.AddSeconds(1));
        app.Clock.Now = renewed.AddDays(-3).AddMinutes(1);
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Equal(2, Recaps(app, monthly).Count);
    }

    /// <summary>
    /// Review of Round 20: an account from before the columns (no period named, nothing said about renewing) keeps
    /// Round 20's reading, the end date minus the slack, until Stripe's next event; and the stamp Round 20 wrote, the end
    /// date, still covers its period.
    /// </summary>
    [Fact]
    public async Task An_account_from_before_the_period_column_keeps_round_20s_reading()
    {
        await using var app = NewApp();
        const string address = "before@example.test";
        const string mailed = "mailed@example.test";
        var id = await SubscriberAsync(app, "recap_before", address, u => { u.BillingPeriodEnd = null; u.BillingRenews = null; });
        await SubscriberAsync(app, "recap_mailed", mailed, u => { u.BillingPeriodEnd = null; u.BillingRenews = null; u.RenewalRecapUntil = u.ProUntil; });

        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Equal("Your OREVOSH Pro renews on 11 October", Assert.Single(app.Email.To(address)).Subject);
        Assert.Empty(app.Email.To(mailed));
        Assert.Equal(Now.AddDays(3), (await UserOf(app, id)).RenewalRecapUntil);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
    }

    /// <summary>Review of Round 20 (F18): the date is the person's own language's, in the subject and in the body.</summary>
    [Theory]
    [InlineData("ru", "Твой OREVOSH Pro продлевается 11 октября")]
    [InlineData("he", "ה-OREVOSH Pro שלך מתחדש ב-11 אוקטובר")]
    [InlineData("ar", "يتجدّد اشتراك OREVOSH Pro في 11 أكتوبر")]
    public async Task The_date_is_written_in_the_persons_language(string language, string subject)
    {
        await using var app = NewApp();
        var address = $"recap-{language}@example.test";
        await SubscriberAsync(app, "recap_" + language, address, u => u.PreferredLanguage = language);

        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        var mail = Assert.Single(app.Email.To(address));
        Assert.Equal(subject, mail.Subject);
        Assert.Contains(Localizer.Day(Now.AddDays(3), language), mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("October", mail.Body, StringComparison.Ordinal);
    }
}
