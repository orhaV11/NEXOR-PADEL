using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

public class CheckEndpointTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public CheckEndpointTests(TestApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    [Fact]
    public async Task Happy_path_returns_feedback_and_keeps_the_photo_private_until_posted()
    {
        var (client, userId, _) = await _app.NewUserAsync("checker1");

        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), intent: "Streetwear", occasion: "concert"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var check = await response.Content.ReadFromJsonAsync<JsonElement>();
        var checkId = check.GetProperty("id").GetGuid();
        Assert.Equal("Streetwear", check.GetProperty("intent").GetString());
        Assert.Equal("concert", check.GetProperty("occasion").GetString());
        Assert.Equal("ok", check.GetProperty("status").GetString());
        Assert.Equal(7, check.GetProperty("score").GetInt32());
        Assert.False(check.TryGetProperty("postId", out var postId) && postId.ValueKind != JsonValueKind.Null);
        var feedback = check.GetProperty("feedback");
        Assert.Equal(72, feedback.GetProperty("intentMatch").GetInt32());
        Assert.Equal("Swap the running shoes for plain white leather sneakers.", feedback.GetProperty("oneTip").GetString());
        Assert.False(check.TryGetProperty("imagePath", out _));

        // Photo on disk, under the private root, outside wwwroot ...
        var relative = Path.Combine(userId.ToString("N"), checkId.ToString("N") + ".jpg");
        Assert.True(File.Exists(Path.Combine(_app.StorageRoot, relative)));

        // ... and not reachable through any URL, including the post image route (there is no post).
        var anonymous = _app.NewClient();
        foreach (var url in new[] { $"/{relative.Replace('\\', '/')}", $"/storage/{relative.Replace('\\', '/')}", $"/api/checks/{checkId}/image", $"/api/posts/{checkId}/image" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task Owner_can_read_a_check_and_nobody_else_can()
    {
        var (owner, _, _) = await _app.NewUserAsync("owner1");
        var (other, _, _) = await _app.NewUserAsync("other1");
        var id = await _app.CheckAsync(owner);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/checks/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/checks/{id}")).StatusCode);
        // The route is public since guests read their own check by cookie (Round 9); a stranger still gets the same 404.
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync($"/api/checks/{id}")).StatusCode);
    }

    [Fact]
    public async Task Accepts_png_and_webp_by_magic_bytes_regardless_of_file_name()
    {
        var (client, userId, _) = await _app.NewUserAsync("formats1");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Png(), fileName: "whatever.jpg"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.WebP(), fileName: "photo.txt"))).StatusCode);
        var files = Directory.GetFiles(Path.Combine(_app.StorageRoot, userId.ToString("N"))).Select(Path.GetExtension).OrderBy(x => x).ToList();
        Assert.Equal([".png", ".webp"], files);
    }

    [Fact]
    public async Task Oversize_upload_is_413()
    {
        var (client, _, _) = await _app.NewUserAsync("big1");
        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(7 * 1024 * 1024)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("6 MB", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Wrong_format_is_415()
    {
        var (client, _, _) = await _app.NewUserAsync("garbage1");
        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Garbage(), fileName: "photo.jpg"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("Use a JPEG, PNG or WebP photo.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Validation_errors_are_400()
    {
        var (client, _, _) = await _app.NewUserAsync("validate1");

        var noImage = new MultipartFormDataContent { { new StringContent("Date"), "intent" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/checks", noImage)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), intent: "Wedding"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), occasion: new string('x', 121)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/checks", new { intent = "Date" })).StatusCode);
    }

    [Fact]
    public async Task Twenty_first_check_in_a_day_is_429()
    {
        var (client, _, _) = await _app.NewUserAsync("capped1");
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
        }

        var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        // The cap is the plan's (Plans:FreeChecksPerDay, 20 in this suite's app) and the refusal names the Pro cap (Round 9).
        Assert.Equal("That's today's 20 free checks. Go Pro for 20 a day, or come back tomorrow.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var (other, _, _) = await _app.NewUserAsync("uncapped1");
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Checks_older_than_24_hours_do_not_count_toward_the_cap()
    {
        var (client, userId, _) = await _app.NewUserAsync("old1");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 20; i++)
            {
                db.Checks.Add(new OutfitCheck { Id = Guid.NewGuid(), UserId = userId, Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Score = 6, PromptVersion = "v1", CreatedAt = DateTime.UtcNow.AddHours(-25) });
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Model_failure_is_502_with_a_stored_error_row_and_no_photo()
    {
        var (client, userId, _) = await _app.NewUserAsync("fail1", language: "he");
        _app.Vision.Handler = _ => throw new VisionClientException("boom");
        try
        {
            var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "he"));

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Contains("הסטייליסט", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = Assert.Single(db.Checks.Where(c => c.UserId == userId));
            Assert.Equal(CheckStatus.Error, row.Status);
            Assert.Equal("", row.ImagePath);
            Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
        }
        finally
        {
            _app.Vision.Handler = _ => Payloads.Ok();
        }

        // Failed calls do not eat the daily allowance.
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Not_outfit_keeps_the_message_and_never_writes_the_photo()
    {
        var (client, userId, _) = await _app.NewUserAsync("desk1");
        _app.Vision.Handler = _ => Payloads.NotOutfit();
        try
        {
            var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var check = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("not_outfit", check.GetProperty("status").GetString());
            Assert.Contains("desk", check.GetProperty("feedback").GetProperty("message").GetString());
            Assert.Equal(0, check.GetProperty("feedback").GetProperty("items").GetArrayLength());
            Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
        }
        finally
        {
            _app.Vision.Handler = _ => Payloads.Ok();
        }
    }

    [Fact]
    public async Task Rejected_stores_only_the_status_and_never_echoes_the_model()
    {
        var (client, userId, _) = await _app.NewUserAsync("reject1", language: "he");
        _app.Vision.Handler = _ => Payloads.Rejected();
        try
        {
            var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: "he", occasion: "my cousin's birthday"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("MODEL_MESSAGE_THAT_MUST_NOT_LEAK", body);
            Assert.DoesNotContain("birthday", body);
            var check = JsonDocument.Parse(body).RootElement;
            Assert.Equal("rejected", check.GetProperty("status").GetString());
            Assert.Equal("אי אפשר לבדוק את התמונה הזו.", check.GetProperty("feedback").GetProperty("message").GetString());

            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = Assert.Single(db.Checks.Where(c => c.UserId == userId));
            Assert.Equal(CheckStatus.Rejected, row.Status);
            Assert.Null(row.FeedbackJson);
            Assert.Null(row.Score);
            Assert.Null(row.Occasion);
            Assert.Equal("", row.ImagePath);
        }
        finally
        {
            _app.Vision.Handler = _ => Payloads.Ok();
        }
    }

    [Fact]
    public async Task Api_level_refusal_reads_as_rejected_not_error()
    {
        var (client, _, _) = await _app.NewUserAsync("refused1");
        _app.Vision.Handler = _ => throw new VisionRefusedException("refused");
        try
        {
            var response = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg()));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var check = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("rejected", check.GetProperty("status").GetString());
            Assert.Equal("We can't check this photo.", check.GetProperty("feedback").GetProperty("message").GetString());
        }
        finally
        {
            _app.Vision.Handler = _ => Payloads.Ok();
        }
    }

    [Fact]
    public async Task Language_flows_into_the_prompt_and_falls_back_to_the_users_preference()
    {
        var (client, _, _) = await _app.NewUserAsync("lang1", language: "he");
        lock (_app.Vision.Requests) { _app.Vision.Requests.Clear(); }

        var explicitHe = await (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), intent: "office", language: "he-IL"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", explicitHe.GetProperty("language").GetString());
        Assert.Equal("Office", explicitHe.GetProperty("intent").GetString());
        VisionRequest request;
        lock (_app.Vision.Requests) { request = _app.Vision.Requests.Last(); }
        Assert.Contains("in Hebrew (he)", request.SystemPrompt);
        Assert.Contains("Office:", request.UserText);

        var missing = await (await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(), language: null))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", missing.GetProperty("language").GetString());

        var stale = new HttpRequestMessage(HttpMethod.Post, "/api/checks") { Content = TestApp.CheckForm(TestImages.Jpeg(), language: "fr") };
        stale.Headers.Add("Accept-Language", "en-US");
        var staleCheck = await (await client.SendAsync(stale)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", staleCheck.GetProperty("language").GetString());
    }

    [Fact]
    public async Task History_is_newest_first_and_capped_at_50()
    {
        var (client, userId, _) = await _app.NewUserAsync("history1");
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

        var list = await client.GetFromJsonAsync<JsonElement>("/api/users/me/checks");

        Assert.Equal(50, list.GetArrayLength());
        var occasions = list.EnumerateArray().Select(c => int.Parse(c.GetProperty("occasion").GetString()!)).ToList();
        Assert.Equal(54, occasions[0]);
        Assert.Equal(occasions.OrderByDescending(x => x), occasions);
    }

    [Fact]
    public async Task Streak_counts_consecutive_days_of_ok_checks()
    {
        var (client, userId, _) = await _app.NewUserAsync("streak1");

        await _app.CheckAsync(client);
        await _app.CheckAsync(client);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(1, me.GetProperty("streak").GetInt32());

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FindAsync(userId);
            user!.LastCheckDate = DateTime.UtcNow.Date.AddDays(-1);
            user.StreakCount = 4;
            await db.SaveChangesAsync();
        }

        await _app.CheckAsync(client);
        me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(5, me.GetProperty("streak").GetInt32());

        var user2 = new AppUser { LastCheckDate = DateTime.UtcNow.Date.AddDays(-3), StreakCount = 9 };
        CheckEndpoints.UpdateStreak(user2, DateTime.UtcNow);
        Assert.Equal(1, user2.StreakCount);
    }
}
