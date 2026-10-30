using System.Buffers.Binary;

namespace Captio.Core.Imaging;

/// <summary>
/// Removes every PNG chunk that is not needed to decode the image
/// (text, timestamps, EXIF, physical size, color profiles...).
/// </summary>
public static class PngSanitizer
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly HashSet<string> Kept = ["IHDR", "PLTE", "tRNS", "IDAT", "IEND"];

    public static byte[] Sanitize(ReadOnlySpan<byte> png)
    {
        if (!png.StartsWith(Signature)) throw new InvalidDataException("Not a PNG stream.");

        using var output = new MemoryStream(png.Length);
        output.Write(Signature);
        var offset = Signature.Length;
        while (offset + 12 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png[offset..]);
            var total = 12 + length;
            if (length < 0 || offset + total > png.Length) throw new InvalidDataException("Truncated PNG chunk.");

            var type = System.Text.Encoding.ASCII.GetString(png.Slice(offset + 4, 4));
            if (Kept.Contains(type)) output.Write(png.Slice(offset, total));
            offset += total;
            if (type == "IEND") break;
        }
        return output.ToArray();
    }

    /// <summary>Reads the pixel dimensions from the IHDR chunk.</summary>
    public static (int Width, int Height) ReadSize(ReadOnlySpan<byte> png)
    {
        if (png.Length < 24 || !png.StartsWith(Signature)) throw new InvalidDataException("Not a PNG stream.");
        return ((int)BinaryPrimitives.ReadUInt32BigEndian(png[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(png[20..]));
    }
}
