using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;

namespace FitCheck.Api.Tests;

public class MetricsComputeTests
{
    private static MetricsEndpoints.MetricRow Row(Guid user, int day, int score = 6, int latency = 3000, string lang = "en", string version = "v1") =>
        new(user, new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(day), score, latency, lang, version);

    [Fact]
    public void Empty_dataset_is_all_zero()
    {
        var m = MetricsEndpoints.Compute([]);
        Assert.Equal(0, m.TotalChecks);
        Assert.Equal(0, m.UsersWithAtLeastOneCheck);
        Assert.Equal(0, m.UsersWithSecondCheckWithin7Days);
        Assert.Equal(0.0, m.ReturnRate);
        Assert.Equal(0, m.AvgLatencyMs);
        Assert.Equal(10, m.ScoreDistribution.Count);
        Assert.All(m.ScoreDistribution.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Return_rate_counts_only_second_checks_within_seven_days_of_the_first()
    {
        var a = Guid.NewGuid(); // returns on day 3
        var b = Guid.NewGuid(); // second check on day 10: too late
        var c = Guid.NewGuid(); // single check
        var d = Guid.NewGuid(); // returns exactly on day 7 (inclusive), third check irrelevant

        var m = MetricsEndpoints.Compute(
        [
            Row(a, 0, score: 7, latency: 2000), Row(a, 3, score: 5, latency: 4000, lang: "he"),
            Row(b, 10, score: 6), Row(b, 0, score: 6), // out of order on purpose
            Row(c, 1, score: 9, version: "v2"),
            Row(d, 0, score: 4), Row(d, 7, score: 6), Row(d, 20, score: 8)
        ]);

        Assert.Equal(8, m.TotalChecks);
        Assert.Equal(4, m.UsersWithAtLeastOneCheck);
        Assert.Equal(2, m.UsersWithSecondCheckWithin7Days);
        Assert.Equal(0.5, m.ReturnRate);
        Assert.Equal(3000, m.AvgLatencyMs);
        Assert.Equal(1, m.ScoreDistribution["7"]);
        Assert.Equal(1, m.ScoreDistribution["5"]);
        Assert.Equal(3, m.ScoreDistribution["6"]);
        Assert.Equal(0, m.ScoreDistribution["10"]);
        Assert.Equal(7, m.ByLanguage["en"]);
        Assert.Equal(1, m.ByLanguage["he"]);
        Assert.Equal(7, m.ByPromptVersion["v1"]);
        Assert.Equal(1, m.ByPromptVersion["v2"]);
        Assert.Null(m.BreakdownAverages);
    }

    [Fact]
    public void Breakdown_averages_cover_only_the_checks_that_carry_one()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var m = MetricsEndpoints.Compute(
        [
            Row(a, 0, score: 7) with { Breakdown = new ScoreBreakdown { Fit = 7, Color = 8, Accessories = 4 } },
            Row(b, 0, score: 8, version: "v2") with { Breakdown = new ScoreBreakdown { Fit = 6, Color = 9, Accessories = 3 } },
            Row(b, 1, score: 5, version: "v2") with { Breakdown = new ScoreBreakdown { Fit = 5, Color = 5, Accessories = 10 } },
            Row(a, 2, score: 6)   // a v1 check: no breakdown, not in the averages, still in the totals
        ]);

        Assert.Equal(4, m.TotalChecks);
        var averages = Assert.IsType<BreakdownAveragesDto>(m.BreakdownAverages);
        Assert.Equal(3, averages.Checks);
        Assert.Equal(6.0, averages.AvgFit);
        Assert.Equal(7.33, averages.AvgColor);
        Assert.Equal(5.67, averages.AvgAccessories);
    }

    [Fact]
    public void Breakdown_is_read_out_of_the_stored_feedback()
    {
        Assert.Null(MetricsEndpoints.BreakdownOf(null));
        Assert.Null(MetricsEndpoints.BreakdownOf(""));
        Assert.Null(MetricsEndpoints.BreakdownOf("not json"));
        Assert.Null(MetricsEndpoints.BreakdownOf("""{"status":"ok","score":6}"""));
        var breakdown = MetricsEndpoints.BreakdownOf("""{"status":"ok","score":6,"breakdown":{"fit":7,"color":8,"accessories":4}}""");
        Assert.Equal((7, 8, 4), (breakdown!.Fit, breakdown.Color, breakdown.Accessories));
    }
}

/// <summary>Own fixture: the gate test signs accounts up, which would move the other class's social counts.</summary>
public class MetricsGateTests : IClassFixture<MetricsGateTests.GateApp>
{
    public sealed class GateApp : TestApp;

    private readonly GateApp _app;

    public MetricsGateTests(GateApp app) => _app = app;

    [Fact]
    public async Task The_pilot_numbers_are_for_moderators_only()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().GetAsync("/api/metrics/pilot")).StatusCode);

        var (person, _, _) = await _app.NewUserAsync("gate_person");
        var forbidden = await person.GetAsync("/api/metrics/pilot");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("Only OREVOSH moderators can do that.", (await forbidden.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var (hebrew, _, _) = await _app.NewUserAsync("gate_mod", language: "he");
        var forbiddenHe = await hebrew.GetAsync("/api/metrics/pilot");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenHe.StatusCode);
        Assert.Equal("רק צוות OREVOSH יכול לעשות את זה.", (await forbiddenHe.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        // The flag on the row is the gate, the same one /api/admin uses; it works from the next request on, and off again when lifted.
        await _app.PromoteAsync("gate_mod");
        var metrics = await hebrew.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");
        Assert.Equal(0, metrics.GetProperty("totalChecks").GetInt32());
        Assert.Equal(2, metrics.GetProperty("social").GetProperty("users").GetInt32());
        Assert.False(metrics.TryGetProperty("breakdownAverages", out _));   // no v2 check yet: null, so absent on the wire

        await _app.DemoteAsync("gate_mod");
        Assert.Equal(HttpStatusCode.Forbidden, (await hebrew.GetAsync("/api/metrics/pilot")).StatusCode);
    }
}

/// <summary>Own fixture: metrics are global, so this class must not share a database with the other endpoint tests.</summary>
public class MetricsEndpointTests : IClassFixture<MetricsEndpointTests.MetricsApp>
{
    public sealed class MetricsApp : TestApp;

    private readonly MetricsApp _app;

    public MetricsEndpointTests(MetricsApp app) => _app = app;

    [Fact]
    public async Task Endpoint_ignores_non_ok_checks_computes_return_rate_and_reports_the_social_loop()
    {
        // The one check made through the API is a rubric v2 check; the rows inserted below are v1 rows without feedback.
        _app.Vision.Handler = _ => V2Payloads.Ok(fit: 7, color: 8, accessories: 4);
        var (returningClient, returning, _) = await _app.NewUserAsync("returning", language: "he");
        var (oneOffClient, oneOff, _) = await _app.NewUserAsync("oneoff");
        var (_, late, _) = await _app.NewUserAsync("late");
        var (_, errorsOnly, _) = await _app.NewUserAsync("errors");
        var (brandClient, _, _) = await _app.NewUserAsync("metricbrand", accountType: "Brand");
        var (moderator, _, _) = await _app.NewUserAsync("metricmod");
        await _app.PromoteAsync("metricmod");

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var t0 = DateTime.UtcNow.AddDays(-20);
            void Add(Guid user, double days, string status, int? score, string lang = "en", int latency = 2500) =>
                db.Checks.Add(new OutfitCheck { Id = Guid.NewGuid(), UserId = user, Intent = StyleIntent.Date, Language = lang, Status = status, Score = score, LatencyMs = latency, PromptVersion = "v1", CreatedAt = t0.AddDays(days) });

            Add(returning, 0, CheckStatus.Ok, 6, "he");
            Add(returning, 2.5, CheckStatus.Ok, 8, "he", 3500);
            Add(oneOff, 0, CheckStatus.Ok, 5);
            Add(oneOff, 1, CheckStatus.NotOutfit, null);   // not an OK check: does not count as a return
            Add(oneOff, 2, CheckStatus.Error, null);
            Add(late, 0, CheckStatus.Ok, 7);
            Add(late, 7.5, CheckStatus.Ok, 7);
            Add(errorsOnly, 0, CheckStatus.Error, null);
            Add(errorsOnly, 0.1, CheckStatus.Rejected, null);
            await db.SaveChangesAsync();
        }

        // A little social activity on top: one post, one fire, one comment, one follow, one open challenge.
        var postId = await _app.CheckAndPostAsync(returningClient);
        await oneOffClient.PostAsync($"/api/posts/{postId}/fire", null);
        await oneOffClient.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "clean" });
        await oneOffClient.PostAsync("/api/users/returning/follow", null);
        await brandClient.PostAsJsonAsync("/api/challenges", new { title = "T", brief = "B", intent = "Date", prize = "P", endsAt = DateTime.UtcNow.AddDays(2) });

        var m = await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");

        Assert.Equal(6, m.GetProperty("totalChecks").GetInt32());
        Assert.Equal(3, m.GetProperty("usersWithAtLeastOneCheck").GetInt32());
        Assert.Equal(1, m.GetProperty("usersWithSecondCheckWithin7Days").GetInt32());
        Assert.Equal(0.3333, m.GetProperty("returnRate").GetDouble(), precision: 4);
        Assert.Equal(2, m.GetProperty("byLanguage").GetProperty("he").GetInt32());
        Assert.Equal(5, m.GetProperty("byPromptVersion").GetProperty("v1").GetInt32());
        Assert.Equal(1, m.GetProperty("byPromptVersion").GetProperty("v2").GetInt32());

        // The sub-score averages cover the one check that has a breakdown; the v1 rows carry none.
        var averages = m.GetProperty("breakdownAverages");
        Assert.Equal(1, averages.GetProperty("checks").GetInt32());
        Assert.Equal(7.0, averages.GetProperty("avgFit").GetDouble());
        Assert.Equal(8.0, averages.GetProperty("avgColor").GetDouble());
        Assert.Equal(4.0, averages.GetProperty("avgAccessories").GetDouble());

        var social = m.GetProperty("social");
        Assert.Equal(6, social.GetProperty("users").GetInt32());
        Assert.Equal(1, social.GetProperty("brands").GetInt32());
        Assert.Equal(1, social.GetProperty("posts").GetInt32());
        Assert.Equal(1, social.GetProperty("fires").GetInt32());
        Assert.Equal(1, social.GetProperty("comments").GetInt32());
        Assert.Equal(1, social.GetProperty("follows").GetInt32());
        Assert.Equal(1, social.GetProperty("challengesOpen").GetInt32());
        Assert.Equal(0, social.GetProperty("challengesEnded").GetInt32());
        Assert.Equal(2, social.GetProperty("activeUsers7d").GetInt32());
    }
}
