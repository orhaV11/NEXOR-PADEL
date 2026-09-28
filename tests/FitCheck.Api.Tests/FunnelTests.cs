using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 13 — the growth loop: the funnel. What the little middleware counts and what it leaves alone, the day rows the
/// numbers page reads back, and the gate that keeps all of it behind a moderator's session.
/// </summary>
public class FunnelTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public FunnelTests(TestApp app) => _app = app;

    private static async Task<long> CounterAsync(TestApp app, string name)
    {
        using var scope = app.Services.CreateScope();
        return await Counters.ReadAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), name, CancellationToken.None);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task A_landing_view_is_one_tally_a_day_and_the_screenshots_beside_it_are_not()
    {
        var before = await CounterAsync(_app, Funnel.Landing(Today));
        var client = _app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/landing/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/landing/index.he.html")).StatusCode);
        // The pictures on the page are not visits.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/landing/screens/check-en.jpg")).StatusCode);

        Assert.Equal(before + 2, await CounterAsync(_app, Funnel.Landing(Today)));
    }

    [Fact]
    public async Task An_arrival_carrying_an_invite_is_counted_and_a_share_marker_is_not_an_invite()
    {
        var before = await CounterAsync(_app, Funnel.InviteArrivals(Today));
        var client = _app.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/?via=someone")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/landing/?via=someone.else_2")).StatusCode);
        // The share marker is the share loop, not an invite; garbage in the query is nobody's link.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/?via=share")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/?via=not%20a%20handle%21")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        // The API is never a page.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/config?via=someone")).StatusCode);

        Assert.Equal(before + 2, await CounterAsync(_app, Funnel.InviteArrivals(Today)));
    }

    [Fact]
    public void The_landing_test_knows_a_page_from_a_file()
    {
        Assert.True(Funnel.IsLandingPage("/landing"));
        Assert.True(Funnel.IsLandingPage("/landing/"));
        Assert.True(Funnel.IsLandingPage("/landing/index.he.html"));
        Assert.False(Funnel.IsLandingPage("/landing/screens/look-en.jpg"));
        Assert.False(Funnel.IsLandingPage("/"));
        Assert.False(Funnel.IsLandingPage("/app/core.js"));
    }

    [Fact]
    public void A_step_over_the_step_before_it_is_null_when_nothing_came_before()
    {
        Assert.Null(Funnel.Rate(3, 0));
        Assert.Equal(0.5, Funnel.Rate(1, 2));
        Assert.Equal(0.3333, Funnel.Rate(1, 3));
        Assert.Equal(0.0, Funnel.Rate(0, 7));
    }

    [Fact]
    public void The_counter_names_are_one_row_per_day()
    {
        var day = new DateOnly(2026, 9, 20);
        Assert.Equal("funnel:landing:20260920", Funnel.Landing(day));
        Assert.Equal("arrivals:look:20260920", Funnel.LookArrivals(day));
        Assert.Equal("arrivals:look:share:20260920", Funnel.ShareArrivals(day));
        Assert.Equal("arrivals:profile:20260920", Funnel.ProfileArrivals(day));
        Assert.Equal("invites:via:20260920", Funnel.InviteArrivals(day));
        // Round 20: the Pro page opened from a refused compare and from the wardrobe line.
        Assert.Equal("funnel:pro:compare:20260920", Funnel.ProFromCompare(day));
        Assert.Equal("funnel:pro:wardrobe:20260920", Funnel.ProFromWardrobe(day));
        Assert.Equal("funnel:pro:compare:20260920", Funnel.ProOpenedCounter("compare", day));
        Assert.Equal("funnel:pro:wardrobe:20260920", Funnel.ProOpenedCounter("wardrobe", day));
        Assert.Null(Funnel.ProOpenedCounter("feed", day));
        Assert.Null(Funnel.ProOpenedCounter(null, day));
        // Round 20: an arrival through an entry link, per source, and a launch from the home screen.
        Assert.Equal("funnel:src:tt:20260920", Funnel.SourceArrivals("tt", day));
        Assert.Equal("funnel:standalone:20260920", Funnel.Standalone(day));
        // Round 20 review: what the guest sweep removed, by the day the check was made, in all and per entry link.
        Assert.Equal("funnel:guest:swept:20260920", Funnel.SweptGuestChecks(day));
        Assert.Equal("funnel:guest:swept:tt:20260920", Funnel.SweptGuestChecks("tt", day));
    }

    [Fact]
    public async Task The_numbers_page_carries_fourteen_days_oldest_first_with_todays_conversion()
    {
        using var app = new TestApp();
        var mod = app.NewClient();
        await app.SignupAsync(mod, "funnelmod");
        await app.PromoteAsync("funnelmod");

        // One walk of the loop: a landing view, a guest check, a signup, a first post, an arrival on its public page.
        var guest = app.CreateClient();
        guest.DefaultRequestHeaders.Add(Sessions.RequestHeader, Sessions.RequestHeaderValue);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync("/landing/")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);

        var (walker, _, _) = await app.NewUserAsync("funnelwalker");
        var postId = await app.CheckAndPostAsync(walker);
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync($"/look/{postId}?via=share")).StatusCode);

        var metrics = await (await mod.GetAsync("/api/metrics/pilot")).Content.ReadFromJsonAsync<JsonElement>();
        var funnel = metrics.GetProperty("funnel");
        var days = funnel.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(Funnel.Days, days.Count);
        Assert.Equal(Today.AddDays(-(Funnel.Days - 1)).ToString("yyyy-MM-dd"), days[0].GetProperty("day").GetString());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), days[^1].GetProperty("day").GetString());

        var last = days[^1];
        Assert.Equal(1, last.GetProperty("landing").GetInt32());
        Assert.Equal(1, last.GetProperty("guestChecks").GetInt32());
        Assert.Equal(2, last.GetProperty("signups").GetInt32());   // the moderator and the walker
        Assert.Equal(1, last.GetProperty("firstPosts").GetInt32());
        Assert.Equal(1, last.GetProperty("lookArrivals").GetInt32());
        Assert.Equal(1, last.GetProperty("shareArrivals").GetInt32());
        // Round 20: nobody has opened the Pro page from either surface yet, and no launch from the home screen on any day.
        Assert.Equal(0, last.GetProperty("proFromCompare").GetInt32());
        Assert.Equal(0, last.GetProperty("proFromWardrobe").GetInt32());
        Assert.All(days, day => Assert.Equal(0, day.GetProperty("standalone").GetInt32()));
        // Round 20: one row per allowlisted entry link, all zero, since nobody followed one.
        var sources = funnel.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(new FunnelOptions().List.Count, sources.Count);
        Assert.All(sources, row => Assert.Equal(0, row.GetProperty("arrivals").GetInt32()));

        // The walker is refused a compare and opens Pro from there, then from the wardrobe line: one each, today.
        Assert.Equal(HttpStatusCode.NoContent, (await walker.PostAsJsonAsync("/api/funnel/pro-opened", new { from = "compare" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await walker.PostAsJsonAsync("/api/funnel/pro-opened", new { from = "wardrobe" })).StatusCode);
        var again = (await (await mod.GetAsync("/api/metrics/pilot")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("funnel").GetProperty("days").EnumerateArray().Last();
        Assert.Equal(1, again.GetProperty("proFromCompare").GetInt32());
        Assert.Equal(1, again.GetProperty("proFromWardrobe").GetInt32());

        var today = funnel.GetProperty("today");
        Assert.Equal(1.0, today.GetProperty("landingToGuestCheck").GetDouble());
        Assert.Equal(2.0, today.GetProperty("guestCheckToSignup").GetDouble());
        Assert.Equal(0.5, today.GetProperty("signupToFirstPost").GetDouble());
        Assert.Equal(1.0, today.GetProperty("arrivalFromShare").GetDouble());
    }

    [Fact]
    public async Task Pro_opened_is_counted_by_source_signed_in_only_and_refuses_an_unknown_one()
    {
        var compareBefore = await CounterAsync(_app, Funnel.ProFromCompare(Today));
        var wardrobeBefore = await CounterAsync(_app, Funnel.ProFromWardrobe(Today));

        // Signed out: the surfaces that send people here are signed-in ones, so nobody else's tap counts.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PostAsJsonAsync("/api/funnel/pro-opened", new { from = "compare" })).StatusCode);

        var (client, _, _) = await _app.NewUserAsync("proopened");
        var feed = await client.PostAsJsonAsync("/api/funnel/pro-opened", new { from = "feed" });
        Assert.Equal(HttpStatusCode.BadRequest, feed.StatusCode);
        Assert.Equal("That request didn't look right.", (await feed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/funnel/pro-opened", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/funnel/pro-opened", new { from = "compare" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/funnel/pro-opened", new { from = "Wardrobe" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/funnel/pro-opened", new { from = "wardrobe" })).StatusCode);

        Assert.Equal(compareBefore + 1, await CounterAsync(_app, Funnel.ProFromCompare(Today)));
        Assert.Equal(wardrobeBefore + 2, await CounterAsync(_app, Funnel.ProFromWardrobe(Today)));
    }

    /// <summary>
    /// Review of Round 20: a session is not an account. A suspended account's cookie and a cookie that outlived its
    /// account are refused (403 and 401, as on every other signed-in door) and signed out, and neither moves the tally
    /// the moderators read - the account they locked out least of all.
    /// </summary>
    [Fact]
    public async Task Pro_opened_is_refused_to_a_suspended_account_and_a_cookie_with_no_account()
    {
        using var app = new TestApp();
        var (suspended, suspendedId, _) = await app.NewUserAsync("proopened_locked");
        var (ghost, ghostId, _) = await app.NewUserAsync("proopened_ghost");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Id == suspendedId)).Suspended = true;
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == ghostId));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await suspended.PostAsJsonAsync("/api/funnel/pro-opened", new { from = "compare" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ghost.PostAsJsonAsync("/api/funnel/pro-opened", new { from = "wardrobe" })).StatusCode);
        // The refusal dropped the cookie, so the next try is plainly signed out.
        Assert.Equal(HttpStatusCode.Unauthorized, (await suspended.PostAsJsonAsync("/api/funnel/pro-opened", new { from = "compare" })).StatusCode);

        Assert.Equal(0, await CounterAsync(app, Funnel.ProFromCompare(Today)));
        Assert.Equal(0, await CounterAsync(app, Funnel.ProFromWardrobe(Today)));
    }

    [Fact]
    public async Task The_funnel_is_moderators_only_like_the_rest_of_the_page()
    {
        var (plain, _, _) = await _app.NewUserAsync("funnelplain");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync("/api/metrics/pilot")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().GetAsync("/api/metrics/pilot")).StatusCode);
    }

    [Fact]
    public async Task A_guest_check_that_was_claimed_is_still_a_guest_check()
    {
        using var app = new TestApp();
        var mod = app.NewClient();
        await app.SignupAsync(mod, "claimmod");
        await app.PromoteAsync("claimmod");

        // The same cookie jar makes a check as a visitor, then signs up: the check follows the account.
        var browser = app.NewClient();
        Assert.Equal(HttpStatusCode.Created, (await browser.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        await app.SignupAsync(browser, "claimwalker");
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/api/checks/claim", null)).StatusCode);

        var metrics = await (await mod.GetAsync("/api/metrics/pilot")).Content.ReadFromJsonAsync<JsonElement>();
        var days = metrics.GetProperty("funnel").GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(1, days[^1].GetProperty("guestChecks").GetInt32());
    }
}
