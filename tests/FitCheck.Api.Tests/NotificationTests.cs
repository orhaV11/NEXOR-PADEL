using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// The activity badge and the activity list, which are one number or a bug: both read
/// <see cref="FitCheck.Api.Endpoints.NotificationEndpoints.VisibleAsync"/>, so a line from an account on either side of a
/// block (Round 11) raises neither the badge nor a row, an unblock brings the two back together, and nothing is left
/// counting what the list hides — a badge the list contradicts would come back on every load and never clear.
/// </summary>
public class NotificationTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public NotificationTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    /// <summary>The tab badge, as every route that answers with "me" carries it.</summary>
    private static async Task<int> BadgeAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("unreadNotifications").GetInt32();

    private static async Task<(int Unread, List<(string Type, string Actor, bool Read)> Items)> ActivityAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        var items = page.GetProperty("items").EnumerateArray()
            .Select(n => (n.GetProperty("type").GetString()!, n.GetProperty("actorHandle").GetString()!, n.GetProperty("read").GetBoolean()))
            .ToList<(string Type, string Actor, bool Read)>();
        return (page.GetProperty("unread").GetInt32(), items);
    }

    /// <summary>The badge, the list's own count and the unread rows the list actually shows: one number, or the finding is back.</summary>
    private static async Task<int> AgreedAsync(HttpClient client)
    {
        var badge = await BadgeAsync(client);
        var (unread, items) = await ActivityAsync(client);
        Assert.Equal(badge, unread);
        Assert.Equal(badge, items.Count(i => !i.Read));
        return badge;
    }

    private T ReadDb<T>(Func<AppDbContext, T> read)
    {
        using var scope = _app.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task A_line_from_a_blocked_account_raises_neither_the_badge_nor_a_row_and_an_unblock_brings_both_back()
    {
        var (a, aId, _) = await _app.NewUserAsync("ntf_blk_a");
        var (b, _, _) = await _app.NewUserAsync("ntf_blk_b");
        var (c, _, _) = await _app.NewUserAsync("ntf_blk_c");
        Assert.Equal(0, await AgreedAsync(a));

        var look = await _app.CheckAndPostAsync(a);
        await b.PostAsync($"/api/posts/{look}/fire", null);
        await c.PostAsync("/api/users/ntf_blk_a/follow", null);
        Assert.Equal(2, await AgreedAsync(a));

        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync("/api/users/ntf_blk_b/block", null)).StatusCode);
        Assert.Equal(1, await AgreedAsync(a));
        Assert.DoesNotContain((await ActivityAsync(a)).Items, i => i.Actor == "ntf_blk_b");
        // The row is still there, unread: only the view of it went away, so an unblock has something to bring back.
        Assert.Equal(1, ReadDb(db => db.Notifications.Count(n => n.UserId == aId && n.ActorHandle == "ntf_blk_b" && n.ReadAt == null)));
        // And it does not come back on the next load: the phantom is gone, not deferred to the following request.
        Assert.Equal(1, await AgreedAsync(a));

        // A fresh sign-in reads the same number: every route that answers with "me" goes through the one count.
        var again = _app.NewClient();
        var login = await again.PostAsJsonAsync("/api/auth/login", new { handle = "ntf_blk_a", password = "password123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(1, (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unreadNotifications").GetInt32());
        Assert.Equal(1, await AgreedAsync(again));

        Assert.Equal(HttpStatusCode.NoContent, (await a.DeleteAsync("/api/users/ntf_blk_b/block")).StatusCode);
        Assert.Equal(2, await AgreedAsync(a));
        Assert.Contains((await ActivityAsync(a)).Items, i => i.Actor == "ntf_blk_b" && !i.Read);
    }

    [Fact]
    public async Task A_block_from_either_side_while_activity_is_open_leaves_the_badge_and_the_list_agreeing()
    {
        var (a, _, _) = await _app.NewUserAsync("ntf_open_a");
        var (b, _, _) = await _app.NewUserAsync("ntf_open_b");
        var (c, _, _) = await _app.NewUserAsync("ntf_open_c");
        var look = await _app.CheckAndPostAsync(a);
        await b.PostAsync($"/api/posts/{look}/fire", null);
        await c.PostAsync($"/api/posts/{look}/fire", null);
        // Activity is open with both lines on it.
        Assert.Equal(2, await AgreedAsync(a));

        // The block is made from the open view (a look's "…"): the next load of the list and of the badge say the same thing.
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsync("/api/users/ntf_open_b/block", null)).StatusCode);
        Assert.Equal(1, await AgreedAsync(a));
        Assert.Equal([("fire", "ntf_open_c", false)], (await ActivityAsync(a)).Items);

        // The other direction hides just the same, and the side that was blocked is never told: the line is simply not there.
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/users/ntf_open_a/block", null)).StatusCode);
        Assert.Equal(0, await AgreedAsync(a));
        Assert.Empty((await ActivityAsync(a)).Items);

        // Opening Activity marks what was there read; the badge goes with it and stays at zero.
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync("/api/notifications/read", null)).StatusCode);
        Assert.Equal(0, await AgreedAsync(a));
    }

    [Fact]
    public async Task With_no_block_in_sight_the_badge_still_follows_the_list_through_a_fire_a_read_and_the_next_line()
    {
        var (a, _, _) = await _app.NewUserAsync("ntf_plain_a");
        var (b, _, _) = await _app.NewUserAsync("ntf_plain_b");
        Assert.Equal(0, await AgreedAsync(a));

        var look = await _app.CheckAndPostAsync(a);
        await b.PostAsync($"/api/posts/{look}/fire", null);
        Assert.Equal(1, await AgreedAsync(a));

        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync("/api/notifications/read", null)).StatusCode);
        Assert.Equal(0, await AgreedAsync(a));

        await b.PostAsync("/api/users/ntf_plain_a/follow", null);
        Assert.Equal(1, await AgreedAsync(a));
    }
}
