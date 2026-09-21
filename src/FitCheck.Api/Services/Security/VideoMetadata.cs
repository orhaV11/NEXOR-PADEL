using System.Buffers.Binary;
using System.Text;

namespace FitCheck.Api.Services.Security;

/// <summary>
/// Blanks the metadata boxes of a stored MP4 (MOV, 3GP: the same ISO base media file) in place (Round 13). A clip from
/// the phone's library carries the same things a photo's Exif does: the GPS position (<c>udta/©xyz</c>,
/// <c>com.apple.quicktime.location.ISO6709</c> in a <c>meta</c> box), the device, the date, and sometimes a title. None
/// of it is the look. The file is walked box by box and every <c>udta</c>, <c>meta</c> (under <c>moov</c> and under each
/// <c>trak</c>) and top-level <c>uuid</c> box (XMP rides there) is turned into a <c>free</c> box of the same size with a
/// zeroed payload: the file keeps its length and every chunk offset in <c>stco</c>/<c>co64</c> stays right, so nothing
/// is re-muxed and nothing can be broken by the strip. Players skip <c>free</c> boxes by definition.
/// <para>
/// Nothing is decoded and the whole file is never read: the walk reads box headers and writes zeros. A file the walker
/// cannot follow (the suite's fixtures are a box header over noise) is left as it is from that point. WebM is not
/// touched: a browser's MediaRecorder writes no location, and the library clips phones keep are MP4/MOV.
/// </para>
/// </summary>
public static class VideoMetadata
{
    private static readonly byte[] FreeType = "free"u8.ToArray();

    /// <summary>Blanks the metadata boxes of the file at <paramref name="path"/>. Returns how many boxes were blanked.</summary>
    public static int BlankMp4(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var blanked = 0;
        var length = file.Length;
        foreach (var (offset, size, headerSize, type) in Boxes(file, 0, length))
        {
            switch (type)
            {
                case "moov":
                    blanked += BlankChildren(file, offset + headerSize, offset + size, walkTracks: true);
                    break;
                case "uuid":
                case "meta":
                case "udta":
                    Blank(file, offset, size, headerSize);
                    blanked++;
                    break;
            }
        }

        return blanked;
    }

    private static int BlankChildren(FileStream file, long start, long end, bool walkTracks)
    {
        var blanked = 0;
        foreach (var (offset, size, headerSize, type) in Boxes(file, start, end))
        {
            switch (type)
            {
                case "udta":
                case "meta":
                case "uuid":
                    Blank(file, offset, size, headerSize);
                    blanked++;
                    break;
                case "trak" when walkTracks:
                    blanked += BlankChildren(file, offset + headerSize, offset + size, walkTracks: false);
                    break;
            }
        }

        return blanked;
    }

    /// <summary>The box becomes <c>free</c> and its payload zeros; the size field is untouched, so every offset after it holds.</summary>
    private static void Blank(FileStream file, long offset, long size, int headerSize)
    {
        file.Position = offset + 4;
        file.Write(FreeType, 0, 4);
        var remaining = size - headerSize;
        file.Position = offset + headerSize;
        var zeros = new byte[Math.Min(64 * 1024, Math.Max(remaining, 1))];
        while (remaining > 0)
        {
            var slice = (int)Math.Min(zeros.Length, remaining);
            file.Write(zeros, 0, slice);
            remaining -= slice;
        }
    }

    /// <summary>The boxes between two offsets: (offset, size, header size, type). Stops at the first header it cannot trust.</summary>
    private static IEnumerable<(long Offset, long Size, int HeaderSize, string Type)> Boxes(FileStream file, long start, long end)
    {
        var header = new byte[16];
        var offset = start;
        while (offset + 8 <= end)
        {
            file.Position = offset;
            if (file.Read(header, 0, 8) != 8)
            {
                yield break;
            }

            long size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            var type = Encoding.Latin1.GetString(header, 4, 4);
            var headerSize = 8;
            if (size == 1)
            {
                if (offset + 16 > end || file.Read(header, 8, 8) != 8)
                {
                    yield break;
                }

                var large = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8));
                if (large > long.MaxValue)
                {
                    yield break;
                }

                size = (long)large;
                headerSize = 16;
            }
            else if (size == 0)
            {
                // "To the end of the file", only meaningful at the top level.
                size = end - offset;
            }

            if (size < headerSize || offset + size > end || !IsBoxType(type))
            {
                yield break;
            }

            yield return (offset, size, headerSize, type);
            offset += size;
        }
    }

    private static bool IsBoxType(string type) =>
        type.Length == 4 && type.All(c => c is >= ' ' and <= '~' || c == '©');
}
