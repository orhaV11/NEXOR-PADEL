using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Endpoints;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace FitCheck.Api.Tests;

/// <summary>
/// "Which one?" end to end against the real app with the scripted stylist: both photos stored under the owner and served
/// to the owner only, the shared daily allowance with checks, the Pro gate, the statuses that keep no photo, and what an
/// account deletion takes with it.
/// </summary>
/// <summary>The real plan world: three free checks a day (the shared TestApp keeps the old twenty for the older suites).</summary>
public sealed class CompareApp : TestApp
{
    public CompareApp()
    {
        FreeChecksPerDay = 3;
    }
}

public class CompareTests : IClassFixture<CompareApp>
{
    private readonly TestApp _app;

    public CompareTests(CompareApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => OutfitComparerTests.Pick();
    }

    /// <summary>The comparison form; null for a side leaves that photo out.</summary>
    public static MultipartFormDataContent CompareForm(byte[]? imageA, byte[]? imageB, string intent = "Date", string? language = "en", string? occasion = null)
    {
        var form = new MultipartFormDataContent { { new StringContent(intent), "intent" } };
        if (language is not null)
        {
            form.Add(new StringContent(language), "language");
        }

        if (occasion is not null)
        {
            form.Add(new StringContent(occasion), "occasion");
        }

        foreach (var (bytes, name) in new[] { (imageA, "imageA"), (imageB, "imageB") })
        {
            if (bytes is null)
            {
                continue;
            }

            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, name, "outfit.jpg");
        }

        return form;
    }

    private static string SidePath(Guid userId, Guid comparisonId, string side, string extension) =>
        Path.Combine(userId.ToString("N"), CompareEndpoints.SideImageId(comparisonId, side).ToString("N") + extension);

    private async Task SetPlanAsync(Guid userId, string plan, DateTime? proUntil = null)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(userId);
        user!.Plan = plan;
        user.ProUntil = proUntil;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Happy_path_stores_both_photos_under_the_owner_and_serves_them_to_the_owner_only()
    {
        var (client, userId, _) = await _app.NewUserAsync("cmp1");

        var response = await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Png(), intent: "Office", occasion: "first day"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetGuid();
        Assert.Equal("Office", dto.GetProperty("intent").GetString());
        Assert.Equal("first day", dto.GetProperty("occasion").GetString());
        Assert.Equal("en", dto.GetProperty("language").GetString());
        Assert.Equal("ok", dto.GetProperty("status").GetString());
        Assert.True(dto.GetProperty("latencyMs").GetInt32() >= 0);
        Assert.Equal($"/api/compare/{id}/image/a", dto.GetProperty("imageUrlA").GetString());
        Assert.Equal($"/api/compare/{id}/image/b", dto.GetProperty("imageUrlB").GetString());
        var feedback = dto.GetProperty("feedback");
        Assert.Equal("b", feedback.GetProperty("winner").GetString());
        Assert.Equal(6, feedback.GetProperty("scoreA").GetInt32());
        Assert.Equal(8, feedback.GetProperty("scoreB").GetInt32());
        Assert.Equal("Safe casual, a little flat", feedback.GetProperty("headlineA").GetString());
        Assert.Equal("Sharper lines, clearer intent", feedback.GetProperty("headlineB").GetString());
        Assert.StartsWith("B reads as the intent", feedback.GetProperty("reason").GetString());
        Assert.StartsWith("For A, swap", feedback.GetProperty("oneTip").GetString());
        Assert.False(dto.TryGetProperty("imagePathA", out _));

        // Both stills on disk under the owner's folder, each in the format its bytes said, never the file name.
        var pathA = SidePath(userId, id, "a", ".jpg");
        var pathB = SidePath(userId, id, "b", ".png");
        Assert.True(File.Exists(Path.Combine(_app.StorageRoot, pathA)));
        Assert.True(File.Exists(Path.Combine(_app.StorageRoot, pathB)));
        Assert.Equal(TestImages.Png(), await File.ReadAllBytesAsync(Path.Combine(_app.StorageRoot, pathB)));

        // The row remembers the winner and the prompt version.
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Comparisons.SingleAsync(c => c.Id == id);
            Assert.Equal("b", row.Winner);
            Assert.Equal("cmp-v1", row.PromptVersion);
            Assert.Equal(pathA, row.ImagePathA);
            Assert.Equal(pathB, row.ImagePathB);
        }

        // Served to the owner, private, in the right media type.
        var imageA = await client.GetAsync($"/api/compare/{id}/image/a");
        Assert.Equal(HttpStatusCode.OK, imageA.StatusCode);
        Assert.Equal("image/jpeg", imageA.Content.Headers.ContentType!.MediaType);
        Assert.True(imageA.Headers.CacheControl!.Private);
        Assert.Equal(TestImages.Jpeg(), await imageA.Content.ReadAsByteArrayAsync());
        var imageB = await client.GetAsync($"/api/compare/{id}/image/b");
        Assert.Equal("image/png", imageB.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/compare/{id}/image/c")).StatusCode);

        // The comparison itself: the owner reads it back, nobody else does.
        var read = await client.GetFromJsonAsync<JsonElement>($"/api/compare/{id}");
        Assert.Equal("b", read.GetProperty("feedback").GetProperty("winner").GetString());
        var (other, _, _) = await _app.NewUserAsync("cmp1other");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/compare/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/compare/{id}/image/a")).StatusCode);
        Assert.Equal("We couldn't find this comparison.", (await (await other.GetAsync($"/api/compare/{id}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        var anonymous = _app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/compare/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/compare/{id}/image/a")).StatusCode);
        foreach (var url in new[] { $"/{pathA.Replace('\\', '/')}", $"/storage/{pathA.Replace('\\', '/')}", $"/api/posts/{id}/image", $"/api/checks/{id}/image" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);
        }

        // And it is in the history.
        var history = await client.GetFromJsonAsync<JsonElement>("/api/users/me/comparisons");
        Assert.Equal(id, Assert.Single(history.EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_stylist_gets_both_images_in_order_with_the_language_and_the_intent()
    {
        var (client, _, _) = await _app.NewUserAsync("cmp2", language: "he");
        lock (_app.Vision.Requests) { _app.Vision.Requests.Clear(); }

        var dto = await (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.WebP(), intent: "party", language: "he-IL", occasion: "rooftop"))).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("he", dto.GetProperty("language").GetString());
        Assert.Equal("Party", dto.GetProperty("intent").GetString());
        VisionRequest request;
        lock (_app.Vision.Requests) { request = Assert.Single(_app.Vision.Requests); }
        Assert.Same(OutfitComparer.Tool, request.Tool);
        Assert.True(request.HasSecondImage);
        Assert.Equal(TestImages.Jpeg(), request.ImageBytes.ToArray());
        Assert.Equal("image/jpeg", request.MediaType);
        Assert.Equal(TestImages.WebP(), request.ImageBytes2.ToArray());
        Assert.Equal("image/webp", request.MediaType2);
        Assert.Contains("in Hebrew (he)", request.SystemPrompt);
        Assert.Contains("Party:", request.UserText);
        Assert.Contains("\"rooftop\"", request.UserText);

        // A missing language falls back to the stored preference, never to a header guess.
        var fallback = await (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg(), language: null))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("he", fallback.GetProperty("language").GetString());
    }

    [Fact]
    public async Task Missing_either_photo_is_400_and_stores_nothing()
    {
        var (client, userId, _) = await _app.NewUserAsync("cmp3");

        foreach (var form in new[] { CompareForm(TestImages.Jpeg(), null), CompareForm(null, TestImages.Jpeg()), CompareForm(null, null), CompareForm(TestImages.Jpeg(), []) })
        {
            var response = await client.PostAsync("/api/compare", form);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Add both photos.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        }

        var hebrew = await client.PostAsync("/api/compare", CompareForm(null, TestImages.Jpeg(), language: "he"));
        Assert.Equal("צריך להוסיף את שתי התמונות.", (await hebrew.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg(), intent: "Wedding"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg(), occasion: new string('x', 121)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/compare", new { intent = "Date" })).StatusCode);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Comparisons.AnyAsync(c => c.UserId == userId));
        Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
    }

    [Fact]
    public async Task Each_photo_is_validated_like_a_checks_still()
    {
        var (client, _, _) = await _app.NewUserAsync("cmp4");

        var big = await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg(7 * 1024 * 1024)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        Assert.Contains("6 MB", (await big.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var garbage = await client.PostAsync("/api/compare", CompareForm(TestImages.Garbage(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, garbage.StatusCode);
        Assert.Equal("Use a JPEG, PNG or WebP photo.", (await garbage.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Garbage()))).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await _app.BareClient().PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Comparisons_share_the_daily_allowance_with_checks()
    {
        // A free account: Plans:FreeChecksPerDay (3) under the Limits:ChecksPerDay ceiling the test host sets (20).
        var (client, userId, _) = await _app.NewUserAsync("cmp5");
        await _app.CheckAsync(client);
        await _app.CheckAsync(client);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);

        var capped = await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        Assert.True(capped.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("That's today's 3 free checks. Go Pro for 30 a day, or come back tomorrow.", (await capped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        // Pro raises the cap (to Plans:ProChecksPerDay, never above Limits:ChecksPerDay); a lapsed Pro is free again.
        await SetPlanAsync(userId, Plans.Pro);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);
        await SetPlanAsync(userId, Plans.Pro, DateTime.UtcNow.AddDays(-1));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);

        // Yesterday's do not count.
        var (old, oldId, _) = await _app.NewUserAsync("cmp5old");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 3; i++)
            {
                db.Comparisons.Add(new OutfitComparison { Id = Guid.NewGuid(), UserId = oldId, Intent = StyleIntent.Casual, Language = "en", Status = CheckStatus.Ok, Winner = "a", PromptVersion = "cmp-v1", CreatedAt = DateTime.UtcNow.AddHours(-25) });
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await old.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Pro_gate_when_the_plan_says_comparisons_need_pro()
    {
        using var app = new ProGatedApp();
        app.Vision.Handler = _ => OutfitComparerTests.Pick();
        var (client, userId, _) = await app.NewUserAsync("cmp6");

        var refused = await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("This one is for Pro.", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Empty(app.Vision.Requests);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FindAsync(userId);
            user!.Plan = Plans.Pro;
            user.ProUntil = DateTime.UtcNow.AddDays(30);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.FindAsync(userId);
            user!.ProUntil = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);

        // Reading what was made while Pro is not gated.
        var history = await client.GetFromJsonAsync<JsonElement>("/api/users/me/comparisons");
        Assert.Equal(1, history.GetArrayLength());
    }

    [Fact]
    public async Task Not_outfit_keeps_the_message_and_never_writes_a_photo()
    {
        var (client, userId, _) = await _app.NewUserAsync("cmp7");
        _app.Vision.Handler = _ => OutfitComparerTests.Pick(status: "not_outfit", message: "Photo A looks like a wall. Try one where the clothes are visible.");
        try
        {
            var response = await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("not_outfit", dto.GetProperty("status").GetString());
            Assert.Contains("Photo A", dto.GetProperty("feedback").GetProperty("message").GetString());
            Assert.Equal("", dto.GetProperty("feedback").GetProperty("winner").GetString());
            Assert.Equal("", dto.GetProperty("imageUrlA").GetString());
            Assert.Equal("", dto.GetProperty("imageUrlB").GetString());
            Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/compare/{dto.GetProperty("id").GetGuid()}/image/a")).StatusCode);

            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = Assert.Single(db.Comparisons.Where(c => c.UserId == userId));
            Assert.Equal(CheckStatus.NotOutfit, row.Status);
            Assert.Equal("", row.Winner);
            Assert.Equal("", row.ImagePathA);
            Assert.NotNull(row.FeedbackJson);
        }
        finally
        {
            _app.Vision.Handler = _ => OutfitComparerTests.Pick();
        }
    }

    [Fact]
    public async Task Rejected_stores_only_the_status_and_never_echoes_the_model()
    {
        var (client, userId, _) = await _app.NewUserAsync("cmp8", language: "he");
        _app.Vision.Handler = _ => OutfitComparerTests.Pick(status: "rejected", message: "MODEL_MESSAGE_THAT_MUST_NOT_LEAK");
        try
        {
            var response = await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg(), language: "he", occasion: "my cousin's birthday"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("MODEL_MESSAGE_THAT_MUST_NOT_LEAK", body);
            Assert.DoesNotContain("birthday", body);
            var dto = JsonDocument.Parse(body).RootElement;
            Assert.Equal("rejected", dto.GetProperty("status").GetString());
            Assert.Equal("אי אפשר לבדוק את התמונה הזו.", dto.GetProperty("feedback").GetProperty("message").GetString());

            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = Assert.Single(db.Comparisons.Where(c => c.UserId == userId));
            Assert.Equal(CheckStatus.Rejected, row.Status);
            Assert.Null(row.FeedbackJson);
            Assert.Null(row.Occasion);
            Assert.Equal("", row.Winner);
            Assert.Equal("", row.ImagePathA);
            Assert.Equal("", row.ImagePathB);
            Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
        }
        finally
        {
            _app.Vision.Handler = _ => OutfitComparerTests.Pick();
        }

        // An API-level refusal reads the same way.
        _app.Vision.Handler = _ => throw new VisionRefusedException("refused");
        try
        {
            var refused = await (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("rejected", refused.GetProperty("status").GetString());
        }
        finally
        {
            _app.Vision.Handler = _ => OutfitComparerTests.Pick();
        }
    }

    [Fact]
    public async Task Model_failure_is_502_with_an_error_row_no_photo_and_no_charge_against_the_allowance()
    {
        var (client, userId, _) = await _app.NewUserAsync("cmp9");
        _app.Vision.Handler = _ => throw new VisionClientException("boom");
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var response = await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()));
                Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
                Assert.Equal("The stylist couldn't look at this one. Please try again in a moment.", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
            }

            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(3, await db.Comparisons.CountAsync(c => c.UserId == userId && c.Status == CheckStatus.Error));
            Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
        }
        finally
        {
            _app.Vision.Handler = _ => OutfitComparerTests.Pick();
        }

        // Three failures did not eat the three free calls.
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_removes_the_comparisons_and_their_photos()
    {
        var (client, userId, _) = await _app.NewUserAsync("cmp10");
        var dto = await (await client.PostAsync("/api/compare", CompareForm(TestImages.Jpeg(), TestImages.Jpeg()))).Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetGuid();
        var folder = Path.Combine(_app.StorageRoot, userId.ToString("N"));
        Assert.Equal(2, Directory.GetFiles(folder).Length);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/users/me")).StatusCode);

        Assert.False(Directory.Exists(folder));
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Comparisons.AnyAsync(c => c.Id == id));
        Assert.False(await db.Comparisons.AnyAsync(c => c.UserId == userId));
    }

    [Fact]
    public async Task History_is_newest_first_and_capped_at_20()
    {
        var (client, userId, _) = await _app.NewUserAsync("cmp11");
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var start = DateTime.UtcNow.AddDays(-30);
            for (var i = 0; i < 25; i++)
            {
                db.Comparisons.Add(new OutfitComparison
                {
                    Id = Guid.NewGuid(), UserId = userId, Intent = StyleIntent.Minimal, Language = "en",
                    Status = i % 5 == 0 ? CheckStatus.Error : CheckStatus.Ok, Winner = i % 5 == 0 ? "" : "a",
                    PromptVersion = "cmp-v1", CreatedAt = start.AddMinutes(i), Occasion = i.ToString()
                });
            }
            await db.SaveChangesAsync();
        }

        var list = await client.GetFromJsonAsync<JsonElement>("/api/users/me/comparisons");

        Assert.Equal(20, list.GetArrayLength());
        var occasions = list.EnumerateArray().Select(c => int.Parse(c.GetProperty("occasion").GetString()!)).ToList();
        Assert.Equal(24, occasions[0]);
        Assert.Equal(occasions.OrderByDescending(x => x), occasions);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().GetAsync("/api/users/me/comparisons")).StatusCode);
    }

    [Fact]
    public void Side_image_ids_are_distinct_per_side_and_stable()
    {
        var id = Guid.NewGuid();
        Assert.Equal(CompareEndpoints.SideImageId(id, "a"), CompareEndpoints.SideImageId(id, "a"));
        Assert.NotEqual(CompareEndpoints.SideImageId(id, "a"), CompareEndpoints.SideImageId(id, "b"));
        Assert.NotEqual(id, CompareEndpoints.SideImageId(id, "a"));
        Assert.NotEqual(id, CompareEndpoints.SideImageId(id, "b"));
    }

    /// <summary>The test host with Plans:CompareNeedsPro on, as a server that sells comparisons runs.</summary>
    private sealed class ProGatedApp : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Plans:CompareNeedsPro", "true");
        }
    }
}
