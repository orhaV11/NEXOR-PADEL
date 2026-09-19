using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FitCheck.Api.Tests;

/// <summary>
/// Blocking (Round 11), end to end: the three routes and their refusals, and the matrix behind them (feed, Explore,
/// search, the tag page, the saved list, the profile and its grids, the look and its photo, the comments, every act on a
/// look, the follow, the mention, the feature, the notifications), in both directions, with a third party and a
/// signed-out reader seeing everything as before. Nothing tells the blocked person: the same profile as a quiet
/// account's, the same 404 as a missing look, one sentence for every refusal.
/// </summary>
public class BlockTests : IClassFixture<TestApp>
{
    private const string Blocked = "You can't interact with this account.";

    private readonly TestApp _app;

    public BlockTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> ErrorOf(HttpResponseMessage response) => (await Json(response)).GetProperty("error").GetString()!;

    private static bool IsNull(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static List<Guid> Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

    private static async Task<List<Guid>> IdsAsync(HttpClient client, string path) => Ids(await client.GetFromJsonAsync<JsonElement>(path));

    private static async Task<List<string>> HandlesAsync(HttpClient client, string path, string property) =>
        (await client.GetFromJsonAsync<JsonElement>(path)).GetProperty(property).EnumerateArray().Select(u => u.GetProperty("user").GetProperty("handle").GetString()!).ToList();

    private static async Task<List<(string Type, string Actor)>> NotificationsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items").EnumerateArray()
        .Select(n => (n.GetProperty("type").GetString()!, n.GetProperty("actorHandle").GetString()!)).ToList();

    private static T ReadDb<T>(TestApp app, Func<AppDbContext, T> read)
    {
        using var scope = app.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task BlockAsync(HttpClient client, string handle) =>
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/users/{handle}/block", null)).StatusCode);

    /// <summary>A brand's open challenge, three days out, that a look with the Office intent can enter.</summary>
    private static async Task<Guid> OpenChallengeAsync(HttpClient brand, string title)
    {
        var response = await brand.PostAsJsonAsync("/api/challenges", new
        {
            title, brief = "Show us your sharpest office fit.", intent = "Office", prize = "A linen shirt of your choice",
            prizeUrl = "https://shop.example/linen", endsAt = DateTime.UtcNow.AddDays(3)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static List<Guid> PostIds(JsonElement array) => array.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

    /// <summary>The thumbnails a challenge card carries: the top three entries the reader may see.</summary>
    private static List<Guid> TopIds(JsonElement card) => PostIds(card.GetProperty("top"));

    /// <summary>The challenge's card as GET /api/challenges draws it for this reader.</summary>
    private static async Task<JsonElement> CardAsync(HttpClient client, Guid challengeId) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/challenges")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == challengeId);

    /// <summary>The same card as GET /api/explore embeds it.</summary>
    private static async Task<JsonElement> ExploreCardAsync(HttpClient client, Guid challengeId) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/explore")).GetProperty("challenges").EnumerateArray()
        .Single(c => c.GetProperty("id").GetGuid() == challengeId);

    /// <summary>The whole leaderboard of GET /api/challenges/{id}, in the order it answers.</summary>
    private static async Task<List<Guid>> LeaderboardAsync(HttpClient client, Guid challengeId) =>
        PostIds((await client.GetFromJsonAsync<JsonElement>($"/api/challenges/{challengeId}")).GetProperty("entriesByVotes"));

    private static async Task<List<Guid>> TodayIdsAsync(HttpClient client) =>
        PostIds((await client.GetFromJsonAsync<JsonElement>("/api/today")).GetProperty("posts"));

    private static async Task<List<Guid>> ItemIdsAsync(HttpClient client, string path) =>
        PostIds((await client.GetFromJsonAsync<JsonElement>(path)).GetProperty("posts"));

    private static async Task<JsonElement> BrandNamedAsync(HttpClient client, string query, string name) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/items/brands?q=" + Uri.EscapeDataString(query)))
        .GetProperty("items").EnumerateArray().Single(b => b.GetProperty("name").GetString() == name);

    /// <summary>The shoes row of a look tagged with a brand and a model, the way the owner tags a piece.</summary>
    private static async Task<Guid> TaggedLookAsync(TestApp app, HttpClient owner, string brand, string model)
    {
        var postId = await app.CheckAndPostAsync(owner);
        var rows = await ItemsTests.RowsAsync(app, postId);
        Assert.Equal(HttpStatusCode.OK, (await ItemsTests.PatchItemsAsync(owner, postId, new object[] { new { id = rows[2].Id, brand, model } })).StatusCode);
        return postId;
    }

    /// <summary>A VAPID key pair so push is on, the same way PushTests and BoardCloserTests make one.</summary>
    private static (string PublicKey, string PrivateKey) VapidKeys() => PushSender.GenerateVapidKeys();

    /// <summary>A browser's subscription for one person: the endpoint the recorder will see.</summary>
    private static async Task<string> SubscribeAsync(HttpClient client, string name)
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);
        var point = new byte[65];
        point[0] = 0x04;
        p.Q.X!.CopyTo(point, 1);
        p.Q.Y!.CopyTo(point, 33);
        var endpoint = $"https://push.example.test/send/{name}-{Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint, p256dh = PushSender.Base64Url(point), auth = PushSender.Base64Url(RandomNumberGenerator.GetBytes(16)) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return endpoint;
    }

    [Fact]
    public async Task A_block_writes_the_row_ends_both_follows_answers_the_account_and_tells_nobody()
    {
        var (a, aId, _) = await _app.NewUserAsync("blk_row_a");
        var (b, bId, _) = await _app.NewUserAsync("blk_row_b", displayName: "Row B");
        await a.PostAsync("/api/users/blk_row_b/follow", null);
        await b.PostAsync("/api/users/blk_row_a/follow", null);
        var before = await NotificationsAsync(b);

        var response = await a.PostAsync("/api/users/BLK_ROW_B/block", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await Json(response);
        Assert.Equal(["user", "createdAt"], dto.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("blk_row_b", dto.GetProperty("user").GetProperty("handle").GetString());
        Assert.Equal("Row B", dto.GetProperty("user").GetProperty("name").GetString());
        Assert.True(dto.GetProperty("createdAt").GetDateTime() > DateTime.UtcNow.AddMinutes(-1));

        Assert.Single(ReadDb(_app, db => db.Blocks.Where(x => x.BlockerId == aId && x.BlockedId == bId).ToList()));
        Assert.Empty(ReadDb(_app, db => db.Follows.Where(f => (f.FollowerId == aId && f.FollowedId == bId) || (f.FollowerId == bId && f.FollowedId == aId)).ToList()));
        var profile = await a.GetFromJsonAsync<JsonElement>("/api/users/blk_row_b");
        Assert.True(profile.GetProperty("viewer").GetProperty("blocked").GetBoolean());
        Assert.False(profile.GetProperty("viewer").GetProperty("following").GetBoolean());
        Assert.Equal(0, profile.GetProperty("followers").GetInt32());
        Assert.Equal(0, (await a.GetFromJsonAsync<JsonElement>("/api/users/blk_row_a")).GetProperty("followers").GetInt32());

        // Silent: nothing new for the blocked person (the blocker's old follow line leaves their list with every other line
        // from the blocker), and the follow they had is simply gone.
        Assert.Equal([("follow", "blk_row_a")], before);
        Assert.DoesNotContain(await NotificationsAsync(b), n => n.Actor != "blk_row_a");
        Assert.Empty(await NotificationsAsync(b));
        Assert.False((await b.GetFromJsonAsync<JsonElement>("/api/users/blk_row_a")).GetProperty("viewer").GetProperty("following").GetBoolean());

        // Unblocking removes the row and nothing comes back: the follows stay ended; the old lines read again.
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/users/blk_row_b/block")).StatusCode);
        Assert.Empty(ReadDb(_app, db => db.Blocks.Where(x => x.BlockerId == aId).ToList()));
        Assert.Equal(before, await NotificationsAsync(b));
        var after = await a.GetFromJsonAsync<JsonElement>("/api/users/blk_row_b");
        Assert.False(after.GetProperty("viewer").GetProperty("blocked").GetBoolean());
        Assert.False(after.GetProperty("viewer").GetProperty("following").GetBoolean());
        Assert.Equal(0, after.GetProperty("followers").GetInt32());
    }

    [Fact]
    public async Task The_refusals_self_twice_unknown_suspended_not_blocked_and_the_gates()
    {
        var (a, _, _) = await _app.NewUserAsync("blk_ref_a", language: "he");
        var (b, _, _) = await _app.NewUserAsync("blk_ref_b");
        await _app.NewUserAsync("blk_ref_gone");
        var (mod, _, _) = await _app.NewUserAsync("blk_ref_mod");
        await _app.PromoteAsync("blk_ref_mod");
        Assert.True((await mod.PostAsync("/api/admin/users/blk_ref_gone/suspend", null)).IsSuccessStatusCode);

        var self = await a.PostAsync("/api/users/blk_ref_a/block", null);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal("אי אפשר לחסום את עצמך.", await ErrorOf(self));
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync("/api/users/blk_nobody_here/block", null)).StatusCode);
        // A suspended account answers like a missing one, as its profile does.
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostAsync("/api/users/blk_ref_gone/block", null)).StatusCode);

        await BlockAsync(b, "blk_ref_a");
        var twice = await b.PostAsync("/api/users/blk_ref_a/block", null);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal("You've already blocked this account.", await ErrorOf(twice));
        // The other direction is its own row: both may block each other.
        await BlockAsync(a, "blk_ref_b");

        Assert.Equal(HttpStatusCode.NoContent, (await b.DeleteAsync("/api/users/blk_ref_a/block")).StatusCode);
        var again = await b.DeleteAsync("/api/users/blk_ref_a/block");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("You haven't blocked this account.", await ErrorOf(again));
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync("/api/users/blk_nobody_here/block")).StatusCode);
        // A's block of B stands on its own.
        Assert.True((await a.GetFromJsonAsync<JsonElement>("/api/users/blk_ref_b")).GetProperty("viewer").GetProperty("blocked").GetBoolean());

        var anonymous = _app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/users/blk_ref_b/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync("/api/users/blk_ref_b/block")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/users/me/blocks")).StatusCode);
        var bare = _app.BareClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.PostAsync("/api/users/blk_ref_b/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.DeleteAsync("/api/users/blk_ref_b/block")).StatusCode);
    }

    [Fact]
    public async Task The_list_is_the_callers_own_blocks_newest_first_and_whole()
    {
        var (a, _, _) = await _app.NewUserAsync("blk_list_a");
        var (b, _, _) = await _app.NewUserAsync("blk_list_b");
        await _app.NewUserAsync("blk_list_c", displayName: "List C");
        Assert.Empty((await a.GetFromJsonAsync<JsonElement>("/api/users/me/blocks")).GetProperty("items").EnumerateArray());

        await BlockAsync(a, "blk_list_c");
        await Task.Delay(20);
        await BlockAsync(a, "blk_list_b");
        await BlockAsync(b, "blk_list_a");

        var list = await a.GetFromJsonAsync<JsonElement>("/api/users/me/blocks");
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["blk_list_b", "blk_list_c"], items.Select(i => i.GetProperty("user").GetProperty("handle").GetString()).ToArray());
        Assert.Equal("List C", items[1].GetProperty("user").GetProperty("name").GetString());
        Assert.True(items[0].GetProperty("createdAt").GetDateTime() >= items[1].GetProperty("createdAt").GetDateTime());
        // Being blocked by someone is not in anyone's list.
        Assert.Equal(["blk_list_a"], (await b.GetFromJsonAsync<JsonElement>("/api/users/me/blocks")).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("user").GetProperty("handle").GetString()).ToArray());
    }

    [Fact]
    public async Task Looks_leave_each_others_feeds_explore_search_tag_pages_and_saved_list_both_ways()
    {
        var (a, _, _) = await _app.NewUserAsync("blk_lk_a");
        var (b, _, _) = await _app.NewUserAsync("blk_lk_b");
        var (c, _, _) = await _app.NewUserAsync("blk_lk_c");
        var fans = new List<HttpClient>();
        for (var i = 0; i < 3; i++)
        {
            fans.Add((await _app.NewUserAsync("blk_lk_fan" + i)).Client);
        }

        var postA = await _app.CheckAndPostAsync(a, caption: "#blocktag");
        var postB = await _app.CheckAndPostAsync(b, caption: "#blocktag");
        var postC = await _app.CheckAndPostAsync(c, caption: "#blocktag");
        foreach (var fan in fans)
        {
            foreach (var id in new[] { postA, postB, postC })
            {
                await fan.PostAsync($"/api/posts/{id}/fire", null);
            }
        }

        await a.PostAsync("/api/users/blk_lk_b/follow", null);
        await a.PostAsync("/api/users/blk_lk_c/follow", null);
        await b.PostAsync("/api/users/blk_lk_c/follow", null);
        await c.PostAsync("/api/users/blk_lk_a/follow", null);
        await c.PostAsync("/api/users/blk_lk_b/follow", null);
        await a.PostAsync($"/api/posts/{postB}/save", null);
        Assert.Contains(postB, await IdsAsync(a, "/api/users/me/saved"));

        await BlockAsync(a, "blk_lk_b");

        foreach (var tab in new[] { "fresh", "top", "foryou", "following" })
        {
            var mine = await IdsAsync(a, $"/api/feed?tab={tab}&limit=30");
            Assert.DoesNotContain(postB, mine);
            Assert.Contains(postC, mine);
            var theirs = await IdsAsync(b, $"/api/feed?tab={tab}&limit=30");
            Assert.DoesNotContain(postA, theirs);
            Assert.Contains(postC, theirs);
            var third = await IdsAsync(c, $"/api/feed?tab={tab}&limit=30");
            Assert.Contains(postA, third);
            Assert.Contains(postB, third);
        }

        Assert.Contains(postB, await IdsAsync(_app.NewClient(), "/api/feed?tab=fresh&limit=30"));

        // Explore: the top looks; search: the people and the looks by piece; the tag page.
        var exploreA = await a.GetFromJsonAsync<JsonElement>("/api/explore");
        var topA = exploreA.GetProperty("topLooks").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(postB, topA);
        Assert.Contains(postA, topA);
        var exploreB = await b.GetFromJsonAsync<JsonElement>("/api/explore");
        Assert.DoesNotContain(postA, exploreB.GetProperty("topLooks").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
        var exploreC = await c.GetFromJsonAsync<JsonElement>("/api/explore");
        Assert.Contains(postA, exploreC.GetProperty("topLooks").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
        Assert.Contains(postB, exploreC.GetProperty("topLooks").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));

        var peopleA = await HandlesAsync(a, "/api/search?q=blk_lk_", "users");
        Assert.DoesNotContain("blk_lk_b", peopleA);
        Assert.Contains("blk_lk_c", peopleA);
        var peopleB = await HandlesAsync(b, "/api/search?q=blk_lk_", "users");
        Assert.DoesNotContain("blk_lk_a", peopleB);
        Assert.Contains("blk_lk_c", peopleB);
        Assert.Contains("blk_lk_b", await HandlesAsync(c, "/api/search?q=blk_lk_", "users"));
        var piecesA = (await a.GetFromJsonAsync<JsonElement>("/api/search?q=white")).GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(postB, piecesA);
        Assert.Contains(postA, piecesA);
        Assert.DoesNotContain(postA, (await b.GetFromJsonAsync<JsonElement>("/api/search?q=white")).GetProperty("posts").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));

        var tagA = await IdsAsync(a, "/api/tags/blocktag/posts?limit=30");
        Assert.DoesNotContain(postB, tagA);
        Assert.Contains(postC, tagA);
        Assert.DoesNotContain(postA, await IdsAsync(b, "/api/tags/blocktag/posts?limit=30"));
        Assert.Contains(postB, await IdsAsync(_app.NewClient(), "/api/tags/blocktag/posts?limit=30"));

        // The saved row stays and the look is out of sight until an unblock.
        Assert.DoesNotContain(postB, await IdsAsync(a, "/api/users/me/saved"));
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/users/blk_lk_b/block")).StatusCode);
        Assert.Contains(postB, await IdsAsync(a, "/api/users/me/saved"));
        Assert.Contains(postB, await IdsAsync(a, "/api/feed?tab=fresh&limit=30"));
    }

    [Fact]
    public async Task The_profile_across_a_block_is_a_quiet_one_and_says_nothing_to_the_blocked_side()
    {
        var (a, _, _) = await _app.NewUserAsync("blk_pf_a");
        var (b, _, _) = await _app.NewUserAsync("blk_pf_b");
        var (c, _, _) = await _app.NewUserAsync("blk_pf_c");
        var postA1 = await _app.CheckAndPostAsync(a);
        var postA2 = await _app.CheckAndPostAsync(a);
        var postB = await _app.CheckAndPostAsync(b);
        await c.PostAsync($"/api/posts/{postA1}/fire", null);
        await c.PostAsync("/api/users/blk_pf_a/follow", null);

        await BlockAsync(a, "blk_pf_b");

        // The blocked person: a profile with nothing on it, with the same fields as any quiet profile and no word of the block.
        var seenByB = await b.GetFromJsonAsync<JsonElement>("/api/users/blk_pf_a");
        Assert.Equal("blk_pf_a", seenByB.GetProperty("handle").GetString());
        Assert.Equal(0, seenByB.GetProperty("posts").GetInt32());
        Assert.Equal(0, seenByB.GetProperty("fireReceived").GetInt32());
        Assert.True(IsNull(seenByB, "bestScore"));
        Assert.Equal(0, seenByB.GetProperty("featured").GetInt32());
        Assert.Equal(1, seenByB.GetProperty("followers").GetInt32());
        var viewer = seenByB.GetProperty("viewer");
        Assert.Equal(["isMe", "following", "blocked"], viewer.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.False(viewer.GetProperty("blocked").GetBoolean());
        Assert.DoesNotContain(seenByB.EnumerateObject(), p => p.Name.Contains("block", StringComparison.OrdinalIgnoreCase));
        var gridForB = await b.GetFromJsonAsync<JsonElement>("/api/users/blk_pf_a/posts");
        Assert.Empty(gridForB.GetProperty("items").EnumerateArray());
        Assert.True(IsNull(gridForB, "nextOffset"));
        Assert.Empty((await b.GetFromJsonAsync<JsonElement>("/api/users/blk_pf_a/featured")).GetProperty("items").EnumerateArray());
        Assert.Empty((await b.GetFromJsonAsync<JsonElement>("/api/users/blk_pf_a/community")).GetProperty("items").EnumerateArray());

        // The blocker: the same quiet profile, marked as theirs to unblock.
        var seenByA = await a.GetFromJsonAsync<JsonElement>("/api/users/blk_pf_b");
        Assert.Equal(0, seenByA.GetProperty("posts").GetInt32());
        Assert.True(seenByA.GetProperty("viewer").GetProperty("blocked").GetBoolean());
        Assert.Empty((await a.GetFromJsonAsync<JsonElement>("/api/users/blk_pf_b/posts")).GetProperty("items").EnumerateArray());

        // Everyone else, and the owner, see the profile as it is.
        foreach (var client in new[] { c, _app.NewClient(), a })
        {
            var full = await client.GetFromJsonAsync<JsonElement>("/api/users/blk_pf_a");
            Assert.Equal(2, full.GetProperty("posts").GetInt32());
            Assert.Equal(1, full.GetProperty("fireReceived").GetInt32());
            Assert.Equal([postA2, postA1], await IdsAsync(client, "/api/users/blk_pf_a/posts"));
        }

        Assert.Equal(1, (await c.GetFromJsonAsync<JsonElement>("/api/users/blk_pf_b")).GetProperty("posts").GetInt32());
        Assert.Equal([postB], await IdsAsync(_app.NewClient(), "/api/users/blk_pf_b/posts"));

        // The follow is refused with one sentence whichever side taps; undoing one is never refused.
        var fromB = await b.PostAsync("/api/users/blk_pf_a/follow", null);
        Assert.Equal(HttpStatusCode.Forbidden, fromB.StatusCode);
        Assert.Equal(Blocked, await ErrorOf(fromB));
        var fromA = await a.PostAsync("/api/users/blk_pf_b/follow", null);
        Assert.Equal(HttpStatusCode.Forbidden, fromA.StatusCode);
        Assert.Equal(Blocked, await ErrorOf(fromA));
        Assert.Equal(HttpStatusCode.OK, (await b.DeleteAsync("/api/users/blk_pf_a/follow")).StatusCode);
    }

    [Fact]
    public async Task A_look_reads_as_missing_its_comments_are_hidden_and_every_act_on_it_is_refused_both_ways()
    {
        var (a, _, _) = await _app.NewUserAsync("blk_pt_a");
        var (b, _, _) = await _app.NewUserAsync("blk_pt_b");
        var (c, _, _) = await _app.NewUserAsync("blk_pt_c");
        var postA = await _app.CheckAndPostAsync(a);
        var postB = await _app.CheckAndPostAsync(b);
        var commentB = (await Json(await b.PostAsJsonAsync($"/api/posts/{postA}/comments", new { text = "from b, before" }))).GetProperty("id").GetGuid();
        await c.PostAsJsonAsync($"/api/posts/{postA}/comments", new { text = "from c" });
        await a.PostAsJsonAsync($"/api/posts/{postB}/comments", new { text = "from a, before" });

        await BlockAsync(a, "blk_pt_b");

        foreach (var (viewer, look) in new[] { (b, postA), (a, postB) })
        {
            var missing = await viewer.GetAsync($"/api/posts/{look}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("We couldn't find this post.", await ErrorOf(missing));
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/posts/{look}/image")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/posts/{look}/video")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/posts/{look}/comments")).StatusCode);

            foreach (var act in new[]
                     {
                         await viewer.PostAsync($"/api/posts/{look}/fire", null),
                         await viewer.PostAsync($"/api/posts/{look}/save", null),
                         await viewer.PostAsJsonAsync($"/api/posts/{look}/comments", new { text = "after" })
                     })
            {
                Assert.Equal(HttpStatusCode.Forbidden, act.StatusCode);
                Assert.Equal(Blocked, await ErrorOf(act));
            }
        }

        // The look is what it was for everyone else; the block wrote nothing on it.
        var forC = await Json(await c.GetAsync($"/api/posts/{postA}"));
        Assert.Equal(0, forC.GetProperty("fireCount").GetInt32());
        Assert.Equal(2, forC.GetProperty("commentCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await _app.NewClient().GetAsync($"/api/posts/{postA}/image")).StatusCode);

        // The blocked person's comment on the blocker's look: out of the blocker's list, still there for everyone else, back after an unblock.
        var listForA = (await a.GetFromJsonAsync<JsonElement>($"/api/posts/{postA}/comments")).EnumerateArray().Select(x => x.GetProperty("user").GetProperty("handle").GetString()).ToList();
        Assert.Equal(["blk_pt_c"], listForA);
        var listForC = (await c.GetFromJsonAsync<JsonElement>($"/api/posts/{postA}/comments")).EnumerateArray().Select(x => x.GetProperty("user").GetProperty("handle").GetString()).ToList();
        Assert.Equal(["blk_pt_b", "blk_pt_c"], listForC);
        Assert.Single(ReadDb(_app, db => db.Comments.Where(x => x.Id == commentB && !x.Hidden).ToList()));
        // And the other way: the blocker's comment leaves the blocked person's look for the blocked person only (were they able to open it).
        Assert.Equal(["blk_pt_a"], (await c.GetFromJsonAsync<JsonElement>($"/api/posts/{postB}/comments")).EnumerateArray().Select(x => x.GetProperty("user").GetProperty("handle").GetString()).ToList());

        // A mention in a caption targets a person: refused across the block, whichever side writes it, before anything is saved.
        var checkB = await _app.CheckAsync(b);
        var mentionByB = await b.PostAsJsonAsync("/api/posts", new { checkId = checkB, caption = "with @blk_pt_a" });
        Assert.Equal(HttpStatusCode.Forbidden, mentionByB.StatusCode);
        Assert.Equal(Blocked, await ErrorOf(mentionByB));
        var checkA = await _app.CheckAsync(a);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync("/api/posts", new { checkId = checkA, caption = "with @blk_pt_b" })).StatusCode);
        Assert.Empty(ReadDb(_app, db => db.Posts.Where(p => p.CheckId == checkA || p.CheckId == checkB).ToList()));
        // The same check posts fine without the mention.
        Assert.Equal(HttpStatusCode.Created, (await b.PostAsJsonAsync("/api/posts", new { checkId = checkB, caption = "with @blk_pt_c" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/users/blk_pt_b/block")).StatusCode);
        Assert.Equal(["blk_pt_b", "blk_pt_c"], (await a.GetFromJsonAsync<JsonElement>($"/api/posts/{postA}/comments")).EnumerateArray().Select(x => x.GetProperty("user").GetProperty("handle").GetString()).ToList());
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync($"/api/posts/{postA}")).StatusCode);
    }

    [Fact]
    public async Task A_feature_across_a_block_is_refused_and_a_moderator_can_be_blocked_and_still_moderate()
    {
        var (person, _, _) = await _app.NewUserAsync("blk_ft_person");
        var (brand, _, _) = await _app.NewUserAsync("blk_ft_brand", accountType: "Brand");
        var (mod, _, _) = await _app.NewUserAsync("blk_ft_mod");
        await _app.PromoteAsync("blk_ft_mod");
        var look = await _app.CheckAndPostAsync(person, caption: "thanks @blk_ft_brand");

        await BlockAsync(person, "blk_ft_brand");
        var feature = await brand.PostAsync($"/api/posts/{look}/feature", null);
        Assert.Equal(HttpStatusCode.Forbidden, feature.StatusCode);
        Assert.Equal(Blocked, await ErrorOf(feature));
        // The brand's own community wall no longer carries the look either.
        Assert.Empty((await brand.GetFromJsonAsync<JsonElement>("/api/users/blk_ft_brand/community")).GetProperty("items").EnumerateArray());

        // A moderator blocked by the author still opens the look and its photo (the queue links there) and can act on it as a moderator,
        // while their personal acts are refused like anyone's.
        await BlockAsync(person, "blk_ft_mod");
        Assert.Equal(HttpStatusCode.OK, (await mod.GetAsync($"/api/posts/{look}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mod.GetAsync($"/api/posts/{look}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mod.GetAsync($"/api/posts/{look}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.PostAsync($"/api/posts/{look}/fire", null)).StatusCode);
        Assert.DoesNotContain(look, await IdsAsync(mod, "/api/feed?tab=fresh&limit=30"));
        Assert.True((await mod.PostAsync($"/api/admin/posts/{look}/hide", null)).IsSuccessStatusCode);
        Assert.True((await Json(await mod.GetAsync($"/api/posts/{look}"))).GetProperty("hidden").GetBoolean());
        Assert.True((await mod.PostAsync($"/api/admin/posts/{look}/unhide", null)).IsSuccessStatusCode);
        Assert.True((await mod.PostAsync("/api/admin/users/blk_ft_person/suspend", null)).IsSuccessStatusCode);
        Assert.True((await mod.PostAsync("/api/admin/users/blk_ft_person/unsuspend", null)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task No_notification_crosses_a_block_and_the_old_ones_leave_the_list_until_an_unblock()
    {
        var (a, _, _) = await _app.NewUserAsync("blk_nt_a");
        var (b, _, _) = await _app.NewUserAsync("blk_nt_b");
        var (c, _, _) = await _app.NewUserAsync("blk_nt_c");
        var (brand, _, _) = await _app.NewUserAsync("blk_nt_brand", accountType: "Brand");
        var postA = await _app.CheckAndPostAsync(a);
        await b.PostAsync($"/api/posts/{postA}/fire", null);
        await b.PostAsync("/api/users/blk_nt_a/follow", null);
        Assert.Equal([("follow", "blk_nt_b"), ("fire", "blk_nt_b")], await NotificationsAsync(a));
        Assert.Equal(2, (await a.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("unread").GetInt32());

        await BlockAsync(a, "blk_nt_b");
        Assert.Empty(await NotificationsAsync(a));
        Assert.Equal(0, (await a.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("unread").GetInt32());
        Assert.Equal(2, ReadDb(_app, db => db.Notifications.Count(n => n.ActorHandle == "blk_nt_b")));

        // The routes that would notify are refused first, and a vote is one of them: it is an act on somebody's look, so it
        // never lands and never moves their ranking. Nothing is written, so nothing can cross.
        var challengeId = await OpenChallengeAsync(brand, "Block week");
        var entry = await _app.CheckAndPostAsync(a, intent: "Office", challengeId: challengeId);
        var refused = await b.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entry });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(Blocked, await ErrorOf(refused));
        Assert.Empty(ReadDb(_app, db => db.ChallengeVotes.Where(v => v.ChallengeId == challengeId).ToList()));
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entry })).StatusCode);
        await c.PostAsync($"/api/posts/{postA}/fire", null);
        Assert.Equal([("fire", "blk_nt_c"), ("vote", "blk_nt_c")], await NotificationsAsync(a));
        Assert.Equal(0, ReadDb(_app, db => db.Notifications.Count(n => n.ActorHandle == "blk_nt_b" && n.Type == "vote")));

        // An entry into the challenge of a brand across a block stands, and the brand hears nothing of it.
        await BlockAsync(brand, "blk_nt_c");
        await _app.CheckAndPostAsync(c, intent: "Office", challengeId: challengeId);
        Assert.Contains(("entry", "blk_nt_a"), await NotificationsAsync(brand));
        Assert.DoesNotContain(await NotificationsAsync(brand), n => n.Actor == "blk_nt_c");

        // The other direction: the blocked person's list loses the blocker's lines too, and both come back after an unblock.
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/users/blk_nt_b/block")).StatusCode);
        Assert.Equal([("fire", "blk_nt_c"), ("vote", "blk_nt_c"), ("follow", "blk_nt_b"), ("fire", "blk_nt_b")], await NotificationsAsync(a));
    }

    [Fact]
    public async Task Deleting_either_account_removes_the_rows_and_frees_the_other_side()
    {
        var (a, aId, _) = await _app.NewUserAsync("blk_del_a");
        var (b, bId, _) = await _app.NewUserAsync("blk_del_b");
        var (c, cId, _) = await _app.NewUserAsync("blk_del_c");
        await BlockAsync(a, "blk_del_b");
        await BlockAsync(c, "blk_del_a");
        await BlockAsync(b, "blk_del_c");
        Assert.Equal(3, ReadDb(_app, db => db.Blocks.Count(x => x.BlockerId == aId || x.BlockedId == aId || x.BlockerId == bId || x.BlockerId == cId)));

        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/users/me")).StatusCode);

        Assert.Empty(ReadDb(_app, db => db.Blocks.Where(x => x.BlockerId == aId || x.BlockedId == aId).ToList()));
        Assert.Single(ReadDb(_app, db => db.Blocks.Where(x => x.BlockerId == bId && x.BlockedId == cId).ToList()));
        Assert.Empty((await c.GetFromJsonAsync<JsonElement>("/api/users/me/blocks")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync("/api/users/blk_del_a")).StatusCode);
    }

    [Fact]
    public async Task A_challenge_entry_leaves_all_three_boards_across_a_block_while_the_tallies_stand()
    {
        var (brand, _, _) = await _app.NewUserAsync("blk_ch_brand", accountType: "Brand");
        var (a, _, _) = await _app.NewUserAsync("blk_ch_a");
        var (b, _, _) = await _app.NewUserAsync("blk_ch_b");
        var (c, _, _) = await _app.NewUserAsync("blk_ch_c");
        var anyone = _app.NewClient();
        var challengeId = await OpenChallengeAsync(brand, "Sharp office");
        var entryA = await _app.CheckAndPostAsync(a, intent: "Office", challengeId: challengeId);
        var entryB = await _app.CheckAndPostAsync(b, intent: "Office", challengeId: challengeId);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryB })).StatusCode);

        await BlockAsync(a, "blk_ch_b");

        // The tallies are the challenge's own and read the same to everyone: two entries and one vote, block or no block.
        // A number that moved would be the block showing, which is the one thing nothing may say.
        foreach (var reader in new[] { a, b, c, anyone })
        {
            var card = await CardAsync(reader, challengeId);
            Assert.Equal(2, card.GetProperty("entries").GetInt32());
            Assert.Equal(1, card.GetProperty("votes").GetInt32());
            Assert.Equal(2, (await ExploreCardAsync(reader, challengeId)).GetProperty("entries").GetInt32());
            Assert.Equal(2, (await Json(await reader.GetAsync($"/api/challenges/{challengeId}"))).GetProperty("challenge").GetProperty("entries").GetInt32());
        }

        // The look itself is off every list that draws it, both ways round, and nobody else notices anything.
        Assert.Equal([entryA], TopIds(await CardAsync(a, challengeId)));
        Assert.Equal([entryA], TopIds(await ExploreCardAsync(a, challengeId)));
        Assert.Equal([entryA], await LeaderboardAsync(a, challengeId));
        Assert.Equal([entryB], TopIds(await CardAsync(b, challengeId)));
        Assert.Equal([entryB], TopIds(await ExploreCardAsync(b, challengeId)));
        Assert.Equal([entryB], await LeaderboardAsync(b, challengeId));
        Assert.Equal([entryB, entryA], TopIds(await CardAsync(c, challengeId)));
        Assert.Equal([entryB, entryA], await LeaderboardAsync(c, challengeId));
        Assert.Equal([entryB, entryA], TopIds(await ExploreCardAsync(anyone, challengeId)));
        Assert.Equal([entryB, entryA], await LeaderboardAsync(anyone, challengeId));
        // The same answer the look itself gives on either side: missing.
        Assert.Equal(HttpStatusCode.NotFound, (await a.GetAsync($"/api/posts/{entryB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/posts/{entryA}")).StatusCode);

        // Each side still reads its own entry on its own card, and the challenge still knows they entered.
        var mine = await CardAsync(a, challengeId);
        Assert.True(mine.GetProperty("viewer").GetProperty("hasEntered").GetBoolean());
        Assert.Equal(entryA, mine.GetProperty("viewer").GetProperty("myEntryId").GetGuid());

        // A vote is refused whichever side taps, with the one sentence, and the tally does not move.
        var blocker = await a.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryB });
        Assert.Equal(HttpStatusCode.Forbidden, blocker.StatusCode);
        Assert.Equal(Blocked, await ErrorOf(blocker));
        var blocked = await b.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryA });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(Blocked, await ErrorOf(blocked));
        Assert.Single(ReadDb(_app, db => db.ChallengeVotes.Where(v => v.ChallengeId == challengeId).ToList()));
        Assert.Equal(1, (await CardAsync(c, challengeId)).GetProperty("votes").GetInt32());

        // Unblocking brings the board back whole for both.
        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/users/blk_ch_b/block")).StatusCode);
        Assert.Equal([entryB, entryA], await LeaderboardAsync(a, challengeId));
        Assert.Equal([entryB, entryA], await LeaderboardAsync(b, challengeId));
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryB })).StatusCode);
    }

    [Fact]
    public async Task Todays_prompt_hides_a_look_across_a_block_and_agrees_with_its_own_tag_page()
    {
        var (a, _, _) = await _app.NewUserAsync("blk_td_a");
        var (b, _, _) = await _app.NewUserAsync("blk_td_b");
        var (c, _, _) = await _app.NewUserAsync("blk_td_c");
        var anyone = _app.NewClient();
        var tag = (await anyone.GetFromJsonAsync<JsonElement>("/api/today")).GetProperty("tag").GetString()!;
        var lookA = await _app.CheckAndPostAsync(a, caption: "#" + tag);
        var lookB = await _app.CheckAndPostAsync(b, caption: "#" + tag);
        var lookC = await _app.CheckAndPostAsync(c, caption: "#" + tag);

        await BlockAsync(a, "blk_td_b");

        Assert.Equal([lookC, lookA], await TodayIdsAsync(a));
        Assert.Equal([lookC, lookB], await TodayIdsAsync(b));
        Assert.Equal([lookC, lookB, lookA], await TodayIdsAsync(c));
        Assert.Equal([lookC, lookB, lookA], await TodayIdsAsync(anyone));
        // The prompt and the hashtag behind it now answer alike, which was the whole complaint.
        Assert.Equal(await TodayIdsAsync(a), await IdsAsync(a, $"/api/tags/{tag}/posts"));
        Assert.Equal(await TodayIdsAsync(b), await IdsAsync(b, $"/api/tags/{tag}/posts"));

        // The probe reads the caller's own rows only: both still know they posted today.
        Assert.True((await a.GetFromJsonAsync<JsonElement>("/api/today")).GetProperty("posted").GetBoolean());
        Assert.True((await b.GetFromJsonAsync<JsonElement>("/api/today")).GetProperty("posted").GetBoolean());
    }

    [Fact]
    public async Task The_item_search_hides_a_look_across_a_block_while_the_brand_counts_stand()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => ItemsTests.StylistPayload();
        var (a, _, _) = await app.NewUserAsync("blk_it_a");
        var (b, _, _) = await app.NewUserAsync("blk_it_b");
        var (c, _, _) = await app.NewUserAsync("blk_it_c");
        await app.NewUserAsync("blk_it_house", accountType: "Brand", displayName: "Nimbus");
        var anyone = app.NewClient();
        var lookA = await TaggedLookAsync(app, a, "Nimbus", "Trail 7");
        var lookB = await TaggedLookAsync(app, b, "Nimbus", "Trail 8");

        await BlockAsync(a, "blk_it_b");
        await BlockAsync(a, "blk_it_house");

        // The one look list the filter had never reached: a look either side of a block is off the page, both ways.
        Assert.Equal([lookA], await ItemIdsAsync(a, "/api/items?brand=nimbus"));
        Assert.Equal([lookA], await ItemIdsAsync(a, "/api/items?q=trail"));
        Assert.Equal([lookA], await ItemIdsAsync(a, "/api/items?category=shoes&brand=Nimbus"));
        Assert.Equal([lookB], await ItemIdsAsync(b, "/api/items?brand=nimbus"));
        Assert.Equal([lookB, lookA], await ItemIdsAsync(c, "/api/items?brand=nimbus"));
        Assert.Equal([lookB, lookA], await ItemIdsAsync(anyone, "/api/items?brand=nimbus"));
        // The page still heads itself with the spelling the looks carry, counted over all of them.
        Assert.Equal("Nimbus", (await a.GetFromJsonAsync<JsonElement>("/api/items?brand=nimbus")).GetProperty("brand").GetString());

        // The autocomplete: two looks carry Nimbus whoever asks, because a tally that moved would be the block showing.
        // What does go is the brand ACCOUNT of that name — a person, and people leave the lists of a block.
        var mine = await BrandNamedAsync(a, "nimbus", "Nimbus");
        Assert.Equal(2, mine.GetProperty("looks").GetInt32());
        Assert.False(mine.TryGetProperty("account", out _));
        var theirs = await BrandNamedAsync(c, "nimbus", "Nimbus");
        Assert.Equal(2, theirs.GetProperty("looks").GetInt32());
        Assert.Equal("blk_it_house", theirs.GetProperty("account").GetProperty("handle").GetString());
        Assert.Equal(2, (await BrandNamedAsync(anyone, "nimbus", "Nimbus")).GetProperty("looks").GetInt32());
        Assert.Equal(2, (await BrandNamedAsync(b, "nimbus", "Nimbus")).GetProperty("looks").GetInt32());
    }

    [Fact]
    public async Task A_challenge_result_across_a_block_writes_no_line_and_queues_no_push()
    {
        var (publicKey, privateKey) = VapidKeys();
        using var app = new TestApp { PushPublicKey = publicKey, PushPrivateKey = privateKey };
        app.Vision.Handler = _ => Payloads.Ok();
        var (brand, _, _) = await app.NewUserAsync("blk_won_brand", accountType: "Brand");
        var (winner, _, _) = await app.NewUserAsync("blk_won_win");
        var (fan, _, _) = await app.NewUserAsync("blk_won_fan");
        var endpoint = await SubscribeAsync(winner, "blk_won_win");
        var challengeId = await OpenChallengeAsync(brand, "Ends across a block");
        var entry = await app.CheckAndPostAsync(winner, intent: "Office", challengeId: challengeId);
        await BlockAsync(winner, "blk_won_brand");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Challenges.Where(c => c.Id == challengeId).ExecuteUpdateAsync(s => s.SetProperty(c => c.EndsAt, DateTime.UtcNow.AddHours(-1)));
        }

        // The result is fixed from every entry, so it never turns on who reads it first.
        Assert.Equal(HttpStatusCode.OK, (await app.NewClient().GetAsync($"/api/challenges/{challengeId}")).StatusCode);
        Assert.Equal(entry, ReadDb(app, db => db.Challenges.Single(c => c.Id == challengeId).WinnerPostId));

        // Neither line is written, either way round, so neither can be pushed.
        Assert.Equal(0, ReadDb(app, db => db.Notifications.Count(n => n.Type == "won" || n.Type == "ended")));
        Assert.DoesNotContain(await NotificationsAsync(winner), n => n.Type == "won");
        Assert.DoesNotContain(await NotificationsAsync(brand), n => n.Type == "ended");

        // And the job never reached the queue: one reader drains it in order, so a "won" queued before this fire would have
        // gone out first. The first request to arrive says whether anything crossed.
        Assert.Equal(HttpStatusCode.OK, (await fan.PostAsync($"/api/posts/{entry}/fire", null)).StatusCode);
        var arrived = await app.PushHandler.WaitForAsync(endpoint);
        Assert.StartsWith("fire-", Assert.Single(arrived).Topic);
        Assert.DoesNotContain(app.PushHandler.Requests, r => r.Topic is not null && (r.Topic.StartsWith("won-") || r.Topic.StartsWith("ended-")));
    }

    [Fact]
    public async Task A_board_place_still_reaches_someone_who_has_blocked_another_account()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();
        app.Clock.Now = BoardFixtures.Midweek(0);
        var (a, _, _) = await app.NewUserAsync("blk_br_a");
        await app.NewUserAsync("blk_br_b");
        var (fan, fanId, _) = await app.NewUserAsync("blk_br_fan");
        // A fire only counts from someone who has checked a look of their own.
        await app.CheckAsync(fan);
        var look = await app.CheckAndPostAsync(a);
        var monday = BoardFixtures.Local(BoardFixtures.Sunday(0).AddDays(1), 8);
        await BoardFixtures.SetPostAsync(app, look, createdAt: monday);
        await BoardFixtures.FireAsync(app, look, fanId, monday.AddHours(2));
        // The recipient has a block, so the pair is looked up; the actor of a board place is the person themselves, which
        // is not a pair, and the line has to go.
        await BlockAsync(a, "blk_br_b");

        app.Clock.Now = BoardFixtures.Local(BoardFixtures.Sunday(1), 0, 3);
        Assert.True(await BoardFixtures.CloseAsync(app) > 0);
        var told = Assert.Single(await BoardFixtures.BoardRankNotificationsAsync(a));
        Assert.Equal("blk_br_a", told.GetProperty("actorHandle").GetString());
        Assert.Equal(1, told.GetProperty("rank").GetInt32());
    }

    [Fact]
    public async Task A_block_is_logged_by_ids_never_by_handle()
    {
        using var app = new BlockLogApp();
        var (a, aId, _) = await app.NewUserAsync("blk_log_a");
        var (_, bId, _) = await app.NewUserAsync("blk_log_b");
        await BlockAsync(a, "blk_log_b");

        var line = Assert.Single(app.Logs.Lines, l => l.Message.StartsWith("Block: ", StringComparison.Ordinal));
        Assert.Equal($"Block: {aId} blocked {bId}", line.Message);
        Assert.EndsWith("BlockEndpoints", line.Category);
        Assert.DoesNotContain("blk_log", line.Message);
    }
}

/// <summary>The app with a recorder on its logs, for the one line a block writes.</summary>
public sealed class BlockLogApp : TestApp
{
    public RecordingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }
}

public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<(string Category, string Message)> _lines = [];

    public IReadOnlyList<(string Category, string Message)> Lines
    {
        get
        {
            lock (_lines)
            {
                return _lines.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Recorder(RecordingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._lines)
            {
                owner._lines.Add((category, formatter(state, exception)));
            }
        }
    }
}
