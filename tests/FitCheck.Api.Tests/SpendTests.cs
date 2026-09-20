using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FitCheck.Api.Tests;

/// <summary>
/// What the daily allowances count (<see cref="Spend"/>): checks and comparisons together, on the check route, the compare
/// route and the global ceiling alike, and the Retry-After that names the call whose expiry actually frees a permit. Each
/// test owns its app: the caps under test are global state.
/// </summary>
public class SpendTests
{
    [Fact]
    public void Retry_after_is_the_call_that_has_to_leave_the_window_for_the_count_to_drop_below_the_cap()
    {
        var now = new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);
        var recent = Enumerable.Range(0, 10).Select(i => now.AddHours(-23).AddMinutes(10 * i)).ToList();

        // At the cap, the oldest call is the next permit. Over it (a lapsed Pro at 10 with a cap of 3), the count must
        // drop to 2: the 8th oldest call has to go, 1 h 70 min from now.
        Assert.Equal(60 * 60, Spend.RetryAfterSeconds(recent, 10, now));
        Assert.Equal(60 * 60 + 70 * 60, Spend.RetryAfterSeconds(recent, 3, now));
        Assert.Equal(60 * 60 + 90 * 60, Spend.RetryAfterSeconds(recent, 1, now));
        // A cap of zero never frees a permit; the newest call's expiry is the most that can be said. Nothing stored: nothing to say.
        Assert.Equal(60 * 60 + 90 * 60, Spend.RetryAfterSeconds(recent, 0, now));
        Assert.Null(Spend.RetryAfterSeconds([], 3, now));
        // Never below one second, even for a call that just left the window.
        Assert.Equal(1, Spend.RetryAfterSeconds([now.AddHours(-24)], 1, now));
    }

    [Fact]
    public async Task A_comparison_counts_against_the_global_ceiling_on_the_check_route()
    {
        using var app = new TestApp { ChecksPerDayGlobal = 2 };
        app.Vision.Handler = request => request.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();
        var (a, _, _) = await app.NewUserAsync("spend_a");
        var (b, _, _) = await app.NewUserAsync("spend_b");
        var (c, _, _) = await app.NewUserAsync("spend_c");

        Assert.Equal(HttpStatusCode.Created, (await a.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await b.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);

        // One check and one comparison are two paid calls: the ceiling of two is reached for a check as it is for a comparison.
        var check = await c.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, check.StatusCode);
        Assert.Equal("OREVOSH is at capacity for today. Please try again tomorrow.", (await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        var comparison = await c.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, comparison.StatusCode);
        Assert.Equal(2, app.Vision.Requests.Count);

        // A guest is under the same ceiling.
        var guest = app.NewClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.77");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(2, app.Vision.Requests.Count);
    }

    [Fact]
    public async Task Retry_after_names_the_call_whose_expiry_frees_a_permit_on_both_routes()
    {
        using var app = new TestApp { FreeChecksPerDay = 3, ChecksPerDay = 30 };
        app.Vision.Handler = request => request.HasSecondImage ? OutfitComparerTests.Pick() : Payloads.Ok();
        var (client, userId, _) = await app.NewUserAsync("spend_lapsed");
        var now = DateTime.UtcNow;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Ten calls while Pro, 23 h ago and every ten minutes after; then Pro lapsed and the cap is three.
            var user = await db.Users.FindAsync(userId);
            user!.Plan = Plans.Pro;
            user.ProUntil = now.AddHours(-1);
            for (var i = 0; i < 10; i++)
            {
                db.Checks.Add(new OutfitCheck
                {
                    Id = Guid.NewGuid(), UserId = userId, Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Score = 6, PromptVersion = "v2",
                    CreatedAt = now.AddHours(-23).AddMinutes(10 * i)
                });
            }

            await db.SaveChangesAsync();
        }

        // The count drops below three once the eighth oldest call leaves the window: in 1 h 70 min, not when the oldest does in 1 h.
        var expected = TimeSpan.FromMinutes(60 + 70);
        var check = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, check.StatusCode);
        Assert.Equal("That's today's 3 free checks. Go Pro for 30 a day, or come back tomorrow.", (await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.InRange(check.Headers.RetryAfter!.Delta!.Value, expected - TimeSpan.FromMinutes(2), expected);

        var comparison = await client.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, comparison.StatusCode);
        Assert.InRange(comparison.Headers.RetryAfter!.Delta!.Value, expected - TimeSpan.FromMinutes(2), expected);
        Assert.Empty(app.Vision.Requests);

        // The day's count on "me" is the same ten, comparisons included.
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(10, me.GetProperty("checksToday").GetInt32());
        Assert.Equal(3, me.GetProperty("checksPerDay").GetInt32());
    }
}

// ---------- Round 13 — money: the spend meter and the daily ceiling ----------

/// <summary>
/// A vision client that reports usage the way the real one does: it hands the meter a <see cref="VisionUsage"/> for
/// every answer and then defers to the app's own scripted <see cref="FakeVisionClient"/>. What the real
/// <c>AnthropicVisionClient</c> does with the API's <c>usage</c> block, without a socket.
/// </summary>
public sealed class MeteredVisionClient(FakeVisionClient inner, SpendMeter meter, Func<VisionUsage> usage) : IOutfitVisionClient
{
    public async Task<JsonElement> AnalyzeAsync(VisionRequest request, CancellationToken ct)
    {
        var answer = await inner.AnalyzeAsync(request, ct);
        await meter.RecordAsync(usage(), CancellationToken.None);
        return answer;
    }
}

/// <summary>
/// The app, with a vision client that reports tokens. <see cref="Usage"/> is what every call is said to have cost; the
/// default is a round 500k in / 100k out, which at the default prices (2 and 10 USD per million) is exactly 2.00 USD a
/// call, so the arithmetic in these tests is readable.
/// </summary>
public sealed class MoneyApp : TestApp
{
    public VisionUsage Usage { get; set; } = new(500_000, 100_000);

    /// <summary>What one call of <see cref="Usage"/> costs at the app's default prices.</summary>
    public const decimal PerCallUsd = 2.00m;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOutfitVisionClient>();
            services.AddSingleton<IOutfitVisionClient>(provider =>
                new MeteredVisionClient(Vision, provider.GetRequiredService<SpendMeter>(), () => Usage));
        });
    }

    public SpendMeter Meter => Services.GetRequiredService<SpendMeter>();

    /// <summary>Today's row as the meter reads it, through the app's own database.</summary>
    public async Task<SpendDay> TodayAsync()
    {
        using var scope = Services.CreateScope();
        return await Meter.TodayAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
    }

    /// <summary>Wipes every spend row, as a new UTC day would leave them: the gate is open again.</summary>
    public async Task ClearSpendAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Counters.RemoveRange(db.Counters.Where(c => c.Name.StartsWith("spend:")));
        await db.SaveChangesAsync();
    }
}

public class SpendMeterTests
{
    private static string Error(JsonElement body) => body.GetProperty("error").GetString()!;

    [Fact]
    public async Task The_estimate_accumulates_over_the_day_and_the_numbers_page_reads_it()
    {
        using var app = new MoneyApp();
        var (client, _, _) = await app.NewUserAsync("money_meter");

        await app.CheckAsync(client);
        var afterOne = await app.TodayAsync();
        Assert.Equal(1, afterOne.Calls);
        Assert.Equal(500_000, afterOne.InputTokens);
        Assert.Equal(100_000, afterOne.OutputTokens);
        Assert.Equal(MoneyApp.PerCallUsd, afterOne.EstimatedUsd);

        await app.CheckAsync(client);
        var afterTwo = await app.TodayAsync();
        Assert.Equal(2, afterTwo.Calls);
        Assert.Equal(1_000_000, afterTwo.InputTokens);
        Assert.Equal(200_000, afterTwo.OutputTokens);
        Assert.Equal(2 * MoneyApp.PerCallUsd, afterTwo.EstimatedUsd);

        // The same numbers on the moderator's page, plus the prices they were made at and the 14-day series.
        await app.PromoteAsync("money_meter");
        var metrics = await client.GetFromJsonAsync<JsonElement>("/api/metrics/pilot");
        var spend = metrics.GetProperty("spend");
        Assert.Equal(2, spend.GetProperty("today").GetProperty("calls").GetInt64());
        Assert.Equal(4.0m, spend.GetProperty("today").GetProperty("estimatedUsd").GetDecimal());
        Assert.Equal(2.00m, spend.GetProperty("priceInPerMillion").GetDecimal());
        Assert.Equal(10.00m, spend.GetProperty("priceOutPerMillion").GetDecimal());
        Assert.Equal(0m, spend.GetProperty("ceilingUsd").GetDecimal());
        Assert.False(spend.GetProperty("resting").GetBoolean());
        var series = spend.GetProperty("series").EnumerateArray().ToList();
        Assert.Equal(SpendMeter.SeriesDays, series.Count);
        // Oldest first, today last, and today is the only day with anything on it.
        Assert.Equal(4.0m, series[^1].GetProperty("estimatedUsd").GetDecimal());
        Assert.All(series.Take(series.Count - 1), day => Assert.Equal(0, day.GetProperty("calls").GetInt64()));
        // Nothing here names a channel's value: only whether one is set.
        Assert.False(spend.GetProperty("alertWebhook").GetBoolean());
    }

    [Fact]
    public async Task The_gate_closes_at_the_ceiling_and_opens_again_after_midnight_utc()
    {
        using var app = new MoneyApp { Settings = { ["Limits:SpendPerDayUsd"] = "3" } };
        app.Clock.Now = new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        var (client, _, _) = await app.NewUserAsync("money_ceiling");

        // Two calls at 2.00 USD each: the first is under the ceiling, the second reaches it.
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(4.0m, (await app.TodayAsync()).EstimatedUsd);

        var refused = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("The stylist is resting until tomorrow. Your look is not spent.",
            Error(await refused.Content.ReadFromJsonAsync<JsonElement>()));
        // The model was never asked, so nothing more was spent and nothing was stored.
        Assert.Equal(2, app.Vision.Requests.Count);
        Assert.Equal(2, (await app.TodayAsync()).Calls);

        // A comparison is a stylist call too, and hears the same sentence.
        var comparison = await client.PostAsync("/api/compare", CompareTests.CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, comparison.StatusCode);
        Assert.Equal("The stylist is resting until tomorrow. Your look is not spent.",
            Error(await comparison.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Equal(2, app.Vision.Requests.Count);

        // The refused calls cost the person nothing: the day's count is still the two that were really made.
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(2, me.GetProperty("checksToday").GetInt32());

        // The rows are per UTC day, so the next one starts at zero and the gate is open again.
        app.Clock.Now = new DateTime(2026, 9, 21, 0, 30, 0, DateTimeKind.Utc);
        Assert.Equal(0m, (await app.TodayAsync()).EstimatedUsd);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(3, app.Vision.Requests.Count);
        Assert.Equal(MoneyApp.PerCallUsd, (await app.TodayAsync()).EstimatedUsd);
        // Yesterday's row is still there, which is what the 14-day series is made of.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(4.0m, (await app.Meter.DayAsync(db, "20260920", CancellationToken.None)).EstimatedUsd);
        }
    }

    [Fact]
    public async Task A_guests_free_look_is_not_spent_by_a_503()
    {
        using var app = new MoneyApp { Settings = { ["Limits:SpendPerDayUsd"] = "3" } };
        var guest = app.NewClient();
        guest.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.42");

        // The day is already over the ceiling before this visitor arrives.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Counters.IncrementAsync(db, SpendMeter.InPrefix + app.Meter.Today, CancellationToken.None, 2_000_000);
            await Counters.IncrementAsync(db, SpendMeter.CallsPrefix + app.Meter.Today, CancellationToken.None, 4);
        }

        var refused = await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Equal("The stylist is resting until tomorrow. Your look is not spent.",
            Error(await refused.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Empty(app.Vision.Requests);
        // No guest cookie was issued: the door was never opened, so nothing about this visitor was written down.
        Assert.DoesNotContain(refused.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.Contains(GuestChecks.CookieName, StringComparison.Ordinal));

        // The next day (the rows cleared, as midnight UTC leaves them), the free look is still there to spend.
        await app.ClearSpendAsync();
        Assert.Equal(HttpStatusCode.Created, (await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        // And now it really is spent: the second one is the guest limit, not the ceiling.
        var second = await guest.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(1, app.Vision.Requests.Count);
    }

    [Fact]
    public async Task A_ceiling_of_zero_is_no_ceiling_at_all()
    {
        using var app = new MoneyApp();
        var (client, _, _) = await app.NewUserAsync("money_off");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Counters.IncrementAsync(db, SpendMeter.InPrefix + app.Meter.Today, CancellationToken.None, 500_000_000);
        }

        Assert.Equal(1000m, (await app.TodayAsync()).EstimatedUsd);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
    }

    /// <summary>
    /// The real client against a scripted handler, built the way the container builds it (ActivatorUtilities with the
    /// HttpClient, which is exactly what AddHttpClient's typed factory does): the meter really is handed to it, the
    /// usage block really is read, and a failed call that the API answered is counted while one that never reached it
    /// is not.
    /// </summary>
    [Fact]
    public async Task The_real_client_hands_every_answers_usage_to_the_meter()
    {
        using var app = new MoneyApp();
        var handler = new ScriptedApiHandler();
        var client = ActivatorUtilities.CreateInstance<AnthropicVisionClient>(app.Services, new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) });
        Environment.SetEnvironmentVariable(AnthropicVisionClient.ApiKeyVariable, "test-key");
        var request = new VisionRequest(
            OutfitAnalyzer.BuildSystemPrompt("en"), OutfitAnalyzer.BuildUserMessage(StyleIntent.Date, null),
            TestImages.Jpeg(64), "image/jpeg", OutfitAnalyzer.Tool);

        // An answer with usage: the call and both token counts, plus the cache fields when the API reports them.
        handler.Responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""
                { "stop_reason": "tool_use",
                  "usage": { "input_tokens": 1200, "output_tokens": 340, "cache_read_input_tokens": 90, "cache_creation_input_tokens": 10 },
                  "content": [ { "type": "tool_use", "name": "{{OutfitAnalyzer.ToolName}}", "input": { "status": "ok", "score": 6 } } ] }
                """, Encoding.UTF8, "application/json")
        });
        await client.AnalyzeAsync(request, CancellationToken.None);
        var one = await app.TodayAsync();
        Assert.Equal(1, one.Calls);
        Assert.Equal(1200, one.InputTokens);
        Assert.Equal(340, one.OutputTokens);
        Assert.Equal(90, one.CacheReadTokens);
        Assert.Equal(10, one.CacheWriteTokens);

        // A 400 whose body still carried usage: the API billed it, so it counts. 400 is not retried.
        handler.Responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{ "type": "error", "usage": { "input_tokens": 700, "output_tokens": 0 } }""", Encoding.UTF8, "application/json")
        });
        await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(request, CancellationToken.None));
        var two = await app.TodayAsync();
        Assert.Equal(2, two.Calls);
        Assert.Equal(1900, two.InputTokens);

        // A connection that never opened: nobody billed anything, so nothing is counted.
        handler.Responses.Enqueue(() => throw new HttpRequestException("no route to host"));
        await Assert.ThrowsAsync<VisionClientException>(() => client.AnalyzeAsync(request, CancellationToken.None));
        var three = await app.TodayAsync();
        Assert.Equal(2, three.Calls);
        Assert.Equal(1900, three.InputTokens);
    }

    [Fact]
    public void An_answer_without_usage_reads_as_nothing_rather_than_as_a_guess()
    {
        Assert.False(VisionUsage.ReadFrom("not json at all").Any);
        Assert.False(VisionUsage.ReadFrom("""{ "content": [] }""").Any);
        Assert.False(VisionUsage.ReadFrom("""{ "usage": { "input_tokens": "lots" } }""").Any);
        // Negative numbers are somebody else's bug, not a credit.
        Assert.False(VisionUsage.ReadFrom("""{ "usage": { "input_tokens": -5, "output_tokens": -1 } }""").Any);
        var usage = VisionUsage.ReadFrom("""{ "usage": { "input_tokens": 9, "output_tokens": 3 } }""");
        Assert.Equal(9, usage.InputTokens);
        Assert.Equal(3, usage.OutputTokens);
    }
}

/// <summary>A handler that answers with whatever the next queued function returns (or throws).</summary>
public sealed class ScriptedApiHandler : HttpMessageHandler
{
    public Queue<Func<HttpResponseMessage>> Responses { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(Responses.Dequeue()());
}
