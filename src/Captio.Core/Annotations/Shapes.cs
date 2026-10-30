namespace Captio.Core.Annotations;

public enum ShapeKind
{
    Arrow,
    Rectangle,
    Ellipse,
    Line,
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

    /// <summary>The end point snapped to the nearest multiple of 45°, keeping the drag length.</summary>
    public static (double X, double Y) SnapTo45(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0) return (x2, y2);
        var angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        // Round away floating noise so horizontal lines stay exactly horizontal.
        return (Math.Round(x1 + Math.Cos(angle) * length, 6), Math.Round(y1 + Math.Sin(angle) * length, 6));
    }

    /// <param name="constrain">Shift held: square for rectangles, circle for ellipses, 45° steps for lines.</param>
    public static StrokedAnnotation Create(
        ShapeKind kind, double x1, double y1, double x2, double y2, bool constrain, uint argb, double thickness)
    {
        switch (kind)
        {
            case ShapeKind.Arrow:
                return new ArrowAnnotation(x1, y1, x2, y2, argb, thickness);
            case ShapeKind.Line:
                var (ex, ey) = constrain ? SnapTo45(x1, y1, x2, y2) : (x2, y2);
                return new LineAnnotation(x1, y1, ex, ey, argb, thickness);
        }

        var b = DragBounds(x1, y1, x2, y2, constrain);
        return kind == ShapeKind.Rectangle
            ? new RectangleAnnotation(b.X, b.Y, b.Width, b.Height, argb, thickness)
            : new EllipseAnnotation(b.X, b.Y, b.Width, b.Height, argb, thickness);
    }

    /// <summary>Rejects accidental clicks that would leave a dot instead of a shape.</summary>
    public static bool IsMeaningful(Annotation annotation, double minSize) => annotation switch
    {
        ArrowAnnotation a => a.Length >= minSize,
        LineAnnotation l => l.Length >= minSize,
        PencilAnnotation p => p.Points.Length >= 2 && p.PathLength >= minSize / 2,
        RectangleAnnotation r => r.Width >= minSize || r.Height >= minSize,
        EllipseAnnotation e => e.Width >= minSize || e.Height >= minSize,
        RedactionAnnotation r => r.Width >= 2 && r.Height >= 2,
        _ => false,
    };

    public static Bounds BoundsOf(Annotation annotation) => annotation switch
    {
        ArrowAnnotation a => Span(a.X1, a.Y1, a.X2, a.Y2),
        LineAnnotation l => Span(l.X1, l.Y1, l.X2, l.Y2),
        PencilAnnotation p => p.Bounds,
        RectangleAnnotation r => new Bounds(r.X, r.Y, r.Width, r.Height),
        EllipseAnnotation e => new Bounds(e.X, e.Y, e.Width, e.Height),
        RedactionAnnotation r => new Bounds(r.X, r.Y, r.Width, r.Height),
        _ => default,
    };

    private static Bounds Span(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
}
