using Captio.Core.Annotations;

namespace Captio.Core.Tests;

public class ShapeTests
{
    private const uint Red = 0xFFFF0000;

    [Fact]
    public void Drag_in_any_direction_normalizes_bounds()
    {
        Assert.Equal(new Bounds(10, 20, 30, 40), ShapeFactory.DragBounds(40, 60, 10, 20, square: false));
    }

    [Theory]
    [InlineData(100, 100, 160, 120, 100, 100, 60)] // down-right: width wins
    [InlineData(100, 100, 90, 40, 40, 40, 60)]     // up-left: height wins, keeps direction
    [InlineData(100, 100, 130, 60, 100, 60, 40)]   // up-right
    public void Shift_turns_rectangle_into_square(double x1, double y1, double x2, double y2, double x, double y, double side)
    {
        var shape = Assert.IsType<RectangleAnnotation>(ShapeFactory.Create(ShapeKind.Rectangle, x1, y1, x2, y2, constrain: true, Red, 6));
        Assert.Equal((x, y, side, side), (shape.X, shape.Y, shape.Width, shape.Height));
    }

    [Fact]
    public void Shift_turns_ellipse_into_circle()
    {
        var shape = Assert.IsType<EllipseAnnotation>(ShapeFactory.Create(ShapeKind.Ellipse, 0, 0, 50, 20, constrain: true, Red, 6));
        Assert.Equal(shape.Width, shape.Height);
        Assert.Equal(50, shape.Width);
    }

    [Fact]
    public void Without_shift_shapes_follow_the_drag()
    {
        var rect = Assert.IsType<RectangleAnnotation>(ShapeFactory.Create(ShapeKind.Rectangle, 0, 0, 50, 20, false, Red, 6));
        var ellipse = Assert.IsType<EllipseAnnotation>(ShapeFactory.Create(ShapeKind.Ellipse, 0, 0, 50, 20, false, Red, 6));
        var arrow = Assert.IsType<ArrowAnnotation>(ShapeFactory.Create(ShapeKind.Arrow, 0, 0, 50, 20, true, Red, 6));

        Assert.Equal((50.0, 20.0), (rect.Width, rect.Height));
        Assert.Equal((50.0, 20.0), (ellipse.Width, ellipse.Height));
        Assert.Equal((50.0, 20.0), (arrow.X2, arrow.Y2));
        Assert.All(new StrokedAnnotation[] { rect, ellipse, arrow }, s => Assert.Equal(Red, s.Argb));
    }

    [Fact]
    public void Rectangle_hit_test_only_on_outline()
    {
        var rect = new RectangleAnnotation(100, 100, 200, 100, Red, 6);

        Assert.True(rect.HitTest(100, 150, 2));   // left edge
        Assert.True(rect.HitTest(200, 202, 2));   // bottom edge, within stroke
        Assert.True(rect.HitTest(304, 100, 2));   // just outside the corner, within reach
        Assert.False(rect.HitTest(200, 150, 2));  // empty interior
        Assert.False(rect.HitTest(120, 150, 2));  // interior near, but not on, the edge
        Assert.False(rect.HitTest(320, 150, 2));  // outside
    }

    [Fact]
    public void Ellipse_hit_test_only_on_outline()
    {
        var ellipse = new EllipseAnnotation(0, 0, 200, 100, Red, 6); // center (100,50), radii 100x50

        Assert.True(ellipse.HitTest(200, 50, 2));  // rightmost point
        Assert.True(ellipse.HitTest(100, 1, 2));   // near the top
        Assert.True(ellipse.HitTest(-3, 50, 2));   // just outside the left
        Assert.False(ellipse.HitTest(100, 50, 2)); // center
        Assert.False(ellipse.HitTest(150, 50, 2)); // interior
        Assert.False(ellipse.HitTest(195, 5, 2));  // bounding box corner, outside the curve
    }

    [Fact]
    public void Circle_hit_test_is_exact_on_the_radius()
    {
        var circle = new EllipseAnnotation(0, 0, 100, 100, Red, 4);
        var angle = Math.PI / 3;
        Assert.True(circle.HitTest(50 + Math.Cos(angle) * 52, 50 + Math.Sin(angle) * 52, 1)); // 2 px out, reach 3
        Assert.False(circle.HitTest(50 + Math.Cos(angle) * 55, 50 + Math.Sin(angle) * 55, 1));
    }

    [Fact]
    public void Shapes_move_with_offset()
    {
        Assert.Equal(new RectangleAnnotation(15, 25, 10, 10, Red, 6), new RectangleAnnotation(10, 20, 10, 10, Red, 6).Offset(5, 5));
        Assert.Equal(new EllipseAnnotation(5, 15, 10, 10, Red, 6), new EllipseAnnotation(10, 20, 10, 10, Red, 6).Offset(-5, -5));
    }

    [Fact]
    public void Shapes_can_be_selected_moved_deleted_undone_and_redone()
    {
        var doc = new AnnotationDocument();
        var rect = new RectangleAnnotation(0, 0, 100, 100, Red, 6);
        var ellipse = new EllipseAnnotation(200, 0, 100, 100, Red, 6);
        doc.Add(rect);
        doc.Add(ellipse);

        Assert.Equal(0, doc.HitTest(0, 50, 3));
        Assert.Equal(1, doc.HitTest(300, 50, 3));
        Assert.Equal(-1, doc.HitTest(50, 50, 3)); // interior of the rectangle selects nothing

        doc.Replace(0, rect.Offset(10, 0));
        Assert.Equal(new RectangleAnnotation(10, 0, 100, 100, Red, 6), doc.Items[0]);
        doc.RemoveAt(1);
        Assert.Single(doc.Items);

        doc.Undo();
        Assert.Equal(2, doc.Items.Count);
        doc.Undo();
        Assert.Equal(rect, doc.Items[0]);
        doc.Redo();
        doc.Redo();
        Assert.Single(doc.Items);
    }

    [Fact]
    public void Tiny_drags_are_not_shapes()
    {
        Assert.False(ShapeFactory.IsMeaningful(new RectangleAnnotation(0, 0, 1, 1, Red, 6), 4));
        Assert.True(ShapeFactory.IsMeaningful(new RectangleAnnotation(0, 0, 40, 1, Red, 6), 4));
        Assert.False(ShapeFactory.IsMeaningful(new ArrowAnnotation(0, 0, 2, 0, Red, 6), 4));
    }

    [Fact]
    public void Palette_has_eight_fully_saturated_hues()
    {
        Assert.Equal(["Rojo", "Naranja", "Amarillo", "Verde", "Cian", "Azul", "Violeta", "Magenta"], ShapePalette.Colors.Select(c => c.Name));
        foreach (var color in ShapePalette.Colors)
        {
            byte r = (byte)(color.Argb >> 16), g = (byte)(color.Argb >> 8), b = (byte)color.Argb;
            Assert.Equal(0xFF, (byte)(color.Argb >> 24));
            Assert.Equal(255, Math.Max(r, Math.Max(g, b)));  // S = 100 %
            Assert.Equal(0, Math.Min(r, Math.Min(g, b)));    // L = 50 %
        }
    }

    [Theory]
    [InlineData(0xFFE5484D, 0xFFFF0000)] // old default red
    [InlineData(0xFF3E8BFF, 0xFF0000FF)] // old blue
    [InlineData(0xFFF5A524, 0xFFFF8000)] // old amber
    [InlineData(0xFFF4F4F5, 0xFFFF0000)] // old white (gray) → default
    [InlineData(0xFF00FFFF, 0xFF00FFFF)] // already in palette
    public void Old_colors_map_to_the_closest_palette_hue(uint old, uint expected)
    {
        Assert.Equal(expected, ShapePalette.Nearest(old));
    }
}
