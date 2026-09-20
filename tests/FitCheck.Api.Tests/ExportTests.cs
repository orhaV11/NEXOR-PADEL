using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace FitCheck.Api.Tests;

/// <summary>
/// The data export (Round 11): the document's shape and that it carries the account's own rows and nothing of anyone
/// else's, the headers that make the browser save it, the three-an-hour brake, a suspended account still taking its
/// data, and the one failure answer.
/// </summary>
public class ExportTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public ExportTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> ErrorOf(HttpResponseMessage response) => (await Json(response)).GetProperty("error").GetString()!;

    private static string[] Keys(JsonElement obj) => obj.EnumerateObject().Select(p => p.Name).ToArray();

    private void WithDb(Action<AppDbContext> action)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        action(db);
        db.SaveChanges();
    }

    private static string Disposition(HttpResponseMessage response) => string.Join(", ", response.Content.Headers.GetValues("Content-Disposition"));

    [Fact]
    public async Task Export_carries_the_accounts_own_rows_and_nothing_of_anyone_elses()
    {
        var (noa, noaId, _) = await _app.NewUserAsync("exp_noa", language: "he", displayName: "Noa");
        var (dan, danId, _) = await _app.NewUserAsync("exp_dan");
        WithDb(db => { var u = db.Users.Single(x => x.Id == noaId); u.Email = "noa@example.test"; u.EmailVerifiedAt = DateTime.UtcNow; });

        // Two checks, the older one posted with a tag; a comment each way; a follow each way; Dan fires the look.
        var firstCheck = await _app.CheckAsync(noa);
        var post = await _app.PostAsync(noa, firstCheck, caption: "first look #ootd");
        var postId = post.GetProperty("id").GetGuid();
        var secondCheck = await _app.CheckAsync(noa, intent: "Office");
        var dansPostId = await _app.CheckAndPostAsync(dan);
        Assert.Equal(HttpStatusCode.Created, (await noa.PostAsJsonAsync($"/api/posts/{dansPostId}/comments", new { text = "love the boots" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await dan.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text = "DAN_WROTE_THIS" })).StatusCode);
        Assert.True((await noa.PostAsync("/api/users/exp_dan/follow", null)).IsSuccessStatusCode);
        Assert.True((await dan.PostAsync("/api/users/exp_noa/follow", null)).IsSuccessStatusCode);
        Assert.True((await dan.PostAsync($"/api/posts/{postId}/fire", null)).IsSuccessStatusCode);
        // Rows no route writes yet or that need another builder: a comparison, a block (Noa shuts Dan out), and a hidden
        // comment of Noa's (still her words).
        var comparisonId = Guid.NewGuid();
        WithDb(db =>
        {
            db.Comparisons.Add(new OutfitComparison { Id = comparisonId, UserId = noaId, Intent = StyleIntent.Date, Status = CheckStatus.Ok, Winner = "b", Language = "he", CreatedAt = DateTime.UtcNow });
            db.Blocks.Add(new Block { BlockerId = noaId, BlockedId = danId, CreatedAt = DateTime.UtcNow });
            db.Comments.Add(new Comment { Id = Guid.NewGuid(), PostId = dansPostId, UserId = noaId, Text = "hidden but mine", Hidden = true, CreatedAt = DateTime.UtcNow.AddMinutes(-5) });
        });

        var response = await noa.GetAsync("/api/users/me/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(raw).RootElement;
        // Round 14 appends "wardrobe": the pieces this account kept, with the checks each one appeared in (WardrobeTests).
        Assert.Equal(["exportedAt", "account", "checks", "posts", "comments", "follows", "followers", "comparisons", "blocks", "notifications", "wardrobe"], Keys(doc));

        // The account: the fields the person set, the effective plan, and no secret of any kind.
        var account = doc.GetProperty("account");
        Assert.Equal(["handle", "name", "accountType", "language", "email", "createdAt", "plan"], Keys(account));
        Assert.Equal("exp_noa", account.GetProperty("handle").GetString());
        Assert.Equal("Noa", account.GetProperty("name").GetString());
        Assert.Equal("Person", account.GetProperty("accountType").GetString());
        Assert.Equal("he", account.GetProperty("language").GetString());
        Assert.Equal("noa@example.test", account.GetProperty("email").GetString());
        Assert.Equal("free", account.GetProperty("plan").GetString());
        Assert.DoesNotContain("1990-01-01", raw);      // the birth date from signup is not in the file
        Assert.DoesNotContain("birth", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("billing", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isAdmin", raw);

        // Checks, newest first, with the stylist's words and pieces; nothing else of the feedback.
        var checks = doc.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal(2, checks.Count);
        Assert.Equal(secondCheck, checks[0].GetProperty("id").GetGuid());
        Assert.Equal("Office", checks[0].GetProperty("intent").GetString());
        Assert.Equal(firstCheck, checks[1].GetProperty("id").GetGuid());
        Assert.Equal(["id", "createdAt", "intent", "score", "headline", "tip", "items", "status"], Keys(checks[1]));
        Assert.Equal(7, checks[1].GetProperty("score").GetInt32());
        Assert.Equal("Clean casual with one weak link", checks[1].GetProperty("headline").GetString());
        Assert.Equal("Swap the running shoes for plain white leather sneakers.", checks[1].GetProperty("tip").GetString());
        Assert.Equal("ok", checks[1].GetProperty("status").GetString());
        var items = checks[1].GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("name").GetString()!, i.GetProperty("category").GetString()!)).ToList();
        Assert.Equal([("White tee", "top"), ("Dark jeans", "bottom"), ("Running shoes", "shoes")], items);
        Assert.DoesNotContain("Too sporty", raw);   // the per-piece notes and verdicts stay with the check

        // The look: the caption, its tag, the pieces as rows, the counters as they stand.
        var look = Assert.Single(doc.GetProperty("posts").EnumerateArray());
        Assert.Equal(postId, look.GetProperty("id").GetGuid());
        Assert.Equal("first look #ootd", look.GetProperty("caption").GetString());
        Assert.Equal("Date", look.GetProperty("intent").GetString());
        Assert.Equal(["ootd"], look.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToArray());
        Assert.Equal(["white tee", "dark jeans", "running shoes"], look.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToArray());
        Assert.Equal(1, look.GetProperty("fires").GetInt32());
        Assert.Equal(1, look.GetProperty("comments").GetInt32());

        // Comments: only what Noa wrote (the hidden one too), never Dan's words on her look.
        var comments = doc.GetProperty("comments").EnumerateArray().Select(c => c.GetProperty("text").GetString()!).ToList();
        Assert.Equal(["love the boots", "hidden but mine"], comments);
        Assert.DoesNotContain("DAN_WROTE_THIS", raw);
        Assert.All(doc.GetProperty("comments").EnumerateArray(), c => Assert.Equal(["postId", "createdAt", "text"], Keys(c)));

        // Follows both ways, the block, the comparison, the notifications: handles and types only.
        Assert.Equal("exp_dan", Assert.Single(doc.GetProperty("follows").EnumerateArray()).GetProperty("handle").GetString());
        Assert.Equal("exp_dan", Assert.Single(doc.GetProperty("followers").EnumerateArray()).GetProperty("handle").GetString());
        var block = Assert.Single(doc.GetProperty("blocks").EnumerateArray());
        Assert.Equal(["handle", "since"], Keys(block));
        Assert.Equal("exp_dan", block.GetProperty("handle").GetString());
        var comparison = Assert.Single(doc.GetProperty("comparisons").EnumerateArray());
        Assert.Equal(comparisonId, comparison.GetProperty("id").GetGuid());
        Assert.Equal("b", comparison.GetProperty("winner").GetString());
        var notifications = doc.GetProperty("notifications").EnumerateArray().ToList();
        Assert.Contains(notifications, n => n.GetProperty("type").GetString() == "fire");
        Assert.Contains(notifications, n => n.GetProperty("type").GetString() == "follow");
        Assert.All(notifications, n => Assert.Equal(["type", "createdAt"], Keys(n)));

        // Dan's file, in turn, has none of Noa's: her comment on his look is hers, and his block list is empty.
        var dansRaw = await (await dan.GetAsync("/api/users/me/export")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("love the boots", dansRaw);
        Assert.DoesNotContain("hidden but mine", dansRaw);
        var dansDoc = JsonDocument.Parse(dansRaw).RootElement;
        Assert.Empty(dansDoc.GetProperty("blocks").EnumerateArray());
        Assert.Equal("DAN_WROTE_THIS", Assert.Single(dansDoc.GetProperty("comments").EnumerateArray()).GetProperty("text").GetString());
        Assert.Equal("exp_noa", Assert.Single(dansDoc.GetProperty("followers").EnumerateArray()).GetProperty("handle").GetString());
    }

    [Fact]
    public async Task Export_reports_the_effective_plan_and_a_check_that_was_not_a_look()
    {
        var (client, id, _) = await _app.NewUserAsync("exp_plan");
        _app.Vision.Handler = _ => Payloads.NotOutfit();
        try
        {
            await _app.CheckAsync(client);
        }
        finally
        {
            _app.Vision.Handler = _ => Payloads.Ok();
        }

        var until = DateTime.UtcNow.AddDays(20);
        WithDb(db => { var u = db.Users.Single(x => x.Id == id); u.Plan = "pro"; u.ProUntil = until; });
        var pro = await Json(await client.GetAsync("/api/users/me/export"));
        Assert.Equal("pro", pro.GetProperty("account").GetProperty("plan").GetString());
        Assert.InRange(pro.GetProperty("account").GetProperty("proUntil").GetDateTime().Ticks, until.AddMinutes(-1).Ticks, until.AddMinutes(1).Ticks);
        // A check that was not an outfit: its status and no words.
        var check = Assert.Single(pro.GetProperty("checks").EnumerateArray());
        Assert.Equal("not_outfit", check.GetProperty("status").GetString());
        Assert.False(check.TryGetProperty("headline", out _));
        Assert.Empty(check.GetProperty("items").EnumerateArray());

        // Lapsed: free again, and no stale end date in the file.
        WithDb(db => db.Users.Single(x => x.Id == id).ProUntil = DateTime.UtcNow.AddMinutes(-1));
        var lapsed = await Json(await client.GetAsync("/api/users/me/export"));
        Assert.Equal("free", lapsed.GetProperty("account").GetProperty("plan").GetString());
        Assert.False(lapsed.GetProperty("account").TryGetProperty("proUntil", out _));
    }

    [Fact]
    public async Task Export_headers_name_the_file_after_the_handle_and_the_day_and_forbid_caching()
    {
        var (client, _, _) = await _app.NewUserAsync("exp_headers");
        _app.Clock.Now = new DateTime(2026, 3, 5, 14, 30, 0, DateTimeKind.Utc);
        try
        {
            var response = await client.GetAsync("/api/users/me/export");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("attachment; filename=\"orevosh-exp_headers-20260305.json\"", Disposition(response));
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal(new DateTime(2026, 3, 5, 14, 30, 0, DateTimeKind.Utc), (await Json(response)).GetProperty("exportedAt").GetDateTime().ToUniversalTime());

            // A handle with letters outside ASCII: the header stays ASCII, with the real name in filename* for the browser.
            var (hebrew, _, _) = await _app.NewUserAsync("נועה_exp");
            var named = await hebrew.GetAsync("/api/users/me/export");
            Assert.Equal(HttpStatusCode.OK, named.StatusCode);
            Assert.Equal("attachment; filename=\"orevosh-_exp-20260305.json\"; filename*=UTF-8''orevosh-%D7%A0%D7%95%D7%A2%D7%94_exp-20260305.json", Disposition(named));
        }
        finally
        {
            _app.Clock.Now = null;
        }

        Assert.Equal("attachment; filename=\"orevosh-noa-20260913.json\"", ExportEndpoints.ContentDisposition("noa", new DateTime(2026, 9, 13, 23, 59, 0, DateTimeKind.Utc)));
        Assert.StartsWith("attachment; filename=\"orevosh-account-20260913.json\"; filename*=UTF-8''", ExportEndpoints.ContentDisposition("נועה", new DateTime(2026, 9, 13)));
    }

    [Fact]
    public async Task Export_is_limited_to_three_an_hour_per_account()
    {
        var (client, _, _) = await _app.NewUserAsync("exp_limited");
        var (other, _, _) = await _app.NewUserAsync("exp_other");
        for (var i = 1; i <= ExportEndpoints.ExportsPerHour; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me/export")).StatusCode);
        }

        var refused = await client.GetAsync("/api/users/me/export");
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("Slow down a little. Try again in a bit.", await ErrorOf(refused));
        Assert.NotNull(refused.Headers.RetryAfter);

        // Per account: the next person is not held back by it.
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/users/me/export")).StatusCode);
        // A stranger without a session is refused at the door and never spends a permit.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().GetAsync("/api/users/me/export")).StatusCode);
    }

    [Fact]
    public async Task A_suspended_account_still_takes_its_data()
    {
        var (client, id, _) = await _app.NewUserAsync("exp_suspended");
        await _app.CheckAndPostAsync(client, caption: "before the ban");
        WithDb(db => db.Users.Single(x => x.Id == id).Suspended = true);

        var response = await client.GetAsync("/api/users/me/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await Json(response);
        Assert.Equal("exp_suspended", doc.GetProperty("account").GetProperty("handle").GetString());
        Assert.Equal("before the ban", Assert.Single(doc.GetProperty("posts").EnumerateArray()).GetProperty("caption").GetString());

        // Every other door is shut to it, as before.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Export_answers_500_when_a_row_cannot_be_read_and_nothing_is_written()
    {
        var (client, id, _) = await _app.NewUserAsync("exp_broken", language: "he");
        WithDb(db => db.Checks.Add(new OutfitCheck { Id = Guid.NewGuid(), UserId = id, Intent = StyleIntent.Date, Status = CheckStatus.Ok, FeedbackJson = "{not json", CreatedAt = DateTime.UtcNow }));

        var response = await client.GetAsync("/api/users/me/export");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("לא הצלחנו להכין את הנתונים שלך. אפשר לנסות שוב עוד רגע.", await ErrorOf(response));
        Assert.Null(response.Content.Headers.ContentDisposition);

        // Repaired, the same account exports on the next try.
        WithDb(db => db.Checks.Single(c => c.UserId == id).FeedbackJson = null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me/export")).StatusCode);
    }
}
