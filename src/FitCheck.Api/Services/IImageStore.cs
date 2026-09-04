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
/// Private photo storage. Phase 1 writes to local disk; the interface exists so blob storage can replace it
/// without touching the endpoints. Paths returned are relative to the store's root and are never web-served.
/// </summary>
public interface IImageStore
{
    Task<string> SaveAsync(Guid userId, Guid checkId, ImageFormat format, ReadOnlyMemory<byte> bytes, CancellationToken ct);

    /// <summary>Removes one photo. Missing files are not an error.</summary>
    void Delete(string relativePath);

    /// <summary>Removes every photo a user has, including the folder.</summary>
    void DeleteUser(Guid userId);

    /// <summary>True when a photo is still on disk. Used by tests and the delete endpoint, never by the client.</summary>
    bool Exists(string relativePath);

    /// <summary>Opens a photo for serving. Only posts call this: a photo without a post is never read back out.</summary>
    Stream? OpenRead(string relativePath);
}
