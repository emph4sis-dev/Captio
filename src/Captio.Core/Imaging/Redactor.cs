using Captio.Core.Annotations;

namespace Captio.Core.Imaging;

/// <summary>
/// Destructively replaces redacted areas of a 32-bit BGRA buffer with opaque black.
/// </summary>
public static class Redactor
{
    /// <param name="pixels">BGRA pixels, 4 bytes per pixel.</param>
    /// <param name="originX">Position of the buffer's top-left pixel in annotation space.</param>
    /// <param name="originY">Position of the buffer's top-left pixel in annotation space.</param>
    public static void Apply(
        Span<byte> pixels, int width, int height, int stride,
        IEnumerable<RedactionAnnotation> redactions, double originX = 0, double originY = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);
        if (pixels.Length < stride * height) throw new ArgumentException("Buffer is smaller than stride * height.", nameof(pixels));

        foreach (var r in redactions)
        {
            // Round outward so partially covered pixels are always blacked out.
            var left = Math.Max(0, (int)Math.Floor(r.X - originX));
            var top = Math.Max(0, (int)Math.Floor(r.Y - originY));
            var right = Math.Min(width, (int)Math.Ceiling(r.X + r.Width - originX));
            var bottom = Math.Min(height, (int)Math.Ceiling(r.Y + r.Height - originY));
            if (left >= right || top >= bottom) continue;

            for (var y = top; y < bottom; y++)
            {
                var row = pixels.Slice(y * stride + left * 4, (right - left) * 4);
                for (var i = 0; i < row.Length; i += 4)
                {
                    row[i] = 0;
                    row[i + 1] = 0;
                    row[i + 2] = 0;
                    row[i + 3] = 255;
                }
            }
        }
    }
}
