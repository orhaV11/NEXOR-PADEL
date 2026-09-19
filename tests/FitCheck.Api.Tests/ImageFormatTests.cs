using FitCheck.Api.Domain;
using FitCheck.Api.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Tests;

public class ImageFormatTests
{
    [Fact]
    public void Detects_jpeg() => Assert.Equal(ImageFormat.Jpeg, ImageFormat.Detect(TestImages.Jpeg()));

    [Fact]
    public void Detects_png() => Assert.Equal(ImageFormat.Png, ImageFormat.Detect(TestImages.Png()));

    [Fact]
    public void Detects_webp() => Assert.Equal(ImageFormat.WebP, ImageFormat.Detect(TestImages.WebP()));

    [Fact]
    public void Garbage_is_null() => Assert.Null(ImageFormat.Detect(TestImages.Garbage()));

    [Fact]
    public void Empty_is_null() => Assert.Null(ImageFormat.Detect([]));

    [Fact]
    public void Riff_without_webp_is_null()
    {
        var bytes = TestImages.WebP();
        bytes[8] = (byte)'A'; // RIFF container that is not WebP (e.g. WAV)
        Assert.Null(ImageFormat.Detect(bytes));
    }

    [Fact]
    public void Truncated_signature_is_null() => Assert.Null(ImageFormat.Detect([0xFF, 0xD8]));

    [Fact]
    public void Clips_are_not_images()
    {
        Assert.Null(ImageFormat.Detect(TestClips.WebM()));
        Assert.Null(ImageFormat.Detect(TestClips.Mp4()));
    }
}

public class VideoFormatTests
{
    [Theory]
    [InlineData("isom")]
    [InlineData("mp42")]
    [InlineData("avc1")]
    [InlineData("qt  ")]
    [InlineData("3gp5")]
    public void Detects_iso_base_media_by_ftyp_whatever_the_brand(string brand)
    {
        var format = VideoFormat.Detect(TestClips.Mp4(brand: brand));

        Assert.Equal(VideoFormat.Mp4, format);
        Assert.Equal("mp4", format!.Extension);
        Assert.Equal("video/mp4", format.MediaType);
    }

    [Fact]
    public void Detects_webm_by_the_ebml_header()
    {
        var format = VideoFormat.Detect(TestClips.WebM());

        Assert.Equal(VideoFormat.WebM, format);
        Assert.Equal("webm", format!.Extension);
        Assert.Equal("video/webm", format.MediaType);
    }

    [Fact]
    public void Sixteen_bytes_are_enough()
    {
        Assert.Equal(VideoFormat.Mp4, VideoFormat.Detect(TestClips.Mp4().AsSpan(0, VideoFormat.SniffLength)));
        Assert.Equal(VideoFormat.WebM, VideoFormat.Detect(TestClips.WebM().AsSpan(0, VideoFormat.SniffLength)));
    }

    [Fact]
    public void Images_are_not_clips()
    {
        Assert.Null(VideoFormat.Detect(TestImages.Jpeg()));
        Assert.Null(VideoFormat.Detect(TestImages.Png()));
        Assert.Null(VideoFormat.Detect(TestImages.WebP()));
    }

    [Fact]
    public void Garbage_empty_and_truncated_are_null()
    {
        Assert.Null(VideoFormat.Detect(TestClips.Garbage()));
        Assert.Null(VideoFormat.Detect(TestImages.Garbage()));
        Assert.Null(VideoFormat.Detect([]));
        Assert.Null(VideoFormat.Detect([0x1A, 0x45, 0xDF]));
        Assert.Null(VideoFormat.Detect([0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y']));
    }

    [Fact]
    public void Ftyp_must_sit_at_offset_four()
    {
        var bytes = TestClips.Mp4();
        bytes[4] = (byte)'F';
        Assert.Null(VideoFormat.Detect(bytes));
        Assert.Null(VideoFormat.Detect("ftypisom"u8));
    }

    [Fact]
    public void Stored_extension_maps_back_to_the_media_type()
    {
        Assert.Equal(VideoFormat.WebM, VideoFormat.FromPath("u/c.webm"));
        Assert.Equal(VideoFormat.WebM, VideoFormat.FromPath("u/c.WEBM"));
        Assert.Equal(VideoFormat.Mp4, VideoFormat.FromPath("u/c.mp4"));
    }
}

public class DiskImageStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fitcheck-store-" + Guid.NewGuid().ToString("N"));
    private readonly DiskImageStore _store;

    public DiskImageStoreTests()
    {
        var env = new FakeEnvironment(Path.GetTempPath());
        _store = new DiskImageStore(Options.Create(new StorageOptions { Root = _root }), env);
    }

    [Fact]
    public async Task Saves_under_user_and_check_ids_with_detected_extension()
    {
        var user = Guid.NewGuid();
        var check = Guid.NewGuid();

        var relative = await _store.SaveAsync(user, check, ImageFormat.WebP, TestImages.WebP(), CancellationToken.None);

        Assert.Equal(Path.Combine(user.ToString("N"), check.ToString("N") + ".webp"), relative);
        Assert.True(_store.Exists(relative));
        Assert.True(File.Exists(Path.Combine(_root, relative)));
    }

    [Fact]
    public async Task Delete_removes_one_file_and_tolerates_missing()
    {
        var user = Guid.NewGuid();
        var relative = await _store.SaveAsync(user, Guid.NewGuid(), ImageFormat.Jpeg, TestImages.Jpeg(), CancellationToken.None);

        _store.Delete(relative);
        _store.Delete(relative);
        _store.Delete("");

        Assert.False(_store.Exists(relative));
    }

    [Fact]
    public async Task DeleteUser_removes_every_file_and_the_folder()
    {
        var user = Guid.NewGuid();
        var a = await _store.SaveAsync(user, Guid.NewGuid(), ImageFormat.Jpeg, TestImages.Jpeg(), CancellationToken.None);
        var b = await _store.SaveAsync(user, Guid.NewGuid(), ImageFormat.Png, TestImages.Png(), CancellationToken.None);

        _store.DeleteUser(user);

        Assert.False(_store.Exists(a));
        Assert.False(_store.Exists(b));
        Assert.False(Directory.Exists(Path.Combine(_root, user.ToString("N"))));
    }

    [Fact]
    public void Paths_outside_the_root_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => _store.Exists("../../etc/passwd"));
        Assert.Throws<InvalidOperationException>(() => _store.Delete("../outside.jpg"));
    }

    [Fact]
    public async Task Streams_a_clip_next_to_the_still_and_reads_it_back_seekable()
    {
        var user = Guid.NewGuid();
        var check = Guid.NewGuid();
        var clip = TestClips.WebM(300 * 1024);
        var still = await _store.SaveAsync(user, check, ImageFormat.Jpeg, TestImages.Jpeg(), CancellationToken.None);

        string relative;
        await using (var source = new MemoryStream(clip))
        {
            relative = await _store.SaveVideoAsync(user, check, VideoFormat.WebM, source, CancellationToken.None);
        }

        Assert.Equal(Path.Combine(user.ToString("N"), check.ToString("N") + ".webm"), relative);
        Assert.True(_store.Exists(relative));
        Assert.True(_store.Exists(still));
        Assert.Equal(clip, await File.ReadAllBytesAsync(Path.Combine(_root, relative)));

        await using (var opened = _store.OpenRead(relative)!)
        {
            Assert.True(opened.CanSeek);
            Assert.Equal(clip.Length, opened.Length);
        }

        _store.Delete(relative);
        Assert.False(_store.Exists(relative));
        Assert.True(_store.Exists(still));
    }

    [Fact]
    public async Task A_clip_that_fails_half_way_leaves_no_file()
    {
        var user = Guid.NewGuid();
        var check = Guid.NewGuid();
        await using var source = new FailAfterStream(TestClips.Mp4(200 * 1024), failAt: 100 * 1024);

        await Assert.ThrowsAsync<IOException>(() => _store.SaveVideoAsync(user, check, VideoFormat.Mp4, source, CancellationToken.None));

        Assert.False(_store.Exists(Path.Combine(user.ToString("N"), check.ToString("N") + ".mp4")));
    }

    /// <summary>A request body that dies part-way, as a phone leaving the tunnel does.</summary>
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
                throw new IOException("connection reset");
            }

            var n = Math.Min(count, Math.Min(bytes.Length - _position, failAt - _position));
            Array.Copy(bytes, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }

    [Fact]
    public void Relative_root_resolves_against_content_root()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "fitcheck-content-" + Guid.NewGuid().ToString("N"));
        var store = new DiskImageStore(Options.Create(new StorageOptions { Root = "storage" }), new FakeEnvironment(contentRoot));

        Assert.Equal(Path.GetFullPath(Path.Combine(contentRoot, "storage")), store.Root);
        Directory.Delete(contentRoot, recursive: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "FitCheck.Api.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
