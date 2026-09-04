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
