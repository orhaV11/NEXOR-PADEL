using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FitCheck.Api.Tests;

public class UserEndpointTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private readonly HttpClient _client;

    public UserEndpointTests(TestApp app)
    {
        _app = app;
        _client = app.CreateClient();
    }

    [Fact]
    public async Task Creates_a_user_with_normalized_language()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { handle = "  noa ", confirmed16Plus = true, language = "he-IL" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, user.GetProperty("id").GetGuid());
        Assert.Equal("noa", user.GetProperty("handle").GetString());
        Assert.Equal("he", user.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Rejects_users_who_do_not_confirm_16_plus_in_their_language()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { handle = "noa", confirmed16Plus = false, language = "he" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("16", error.GetProperty("error").GetString());
        Assert.Contains("גיל", error.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this handle is far too long to be accepted by the api")]
    [InlineData("bad\nhandle")]
    public async Task Rejects_bad_handles(string handle)
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { handle, confirmed16Plus = true, language = "en" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pick a handle between 2 and 40 characters.", error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Missing_handle_is_a_400()
    {
        var response = await _client.PostAsJsonAsync("/api/users", new { confirmed16Plus = true, language = "en" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_language_falls_back_to_accept_language_then_english()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/users")
        {
            Content = JsonContent.Create(new { handle = "noa", confirmed16Plus = true, language = "fr" })
        };
        request.Headers.Add("Accept-Language", "he-IL");
        var response = await _client.SendAsync(request);

        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", user.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Updates_language()
    {
        var id = await _app.CreateUserAsync(_client);

        var response = await _client.PatchAsJsonAsync($"/api/users/{id}", new { language = "he-IL" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", user.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Rejects_unsupported_language_update()
    {
        var id = await _app.CreateUserAsync(_client);
        var response = await _client.PatchAsJsonAsync($"/api/users/{id}", new { language = "fr" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_and_delete_of_unknown_user_are_404()
    {
        var missing = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PatchAsJsonAsync($"/api/users/{missing}", new { language = "en" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/users/{missing}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/users/{missing}/checks")).StatusCode);
    }

    [Fact]
    public async Task Delete_removes_user_checks_and_image_files()
    {
        var id = await _app.CreateUserAsync(_client);
        _app.Vision.Handler = _ => Payloads.Ok();
        for (var i = 0; i < 2; i++)
        {
            var created = await _client.PostAsync("/api/checks", TestApp.CheckForm(id, TestImages.Jpeg()));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var userFolder = Path.Combine(_app.StorageRoot, id.ToString("N"));
        Assert.Equal(2, Directory.GetFiles(userFolder).Length);

        var response = await _client.DeleteAsync($"/api/users/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(Directory.Exists(userFolder));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/users/{id}/checks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/users/{id}")).StatusCode);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitCheck.Api.Data.AppDbContext>();
        Assert.Empty(db.Checks.Where(c => c.UserId == id));
        Assert.Null(db.Users.Find(id));
    }
}
