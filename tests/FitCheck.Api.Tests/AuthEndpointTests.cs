using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

public class AuthEndpointTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public AuthEndpointTests(TestApp app) => _app = app;

    [Fact]
    public async Task Signup_signs_in_and_me_works_on_the_same_cookie_jar()
    {
        var client = _app.NewClient();
        var me = await _app.SignupAsync(client, "Noa_1", language: "he-IL", displayName: "  Noa  ");

        Assert.Equal("Noa_1", me.GetProperty("handle").GetString());
        Assert.Equal("Noa", me.GetProperty("name").GetString());
        Assert.Equal("he", me.GetProperty("language").GetString());
        Assert.Equal("Person", me.GetProperty("accountType").GetString());
        Assert.Equal(0, me.GetProperty("unreadNotifications").GetInt32());

        var again = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(me.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());

        // A different client has no cookie and is signed out.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().GetAsync("/api/auth/me")).StatusCode);
    }

    /// <summary>Round 9: the date of birth decides (SignupDobTests); the 16+ checkbox older clients still send is ignored either way.</summary>
    [Fact]
    public async Task The_16_plus_checkbox_is_ignored_once_a_birth_date_is_given()
    {
        var response = await _app.NewClient().PostAsJsonAsync("/api/auth/signup", new { handle = "young", password = "password123", confirmed16Plus = false, birthDate = "1990-01-01", language = "he" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("me")]
    [InlineData("bad\nhandle")]
    [InlineData("this_handle_is_far_too_long_to_be_accepted_by_the_api")]
    public async Task Rejects_bad_handles(string handle)
    {
        var response = await _app.NewClient().PostAsJsonAsync("/api/auth/signup", new { handle, password = "password123", confirmed16Plus = true, language = "en" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("2 to 40", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Hebrew_handles_are_fine()
    {
        var me = await _app.SignupAsync(_app.NewClient(), "נועה.כהן");
        Assert.Equal("נועה.כהן", me.GetProperty("handle").GetString());
        var profile = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/users/" + Uri.EscapeDataString("נועה.כהן"));
        Assert.Equal("נועה.כהן", profile.GetProperty("handle").GetString());
    }

    [Fact]
    public async Task Rejects_short_passwords()
    {
        var response = await _app.NewClient().PostAsJsonAsync("/api/auth/signup", new { handle = "shortpw", password = "1234567", confirmed16Plus = true, language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Handles_are_unique_case_insensitively()
    {
        await _app.SignupAsync(_app.NewClient(), "Taken");
        var response = await _app.NewClient().PostAsJsonAsync("/api/auth/signup", new { handle = "TAKEN", password = "password123", confirmed16Plus = true, language = "en" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("That handle is taken.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Login_logout_round_trip()
    {
        await _app.SignupAsync(_app.NewClient(), "loginme", password: "correct horse");

        var wrong = await _app.NewClient().PostAsJsonAsync("/api/auth/login", new { handle = "loginme", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var unknown = await _app.NewClient().PostAsJsonAsync("/api/auth/login", new { handle = "nobody", password = "correct horse" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

        var client = _app.NewClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { handle = "LOGINME", password = "correct horse" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal("loginme", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("handle").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task State_changing_calls_need_the_request_header()
    {
        var bare = _app.BareClient();
        var response = await bare.PostAsJsonAsync("/api/auth/signup", new { handle = "csrf", password = "password123", confirmed16Plus = true, language = "en" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        // Reads never need it.
        Assert.Equal(HttpStatusCode.OK, (await bare.GetAsync("/api/feed")).StatusCode);
    }

    [Fact]
    public async Task Signed_out_writes_get_a_json_401_not_a_redirect()
    {
        // Posting a look needs a session (a check does not since Round 9: a visitor gets one as a guest).
        var response = await _app.NewClient().PostAsJsonAsync("/api/posts", new { checkId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Sign in to continue.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Updates_profile_fields_and_validates_them()
    {
        var (client, _, _) = await _app.NewUserAsync("editor");

        var ok = await client.PatchAsJsonAsync("/api/users/me", new { language = "he-IL", displayName = "Edit Or", bio = "  hello  ", website = "https://example.com" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var me = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", me.GetProperty("language").GetString());
        Assert.Equal("Edit Or", me.GetProperty("name").GetString());
        Assert.Equal("hello", me.GetProperty("bio").GetString());
        Assert.Equal("https://example.com", me.GetProperty("website").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/users/me", new { website = "http://insecure.example" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/users/me", new { bio = new string('x', 161) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/api/users/me", new { language = "fr" })).StatusCode);

        // Clearing works with an empty string; omitted fields are untouched.
        var cleared = await (await client.PatchAsJsonAsync("/api/users/me", new { displayName = "" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("editor", cleared.GetProperty("name").GetString());
        Assert.Equal("hello", cleared.GetProperty("bio").GetString());
    }

    [Fact]
    public async Task Delete_me_removes_everything_and_fixes_other_peoples_counters()
    {
        var (alice, aliceId, _) = await _app.NewUserAsync("alice_del");
        var (bob, _, _) = await _app.NewUserAsync("bob_del");
        _app.Vision.Handler = _ => Payloads.Ok();

        var alicePost = await _app.CheckAndPostAsync(alice);
        var bobPost = await _app.CheckAndPostAsync(bob);
        await alice.PostAsync($"/api/posts/{bobPost}/fire", null);
        await alice.PostAsJsonAsync($"/api/posts/{bobPost}/comments", new { text = "nice" });
        await alice.PostAsync("/api/users/bob_del/follow", null);
        await bob.PostAsync($"/api/posts/{alicePost}/fire", null);
        await bob.PostAsync("/api/users/alice_del/follow", null);

        var userFolder = Path.Combine(_app.StorageRoot, aliceId.ToString("N"));
        Assert.True(Directory.Exists(userFolder) && Directory.GetFiles(userFolder).Length == 1);

        var response = await alice.DeleteAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(Directory.Exists(userFolder));
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync("/api/users/alice_del")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/posts/{alicePost}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/posts/{alicePost}/image")).StatusCode);

        var bobsPost = await bob.GetFromJsonAsync<JsonElement>($"/api/posts/{bobPost}");
        Assert.Equal(0, bobsPost.GetProperty("fireCount").GetInt32());
        Assert.Equal(0, bobsPost.GetProperty("commentCount").GetInt32());
        var bobProfile = await bob.GetFromJsonAsync<JsonElement>("/api/users/bob_del");
        Assert.Equal(0, bobProfile.GetProperty("followers").GetInt32());
        Assert.Equal(0, bobProfile.GetProperty("following").GetInt32());

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(db.Users.Find(aliceId));
        Assert.Empty(db.Checks.Where(c => c.UserId == aliceId));
        Assert.Empty(db.Posts.Where(p => p.UserId == aliceId));
        Assert.Empty(db.Fires.Where(f => f.UserId == aliceId));
        Assert.Empty(db.Comments.Where(c => c.UserId == aliceId));
        Assert.Empty(db.Follows.Where(f => f.FollowerId == aliceId || f.FollowedId == aliceId));
        Assert.Empty(db.Notifications.Where(n => n.UserId == aliceId));
    }

    [Fact]
    public async Task A_cookie_that_outlives_its_account_is_signed_out()
    {
        var (client, id, _) = await _app.NewUserAsync("ghost");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Remove(db.Users.Find(id)!);
            await db.SaveChangesAsync();
        }

        // The stale cookie is refused and dropped by the first refusal, whichever door it knocks on; with no cookie at all
        // a check would be a guest's (Round 9), so the check goes first, while the cookie is still there.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
