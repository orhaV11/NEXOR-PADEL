using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

/// <summary>Scripted rubric v2 answers: what <see cref="Payloads.Ok"/> sends plus the breakdown and the accessories read.</summary>
public static class V2Payloads
{
    public static JsonElement Ok(
        int fit = 7, int color = 8, int accessories = 4, string verdict = "missing", string present = "[]",
        string addOne = "A thin black leather belt.", string status = "ok") => Payloads.Parse($$"""
        {
          "status": "{{status}}",
          "score": 7,
          "intent_match": 72,
          "headline": "Clean casual with one weak link",
          "vibe": "relaxed weekend",
          "items": [
            { "name": "White tee", "category": "top", "verdict": "works", "note": "Crisp and simple." },
            { "name": "Running shoes", "category": "shoes", "verdict": "weak", "note": "Too sporty for the rest." }
          ],
          "working": ["The palette is tight"],
          "one_tip": "Swap the running shoes for plain white leather sneakers.",
          "message": "This looks like a photo of a desk.",
          "breakdown": { "fit": {{fit}}, "color": {{color}}, "accessories": {{accessories}} },
          "accessories": { "verdict": "{{verdict}}", "present": {{present}}, "note": "Nothing on, so the look stops at the clothes.", "add_one": "{{addOne}}" }
        }
        """);
}

/// <summary>The rubric v2 fields end to end: a check carries them, a refusal does not, a look carries the sub-scores, an old check carries nothing.</summary>
public class StylistV2Tests : IClassFixture<StylistV2Tests.V2App>
{
    public sealed class V2App : TestApp;

    private readonly V2App _app;

    public StylistV2Tests(V2App app) => _app = app;

    [Fact]
    public async Task A_check_returns_and_stores_the_breakdown_and_the_accessories_read()
    {
        _app.Vision.Handler = _ => V2Payloads.Ok(verdict: "adds", present: "[\"gold hoops\", \"black leather belt\"]", addOne: "");
        var (client, _, _) = await _app.NewUserAsync("v2_check");

        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), "Date"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = check.GetProperty("id").GetGuid();
        Assert.Equal("ok", check.GetProperty("status").GetString());

        var feedback = check.GetProperty("feedback");
        var breakdown = feedback.GetProperty("breakdown");
        Assert.Equal(7, breakdown.GetProperty("fit").GetInt32());
        Assert.Equal(8, breakdown.GetProperty("color").GetInt32());
        Assert.Equal(4, breakdown.GetProperty("accessories").GetInt32());
        var accessories = feedback.GetProperty("accessories");
        Assert.Equal("adds", accessories.GetProperty("verdict").GetString());
        Assert.Equal(["gold hoops", "black leather belt"], accessories.GetProperty("present").EnumerateArray().Select(p => p.GetString()).ToList());
        Assert.Equal("Nothing on, so the look stops at the clothes.", accessories.GetProperty("note").GetString());
        Assert.Equal("", accessories.GetProperty("addOne").GetString());

        // Stored with the check, read back the same way, stamped with the rubric version that produced it.
        var again = await client.GetFromJsonAsync<JsonElement>($"/api/checks/{id}");
        Assert.Equal(4, again.GetProperty("feedback").GetProperty("breakdown").GetProperty("accessories").GetInt32());
        Assert.Equal("adds", again.GetProperty("feedback").GetProperty("accessories").GetProperty("verdict").GetString());
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Checks.SingleAsync(c => c.Id == id);
        Assert.Equal("v3", row.PromptVersion);
        Assert.Contains("\"breakdown\":{\"fit\":7,\"color\":8,\"accessories\":4}", row.FeedbackJson);
    }

    [Fact]
    public async Task Sloppy_v2_values_are_normalised_on_the_way_in()
    {
        _app.Vision.Handler = _ => Payloads.Parse("""
            { "status": "ok", "score": 6, "intent_match": 50, "headline": "h", "vibe": "v", "items": [], "working": [], "one_tip": "t",
              "breakdown": { "fit": "12", "color": 0, "accessories": 5.6 },
              "accessories": { "verdict": "MISSING", "present": ["", "belt"], "note": "n", "add_one": "a" } }
            """);
        var (client, _, _) = await _app.NewUserAsync("v2_sloppy");

        var check = await (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).Content.ReadFromJsonAsync<JsonElement>();

        var breakdown = check.GetProperty("feedback").GetProperty("breakdown");
        Assert.Equal(10, breakdown.GetProperty("fit").GetInt32());
        Assert.Equal(1, breakdown.GetProperty("color").GetInt32());
        Assert.Equal(6, breakdown.GetProperty("accessories").GetInt32());
        var accessories = check.GetProperty("feedback").GetProperty("accessories");
        Assert.Equal("missing", accessories.GetProperty("verdict").GetString());
        Assert.Equal(["belt"], accessories.GetProperty("present").EnumerateArray().Select(p => p.GetString()).ToList());
    }

    [Fact]
    public async Task Not_outfit_and_rejected_checks_carry_no_v2_fields()
    {
        var (client, _, _) = await _app.NewUserAsync("v2_refused");

        _app.Vision.Handler = _ => V2Payloads.Ok(status: "not_outfit");
        var notOutfit = await (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_outfit", notOutfit.GetProperty("status").GetString());
        var feedback = notOutfit.GetProperty("feedback");
        Assert.Equal("This looks like a photo of a desk.", feedback.GetProperty("message").GetString());
        Assert.False(feedback.TryGetProperty("breakdown", out _));
        Assert.False(feedback.TryGetProperty("accessories", out _));

        _app.Vision.Handler = _ => V2Payloads.Ok(status: "rejected");
        var rejected = await (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rejected", rejected.GetProperty("status").GetString());
        feedback = rejected.GetProperty("feedback");
        Assert.False(feedback.TryGetProperty("breakdown", out _));
        Assert.False(feedback.TryGetProperty("accessories", out _));
        Assert.DoesNotContain("desk", await (await client.GetAsync($"/api/checks/{rejected.GetProperty("id").GetGuid()}")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Posting_copies_the_breakdown_onto_the_look_and_keeps_the_accessories_read_private()
    {
        _app.Vision.Handler = _ => V2Payloads.Ok(fit: 6, color: 9, accessories: 3);
        var (client, _, _) = await _app.NewUserAsync("v2_post");
        var checkId = await _app.CheckAsync(client);

        var post = await _app.PostAsync(client, checkId, caption: "belt next time");
        var postId = post.GetProperty("id").GetGuid();
        var breakdown = post.GetProperty("breakdown");
        Assert.Equal(6, breakdown.GetProperty("fit").GetInt32());
        Assert.Equal(9, breakdown.GetProperty("color").GetInt32());
        Assert.Equal(3, breakdown.GetProperty("accessories").GetInt32());
        Assert.False(post.TryGetProperty("accessories", out _));

        // Public on the look page and in the feed, for a visitor too; the columns are the source.
        var visitor = _app.NewClient();
        var fetched = await visitor.GetFromJsonAsync<JsonElement>($"/api/posts/{postId}");
        Assert.Equal(3, fetched.GetProperty("breakdown").GetProperty("accessories").GetInt32());
        Assert.False(fetched.TryGetProperty("accessories", out _));
        var feed = await visitor.GetFromJsonAsync<JsonElement>("/api/feed");
        var inFeed = feed.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == postId);
        Assert.Equal(6, inFeed.GetProperty("breakdown").GetProperty("fit").GetInt32());

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Posts.SingleAsync(p => p.Id == postId);
        Assert.Equal((6, 9, 3), (row.FitScore, row.ColorScore, row.AccessoriesScore));
    }

    [Fact]
    public async Task A_check_stored_before_v2_shows_no_breakdown_and_posts_without_one()
    {
        var (client, userId, _) = await _app.NewUserAsync("v2_legacy");
        var checkId = Guid.NewGuid();
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Exactly what a v1 check left behind: the feedback document without the two v2 objects.
            var v1 = OutfitAnalyzer.MapToolInput(Payloads.Ok(score: 6));
            Assert.Null(v1.Breakdown);
            db.Checks.Add(new OutfitCheck
            {
                Id = checkId, UserId = userId, Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Score = 6,
                ImagePath = "legacy/photo.jpg", FeedbackJson = JsonSerializer.Serialize(v1, AppJson.Options), PromptVersion = "v1",
                LatencyMs = 1200, CreatedAt = DateTime.UtcNow.AddDays(-30)
            });
            await db.SaveChangesAsync();
        }

        var check = await client.GetFromJsonAsync<JsonElement>($"/api/checks/{checkId}");
        Assert.Equal(6, check.GetProperty("score").GetInt32());
        Assert.Equal("Clean casual with one weak link", check.GetProperty("feedback").GetProperty("headline").GetString());
        Assert.False(check.GetProperty("feedback").TryGetProperty("breakdown", out _));
        Assert.False(check.GetProperty("feedback").TryGetProperty("accessories", out _));

        var post = await _app.PostAsync(client, checkId);
        Assert.False(post.TryGetProperty("breakdown", out _));
        var fetched = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{post.GetProperty("id").GetGuid()}");
        Assert.False(fetched.TryGetProperty("breakdown", out _));
    }

    [Fact]
    public async Task The_stylist_is_asked_for_the_v2_fields_in_the_wearers_language()
    {
        _app.Vision.Handler = _ => V2Payloads.Ok();
        var (client, _, _) = await _app.NewUserAsync("v2_prompt", language: "he");
        _app.Vision.Requests.Clear();

        await _app.CheckAsync(client, "Party", "he");

        var request = Assert.Single(_app.Vision.Requests);
        Assert.Contains("ACCESSORIES", request.SystemPrompt);
        Assert.Contains("accessories.add_one) in Hebrew (he)", request.SystemPrompt);
        var required = request.Tool.InputSchema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("breakdown", required);
        Assert.Contains("accessories", required);
    }
}
