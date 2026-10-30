using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captio.Capture;
using Captio.Core.Annotations;

namespace Captio.Tests;

public class ImageComposerTests
{
    /// <summary>WPF rendering requires an STA thread.</summary>
    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
        return result;
    }

    /// <summary>A 40x30 "monitor" filled with a recognizable color.</summary>
    private static BitmapSource Monitor(byte b, byte g, byte r)
    {
        const int w = 40, h = 30;
        var pixels = new byte[w * h * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
        return pixels;
    }

    [Fact]
    public void Redacted_area_is_solid_black_in_bitmap_and_encoded_png()
    {
        var (bitmap, decoded) = OnSta(() =>
        {
            var image = ImageComposer.Flatten(Monitor(10, 200, 30), new Int32Rect(5, 5, 20, 10),
                [new RedactionAnnotation(10, 8, 6, 4)]);
            var frame = BitmapDecoder.Create(new MemoryStream(image.Png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            return (Pixels(image.Bitmap), Pixels(frame));
        });

        foreach (var px in new[] { bitmap, decoded })
        {
            for (var y = 0; y < 10; y++)
            for (var x = 0; x < 20; x++)
            {
                var i = (y * 20 + x) * 4;
                // Region starts at (5,5): the redaction covers x 5..10, y 3..6 in output space.
                var redacted = x is >= 5 and < 11 && y is >= 3 and < 7;
                var expected = redacted ? new byte[] { 0, 0, 0, 255 } : [10, 200, 30, 255];
                Assert.Equal(expected, px[i..(i + 4)]);
            }
        }
    }

    [Fact]
    public void Arrow_under_a_redaction_cannot_reveal_anything()
    {
        var px = OnSta(() => Pixels(ImageComposer.Flatten(Monitor(255, 255, 255), new Int32Rect(0, 0, 40, 30),
        [
            new RedactionAnnotation(0, 0, 40, 30),
            new ArrowAnnotation(2, 2, 38, 28, 0xFFE5484D, 6),
        ]).Bitmap));

        Assert.All(px.Chunk(4), p => Assert.Equal(new byte[] { 0, 0, 0, 255 }, p));
    }

    [Fact]
    public void Arrow_is_drawn_inside_region()
    {
        var px = OnSta(() => Pixels(ImageComposer.Flatten(Monitor(255, 255, 255), new Int32Rect(0, 0, 40, 30),
            [new ArrowAnnotation(2, 15, 38, 15, 0xFFFF0000, 6)]).Bitmap));

        var i = (15 * 40 + 20) * 4;
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, px[i..(i + 4)]);
    }

    [Fact]
    public void Png_has_no_ancillary_metadata()
    {
        var png = OnSta(() => ImageComposer.Flatten(Monitor(1, 2, 3), new Int32Rect(0, 0, 8, 8), []).Png);
        var ascii = Encoding.ASCII.GetString(png);

        foreach (var chunk in new[] { "pHYs", "tEXt", "iTXt", "zTXt", "tIME", "eXIf", "iCCP", "gAMA", "sRGB", "cHRM" })
            Assert.DoesNotContain(chunk, ascii);
    }

    private static byte[] At(byte[] px, int width, int x, int y) => px[((y * width + x) * 4)..((y * width + x) * 4 + 4)];

    private static readonly byte[] White = [255, 255, 255, 255];
    private static readonly byte[] PureRed = [0, 0, 255, 255];
    private static readonly byte[] Black = [0, 0, 0, 255];

    [Fact]
    public void Rectangle_is_flattened_as_outline_only()
    {
        // Stroke 2 centered on x=5 covers pixels 4 and 5 exactly.
        var px = OnSta(() => Pixels(ImageComposer.Flatten(Monitor(255, 255, 255), new Int32Rect(0, 0, 40, 30),
            [new RectangleAnnotation(5, 5, 30, 20, 0xFFFF0000, 2)]).Bitmap));

        Assert.Equal(PureRed, At(px, 40, 5, 15));  // left edge
        Assert.Equal(PureRed, At(px, 40, 20, 4));  // top edge
        Assert.Equal(White, At(px, 40, 20, 15));   // interior untouched: no fill
        Assert.Equal(White, At(px, 40, 1, 1));     // outside
    }

    [Fact]
    public void Ellipse_is_flattened_as_outline_only()
    {
        var px = OnSta(() => Pixels(ImageComposer.Flatten(Monitor(255, 255, 255), new Int32Rect(0, 0, 40, 30),
            [new EllipseAnnotation(4, 4, 32, 22, 0xFFFF0000, 3)]).Bitmap));

        var rightmost = At(px, 40, 35, 15);
        Assert.True(rightmost[2] > 200 && rightmost[1] < 80, "the outline is red");
        Assert.Equal(White, At(px, 40, 20, 15));   // center
        Assert.Equal(White, At(px, 40, 1, 1));     // bounding-box corner outside the curve
    }

    [Fact]
    public void Shapes_are_clipped_to_the_capture_region()
    {
        // Region (10,10,20,10); the rectangle's outline lies entirely outside it.
        var px = OnSta(() => Pixels(ImageComposer.Flatten(Monitor(255, 255, 255), new Int32Rect(10, 10, 20, 10),
            [new RectangleAnnotation(0, 0, 40, 30, 0xFFFF0000, 2), new RectangleAnnotation(15, 12, 100, 100, 0xFFFF0000, 2)]).Bitmap));

        Assert.Equal(20 * 10 * 4, px.Length);
        Assert.Equal(PureRed, At(px, 20, 5, 5));   // left edge of the second rectangle, in region space
        Assert.Equal(White, At(px, 20, 0, 0));
        Assert.Equal(White, At(px, 20, 19, 9));
    }

    [Fact]
    public void Redaction_wins_over_shapes_drawn_on_top()
    {
        var px = OnSta(() => Pixels(ImageComposer.Flatten(Monitor(255, 255, 255), new Int32Rect(0, 0, 40, 30),
        [
            new RedactionAnnotation(0, 0, 40, 30),
            new RectangleAnnotation(5, 5, 30, 20, 0xFF00FF00, 4),
            new EllipseAnnotation(5, 5, 30, 20, 0xFF0000FF, 4),
        ]).Bitmap));

        Assert.All(px.Chunk(4), p => Assert.Equal(Black, p));
    }

    [Fact]
    public void Line_and_pencil_are_flattened_in_their_color()
    {
        var px = OnSta(() => Pixels(ImageComposer.Flatten(Monitor(255, 255, 255), new Int32Rect(0, 0, 40, 30),
        [
            new LineAnnotation(4, 6, 36, 6, 0xFFFF0000, 4),
            new PencilAnnotation([new PointD(4, 20), new PointD(20, 24), new PointD(36, 20)], 0xFF0000FF, 4),
        ]).Bitmap));

        Assert.Equal(PureRed, At(px, 40, 20, 6));        // on the line
        Assert.Equal(White, At(px, 40, 20, 12));         // between them
        var onStroke = At(px, 40, 20, 24);
        Assert.True(onStroke[0] > 200 && onStroke[2] < 60, "the pencil stroke is blue");
    }

    [Fact]
    public void Redaction_hides_a_pencil_stroke_drawn_over_it()
    {
        var px = OnSta(() => Pixels(ImageComposer.Flatten(Monitor(255, 255, 255), new Int32Rect(0, 0, 40, 30),
        [
            new RedactionAnnotation(0, 0, 40, 30),
            new PencilAnnotation([new PointD(2, 2), new PointD(38, 28)], 0xFF00FF00, 6),
        ]).Bitmap));

        Assert.All(px.Chunk(4), p => Assert.Equal(Black, p));
    }
}
