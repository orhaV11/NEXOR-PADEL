namespace FitCheck.Api.Services;

/// <summary>Detected from the first bytes of the upload, never from the client's content type.</summary>
public sealed record ImageFormat(string Extension, string MediaType)
{
    public static readonly ImageFormat Jpeg = new("jpg", "image/jpeg");
    public static readonly ImageFormat Png = new("png", "image/png");
    public static readonly ImageFormat WebP = new("webp", "image/webp");

    /// <summary>Returns null for anything that is not a JPEG, PNG or WebP.</summary>
    public static ImageFormat? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return Jpeg;
        }

        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length >= pngSignature.Length && bytes[..pngSignature.Length].SequenceEqual(pngSignature))
        {
            return Png;
        }

        // RIFF....WEBP: bytes 4-7 hold the chunk size and vary per file.
        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return WebP;
        }

        return null;
    }
}

/// <summary>
/// A look clip's container, detected from its first bytes. MOV and 3GP are ISO base media files like MP4 and are stored
/// and served as MP4: same container, and the browsers that play one play the other.
/// </summary>
public sealed record VideoFormat(string Extension, string MediaType)
{
    public static readonly VideoFormat Mp4 = new("mp4", "video/mp4");
    public static readonly VideoFormat WebM = new("webm", "video/webm");

    /// <summary>Bytes needed from the head of an upload for <see cref="Detect"/>.</summary>
    public const int SniffLength = 16;

    /// <summary>Returns null for anything that is not an ISO base media file (MP4, MOV, 3GP) or a Matroska/WebM file.</summary>
    public static VideoFormat? Detect(ReadOnlySpan<byte> bytes)
    {
        // ISO BMFF: a 4-byte box size, then "ftyp", then the brand (isom, mp42, avc1, "qt  " ...), which varies per encoder.
        if (bytes.Length >= 8 && bytes[4..8].SequenceEqual("ftyp"u8))
        {
            return Mp4;
        }

        // Every Matroska file, WebM included, opens with the EBML header id.
        ReadOnlySpan<byte> ebml = [0x1A, 0x45, 0xDF, 0xA3];
        if (bytes.Length >= ebml.Length && bytes[..ebml.Length].SequenceEqual(ebml))
        {
            return WebM;
        }

        return null;
    }

    /// <summary>The format a stored clip was saved as, read back from the extension <see cref="Detect"/> gave it.</summary>
    public static VideoFormat FromPath(string path) =>
        Path.GetExtension(path).Equals(".webm", StringComparison.OrdinalIgnoreCase) ? WebM : Mp4;
}

/// <summary>
/// Private photo storage. Phase 1 writes to local disk; the interface exists so blob storage can replace it
/// without touching the endpoints. Paths returned are relative to the store's root and are never web-served.
/// Look clips share the store: &lt;userId&gt;/&lt;checkId&gt;.&lt;ext&gt; next to the still they came from.
/// </summary>
public interface IImageStore
{
    Task<string> SaveAsync(Guid userId, Guid checkId, ImageFormat format, ReadOnlyMemory<byte> bytes, CancellationToken ct);

    /// <summary>
    /// Streams a look clip to &lt;userId&gt;/&lt;checkId&gt;.&lt;ext&gt; without holding it in memory (a clip is tens of megabytes).
    /// Returns the relative path. Nothing is left behind when the copy fails part-way.
    /// </summary>
    Task<string> SaveVideoAsync(Guid userId, Guid checkId, VideoFormat format, Stream content, CancellationToken ct);

    /// <summary>
    /// Swaps a stored clip for its transcoded MP4: &lt;userId&gt;/&lt;checkId&gt;.mp4 is written whole (through a temporary file next
    /// to it) before the old file goes, so a reader never finds a half clip and a failure leaves the old one in place. An
    /// old clip that is an MP4 already is replaced under its own name. Returns the new relative path. A clip that is no
    /// longer there (the look was deleted meanwhile) is not replaced: <see cref="FileNotFoundException"/>, nothing written.
    /// </summary>
    Task<string> ReplaceVideoAsync(string relativeOld, Stream mp4, CancellationToken ct);

    /// <summary>Removes one photo or clip. Missing files are not an error.</summary>
    void Delete(string relativePath);

    /// <summary>Removes every photo and clip a user has, including the folder.</summary>
    void DeleteUser(Guid userId);

    /// <summary>True when a photo or clip is still on disk. Used by tests and the delete endpoints, never by the client.</summary>
    bool Exists(string relativePath);

    /// <summary>
    /// Opens a photo or clip for serving. Only posts and avatars call this: a file without a post is never read back out.
    /// The stream is seekable, so a clip can answer Range requests.
    /// </summary>
    Stream? OpenRead(string relativePath);

    /// <summary>Writes &lt;userId&gt;/avatar.&lt;ext&gt;, replacing any earlier avatar whatever its extension. Returns the relative path.</summary>
    Task<string> SaveAvatarAsync(Guid userId, ImageFormat format, ReadOnlyMemory<byte> bytes, CancellationToken ct);
}
