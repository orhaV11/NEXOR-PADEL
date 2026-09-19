using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace FitCheck.Api.Tests;

/// <summary>An app whose moderator, "mod_one", signs up like anyone else and is then promoted the way --admin does it.</summary>
public sealed class AdminApp : TestApp
{
    public const string Moderator = "mod_one";
}

/// <summary>
/// The moderation routes: the gate, the queue, hide/show/delete, and suspension with everything it takes along; and how an
/// account becomes a moderator (the flag on the row, the Admin:Handles sync, --admin/--unadmin) and what that protects.
/// </summary>
public class AdminTests : IClassFixture<AdminApp>
{
    private readonly AdminApp _app;

    public AdminTests(AdminApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> ErrorOf(HttpResponseMessage response) => (await Json(response)).GetProperty("error").GetString()!;

    private static List<Guid> Ids(JsonElement feed) =>
        feed.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

    private void WithDb(Action<AppDbContext> action)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        action(db);
        db.SaveChanges();
    }

    private T FromDb<T>(Func<AppDbContext, T> read)
    {
        using var scope = _app.Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>
    /// The moderator's own client: a normal signup, then the flag, as --admin sets it. The handle differs in case from the
    /// promoted one on purpose: the command is case-insensitive, like handles everywhere.
    /// </summary>
    private async Task<HttpClient> ModeratorAsync()
    {
        var client = _app.NewClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { handle = "MOD_one", password = "password123" });
        if (login.StatusCode == HttpStatusCode.OK)
        {
            return client;
        }

        var me = await _app.SignupAsync(client, "Mod_One");
        Assert.False(me.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(AdminChange.Changed, await _app.PromoteAsync("MOD_ONE"));
        return client;
    }

    /// <summary>Another moderator, promoted the same way, for the tests that need two.</summary>
    private async Task<HttpClient> PromotedAsync(string handle)
    {
        var (client, _, _) = await _app.NewUserAsync(handle);
        Assert.Equal(AdminChange.Changed, await _app.PromoteAsync(handle));
        return client;
    }

    private static MultipartFormDataContent AvatarForm(byte[] image)
    {
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "image", "me.jpg" } };
    }

    private static async Task<JsonElement> OpenChallengeAsync(HttpClient brand, string title)
    {
        var response = await brand.PostAsJsonAsync("/api/challenges", new
        {
            title, brief = "Show us the look.", intent = "Office", prize = "A shirt", endsAt = DateTime.UtcNow.AddDays(3)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private async Task<List<HttpClient>> ReportPostAsync(Guid postId, string prefix, int count, string reason = "spam")
    {
        var reporters = new List<HttpClient>();
        for (var i = 0; i < count; i++)
        {
            var (client, _, _) = await _app.NewUserAsync($"{prefix}{i}");
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason })).StatusCode);
            reporters.Add(client);
        }

        return reporters;
    }

    private async Task<Guid> CommentAsync(HttpClient client, Guid postId, string text)
    {
        var response = await client.PostAsJsonAsync($"/api/posts/{postId}/comments", new { text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetGuid();
    }

    private static JsonElement Item(JsonElement queue, Guid id) =>
        queue.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task Every_admin_route_answers_403_to_a_signed_in_non_admin_and_401_to_nobody()
    {
        var (plain, _, _) = await _app.NewUserAsync("gate_plain", language: "he");
        var id = Guid.NewGuid();
        var routes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, "/api/admin/queue"),
            (HttpMethod.Get, "/api/admin/users?q=g"),
            (HttpMethod.Post, $"/api/admin/posts/{id}/hide"),
            (HttpMethod.Post, $"/api/admin/posts/{id}/unhide"),
            (HttpMethod.Delete, $"/api/admin/posts/{id}"),
            (HttpMethod.Post, $"/api/admin/comments/{id}/hide"),
            (HttpMethod.Post, $"/api/admin/comments/{id}/unhide"),
            (HttpMethod.Delete, $"/api/admin/comments/{id}"),
            (HttpMethod.Post, "/api/admin/users/gate_plain/suspend"),
            (HttpMethod.Post, "/api/admin/users/gate_plain/unsuspend"),
        };

        foreach (var (method, path) in routes)
        {
            var response = await plain.SendAsync(new HttpRequestMessage(method, path));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            // In the caller's language, and the same answer whether or not the id exists: nothing was looked up.
            Assert.Contains("OREVOSH", await ErrorOf(response));
            Assert.Contains("צוות", await ErrorOf(await plain.SendAsync(new HttpRequestMessage(method, path))));

            var anonymous = await _app.NewClient().SendAsync(new HttpRequestMessage(method, path));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        // Still signed in: a refused admin call is not a sign-out.
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Me_says_isAdmin_only_for_promoted_accounts_and_the_flag_takes_effect_on_the_next_request()
    {
        var moderator = await ModeratorAsync();
        var me = await moderator.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("isAdmin").GetBoolean());
        var edited = await Json(await moderator.PatchAsJsonAsync("/api/users/me", new { bio = "on duty" }));
        Assert.True(edited.GetProperty("isAdmin").GetBoolean());

        var (plain, _, _) = await _app.NewUserAsync("me_plain");
        Assert.False((await plain.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("isAdmin").GetBoolean());
        var login = await Json(await _app.NewClient().PostAsJsonAsync("/api/auth/login", new { handle = "me_plain", password = "password123" }));
        Assert.False(login.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync("/api/admin/queue")).StatusCode);

        // The flag is read off the row every time: --unadmin shuts the door on the very next request of a live session, and
        // --admin opens it, with no new cookie either way.
        var second = await PromotedAsync("mod_flag");
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/admin/queue")).StatusCode);
        Assert.Equal(AdminChange.Changed, await _app.DemoteAsync("mod_flag"));
        Assert.Equal(AdminChange.Unchanged, await _app.DemoteAsync("mod_flag"));
        Assert.False((await second.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await second.GetAsync("/api/admin/queue")).StatusCode);
        Assert.Equal(AdminChange.Changed, await _app.PromoteAsync("mod_flag"));
        Assert.Equal(AdminChange.Unchanged, await _app.PromoteAsync("mod_flag"));
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/admin/queue")).StatusCode);
        Assert.Equal(AdminChange.NotFound, await _app.PromoteAsync("nobody_at_all"));
    }

    [Fact]
    public async Task A_handle_in_the_admin_list_cannot_be_registered_by_anyone()
    {
        // The owner signs up first and lists the handle afterwards; from then on the handle is taken for everybody else, in
        // any case, so nobody can register it and be promoted at the next restart.
        using var app = new TestApp { AdminHandles = "Owner_Handle" };
        var client = app.NewClient();
        foreach (var handle in new[] { "owner_handle", "OWNER_HANDLE", "Owner_Handle" })
        {
            var refused = await client.PostAsJsonAsync("/api/auth/signup", new { handle, password = "password123", confirmed16Plus = true, language = "en" });
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("That handle is taken.", await ErrorOf(refused));
        }

        var neighbour = await app.SignupAsync(app.NewClient(), "owner_handle2");
        Assert.False(neighbour.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task The_sync_promotes_listed_accounts_that_exist_and_never_demotes()
    {
        var (listed, listedId, _) = await _app.NewUserAsync("Sync_Listed");
        var (other, otherId, _) = await _app.NewUserAsync("sync_other");
        Assert.Equal(HttpStatusCode.Forbidden, (await listed.GetAsync("/api/admin/queue")).StatusCode);

        // What the start of the app runs after the migrations, with the list from Admin:Handles (case-insensitive, a handle
        // that has not signed up yet is only a warning).
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await AdminSync.PromoteListedAsync(db, ["SYNC_LISTED", "not_signed_up_yet"], NullLogger.Instance, CancellationToken.None));
            Assert.Equal(0, await AdminSync.PromoteListedAsync(db, ["sync_listed"], NullLogger.Instance, CancellationToken.None));
        }

        Assert.True((await listed.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await listed.GetAsync("/api/admin/queue")).StatusCode);
        Assert.False((await other.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/admin/queue")).StatusCode);

        // A list without the handle (or an empty one) takes nothing away: removing a moderator is --unadmin, on purpose.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await AdminSync.PromoteListedAsync(db, ["someone_else_entirely"], NullLogger.Instance, CancellationToken.None));
            Assert.Equal(0, await AdminSync.PromoteListedAsync(db, [], NullLogger.Instance, CancellationToken.None));
            Assert.Equal([listedId], db.Users.Where(u => (u.Id == listedId || u.Id == otherId) && u.IsAdmin).Select(u => u.Id).ToArray());
        }

        Assert.True(FromDb(db => db.Users.Single(u => u.Id == listedId).IsAdmin));
        Assert.False(FromDb(db => db.Users.Single(u => u.Id == otherId).IsAdmin));
    }

    [Fact]
    public async Task A_moderator_cannot_delete_their_account_until_un_admined_and_the_freed_handle_carries_nothing()
    {
        var leaving = await PromotedAsync("mod_leaving");
        var refused = await leaving.DeleteAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("Moderators can't delete their account while they moderate. Run --unadmin first.", await ErrorOf(refused));
        // Still there, still signed in, still a moderator.
        Assert.Equal(HttpStatusCode.OK, (await leaving.GetAsync("/api/admin/queue")).StatusCode);

        Assert.Equal(AdminChange.Changed, await _app.DemoteAsync("mod_leaving"));
        Assert.Equal(HttpStatusCode.NoContent, (await leaving.DeleteAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await leaving.GetAsync("/api/auth/me")).StatusCode);

        // A stranger takes the freed handle: an ordinary account, whatever the handle used to be.
        var (stranger, _, _) = await _app.NewUserAsync("mod_leaving");
        Assert.False((await stranger.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/api/admin/queue")).StatusCode);
        var (victim, _, _) = await _app.NewUserAsync("mod_leaving_victim");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync("/api/admin/users/mod_leaving_victim/suspend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await victim.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Suspending_a_moderator_is_refused_with_the_command_that_does_it()
    {
        var moderator = await ModeratorAsync();
        var other = await PromotedAsync("mod_protected");

        var refused = await moderator.PostAsync("/api/admin/users/MOD_PROTECTED/suspend", null);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("Moderators are removed with the --unadmin command, not suspended.", await ErrorOf(refused));
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/admin/queue")).StatusCode);
        Assert.False(FromDb(db => db.Users.Single(u => u.HandleLower == "mod_protected").Suspended));

        // Once un-admined, the account is anyone's to suspend.
        Assert.Equal(AdminChange.Changed, await _app.DemoteAsync("mod_protected"));
        var suspended = await Json(await moderator.PostAsync("/api/admin/users/mod_protected/suspend", null));
        Assert.True(suspended.GetProperty("suspended").GetBoolean());
        await moderator.PostAsync("/api/admin/users/mod_protected/unsuspend", null);
    }

    [Fact]
    public async Task Suspending_a_brand_closes_its_open_challenges_for_good()
    {
        var moderator = await ModeratorAsync();
        var (brand, brandId, _) = await _app.NewUserAsync("susp_brand", accountType: "Brand");
        var (entrant, _, _) = await _app.NewUserAsync("susp_entrant");
        var (late, _, _) = await _app.NewUserAsync("susp_late");
        var (voter, _, _) = await _app.NewUserAsync("susp_voter");
        var challenge = await OpenChallengeAsync(brand, "Brand looks");
        var challengeId = challenge.GetProperty("id").GetGuid();
        var tag = challenge.GetProperty("tag").GetString()!;
        var entryCheck = await _app.CheckAsync(entrant);
        var entry = await _app.PostAsync(entrant, entryCheck, caption: $"in #{tag}");
        Assert.Equal(challengeId, entry.GetProperty("challengeId").GetGuid());
        var entryId = entry.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await voter.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryId })).StatusCode);
        var anonymous = _app.NewClient();
        Assert.Contains((await anonymous.GetFromJsonAsync<JsonElement>("/api/challenges")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == challengeId);

        Assert.True((await Json(await moderator.PostAsync("/api/admin/users/susp_brand/suspend", null))).GetProperty("suspended").GetBoolean());

        // Closed now, with no winner, and nothing goes out about it.
        var row = FromDb(db => db.Challenges.Single(c => c.Id == challengeId));
        Assert.NotNull(row.ResolvedAt);
        Assert.Null(row.WinnerPostId);
        var detail = await anonymous.GetFromJsonAsync<JsonElement>($"/api/challenges/{challengeId}");
        Assert.False(detail.GetProperty("challenge").GetProperty("isOpen").GetBoolean());
        Assert.True(IsNull(detail.GetProperty("challenge"), "winnerPostId"));
        Assert.True(IsNull(detail, "winner"));
        Assert.DoesNotContain((await anonymous.GetFromJsonAsync<JsonElement>("/api/challenges")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == challengeId);
        Assert.Contains((await anonymous.GetFromJsonAsync<JsonElement>("/api/challenges?state=ended")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == challengeId);
        Assert.DoesNotContain((await entrant.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items").EnumerateArray(), n => n.GetProperty("type").GetString() == "won");
        Assert.Equal(0, FromDb(db => db.Notifications.Count(n => n.UserId == brandId && n.Type == "ended")));

        // The hashtag no longer enters anyone, and votes are closed.
        var lateCheck = await _app.CheckAsync(late);
        var latePost = await _app.PostAsync(late, lateCheck, caption: $"too late #{tag}");
        Assert.True(IsNull(latePost, "challengeId"));
        Assert.Equal(HttpStatusCode.BadRequest, (await late.PostAsJsonAsync($"/api/challenges/{challengeId}/vote", new { postId = entryId })).StatusCode);
        // The entry itself is the entrant's look and stays up.
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/posts/{entryId}")).StatusCode);

        // Lifting the suspension does not reopen it, and reading it afterwards still crowns nobody.
        await moderator.PostAsync("/api/admin/users/susp_brand/unsuspend", null);
        detail = await anonymous.GetFromJsonAsync<JsonElement>($"/api/challenges/{challengeId}");
        Assert.False(detail.GetProperty("challenge").GetProperty("isOpen").GetBoolean());
        Assert.True(IsNull(detail, "winner"));
        Assert.DoesNotContain((await anonymous.GetFromJsonAsync<JsonElement>("/api/challenges")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == challengeId);
        Assert.Null(FromDb(db => db.Challenges.Single(c => c.Id == challengeId).WinnerPostId));
        // The brand is back and can open a new one.
        Assert.Equal(HttpStatusCode.OK, (await _app.NewClient().PostAsJsonAsync("/api/auth/login", new { handle = "susp_brand", password = "password123" })).StatusCode);
    }

    [Fact]
    public async Task A_suspended_accounts_avatar_is_not_served_until_the_suspension_is_lifted()
    {
        var moderator = await ModeratorAsync();
        var (user, _, _) = await _app.NewUserAsync("susp_face");
        var me = await Json(await user.PostAsync("/api/users/me/avatar", AvatarForm(TestImages.Jpeg())));
        var url = me.GetProperty("avatarUrl").GetString()!;
        var anonymous = _app.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(url)).StatusCode);

        await moderator.PostAsync("/api/admin/users/susp_face/suspend", null);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/users/susp_face/avatar")).StatusCode);

        await moderator.PostAsync("/api/admin/users/susp_face/unsuspend", null);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(url)).StatusCode);
    }

    private static bool IsNull(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    [Fact]
    public async Task The_queue_lists_reported_looks_and_comments_newest_report_first_with_reasons_and_counts()
    {
        var moderator = await ModeratorAsync();
        var (author, _, _) = await _app.NewUserAsync("q_author", displayName: "Queue Author");
        var (talker, _, _) = await _app.NewUserAsync("q_talker");
        var postId = await _app.CheckAndPostAsync(author, caption: "look at this");
        var commentId = await CommentAsync(talker, postId, "something rude");

        var reporters = await ReportPostAsync(postId, "q_rep", 2, reason: "spam");
        Assert.Equal(HttpStatusCode.NoContent, (await reporters[0].PostAsJsonAsync($"/api/comments/{commentId}/report", new { reason = "  rude  " })).StatusCode);
        // An empty reason is not a reason; a repeated one is listed once.
        Assert.Equal(HttpStatusCode.NoContent, (await reporters[1].PostAsJsonAsync($"/api/comments/{commentId}/report", new { reason = "" })).StatusCode);

        var queue = await moderator.GetFromJsonAsync<JsonElement>("/api/admin/queue");
        var items = queue.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);

        var comment = items[0];
        Assert.Equal("comment", comment.GetProperty("kind").GetString());
        Assert.Equal(commentId, comment.GetProperty("id").GetGuid());
        Assert.Equal(2, comment.GetProperty("reports").GetInt32());
        Assert.False(comment.GetProperty("hidden").GetBoolean());
        Assert.Equal(["rude"], comment.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToArray());
        Assert.Equal("something rude", comment.GetProperty("comment").GetProperty("text").GetString());
        Assert.True(comment.GetProperty("comment").GetProperty("canDelete").GetBoolean());
        Assert.Equal("q_talker", comment.GetProperty("author").GetProperty("handle").GetString());
        Assert.False(comment.GetProperty("authorSuspended").GetBoolean());

        var post = items[1];
        Assert.Equal("post", post.GetProperty("kind").GetString());
        Assert.Equal(postId, post.GetProperty("id").GetGuid());
        Assert.Equal(2, post.GetProperty("reports").GetInt32());
        Assert.Equal(["spam"], post.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToArray());
        Assert.Equal("look at this", post.GetProperty("post").GetProperty("caption").GetString());
        Assert.Equal("Queue Author", post.GetProperty("author").GetProperty("name").GetString());
        Assert.True(post.GetProperty("lastReportedAt").GetDateTime() <= comment.GetProperty("lastReportedAt").GetDateTime());

        Assert.Equal(0, queue.GetProperty("hiddenPosts").GetInt32());
        Assert.Equal(0, queue.GetProperty("hiddenComments").GetInt32());
        Assert.Equal(0, queue.GetProperty("suspendedUsers").GetInt32());

        // The third report hides the look; the queue keeps it, flagged, and the moderator can still open it and its photo.
        await ReportPostAsync(postId, "q_more", 1, reason: "spam");
        queue = await moderator.GetFromJsonAsync<JsonElement>("/api/admin/queue");
        var hidden = Item(queue, postId);
        Assert.True(hidden.GetProperty("hidden").GetBoolean());
        Assert.Equal(3, hidden.GetProperty("reports").GetInt32());
        Assert.Equal(1, queue.GetProperty("hiddenPosts").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync($"/api/posts/{postId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync($"/api/posts/{postId}/image")).StatusCode);
    }

    [Fact]
    public async Task Hide_and_unhide_flip_visibility_and_unhide_forgets_the_reports()
    {
        var moderator = await ModeratorAsync();
        var (author, _, _) = await _app.NewUserAsync("h_author");
        var postId = await _app.CheckAndPostAsync(author);
        var reporter = (await ReportPostAsync(postId, "h_rep", 1, reason: "off topic"))[0];
        var viewer = _app.NewClient();

        var hidden = await Json(await moderator.PostAsync($"/api/admin/posts/{postId}/hide", null));
        Assert.True(hidden.GetProperty("hidden").GetBoolean());
        Assert.Equal(1, hidden.GetProperty("reports").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/posts/{postId}")).StatusCode);
        Assert.DoesNotContain(postId, Ids(await viewer.GetFromJsonAsync<JsonElement>("/api/feed?tab=fresh&limit=30")));
        // The author still sees it, under review.
        Assert.True((await Json(await author.GetAsync($"/api/posts/{postId}"))).GetProperty("hidden").GetBoolean());

        var shown = await Json(await moderator.PostAsync($"/api/admin/posts/{postId}/unhide", null));
        Assert.False(shown.GetProperty("hidden").GetBoolean());
        Assert.Equal(0, shown.GetProperty("reports").GetInt32());
        Assert.Empty(shown.GetProperty("reasons").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Contains(postId, Ids(await viewer.GetFromJsonAsync<JsonElement>("/api/feed?tab=fresh&limit=30")));
        Assert.DoesNotContain((await moderator.GetFromJsonAsync<JsonElement>("/api/admin/queue")).GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == postId);

        // The same person can report it again: the rows went with the count.
        Assert.Equal(HttpStatusCode.NoContent, (await reporter.PostAsJsonAsync($"/api/posts/{postId}/report", new { reason = "again" })).StatusCode);
        var again = Item(await moderator.GetFromJsonAsync<JsonElement>("/api/admin/queue"), postId);
        Assert.Equal(1, again.GetProperty("reports").GetInt32());
        Assert.Equal(["again"], again.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToArray());

        Assert.Equal(HttpStatusCode.NotFound, (await moderator.PostAsync($"/api/admin/posts/{Guid.NewGuid()}/hide", null)).StatusCode);
    }

    [Fact]
    public async Task A_moderators_delete_removes_the_look_its_check_and_its_photo()
    {
        var moderator = await ModeratorAsync();
        var (author, authorId, _) = await _app.NewUserAsync("d_author");
        var checkId = await _app.CheckAsync(author);
        var postId = (await _app.PostAsync(author, checkId)).GetProperty("id").GetGuid();
        var photo = Path.Combine(_app.StorageRoot, authorId.ToString("N"), $"{checkId:N}.jpg");
        Assert.True(File.Exists(photo));

        Assert.Equal(HttpStatusCode.NoContent, (await moderator.DeleteAsync($"/api/admin/posts/{postId}")).StatusCode);

        Assert.False(File.Exists(photo));
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync($"/api/posts/{postId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync($"/api/checks/{checkId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.DeleteAsync($"/api/admin/posts/{postId}")).StatusCode);
        // The owner's own delete keeps the check and its photo, as before.
        var keptCheck = await _app.CheckAsync(author);
        var keptPost = (await _app.PostAsync(author, keptCheck)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await author.DeleteAsync($"/api/posts/{keptPost}")).StatusCode);
        Assert.True(File.Exists(Path.Combine(_app.StorageRoot, authorId.ToString("N"), $"{keptCheck:N}.jpg")));
        Assert.Equal(HttpStatusCode.OK, (await author.GetAsync($"/api/checks/{keptCheck}")).StatusCode);
    }

    [Fact]
    public async Task Comment_hide_unhide_and_delete_keep_the_posts_count_honest()
    {
        var moderator = await ModeratorAsync();
        var (author, _, _) = await _app.NewUserAsync("c_author");
        var (talker, _, _) = await _app.NewUserAsync("c_talker");
        var postId = await _app.CheckAndPostAsync(author);
        var first = await CommentAsync(talker, postId, "first");
        await CommentAsync(talker, postId, "second");
        var count = async () => (await Json(await _app.NewClient().GetAsync($"/api/posts/{postId}"))).GetProperty("commentCount").GetInt32();
        var listed = async () => (await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}/comments")).EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(2, await count());

        var hidden = await Json(await moderator.PostAsync($"/api/admin/comments/{first}/hide", null));
        Assert.True(hidden.GetProperty("hidden").GetBoolean());
        Assert.Equal("comment", hidden.GetProperty("kind").GetString());
        Assert.Equal(1, await count());
        Assert.DoesNotContain(first, await listed());
        // Hiding twice moves the count once.
        await moderator.PostAsync($"/api/admin/comments/{first}/hide", null);
        Assert.Equal(1, await count());

        var shown = await Json(await moderator.PostAsync($"/api/admin/comments/{first}/unhide", null));
        Assert.False(shown.GetProperty("hidden").GetBoolean());
        Assert.Equal(2, await count());
        Assert.Contains(first, await listed());

        Assert.Equal(HttpStatusCode.NoContent, (await moderator.DeleteAsync($"/api/admin/comments/{first}")).StatusCode);
        Assert.Equal(1, await count());
        Assert.DoesNotContain(first, await listed());
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.DeleteAsync($"/api/admin/comments/{first}")).StatusCode);
    }

    [Fact]
    public async Task Suspension_locks_the_account_out_and_hides_everything_until_it_is_lifted()
    {
        var moderator = await ModeratorAsync();
        var (user, userId, _) = await _app.NewUserAsync("susp_one", displayName: "Suspended One");
        var (other, _, _) = await _app.NewUserAsync("susp_other");
        var clean = await _app.CheckAndPostAsync(user);
        var reported = await _app.CheckAndPostAsync(user);
        await ReportPostAsync(reported, "s_rep", 3);   // hidden by the community, before any moderator touched it
        var othersPost = await _app.CheckAndPostAsync(other);
        var comment = await CommentAsync(user, othersPost, "hello there");
        WithDb(db => db.PushSubscriptions.Add(new PushSubscription
        {
            Id = Guid.NewGuid(), UserId = userId, Endpoint = "https://push.example/" + userId, P256dh = "p", Auth = "a", CreatedAt = DateTime.UtcNow
        }));
        var anonymous = _app.NewClient();
        Assert.Equal(1, (await Json(await anonymous.GetAsync($"/api/posts/{othersPost}"))).GetProperty("commentCount").GetInt32());

        var suspended = await Json(await moderator.PostAsync("/api/admin/users/SUSP_ONE/suspend", null));
        Assert.True(suspended.GetProperty("suspended").GetBoolean());
        Assert.Equal("susp_one", suspended.GetProperty("user").GetProperty("handle").GetString());
        Assert.Equal(2, suspended.GetProperty("posts").GetInt32());
        Assert.Equal(3, suspended.GetProperty("reports").GetInt32());

        // The next call with the old cookie is refused and the cookie goes; signing in again is refused too.
        var refused = await user.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("suspended", await ErrorOf(refused));
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _app.NewClient().PostAsJsonAsync("/api/auth/login", new { handle = "susp_one", password = "password123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PostAsJsonAsync("/api/auth/login", new { handle = "susp_one", password = "wrong password" })).StatusCode);

        // Gone from the public: profile, looks, feed, search, comment; the push subscription is dropped.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/users/susp_one")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/users/susp_one/posts")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync("/api/users/susp_one/follow", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/posts/{clean}")).StatusCode);
        Assert.DoesNotContain(clean, Ids(await anonymous.GetFromJsonAsync<JsonElement>("/api/feed?tab=fresh&limit=30")));
        Assert.Empty((await anonymous.GetFromJsonAsync<JsonElement>("/api/search?q=susp_one")).GetProperty("users").EnumerateArray());
        Assert.DoesNotContain((await anonymous.GetFromJsonAsync<JsonElement>($"/api/posts/{othersPost}/comments")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == comment);
        Assert.Equal(0, (await Json(await anonymous.GetAsync($"/api/posts/{othersPost}"))).GetProperty("commentCount").GetInt32());
        Assert.Equal(0, FromDb(db => db.PushSubscriptions.Count(s => s.UserId == userId)));
        var queue = await moderator.GetFromJsonAsync<JsonElement>("/api/admin/queue");
        Assert.Equal(1, queue.GetProperty("suspendedUsers").GetInt32());
        Assert.True(Item(queue, reported).GetProperty("authorSuspended").GetBoolean());

        // The moderator finds the account by prefix, and with no term sees the suspended ones.
        var found = await moderator.GetFromJsonAsync<JsonElement>("/api/admin/users?q=@SUS");
        Assert.Contains(found.EnumerateArray(), u => u.GetProperty("user").GetProperty("handle").GetString() == "susp_one" && u.GetProperty("suspended").GetBoolean());
        Assert.Contains(found.EnumerateArray(), u => u.GetProperty("user").GetProperty("handle").GetString() == "susp_other" && !u.GetProperty("suspended").GetBoolean());
        var listed = await moderator.GetFromJsonAsync<JsonElement>("/api/admin/users?q=");
        Assert.Equal(["susp_one"], listed.EnumerateArray().Select(u => u.GetProperty("user").GetProperty("handle").GetString()).ToArray());

        // Showing the reported look again while the author is out forgets the reports but waits for the lift.
        var waiting = await Json(await moderator.PostAsync($"/api/admin/posts/{reported}/unhide", null));
        Assert.True(waiting.GetProperty("hidden").GetBoolean());
        Assert.Equal(0, waiting.GetProperty("reports").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/posts/{reported}")).StatusCode);
        // Another look that the community hid stays hidden through the lift.
        var stillReported = await FromDbPostAsync(userId);
        await ReportPostAsync(stillReported, "s_rep2", 3);

        var lifted = await Json(await moderator.PostAsync("/api/admin/users/susp_one/unsuspend", null));
        Assert.False(lifted.GetProperty("suspended").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/users/susp_one")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/posts/{clean}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/posts/{reported}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/posts/{stillReported}")).StatusCode);
        Assert.Contains(clean, Ids(await anonymous.GetFromJsonAsync<JsonElement>("/api/feed?tab=fresh&limit=30")));
        Assert.Contains((await anonymous.GetFromJsonAsync<JsonElement>($"/api/posts/{othersPost}/comments")).EnumerateArray(), c => c.GetProperty("id").GetGuid() == comment);
        Assert.Equal(1, (await Json(await anonymous.GetAsync($"/api/posts/{othersPost}"))).GetProperty("commentCount").GetInt32());
        var back = await _app.NewClient().PostAsJsonAsync("/api/auth/login", new { handle = "susp_one", password = "password123" });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.False((await Json(back)).GetProperty("isAdmin").GetBoolean());
        Assert.Equal(0, (await moderator.GetFromJsonAsync<JsonElement>("/api/admin/queue")).GetProperty("suspendedUsers").GetInt32());
    }

    /// <summary>A fresh visible look for a suspended account, written straight to the rows: the account itself cannot post.</summary>
    private Task<Guid> FromDbPostAsync(Guid userId)
    {
        var postId = Guid.NewGuid();
        WithDb(db =>
        {
            var template = db.Posts.First(p => p.UserId == userId);
            var check = db.Checks.First(c => c.Id == template.CheckId);
            var checkId = Guid.NewGuid();
            db.Checks.Add(new OutfitCheck
            {
                Id = checkId, UserId = userId, Intent = check.Intent, Language = check.Language, ImagePath = check.ImagePath, Status = check.Status,
                Score = check.Score, FeedbackJson = check.FeedbackJson, PromptVersion = check.PromptVersion, CreatedAt = DateTime.UtcNow
            });
            db.Posts.Add(new Post
            {
                Id = postId, UserId = userId, CheckId = checkId, Intent = template.Intent, Score = template.Score, IntentMatch = template.IntentMatch,
                Headline = template.Headline, Hidden = true, CreatedAt = DateTime.UtcNow
            });
        });
        return Task.FromResult(postId);
    }

    [Fact]
    public async Task A_moderator_cannot_suspend_themselves_and_unknown_handles_are_404()
    {
        var moderator = await ModeratorAsync();
        var self = await moderator.PostAsync($"/api/admin/users/{AdminApp.Moderator}/suspend", null);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Contains("--unadmin", await ErrorOf(self));
        Assert.False((await moderator.GetFromJsonAsync<JsonElement>("/api/auth/me")).TryGetProperty("suspended", out _));
        Assert.Equal(HttpStatusCode.OK, (await moderator.GetAsync("/api/admin/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.PostAsync("/api/admin/users/nobody_here/suspend", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await moderator.PostAsync("/api/admin/users/nobody_here/unsuspend", null)).StatusCode);

        // Lifting a suspension that is not there is not an error, and says so.
        var (plain, _, _) = await _app.NewUserAsync("not_suspended");
        var lifted = await Json(await moderator.PostAsync("/api/admin/users/not_suspended/unsuspend", null));
        Assert.False(lifted.GetProperty("suspended").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync("/api/auth/me")).StatusCode);
    }
}
