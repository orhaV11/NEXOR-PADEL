using System.Buffers.Binary;

namespace FitCheck.Api.Services.Security;

/// <summary>
/// Lossless metadata strip for the three photo formats the app accepts (Round 13). A phone's JPEG carries an Exif block
/// with the GPS position, the device model and the moment the photo was taken; a PNG or a WebP can carry the same in
/// text chunks or an EXIF/XMP chunk. None of it is the outfit. The client re-encodes through a canvas, which drops all of
/// it, but it falls back to the original file when the canvas fails, and an API client can send anything; so the server
/// strips on its own, before the bytes are stored and before they go to the model, without decoding a pixel: the
/// container is walked segment by segment (JPEG), chunk by chunk (PNG, WebP), the metadata pieces are left out, and
/// everything that draws the picture is copied byte for byte.
/// <para>
/// What stays, and why: the JPEG codestream (SOF, DHT, DQT, DRI, SOS and the scan data), the JFIF APP0 (densities, no
/// personal data), the ICC profile (APP2 <c>ICC_PROFILE</c>, PNG <c>iCCP</c>, WebP <c>ICCP</c>: a wide-gamut phone photo
/// shifts colour without it, and a profile names a colour space, not a person) and Adobe's APP14 (the colour transform
/// flag: dropping it turns some JPEGs the wrong colours). What goes: APP1 (Exif, XMP), every other APPn (MPF embedded
/// previews, Photoshop IPTC, ...), COM, anything after the EOI marker (a "motion photo" hides a clip there); PNG
/// tEXt/zTXt/iTXt/eXIf/tIME and every chunk not on the allow-list, and anything after IEND; WebP EXIF and XMP chunks with
/// their flags in VP8X cleared, and anything after the RIFF container.
/// </para>
/// <para>
/// A file the walker cannot follow (the fixtures in the test suite are magic bytes over noise; a corrupt upload) is
/// returned as it came from the point the walk failed: the person's own photo is the only thing at stake, the model
/// refuses what it cannot read, and refusing here would turn every corrupt file into a support question.
/// </para>
/// </summary>
public static class ImageMetadata
{
    /// <summary>The bytes to store and to send: the same array when nothing was stripped, a fresh one otherwise.</summary>
    public static byte[] Strip(byte[] bytes, ImageFormat format)
    {
        if (format == ImageFormat.Jpeg)
        {
            return StripJpeg(bytes);
        }

        if (format == ImageFormat.Png)
        {
            return StripPng(bytes);
        }

        if (format == ImageFormat.WebP)
        {
            return StripWebP(bytes);
        }

        return bytes;
    }

    // ---------- JPEG ----------

    private const byte Marker = 0xFF;
    private const byte Soi = 0xD8;
    private const byte Eoi = 0xD9;
    private const byte Sos = 0xDA;

    /// <summary>
    /// Walks the marker segments. Entropy-coded data after an SOS is copied until the next real marker (in scan data a
    /// 0xFF is always followed by 0x00 or an RSTn), so progressive files with several scans and the tables between them
    /// come through whole.
    /// </summary>
    public static byte[] StripJpeg(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != Marker || bytes[1] != Soi)
        {
            return bytes;
        }

        var output = new MemoryStream(bytes.Length);
        output.Write(bytes, 0, 2);
        var changed = false;
        var i = 2;
        while (true)
        {
            if (i >= bytes.Length)
            {
                break;
            }

            if (bytes[i] != Marker)
            {
                // Not a marker where one must be: copy the rest untouched and stop guessing.
                output.Write(bytes, i, bytes.Length - i);
                break;
            }

            // Fill bytes: any number of 0xFF may precede a marker.
            while (i < bytes.Length && bytes[i] == Marker)
            {
                i++;
            }

            if (i >= bytes.Length)
            {
                output.WriteByte(Marker);
                break;
            }

            var marker = bytes[i];
            if (marker == Eoi)
            {
                output.WriteByte(Marker);
                output.WriteByte(Eoi);
                changed |= i + 1 < bytes.Length;
                break;
            }

            if (marker is >= 0xD0 and <= 0xD7 || marker == Soi || marker == 0x01)
            {
                // Standalone markers carry no length.
                output.WriteByte(Marker);
                output.WriteByte(marker);
                i++;
                continue;
            }

            if (i + 2 >= bytes.Length)
            {
                output.Write(bytes, i - 1, bytes.Length - (i - 1));
                break;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 1, 2));
            if (length < 2 || i + 1 + length > bytes.Length)
            {
                output.Write(bytes, i - 1, bytes.Length - (i - 1));
                break;
            }

            var payload = bytes.AsSpan(i + 3, length - 2);
            var segmentEnd = i + 1 + length;
            if (KeepJpegSegment(marker, payload))
            {
                output.WriteByte(Marker);
                output.Write(bytes, i, 1 + length);
            }
            else
            {
                changed = true;
            }

            i = segmentEnd;
            if (marker == Sos)
            {
                // Scan data runs to the next marker that is not stuffing (FF00) or a restart (FFD0-FFD7).
                var scanEnd = i;
                while (scanEnd < bytes.Length)
                {
                    if (bytes[scanEnd] == Marker && scanEnd + 1 < bytes.Length)
                    {
                        var next = bytes[scanEnd + 1];
                        if (next != 0x00 && !(next is >= 0xD0 and <= 0xD7) && next != Marker)
                        {
                            break;
                        }
                    }

                    scanEnd++;
                }

                output.Write(bytes, i, scanEnd - i);
                i = scanEnd;
            }
        }

        return changed ? output.ToArray() : bytes;
    }

    private static bool KeepJpegSegment(byte marker, ReadOnlySpan<byte> payload)
    {
        switch (marker)
        {
            case 0xE0:
                // JFIF only (densities, an optional thumbnail of the picture itself); Canon's CIFF and anything else in APP0 goes.
                return payload.StartsWith("JFIF"u8);
            case 0xE2:
                return payload.StartsWith("ICC_PROFILE\0"u8);
            case 0xEE:
                return payload.StartsWith("Adobe"u8);
            case >= 0xE1 and <= 0xEF:
                return false;
            case 0xFE:
                return false;
            default:
                // SOFn, DHT, DAC, DQT, DRI, DHP, EXP, JPGn, SOS: the codestream.
                return true;
        }
    }

    // ---------- PNG ----------

    private static readonly HashSet<string> PngKeep = new(StringComparer.Ordinal)
    {
        "IHDR", "PLTE", "IDAT", "IEND",
        "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "bKGD", "pHYs", "hIST",
        "acTL", "fcTL", "fdAT",
        "cICP", "mDCv", "cLLi"
    };

    public static byte[] StripPng(byte[] bytes)
    {
        const int signature = 8;
        if (bytes.Length < signature + 12)
        {
            return bytes;
        }

        var output = new MemoryStream(bytes.Length);
        output.Write(bytes, 0, signature);
        var changed = false;
        var i = signature;
        while (true)
        {
            if (i + 8 > bytes.Length)
            {
                output.Write(bytes, i, bytes.Length - i);
                break;
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i, 4));
            if (length > int.MaxValue - 12 || i + 12 + (int)length > bytes.Length)
            {
                output.Write(bytes, i, bytes.Length - i);
                break;
            }

            var chunkLength = 12 + (int)length;
            var type = System.Text.Encoding.ASCII.GetString(bytes, i + 4, 4);
            if (PngKeep.Contains(type))
            {
                output.Write(bytes, i, chunkLength);
            }
            else
            {
                changed = true;
            }

            i += chunkLength;
            if (type == "IEND")
            {
                changed |= i < bytes.Length;
                break;
            }
        }

        return changed ? output.ToArray() : bytes;
    }

    // ---------- WebP ----------

    private const byte WebPExifFlag = 0x08;
    private const byte WebPXmpFlag = 0x04;

    public static byte[] StripWebP(byte[] bytes)
    {
        const int header = 12;
        if (bytes.Length < header + 8)
        {
            return bytes;
        }

        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
        var end = riffSize > int.MaxValue - 8 ? bytes.Length : Math.Min(bytes.Length, 8 + (int)riffSize);
        var output = new MemoryStream(bytes.Length);
        output.Write(bytes, 0, header);
        var changed = false;
        var i = header;
        var vp8xFlagsOffset = -1;
        var walked = true;
        while (i + 8 <= end)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i + 4, 4));
            if (size > int.MaxValue - 9 || i + 8 + (int)size > end)
            {
                walked = false;
                break;
            }

            var padded = (int)size + ((int)size & 1);
            var chunkLength = Math.Min(8 + padded, end - i);
            var fourcc = System.Text.Encoding.ASCII.GetString(bytes, i, 4);
            if (fourcc is "EXIF" or "XMP ")
            {
                changed = true;
            }
            else
            {
                if (fourcc == "VP8X" && size >= 1)
                {
                    vp8xFlagsOffset = (int)output.Length + 8;
                }

                output.Write(bytes, i, chunkLength);
            }

            i += chunkLength;
        }

        if (!walked || i < end)
        {
            // A chunk the walker could not follow: the rest of the file goes through as it was, whatever the RIFF size says.
            output.Write(bytes, i, bytes.Length - i);
        }
        else if (end < bytes.Length)
        {
            // The container was walked whole: bytes after it are not part of the picture.
            changed = true;
        }

        if (!changed)
        {
            return bytes;
        }

        var result = output.ToArray();
        if (vp8xFlagsOffset >= 0 && vp8xFlagsOffset < result.Length)
        {
            result[vp8xFlagsOffset] = (byte)(result[vp8xFlagsOffset] & ~(WebPExifFlag | WebPXmpFlag));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)(result.Length - 8));
        return result;
    }
}
