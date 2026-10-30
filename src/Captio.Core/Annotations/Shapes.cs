namespace Captio.Core.Annotations;

public enum ShapeKind
{
    Arrow,
    Rectangle,
    Ellipse,
}

public readonly record struct Bounds(double X, double Y, double Width, double Height);

public static class ShapeFactory
{
    /// <summary>
    /// Bounds dragged from (x1, y1) to (x2, y2). With <paramref name="square"/> both
    /// sides take the longer length, keeping the drag direction.
    /// </summary>
    public static Bounds DragBounds(double x1, double y1, double x2, double y2, bool square)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        if (square)
        {
            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = dx < 0 ? -side : side;
            dy = dy < 0 ? -side : side;
        }
        return new Bounds(Math.Min(x1, x1 + dx), Math.Min(y1, y1 + dy), Math.Abs(dx), Math.Abs(dy));
    }

    /// <param name="constrain">Shift held: square for rectangles, circle for ellipses.</param>
    public static StrokedAnnotation Create(
        ShapeKind kind, double x1, double y1, double x2, double y2, bool constrain, uint argb, double thickness)
    {
        if (kind == ShapeKind.Arrow) return new ArrowAnnotation(x1, y1, x2, y2, argb, thickness);

        var b = DragBounds(x1, y1, x2, y2, constrain);
        return kind == ShapeKind.Rectangle
            ? new RectangleAnnotation(b.X, b.Y, b.Width, b.Height, argb, thickness)
            : new EllipseAnnotation(b.X, b.Y, b.Width, b.Height, argb, thickness);
    }

    /// <summary>Rejects accidental clicks that would leave a dot instead of a shape.</summary>
    public static bool IsMeaningful(Annotation annotation, double minSize) => annotation switch
    {
        ArrowAnnotation a => a.Length >= minSize,
        RectangleAnnotation r => r.Width >= minSize || r.Height >= minSize,
        EllipseAnnotation e => e.Width >= minSize || e.Height >= minSize,
        RedactionAnnotation r => r.Width >= 2 && r.Height >= 2,
        _ => false,
    };

    public static Bounds BoundsOf(Annotation annotation) => annotation switch
    {
        ArrowAnnotation a => new Bounds(Math.Min(a.X1, a.X2), Math.Min(a.Y1, a.Y2), Math.Abs(a.X2 - a.X1), Math.Abs(a.Y2 - a.Y1)),
        RectangleAnnotation r => new Bounds(r.X, r.Y, r.Width, r.Height),
        EllipseAnnotation e => new Bounds(e.X, e.Y, e.Width, e.Height),
        RedactionAnnotation r => new Bounds(r.X, r.Y, r.Width, r.Height),
        _ => default,
    };
}
