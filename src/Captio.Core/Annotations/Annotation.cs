using System.Collections.Immutable;

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

/// <summary>An annotation drawn with the user's color and a stroke.</summary>
public abstract record StrokedAnnotation(uint Argb, double Thickness) : Annotation;

public sealed record ArrowAnnotation(double X1, double Y1, double X2, double Y2, uint Argb, double Thickness)
    : StrokedAnnotation(Argb, Thickness)
{
    public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));

    public override Annotation Offset(double dx, double dy) =>
        this with { X1 = X1 + dx, Y1 = Y1 + dy, X2 = X2 + dx, Y2 = Y2 + dy };

    public override bool HitTest(double x, double y, double tolerance) =>
        Geometry.DistanceToSegment(x, y, X1, Y1, X2, Y2) <= tolerance + Thickness / 2;
}

/// <summary>A straight stroke with round ends, for underlining or tracing.</summary>
public sealed record LineAnnotation(double X1, double Y1, double X2, double Y2, uint Argb, double Thickness)
    : StrokedAnnotation(Argb, Thickness)
{
    public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));

    public override Annotation Offset(double dx, double dy) =>
        this with { X1 = X1 + dx, Y1 = Y1 + dy, X2 = X2 + dx, Y2 = Y2 + dy };

    public override bool HitTest(double x, double y, double tolerance) =>
        Geometry.DistanceToSegment(x, y, X1, Y1, X2, Y2) <= tolerance + Thickness / 2;
}

public readonly record struct PointD(double X, double Y);

/// <summary>A freehand pencil stroke through the given points.</summary>
public sealed record PencilAnnotation(ImmutableArray<PointD> Points, uint Argb, double Thickness)
    : StrokedAnnotation(Argb, Thickness)
{
    public double PathLength
    {
        get
        {
            var length = 0.0;
            for (var i = 1; i < Points.Length; i++)
                length += Math.Sqrt(Math.Pow(Points[i].X - Points[i - 1].X, 2) + Math.Pow(Points[i].Y - Points[i - 1].Y, 2));
            return length;
        }
    }

    public Bounds Bounds
    {
        get
        {
            if (Points.IsDefaultOrEmpty) return default;
            double minX = Points.Min(p => p.X), minY = Points.Min(p => p.Y);
            return new Bounds(minX, minY, Points.Max(p => p.X) - minX, Points.Max(p => p.Y) - minY);
        }
    }

    public override Annotation Offset(double dx, double dy) =>
        this with { Points = Points.Select(p => new PointD(p.X + dx, p.Y + dy)).ToImmutableArray() };

    /// <summary>Hits only on the stroke itself, never in the area it encloses.</summary>
    public override bool HitTest(double x, double y, double tolerance)
    {
        var reach = tolerance + Thickness / 2;
        if (Points.Length == 1) return Geometry.DistanceToSegment(x, y, Points[0].X, Points[0].Y, Points[0].X, Points[0].Y) <= reach;
        for (var i = 1; i < Points.Length; i++)
        {
            if (Geometry.DistanceToSegment(x, y, Points[i - 1].X, Points[i - 1].Y, Points[i].X, Points[i].Y) <= reach) return true;
        }
        return false;
    }
}

internal static class Geometry
{
    public static double DistanceToSegment(double x, double y, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((x - x1) * dx + (y - y1) * dy) / lengthSquared, 0, 1);
        var px = x1 + t * dx - x;
        var py = y1 + t * dy - y;
        return Math.Sqrt(px * px + py * py);
    }
}

/// <summary>Outline-only rectangle; its empty interior is not part of the shape.</summary>
public sealed record RectangleAnnotation(double X, double Y, double Width, double Height, uint Argb, double Thickness)
    : StrokedAnnotation(Argb, Thickness)
{
    public override Annotation Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    public override bool HitTest(double x, double y, double tolerance)
    {
        var reach = tolerance + Thickness / 2;
        var withinOuter = x >= X - reach && x <= X + Width + reach && y >= Y - reach && y <= Y + Height + reach;
        var withinInner = x > X + reach && x < X + Width - reach && y > Y + reach && y < Y + Height - reach;
        return withinOuter && !withinInner;
    }
}

/// <summary>Outline-only ellipse inscribed in the given bounds.</summary>
public sealed record EllipseAnnotation(double X, double Y, double Width, double Height, uint Argb, double Thickness)
    : StrokedAnnotation(Argb, Thickness)
{
    public override Annotation Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    public override bool HitTest(double x, double y, double tolerance)
    {
        var reach = tolerance + Thickness / 2;
        var a = Width / 2;
        var b = Height / 2;
        if (a < 0.5 || b < 0.5)
            return new RectangleAnnotation(X, Y, Width, Height, Argb, Thickness).HitTest(x, y, tolerance);

        var dx = x - (X + a);
        var dy = y - (Y + b);
        var k = Math.Sqrt(dx / a * (dx / a) + dy / b * (dy / b));
        // Distance along the ray from the center to the boundary (exact for circles).
        var distance = k == 0 ? Math.Min(a, b) : Math.Sqrt(dx * dx + dy * dy) * Math.Abs(1 - 1 / k);
        return distance <= reach;
    }
}

public sealed record RedactionAnnotation(double X, double Y, double Width, double Height) : Annotation
{
    public static RedactionAnnotation FromCorners(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public override Annotation Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    // Redactions are solid, so their whole area selects them.
    public override bool HitTest(double x, double y, double tolerance) =>
        x >= X - tolerance && x <= X + Width + tolerance &&
        y >= Y - tolerance && y <= Y + Height + tolerance;
}
