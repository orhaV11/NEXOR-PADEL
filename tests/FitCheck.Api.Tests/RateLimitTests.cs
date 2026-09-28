using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using Microsoft.AspNetCore.Hosting;

namespace FitCheck.Api.Tests;

/// <summary>
/// Comments and reports are capped per account per hour (Limits:CommentsPerHour, default 30; Limits:ReportsPerHour, default
/// 20). The bucket is the signed-in account, never the address, so the next person is not slowed down by the first; the
/// answer is 429 with a friendly message and a Retry-After, and nothing is written.
/// </summary>
public class RateLimitTests
{
    private const string TooFast = "Slow down a little. Try again in a bit.";

    /// <summary>The app with the caps lowered, so a test does not need thirty calls to see the option honoured.</summary>
    private sealed class LimitedApp(int commentsPerHour, int reportsPerHour) : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Limits:CommentsPerHour", commentsPerHour.ToString());
            builder.UseSetting("Limits:ReportsPerHour", reportsPerHour.ToString());
        }
    }

    /// <summary>Round 20: the app with the moderator's account-action cap lowered (Limits:AdminActionsPerHour, default 120).</summary>
    private sealed class AdminLimitedApp(int adminActionsPerHour) : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Limits:AdminActionsPerHour", adminActionsPerHour.ToString());
        }
    }

    private static Task<HttpResponseMessage> CommentAsync(HttpClient client, Guid postId, string text) =>
        client.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text });

    private static async Task<string?> ErrorAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();

    [Fact]
    public async Task The_thirty_first_comment_in_an_hour_is_refused_and_the_next_person_is_not()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("owner");
        var (talker, _, _) = await app.NewUserAsync("talker");
        var (other, _, _) = await app.NewUserAsync("other");
        var postId = await app.CheckAndPostAsync(owner);

        for (var i = 1; i <= 30; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await CommentAsync(talker, postId, $"comment {i}")).StatusCode);
        }

        var refused = await CommentAsync(talker, postId, "comment 31");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(TooFast, await ErrorAsync(refused));
        var retryAfter = refused.Headers.RetryAfter?.Delta;
        Assert.True(retryAfter is { } delta && delta > TimeSpan.Zero && delta <= TimeSpan.FromHours(1), $"Retry-After: {retryAfter}");

        // The refused comment was never written, and the same account is only slowed on comments.
        var comments = await app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}/comments");
        Assert.Equal(30, comments.EnumerateArray().Count());
        Assert.Equal(HttpStatusCode.OK, (await talker.PostAsync($"/api/posts/{postId}/fire", null)).StatusCode);

        // Per account: the next person, in the same hour and from the same address, is not affected.
        Assert.Equal(HttpStatusCode.Created, (await CommentAsync(other, postId, "still fine")).StatusCode);
    }

    [Fact]
    public async Task The_twenty_first_report_in_an_hour_is_refused_looks_and_comments_together()
    {
        using var app = new TestApp();
        var (owner, _, _) = await app.NewUserAsync("owner");
        var (reporter, _, _) = await app.NewUserAsync("reporter");
        var (other, _, _) = await app.NewUserAsync("other");
        var postId = await app.CheckAndPostAsync(owner);
        var comment = await (await CommentAsync(owner, postId, "my own note")).Content.ReadFromJsonAsync<JsonElement>();
        var commentId = comment.GetProperty("id").GetGuid();

        // Reporting the same thing again is idempotent (204) but still a call: the bucket counts calls, on both routes.
        for (var i = 0; i < 20; i++)
        {
            var response = i % 2 == 0
                ? await reporter.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" })
                : await reporter.PostAsJsonAsync($"/api/comments/{commentId}/report", new { reason = "spam" });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var refusedPost = await reporter.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refusedPost.StatusCode);
        Assert.Equal(TooFast, await ErrorAsync(refusedPost));
        var refusedComment = await reporter.PostAsJsonAsync($"/api/comments/{commentId}/report", new { reason = "spam" });
        Assert.Equal(HttpStatusCode.TooManyRequests, refusedComment.StatusCode);

        // Reports and comments are separate buckets, and another account is not slowed.
        Assert.Equal(HttpStatusCode.Created, (await CommentAsync(reporter, postId, "can still talk")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" })).StatusCode);
    }

    [Fact]
    public async Task The_caps_are_options_and_the_message_follows_the_callers_language()
    {
        using var app = new LimitedApp(commentsPerHour: 2, reportsPerHour: 1);
        var (owner, _, _) = await app.NewUserAsync("owner");
        var (person, _, _) = await app.NewUserAsync("person", language: "he");
        var postId = await app.CheckAndPostAsync(owner);

        Assert.Equal(HttpStatusCode.Created, (await CommentAsync(person, postId, "one")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CommentAsync(person, postId, "two")).StatusCode);
        var refused = await CommentAsync(person, postId, "three");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(TooFast, await ErrorAsync(refused));

        // The limiter answers before any handler runs, so the language comes from Accept-Language, as for signup and login.
        person.DefaultRequestHeaders.AcceptLanguage.ParseAdd("he");
        var refusedInHebrew = await CommentAsync(person, postId, "four");
        Assert.Equal(HttpStatusCode.TooManyRequests, refusedInHebrew.StatusCode);
        Assert.Equal("קצת יותר לאט. אפשר לנסות שוב עוד רגע.", await ErrorAsync(refusedInHebrew));

        Assert.Equal(HttpStatusCode.NoContent, (await person.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await person.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" })).StatusCode);
    }

    [Fact]
    public async Task Unsigned_calls_never_reach_the_limiter()
    {
        using var app = new LimitedApp(commentsPerHour: 1, reportsPerHour: 1);
        var (owner, _, _) = await app.NewUserAsync("owner");
        var postId = await app.CheckAndPostAsync(owner);
        var anonymous = app.NewClient();

        // Authorization answers first (401), so an anonymous flood spends nobody's permits, and the CSRF check (403) is
        // still ahead of both.
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await CommentAsync(anonymous, postId, "hi")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "spam" })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await CommentAsync(app.BareClient(), postId, "hi")).StatusCode);

        // The signed-in account's own permit is still there.
        var (person, _, _) = await app.NewUserAsync("person");
        Assert.Equal(HttpStatusCode.Created, (await CommentAsync(person, postId, "one")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await CommentAsync(person, postId, "two")).StatusCode);
    }

    [Fact]
    public async Task The_account_actions_are_capped_per_moderator_per_hour()
    {
        using var app = new AdminLimitedApp(3);
        var (first, _, _) = await app.NewUserAsync("rl_mod1");
        Assert.Equal(AdminChange.Changed, await app.PromoteAsync("rl_mod1"));
        var (second, _, _) = await app.NewUserAsync("rl_mod2");
        Assert.Equal(AdminChange.Changed, await app.PromoteAsync("rl_mod2"));
        await app.NewUserAsync("rl_target");

        Assert.Equal(HttpStatusCode.OK, (await first.PostAsync("/api/admin/users/rl_target/verify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsync("/api/admin/users/rl_target/unverify", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsync("/api/admin/users/rl_target/verify", null)).StatusCode);

        var refused = await first.PostAsync("/api/admin/users/rl_target/unverify", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(TooFast, await ErrorAsync(refused));
        var retryAfter = refused.Headers.RetryAfter?.Delta;
        Assert.True(retryAfter is { } delta && delta > TimeSpan.Zero && delta <= TimeSpan.FromHours(1), $"Retry-After: {retryAfter}");
        // The refused call never reached the handler: the flag stands where the third call left it.
        var row = (await first.GetFromJsonAsync<JsonElement>("/api/admin/users?q=rl_target")).EnumerateArray().Single();
        Assert.True(row.GetProperty("verified").GetBoolean());

        // The whole family shares the bucket: a suspend from the same moderator is refused too, and the reads are not.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsync("/api/admin/users/rl_target/suspend", null)).StatusCode);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/admin/users?q=rl_target")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/admin/queue")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/admin/sponsor")).StatusCode);
        }

        // Per account: the second moderator, in the same hour and from the same address, is not slowed.
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsync("/api/admin/users/rl_target/unverify", null)).StatusCode);
    }
}
