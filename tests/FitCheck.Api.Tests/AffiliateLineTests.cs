using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 16 — the only money a FREE user can make. A tap on a store link used to raise one global tally with no
/// store, no item, no look and no day in it: nothing to reconcile against a partner's report, and no way to know
/// which looks earn. And there was nowhere at all to put a commission — none of the app's tables could hold one.
/// </summary>
public class AffiliateLineTests
{
    private sealed class ShopApp : TestApp
    {
        public ShopApp(bool programme = true)
        {
            Vision.Handler = _ => Payloads.Ok();
            if (programme)
            {
                // A configured store earns; anything else is traffic given away.
                Settings["Affiliate:Hosts:shop.test"] = "tag=orevosh-20";
            }
        }
    }

    /// <summary>
    /// Someone tapping a store link, whose client does NOT follow the redirect. One that does walks the 302's absolute
    /// address back into the test server and is answered 404 by the app's own router — which reads exactly like the
    /// door being shut, and is not.
    /// </summary>
    private static HttpClient Shopper(TestApp app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>
    /// An ORDINARY account tagging a garment with a store link (PATCH /api/posts/{id}/items). The brand-only gate is
    /// on a brand's own product links, which are a different feature — anyone can tag what they are wearing, so this
    /// line of revenue is open to every user and not only to brands.
    /// </summary>
    private static async Task<(Guid ItemId, Guid PostId, Guid OwnerId)> TaggedLookAsync(TestApp app, string handle, string url)
    {
        var (client, ownerId, _) = await app.NewUserAsync(handle);
        var checkId = await app.CheckAsync(client);
        var postId = (await app.PostAsync(client, checkId, caption: "with a link")).GetProperty("id").GetGuid();

        var patched = await client.PatchAsJsonAsync($"/api/posts/{postId}/items", new
        {
            items = new[] { new { name = "black boots", category = "shoes", url } }
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = db.PostItems.Single(i => i.PostId == postId);
        var post = db.Posts.Single(p => p.Id == postId);
        Assert.True(Services.PostItems.IsStoreUrl(item.Url), $"the tagged item kept no store link: url={item.Url ?? "<null>"}");
        Assert.False(post.Hidden, "the look is hidden, so the out door is shut");
        return (item.Id, postId, ownerId);
    }

    [Fact]
    public async Task A_tap_that_leaves_is_a_row_with_the_store_the_look_and_whose_look_it_was()
    {
        using var app = new ShopApp();
        var (itemId, postId, ownerId) = await TaggedLookAsync(app, "shop_owner", "https://www.shop.test/boots");

        var response = await Shopper(app).GetAsync($"/api/items/{itemId}/out");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("tag=orevosh-20", response.Headers.Location!.ToString(), StringComparison.Ordinal);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var click = Assert.Single(db.ItemClicks.ToList());

        Assert.Equal(itemId, click.ItemId);
        Assert.Equal(postId, click.PostId);
        Assert.Equal(ownerId, click.OwnerId);
        // "www." is off and the case is down: a partner reports "Shop.test" and the link said "www.shop.test".
        Assert.Equal("shop.test", click.Host);
        Assert.True(click.Earning);
    }

    /// <summary>
    /// Whether a tap could earn is stored at the moment of the tap, not worked out later. Configuration changes, and a
    /// tap that never could have earned must not look, a month afterwards, like one that failed to.
    /// </summary>
    [Fact]
    public async Task A_store_with_no_programme_is_recorded_as_traffic_given_away()
    {
        using var app = new ShopApp(programme: false);
        var (itemId, _, _) = await TaggedLookAsync(app, "shop_free", "https://shop.test/boots");

        Assert.Equal(HttpStatusCode.Redirect, (await Shopper(app).GetAsync($"/api/items/{itemId}/out")).StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(Assert.Single(db.ItemClicks.ToList()).Earning);
    }

    /// <summary>
    /// The one that stops a business inventing revenue. A commission arrives expected, comes back confirmed weeks
    /// later under the SAME partner id, and must move the row it already made. Import the file twice and the money
    /// must not double.
    /// </summary>
    [Fact]
    public async Task The_same_sale_reported_again_moves_its_row_rather_than_adding_a_second()
    {
        using var app = new ShopApp();
        var (moderator, _, _) = await app.NewUserAsync("aff_mod");
        await app.PromoteAsync("aff_mod");

        object Row(string state, decimal amount) => new
        {
            host = "Shop.test", externalId = "ORD-1", amount, currency = "usd", state,
            occurredAt = DateTime.UtcNow.AddDays(-2)
        };

        Assert.Equal(HttpStatusCode.OK, (await moderator.PostAsJsonAsync("/api/admin/affiliate/commissions",
            new { rows = new[] { Row("expected", 4.20m) } })).StatusCode);
        // Re-imported unchanged, then the same sale confirmed at a slightly different amount.
        await moderator.PostAsJsonAsync("/api/admin/affiliate/commissions", new { rows = new[] { Row("expected", 4.20m) } });
        await moderator.PostAsJsonAsync("/api/admin/affiliate/commissions", new { rows = new[] { Row("confirmed", 3.90m) } });

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = Assert.Single(db.Commissions.ToList());
        Assert.Equal(CommissionState.Confirmed, row.State);
        Assert.Equal(390, row.AmountMinor);
        Assert.Equal(3.90m, row.Amount);
        Assert.Equal("USD", row.Currency);
        // The host is stored the way a click stores it, or the two could never be reconciled.
        Assert.Equal("shop.test", row.Host);
    }

    [Fact]
    public async Task Expected_and_confirmed_are_never_added_together()
    {
        using var app = new ShopApp();
        var (moderator, _, _) = await app.NewUserAsync("aff_mod2");
        await app.PromoteAsync("aff_mod2");
        var (itemId, _, _) = await TaggedLookAsync(app, "shop_owner2", "https://shop.test/boots");
        await Shopper(app).GetAsync($"/api/items/{itemId}/out");

        await moderator.PostAsJsonAsync("/api/admin/affiliate/commissions", new
        {
            rows = new object[]
            {
                new { host = "shop.test", externalId = "A", amount = 5.00m, currency = "USD", state = "expected", occurredAt = DateTime.UtcNow },
                new { host = "shop.test", externalId = "B", amount = 7.00m, currency = "USD", state = "confirmed", occurredAt = DateTime.UtcNow },
                new { host = "shop.test", externalId = "C", amount = 2.00m, currency = "USD", state = "reversed", occurredAt = DateTime.UtcNow }
            }
        });

        var report = await Json(await moderator.GetAsync("/api/admin/affiliate"));
        Assert.Equal(1, report.GetProperty("clicks").GetInt32());
        Assert.Equal(1, report.GetProperty("earning").GetInt32());

        var money = report.GetProperty("money").EnumerateArray()
            .ToDictionary(m => m.GetProperty("state").GetString()!, m => m.GetProperty("amount").GetDecimal());
        Assert.Equal(5.00m, money["expected"]);
        Assert.Equal(7.00m, money["confirmed"]);
        Assert.Equal(2.00m, money["reversed"]);
        // Three separate lines, never one total: expected money is a sale inside its return window, not income.
        Assert.Equal(3, money.Count);
    }

    [Fact]
    public async Task A_report_this_app_cannot_read_is_refused_whole_rather_than_half_imported()
    {
        using var app = new ShopApp();
        var (moderator, _, _) = await app.NewUserAsync("aff_mod3");
        await app.PromoteAsync("aff_mod3");

        var refused = await moderator.PostAsJsonAsync("/api/admin/affiliate/commissions", new
        {
            rows = new object[]
            {
                new { host = "shop.test", externalId = "GOOD", amount = 5.00m, currency = "USD", state = "expected", occurredAt = DateTime.UtcNow },
                new { host = "shop.test", externalId = "BAD", amount = 5.00m, currency = "USD", state = "maybe", occurredAt = DateTime.UtcNow }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.Commissions.ToList());
    }

    [Fact]
    public async Task The_affiliate_numbers_are_a_moderators_and_nobody_elses()
    {
        using var app = new ShopApp();
        var (ordinary, _, _) = await app.NewUserAsync("aff_nobody");
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.GetAsync("/api/admin/affiliate")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().GetAsync("/api/admin/affiliate")).StatusCode);
    }

    [Fact]
    public void A_host_is_stored_one_way_so_a_click_and_a_commission_can_meet()
    {
        Assert.Equal("shop.test", Affiliates.NormaliseHost("www.Shop.Test"));
        Assert.Equal("shop.test", Affiliates.NormaliseHost(" shop.test. "));
        Assert.Equal("eu.shop.test", Affiliates.NormaliseHost("EU.shop.test"));
        Assert.Equal("", Affiliates.NormaliseHost(null));
    }

    /// <summary>
    /// Money is hundredths of its currency, in a long. A decimal here is the trap SQLite sets: it maps to TEXT, cannot
    /// be SUMmed at all — the report page threw a 500 the first time it was asked to add money up — and sorts 9.00
    /// after 10.00 because that is what text does.
    /// </summary>
    [Fact]
    public void A_commission_is_held_in_whole_minor_units()
    {
        Assert.Equal(420, Commission.ToMinor(4.20m));
        Assert.Equal(0, Commission.ToMinor(0m));
        Assert.Equal(-150, Commission.ToMinor(-1.50m));
        // Half away from zero, which is how money rounds and not what banker's rounding would give.
        Assert.Equal(5, Commission.ToMinor(0.045m));
        Assert.Equal(4.20m, new Commission { AmountMinor = 420 }.Amount);
    }
}
