using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

/// <summary>The scoring function on its own: one test per term, the decay, the tie order and the interests parser.</summary>
public class FeedRankerTests
{
    private static readonly DateTime Now = new(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

    private static double Score(
        int fires = 0, int comments = 0, double hoursOld = 0,
        bool followed = false, bool interest = false, bool checkedIntent = false, bool featured = false) =>
        FeedRanker.Score(fires, comments, Now.AddHours(-hoursOld), Now, followed, interest, checkedIntent, featured);

    [Fact]
    public void A_brand_new_post_nobody_reacted_to_scores_zero() => Assert.Equal(0, Score(), 10);

    [Fact]
    public void Fire_counts_logarithmically()
    {
        Assert.Equal(Math.Log(4), Score(fires: 3), 10);
        Assert.True(Score(fires: 100) - Score(fires: 99) < Score(fires: 1) - Score(fires: 0));
    }

    [Fact]
    public void Comments_count_half_as_much_as_fire() => Assert.Equal(0.5 * Math.Log(3), Score(comments: 2), 10);

    [Fact]
    public void Following_the_author_adds_two() => Assert.Equal(2, Score(followed: true), 10);

    [Fact]
    public void An_intent_in_the_viewers_interests_adds_one() => Assert.Equal(1, Score(interest: true), 10);

    [Fact]
    public void A_recent_ok_check_with_the_intent_adds_half() => Assert.Equal(0.5, Score(checkedIntent: true), 10);

    [Fact]
    public void A_brand_feature_adds_one() => Assert.Equal(1, Score(featured: true), 10);

    [Fact]
    public void Age_costs_035_per_day_and_the_future_is_free()
    {
        Assert.Equal(-0.35, Score(hoursOld: 24), 10);
        Assert.Equal(-0.7, Score(hoursOld: 48), 10);
        Assert.Equal(-0.35 * 6 / 24, Score(hoursOld: 6), 10);
        Assert.Equal(0, Score(hoursOld: -5), 10);
    }

    [Fact]
    public void The_terms_add_up()
    {
        var expected = Math.Log(3) + 0.5 * Math.Log(2) + 2 + 1 + 0.5 + 1 - 0.35 * 2;
        Assert.Equal(expected, Score(fires: 2, comments: 1, hoursOld: 48, followed: true, interest: true, checkedIntent: true, featured: true), 10);
    }

    [Fact]
    public void Negative_counts_never_produce_nan()
    {
        Assert.Equal(0, FeedRanker.Score(-3, -1, Now, Now, false, false, false, false), 10);
    }

    [Fact]
    public void Rank_orders_by_score_then_newest()
    {
        var items = new List<(string Name, double Score, DateTime CreatedAt)>
        {
            ("older-tie", 1.0, Now.AddHours(-2)),
            ("newer-tie", 1.0, Now.AddHours(-1)),
            ("best-but-old", 2.0, Now.AddDays(-3)),
            ("worst-but-newest", 0.0, Now)
        };

        var ranked = FeedRanker.Rank(items, i => i.Score, i => i.CreatedAt).Select(i => i.Name).ToList();
        Assert.Equal(["best-but-old", "newer-tie", "older-tie", "worst-but-newest"], ranked);
    }

    [Fact]
    public void Interests_parse_case_insensitively_and_skip_junk()
    {
        Assert.Equal([StyleIntent.Casual, StyleIntent.Office, StyleIntent.Party], FeedRanker.ParseInterests("Casual, office,PARTY").Order().ToList());
        Assert.Empty(FeedRanker.ParseInterests(null));
        Assert.Empty(FeedRanker.ParseInterests("  "));
        Assert.Equal([StyleIntent.Sport], FeedRanker.ParseInterests("Sport,Wedding,99,,").ToList());
    }
}

/// <summary>The "for you" tab end to end, plus a check that the other tabs still behave as they did.</summary>
public class FeedRankingTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public FeedRankingTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static List<Guid> Ids(JsonElement feed) =>
        feed.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

    private static bool IsNull(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    /// <summary>Walks every page so the assertions never depend on how many posts other tests in this class have made.</summary>
    private static async Task<List<JsonElement>> AllItemsAsync(HttpClient client, string query)
    {
        var items = new List<JsonElement>();
        int? offset = 0;
        while (offset is int o)
        {
            var page = await client.GetFromJsonAsync<JsonElement>($"/api/feed?{query}&offset={o}&limit=30");
            items.AddRange(page.GetProperty("items").EnumerateArray());
            offset = IsNull(page, "nextOffset") ? null : page.GetProperty("nextOffset").GetInt32();
        }

        return items;
    }

    private static async Task<List<Guid>> AllIdsAsync(HttpClient client, string query) =>
        (await AllItemsAsync(client, query)).Select(p => p.GetProperty("id").GetGuid()).ToList();

    private static void AssertBefore(List<Guid> ids, Guid first, Guid second)
    {
        Assert.Contains(first, ids);
        Assert.Contains(second, ids);
        Assert.True(ids.IndexOf(first) < ids.IndexOf(second), $"expected {first} to rank above {second}");
    }

    private async Task UpdatePostAsync(Guid postId, Action<Post> change)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var post = await db.Posts.SingleAsync(p => p.Id == postId);
        change(post);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_followed_authors_post_outranks_a_strangers_equal_post_but_only_when_signed_in()
    {
        var (author, _, _) = await _app.NewUserAsync("fy_author1");
        var (stranger, _, _) = await _app.NewUserAsync("fy_stranger1");
        var (viewer, _, _) = await _app.NewUserAsync("fy_viewer1");
        await viewer.PostAsync("/api/users/fy_author1/follow", null);
        var followedPost = await _app.CheckAndPostAsync(author, intent: "Minimal");
        var strangerPost = await _app.CheckAndPostAsync(stranger, intent: "Minimal");   // newer, so it wins on the tie-break alone

        AssertBefore(await AllIdsAsync(viewer, "tab=foryou&intent=Minimal"), followedPost, strangerPost);

        // Signed out there is no follow term: two otherwise equal posts come newest first.
        AssertBefore(await AllIdsAsync(_app.NewClient(), "tab=foryou&intent=Minimal"), strangerPost, followedPost);
    }

    [Fact]
    public async Task An_intent_in_the_viewers_interests_lifts_a_post()
    {
        var (sporty, _, _) = await _app.NewUserAsync("fy_sporty2");
        var (suited, _, _) = await _app.NewUserAsync("fy_suited2");
        var (viewer, viewerId, _) = await _app.NewUserAsync("fy_viewer2");
        var sportPost = await _app.CheckAndPostAsync(sporty, intent: "Sport");
        var officePost = await _app.CheckAndPostAsync(suited, intent: "Office");   // newer

        AssertBefore(await AllIdsAsync(viewer, "tab=foryou"), officePost, sportPost);

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == viewerId);
            user.Interests = "Casual,Sport";
            await db.SaveChangesAsync();
        }

        AssertBefore(await AllIdsAsync(viewer, "tab=foryou"), sportPost, officePost);
        // Somebody else's feed is untouched by this viewer's interests.
        AssertBefore(await AllIdsAsync(_app.NewClient(), "tab=foryou"), officePost, sportPost);
    }

    [Fact]
    public async Task A_recent_ok_check_with_the_intent_lifts_a_post()
    {
        var (street, _, _) = await _app.NewUserAsync("fy_street3");
        var (plain, _, _) = await _app.NewUserAsync("fy_plain3");
        var (viewer, _, _) = await _app.NewUserAsync("fy_viewer3");
        var streetPost = await _app.CheckAndPostAsync(street, intent: "Streetwear");
        var casualPost = await _app.CheckAndPostAsync(plain, intent: "Casual");   // newer

        AssertBefore(await AllIdsAsync(viewer, "tab=foryou"), casualPost, streetPost);

        await _app.CheckAsync(viewer, intent: "Streetwear");   // private, never posted; still tells us what the viewer wears
        AssertBefore(await AllIdsAsync(viewer, "tab=foryou"), streetPost, casualPost);
    }

    [Fact]
    public async Task A_brand_feature_lifts_a_post_for_everyone()
    {
        var (brand, brandId, _) = await _app.NewUserAsync("fy_brand4", accountType: "Brand", displayName: "Brand Four");
        var (a, _, _) = await _app.NewUserAsync("fy_a4");
        var (b, _, _) = await _app.NewUserAsync("fy_b4");
        var featuredPost = await _app.CheckAndPostAsync(a, intent: "Party");
        var plainPost = await _app.CheckAndPostAsync(b, intent: "Party");   // newer

        AssertBefore(await AllIdsAsync(_app.NewClient(), "intent=Party"), plainPost, featuredPost);

        await UpdatePostAsync(featuredPost, p =>
        {
            p.FeaturedByBrandId = brandId;
            p.FeaturedAt = DateTime.UtcNow;
        });

        var items = await AllItemsAsync(_app.NewClient(), "intent=Party");
        AssertBefore(items.Select(p => p.GetProperty("id").GetGuid()).ToList(), featuredPost, plainPost);
        var featured = items.Single(p => p.GetProperty("id").GetGuid() == featuredPost);
        Assert.Equal("fy_brand4", featured.GetProperty("featuredBy").GetProperty("handle").GetString());
        Assert.Equal("Brand Four", featured.GetProperty("featuredBy").GetProperty("name").GetString());
        Assert.True(IsNull(items.Single(p => p.GetProperty("id").GetGuid() == plainPost), "featuredBy"));
    }

    [Fact]
    public async Task Fire_and_comments_lift_a_post_and_the_ranking_is_the_default_for_any_unknown_tab()
    {
        var (a, _, _) = await _app.NewUserAsync("fy_a5");
        var (b, _, _) = await _app.NewUserAsync("fy_b5");
        var (fan, _, _) = await _app.NewUserAsync("fy_fan5");
        var fired = await _app.CheckAndPostAsync(a, intent: "Casual");
        var commented = await _app.CheckAndPostAsync(b, intent: "Casual");
        var quiet = await _app.CheckAndPostAsync(b, intent: "Casual");   // newest

        await fan.PostAsync($"/api/posts/{fired}/fire", null);
        await fan.PostAsJsonAsync($"/api/posts/{commented}/comments", new { text = "nice" });

        foreach (var query in new[] { "intent=Casual", "tab=foryou&intent=Casual", "tab=ForYou&intent=Casual", "tab=whatever&intent=Casual" })
        {
            var ids = await AllIdsAsync(_app.NewClient(), query);
            AssertBefore(ids, fired, commented);      // ln 2 beats 0.5 ln 2
            AssertBefore(ids, commented, quiet);
        }

        // Fresh ignores reactions entirely.
        AssertBefore(await AllIdsAsync(_app.NewClient(), "tab=fresh&intent=Casual"), quiet, fired);
    }

    [Fact]
    public async Task Only_the_last_30_days_are_candidates_and_hidden_posts_never_are()
    {
        var (a, _, _) = await _app.NewUserAsync("fy_a6");
        var (fan, _, _) = await _app.NewUserAsync("fy_fan6");
        var old = await _app.CheckAndPostAsync(a, intent: "Minimal");
        var hidden = await _app.CheckAndPostAsync(a, intent: "Minimal");
        var current = await _app.CheckAndPostAsync(a, intent: "Minimal");
        await fan.PostAsync($"/api/posts/{old}/fire", null);   // popular, but too old to matter
        await UpdatePostAsync(old, p => p.CreatedAt = DateTime.UtcNow.AddDays(-31));
        await UpdatePostAsync(hidden, p => p.Hidden = true);

        var forYou = await AllIdsAsync(_app.NewClient(), "tab=foryou&intent=Minimal");
        Assert.Contains(current, forYou);
        Assert.DoesNotContain(old, forYou);
        Assert.DoesNotContain(hidden, forYou);

        // Fresh has no window: the old post is still there, at the end.
        var fresh = await AllIdsAsync(_app.NewClient(), "tab=fresh&intent=Minimal");
        Assert.Contains(old, fresh);
        Assert.DoesNotContain(hidden, fresh);
    }

    [Fact]
    public async Task Paging_walks_the_ranked_list_without_gaps_or_repeats()
    {
        var (a, _, _) = await _app.NewUserAsync("fy_a7");
        var (b, _, _) = await _app.NewUserAsync("fy_b7");
        var (fan, _, _) = await _app.NewUserAsync("fy_fan7");
        var posts = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            posts.Add(await _app.CheckAndPostAsync(i % 2 == 0 ? a : b, intent: "OldMoney"));   // OldMoney is this test's alone
        }

        await fan.PostAsync($"/api/posts/{posts[1]}/fire", null);
        await a.PostAsync($"/api/posts/{posts[1]}/fire", null);
        await fan.PostAsync($"/api/posts/{posts[3]}/fire", null);

        var whole = Ids(await fan.GetFromJsonAsync<JsonElement>("/api/feed?tab=foryou&intent=OldMoney&limit=30"));
        Assert.Equal(5, whole.Count);
        Assert.Equal(posts[1], whole[0]);
        Assert.Equal(posts[3], whole[1]);

        var walked = new List<Guid>();
        int? offset = 0;
        var pages = 0;
        while (offset is int o)
        {
            var page = await fan.GetFromJsonAsync<JsonElement>($"/api/feed?tab=foryou&intent=OldMoney&offset={o}&limit=2");
            walked.AddRange(Ids(page));
            offset = IsNull(page, "nextOffset") ? null : page.GetProperty("nextOffset").GetInt32();
            pages++;
        }

        Assert.Equal(3, pages);
        Assert.Equal(whole, walked);
        Assert.Equal(walked.Count, walked.Distinct().Count());

        var beyond = await fan.GetFromJsonAsync<JsonElement>("/api/feed?tab=foryou&intent=OldMoney&offset=50&limit=2");
        Assert.Equal(0, beyond.GetProperty("items").GetArrayLength());
        Assert.True(IsNull(beyond, "nextOffset"));
    }

    [Fact]
    public async Task Following_top_and_fresh_behave_as_before()
    {
        var (a, _, _) = await _app.NewUserAsync("fy_a8");
        var (b, _, _) = await _app.NewUserAsync("fy_b8");
        var (reader, _, _) = await _app.NewUserAsync("fy_reader8");
        var p1 = await _app.CheckAndPostAsync(a, intent: "Date");
        var p2 = await _app.CheckAndPostAsync(b, intent: "Office");
        var p3 = await _app.CheckAndPostAsync(a, intent: "Date");
        await reader.PostAsync($"/api/posts/{p2}/fire", null);
        await a.PostAsync($"/api/posts/{p2}/fire", null);
        await reader.PostAsync("/api/users/fy_a8/follow", null);

        var fresh = await AllIdsAsync(_app.NewClient(), "tab=fresh");
        Assert.True(fresh.IndexOf(p3) < fresh.IndexOf(p2) && fresh.IndexOf(p2) < fresh.IndexOf(p1));

        var top = await AllItemsAsync(reader, "tab=top");
        var topIds = top.Select(p => p.GetProperty("id").GetGuid()).ToList();
        AssertBefore(topIds, p2, p3);
        AssertBefore(topIds, p2, p1);
        var fireCounts = top.Select(p => p.GetProperty("fireCount").GetInt32()).ToList();
        Assert.Equal(fireCounts.OrderByDescending(c => c), fireCounts);
        Assert.True(top.Single(p => p.GetProperty("id").GetGuid() == p2).GetProperty("fired").GetBoolean());

        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().GetAsync("/api/feed?tab=following")).StatusCode);
        var following = await AllIdsAsync(reader, "tab=following");
        Assert.Contains(p1, following);
        Assert.Contains(p3, following);
        Assert.DoesNotContain(p2, following);
        Assert.True(following.IndexOf(p3) < following.IndexOf(p1));

        var office = await AllItemsAsync(_app.NewClient(), "intent=office");
        Assert.All(office, p => Assert.Equal("Office", p.GetProperty("intent").GetString()));
        Assert.Contains(office, p => p.GetProperty("id").GetGuid() == p2);
    }
}
