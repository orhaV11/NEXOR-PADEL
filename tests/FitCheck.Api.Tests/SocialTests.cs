using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FitCheck.Api.Tests;

public class SocialTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public SocialTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Posting_a_check_publishes_the_photo_and_only_the_public_parts_of_the_feedback()
    {
        var (client, _, _) = await _app.NewUserAsync("poster1", displayName: "Poster One");
        var checkId = await _app.CheckAsync(client, intent: "Office");

        var post = await _app.PostAsync(client, checkId, caption: "  first day  ");

        Assert.Equal("poster1", post.GetProperty("user").GetProperty("handle").GetString());
        Assert.Equal("Poster One", post.GetProperty("user").GetProperty("name").GetString());
        Assert.Equal("Office", post.GetProperty("intent").GetString());
        Assert.Equal(7, post.GetProperty("score").GetInt32());
        Assert.Equal(72, post.GetProperty("intentMatch").GetInt32());
        Assert.Equal("Clean casual with one weak link", post.GetProperty("headline").GetString());
        Assert.Equal("first day", post.GetProperty("caption").GetString());
        Assert.False(post.TryGetProperty("oneTip", out _));
        Assert.True(post.GetProperty("isMine").GetBoolean());
        var postId = post.GetProperty("id").GetGuid();

        // The photo is now public, to anyone, through the post only.
        var anonymous = _app.NewClient();
        var image = await anonymous.GetAsync($"/api/posts/{postId}/image");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TestImages.Jpeg(), await image.Content.ReadAsByteArrayAsync());

        var check = await client.GetFromJsonAsync<JsonElement>($"/api/checks/{checkId}");
        Assert.Equal(postId, check.GetProperty("postId").GetGuid());

        var view = await Json(await anonymous.GetAsync($"/api/posts/{postId}"));
        Assert.False(view.GetProperty("isMine").GetBoolean());
        Assert.False(view.GetProperty("fired").GetBoolean());
    }

    [Fact]
    public async Task Post_validation()
    {
        var (client, _, _) = await _app.NewUserAsync("validator1");
        var checkId = await _app.CheckAsync(client);
        await _app.PostAsync(client, checkId);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/posts", new { checkId })).StatusCode);

        var second = await _app.CheckAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/posts", new { checkId = second, caption = new string('c', 141) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/posts", new { checkId = second, products = new[] { new { label = "Tee", url = "https://shop.example/tee" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/posts", new { checkId = Guid.NewGuid() })).StatusCode);

        var (other, _, _) = await _app.NewUserAsync("validator2");
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync("/api/posts", new { checkId = second })).StatusCode);

        _app.Vision.Handler = _ => Payloads.NotOutfit();
        try
        {
            var notOutfit = await Json(await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg())));
            var response = await client.PostAsJsonAsync("/api/posts", new { checkId = notOutfit.GetProperty("id").GetGuid() });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Only checks the stylist scored can be posted.", (await Json(response)).GetProperty("error").GetString());
        }
        finally
        {
            _app.Vision.Handler = _ => Payloads.Ok();
        }
    }

    [Fact]
    public async Task Brands_can_attach_up_to_three_https_product_links()
    {
        var (brand, _, _) = await _app.NewUserAsync("shop1", accountType: "Brand", displayName: "Shop One");
        var checkId = await _app.CheckAsync(brand);

        var post = await _app.PostAsync(brand, checkId, products: new[]
        {
            new { label = "Linen shirt", url = "https://shop.example/linen", price = "240 ₪" },
            new { label = "Loafers", url = "https://shop.example/loafers", price = (string?)null }
        });

        var products = post.GetProperty("products").EnumerateArray().ToList();
        Assert.Equal(2, products.Count);
        Assert.Equal("240 ₪", products[0].GetProperty("price").GetString());
        Assert.Equal("Brand", post.GetProperty("user").GetProperty("accountType").GetString());

        var another = await _app.CheckAsync(brand);
        Assert.Equal(HttpStatusCode.BadRequest, (await brand.PostAsJsonAsync("/api/posts", new { checkId = another, products = new[] { new { label = "x", url = "http://insecure.example" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await brand.PostAsJsonAsync("/api/posts", new
        {
            checkId = another,
            products = Enumerable.Range(0, 4).Select(i => new { label = "p" + i, url = "https://shop.example/" + i }).ToArray()
        })).StatusCode);
    }

    [Fact]
    public async Task Feed_tabs_filters_and_paging()
    {
        var (a, _, _) = await _app.NewUserAsync("feed_a");
        var (b, _, _) = await _app.NewUserAsync("feed_b");
        var (reader, _, _) = await _app.NewUserAsync("feed_reader");
        var p1 = await _app.CheckAndPostAsync(a, intent: "Date");
        var p2 = await _app.CheckAndPostAsync(b, intent: "Office");
        var p3 = await _app.CheckAndPostAsync(a, intent: "Date");
        await reader.PostAsync($"/api/posts/{p2}/fire", null);
        await a.PostAsync($"/api/posts/{p2}/fire", null);
        await reader.PostAsync("/api/users/feed_a/follow", null);

        var fresh = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/feed?tab=fresh&limit=30");
        var freshIds = fresh.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(0, freshIds.IndexOf(p3));
        Assert.True(freshIds.IndexOf(p3) < freshIds.IndexOf(p2) && freshIds.IndexOf(p2) < freshIds.IndexOf(p1));

        var office = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/feed?intent=office&limit=30");
        Assert.All(office.GetProperty("items").EnumerateArray(), p => Assert.Equal("Office", p.GetProperty("intent").GetString()));
        Assert.Contains(office.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetGuid() == p2);

        var top = await reader.GetFromJsonAsync<JsonElement>("/api/feed?tab=top&limit=30");
        Assert.Equal(p2, top.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.True(top.GetProperty("items")[0].GetProperty("fired").GetBoolean());

        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().GetAsync("/api/feed?tab=following")).StatusCode);
        var following = await reader.GetFromJsonAsync<JsonElement>("/api/feed?tab=following&limit=30");
        var followingIds = following.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(p1, followingIds);
        Assert.Contains(p3, followingIds);
        Assert.DoesNotContain(p2, followingIds);

        var page = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/feed?limit=2");
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(2, page.GetProperty("nextOffset").GetInt32());
    }

    [Fact]
    public async Task Fire_is_idempotent_counts_once_and_notifies_the_owner_once()
    {
        var (owner, _, _) = await _app.NewUserAsync("fire_owner");
        var (fan, _, _) = await _app.NewUserAsync("fire_fan");
        var postId = await _app.CheckAndPostAsync(owner);

        var first = await Json(await fan.PostAsync($"/api/posts/{postId}/fire", null));
        Assert.Equal(1, first.GetProperty("fireCount").GetInt32());
        Assert.True(first.GetProperty("fired").GetBoolean());
        var second = await Json(await fan.PostAsync($"/api/posts/{postId}/fire", null));
        Assert.Equal(1, second.GetProperty("fireCount").GetInt32());
        await owner.PostAsync($"/api/posts/{postId}/fire", null);

        var notifications = await owner.GetFromJsonAsync<JsonElement>("/api/notifications");
        var fires = notifications.GetProperty("items").EnumerateArray().Where(n => n.GetProperty("type").GetString() == "fire").ToList();
        Assert.Single(fires);
        Assert.Equal("fire_fan", fires[0].GetProperty("actorHandle").GetString());
        Assert.Equal(postId, fires[0].GetProperty("postId").GetGuid());
        Assert.Equal(1, notifications.GetProperty("unread").GetInt32());
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("unreadNotifications").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/notifications/read", null)).StatusCode);
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("unread").GetInt32());

        var unfired = await Json(await fan.DeleteAsync($"/api/posts/{postId}/fire"));
        Assert.Equal(1, unfired.GetProperty("fireCount").GetInt32());
        Assert.False(unfired.GetProperty("fired").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PostAsync($"/api/posts/{postId}/fire", null)).StatusCode);
    }

    [Fact]
    public async Task Save_is_private_and_listable()
    {
        var (owner, _, _) = await _app.NewUserAsync("save_owner");
        var (saver, _, _) = await _app.NewUserAsync("save_saver");
        var postId = await _app.CheckAndPostAsync(owner);

        Assert.True((await Json(await saver.PostAsync($"/api/posts/{postId}/save", null))).GetProperty("saved").GetBoolean());
        var saved = await saver.GetFromJsonAsync<JsonElement>("/api/users/me/saved");
        Assert.Equal(postId, saved.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.True(saved.GetProperty("items")[0].GetProperty("saved").GetBoolean());

        Assert.False((await Json(await saver.DeleteAsync($"/api/posts/{postId}/save"))).GetProperty("saved").GetBoolean());
        Assert.Equal(0, (await saver.GetFromJsonAsync<JsonElement>("/api/users/me/saved")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Comments_are_public_deletable_by_author_or_post_owner_and_counted()
    {
        var (owner, _, _) = await _app.NewUserAsync("c_owner");
        var (friend, _, _) = await _app.NewUserAsync("c_friend");
        var (stranger, _, _) = await _app.NewUserAsync("c_stranger");
        var postId = await _app.CheckAndPostAsync(owner);

        var created = await friend.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "  love the layers  " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var comment = await Json(created);
        Assert.Equal("love the layers", comment.GetProperty("text").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await friend.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await friend.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = new string('x', 201) })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "hi" })).StatusCode);

        var list = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}/comments");
        Assert.Single(list.EnumerateArray());
        Assert.Equal("c_friend", list[0].GetProperty("user").GetProperty("handle").GetString());
        Assert.False(list[0].GetProperty("canDelete").GetBoolean());
        Assert.Equal(1, (await Json(await owner.GetAsync($"/api/posts/{postId}"))).GetProperty("commentCount").GetInt32());

        var ownerView = await owner.GetFromJsonAsync<JsonElement>($"/api/posts/{postId}/comments");
        Assert.True(ownerView[0].GetProperty("canDelete").GetBoolean());
        Assert.False(ownerView[0].GetProperty("isMine").GetBoolean());

        var notification = (await owner.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items")[0];
        Assert.Equal("comment", notification.GetProperty("type").GetString());

        var commentId = comment.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.DeleteAsync($"/api/comments/{commentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/comments/{commentId}")).StatusCode);
        Assert.Equal(0, (await Json(await owner.GetAsync($"/api/posts/{postId}"))).GetProperty("commentCount").GetInt32());

        var own = await Json(await friend.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "again" }));
        Assert.Equal(HttpStatusCode.NoContent, (await friend.DeleteAsync($"/api/comments/{own.GetProperty("id").GetGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Follow_updates_counts_notifies_and_refuses_self()
    {
        var (a, _, _) = await _app.NewUserAsync("follow_a");
        var (b, _, _) = await _app.NewUserAsync("follow_b");

        var state = await Json(await a.PostAsync("/api/users/Follow_B/follow", null));
        Assert.Equal(1, state.GetProperty("followers").GetInt32());
        Assert.True(state.GetProperty("following").GetBoolean());
        Assert.Equal(1, (await Json(await a.PostAsync("/api/users/follow_b/follow", null))).GetProperty("followers").GetInt32());

        var profile = await a.GetFromJsonAsync<JsonElement>("/api/users/follow_b");
        Assert.Equal(1, profile.GetProperty("followers").GetInt32());
        Assert.True(profile.GetProperty("viewer").GetProperty("following").GetBoolean());
        Assert.False(profile.GetProperty("viewer").GetProperty("isMe").GetBoolean());
        Assert.Equal(1, (await a.GetFromJsonAsync<JsonElement>("/api/users/follow_a")).GetProperty("following").GetInt32());

        Assert.Equal("follow", (await b.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items")[0].GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsync("/api/users/follow_a/follow", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync("/api/users/nobody_here/follow", null)).StatusCode);

        var unfollowed = await Json(await a.DeleteAsync("/api/users/follow_b/follow"));
        Assert.Equal(0, unfollowed.GetProperty("followers").GetInt32());
        Assert.False(unfollowed.GetProperty("following").GetBoolean());
    }

    [Fact]
    public async Task Profile_aggregates_public_numbers_only()
    {
        var (user, _, _) = await _app.NewUserAsync("profile_u", displayName: "Pro File");
        var (fan, _, _) = await _app.NewUserAsync("profile_fan");
        _app.Vision.Handler = _ => Payloads.Ok(score: 5);
        var low = await _app.CheckAndPostAsync(user);
        _app.Vision.Handler = _ => Payloads.Ok(score: 9);
        var high = await _app.CheckAndPostAsync(user);
        await _app.CheckAsync(user); // private, never posted
        _app.Vision.Handler = _ => Payloads.Ok();
        await fan.PostAsync($"/api/posts/{low}/fire", null);
        await fan.PostAsync($"/api/posts/{high}/fire", null);

        var profile = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/PROFILE_U");
        Assert.Equal("profile_u", profile.GetProperty("handle").GetString());
        Assert.Equal("Pro File", profile.GetProperty("name").GetString());
        Assert.Equal(2, profile.GetProperty("posts").GetInt32());
        Assert.Equal(2, profile.GetProperty("fireReceived").GetInt32());
        Assert.Equal(9, profile.GetProperty("bestScore").GetInt32());
        Assert.Equal(1, profile.GetProperty("streak").GetInt32());
        Assert.False(profile.GetProperty("viewer").GetProperty("isMe").GetBoolean());

        var posts = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/profile_u/posts");
        Assert.Equal(2, posts.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync("/api/users/does_not_exist")).StatusCode);
    }

    [Fact]
    public async Task Three_reports_hide_a_post_from_everyone_but_its_owner()
    {
        var (owner, _, _) = await _app.NewUserAsync("rep_owner");
        var reporters = new List<HttpClient>();
        for (var i = 0; i < 3; i++)
        {
            reporters.Add((await _app.NewUserAsync("rep_" + i)).Client);
        }

        var postId = await _app.CheckAndPostAsync(owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "mine" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await reporters[0].PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await reporters[0].PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam again" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _app.NewClient().GetAsync($"/api/posts/{postId}")).StatusCode);

        await reporters[1].PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" });
        await reporters[2].PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" });

        var anonymous = _app.NewClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/posts/{postId}/image")).StatusCode);
        Assert.DoesNotContain((await anonymous.GetFromJsonAsync<JsonElement>("/api/feed?limit=30")).GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetGuid() == postId);
        Assert.Equal(0, (await anonymous.GetFromJsonAsync<JsonElement>("/api/users/rep_owner")).GetProperty("posts").GetInt32());

        var ownerView = await Json(await owner.GetAsync($"/api/posts/{postId}"));
        Assert.True(ownerView.GetProperty("hidden").GetBoolean());
        var ownPosts = await owner.GetFromJsonAsync<JsonElement>("/api/users/rep_owner/posts");
        Assert.True(ownPosts.GetProperty("items")[0].GetProperty("hidden").GetBoolean());
    }

    [Fact]
    public async Task Three_reports_hide_a_comment()
    {
        var (owner, _, _) = await _app.NewUserAsync("crep_owner");
        var (troll, _, _) = await _app.NewUserAsync("crep_troll");
        var postId = await _app.CheckAndPostAsync(owner);
        var comment = await Json(await troll.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "meh" }));
        var commentId = comment.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await troll.PostAsJsonAsync($"/api/comments/{commentId}/report", new { reason = "x" })).StatusCode);
        for (var i = 0; i < 3; i++)
        {
            var reporter = (await _app.NewUserAsync("crep_" + i)).Client;
            Assert.Equal(HttpStatusCode.NoContent, (await reporter.PostAsJsonAsync($"/api/comments/{commentId}/report", new { reason = "rude" })).StatusCode);
        }

        Assert.Empty((await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}/comments")).EnumerateArray());
        Assert.Equal(0, (await Json(await owner.GetAsync($"/api/posts/{postId}"))).GetProperty("commentCount").GetInt32());
    }

    [Fact]
    public async Task Deleting_a_hidden_comment_does_not_lower_the_count_again()
    {
        var (owner, _, _) = await _app.NewUserAsync("hid_owner");
        var (troll, _, _) = await _app.NewUserAsync("hid_troll");
        var (friend, _, _) = await _app.NewUserAsync("hid_friend");
        var postId = await _app.CheckAndPostAsync(owner);
        var trollComment = (await Json(await troll.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "meh" }))).GetProperty("id").GetGuid();
        await friend.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "love it" });
        for (var i = 0; i < 3; i++)
        {
            var reporter = (await _app.NewUserAsync("hid_rep" + i)).Client;
            await reporter.PostAsJsonAsync($"/api/comments/{trollComment}/report", new { reason = "rude" });
        }

        Assert.Equal(1, (await Json(await owner.GetAsync($"/api/posts/{postId}"))).GetProperty("commentCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await troll.DeleteAsync($"/api/comments/{trollComment}")).StatusCode);
        Assert.Equal(1, (await Json(await owner.GetAsync($"/api/posts/{postId}"))).GetProperty("commentCount").GetInt32());
        Assert.Single((await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}/comments")).EnumerateArray());
    }

    [Fact]
    public async Task Deleting_a_post_makes_the_photo_private_again()
    {
        var (owner, _, _) = await _app.NewUserAsync("del_owner");
        var (fan, _, _) = await _app.NewUserAsync("del_fan");
        var checkId = await _app.CheckAsync(owner);
        var postId = (await _app.PostAsync(owner, checkId)).GetProperty("id").GetGuid();
        await fan.PostAsync($"/api/posts/{postId}/fire", null);
        await fan.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "hi" });

        Assert.Equal(HttpStatusCode.NotFound, (await fan.DeleteAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/posts/{postId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await fan.GetAsync($"/api/posts/{postId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fan.GetAsync($"/api/posts/{postId}")).StatusCode);
        var check = await owner.GetFromJsonAsync<JsonElement>($"/api/checks/{checkId}");
        Assert.False(check.TryGetProperty("postId", out var pid) && pid.ValueKind != JsonValueKind.Null);
        Assert.Equal("ok", check.GetProperty("status").GetString());
        // The check can be posted again.
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/posts", new { checkId })).StatusCode);
    }
}
