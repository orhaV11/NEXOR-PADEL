using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Domain;

namespace FitCheck.Api.Tests;

/// <summary>
/// Round 16 — a report that reaches nobody is not moderation. Until this, reporting a look wrote a row and stopped:
/// no notification, no badge, no alert, not a log line. The look stayed public — in the feed, in Explore, on its
/// public share page — until three DIFFERENT accounts reported it, and on a new server a second and a third stranger
/// reporting the same look does not happen. So the first report was also the last thing that happened.
/// </summary>
public class ReportReachesSomeoneTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task The_first_report_tells_every_moderator_and_never_names_the_reporter()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();

        var (author, _, _) = await app.NewUserAsync("reported_author");
        var (reporter, _, _) = await app.NewUserAsync("the_reporter");
        var (moderator, _, _) = await app.NewUserAsync("mod_one");
        var (other, _, _) = await app.NewUserAsync("not_a_mod");
        await app.PromoteAsync("mod_one");

        var postId = await app.CheckAndPostAsync(author);

        // Nobody has reported it yet: the moderator's activity is empty of reports.
        Assert.Empty(await ReportsFor(moderator));

        Assert.Equal(HttpStatusCode.NoContent, (await reporter.PostAsJsonAsync(
            $"/api/posts/{postId}/report", new { reason = "not an outfit" })).StatusCode);

        var told = await ReportsFor(moderator);
        var one = Assert.Single(told);
        // The actor is the account that was reported, so the moderator knows whose look it is...
        Assert.Equal("reported_author", one.GetProperty("actorHandle").GetString());
        // ...and never the person who reported it, whose name travelling would end reporting on this server.
        Assert.DoesNotContain("the_reporter", one.ToString(), StringComparison.Ordinal);
        Assert.Equal(postId, one.GetProperty("postId").GetGuid());

        // It went to the moderators, not to everyone: neither the author nor a bystander hears about it.
        Assert.Empty(await ReportsFor(author));
        Assert.Empty(await ReportsFor(other));

        // The badge is what actually makes them look, so it has to have moved.
        var me = await Json(await moderator.GetAsync("/api/auth/me"));
        Assert.True(me.GetProperty("unreadNotifications").GetInt32() >= 1);
    }

    [Fact]
    public async Task Three_people_reporting_one_look_is_one_queue_item_not_three()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();

        var (author, _, _) = await app.NewUserAsync("busy_author");
        var (moderator, _, _) = await app.NewUserAsync("mod_two");
        await app.PromoteAsync("mod_two");
        var postId = await app.CheckAndPostAsync(author);

        foreach (var name in new[] { "reporter_a", "reporter_b", "reporter_c" })
        {
            var (client, _, _) = await app.NewUserAsync(name);
            await client.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "no" });
        }

        Assert.Single(await ReportsFor(moderator));
    }

    /// <summary>
    /// The one that a block must not be able to switch off. Every other notification in the app is suppressed between
    /// two people who blocked each other, which is right for a fire and wrong for a report: it would let an account
    /// block the moderator and become unreportable.
    /// </summary>
    [Fact]
    public async Task A_block_between_the_moderator_and_the_reported_account_does_not_swallow_it()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();

        var (author, _, _) = await app.NewUserAsync("blocker");
        var (moderator, _, _) = await app.NewUserAsync("mod_three");
        await app.PromoteAsync("mod_three");
        var postId = await app.CheckAndPostAsync(author);

        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync("/api/users/mod_three/block", null)).StatusCode);

        var (reporter, _, _) = await app.NewUserAsync("reporter_d");
        await reporter.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "no" });

        Assert.Single(await ReportsFor(moderator));
    }

    [Fact]
    public async Task A_reported_comment_reaches_them_through_the_look_it_sits_under()
    {
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();

        var (author, _, _) = await app.NewUserAsync("comment_host");
        var (commenter, _, _) = await app.NewUserAsync("rude_one");
        var (moderator, _, _) = await app.NewUserAsync("mod_four");
        await app.PromoteAsync("mod_four");

        var postId = await app.CheckAndPostAsync(author);
        var comment = await Json(await commenter.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "something" }));
        var commentId = comment.GetProperty("id").GetGuid();

        var (reporter, _, _) = await app.NewUserAsync("reporter_e");
        Assert.Equal(HttpStatusCode.NoContent, (await reporter.PostAsJsonAsync(
            $"/api/comments/{commentId}/report", new { reason = "abuse" })).StatusCode);

        var one = Assert.Single(await ReportsFor(moderator));
        Assert.Equal("rude_one", one.GetProperty("actorHandle").GetString());
        Assert.Equal(postId, one.GetProperty("postId").GetGuid());
    }

    private static async Task<List<JsonElement>> ReportsFor(HttpClient client)
    {
        var body = await Json(await client.GetAsync("/api/notifications"));
        return body.GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("type").GetString() == NotificationType.Reported).ToList();
    }
}
