using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Tests;

/// <summary>Hand-made containers: the server checks magic bytes and never decodes a frame, so a header plus padding is a clip.</summary>
public static class TestClips
{
    /// <summary>EBML header id, then a plausible header size; what every WebM starts with.</summary>
    public static byte[] WebM(int size = 4096) => WithHeader([0x1A, 0x45, 0xDF, 0xA3, 0xA3, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81], size);

    /// <summary>A 24-byte ftyp box with the given brand: "isom" from most encoders, "qt  " from an iPhone, "mp42", "avc1"...</summary>
    public static byte[] Mp4(int size = 4096, string brand = "isom")
    {
        var header = new byte[12];
        header[3] = 0x18;
        "ftyp"u8.CopyTo(header.AsSpan(4));
        Encoding.ASCII.GetBytes(brand.PadRight(4)[..4]).CopyTo(header, 8);
        return WithHeader(header, size);
    }

    public static byte[] Garbage(int size = 4096) => WithHeader("definitely not a clip"u8.ToArray(), size);

    /// <summary>A check form with the still and a clip in the optional "video" field.</summary>
    public static MultipartFormDataContent Form(byte[] clip, byte[]? image = null, string intent = "Date", string fileName = "look.webm")
    {
        var form = TestApp.CheckForm(image ?? TestImages.Jpeg(), intent);
        var file = new ByteArrayContent(clip);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "video", fileName);
        return form;
    }

    private static byte[] WithHeader(byte[] header, int size)
    {
        var bytes = new byte[Math.Max(size, header.Length)];
        header.CopyTo(bytes, 0);
        for (var i = header.Length; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }
}

public class ClipTests : IClassFixture<ClipTests.ClipApp>
{
    public sealed class ClipApp : TestApp;

    private readonly ClipApp _app;

    public ClipTests(ClipApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(Guid CheckId, Guid PostId)> CheckAndPostClipAsync(HttpClient client, byte[] clip, string fileName = "look.webm")
    {
        var response = await client.PostAsync("/api/checks", TestClips.Form(clip, fileName: fileName));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var checkId = (await Json(response)).GetProperty("id").GetGuid();
        var postId = (await _app.PostAsync(client, checkId)).GetProperty("id").GetGuid();
        return (checkId, postId);
    }

    private async Task<string?> VideoPathAsync(Guid checkId)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Checks.Where(c => c.Id == checkId).Select(c => c.VideoPath).SingleAsync();
    }

    [Fact]
    public async Task Webm_clip_is_stored_next_to_the_still_and_served_through_the_post()
    {
        var (client, userId, _) = await _app.NewUserAsync("clip_webm");
        var clip = TestClips.WebM(200 * 1024);

        var response = await client.PostAsync("/api/checks", TestClips.Form(clip, fileName: "anything.bin"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var check = await Json(response);
        Assert.Equal("ok", check.GetProperty("status").GetString());
        Assert.False(check.TryGetProperty("videoPath", out _));
        var checkId = check.GetProperty("id").GetGuid();

        // The still and the clip sit side by side under the private root, named by the check.
        var folder = Path.Combine(_app.StorageRoot, userId.ToString("N"));
        Assert.True(File.Exists(Path.Combine(folder, checkId.ToString("N") + ".jpg")));
        var clipFile = Path.Combine(folder, checkId.ToString("N") + ".webm");
        Assert.True(File.Exists(clipFile));
        Assert.Equal(clip, await File.ReadAllBytesAsync(clipFile));
        Assert.Equal(Path.Combine(userId.ToString("N"), checkId.ToString("N") + ".webm"), await VideoPathAsync(checkId));

        // No door until the look is posted.
        var anonymous = _app.NewClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/posts/{checkId}/video")).StatusCode);

        var post = await _app.PostAsync(client, checkId);
        var postId = post.GetProperty("id").GetGuid();
        Assert.Equal($"/api/posts/{postId}/video", post.GetProperty("videoUrl").GetString());
        Assert.Equal($"/api/posts/{postId}/image", post.GetProperty("imageUrl").GetString());

        // The same URL from the feed, the single post and the profile: the reader is the one place it is built.
        var feed = await anonymous.GetFromJsonAsync<JsonElement>("/api/feed?tab=fresh");
        var card = feed.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == postId);
        Assert.Equal($"/api/posts/{postId}/video", card.GetProperty("videoUrl").GetString());
        Assert.Equal($"/api/posts/{postId}/video", (await anonymous.GetFromJsonAsync<JsonElement>($"/api/posts/{postId}")).GetProperty("videoUrl").GetString());

        var video = await anonymous.GetAsync($"/api/posts/{postId}/video");
        Assert.Equal(HttpStatusCode.OK, video.StatusCode);
        Assert.Equal("video/webm", video.Content.Headers.ContentType?.MediaType);
        Assert.Equal(clip.Length, video.Content.Headers.ContentLength);
        Assert.Equal(clip, await video.Content.ReadAsByteArrayAsync());
        Assert.Contains("bytes", video.Headers.AcceptRanges);
        Assert.True(video.Headers.CacheControl?.Private);
        Assert.Equal(TimeSpan.FromHours(1), video.Headers.CacheControl?.MaxAge);
        Assert.Null(video.Content.Headers.ContentDisposition);
    }

    [Fact]
    public async Task Mp4_and_mov_clips_are_served_as_video_mp4_whatever_the_file_name()
    {
        var (client, userId, _) = await _app.NewUserAsync("clip_mp4");
        var anonymous = _app.NewClient();

        foreach (var (brand, name) in new[] { ("isom", "clip.webm"), ("qt  ", "IMG_0001.MOV"), ("mp42", "x"), ("avc1", "clip.mp4") })
        {
            var (checkId, postId) = await CheckAndPostClipAsync(client, TestClips.Mp4(brand: brand), name);
            Assert.EndsWith(".mp4", await VideoPathAsync(checkId));
            var video = await anonymous.GetAsync($"/api/posts/{postId}/video");
            Assert.Equal(HttpStatusCode.OK, video.StatusCode);
            Assert.Equal("video/mp4", video.Content.Headers.ContentType?.MediaType);
        }

        Assert.Equal(4, Directory.GetFiles(Path.Combine(_app.StorageRoot, userId.ToString("N")), "*.mp4").Length);
    }

    [Fact]
    public async Task Range_request_returns_a_206_slice_so_the_player_can_seek()
    {
        var (client, _, _) = await _app.NewUserAsync("clip_range");
        var clip = TestClips.WebM(100 * 1024);
        var (_, postId) = await CheckAndPostClipAsync(client, clip);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/posts/{postId}/video");
        request.Headers.Range = new RangeHeaderValue(0, 3);
        var response = await _app.NewClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(4, response.Content.Headers.ContentLength);
        Assert.Equal(clip[..4], await response.Content.ReadAsByteArrayAsync());
        var contentRange = response.Content.Headers.ContentRange;
        Assert.NotNull(contentRange);
        Assert.Equal("bytes", contentRange.Unit);
        Assert.Equal(0, contentRange.From);
        Assert.Equal(3, contentRange.To);
        Assert.Equal(clip.Length, contentRange.Length);
        Assert.Equal("video/webm", response.Content.Headers.ContentType?.MediaType);

        // A suffix range, the shape Safari uses to find the end of the file.
        using var tail = new HttpRequestMessage(HttpMethod.Get, $"/api/posts/{postId}/video");
        tail.Headers.Range = new RangeHeaderValue(null, 10);
        var tailResponse = await _app.NewClient().SendAsync(tail);
        Assert.Equal(HttpStatusCode.PartialContent, tailResponse.StatusCode);
        Assert.Equal(clip[^10..], await tailResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task The_stylist_only_ever_gets_the_still()
    {
        var (client, _, _) = await _app.NewUserAsync("clip_still");
        var still = TestImages.Png();
        var clip = TestClips.Mp4(64 * 1024);
        var before = _app.Vision.Requests.Count;

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestClips.Form(clip, still))).StatusCode);

        var request = _app.Vision.Requests[before];
        Assert.Equal(before + 1, _app.Vision.Requests.Count);
        Assert.Equal("image/png", request.MediaType);
        Assert.Equal(still, request.ImageBytes.ToArray());
    }

    [Fact]
    public async Task Clip_with_the_wrong_bytes_is_415_and_nothing_is_stored()
    {
        var (client, userId, _) = await _app.NewUserAsync("clip_garbage");
        var before = _app.Vision.Requests.Count;

        var response = await client.PostAsync("/api/checks", TestClips.Form(TestClips.Garbage(), fileName: "real.mp4"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("That doesn't look like an MP4 or WebM clip.", (await Json(response)).GetProperty("error").GetString());
        // Refused before the model was called and before anything touched the disk or the database.
        Assert.Equal(before, _app.Vision.Requests.Count);
        Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
        using var scope = _app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Checks.AnyAsync(c => c.UserId == userId));

        // A JPEG in the clip slot is still not a clip, and the message comes in the check's language.
        var hebrew = await client.PostAsync("/api/checks", TestClips.Form(TestImages.Jpeg(), intent: "Casual"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, hebrew.StatusCode);
    }

    [Fact]
    public async Task A_bad_still_is_reported_before_the_clip_is_looked_at()
    {
        var (client, _, _) = await _app.NewUserAsync("clip_badstill");
        var response = await client.PostAsync("/api/checks", TestClips.Form(TestClips.Garbage(), TestImages.Garbage()));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("Use a JPEG, PNG or WebP photo.", (await Json(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_empty_video_field_is_a_photo_check()
    {
        var (client, userId, _) = await _app.NewUserAsync("clip_empty");
        // What a browser sends for an untouched <input type="file" name="video">: an empty part with filename="".
        var form = TestApp.CheckForm(TestImages.Jpeg());
        var empty = new ByteArrayContent([]);
        empty.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"video\"", FileName = "\"\"" };
        empty.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(empty);
        var response = await client.PostAsync("/api/checks", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var checkId = (await Json(response)).GetProperty("id").GetGuid();
        Assert.Null(await VideoPathAsync(checkId));
        Assert.Single(Directory.GetFiles(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
    }

    [Fact]
    public async Task Rejected_and_not_outfit_checks_store_neither_the_photo_nor_the_clip()
    {
        var (client, userId, _) = await _app.NewUserAsync("clip_rejected");
        try
        {
            _app.Vision.Handler = _ => Payloads.Rejected();
            var rejected = await client.PostAsync("/api/checks", TestClips.Form(TestClips.WebM()));
            Assert.Equal(HttpStatusCode.Created, rejected.StatusCode);
            Assert.Equal("rejected", (await Json(rejected)).GetProperty("status").GetString());

            _app.Vision.Handler = _ => Payloads.NotOutfit();
            var desk = await client.PostAsync("/api/checks", TestClips.Form(TestClips.Mp4()));
            Assert.Equal(HttpStatusCode.Created, desk.StatusCode);
            Assert.Equal("not_outfit", (await Json(desk)).GetProperty("status").GetString());
        }
        finally
        {
            _app.Vision.Handler = _ => Payloads.Ok();
        }

        Assert.False(Directory.Exists(Path.Combine(_app.StorageRoot, userId.ToString("N"))));
        using var scope = _app.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Checks.Where(c => c.UserId == userId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("", row.ImagePath);
            Assert.Null(row.VideoPath);
        });
    }

    [Fact]
    public async Task Deleting_the_post_removes_the_clip_and_keeps_the_still()
    {
        var (owner, userId, _) = await _app.NewUserAsync("clip_delete");
        var (fan, _, _) = await _app.NewUserAsync("clip_delete_fan");
        var (checkId, postId) = await CheckAndPostClipAsync(owner, TestClips.WebM());
        var folder = Path.Combine(_app.StorageRoot, userId.ToString("N"));
        var clipFile = Path.Combine(folder, checkId.ToString("N") + ".webm");
        Assert.True(File.Exists(clipFile));

        // Someone else's delete changes nothing, including the file.
        Assert.Equal(HttpStatusCode.NotFound, (await fan.DeleteAsync($"/api/posts/{postId}")).StatusCode);
        Assert.True(File.Exists(clipFile));

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/posts/{postId}")).StatusCode);

        Assert.False(File.Exists(clipFile));
        Assert.True(File.Exists(Path.Combine(folder, checkId.ToString("N") + ".jpg")));
        Assert.Null(await VideoPathAsync(checkId));
        Assert.Equal(HttpStatusCode.NotFound, (await fan.GetAsync($"/api/posts/{postId}/video")).StatusCode);

        // The check can be posted again, as a photo look now.
        var again = await _app.PostAsync(owner, checkId);
        Assert.False(again.TryGetProperty("videoUrl", out var url) && url.ValueKind != JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.NotFound, (await fan.GetAsync($"/api/posts/{again.GetProperty("id").GetGuid()}/video")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fan.GetAsync($"/api/posts/{again.GetProperty("id").GetGuid()}/image")).StatusCode);
    }

    [Fact]
    public async Task A_photo_look_has_no_video_url_and_no_video_route()
    {
        var (client, _, _) = await _app.NewUserAsync("clip_none");
        var checkId = await _app.CheckAsync(client);
        var post = await _app.PostAsync(client, checkId);
        var postId = post.GetProperty("id").GetGuid();

        Assert.False(post.TryGetProperty("videoUrl", out var url) && url.ValueKind != JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/posts/{postId}/video")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync($"/api/posts/{postId}/video")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _app.NewClient().GetAsync($"/api/posts/{postId}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync($"/api/posts/{Guid.NewGuid()}/video")).StatusCode);
    }

    [Fact]
    public async Task A_hidden_look_streams_its_clip_to_its_author_only()
    {
        var (owner, _, _) = await _app.NewUserAsync("clip_hidden");
        var (_, postId) = await CheckAndPostClipAsync(owner, TestClips.WebM());
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Posts.Where(p => p.Id == postId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Hidden, true));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewClient().GetAsync($"/api/posts/{postId}/video")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/posts/{postId}/video")).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_removes_the_clips_posted_or_not()
    {
        var (client, userId, _) = await _app.NewUserAsync("clip_account");
        var (postedCheck, _) = await CheckAndPostClipAsync(client, TestClips.WebM());
        var unposted = await client.PostAsync("/api/checks", TestClips.Form(TestClips.Mp4()));
        var unpostedCheck = (await Json(unposted)).GetProperty("id").GetGuid();
        var folder = Path.Combine(_app.StorageRoot, userId.ToString("N"));
        Assert.True(File.Exists(Path.Combine(folder, postedCheck.ToString("N") + ".webm")));
        Assert.True(File.Exists(Path.Combine(folder, unpostedCheck.ToString("N") + ".mp4")));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/users/me")).StatusCode);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Oversize_clip_is_413_with_the_limit_even_when_the_still_is_fine()
    {
        using var app = new TestApp { MaxVideoBytes = 1024 * 1024 };
        app.Vision.Handler = _ => Payloads.Ok();
        var (client, userId, _) = await app.NewUserAsync("clip_big");

        // Over the clip limit but within the body limit: caught on the parsed file's length.
        var response = await client.PostAsync("/api/checks", TestClips.Form(TestClips.WebM(2 * 1024 * 1024)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var message = (await Json(response)).GetProperty("error").GetString();
        Assert.Equal("That clip is too large. Try one under 1 MB or shorter than 30 seconds.", message);

        // Over the whole body limit (still + clip + overhead): refused on Content-Length before the form is read.
        var declared = await client.PostAsync("/api/checks", TestClips.Form(TestClips.WebM(8 * 1024 * 1024)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, declared.StatusCode);
        Assert.Contains("1 MB", (await Json(declared)).GetProperty("error").GetString());

        // Nothing was stored, nothing counted, and the same still with a clip under the limit goes through.
        Assert.Empty(app.Vision.Requests);
        Assert.False(Directory.Exists(Path.Combine(app.StorageRoot, userId.ToString("N"))));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/checks", TestClips.Form(TestClips.WebM(512 * 1024)))).StatusCode);

        // The still keeps its own limit and its own message.
        var bigStill = await client.PostAsync("/api/checks", TestApp.CheckForm(TestImages.Jpeg(7 * 1024 * 1024)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, bigStill.StatusCode);
        Assert.Contains("6 MB", (await Json(bigStill)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_clip_that_cannot_be_written_does_not_lose_the_check()
    {
        using var app = new ClipStoreFailsApp();
        app.Vision.Handler = _ => Payloads.Ok();
        var (client, userId, _) = await app.NewUserAsync("clip_diskfull");

        var response = await client.PostAsync("/api/checks", TestClips.Form(TestClips.WebM()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var check = await Json(response);
        Assert.Equal("ok", check.GetProperty("status").GetString());
        var checkId = check.GetProperty("id").GetGuid();
        var folder = Path.Combine(app.StorageRoot, userId.ToString("N"));
        Assert.True(File.Exists(Path.Combine(folder, checkId.ToString("N") + ".jpg")));
        Assert.Empty(Directory.GetFiles(folder, "*.webm"));

        var post = await app.PostAsync(client, checkId);
        Assert.False(post.TryGetProperty("videoUrl", out var url) && url.ValueKind != JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/posts/{post.GetProperty("id").GetGuid()}/video")).StatusCode);
    }

    /// <summary>The real disk store, except that every clip write fails the way a full disk would.</summary>
    private sealed class ClipStoreFailsApp : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IImageStore>();
                services.AddSingleton<IImageStore>(sp => new FailingClipStore(
                    new DiskImageStore(sp.GetRequiredService<IOptions<StorageOptions>>(), sp.GetRequiredService<IHostEnvironment>())));
            });
        }
    }

    private sealed class FailingClipStore(DiskImageStore inner) : IImageStore
    {
        public Task<string> SaveAsync(Guid userId, Guid checkId, ImageFormat format, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
            inner.SaveAsync(userId, checkId, format, bytes, ct);

        public Task<string> SaveVideoAsync(Guid userId, Guid checkId, VideoFormat format, Stream content, CancellationToken ct) =>
            throw new IOException("No space left on device");

        public Task<string> ReplaceVideoAsync(string relativeOld, Stream mp4, CancellationToken ct) => inner.ReplaceVideoAsync(relativeOld, mp4, ct);

        public void Delete(string relativePath) => inner.Delete(relativePath);
        public void DeleteUser(Guid userId) => inner.DeleteUser(userId);
        public bool Exists(string relativePath) => inner.Exists(relativePath);
        public Stream? OpenRead(string relativePath) => inner.OpenRead(relativePath);

        public Task<string> SaveAvatarAsync(Guid userId, ImageFormat format, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
            inner.SaveAvatarAsync(userId, format, bytes, ct);
    }
}

/// <summary>Own fixture: the metrics are global, so this class must not share a database with the other clip tests.</summary>
public class ClipMetricsTests : IClassFixture<ClipMetricsTests.ClipMetricsApp>
{
    public sealed class ClipMetricsApp : TestApp;

    private readonly ClipMetricsApp _app;

    public ClipMetricsTests(ClipMetricsApp app)
    {
        _app = app;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    [Fact]
    public async Task Videos_counts_visible_posts_whose_check_has_a_clip()
    {
        var (a, _, _) = await _app.NewUserAsync("clipmetric_a");
        var (b, _, _) = await _app.NewUserAsync("clipmetric_b");

        async Task<Guid> ClipCheckAsync(HttpClient client, byte[] clip)
        {
            var response = await client.PostAsync("/api/checks", TestClips.Form(clip));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        // Two clip looks in the feed, one clip look hidden, one clip never posted, one photo look.
        await _app.PostAsync(a, await ClipCheckAsync(a, TestClips.WebM()));
        await _app.PostAsync(b, await ClipCheckAsync(b, TestClips.Mp4()));
        var hidden = (await _app.PostAsync(a, await ClipCheckAsync(a, TestClips.WebM()))).GetProperty("id").GetGuid();
        await ClipCheckAsync(b, TestClips.WebM());
        await _app.CheckAndPostAsync(b);
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Posts.Where(p => p.Id == hidden).ExecuteUpdateAsync(s => s.SetProperty(p => p.Hidden, true));
        }

        var social = (await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/metrics/pilot")).GetProperty("social");

        Assert.Equal(3, social.GetProperty("posts").GetInt32());
        Assert.Equal(2, social.GetProperty("videos").GetInt32());
        Assert.Equal(0, social.GetProperty("pushSubscriptions").GetInt32());
    }
}
