using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

public class ChallengeTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public ChallengeTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static bool IsNull(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private async Task<Guid> OpenChallengeAsync(HttpClient brand, string intent = "Office", string title = "Monday looks", double days = 3)
    {
        var response = await brand.PostAsJsonAsync("/api/challenges", new
        {
            title, brief = "Show us your sharpest office fit.", intent, prize = "A linen shirt of your choice",
            prizeUrl = "https://shop.example/linen", endsAt = DateTime.UtcNow.AddDays(days)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Only_brands_open_challenges_and_the_form_is_validated()
    {
        var (person, _, _) = await _app.NewUserAsync("ch_person");
        var (brand, _, _) = await _app.NewUserAsync("ch_brand0", accountType: "Brand");
        var body = new { title = "T", brief = "B", intent = "Office", prize = "P", endsAt = DateTime.UtcNow.AddDays(1) };

        Assert.Equal(HttpStatusCode.Forbidden, (await person.PostAsJsonAsync("/api/challenges", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PostAsJsonAsync("/api/challenges", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await brand.PostAsJsonAsync("/api/challenges", body)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await brand.PostAsJsonAsync("/api/challenges", body with { endsAt = DateTime.UtcNow.AddMinutes(10) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await brand.PostAsJsonAsync("/api/challenges", body with { endsAt = DateTime.UtcNow.AddDays(61) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await brand.PostAsJsonAsync("/api/challenges", body with { intent = "Wedding" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await brand.PostAsJsonAsync("/api/challenges", body with { title = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await brand.PostAsJsonAsync("/api/challenges", new { title = "T", brief = "B", intent = "Office", prize = "P", prizeUrl = "http://x", endsAt = DateTime.UtcNow.AddDays(1) })).StatusCode);
    }

    [Fact]
    public async Task Entering_requires_the_right_intent_and_one_entry_per_person()
    {
        var (brand, _, _) = await _app.NewUserAsync("ch_brand1", accountType: "Brand");
        var (entrant, _, _) = await _app.NewUserAsync("ch_entrant1");
        var challengeId = await OpenChallengeAsync(brand);

        var wrongIntent = await _app.CheckAsync(entrant, intent: "Party");
        var response = await entrant.PostAsJsonAsync("/api/posts", new { checkId = wrongIntent, challengeId });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Office", (await Json(response)).GetProperty("error").GetString());

        var entryId = await _app.CheckAndPostAsync(entrant, intent: "Office", challengeId: challengeId);
        var second = await _app.CheckAsync(entrant, intent: "Office");
        Assert.Equal(HttpStatusCode.Conflict, (await entrant.PostAsJsonAsync("/api/posts", new { checkId = second, challengeId })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await entrant.PostAsJsonAsync("/api/posts", new { checkId = second, challengeId = Guid.NewGuid() })).StatusCode);

        var brandNotifications = await brand.GetFromJsonAsync<JsonElement>("/api/notifications");
        var entry = brandNotifications.GetProperty("items")[0];
        Assert.Equal("entry", entry.GetProperty("type").GetString());
        Assert.Equal("ch_entrant1", entry.GetProperty("actorHandle").GetString());
        Assert.Equal(challengeId, entry.GetProperty("challengeId").GetGuid());

        var list = await entrant.GetFromJsonAsync<JsonElement>("/api/challenges");
        var mine = list.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == challengeId);
        Assert.Equal(1, mine.GetProperty("entries").GetInt32());
        Assert.True(mine.GetProperty("isOpen").GetBoolean());
        Assert.True(mine.GetProperty("viewer").GetProperty("hasEntered").GetBoolean());
        Assert.Equal(entryId, mine.GetProperty("viewer").GetProperty("myEntryId").GetGuid());
        Assert.Equal(entryId, mine.GetProperty("top")[0].GetProperty("id").GetGuid());
        Assert.Equal("Monday looks", mine.GetProperty("top")[0].GetProperty("challengeTitle").GetString());
        Assert.Equal("ch_brand1", mine.GetProperty("brand").GetProperty("handle").GetString());
    }

    [Fact]
    public async Task Voting_one_per_person_changeable_never_for_yourself()
    {
        var (brand, _, _) = await _app.NewUserAsync("ch_brand2", accountType: "Brand");
        var (a, _, _) = await _app.NewUserAsync("ch_a2");
        var (b, _, _) = await _app.NewUserAsync("ch_b2");
        var (voter, _, _) = await _app.NewUserAsync("ch_voter2");
        var challengeId = await OpenChallengeAsync(brand);
        var entryA = await _app.CheckAndPostAsync(a, intent: "Office", challengeId: challengeId);
        var entryB = await _app.CheckAndPostAsync(b, intent: "Office", challengeId: challengeId);
        var plain = await _app.CheckAndPostAsync(voter, intent: "Office");

        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryA })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await voter.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = plain })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryA })).StatusCode);

        var vote = await Json(await voter.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryA }));
        Assert.Equal(entryA, vote.GetProperty("votedPostId").GetGuid());
        Assert.Equal(1, vote.GetProperty("votes").GetInt32());

        var moved = await Json(await voter.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryB }));
        Assert.Equal(entryB, moved.GetProperty("votedPostId").GetGuid());
        Assert.Equal(1, moved.GetProperty("votes").GetInt32());

        var detail = await voter.GetFromJsonAsync<JsonElement>($"/api/challenges/{challengeId}");
        Assert.Equal(entryB, detail.GetProperty("challenge").GetProperty("viewer").GetProperty("votedPostId").GetGuid());
        Assert.Equal(1, detail.GetProperty("challenge").GetProperty("votes").GetInt32());
        Assert.Equal(entryB, detail.GetProperty("entriesByVotes")[0].GetProperty("id").GetGuid());
        Assert.Equal(1, detail.GetProperty("entriesByVotes")[0].GetProperty("votes").GetInt32());
        Assert.Equal(0, detail.GetProperty("entriesByVotes")[1].GetProperty("votes").GetInt32());

        var aNotifications = await a.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Single(aNotifications.GetProperty("items").EnumerateArray().Where(n => n.GetProperty("type").GetString() == "vote"));

        var retracted = await Json(await voter.DeleteAsync($"/api/challenges/{challengeId}/vote"));
        Assert.True(IsNull(retracted, "votedPostId"));
        Assert.Equal(0, retracted.GetProperty("votes").GetInt32());
    }

    [Fact]
    public async Task Winner_is_fixed_on_the_first_read_after_the_end_with_notifications_once()
    {
        var (brand, brandId, _) = await _app.NewUserAsync("ch_brand3", accountType: "Brand");
        var (a, aId, _) = await _app.NewUserAsync("ch_a3");
        var (b, bId, _) = await _app.NewUserAsync("ch_b3");
        var (v1, _, _) = await _app.NewUserAsync("ch_v3a");
        var (v2, _, _) = await _app.NewUserAsync("ch_v3b");
        var challengeId = await OpenChallengeAsync(brand, title: "Ends soon");
        var entryA = await _app.CheckAndPostAsync(a, intent: "Office", challengeId: challengeId);
        var entryB = await _app.CheckAndPostAsync(b, intent: "Office", challengeId: challengeId);
        await v1.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryB });
        await v2.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryB });
        await a.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryB });

        // Time travel: the challenge ended an hour ago.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Challenges.Where(c => c.Id == challengeId).ExecuteUpdateAsync(s => s.SetProperty(c => c.EndsAt, DateTime.UtcNow.AddHours(-1)));
        }

        var detail = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/challenges/{challengeId}");
        Assert.False(detail.GetProperty("challenge").GetProperty("isOpen").GetBoolean());
        Assert.Equal(entryB, detail.GetProperty("challenge").GetProperty("winnerPostId").GetGuid());
        Assert.Equal(entryB, detail.GetProperty("winner").GetProperty("id").GetGuid());
        Assert.Equal(3, detail.GetProperty("winner").GetProperty("votes").GetInt32());

        // Reading again changes nothing and sends nothing twice.
        await _app.NewClient().GetAsync($"/api/challenges/{challengeId}");
        await _app.NewClient().GetAsync("/api/challenges?state=ended");
        var bNotifications = await b.GetFromJsonAsync<JsonElement>("/api/notifications");
        var won = bNotifications.GetProperty("items").EnumerateArray().Where(n => n.GetProperty("type").GetString() == "won").ToList();
        Assert.Single(won);
        Assert.Equal("ch_brand3", won[0].GetProperty("actorHandle").GetString());
        var brandNotifications = await brand.GetFromJsonAsync<JsonElement>("/api/notifications");
        var ended = brandNotifications.GetProperty("items").EnumerateArray().Where(n => n.GetProperty("type").GetString() == "ended").ToList();
        Assert.Single(ended);
        Assert.Equal("ch_b3", ended[0].GetProperty("actorHandle").GetString());
        Assert.Empty((await a.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items").EnumerateArray().Where(n => n.GetProperty("type").GetString() == "won"));

        // Nothing moves after the end.
        Assert.Equal(HttpStatusCode.BadRequest, (await v1.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryA })).StatusCode);
        var late = await _app.CheckAsync(v1, intent: "Office");
        Assert.Equal(HttpStatusCode.BadRequest, (await v1.PostAsJsonAsync("/api/posts", new { checkId = late, challengeId })).StatusCode);

        var endedList = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/challenges?state=ended");
        Assert.Contains(endedList.EnumerateArray(), c => c.GetProperty("id").GetGuid() == challengeId);
        var openList = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/challenges");
        Assert.DoesNotContain(openList.EnumerateArray(), c => c.GetProperty("id").GetGuid() == challengeId);
    }

    [Fact]
    public async Task Ties_go_to_the_earlier_entry_and_an_empty_challenge_has_no_winner()
    {
        var (brand, _, _) = await _app.NewUserAsync("ch_brand4", accountType: "Brand");
        var (a, _, _) = await _app.NewUserAsync("ch_a4");
        var (b, _, _) = await _app.NewUserAsync("ch_b4");
        var tie = await OpenChallengeAsync(brand, title: "Tie");
        var empty = await OpenChallengeAsync(brand, title: "Empty");
        var entryA = await _app.CheckAndPostAsync(a, intent: "Office", challengeId: tie);
        await _app.CheckAndPostAsync(b, intent: "Office", challengeId: tie);

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Challenges.Where(c => c.Id == tie || c.Id == empty).ExecuteUpdateAsync(s => s.SetProperty(c => c.EndsAt, DateTime.UtcNow.AddMinutes(-5)));
        }

        var tieDetail = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/challenges/{tie}");
        Assert.Equal(entryA, tieDetail.GetProperty("challenge").GetProperty("winnerPostId").GetGuid());

        var emptyDetail = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/challenges/{empty}");
        Assert.True(IsNull(emptyDetail.GetProperty("challenge"), "winnerPostId"));
        Assert.True(IsNull(emptyDetail, "winner"));
        var brandNotifications = await brand.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(2, brandNotifications.GetProperty("items").EnumerateArray().Count(n => n.GetProperty("type").GetString() == "ended"));
    }

    [Fact]
    public async Task Deleting_a_brand_keeps_entries_as_plain_posts()
    {
        var (brand, _, _) = await _app.NewUserAsync("ch_brand5", accountType: "Brand");
        var (a, _, _) = await _app.NewUserAsync("ch_a5");
        var challengeId = await OpenChallengeAsync(brand);
        var entry = await _app.CheckAndPostAsync(a, intent: "Office", challengeId: challengeId);

        Assert.Equal(HttpStatusCode.NoContent, (await brand.DeleteAsync("/api/users/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/challenges/{challengeId}")).StatusCode);
        var post = await a.GetFromJsonAsync<JsonElement>($"/api/posts/{entry}");
        Assert.True(IsNull(post, "challengeId"));
    }
}
