using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
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

        var today = funnel.GetProperty("today");
        Assert.Equal(1.0, today.GetProperty("landingToGuestCheck").GetDouble());
        Assert.Equal(2.0, today.GetProperty("guestCheckToSignup").GetDouble());
        Assert.Equal(0.5, today.GetProperty("signupToFirstPost").GetDouble());
        Assert.Equal(1.0, today.GetProperty("arrivalFromShare").GetDouble());
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
