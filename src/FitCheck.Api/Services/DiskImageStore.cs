using FitCheck.Api.Domain;
using FitCheck.Api.Services.Security;
using Microsoft.Extensions.Options;

namespace FitCheck.Api.Services;

/// <summary>
/// Stores photos and clips as storage/&lt;userId&gt;/&lt;checkId&gt;.&lt;ext&gt; under a root that is outside wwwroot. Nothing
/// is written with its metadata (Round 13): a photo goes through <see cref="ImageMetadata.Strip"/> before it touches the
/// disk, an MP4 clip has its metadata boxes blanked in place once it is on it (<see cref="VideoMetadata.BlankMp4"/>), and
/// the transcoder's MP4 gets the same treatment before it replaces the original. The endpoints strip a photo again before
/// it goes to the model; the store strips whatever it is handed, so no caller can forget.
/// </summary>
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
        await File.WriteAllBytesAsync(full, ImageMetadata.Strip(bytes.ToArray(), format), ct);
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
            await using (var file = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await content.CopyToAsync(file, ct);
            }

            // On disk and closed: the location, the device and the date the phone wrote into the container are blanked in place.
            if (format == VideoFormat.Mp4)
            {
                VideoMetadata.BlankMp4(full);
            }
        }
        catch
        {
            // A half-written clip must never be served later as if it were whole.
            File.Delete(full);
            throw;
        }

        return relative;
    }

    public async Task<string> ReplaceVideoAsync(string relativeOld, Stream mp4, CancellationToken ct)
    {
        var oldFull = Resolve(relativeOld);
        if (!File.Exists(oldFull))
        {
            throw new FileNotFoundException("The clip to replace is no longer in the store.", relativeOld);
        }

        var relative = Path.ChangeExtension(relativeOld, ".mp4");
        var full = Resolve(relative);
        // Written next to the target, then renamed over it: the rename is atomic on the same file system, so the path either
        // still holds the old clip or already holds the whole new one, never a partial file.
        var part = full + ".part";
        try
        {
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await mp4.CopyToAsync(file, ct);
                await file.FlushAsync(ct);
            }

            // ffmpeg copies the source's global tags into its output; they are blanked before the file takes the clip's place.
            VideoMetadata.BlankMp4(part);
            File.Move(part, full, overwrite: true);
        }
        catch
        {
            File.Delete(part);
            throw;
        }

        // The old file goes only now, and only when it is another file: an MP4 replaced under its own name is already gone.
        if (!string.Equals(oldFull, full, StringComparison.Ordinal) && File.Exists(oldFull))
        {
            File.Delete(oldFull);
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
        await File.WriteAllBytesAsync(Resolve(relative), ImageMetadata.Strip(bytes.ToArray(), format), ct);
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
