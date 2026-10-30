namespace Captio.Core.Annotations;

/// <summary>
/// An editable mark on a capture. Coordinates are physical pixels in the
/// coordinate space of the captured monitor image.
/// </summary>
public abstract record Annotation
{
    public abstract Annotation Offset(double dx, double dy);

    public abstract bool HitTest(double x, double y, double tolerance);
}

public sealed record ArrowAnnotation(double X1, double Y1, double X2, double Y2, uint Argb, double Thickness) : Annotation
{
    public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));

    public override Annotation Offset(double dx, double dy) =>
        this with { X1 = X1 + dx, Y1 = Y1 + dy, X2 = X2 + dx, Y2 = Y2 + dy };

    public override bool HitTest(double x, double y, double tolerance)
    {
        var dx = X2 - X1;
        var dy = Y2 - Y1;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((x - X1) * dx + (y - Y1) * dy) / lengthSquared, 0, 1);
        var px = X1 + t * dx - x;
        var py = Y1 + t * dy - y;
        var reach = tolerance + Thickness / 2;
        return px * px + py * py <= reach * reach;
    }
}

public sealed record RedactionAnnotation(double X, double Y, double Width, double Height) : Annotation
{
    public static RedactionAnnotation FromCorners(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public override Annotation Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    public override bool HitTest(double x, double y, double tolerance) =>
        x >= X - tolerance && x <= X + Width + tolerance &&
        y >= Y - tolerance && y <= Y + Height + tolerance;
}
