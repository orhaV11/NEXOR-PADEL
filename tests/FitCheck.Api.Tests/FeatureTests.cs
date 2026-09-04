using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

/// <summary>Caption parsing at post time, brand-featured looks, and the rows both leave behind.</summary>
public class FeatureTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public FeatureTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static bool IsNull(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static async Task<string?> ErrorOf(HttpResponseMessage response) => (await Json(response)).GetProperty("error").GetString();

    private static string? FeaturedBy(JsonElement element) => element.GetProperty("featuredBy").GetProperty("handle").GetString();

    private static bool IsType(JsonElement notification, string type) => notification.GetProperty("type").GetString() == type;

    private static async Task<JsonElement> NotificationsOf(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items");

    /// <summary>Avatar upload lives in the users area; a ref only needs the two columns, so they are set directly.</summary>
    private async Task GiveAvatarAsync(Guid userId, int version)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(userId);
        user!.AvatarPath = $"{userId}/avatar.jpg";
        user.AvatarVersion = version;
        await db.SaveChangesAsync();
    }

    private async Task HideAsync(Guid postId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Posts.Where(p => p.Id == postId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Hidden, true));
    }

    private async Task<Guid> OpenChallengeAsync(HttpClient brand, string intent = "Office")
    {
        var response = await brand.PostAsJsonAsync("/api/challenges", new
        {
            title = "Featured looks", brief = "Show us your sharpest fit.", intent, prize = "A shirt", endsAt = DateTime.UtcNow.AddDays(3)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Posting_parses_tags_and_mentions_and_notifies_the_mentioned_brand_but_not_the_author()
    {
        var (brand, brandId, _) = await _app.NewUserAsync("ft_brand", accountType: "Brand", displayName: "Nexor");
        await GiveAvatarAsync(brandId, 3);
        var (author, _, _) = await _app.NewUserAsync("ft_author");
        var checkId = await _app.CheckAsync(author);

        var post = await _app.PostAsync(author, checkId, caption: "#summer #Summer @ft_brand @nobody @ft_author, day one");
        var postId = post.GetProperty("id").GetGuid();

        Assert.Equal(new[] { "summer" }, post.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToList());
        var mention = Assert.Single(post.GetProperty("mentions").EnumerateArray());
        Assert.Equal("ft_brand", mention.GetProperty("handle").GetString());
        Assert.Equal("Nexor", mention.GetProperty("name").GetString());
        Assert.Equal("Brand", mention.GetProperty("accountType").GetString());
        Assert.Equal("/api/users/ft_brand/avatar?v=3", mention.GetProperty("avatarUrl").GetString());
        Assert.True(IsNull(post, "featuredBy"));
        Assert.True(IsNull(post.GetProperty("user"), "avatarUrl"));

        // The same shape wherever a post is read.
        var view = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}");
        Assert.Equal(new[] { "summer" }, view.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToList());
        Assert.Equal("ft_brand", Assert.Single(view.GetProperty("mentions").EnumerateArray()).GetProperty("handle").GetString());
        Assert.True(IsNull(view, "featuredBy"));

        var notification = Assert.Single((await NotificationsOf(brand)).EnumerateArray(), n => IsType(n, "mention"));
        Assert.Equal("ft_author", notification.GetProperty("actorHandle").GetString());
        Assert.Equal(postId, notification.GetProperty("postId").GetGuid());
        Assert.True(IsNull(notification, "challengeId"));
        Assert.DoesNotContain((await NotificationsOf(author)).EnumerateArray(), n => IsType(n, "mention"));

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(new[] { "summer" }, await db.PostTags.Where(t => t.PostId == postId).Select(t => t.Tag).ToListAsync());
        Assert.Equal(new[] { brandId }, await db.PostMentions.Where(m => m.PostId == postId).Select(m => m.UserId).ToListAsync());
    }

    [Fact]
    public async Task A_mentioned_brand_features_the_look_once_and_can_undo_it()
    {
        var (brand, brandId, _) = await _app.NewUserAsync("ft_fbrand", accountType: "Brand", displayName: "Feature Co");
        await GiveAvatarAsync(brandId, 1);
        var (author, _, _) = await _app.NewUserAsync("ft_fauthor");
        var postId = await _app.CheckAndPostAsync(author, caption: "wearing @ft_fbrand");

        var featured = await brand.PostAsync($"/api/posts/{postId}/feature", null);
        Assert.Equal(HttpStatusCode.OK, featured.StatusCode);
        var state = await Json(featured);
        Assert.Equal("ft_fbrand", FeaturedBy(state));
        Assert.Equal("Feature Co", state.GetProperty("featuredBy").GetProperty("name").GetString());
        Assert.Equal("/api/users/ft_fbrand/avatar?v=1", state.GetProperty("featuredBy").GetProperty("avatarUrl").GetString());

        // A second tap gets the same answer, and the author hears about it once.
        var again = await brand.PostAsync($"/api/posts/{postId}/feature", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("ft_fbrand", FeaturedBy(await Json(again)));

        var notification = Assert.Single((await NotificationsOf(author)).EnumerateArray(), n => IsType(n, "featured"));
        Assert.Equal("ft_fbrand", notification.GetProperty("actorHandle").GetString());
        Assert.Equal("Feature Co", notification.GetProperty("actorName").GetString());
        Assert.Equal(postId, notification.GetProperty("postId").GetGuid());

        Assert.Equal("ft_fbrand", FeaturedBy(await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}")));
        var feed = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/feed?limit=30");
        var card = Assert.Single(feed.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetGuid() == postId);
        Assert.Equal("ft_fbrand", FeaturedBy(card));

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Posts.AsNoTracking().FirstAsync(p => p.Id == postId);
            Assert.Equal(brandId, row.FeaturedByBrandId);
            Assert.NotNull(row.FeaturedAt);
        }

        var cleared = await brand.DeleteAsync($"/api/posts/{postId}/feature");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.True(IsNull(await Json(cleared), "featuredBy"));
        Assert.True(IsNull(await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}"), "featuredBy"));
        // Undoing twice is not an error: the look is in the state the brand asked for.
        Assert.Equal(HttpStatusCode.OK, (await brand.DeleteAsync($"/api/posts/{postId}/feature")).StatusCode);

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Posts.AsNoTracking().FirstAsync(p => p.Id == postId);
            Assert.Null(row.FeaturedByBrandId);
            Assert.Null(row.FeaturedAt);
        }
    }

    [Fact]
    public async Task Featuring_needs_a_mention_a_brand_account_and_a_look_nobody_else_holds()
    {
        var (author, _, _) = await _app.NewUserAsync("ft_rauthor");
        var (first, _, _) = await _app.NewUserAsync("ft_rb1", accountType: "Brand");
        var (second, _, _) = await _app.NewUserAsync("ft_rb2", accountType: "Brand");
        var (outsider, _, _) = await _app.NewUserAsync("ft_rb3", accountType: "Brand");
        var (person, _, _) = await _app.NewUserAsync("ft_rperson");
        var postId = await _app.CheckAndPostAsync(author, caption: "@ft_rb1 and @ft_rb2 made this");

        var notAllowed = await outsider.PostAsync($"/api/posts/{postId}/feature", null);
        Assert.Equal(HttpStatusCode.BadRequest, notAllowed.StatusCode);
        Assert.Equal("You can feature a look when it mentions your brand or entered one of your challenges.", await ErrorOf(notAllowed));
        Assert.Equal(HttpStatusCode.Forbidden, (await person.PostAsync($"/api/posts/{postId}/feature", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PostAsync($"/api/posts/{postId}/feature", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await first.PostAsync($"/api/posts/{Guid.NewGuid()}/feature", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await first.PostAsync($"/api/posts/{postId}/feature", null)).StatusCode);
        var taken = await second.PostAsync($"/api/posts/{postId}/feature", null);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("Another brand featured this look first.", await ErrorOf(taken));

        var notYours = await second.DeleteAsync($"/api/posts/{postId}/feature");
        Assert.Equal(HttpStatusCode.Forbidden, notYours.StatusCode);
        Assert.Equal("Only the brand that featured this look can undo it.", await ErrorOf(notYours));
        Assert.Equal(HttpStatusCode.Forbidden, (await person.DeleteAsync($"/api/posts/{postId}/feature")).StatusCode);
        Assert.Equal("ft_rb1", FeaturedBy(await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}")));

        // Once the first brand lets go, the other mentioned brand can take it.
        Assert.Equal(HttpStatusCode.OK, (await first.DeleteAsync($"/api/posts/{postId}/feature")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsync($"/api/posts/{postId}/feature", null)).StatusCode);
        Assert.Equal("ft_rb2", FeaturedBy(await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}")));

        var featuredBy = (await NotificationsOf(author)).EnumerateArray().Where(n => IsType(n, "featured"))
            .Select(n => n.GetProperty("actorHandle").GetString()).OrderBy(h => h, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "ft_rb1", "ft_rb2" }, featuredBy);

        // A look under review is not there to be featured.
        var hidden = await _app.CheckAndPostAsync(author, caption: "@ft_rb1 again");
        await HideAsync(hidden);
        Assert.Equal(HttpStatusCode.NotFound, (await first.PostAsync($"/api/posts/{hidden}/feature", null)).StatusCode);
    }

    [Fact]
    public async Task An_entry_in_the_brands_own_challenge_can_be_featured_by_that_brand_only()
    {
        var (brand, _, _) = await _app.NewUserAsync("ft_cbrand", accountType: "Brand");
        var (other, _, _) = await _app.NewUserAsync("ft_cother", accountType: "Brand");
        var (entrant, _, _) = await _app.NewUserAsync("ft_centrant");
        var challengeId = await OpenChallengeAsync(brand);
        var entryId = await _app.CheckAndPostAsync(entrant, intent: "Office", challengeId: challengeId, caption: "my entry, no mentions");

        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsync($"/api/posts/{entryId}/feature", null)).StatusCode);
        var featured = await brand.PostAsync($"/api/posts/{entryId}/feature", null);
        Assert.Equal(HttpStatusCode.OK, featured.StatusCode);
        Assert.Equal("ft_cbrand", FeaturedBy(await Json(featured)));

        var detail = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/challenges/{challengeId}");
        var entry = Assert.Single(detail.GetProperty("entriesByVotes").EnumerateArray());
        Assert.Equal("ft_cbrand", FeaturedBy(entry));

        var notification = Assert.Single((await NotificationsOf(entrant)).EnumerateArray(), n => IsType(n, "featured"));
        Assert.Equal("ft_cbrand", notification.GetProperty("actorHandle").GetString());
        Assert.Equal(entryId, notification.GetProperty("postId").GetGuid());
    }

    [Fact]
    public async Task Deleting_a_post_takes_its_tags_mentions_and_their_activity_with_it()
    {
        var (brand, _, _) = await _app.NewUserAsync("ft_dbrand", accountType: "Brand");
        var (author, _, _) = await _app.NewUserAsync("ft_dauthor");
        var postId = await _app.CheckAndPostAsync(author, caption: "#gone @ft_dbrand");
        Assert.Equal(HttpStatusCode.OK, (await brand.PostAsync($"/api/posts/{postId}/feature", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await author.DeleteAsync($"/api/posts/{postId}")).StatusCode);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.PostTags.AnyAsync(t => t.PostId == postId));
        Assert.False(await db.PostMentions.AnyAsync(m => m.PostId == postId));
        Assert.False(await db.Notifications.AnyAsync(n => n.PostId == postId));
        Assert.DoesNotContain((await NotificationsOf(brand)).EnumerateArray(), n => IsType(n, "mention"));
        Assert.DoesNotContain((await NotificationsOf(author)).EnumerateArray(), n => IsType(n, "featured"));
    }
}

/// <summary>Own fixture: metrics are global, so these counts must not share a database with the other feature tests.</summary>
public class FeatureMetricsTests : IClassFixture<FeatureMetricsTests.MetricsApp>
{
    public sealed class MetricsApp : TestApp;

    private readonly MetricsApp _app;

    public FeatureMetricsTests(MetricsApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    [Fact]
    public async Task Metrics_count_mentions_and_featured_looks_on_visible_posts_only()
    {
        var (brand, _, _) = await _app.NewUserAsync("mb_brand", accountType: "Brand");
        var (a, _, _) = await _app.NewUserAsync("mb_a");
        var (b, _, _) = await _app.NewUserAsync("mb_b");
        var shown = await _app.CheckAndPostAsync(a, caption: "@mb_brand @mb_b #two");
        await _app.CheckAndPostAsync(b, caption: "nothing to count here");
        var hidden = await _app.CheckAndPostAsync(b, caption: "@mb_brand");
        Assert.Equal(HttpStatusCode.OK, (await brand.PostAsync($"/api/posts/{shown}/feature", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await brand.PostAsync($"/api/posts/{hidden}/feature", null)).StatusCode);

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Posts.Where(p => p.Id == hidden).ExecuteUpdateAsync(s => s.SetProperty(p => p.Hidden, true));
        }

        var social = (await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social");
        Assert.Equal(2, social.GetProperty("posts").GetInt32());
        Assert.Equal(2, social.GetProperty("mentions").GetInt32());
        Assert.Equal(1, social.GetProperty("featured").GetInt32());
    }
}
