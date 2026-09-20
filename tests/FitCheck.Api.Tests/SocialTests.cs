using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
// Round 14 — the community round's tests reach for the moderator flag (AdminChange) the way AdminTests do.
using FitCheck.Api.Data;

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

/// <summary>
/// Round 14 — post the look, keep the grade. The number, the sub-scores and the intent match are the author's to
/// publish or to keep, at the moment of posting and afterwards; everything else about the look works as it always did.
/// The routes are enumerated the way the block filter's are: every door a look comes out of is opened, and wherever the
/// look appears it carries no number — while its fire count and its place are exactly what they were.
/// </summary>
public class ScorePrivacyTests : IClassFixture<ScorePrivacyTests.PrivacyApp>
{
    public sealed class PrivacyApp : TestApp
    {
        public PrivacyApp() => Vision.Handler = _ => V2Payloads.Ok();
    }

    private readonly PrivacyApp _app;

    public ScorePrivacyTests(PrivacyApp app) => _app = app;

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static bool IsNull(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    /// <summary>Every look in an answer, however deep it sits (a feed's items, a challenge's entries, a board's rows).</summary>
    private static IEnumerable<JsonElement> LooksIn(JsonElement node, Guid postId)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (node.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    && Guid.TryParse(id.GetString(), out var parsed) && parsed == postId && node.TryGetProperty("headline", out _))
                {
                    yield return node;
                }

                foreach (var property in node.EnumerateObject())
                {
                    foreach (var found in LooksIn(property.Value, postId))
                    {
                        yield return found;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                {
                    foreach (var found in LooksIn(item, postId))
                    {
                        yield return found;
                    }
                }

                break;
        }
    }

    /// <summary>The number as the answer carries it: the score, the match and the breakdown, or null where they are gone.</summary>
    private static (int? Score, int? Match, bool Breakdown) Numbers(JsonElement look) => (
        IsNull(look, "score") ? null : look.GetProperty("score").GetInt32(),
        IsNull(look, "intentMatch") ? null : look.GetProperty("intentMatch").GetInt32(),
        !IsNull(look, "breakdown"));

    [Fact]
    public async Task A_private_grade_is_invisible_on_every_route_that_returns_a_look_while_the_fires_and_the_place_stand()
    {
        var (author, _, _) = await _app.NewUserAsync("gr_author");
        var (reader, _, _) = await _app.NewUserAsync("gr_reader");
        var (brand, _, _) = await _app.NewUserAsync("gr_brand", accountType: "Brand");
        var anonymous = _app.NewClient();

        // A look with everything on it: a tag, a mention of the brand, an entry in the brand's challenge, a fire and a save.
        var challenge = await Json(await brand.PostAsJsonAsync("/api/challenges", new
        {
            title = "Quiet week", brief = "Something quiet.", intent = "Office", prize = "A tote", endsAt = DateTime.UtcNow.AddDays(3)
        }));
        var challengeId = challenge.GetProperty("id").GetGuid();
        var checkId = await _app.CheckAsync(author, intent: "Office");
        var posted = await Json(await author.PostAsJsonAsync("/api/posts", new { checkId, caption = "#gradetag with @gr_brand", challengeId }));
        var postId = posted.GetProperty("id").GetGuid();
        Assert.Equal(7, posted.GetProperty("score").GetInt32());
        Assert.False(posted.GetProperty("scorePrivate").GetBoolean());

        await reader.PostAsync($"/api/posts/{postId}/fire", null);
        await reader.PostAsync($"/api/posts/{postId}/save", null);
        await brand.PostAsync($"/api/posts/{postId}/feature", null);
        await reader.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "the shoes are doing the work" });

        // Every door a look comes out of, for a reader who is not its author.
        string[] routes =
        [
            $"/api/posts/{postId}",
            "/api/feed?tab=fresh&limit=30",
            "/api/feed?tab=top&limit=30",
            "/api/feed?tab=foryou&limit=30",
            "/api/users/gr_author/posts",
            "/api/users/gr_author/community",
            "/api/users/gr_brand/featured",
            "/api/users/me/saved",
            "/api/tags/gradetag/posts",
            "/api/explore",
            "/api/search?q=running",
            "/api/items?q=running",
            $"/api/challenges/{challengeId}",
            "/api/challenges"
        ];

        async Task<int> SweepAsync(HttpClient client, bool numberExpected, bool signedIn = true)
        {
            var seen = 0;
            foreach (var route in routes)
            {
                // The saved list is the one door that only opens for somebody: a signed-out sweep walks past it.
                if (!signedIn && route.Contains("/me/saved", StringComparison.Ordinal))
                {
                    continue;
                }

                var response = await client.GetAsync(route);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                foreach (var look in LooksIn(await Json(response), postId))
                {
                    seen++;
                    var (score, match, breakdown) = Numbers(look);
                    if (numberExpected)
                    {
                        Assert.True(score == 7, $"{route}: expected the score, got {score?.ToString() ?? "null"}");
                        Assert.Equal(72, match);
                        Assert.True(breakdown, $"{route}: the breakdown went missing while the score is public");
                    }
                    else
                    {
                        Assert.True(score is null, $"{route}: the score {score} leaked on a look whose grade is private");
                        Assert.True(match is null, $"{route}: the intent match {match} leaked");
                        Assert.False(breakdown, $"{route}: the breakdown leaked");
                    }

                    // Nothing else about the look changes: the fire count is the same number to everybody, always.
                    Assert.Equal(1, look.GetProperty("fireCount").GetInt32());
                    Assert.Equal(1, look.GetProperty("commentCount").GetInt32());
                }
            }

            return seen;
        }

        var before = await SweepAsync(reader, numberExpected: true);
        Assert.True(before >= routes.Length, $"only {before} looks were found over {routes.Length} routes; the sweep is not sweeping");

        // The author keeps the grade.
        var flipped = await Json(await author.PatchAsJsonAsync($"/api/posts/{postId}/score-privacy", new { scorePrivate = true }));
        Assert.True(flipped.GetProperty("scorePrivate").GetBoolean());

        var after = await SweepAsync(reader, numberExpected: false);
        Assert.Equal(before, after);   // the look is on every list it was on: the row stays, only the number goes
        var anonymously = await SweepAsync(anonymous, numberExpected: false, signedIn: false);
        Assert.True(anonymously > 0);

        // The author still reads their own number, and so does a moderator, in the queue where a reported look is judged.
        var own = await Json(await author.GetAsync($"/api/posts/{postId}"));
        Assert.Equal(7, own.GetProperty("score").GetInt32());
        Assert.Equal(72, own.GetProperty("intentMatch").GetInt32());
        Assert.Equal(7, own.GetProperty("breakdown").GetProperty("fit").GetInt32());
        Assert.True(own.GetProperty("scorePrivate").GetBoolean());

        // The public page and the public profile grid say the same as the app: the look, and no number.
        var page = await anonymous.GetStringAsync($"/look/{postId}");
        Assert.DoesNotContain("score-ring", page, StringComparison.Ordinal);
        Assert.DoesNotContain("7 out of 10", page, StringComparison.Ordinal);
        Assert.Contains("Clean casual with one weak link", page, StringComparison.Ordinal);
        var profilePage = await anonymous.GetStringAsync("/u/gr_author");
        Assert.DoesNotContain("<span class=\"n\">7</span>", profilePage, StringComparison.Ordinal);

        // And back: the choice is not a one-way door.
        var restored = await Json(await author.PatchAsJsonAsync($"/api/posts/{postId}/score-privacy", new { scorePrivate = false }));
        Assert.False(restored.GetProperty("scorePrivate").GetBoolean());
        Assert.Equal(before, await SweepAsync(reader, numberExpected: true));
        Assert.Contains("7 out of 10", await anonymous.GetStringAsync($"/look/{postId}"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_choice_is_made_when_the_look_is_posted_and_is_the_authors_alone_afterwards()
    {
        var (author, _, _) = await _app.NewUserAsync("gr_quiet");
        var (stranger, _, _) = await _app.NewUserAsync("gr_stranger");
        var checkId = await _app.CheckAsync(author);
        var posted = await Json(await author.PostAsJsonAsync("/api/posts", new { checkId, scorePrivate = true }));
        var postId = posted.GetProperty("id").GetGuid();
        Assert.True(posted.GetProperty("scorePrivate").GetBoolean());
        Assert.Equal(7, posted.GetProperty("score").GetInt32());   // the author's own answer carries their own number

        var theirs = await Json(await stranger.GetAsync($"/api/posts/{postId}"));
        Assert.True(IsNull(theirs, "score"));
        Assert.True(theirs.GetProperty("scorePrivate").GetBoolean());
        Assert.Equal("Clean casual with one weak link", theirs.GetProperty("headline").GetString());

        // A stranger cannot flip it, and cannot learn from the refusal whether the look is even there.
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PatchAsJsonAsync($"/api/posts/{postId}/score-privacy", new { scorePrivate = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PatchAsJsonAsync($"/api/posts/{Guid.NewGuid()}/score-privacy", new { scorePrivate = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PatchAsJsonAsync($"/api/posts/{postId}/score-privacy", new { scorePrivate = false })).StatusCode);
        Assert.True((await Json(await author.GetAsync($"/api/posts/{postId}"))).GetProperty("scorePrivate").GetBoolean());

        // Asking for the state it is already in is not an error.
        Assert.True((await Json(await author.PatchAsJsonAsync($"/api/posts/{postId}/score-privacy", new { scorePrivate = true }))).GetProperty("scorePrivate").GetBoolean());
    }

    [Fact]
    public async Task A_moderator_reads_the_number_of_a_reported_look_whose_grade_is_private()
    {
        var (author, _, _) = await _app.NewUserAsync("gr_reported");
        var checkId = await _app.CheckAsync(author);
        var postId = (await Json(await author.PostAsJsonAsync("/api/posts", new { checkId, scorePrivate = true }))).GetProperty("id").GetGuid();
        var (moderator, _, _) = await _app.NewUserAsync("gr_mod");
        Assert.Equal(AdminChange.Changed, await _app.PromoteAsync("gr_mod"));
        for (var i = 0; i < 3; i++)
        {
            var reporter = (await _app.NewUserAsync("gr_rep" + i)).Client;
            await reporter.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" });
        }

        var queue = await moderator.GetFromJsonAsync<JsonElement>("/api/admin/queue");
        var item = queue.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == postId);
        Assert.Equal(7, item.GetProperty("post").GetProperty("score").GetInt32());
        Assert.True(item.GetProperty("post").GetProperty("scorePrivate").GetBoolean());
    }

    [Fact]
    public async Task The_before_after_share_is_the_authors_to_count_and_says_which_version_was_used()
    {
        var (author, _, _) = await _app.NewUserAsync("ba_author");
        var (stranger, _, _) = await _app.NewUserAsync("ba_stranger");
        var firstId = await _app.CheckAndPostAsync(author);
        var secondCheck = await _app.CheckAsync(author);
        var after = await Json(await author.PostAsJsonAsync("/api/posts", new { checkId = secondCheck, beforePostId = firstId }));
        var afterId = after.GetProperty("id").GetGuid();
        Assert.Equal(firstId, after.GetProperty("before").GetProperty("postId").GetGuid());
        Assert.Equal(7, after.GetProperty("before").GetProperty("score").GetInt32());

        // The earlier look's own choice decides its number on the strip; the thumbnail and the link stay either way.
        await author.PatchAsJsonAsync($"/api/posts/{firstId}/score-privacy", new { scorePrivate = true });
        var theirs = await Json(await stranger.GetAsync($"/api/posts/{afterId}"));
        Assert.True(IsNull(theirs.GetProperty("before"), "score"));
        Assert.Equal($"/api/posts/{firstId}/image", theirs.GetProperty("before").GetProperty("imageUrl").GetString());
        Assert.Equal(7, (await Json(await author.GetAsync($"/api/posts/{afterId}"))).GetProperty("before").GetProperty("score").GetInt32());

        // The tally: one row for the version with the two verdicts, one for the version with no numbers at all.
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync($"/api/posts/{afterId}/shared-after", new { withScores = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync($"/api/posts/{afterId}/shared-after", new { withScores = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsJsonAsync($"/api/posts/{afterId}/shared-after", new { withScores = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/posts/{afterId}/shared-after", new { withScores = true })).StatusCode);

        var (moderator, _, _) = await _app.NewUserAsync("ba_mod");
        Assert.Equal(AdminChange.Changed, await _app.PromoteAsync("ba_mod"));
        var social = (await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social");
        Assert.Equal(1, social.GetProperty("beforeAfterShares").GetInt32());
        Assert.Equal(2, social.GetProperty("beforeAfterSharesPlain").GetInt32());
        Assert.True(social.GetProperty("privateScores").GetInt32() >= 1);
    }

    [Fact]
    public async Task An_opener_is_counted_once_for_all_three_and_posts_nothing_by_itself()
    {
        var (author, _, _) = await _app.NewUserAsync("op_author");
        var (reader, _, _) = await _app.NewUserAsync("op_reader");
        var postId = await _app.CheckAndPostAsync(author);

        // An opener is a way into the box, never a comment: nothing is posted until the text is.
        Assert.Equal(HttpStatusCode.BadRequest, (await reader.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "", opener = "piece" })).StatusCode);
        Assert.Empty((await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}/comments")).EnumerateArray());

        var comment = await Json(await reader.PostAsJsonAsync($"/api/posts/{postId}/comments",
            new { text = "The piece doing the most work here is the coat", opener = "piece" }));
        Assert.Equal("The piece doing the most work here is the coat", comment.GetProperty("text").GetString());
        Assert.False(comment.TryGetProperty("opener", out _));   // what was typed is the comment; the opener is not kept

        await reader.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "I would try swapping the shoes", opener = "swap" });
        await reader.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "where is the coat from?", opener = "where" });
        await reader.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "no opener here" });
        await reader.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "a made-up one", opener = "whatever" });

        var (moderator, _, _) = await _app.NewUserAsync("op_mod");
        Assert.Equal(AdminChange.Changed, await _app.PromoteAsync("op_mod"));
        var social = (await moderator.GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social");
        Assert.Equal(3, social.GetProperty("commentOpeners").GetInt32());
        Assert.Equal(5, (await Json(await author.GetAsync($"/api/posts/{postId}"))).GetProperty("commentCount").GetInt32());
    }

    [Fact]
    public void The_three_openers_are_offered_in_all_four_languages()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FitCheck.Api", "wwwroot", "i18n"));
        foreach (var code in new[] { "en", "he", "ar", "ru" })
        {
            var keys = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, code + ".json"))).RootElement;
            foreach (var key in new[] { "comment.opener_piece", "comment.opener_swap", "comment.opener_where", "comment.openers_label" })
            {
                Assert.True(keys.TryGetProperty(key, out var value) && !string.IsNullOrWhiteSpace(value.GetString()), $"{code}.json is missing {key}");
            }

            // The before/after share and the constraint challenge speak every language too.
            foreach (var key in new[] { "share.before_after_title", "share.with_numbers", "share.without_numbers", "share.grade_keep", "challenge.rule_label" })
            {
                Assert.True(keys.TryGetProperty(key, out var value) && !string.IsNullOrWhiteSpace(value.GetString()), $"{code}.json is missing {key}");
            }
        }
    }
}

/// <summary>
/// Round 14 — the boards under a private grade, in their own week: the fire boards keep the look, its fires and its
/// place; the stylist's picks board, which ranks by the number itself, does not carry it at all — live, from the
/// archive, or in the hall.
/// </summary>
public class ScorePrivacyBoardTests : IClassFixture<ScorePrivacyBoardTests.BoardApp>
{
    public sealed class BoardApp : TestApp
    {
        public BoardApp() => Vision.Handler = _ => Payloads.Ok();
    }

    private readonly BoardApp _app;

    public ScorePrivacyBoardTests(BoardApp app) => _app = app;

    [Fact]
    public async Task A_private_grade_keeps_its_fires_and_its_place_and_leaves_the_picks_board()
    {
        _app.Clock.Now = BoardFixtures.Midweek(2);
        var (author, _, _) = await _app.NewUserAsync("gb_author");
        var (other, _, _) = await _app.NewUserAsync("gb_other");
        var monday = BoardFixtures.Local(BoardFixtures.Sunday(2).AddDays(1), 8);
        var quiet = await _app.CheckAndPostAsync(author);
        var loud = await _app.CheckAndPostAsync(other);
        await BoardFixtures.SetPostAsync(_app, quiet, createdAt: monday, score: 9);
        await BoardFixtures.SetPostAsync(_app, loud, createdAt: monday.AddHours(1), score: 6);

        // Two fans with a check each, so their fires count towards the week.
        var fans = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var (fan, fanId, _) = await _app.NewUserAsync("gb_fan" + i);
            await _app.CheckAsync(fan);
            fans.Add(fanId);
        }

        await BoardFixtures.FireAsync(_app, quiet, fans[0], monday.AddHours(2));
        await BoardFixtures.FireAsync(_app, quiet, fans[1], monday.AddHours(2));
        await BoardFixtures.FireAsync(_app, loud, fans[0], monday.AddHours(3));

        var before = await BoardFixtures.BoardAsync(_app);
        Assert.Equal([quiet, loud], BoardFixtures.PostIds(before, "looks"));
        Assert.Equal([quiet, loud], BoardFixtures.PostIds(before, "picks"));
        Assert.Equal(9, before.GetProperty("picks")[0].GetProperty("score").GetInt32());

        await author.PatchAsJsonAsync($"/api/posts/{quiet}/score-privacy", new { scorePrivate = true });

        var after = await BoardFixtures.BoardAsync(_app);
        // The looks board is ranked by fires: the row stays where it was, with the fires it had, and no number on its card.
        Assert.Equal([quiet, loud], BoardFixtures.PostIds(after, "looks"));
        Assert.Equal([(1, 2, quiet), (2, 1, loud)], BoardFixtures.LookRows(after, "looks"));
        var row = after.GetProperty("looks")[0].GetProperty("post");
        // A null is not written at all (AppJson: WhenWritingNull), so "no number" is the property being absent.
        Assert.False(row.TryGetProperty("score", out _));
        Assert.True(row.GetProperty("scorePrivate").GetBoolean());
        Assert.Equal(2, row.GetProperty("fireCount").GetInt32());
        // The picks board is ranked by the number: the look is not on it, and the one below moves up rather than into a gap.
        Assert.Equal([loud], BoardFixtures.PostIds(after, "picks"));
        Assert.Equal(1, after.GetProperty("picks")[0].GetProperty("rank").GetInt32());
        // The author's own places follow the same rule: still on the looks board, no longer among the picks.
        var mine = (await BoardFixtures.BoardAsync(_app, author)).GetProperty("me");
        Assert.Equal(1, mine.GetProperty("looks").GetInt32());
        Assert.False(mine.TryGetProperty("picks", out _));
    }

    [Fact]
    public async Task An_archived_picks_place_goes_when_the_grade_does_and_the_hall_agrees()
    {
        // Its own app: the hall lists every closed week there is, so this test needs a calendar nobody else wrote in.
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(4);
        var (author, _, _) = await app.NewUserAsync("gh_author");
        var (fan, fanId, _) = await app.NewUserAsync("gh_fan");
        await app.CheckAsync(fan);
        var monday = BoardFixtures.Local(BoardFixtures.Sunday(4).AddDays(1), 8);
        var post = await app.CheckAndPostAsync(author);
        await BoardFixtures.SetPostAsync(app, post, createdAt: monday, score: 8);
        await BoardFixtures.FireAsync(app, post, fanId, monday.AddHours(2));

        // The week closes with the look on the picks board, in the archive the hall reads.
        app.Clock.Now = BoardFixtures.Local(BoardFixtures.Sunday(5), 1);
        Assert.True(await BoardFixtures.CloseAsync(app) >= 1);
        var week = BoardFixtures.Key(BoardFixtures.Sunday(4));
        var closed = await BoardFixtures.BoardAsync(app, week: week);
        Assert.True(closed.GetProperty("closed").GetBoolean());
        Assert.Equal([post], BoardFixtures.PostIds(closed, "picks"));
        Assert.Equal([post], BoardFixtures.PostIds(closed, "looks"));
        Assert.Contains((await app.NewClient().GetFromJsonAsync<JsonElement>("/api/board/hall")).GetProperty("weeks").EnumerateArray()
            .SelectMany(w => w.GetProperty("winners").EnumerateArray()), w => w.GetProperty("board").GetString() == "picks");

        await author.PatchAsJsonAsync($"/api/posts/{post}/score-privacy", new { scorePrivate = true });

        var again = await BoardFixtures.BoardAsync(app, week: week);
        Assert.Empty(again.GetProperty("picks").EnumerateArray());
        // The place on the board that counts fires is untouched, and it is the same place it was.
        Assert.Equal([post], BoardFixtures.PostIds(again, "looks"));
        var hall = await app.NewClient().GetFromJsonAsync<JsonElement>("/api/board/hall");
        var winners = hall.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("winners").EnumerateArray()).ToList();
        Assert.DoesNotContain(winners, w => w.GetProperty("board").GetString() == "picks");
        Assert.Contains(winners, w => w.GetProperty("board").GetString() == "looks");
        // The archive row itself is untouched: the number was hidden, not deleted, and showing the grade brings it back.
        await author.PatchAsJsonAsync($"/api/posts/{post}/score-privacy", new { scorePrivate = false });
        Assert.Equal([post], BoardFixtures.PostIds(await BoardFixtures.BoardAsync(app, week: week), "picks"));
    }
}
