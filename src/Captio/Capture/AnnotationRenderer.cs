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
            case RedactionAnnotation r:
                dc.DrawRectangle(Brushes.Black, null, new Rect(r.X, r.Y, r.Width, r.Height));
                break;
        }
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
