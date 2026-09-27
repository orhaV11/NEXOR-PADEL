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

    /// <summary>A paying subscriber: Pro with a subscription and a customer on Stripe's side, a confirmed address, the end date six days out (three before the charge).</summary>
    private static async Task<Guid> SubscriberAsync(TestApp app, string handle, string address, Action<AppUser>? shape = null)
    {
        var (_, id, _) = await app.NewUserAsync(handle);
        await WithDbAsync(app, async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == id);
            user.Plan = Plans.Pro;
            user.ProUntil = Now.AddDays(6);
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
        // The charge is the end date minus the slack: the 11th for an end date on the 14th.
        Assert.Equal("Your OREVOSH Pro renews on 11 October", mail.Subject);
        Assert.Contains("@recap_one", mail.Body, StringComparison.Ordinal);
        Assert.Contains("renews on 11 October", mail.Body, StringComparison.Ordinal);
        Assert.Contains("2 comparisons decided", mail.Body, StringComparison.Ordinal);
        Assert.Contains("3 outfits planned, 2 of them worn", mail.Body, StringComparison.Ordinal);
        Assert.Contains("1 tips that named something you already own", mail.Body, StringComparison.Ordinal);
        Assert.Contains(Origin + BillingEndpoints.PortalReturnPath, mail.Body, StringComparison.Ordinal);
        // Transactional: no unsubscribe link, no "digest" anything.
        Assert.DoesNotContain("/digest/off/", mail.Body, StringComparison.Ordinal);

        var user = await UserOf(app, id);
        Assert.Equal(user.ProUntil, user.RenewalRecapUntil);
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

        // The renewal moves the end date a month on (what invoice.paid does); eight days before it is too early.
        var renewed = Now.AddDays(36);
        await WithDbAsync(app, async db => (await db.Users.SingleAsync(u => u.Id == id)).ProUntil = renewed);
        app.Clock.Now = renewed.AddDays(-8);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        // Six days before it, the next period's mail goes, and only once.
        app.Clock.Now = renewed.AddDays(-6);
        Assert.Equal(1, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Equal(0, (await Worker(app).RunAsync(CancellationToken.None)).Sent);
        Assert.Equal(2, app.Email.To(address).Count);
        Assert.Equal(renewed, (await UserOf(app, id)).RenewalRecapUntil);
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
        await SubscriberAsync(app, "recap_far", "far@example.test", u => u.ProUntil = Now.AddDays(20));

        var run = await Worker(app).RunAsync(CancellationToken.None);
        Assert.Equal(1, run.Sent);
        // Money mail is transactional: the digest switch does not silence it.
        Assert.Single(app.Email.To("quiet@example.test"));
        foreach (var address in new[] { "gift@example.test", "typo@example.test", "susp@example.test", "free@example.test", "lapsed@example.test", "far@example.test" })
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
        Assert.Equal(Now.AddDays(6), (await UserOf(app, id)).RenewalRecapUntil);
    }
}
