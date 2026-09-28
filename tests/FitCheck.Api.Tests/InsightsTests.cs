using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

/// <summary>
/// What your checks say about you: the math over a seeded set of checks (intents, weak pieces, accessories, the
/// streak), the sentences in the account's language, the empty case under three checks, the window, the session, and
/// the Pro gate that is on exactly when comparisons need Pro. Checks are written straight into the table so every
/// field of the feedback is under the test's control.
/// </summary>
public class InsightsTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public InsightsTests(TestApp app) => _app = app;

    private static bool IsNull(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static List<string> Lines(JsonElement dto) => dto.GetProperty("lines").EnumerateArray().Select(l => l.GetString()!).ToList();

    /// <summary>A feedback with items of the given categories and verdicts, and the rubric v2 accessories read when a verdict is given.</summary>
    private static OutfitFeedback Feedback(int score, string? accessoriesVerdict, params (string Category, string Verdict)[] items) => new()
    {
        Score = score,
        IntentMatch = 70,
        Headline = "Seeded",
        Items = items.Select((item, i) => new OutfitItem { Name = "piece " + i, Category = item.Category, Verdict = item.Verdict, Note = "" }).ToList(),
        Breakdown = accessoriesVerdict is null ? null : new ScoreBreakdown { Fit = 7, Color = 7, Accessories = 5 },
        Accessories = accessoriesVerdict is null ? null : new AccessoriesFeedback { Verdict = accessoriesVerdict }
    };

    private async Task SeedAsync(Guid userId, StyleIntent intent, int score, OutfitFeedback? feedback, DateTime at, string status = CheckStatus.Ok)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Checks.Add(new OutfitCheck
        {
            Id = Guid.NewGuid(), UserId = userId, Intent = intent, Language = "en", ImagePath = $"{userId}/{Guid.NewGuid():N}.jpg",
            Status = status, Score = status == CheckStatus.Ok ? score : null,
            FeedbackJson = feedback is null ? null : JsonSerializer.Serialize(feedback, AppJson.Options),
            PromptVersion = "v2", LatencyMs = 800, CreatedAt = at
        });
        await db.SaveChangesAsync();
    }

    private async Task SetStreakAsync(Guid userId, int streak)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.StreakCount = streak;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Insights_read_the_best_intent_the_weakest_piece_the_missing_accessories_and_the_streak()
    {
        var (client, id, _) = await _app.NewUserAsync("ins_math");
        var t0 = DateTime.UtcNow.AddHours(-6);
        // Date twice (8 and 6: 7.0 on average), Casual twice (5 and 7: 6.0), Office once at 9 (too few to count as best).
        await SeedAsync(id, StyleIntent.Date, 8, Feedback(8, "missing", ("shoes", "weak"), ("top", "works")), t0);
        await SeedAsync(id, StyleIntent.Date, 6, Feedback(6, "adds", ("shoes", "weak"), ("top", "weak")), t0.AddMinutes(10));
        await SeedAsync(id, StyleIntent.Office, 9, Feedback(9, "missing", ("bottom", "works")), t0.AddMinutes(20));
        await SeedAsync(id, StyleIntent.Casual, 5, Feedback(5, "neutral", ("top", "weak")), t0.AddMinutes(30));
        // A check from before rubric v2: items but no accessories read, so it counts for the pieces and not for the accessories.
        await SeedAsync(id, StyleIntent.Casual, 7, Feedback(7, null, ("shoes", "weak"), ("bottom", "neutral")), t0.AddMinutes(40));
        // A refusal is not a check that says anything.
        await SeedAsync(id, StyleIntent.Sport, 1, null, t0.AddMinutes(50), status: CheckStatus.NotOutfit);
        await SetStreakAsync(id, 3);

        var response = await client.GetAsync("/api/users/me/insights");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(5, dto.GetProperty("checks").GetInt32());
        Assert.Equal(7.0, dto.GetProperty("avgScore").GetDouble());
        Assert.Equal(9, dto.GetProperty("bestScore").GetInt32());
        Assert.Equal("Date", dto.GetProperty("bestIntent").GetString());
        // Shoes were weak in three of the five checks with items; tops in two.
        Assert.Equal("shoes", dto.GetProperty("weakestCategory").GetString());
        Assert.Equal(0.6, dto.GetProperty("weakestShare").GetDouble());
        // Four checks carry the accessories read; two of them had nothing on.
        Assert.Equal(0.5, dto.GetProperty("accessoriesMissingShare").GetDouble());
        Assert.Equal(3, dto.GetProperty("streak").GetInt32());
        Assert.Equal(
        [
            "Your date looks score highest, 7.0 on average.",
            "Shoes are the weak link in 60% of your looks.",
            "50% of your looks had no accessories. One piece finishes a look.",
            "3 days in a row. Keep it going."
        ], Lines(dto));
    }

    [Fact]
    public async Task Lines_come_in_the_accounts_language_and_the_most_checked_intent_stands_in_when_none_has_two()
    {
        var (client, id, _) = await _app.NewUserAsync("ins_he", language: "he");
        var t0 = DateTime.UtcNow.AddHours(-3);
        // Three intents once each: no intent qualifies by average, every count ties, so the higher average wins and
        // Office beats Party on the enum order. No items and no accessories read: those lines stay out, and so does a
        // one-day streak.
        await SeedAsync(id, StyleIntent.Party, 8, Feedback(8, null), t0);
        await SeedAsync(id, StyleIntent.Date, 5, Feedback(5, null), t0.AddMinutes(1));
        await SeedAsync(id, StyleIntent.Office, 8, Feedback(8, null), t0.AddMinutes(2));
        await SetStreakAsync(id, 1);

        var dto = await client.GetFromJsonAsync<JsonElement>("/api/users/me/insights");
        Assert.Equal(3, dto.GetProperty("checks").GetInt32());
        Assert.Equal(7.0, dto.GetProperty("avgScore").GetDouble());
        Assert.Equal(8, dto.GetProperty("bestScore").GetInt32());
        Assert.Equal("Office", dto.GetProperty("bestIntent").GetString());
        Assert.True(IsNull(dto, "weakestCategory"));
        Assert.True(IsNull(dto, "weakestShare"));
        Assert.True(IsNull(dto, "accessoriesMissingShare"));
        Assert.Equal(1, dto.GetProperty("streak").GetInt32());
        Assert.Equal(["הלוקים שלך בסגנון משרד מקבלים את הציון הכי גבוה: 8.0 בממוצע."], Lines(dto));

        // The account's language decides, not the request's.
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        var again = await client.GetFromJsonAsync<JsonElement>("/api/users/me/insights");
        Assert.Equal(Lines(dto), Lines(again));
    }

    [Fact]
    public async Task Under_three_checks_there_is_a_count_and_no_reading()
    {
        var (client, id, _) = await _app.NewUserAsync("ins_two");
        await SetStreakAsync(id, 2);
        var none = await client.GetFromJsonAsync<JsonElement>("/api/users/me/insights");
        Assert.Equal(0, none.GetProperty("checks").GetInt32());
        Assert.Empty(none.GetProperty("lines").EnumerateArray());

        var t0 = DateTime.UtcNow.AddHours(-1);
        await SeedAsync(id, StyleIntent.Date, 8, Feedback(8, "missing", ("shoes", "weak")), t0);
        await SeedAsync(id, StyleIntent.Date, 9, Feedback(9, "missing", ("shoes", "weak")), t0.AddMinutes(1));
        var two = await client.GetFromJsonAsync<JsonElement>("/api/users/me/insights");
        Assert.Equal(2, two.GetProperty("checks").GetInt32());
        Assert.True(IsNull(two, "avgScore"));
        Assert.True(IsNull(two, "bestScore"));
        Assert.True(IsNull(two, "bestIntent"));
        Assert.True(IsNull(two, "weakestCategory"));
        Assert.True(IsNull(two, "accessoriesMissingShare"));
        Assert.Equal(2, two.GetProperty("streak").GetInt32());
        Assert.Empty(two.GetProperty("lines").EnumerateArray());

        // The third check turns the page on, streak line included.
        await SeedAsync(id, StyleIntent.Office, 4, Feedback(4, "adds"), t0.AddMinutes(2));
        var three = await client.GetFromJsonAsync<JsonElement>("/api/users/me/insights");
        Assert.Equal(3, three.GetProperty("checks").GetInt32());
        Assert.Equal(7.0, three.GetProperty("avgScore").GetDouble());
        Assert.Equal("Date", three.GetProperty("bestIntent").GetString());
        Assert.Equal(
        [
            "Your date looks score highest, 8.5 on average.",
            "Shoes are the weak link in 100% of your looks.",
            "67% of your looks had no accessories. One piece finishes a look.",
            "2 days in a row. Keep it going."
        ], Lines(three));
    }

    [Fact]
    public async Task Only_the_newest_two_hundred_ok_checks_are_read()
    {
        var (client, id, _) = await _app.NewUserAsync("ins_window");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var t0 = DateTime.UtcNow.AddDays(-2);
            // Three old ones at 1, then two hundred at 8: the window drops the old ones and the average is a clean 8.
            db.Checks.AddRange(Enumerable.Range(0, 203).Select(i => new OutfitCheck
            {
                Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Casual, Language = "en", ImagePath = $"{id}/{i}.jpg",
                Status = CheckStatus.Ok, Score = i < 3 ? 1 : 8, PromptVersion = "v2", LatencyMs = 500, CreatedAt = t0.AddMinutes(i),
                FeedbackJson = JsonSerializer.Serialize(Feedback(i < 3 ? 1 : 8, null), AppJson.Options)
            }));
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<JsonElement>("/api/users/me/insights");
        Assert.Equal(InsightsEndpoints.Window, dto.GetProperty("checks").GetInt32());
        Assert.Equal(8.0, dto.GetProperty("avgScore").GetDouble());
        Assert.Equal(8, dto.GetProperty("bestScore").GetInt32());
        Assert.Equal("Casual", dto.GetProperty("bestIntent").GetString());
    }

    [Fact]
    public async Task Insights_are_the_callers_own_and_need_a_session()
    {
        var (owner, id, _) = await _app.NewUserAsync("ins_owner");
        var t0 = DateTime.UtcNow.AddHours(-1);
        for (var i = 0; i < 3; i++)
        {
            await SeedAsync(id, StyleIntent.Date, 9, Feedback(9, "adds"), t0.AddMinutes(i));
        }

        Assert.Equal(3, (await owner.GetFromJsonAsync<JsonElement>("/api/users/me/insights")).GetProperty("checks").GetInt32());

        // Someone else sees their own (empty) numbers, never the owner's.
        var (other, _, _) = await _app.NewUserAsync("ins_other");
        Assert.Equal(0, (await other.GetFromJsonAsync<JsonElement>("/api/users/me/insights")).GetProperty("checks").GetInt32());

        // No cookie, no numbers.
        var anonymous = await _app.NewClient().GetAsync("/api/users/me/insights");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Insights_need_pro_exactly_when_comparisons_do()
    {
        // The default: Plans:CompareNeedsPro is off and a free account reads its own insights.
        var (free, _, _) = await _app.NewUserAsync("ins_free");
        Assert.Equal(HttpStatusCode.OK, (await free.GetAsync("/api/users/me/insights")).StatusCode);

        // With the gate on, the same wall as "which one?": 403 in the account's language for a free account, the reading for Pro.
        using var gated = new ProGatedInsightsApp();
        var (client, id, handle) = await gated.NewUserAsync("ins_gated", language: "he");
        var refused = await client.GetAsync("/api/users/me/insights");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("זה לפרו.", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        Assert.Equal(AdminChange.Changed, await AdminSync.SetProAsync(gated.ConnectionString, handle, DateTime.UtcNow.AddDays(31)));
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/users/me/insights")).GetProperty("checks").GetInt32());

        // A lapsed Pro is refused again.
        using (var scope = gated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Id == id)).ProUntil = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users/me/insights")).StatusCode);
    }

    /// <summary>The test host with Plans:CompareNeedsPro on: comparisons and the insights are Pro's.</summary>
    private sealed class ProGatedInsightsApp : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Plans:CompareNeedsPro", "true");
        }
    }
}
