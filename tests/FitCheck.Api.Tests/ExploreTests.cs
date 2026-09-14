using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

/// <summary>Explore, search and tag feeds. Tags are inserted straight into the table: parsing captions is the posts area's job.</summary>
public class ExploreTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public ExploreTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static bool IsNull(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static List<Guid> Ids(JsonElement list) => list.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

    private static List<string> Handles(JsonElement cards) =>
        cards.EnumerateArray().Select(c => c.GetProperty("user").GetProperty("handle").GetString()!).ToList();

    private static Dictionary<string, int> TagCounts(JsonElement tags) =>
        tags.EnumerateArray().ToDictionary(t => t.GetProperty("tag").GetString()!, t => t.GetProperty("posts").GetInt32());

    private async Task TagAsync(Guid postId, params string[] tags)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PostTags.AddRange(tags.Select(t => new PostTag { PostId = postId, Tag = t }));
        await db.SaveChangesAsync();
    }

    private async Task UpdatePostAsync(Guid postId, Action<Post> change)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var post = await db.Posts.SingleAsync(p => p.Id == postId);
        change(post);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> OpenChallengeAsync(HttpClient brand, string title, string intent = "Office", double days = 3)
    {
        var response = await brand.PostAsJsonAsync("/api/challenges", new
        {
            title, brief = "Show us the look.", intent, prize = "A gift card", endsAt = DateTime.UtcNow.AddDays(days)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Trending_tags_count_visible_posts_of_the_last_seven_days_only()
    {
        var (a, _, _) = await _app.NewUserAsync("ex_tag_a");
        var (b, _, _) = await _app.NewUserAsync("ex_tag_b");
        var p1 = await _app.CheckAndPostAsync(a);
        var p2 = await _app.CheckAndPostAsync(a);
        var p3 = await _app.CheckAndPostAsync(b);
        var old = await _app.CheckAndPostAsync(b);
        var hidden = await _app.CheckAndPostAsync(a);
        await TagAsync(p1, "exsummer", "exdenim");
        await TagAsync(p2, "exsummer");
        await TagAsync(p3, "exsummer", "exdenim");
        await TagAsync(old, "exsummer", "exold");
        await TagAsync(hidden, "exdenim", "exhidden");
        await UpdatePostAsync(old, p => p.CreatedAt = DateTime.UtcNow.AddDays(-8));
        await UpdatePostAsync(hidden, p => p.Hidden = true);

        var explore = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/explore");
        var trending = explore.GetProperty("trendingTags");
        var counts = TagCounts(trending);
        Assert.Equal(3, counts["exsummer"]);
        Assert.Equal(2, counts["exdenim"]);
        Assert.False(counts.ContainsKey("exold"));
        Assert.False(counts.ContainsKey("exhidden"));

        var order = trending.EnumerateArray().Select(t => t.GetProperty("tag").GetString()).ToList();
        Assert.True(order.IndexOf("exsummer") < order.IndexOf("exdenim"));
        Assert.Equal(counts.Values.OrderByDescending(c => c), trending.EnumerateArray().Select(t => t.GetProperty("posts").GetInt32()));
        Assert.True(trending.GetArrayLength() <= 10);
    }

    [Fact]
    public async Task Brands_come_by_followers_with_the_viewers_following_flag()
    {
        var (top, _, _) = await _app.NewUserAsync("ex_brand_top", accountType: "Brand", displayName: "Top Brand");
        var (mid, _, _) = await _app.NewUserAsync("ex_brand_mid", accountType: "Brand");
        await _app.NewUserAsync("ex_brand_none", accountType: "Brand");
        await _app.NewUserAsync("ex_brand_person", displayName: "Not A Brand");
        var (f1, _, _) = await _app.NewUserAsync("ex_brand_f1");
        var (f2, _, _) = await _app.NewUserAsync("ex_brand_f2");
        await f1.PostAsync("/api/users/ex_brand_top/follow", null);
        await f2.PostAsync("/api/users/ex_brand_top/follow", null);
        await f1.PostAsync("/api/users/ex_brand_mid/follow", null);
        await _app.CheckAndPostAsync(top);
        var hidden = await _app.CheckAndPostAsync(top);
        await UpdatePostAsync(hidden, p => p.Hidden = true);
        await _app.CheckAndPostAsync(mid);

        var explore = await f1.GetFromJsonAsync<JsonElement>("/api/explore");
        var brands = explore.GetProperty("brands");
        var handles = Handles(brands);
        Assert.True(handles.IndexOf("ex_brand_top") < handles.IndexOf("ex_brand_mid"));
        Assert.True(handles.IndexOf("ex_brand_mid") < handles.IndexOf("ex_brand_none"));
        Assert.DoesNotContain("ex_brand_person", handles);
        Assert.DoesNotContain("ex_brand_f1", handles);
        Assert.True(brands.GetArrayLength() <= 10);

        var topCard = brands.EnumerateArray().Single(c => c.GetProperty("user").GetProperty("handle").GetString() == "ex_brand_top");
        Assert.Equal("Top Brand", topCard.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal("Brand", topCard.GetProperty("user").GetProperty("accountType").GetString());
        Assert.Equal(2, topCard.GetProperty("followers").GetInt32());
        Assert.Equal(1, topCard.GetProperty("posts").GetInt32());
        Assert.True(topCard.GetProperty("following").GetBoolean());

        var noneCard = brands.EnumerateArray().Single(c => c.GetProperty("user").GetProperty("handle").GetString() == "ex_brand_none");
        Assert.Equal(0, noneCard.GetProperty("followers").GetInt32());
        Assert.Equal(0, noneCard.GetProperty("posts").GetInt32());
        Assert.False(noneCard.GetProperty("following").GetBoolean());

        var followerCounts = brands.EnumerateArray().Select(c => c.GetProperty("followers").GetInt32()).ToList();
        Assert.Equal(followerCounts.OrderByDescending(c => c), followerCounts);

        var anonymous = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/explore");
        Assert.All(anonymous.GetProperty("brands").EnumerateArray(), c => Assert.False(c.GetProperty("following").GetBoolean()));
    }

    [Fact]
    public async Task Top_looks_are_the_most_fired_visible_posts_of_the_week()
    {
        var (owner, _, _) = await _app.NewUserAsync("ex_looks_owner");
        var (f1, _, _) = await _app.NewUserAsync("ex_looks_f1");
        var (f2, _, _) = await _app.NewUserAsync("ex_looks_f2");
        var (f3, _, _) = await _app.NewUserAsync("ex_looks_f3");
        var hot = await _app.CheckAndPostAsync(owner);
        var warm = await _app.CheckAndPostAsync(owner);
        var stale = await _app.CheckAndPostAsync(owner);
        var hidden = await _app.CheckAndPostAsync(owner);
        foreach (var fan in new[] { f1, f2, f3 })
        {
            await fan.PostAsync($"/api/posts/{hot}/fire", null);
            await fan.PostAsync($"/api/posts/{stale}/fire", null);
            await fan.PostAsync($"/api/posts/{hidden}/fire", null);
        }

        await f1.PostAsync($"/api/posts/{warm}/fire", null);
        await UpdatePostAsync(stale, p => p.CreatedAt = DateTime.UtcNow.AddDays(-8));
        await UpdatePostAsync(hidden, p => p.Hidden = true);

        var explore = await f1.GetFromJsonAsync<JsonElement>("/api/explore");
        var looks = explore.GetProperty("topLooks");
        var ids = Ids(looks);
        Assert.Equal(hot, ids[0]);
        Assert.Contains(warm, ids);
        Assert.DoesNotContain(stale, ids);
        Assert.DoesNotContain(hidden, ids);
        Assert.True(ids.Count <= 6);
        var fires = looks.EnumerateArray().Select(p => p.GetProperty("fireCount").GetInt32()).ToList();
        Assert.Equal(fires.OrderByDescending(c => c), fires);
        Assert.True(looks[0].GetProperty("fired").GetBoolean());
        Assert.Equal("ex_looks_owner", looks[0].GetProperty("user").GetProperty("handle").GetString());
    }

    [Fact]
    public async Task Open_challenges_are_included_ended_ones_are_not()
    {
        var (brand, _, _) = await _app.NewUserAsync("ex_ch_brand", accountType: "Brand");
        var (entrant, _, _) = await _app.NewUserAsync("ex_ch_entrant");
        var open = await OpenChallengeAsync(brand, "Explore office looks");
        var ended = await OpenChallengeAsync(brand, "Yesterday's looks", days: 2);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var challenge = await db.Challenges.SingleAsync(c => c.Id == ended);
            challenge.EndsAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var entry = await _app.CheckAndPostAsync(entrant, intent: "Office", challengeId: open);

        var explore = await entrant.GetFromJsonAsync<JsonElement>("/api/explore");
        var challenges = explore.GetProperty("challenges");
        var ids = Ids(challenges);
        Assert.Contains(open, ids);
        Assert.DoesNotContain(ended, ids);
        Assert.True(ids.Count <= 5);

        var card = challenges.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == open);
        Assert.Equal("Explore office looks", card.GetProperty("title").GetString());
        Assert.Equal("ex_ch_brand", card.GetProperty("brand").GetProperty("handle").GetString());
        Assert.True(card.GetProperty("isOpen").GetBoolean());
        Assert.Equal(1, card.GetProperty("entries").GetInt32());
        Assert.True(card.GetProperty("viewer").GetProperty("hasEntered").GetBoolean());
        Assert.Equal(entry, card.GetProperty("top")[0].GetProperty("id").GetGuid());

        // The same card through the challenges list, so the two routes cannot drift apart.
        var listed = (await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/challenges")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == open);
        Assert.Equal(card.GetProperty("entries").GetInt32(), listed.GetProperty("entries").GetInt32());
        Assert.Equal(card.GetProperty("endsAt").GetString(), listed.GetProperty("endsAt").GetString());
    }

    [Fact]
    public async Task Search_matches_handle_prefix_or_name_substring_brands_first_then_followers()
    {
        var (shop, _, _) = await _app.NewUserAsync("ex_velvet_shop", accountType: "Brand", displayName: "Velvet Shop");
        var (fan, _, _) = await _app.NewUserAsync("ex_velvet_fan", displayName: "Velvet Fan");
        var (lane, _, _) = await _app.NewUserAsync("ex_lane", displayName: "The VELVET Lane");
        await _app.NewUserAsync("ex_plain", displayName: "Nothing to see");
        var (follower, _, _) = await _app.NewUserAsync("ex_velvet_follower");   // matches by handle prefix too
        await follower.PostAsync("/api/users/ex_lane/follow", null);
        await fan.PostAsync("/api/users/ex_lane/follow", null);
        await follower.PostAsync("/api/users/ex_velvet_shop/follow", null);
        var shopPost = await _app.CheckAndPostAsync(shop);
        var lanePost = await _app.CheckAndPostAsync(lane);
        await TagAsync(shopPost, "velvetcoat", "velvety");
        await TagAsync(lanePost, "velvety", "notvelvet");

        // Display-name substring, any case.
        var byName = await follower.GetFromJsonAsync<JsonElement>("/api/search?q=Velvet");
        var users = byName.GetProperty("users");
        // The follower's handle starts with "ex_", not "velvet", and it has no display name: not a match here.
        Assert.Equal(["ex_velvet_shop", "ex_lane", "ex_velvet_fan"], Handles(users));
        var shopCard = users[0];
        Assert.Equal("Brand", shopCard.GetProperty("user").GetProperty("accountType").GetString());
        Assert.Equal("Velvet Shop", shopCard.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal(1, shopCard.GetProperty("followers").GetInt32());
        Assert.Equal(1, shopCard.GetProperty("posts").GetInt32());
        Assert.True(shopCard.GetProperty("following").GetBoolean());
        Assert.Equal(2, users[1].GetProperty("followers").GetInt32());
        Assert.Equal(0, users[2].GetProperty("followers").GetInt32());
        Assert.False(users[2].GetProperty("following").GetBoolean());

        var tags = byName.GetProperty("tags");
        Assert.Equal(["velvety", "velvetcoat"], tags.EnumerateArray().Select(t => t.GetProperty("tag").GetString()).ToList());
        Assert.Equal(2, TagCounts(tags)["velvety"]);
        Assert.Equal(1, TagCounts(tags)["velvetcoat"]);

        // Handle prefix only reaches the handles that start with it.
        var byHandle = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/search?q=EX_VELVET");
        Assert.Equal(["ex_velvet_shop", "ex_velvet_fan", "ex_velvet_follower"], Handles(byHandle.GetProperty("users")));
        Assert.Equal(0, byHandle.GetProperty("tags").GetArrayLength());
        Assert.All(byHandle.GetProperty("users").EnumerateArray(), c => Assert.False(c.GetProperty("following").GetBoolean()));

        // A leading @ or # is how people type it, not part of the term.
        var at = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/search?q=%40ex_velvet");
        Assert.Equal(Handles(byHandle.GetProperty("users")), Handles(at.GetProperty("users")));
        var hash = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/search?q=%23velvet");
        Assert.Equal(["velvety", "velvetcoat"], hash.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("tag").GetString()).ToList());
    }

    [Fact]
    public async Task Search_returns_at_most_ten_users_and_ten_tags()
    {
        var (owner, _, _) = await _app.NewUserAsync("ex_limit_owner");
        var postId = await _app.CheckAndPostAsync(owner);
        for (var i = 1; i <= 12; i++)
        {
            await _app.NewUserAsync($"exlim{i:00}");
        }

        await TagAsync(postId, Enumerable.Range(1, 12).Select(i => $"exlimtag{i:00}").ToArray());

        var result = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/search?q=exlim");
        var handles = Handles(result.GetProperty("users"));
        Assert.Equal(10, handles.Count);
        Assert.Equal("exlim01", handles[0]);
        Assert.DoesNotContain("exlim11", handles);
        Assert.DoesNotContain("exlim12", handles);

        var tags = result.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("tag").GetString()).ToList();
        Assert.Equal(10, tags.Count);
        Assert.Equal("exlimtag01", tags[0]);
        Assert.DoesNotContain("exlimtag11", tags);
    }

    [Fact]
    public async Task Search_needs_one_to_forty_characters()
    {
        var client = _app.NewClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/search")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/search?q=")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/search?q=%20%20")).StatusCode);
        var tooLong = await client.GetAsync("/api/search?q=" + new string('a', 41));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("Type 1 to 40 characters to search.", (await Json(tooLong)).GetProperty("error").GetString());

        var hebrew = _app.NewClient();
        hebrew.DefaultRequestHeaders.AcceptLanguage.ParseAdd("he-IL");
        Assert.Equal("כותבים בין תו אחד ל-40 תווים כדי לחפש.", (await Json(await hebrew.GetAsync("/api/search?q="))).GetProperty("error").GetString());

        var longest = await client.GetAsync("/api/search?q=" + new string('a', 40));
        Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
        var body = await Json(longest);
        Assert.Equal(0, body.GetProperty("users").GetArrayLength());
        Assert.Equal(0, body.GetProperty("tags").GetArrayLength());

        // A bare marker is a valid query that matches nothing.
        var marker = await Json(await client.GetAsync("/api/search?q=%23"));
        Assert.Equal(0, marker.GetProperty("users").GetArrayLength());
        Assert.Equal(0, marker.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task Tag_feed_normalises_the_tag_pages_newest_first_and_is_empty_for_unknown_tags()
    {
        var (a, _, _) = await _app.NewUserAsync("ex_feed_a");
        var (b, _, _) = await _app.NewUserAsync("ex_feed_b");
        var p1 = await _app.CheckAndPostAsync(a);
        var p2 = await _app.CheckAndPostAsync(b);
        var p3 = await _app.CheckAndPostAsync(a);
        var hidden = await _app.CheckAndPostAsync(b);
        var other = await _app.CheckAndPostAsync(b);
        await TagAsync(p1, "exfeed");
        await TagAsync(p2, "exfeed", "exother");
        await TagAsync(p3, "exfeed");
        await TagAsync(hidden, "exfeed");
        await TagAsync(other, "exother");
        await UpdatePostAsync(hidden, p => p.Hidden = true);

        var client = _app.NewClient();
        var first = await client.GetFromJsonAsync<JsonElement>("/api/tags/%23ExFeed/posts?limit=2");
        Assert.Equal([p3, p2], Ids(first.GetProperty("items")));
        Assert.Equal(2, first.GetProperty("nextOffset").GetInt32());
        Assert.All(first.GetProperty("items").EnumerateArray(), p => Assert.Contains("exfeed", p.GetProperty("tags").EnumerateArray().Select(t => t.GetString())));

        var second = await client.GetFromJsonAsync<JsonElement>("/api/tags/%23ExFeed/posts?limit=2&offset=2");
        Assert.Equal([p1], Ids(second.GetProperty("items")));
        Assert.True(IsNull(second, "nextOffset"));

        var whole = await client.GetFromJsonAsync<JsonElement>("/api/tags/%20%20EXFEED%20/posts");
        Assert.Equal([p3, p2, p1], Ids(whole.GetProperty("items")));
        Assert.True(IsNull(whole, "nextOffset"));

        var otherTag = await client.GetFromJsonAsync<JsonElement>("/api/tags/exother/posts");
        Assert.Equal([other, p2], Ids(otherTag.GetProperty("items")));

        foreach (var unknown in new[] { "/api/tags/nothing_here/posts", "/api/tags/%23/posts", "/api/tags/%20%23%20/posts" })
        {
            var response = await client.GetAsync(unknown);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var empty = await Json(response);
            Assert.Equal(0, empty.GetProperty("items").GetArrayLength());
            Assert.True(IsNull(empty, "nextOffset"));
        }

        // Signed in, the viewer's own state rides along as in every feed.
        await b.PostAsync($"/api/posts/{p3}/fire", null);
        var mine = await b.GetFromJsonAsync<JsonElement>("/api/tags/exfeed/posts");
        Assert.True(mine.GetProperty("items")[0].GetProperty("fired").GetBoolean());
        Assert.True(mine.GetProperty("items")[1].GetProperty("isMine").GetBoolean());
    }

    // ---- looks by piece (PostItems) ----

    /// <summary>A scripted answer whose items are the given (name, category) pairs; the rest is <see cref="Payloads.Ok"/>'s.</summary>
    private static JsonElement ItemsPayload(params (string Name, string Category)[] items)
    {
        var list = string.Join(",", items.Select(i => $$"""{ "name": {{JsonSerializer.Serialize(i.Name)}}, "category": "{{i.Category}}", "verdict": "neutral", "note": "" }"""));
        return Payloads.Parse($$"""
            {
              "status": "ok", "score": 7, "intent_match": 70, "headline": "Seeded", "vibe": "seeded",
              "items": [{{list}}], "working": ["Seeded"], "one_tip": "Seeded."
            }
            """);
    }

    private async Task<List<(string Name, string Category)>> ItemsOfAsync(Guid postId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.PostItems.Where(i => i.PostId == postId).OrderBy(i => i.Name).Select(i => new ValueTuple<string, string>(i.Name, i.Category)).ToListAsync();
    }

    [Fact]
    public async Task Posting_writes_the_stylists_items_lower_cased_capped_and_distinct_and_deleting_the_look_removes_them()
    {
        var longName = "A " + new string('x', 70);
        _app.Vision.Handler = _ => ItemsPayload(
            ("Black Boots", "shoes"), ("black boots", "shoes"), ("  White   Tee ", "top"), ("Dark jeans", "bottom"), (longName, "outerwear"),
            ("Gold hoops", "accessory"), ("Beige trench", "outerwear"), ("Silk scarf", "hat"), ("Leather belt", "accessory"), ("Wool coat", "outerwear"), ("Extra one", "other"));
        var (owner, _, _) = await _app.NewUserAsync("ex_items_owner");
        var postId = await _app.CheckAndPostAsync(owner);

        var items = await ItemsOfAsync(postId);
        // Eleven named, one a duplicate by case, so ten distinct; the first eight are kept, in lower case, one space between words.
        Assert.Equal(8, items.Count);
        var names = items.Select(i => i.Name).ToList();
        Assert.Contains("black boots", names);
        Assert.Contains("white tee", names);
        Assert.Contains("dark jeans", names);
        Assert.Contains("gold hoops", names);
        Assert.Contains("beige trench", names);
        Assert.Contains("silk scarf", names);
        Assert.Contains("leather belt", names);
        Assert.DoesNotContain("wool coat", names);
        Assert.DoesNotContain("extra one", names);
        Assert.All(names, n => Assert.Equal(n.ToLowerInvariant(), n));
        var cut = Assert.Single(names, n => n.StartsWith("a xxx", StringComparison.Ordinal));
        Assert.Equal(60, cut.Length);
        Assert.Equal("shoes", items.Single(i => i.Name == "black boots").Category);
        // A category the stylist invents is stored as "other".
        Assert.Equal("other", items.Single(i => i.Name == "silk scarf").Category);

        // The look's items go with the look; the check keeps its own feedback.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Empty(await ItemsOfAsync(postId));
    }

    [Fact]
    public async Task Search_finds_looks_by_item_newest_first_without_hidden_or_suspended_ones_and_caps_at_twelve()
    {
        _app.Vision.Handler = _ => ItemsPayload(("Black leather boots", "shoes"), ("White tee", "top"));
        var (owner, _, _) = await _app.NewUserAsync("ex_item_owner");
        var (banned, bannedId, _) = await _app.NewUserAsync("ex_item_banned");
        var (other, _, _) = await _app.NewUserAsync("ex_item_other");

        var posted = new List<Guid>();
        for (var i = 0; i < 14; i++)
        {
            posted.Add(await _app.CheckAndPostAsync(owner));
        }

        var hidden = posted[5];
        await UpdatePostAsync(hidden, p => p.Hidden = true);
        var bannedPost = await _app.CheckAndPostAsync(banned);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Users.SingleAsync(u => u.Id == bannedId)).Suspended = true;
            await db.SaveChangesAsync();
        }

        _app.Vision.Handler = _ => ItemsPayload(("Red dress", "dress"));
        var dress = await _app.CheckAndPostAsync(other);

        var result = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/search?q=BOOTS");
        var ids = Ids(result.GetProperty("posts"));
        // Thirteen visible looks by the owner match; the twelve newest come back, newest first; the hidden one and the
        // suspended account's look never do, and neither does a look whose pieces do not carry the term.
        var visible = posted.Where(id => id != hidden).ToList();
        visible.Reverse();
        Assert.Equal(visible.Take(12), ids);
        Assert.DoesNotContain(hidden, ids);
        Assert.DoesNotContain(bannedPost, ids);
        Assert.DoesNotContain(dress, ids);
        Assert.Equal("/api/posts/" + ids[0] + "/image", result.GetProperty("posts")[0].GetProperty("imageUrl").GetString());
        Assert.Equal("ex_item_owner", result.GetProperty("posts")[0].GetProperty("user").GetProperty("handle").GetString());

        // A piece is matched anywhere in its name, in either case; the dress answers to its own word.
        Assert.Equal(12, Ids((await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/search?q=leather")).GetProperty("posts")).Count);
        Assert.Equal([dress], Ids((await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/search?q=%23Red")).GetProperty("posts")));
        Assert.Empty((await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/search?q=sandals")).GetProperty("posts").EnumerateArray());

        // Signed in, the viewer's own state rides along as in every list of looks.
        await other.PostAsync($"/api/posts/{ids[0]}/fire", null);
        var mine = await other.GetFromJsonAsync<JsonElement>("/api/search?q=boots");
        Assert.True(mine.GetProperty("posts")[0].GetProperty("fired").GetBoolean());
    }

    [Fact]
    public async Task Search_by_item_takes_percent_and_underscore_as_letters()
    {
        _app.Vision.Handler = _ => ItemsPayload(("100% cotton tee", "top"), ("wide_leg jeans", "bottom"), ("Plain sneakers", "shoes"));
        var (owner, _, _) = await _app.NewUserAsync("ex_item_escape");
        var postId = await _app.CheckAndPostAsync(owner);
        var client = _app.NewClient();

        async Task<List<Guid>> Find(string q) => Ids((await client.GetFromJsonAsync<JsonElement>("/api/search?q=" + Uri.EscapeDataString(q))).GetProperty("posts"));

        Assert.Equal([postId], await Find("100%"));
        Assert.Equal([postId], await Find("wide_leg"));
        // "_" is not "any one character" and "%" is not "anything": the near misses stay misses.
        Assert.Empty(await Find("100_"));
        Assert.Empty(await Find("wide%leg"));
        Assert.Empty(await Find("plain%sneakers"));
        Assert.Empty(await Find("plai_ sneakers"));
        // A bare wildcard on its own finds only names that carry the character.
        Assert.Equal([postId], await Find("%"));
        Assert.Equal([postId], await Find("_"));
    }
}
