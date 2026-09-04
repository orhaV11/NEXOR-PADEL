using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

public class CheckEndpointTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;
    private readonly HttpClient _client;

    public CheckEndpointTests(TestApp app)
    {
        _app = app;
        _client = app.CreateClient();
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    [Fact]
    public async Task Happy_path_returns_feedback_and_keeps_the_photo_private()
    {
        var userId = await _app.CreateUserAsync(_client);

        var response = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg(), intent: "Streetwear", occasion: "concert"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<JsonElement>();
        var checkId = check.GetProperty("id").GetGuid();
        Assert.Equal("Streetwear", check.GetProperty("intent").GetString());
        Assert.Equal("concert", check.GetProperty("occasion").GetString());
        Assert.Equal("en", check.GetProperty("language").GetString());
        Assert.Equal("ok", check.GetProperty("status").GetString());
        Assert.Equal(7, check.GetProperty("score").GetInt32());
        Assert.True(check.GetProperty("latencyMs").GetInt32() >= 0);
        var feedback = check.GetProperty("feedback");
        Assert.Equal(72, feedback.GetProperty("intentMatch").GetInt32());
        Assert.Equal("Swap the running shoes for plain white leather sneakers.", feedback.GetProperty("oneTip").GetString());
        Assert.Equal(3, feedback.GetProperty("items").GetArrayLength());
        Assert.False(check.TryGetProperty("imagePath", out _));

        // Photo on disk, under the private root, outside wwwroot ...
        var relative = Path.Combine(userId.ToString("N"), checkId.ToString("N") + ".jpg");
        Assert.True(File.Exists(Path.Combine(_app.StorageRoot, relative)));

        // ... and not reachable through any URL.
        foreach (var url in new[] { $"/{relative.Replace('\\', '/')}", $"/storage/{relative.Replace('\\', '/')}", $"/api/checks/{checkId}/image" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task Owner_can_read_a_check_and_nobody_else_can()
    {
        var owner = await _app.CreateUserAsync(_client);
        var other = await _app.CreateUserAsync(_client, "other");
        var created = await (await _client.PostAsync("/api/checks", TestApp.CheckForm(owner, TestImages.Png()))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/checks/{id}?userId={owner}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/checks/{id}?userId={other}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/checks/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/checks/{Guid.NewGuid()}?userId={owner}")).StatusCode);
    }

    [Fact]
    public async Task Accepts_png_and_webp_by_magic_bytes_regardless_of_file_name()
    {
        var userId = await _app.CreateUserAsync(_client);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Png(), fileName: "whatever.jpg"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.WebP(), fileName: "photo.txt"))).StatusCode);
        var files = Directory.GetFiles(Path.Combine(_app.StorageRoot, userId.ToString("N"))).Select(Path.GetExtension).OrderBy(x => x).ToList();
        Assert.Equal([".png", ".webp"], files);
    }

    [Fact]
    public async Task Oversize_upload_is_413()
    {
        var userId = await _app.CreateUserAsync(_client);
        var response = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg(7 * 1024 * 1024)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("6 MB", error.GetProperty("error").GetString());
        Assert.DoesNotContain(_app.Vision.Requests, r => r.ImageBytes.Length > 6 * 1024 * 1024);
    }

    [Fact]
    public async Task Wrong_format_is_415()
    {
        var userId = await _app.CreateUserAsync(_client);
        var response = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Garbage(), fileName: "photo.jpg"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("Use a JPEG, PNG or WebP photo.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Validation_errors_are_400_and_unknown_user_is_404()
    {
        var userId = await _app.CreateUserAsync(_client);

        var noImage = new MultipartFormDataContent { { new StringContent(userId.ToString()), "userId" }, { new StringContent("Date"), "intent" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/checks", noImage)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg(), intent: "Wedding"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg(), occasion: new string('x', 121)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/checks", TestApp.CheckForm(Guid.NewGuid(), TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/checks", new { userId })).StatusCode);
    }

    [Fact]
    public async Task Twenty_first_check_in_a_day_is_429()
    {
        var userId = await _app.CreateUserAsync(_client);
        for (var i = 0; i < 20; i++)
        {
            var ok = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var response = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("You've reached today's limit of 20 checks. Come back tomorrow.", error.GetProperty("error").GetString());

        // Another user is unaffected.
        var other = await _app.CreateUserAsync(_client, "other");
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/checks", TestApp.CheckForm(other, TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Checks_older_than_24_hours_do_not_count_toward_the_cap()
    {
        var userId = await _app.CreateUserAsync(_client);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 20; i++)
            {
                db.Checks.Add(new OutfitCheck { Id = Guid.NewGuid(), UserId = userId, Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Score = 6, PromptVersion = "v1", CreatedAt = DateTime.UtcNow.AddHours(-25) });
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Model_failure_is_502_with_a_stored_error_row_and_no_photo()
    {
        var userId = await _app.CreateUserAsync(_client, language: "he");
        _app.Vision.Handler = _ => throw new VisionClientException("boom");

        var response = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg(), language: "he"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("הסטייליסט", error.GetProperty("error").GetString());

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = Assert.Single(db.Checks.Where(c => c.UserId == userId));
        Assert.Equal(CheckStatus.Error, row.Status);
        Assert.Equal("", row.ImagePath);
        Assert.Null(row.FeedbackJson);
        Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))) && Directory.GetFiles(Path.Combine(_app.StorageRoot, userId.ToString("N"))).Length > 0);

        // Failed calls do not eat the daily allowance.
        _app.Vision.Handler = _ => Payloads.Ok();
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Not_outfit_keeps_the_message_and_drops_the_photo()
    {
        var userId = await _app.CreateUserAsync(_client);
        _app.Vision.Handler = _ => Payloads.NotOutfit();

        var response = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_outfit", check.GetProperty("status").GetString());
        Assert.False(check.TryGetProperty("score", out var score) && score.ValueKind != JsonValueKind.Null);
        var feedback = check.GetProperty("feedback");
        Assert.Equal("not_outfit", feedback.GetProperty("status").GetString());
        Assert.Contains("desk", feedback.GetProperty("message").GetString());
        Assert.Equal(0, feedback.GetProperty("items").GetArrayLength());
        Assert.Equal(0, feedback.GetProperty("working").GetArrayLength());

        var folder = Path.Combine(_app.StorageRoot, userId.ToString("N"));
        Assert.True(!Directory.Exists(folder) || Directory.GetFiles(folder).Length == 0);
    }

    [Fact]
    public async Task Rejected_stores_only_the_status_and_never_echoes_the_model()
    {
        var userId = await _app.CreateUserAsync(_client, language: "he");
        _app.Vision.Handler = _ => Payloads.Rejected();

        var response = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg(), language: "he"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("MODEL_MESSAGE_THAT_MUST_NOT_LEAK", body);
        var check = JsonDocument.Parse(body).RootElement;
        Assert.Equal("rejected", check.GetProperty("status").GetString());
        Assert.Equal("אי אפשר לבדוק את התמונה הזו.", check.GetProperty("feedback").GetProperty("message").GetString());

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = Assert.Single(db.Checks.Where(c => c.UserId == userId));
        Assert.Equal(CheckStatus.Rejected, row.Status);
        Assert.Null(row.FeedbackJson);
        Assert.Null(row.Score);
        Assert.Equal("", row.ImagePath);
        var folder = Path.Combine(_app.StorageRoot, userId.ToString("N"));
        Assert.True(!Directory.Exists(folder) || Directory.GetFiles(folder).Length == 0);
    }

    [Fact]
    public async Task Api_level_refusal_reads_as_rejected_not_error()
    {
        var userId = await _app.CreateUserAsync(_client);
        _app.Vision.Handler = _ => throw new VisionRefusedException("refused");

        var response = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rejected", check.GetProperty("status").GetString());
        Assert.Equal("We can't check this photo.", check.GetProperty("feedback").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Language_flows_into_the_prompt_and_onto_the_check()
    {
        var userId = await _app.CreateUserAsync(_client, language: "he");
        _app.Vision.Requests.Clear();

        var response = await _client.PostAsync("/api/checks", TestApp.CheckForm(userId, TestImages.Jpeg(), language: "he-IL", intent: "office"));

        var check = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", check.GetProperty("language").GetString());
        Assert.Equal("Office", check.GetProperty("intent").GetString());
        var request = Assert.Single(_app.Vision.Requests);
        Assert.Contains("in Hebrew (he)", request.SystemPrompt);
        Assert.Contains("Office:", request.UserText);
        Assert.Equal("image/jpeg", request.MediaType);
    }

    [Fact]
    public async Task Missing_language_falls_back_to_the_users_preference()
    {
        var userId = await _app.CreateUserAsync(_client, language: "he");
        var form = new MultipartFormDataContent
        {
            { new StringContent(userId.ToString()), "userId" },
            { new StringContent("Casual"), "intent" },
            { new ByteArrayContent(TestImages.Jpeg()), "image", "a.jpg" }
        };

        var check = await (await _client.PostAsync("/api/checks", form)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", check.GetProperty("language").GetString());
    }

    [Fact]
    public async Task History_is_newest_first_and_capped_at_50()
    {
        var userId = await _app.CreateUserAsync(_client);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var start = DateTime.UtcNow.AddDays(-30);
            for (var i = 0; i < 55; i++)
            {
                db.Checks.Add(new OutfitCheck
                {
                    Id = Guid.NewGuid(), UserId = userId, Intent = StyleIntent.Minimal, Language = "en",
                    Status = i % 5 == 0 ? CheckStatus.Error : CheckStatus.Ok, Score = i % 5 == 0 ? null : 5 + i % 5,
                    PromptVersion = "v1", CreatedAt = start.AddMinutes(i), Occasion = i.ToString()
                });
            }
            await db.SaveChangesAsync();
        }

        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/users/{userId}/checks");

        Assert.Equal(50, list.GetArrayLength());
        var occasions = list.EnumerateArray().Select(c => int.Parse(c.GetProperty("occasion").GetString()!)).ToList();
        Assert.Equal(54, occasions[0]);
        Assert.Equal(occasions.OrderByDescending(x => x), occasions);
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("status").GetString() == "error");
    }
}
