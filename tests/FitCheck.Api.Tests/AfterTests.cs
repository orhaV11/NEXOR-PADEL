using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>"After the tip": a look posted as the improvement on an earlier one, with the score before and after.</summary>
public class AfterTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public AfterTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static bool HasBefore(JsonElement post) => post.TryGetProperty("before", out var before) && before.ValueKind == JsonValueKind.Object;

    /// <summary>Check and post at a scripted score, so the before and the after differ.</summary>
    private async Task<Guid> PostAtAsync(HttpClient client, int score, Guid? beforePostId = null, string? caption = null)
    {
        _app.Vision.Handler = _ => Payloads.Ok(score: score);
        try
        {
            var checkId = await _app.CheckAsync(client);
            var response = await client.PostAsJsonAsync("/api/posts", new { checkId, caption, beforePostId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await Json(response)).GetProperty("id").GetGuid();
        }
        finally
        {
            _app.Vision.Handler = _ => Payloads.Ok();
        }
    }

    private async Task UpdatePostAsync(Guid postId, Action<Post> change)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var post = await db.Posts.SingleAsync(p => p.Id == postId);
        change(post);
        await db.SaveChangesAsync();
    }

    private async Task<Guid?> BeforeColumnAsync(Guid postId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Posts.Where(p => p.Id == postId).Select(p => p.BeforePostId).SingleAsync();
    }

    [Fact]
    public async Task An_after_the_tip_look_carries_the_before_score_and_photo_everywhere_the_card_is_read()
    {
        var (client, _, _) = await _app.NewUserAsync("after_a");
        var before = await PostAtAsync(client, score: 5, caption: "first try");

        _app.Vision.Handler = _ => Payloads.Ok(score: 8);
        JsonElement post;
        try
        {
            var checkId = await _app.CheckAsync(client);
            var response = await client.PostAsJsonAsync("/api/posts", new { checkId, caption = "took the tip", beforePostId = before });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            post = await Json(response);
        }
        finally
        {
            _app.Vision.Handler = _ => Payloads.Ok();
        }

        var strip = post.GetProperty("before");
        Assert.Equal(before, strip.GetProperty("postId").GetGuid());
        Assert.Equal(5, strip.GetProperty("score").GetInt32());
        Assert.Equal($"/api/posts/{before}/image", strip.GetProperty("imageUrl").GetString());
        Assert.Equal(8, post.GetProperty("score").GetInt32());
        var after = post.GetProperty("id").GetGuid();
        Assert.Equal(before, await BeforeColumnAsync(after));

        // The same strip on the look page, in the feed and on the profile, to anyone; the earlier look itself carries none.
        var anonymous = _app.NewClient();
        var page = await Json(await anonymous.GetAsync($"/api/posts/{after}"));
        Assert.Equal(5, page.GetProperty("before").GetProperty("score").GetInt32());
        var feed = await Json(await anonymous.GetAsync("/api/feed?tab=fresh&limit=30"));
        var inFeed = feed.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == after);
        Assert.Equal(before, inFeed.GetProperty("before").GetProperty("postId").GetGuid());
        var earlier = feed.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == before);
        Assert.False(HasBefore(earlier));
        var profile = await Json(await anonymous.GetAsync("/api/users/after_a/posts"));
        Assert.True(HasBefore(profile.GetProperty("items")[0]));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(strip.GetProperty("imageUrl").GetString()!)).StatusCode);
    }

    [Fact]
    public async Task The_before_look_must_be_a_visible_look_of_your_own_and_not_this_check()
    {
        var (mine, _, _) = await _app.NewUserAsync("after_v1");
        var (other, _, _) = await _app.NewUserAsync("after_v2");
        var own = await PostAtAsync(mine, score: 6);
        var theirs = await PostAtAsync(other, score: 6);

        async Task<HttpResponseMessage> PostWithBefore(Guid checkId, Guid beforePostId) =>
            await mine.PostAsJsonAsync("/api/posts", new { checkId, beforePostId });

        var check = await _app.CheckAsync(mine);
        var someoneElses = await PostWithBefore(check, theirs);
        Assert.Equal(HttpStatusCode.BadRequest, someoneElses.StatusCode);
        Assert.Equal("The earlier look has to be one of yours.", (await Json(someoneElses)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await PostWithBefore(check, Guid.NewGuid())).StatusCode);

        await UpdatePostAsync(own, p => p.Hidden = true);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostWithBefore(check, own)).StatusCode);
        await UpdatePostAsync(own, p => p.Hidden = false);

        // A look can never be its own before: the check that made the earlier look is refused as a before, not as a repeat.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ownCheck = await db.Posts.Where(p => p.Id == own).Select(p => p.CheckId).SingleAsync();
            Assert.Equal(HttpStatusCode.BadRequest, (await PostWithBefore(ownCheck, own)).StatusCode);
        }

        // Nothing above posted the check; a valid before still does, and the same check then cannot be posted again.
        var posted = await PostWithBefore(check, own);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        Assert.Equal(own, (await Json(posted)).GetProperty("before").GetProperty("postId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await PostWithBefore(check, own)).StatusCode);

        // In Hebrew, the refusal speaks Hebrew.
        var (hebrew, _, _) = await _app.NewUserAsync("after_v3", language: "he");
        var hebrewCheck = await _app.CheckAsync(hebrew);
        var refused = await hebrew.PostAsJsonAsync("/api/posts", new { checkId = hebrewCheck, beforePostId = own });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("הלוק הקודם צריך להיות שלך.", (await Json(refused)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Deleting_the_before_look_clears_the_link_and_the_card_copes()
    {
        var (client, _, _) = await _app.NewUserAsync("after_d");
        var before = await PostAtAsync(client, score: 4);
        var after = await PostAtAsync(client, score: 7, beforePostId: before);
        Assert.True(HasBefore(await Json(await client.GetAsync($"/api/posts/{after}"))));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/posts/{before}")).StatusCode);

        Assert.Null(await BeforeColumnAsync(after));
        var page = await Json(await _app.NewClient().GetAsync($"/api/posts/{after}"));
        Assert.False(HasBefore(page));
        Assert.Equal(7, page.GetProperty("score").GetInt32());
        var profile = await Json(await client.GetAsync("/api/users/after_d/posts"));
        Assert.Single(profile.GetProperty("items").EnumerateArray());
        Assert.False(HasBefore(profile.GetProperty("items")[0]));
    }

    [Fact]
    public async Task A_before_look_under_review_leaves_the_strip_off_until_it_is_back()
    {
        var (client, _, _) = await _app.NewUserAsync("after_h");
        var before = await PostAtAsync(client, score: 5);
        var after = await PostAtAsync(client, score: 6, beforePostId: before);

        await UpdatePostAsync(before, p => p.Hidden = true);
        Assert.False(HasBefore(await Json(await client.GetAsync($"/api/posts/{after}"))));
        Assert.Equal(before, await BeforeColumnAsync(after));

        await UpdatePostAsync(before, p => p.Hidden = false);
        Assert.True(HasBefore(await Json(await client.GetAsync($"/api/posts/{after}"))));
    }
}
