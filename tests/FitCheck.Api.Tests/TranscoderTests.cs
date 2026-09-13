using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FitCheck.Api.Data;
using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace FitCheck.Api.Tests;

/// <summary>
/// The ffmpeg on this machine, when there is one: the worker needs real clips to decode, so the tests make them here rather
/// than ship binaries. Without ffmpeg on PATH every ffmpeg-backed test in this file returns early with a line in the output
/// (xUnit 2 has no runtime skip), so CI without it stays green while a box with it runs the real thing.
/// </summary>
public static class Ffmpeg
{
    private static readonly Lazy<bool> Found = new(() => Run("ffmpeg", "-version")?.ExitCode == 0 && Run("ffprobe", "-version")?.ExitCode == 0);

    public static bool Present => Found.Value;

    /// <summary>Runs a binary with the arguments and waits (60 s at most). Null when the binary is not there.</summary>
    public static (int ExitCode, string Stdout, string Stderr)? Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                return (-1, "", "timeout");
            }

            return (process.ExitCode, stdout.Result, stderr.Result);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>A one-second 64x64 VP8 WebM without audio: what an Android phone's MediaRecorder produces, in miniature.</summary>
    public static byte[] WebM() => Make("webm", "-c:v", "libvpx");

    /// <summary>The same second as H.264 in an MP4 with faststart: what the transcoder would make, so nothing needs doing.</summary>
    public static byte[] Mp4() => Make("mp4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart");

    private static byte[] Make(string extension, params string[] codec)
    {
        var path = Path.Combine(Path.GetTempPath(), "fitcheck-clips", $"{Guid.NewGuid():N}.{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var result = Run("ffmpeg", ["-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=duration=1:size=64x64:rate=10", .. codec, "-an", path]);
            if (result is not { ExitCode: 0 })
            {
                throw new InvalidOperationException($"ffmpeg could not make the test clip: {result?.Stderr}");
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The codec of the first video stream as ffprobe reads it from the bytes ("h264", "vp8"...), or null when it cannot read them.</summary>
    public static string? VideoCodec(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "fitcheck-clips", $"{Guid.NewGuid():N}.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, bytes);
            var result = Run("ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name", "-of", "csv=p=0", path);
            var line = result?.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return result is { ExitCode: 0 } && !string.IsNullOrEmpty(line) ? line.TrimEnd(',') : null;
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class TranscoderTests : IClassFixture<TranscoderTests.TranscodeApp>
{
    /// <summary>The real app with Storage:Transcode on; ffmpeg is whatever PATH has.</summary>
    public sealed class TranscodeApp : TestApp
    {
        public TranscodeApp()
        {
            Transcode = true;
        }
    }

    /// <summary>A second app over the first one's database and media folder, with transcoding on: a restart after ffmpeg was installed.</summary>
    private sealed class RestartedApp : TestApp
    {
        private readonly TestApp _source;

        public RestartedApp(TestApp source)
        {
            _source = source;
            Transcode = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Default", _source.ConnectionString);
            builder.UseSetting("Storage:Root", _source.StorageRoot);
        }
    }

    private readonly TranscodeApp _app;
    private readonly ITestOutputHelper _output;

    public TranscoderTests(TranscodeApp app, ITestOutputHelper output)
    {
        _app = app;
        _output = output;
        _app.Vision.Handler = _ => Payloads.Ok();
    }

    /// <summary>True, with a line in the output, when this machine has no ffmpeg: the test then returns without asserting.</summary>
    private bool NoFfmpeg()
    {
        if (Ffmpeg.Present)
        {
            return false;
        }

        _output.WriteLine("ffmpeg is not on PATH: transcoding was not exercised.");
        return true;
    }

    private static async Task<(Guid CheckId, Guid PostId)> CheckAndPostAsync(TestApp app, HttpClient client, byte[] clip, string fileName = "look.webm")
    {
        var response = await client.PostAsync("/api/checks", TestClips.Form(clip, fileName: fileName));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var checkId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var postId = (await app.PostAsync(client, checkId)).GetProperty("id").GetGuid();
        return (checkId, postId);
    }

    private static async Task<string?> VideoPathAsync(TestApp app, Guid checkId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Checks.Where(c => c.Id == checkId).Select(c => c.VideoPath).SingleAsync();
    }

    private static string ClipFile(TestApp app, Guid userId, Guid checkId, string extension) =>
        Path.Combine(app.StorageRoot, userId.ToString("N"), checkId.ToString("N") + extension);

    private static bool IsMp4(byte[] bytes) => bytes.Length >= 8 && bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8);

    /// <summary>Polls the post's clip until the worker has swapped it for an MP4 (an ftyp box served as video/mp4), 30 s at most.</summary>
    private static async Task<byte[]> WaitForMp4Async(HttpClient client, Guid postId, int timeoutMs = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            using var response = await client.GetAsync($"/api/posts/{postId}/video");
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType == "video/mp4" && IsMp4(bytes))
            {
                return bytes;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"The clip of post {postId} was still {(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType} after {timeoutMs} ms.");
            }

            await Task.Delay(250);
        }
    }

    private static async Task WaitForFileAsync(string path, int timeoutMs = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!File.Exists(path))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"{path} did not appear within {timeoutMs} ms.");
            }

            await Task.Delay(250);
        }
    }

    [Fact]
    public async Task A_webm_clip_becomes_an_h264_mp4_served_through_the_post()
    {
        if (NoFfmpeg())
        {
            return;
        }

        var config = await _app.NewClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.True(config.GetProperty("transcoding").GetBoolean());
        Assert.True(_app.Services.GetRequiredService<Transcoder>().Available);

        var (client, userId, _) = await _app.NewUserAsync("tc_webm");
        var webm = Ffmpeg.WebM();
        Assert.Equal("vp8", Ffmpeg.VideoCodec(webm));
        var (checkId, postId) = await CheckAndPostAsync(_app, client, webm);

        var served = await WaitForMp4Async(_app.NewClient(), postId);

        Assert.Equal("h264", Ffmpeg.VideoCodec(served));
        Assert.NotEqual(webm, served);
        // The MP4 took the clip's name next to the still; the WebM is gone, and so is the temporary file it was written through.
        var mp4 = ClipFile(_app, userId, checkId, ".mp4");
        Assert.True(File.Exists(mp4));
        Assert.Equal(served, await File.ReadAllBytesAsync(mp4));
        Assert.False(File.Exists(ClipFile(_app, userId, checkId, ".webm")));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(mp4)!, "*.part"));
        Assert.True(File.Exists(ClipFile(_app, userId, checkId, ".jpg")));
        Assert.Equal(Path.Combine(userId.ToString("N"), checkId.ToString("N") + ".mp4"), await VideoPathAsync(_app, checkId));
        Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "orevosh-transcode", checkId.ToString("N"))));

        // The look's URL did not change, and Range requests work on the new file as they did on the old.
        var post = await _app.NewClient().GetFromJsonAsync<JsonElement>($"/api/posts/{postId}");
        Assert.Equal($"/api/posts/{postId}/video", post.GetProperty("videoUrl").GetString());
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/posts/{postId}/video");
        request.Headers.Range = new RangeHeaderValue(0, 7);
        var slice = await _app.NewClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, slice.StatusCode);
        Assert.Equal("video/mp4", slice.Content.Headers.ContentType?.MediaType);
        Assert.Equal(served[..8], await slice.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task An_h264_mp4_is_left_exactly_as_uploaded()
    {
        if (NoFfmpeg())
        {
            return;
        }

        var (client, userId, _) = await _app.NewUserAsync("tc_mp4");
        var mp4 = Ffmpeg.Mp4();
        Assert.Equal("h264", Ffmpeg.VideoCodec(mp4));
        var (checkId, postId) = await CheckAndPostAsync(_app, client, mp4, "IMG_0001.mp4");
        var file = new FileInfo(ClipFile(_app, userId, checkId, ".mp4"));
        Assert.True(file.Exists);
        var (length, written) = (file.Length, file.LastWriteTimeUtc);

        // The worker takes jobs in order, one at a time: once a clip queued after this one is done, this one has had its turn.
        var (_, marker) = await CheckAndPostAsync(_app, client, Ffmpeg.WebM());
        await WaitForMp4Async(_app.NewClient(), marker);

        file.Refresh();
        Assert.Equal(length, file.Length);
        Assert.Equal(written, file.LastWriteTimeUtc);
        Assert.Equal(mp4, await File.ReadAllBytesAsync(file.FullName));
        var served = await _app.NewClient().GetAsync($"/api/posts/{postId}/video");
        Assert.Equal("video/mp4", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(mp4, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_clip_ffmpeg_cannot_read_keeps_serving_as_uploaded()
    {
        if (NoFfmpeg())
        {
            return;
        }

        var (client, userId, _) = await _app.NewUserAsync("tc_broken");
        // A WebM by its first bytes and nothing else: the upload check lets it through, ffmpeg cannot decode it.
        var broken = TestClips.WebM(64 * 1024);
        var (checkId, postId) = await CheckAndPostAsync(_app, client, broken);
        var (_, marker) = await CheckAndPostAsync(_app, client, Ffmpeg.WebM());
        await WaitForMp4Async(_app.NewClient(), marker);

        Assert.True(File.Exists(ClipFile(_app, userId, checkId, ".webm")));
        Assert.False(File.Exists(ClipFile(_app, userId, checkId, ".mp4")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_app.StorageRoot, userId.ToString("N")), "*.part"));
        Assert.EndsWith(".webm", await VideoPathAsync(_app, checkId));
        Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "orevosh-transcode", checkId.ToString("N"))));
        var served = await _app.NewClient().GetAsync($"/api/posts/{postId}/video");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("video/webm", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(broken, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task With_transcode_off_nothing_runs_and_config_says_so()
    {
        // Needs no ffmpeg: nothing is probed, nothing is started.
        using var app = new TestApp();
        app.Vision.Handler = _ => Payloads.Ok();
        Assert.False((await app.NewClient().GetFromJsonAsync<JsonElement>("/api/config")).GetProperty("transcoding").GetBoolean());
        Assert.False(app.Services.GetRequiredService<Transcoder>().Available);

        var (client, userId, _) = await app.NewUserAsync("tc_off");
        var webm = Ffmpeg.Present ? Ffmpeg.WebM() : TestClips.WebM();
        var (checkId, postId) = await CheckAndPostAsync(app, client, webm);
        await Task.Delay(1500);

        Assert.True(File.Exists(ClipFile(app, userId, checkId, ".webm")));
        Assert.False(File.Exists(ClipFile(app, userId, checkId, ".mp4")));
        Assert.EndsWith(".webm", await VideoPathAsync(app, checkId));
        var served = await app.NewClient().GetAsync($"/api/posts/{postId}/video");
        Assert.Equal("video/webm", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(webm, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Clips_stored_before_ffmpeg_arrived_are_transcoded_at_the_next_start()
    {
        if (NoFfmpeg())
        {
            return;
        }

        using var before = new TestApp();
        before.Vision.Handler = _ => Payloads.Ok();
        var (client, userId, _) = await before.NewUserAsync("tc_sweep");
        var (postedCheck, postId) = await CheckAndPostAsync(before, client, Ffmpeg.WebM());
        var unposted = await client.PostAsync("/api/checks", TestClips.Form(Ffmpeg.WebM()));
        Assert.Equal(HttpStatusCode.Created, unposted.StatusCode);
        var unpostedCheck = (await unposted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var untouched = await before.CheckAsync(client);
        Assert.True(File.Exists(ClipFile(before, userId, postedCheck, ".webm")));
        Assert.True(File.Exists(ClipFile(before, userId, unpostedCheck, ".webm")));

        // The same database and media folder opened by an app with transcoding on: the sweep at start finds both WebMs.
        using var after = new RestartedApp(before);
        var served = await WaitForMp4Async(after.NewClient(), postId);
        await WaitForFileAsync(ClipFile(before, userId, unpostedCheck, ".mp4"));

        Assert.Equal("h264", Ffmpeg.VideoCodec(served));
        Assert.False(File.Exists(ClipFile(before, userId, postedCheck, ".webm")));
        Assert.EndsWith(".mp4", await VideoPathAsync(after, postedCheck));
        Assert.Equal("h264", Ffmpeg.VideoCodec(await File.ReadAllBytesAsync(ClipFile(before, userId, unpostedCheck, ".mp4"))));
        // The row follows the file for the unposted clip too, and a photo check is not touched.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!(await VideoPathAsync(after, unpostedCheck))!.EndsWith(".mp4") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }

        Assert.EndsWith(".mp4", await VideoPathAsync(after, unpostedCheck));
        Assert.False(File.Exists(ClipFile(before, userId, unpostedCheck, ".webm")));
        Assert.Null(await VideoPathAsync(after, untouched));
    }
}

/// <summary>The store's side of the swap: the MP4 takes the clip's name, and the old file goes only once the new one is whole.</summary>
public class TranscoderStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fitcheck-replace-" + Guid.NewGuid().ToString("N"));
    private readonly DiskImageStore _store;

    public TranscoderStoreTests()
    {
        _store = new DiskImageStore(Options.Create(new StorageOptions { Root = _root }), new TempEnvironment());
    }

    private async Task<string> SaveClipAsync(Guid user, Guid check, VideoFormat format, byte[] bytes)
    {
        await using var source = new MemoryStream(bytes);
        return await _store.SaveVideoAsync(user, check, format, source, CancellationToken.None);
    }

    [Fact]
    public async Task The_mp4_takes_the_clips_name_and_the_webm_goes()
    {
        var user = Guid.NewGuid();
        var check = Guid.NewGuid();
        var still = await _store.SaveAsync(user, check, ImageFormat.Jpeg, TestImages.Jpeg(), CancellationToken.None);
        var webm = await SaveClipAsync(user, check, VideoFormat.WebM, TestClips.WebM());
        var bytes = TestClips.Mp4(50 * 1024);

        string mp4;
        await using (var source = new MemoryStream(bytes))
        {
            mp4 = await _store.ReplaceVideoAsync(webm, source, CancellationToken.None);
        }

        Assert.Equal(Path.Combine(user.ToString("N"), check.ToString("N") + ".mp4"), mp4);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, mp4)));
        Assert.False(_store.Exists(webm));
        Assert.True(_store.Exists(still));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, user.ToString("N"))).Length);
    }

    [Fact]
    public async Task An_mp4_is_replaced_under_the_same_name()
    {
        var user = Guid.NewGuid();
        var check = Guid.NewGuid();
        var old = await SaveClipAsync(user, check, VideoFormat.Mp4, TestClips.Mp4(20 * 1024, "hev1"));
        var bytes = TestClips.Mp4(30 * 1024, "avc1");

        string replaced;
        await using (var source = new MemoryStream(bytes))
        {
            replaced = await _store.ReplaceVideoAsync(old, source, CancellationToken.None);
        }

        Assert.Equal(old, replaced);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, replaced)));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, user.ToString("N"))));
    }

    [Fact]
    public async Task A_write_that_fails_half_way_keeps_the_old_clip_and_leaves_nothing_else()
    {
        var user = Guid.NewGuid();
        var check = Guid.NewGuid();
        var original = TestClips.WebM(40 * 1024);
        var webm = await SaveClipAsync(user, check, VideoFormat.WebM, original);
        await using var source = new FailAfterStream(TestClips.Mp4(200 * 1024), failAt: 100 * 1024);

        await Assert.ThrowsAsync<IOException>(() => _store.ReplaceVideoAsync(webm, source, CancellationToken.None));

        Assert.True(_store.Exists(webm));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(_root, webm)));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, user.ToString("N"))));
    }

    [Fact]
    public async Task A_clip_that_is_already_gone_is_not_replaced()
    {
        var user = Guid.NewGuid();
        var gone = Path.Combine(user.ToString("N"), Guid.NewGuid().ToString("N") + ".webm");
        await using var source = new MemoryStream(TestClips.Mp4());

        await Assert.ThrowsAsync<FileNotFoundException>(() => _store.ReplaceVideoAsync(gone, source, CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(_root, user.ToString("N"))));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A source that dies part-way, as ffmpeg's output would if the disk filled.</summary>
    private sealed class FailAfterStream(byte[] bytes, int failAt) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= failAt)
            {
                throw new IOException("No space left on device");
            }

            var n = Math.Min(count, Math.Min(bytes.Length - _position, failAt - _position));
            Array.Copy(bytes, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }

    private sealed class TempEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "FitCheck.Api.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
