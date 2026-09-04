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
    }
}

/// <summary>Own fixture: metrics are global, so this class must not share a database with the other endpoint tests.</summary>
public class MetricsEndpointTests : IClassFixture<MetricsEndpointTests.MetricsApp>
{
    public sealed class MetricsApp : TestApp;

    private readonly MetricsApp _app;

    public MetricsEndpointTests(MetricsApp app) => _app = app;

    [Fact]
    public async Task Endpoint_ignores_non_ok_checks_and_computes_return_rate()
    {
        var client = _app.CreateClient();
        var returning = await _app.CreateUserAsync(client, "returning", "he");
        var oneOff = await _app.CreateUserAsync(client, "oneoff");
        var late = await _app.CreateUserAsync(client, "late");
        var errorsOnly = await _app.CreateUserAsync(client, "errors");

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

        var m = await client.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");

        Assert.Equal(5, m.GetProperty("totalChecks").GetInt32());
        Assert.Equal(3, m.GetProperty("usersWithAtLeastOneCheck").GetInt32());
        Assert.Equal(1, m.GetProperty("usersWithSecondCheckWithin7Days").GetInt32());
        Assert.Equal(0.3333, m.GetProperty("returnRate").GetDouble(), precision: 4);
        Assert.Equal(2700, m.GetProperty("avgLatencyMs").GetInt32());
        Assert.Equal(2, m.GetProperty("scoreDistribution").GetProperty("7").GetInt32());
        Assert.Equal(1, m.GetProperty("scoreDistribution").GetProperty("8").GetInt32());
        Assert.Equal(2, m.GetProperty("byLanguage").GetProperty("he").GetInt32());
        Assert.Equal(3, m.GetProperty("byLanguage").GetProperty("en").GetInt32());
        Assert.Equal(5, m.GetProperty("byPromptVersion").GetProperty("v1").GetInt32());
    }
}
