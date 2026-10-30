using System.Windows;
using System.Windows.Media;
using Captio.Core.Annotations;

namespace Captio.Capture;

/// <summary>Draws annotations in pixel space. Shared by the editor and the final flattening.</summary>
internal static class AnnotationRenderer
{
    public static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    public static void Draw(DrawingContext dc, Annotation annotation)
    {
        switch (annotation)
        {
            case ArrowAnnotation arrow:
                DrawArrow(dc, arrow);
                break;
            case LineAnnotation line:
                dc.DrawLine(RoundPen(line), new Point(line.X1, line.Y1), new Point(line.X2, line.Y2));
                break;
            case PencilAnnotation pencil when !pencil.Points.IsDefaultOrEmpty:
                DrawPencil(dc, pencil);
                break;
            case RectangleAnnotation rect:
                dc.DrawRectangle(null, OutlinePen(rect), new Rect(rect.X, rect.Y, rect.Width, rect.Height));
                break;
            case EllipseAnnotation ellipse:
                dc.DrawEllipse(null, OutlinePen(ellipse),
                    new Point(ellipse.X + ellipse.Width / 2, ellipse.Y + ellipse.Height / 2), ellipse.Width / 2, ellipse.Height / 2);
                break;
            case RedactionAnnotation r:
                dc.DrawRectangle(Brushes.Black, null, new Rect(r.X, r.Y, r.Width, r.Height));
                break;
        }
    }

    private static Pen OutlinePen(StrokedAnnotation shape)
    {
        var brush = new SolidColorBrush(ToColor(shape.Argb));
        brush.Freeze();
        var pen = new Pen(brush, shape.Thickness) { LineJoin = PenLineJoin.Miter };
        pen.Freeze();
        return pen;
    }

    private static Pen RoundPen(StrokedAnnotation shape)
    {
        var brush = new SolidColorBrush(ToColor(shape.Argb));
        brush.Freeze();
        var pen = new Pen(brush, shape.Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static void DrawPencil(DrawingContext dc, PencilAnnotation p)
    {
        var pen = RoundPen(p);
        if (p.Points.Length == 1)
        {
            // A single click leaves a round dot the size of the stroke.
            dc.DrawEllipse(pen.Brush, null, new Point(p.Points[0].X, p.Points[0].Y), p.Thickness / 2, p.Thickness / 2);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(p.Points[0].X, p.Points[0].Y), isFilled: false, isClosed: false);
            ctx.PolyLineTo(p.Points.Skip(1).Select(pt => new Point(pt.X, pt.Y)).ToList(), isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void DrawArrow(DrawingContext dc, ArrowAnnotation a)
    {
        var length = a.Length;
        if (length < 1) return;

        var brush = new SolidColorBrush(ToColor(a.Argb));
        brush.Freeze();

        var ux = (a.X2 - a.X1) / length;
        var uy = (a.Y2 - a.Y1) / length;
        var head = Math.Min(length, Math.Max(14, a.Thickness * 3.6));
        var halfWidth = head * 0.48;
        var baseX = a.X2 - ux * head;
        var baseY = a.Y2 - uy * head;

        var pen = new Pen(brush, a.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Flat };
        pen.Freeze();
        // Stop the shaft inside the head so the joint has no visible seam.
        dc.DrawLine(pen, new Point(a.X1, a.Y1), new Point(baseX + ux * head * 0.3, baseY + uy * head * 0.3));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(a.X2, a.Y2), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(baseX - uy * halfWidth, baseY + ux * halfWidth), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(baseX + uy * halfWidth, baseY - ux * halfWidth), isStroked: false, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }
}
