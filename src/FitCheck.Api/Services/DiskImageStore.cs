using FitCheck.Api.Domain;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>Stores photos and clips as storage/&lt;userId&gt;/&lt;checkId&gt;.&lt;ext&gt; under a root that is outside wwwroot.</summary>
public sealed class DiskImageStore : IImageStore
{
    private readonly string _root;

    public DiskImageStore(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.Root;
        _root = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured));
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public async Task<string> SaveAsync(Guid userId, Guid checkId, ImageFormat format, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        // Both segments come from Guids we generated, so the relative path can never escape the root.
        var relative = Path.Combine(userId.ToString("N"), $"{checkId:N}.{format.Extension}");
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes.ToArray(), ct);
        return relative;
    }

    public async Task<string> SaveVideoAsync(Guid userId, Guid checkId, VideoFormat format, Stream content, CancellationToken ct)
    {
        var relative = Path.Combine(userId.ToString("N"), $"{checkId:N}.{format.Extension}");
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        try
        {
            // Copied through a 64 KB buffer: the clip goes from ASP.NET's request buffer to disk without being in memory whole.
            await using var file = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            await content.CopyToAsync(file, ct);
        }
        catch
        {
            // A half-written clip must never be served later as if it were whole.
            File.Delete(full);
            throw;
        }

        return relative;
    }

    public async Task<string> SaveAvatarAsync(Guid userId, ImageFormat format, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        var folder = Resolve(userId.ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder, "avatar.*"))
        {
            File.Delete(old);
        }

        var relative = Path.Combine(userId.ToString("N"), $"avatar.{format.Extension}");
        await File.WriteAllBytesAsync(Resolve(relative), bytes.ToArray(), ct);
        return relative;
    }

    public void Delete(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return;
        }

        var full = Resolve(relativePath);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    public void DeleteUser(Guid userId)
    {
        var folder = Resolve(userId.ToString("N"));
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    public bool Exists(string relativePath) =>
        !string.IsNullOrEmpty(relativePath) && File.Exists(Resolve(relativePath));

    public Stream? OpenRead(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return null;
        }

        var full = Resolve(relativePath);
        // FileShare.Delete lets an account or post deletion succeed on Windows while a photo is still being streamed.
        return File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 64 * 1024, useAsync: true) : null;
    }

    private string Resolve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        // Defence in depth: nothing user-controlled reaches here, but a stored path must still stay inside the root.
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && full != _root)
        {
            throw new InvalidOperationException("Image path escapes the storage root.");
        }

        return full;
    }
}
