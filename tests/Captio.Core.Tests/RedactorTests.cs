using Captio.Core.Annotations;
using Captio.Core.Imaging;

namespace Captio.Core.Tests;

public class RedactorTests
{
    private static byte[] White(int width, int height) => Enumerable.Repeat((byte)255, width * height * 4).ToArray();

    private static bool IsBlack(byte[] px, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return px[i] == 0 && px[i + 1] == 0 && px[i + 2] == 0 && px[i + 3] == 255;
    }

    [Fact]
    public void Replaces_pixels_inside_redaction_with_opaque_black()
    {
        var px = White(10, 10);
        Redactor.Apply(px, 10, 10, 40, [new RedactionAnnotation(2, 3, 4, 2)]);

        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 10; x++)
        {
            var inside = x is >= 2 and < 6 && y is >= 3 and < 5;
            Assert.Equal(inside, IsBlack(px, 10, x, y));
        }
    }

    [Fact]
    public void Rounds_fractional_edges_outward()
    {
        var px = White(10, 10);
        Redactor.Apply(px, 10, 10, 40, [new RedactionAnnotation(1.6, 1.6, 0.5, 0.5)]);

        Assert.True(IsBlack(px, 10, 1, 1));
        Assert.True(IsBlack(px, 10, 2, 2));
        Assert.False(IsBlack(px, 10, 3, 3));
    }

    [Fact]
    public void Applies_origin_offset_and_clips_to_buffer()
    {
        var px = White(4, 4);
        Redactor.Apply(px, 4, 4, 16, [new RedactionAnnotation(102, 101, 10, 10)], originX: 100, originY: 100);

        Assert.False(IsBlack(px, 4, 1, 1));
        Assert.False(IsBlack(px, 4, 2, 0));
        Assert.True(IsBlack(px, 4, 2, 1));
        Assert.True(IsBlack(px, 4, 3, 3));
    }

    [Fact]
    public void Respects_row_padding()
    {
        const int stride = 4 * 3 + 4;
        var px = Enumerable.Repeat((byte)255, stride * 2).ToArray();
        Redactor.Apply(px, 3, 2, stride, [new RedactionAnnotation(0, 0, 3, 2)]);

        Assert.All(new[] { 12, 13, 14, 15, 28, 29, 30, 31 }, i => Assert.Equal(255, px[i]));
    }
}
