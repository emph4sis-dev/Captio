using Captio.Core.Annotations;
using Captio.Core.Settings;

namespace Captio.Core.Layout;

/// <param name="Inside">True when no outer side fit and the toolbar overlaps the selection.</param>
public readonly record struct ToolbarPosition(double X, double Y, ToolbarSide Side, bool Inside);

/// <summary>
/// Places the editing toolbar next to the selection, centered along that side
/// (clamped to the screen). Tries the preferred side, then
/// the opposite side, then a spot inside the screen that leaves the selection's center
/// visible. The result is always fully on screen when the toolbar fits on it at all.
/// </summary>
public static class ToolbarPlacement
{
    public const double Gap = 10;
    public const double Margin = 8;

    public static bool IsVertical(ToolbarSide side) => side is ToolbarSide.Left or ToolbarSide.Right;

    public static ToolbarSide Opposite(ToolbarSide side) => side switch
    {
        ToolbarSide.Left => ToolbarSide.Right,
        ToolbarSide.Right => ToolbarSide.Left,
        ToolbarSide.Top => ToolbarSide.Bottom,
        _ => ToolbarSide.Top,
    };

    /// <summary>
    /// Selection and screen in physical pixels, toolbar in DIPs; returns DIPs.
    /// Keeps placement identical across monitors with different scaling.
    /// </summary>
    public static ToolbarPosition PlaceOnMonitor(
        Bounds selectionPixels, double toolbarWidth, double toolbarHeight, Bounds screenPixels, ToolbarSide preferred, double dpiScale) =>
        Place(Scale(selectionPixels, 1 / dpiScale), toolbarWidth, toolbarHeight, Scale(screenPixels, 1 / dpiScale), preferred);

    /// <summary>All inputs and the result share one unit (DIPs).</summary>
    public static ToolbarPosition Place(Bounds selection, double width, double height, Bounds screen, ToolbarSide preferred)
    {
        foreach (var side in new[] { preferred, Opposite(preferred) })
        {
            var (x, y) = Outside(side, selection, width, height);
            if (FitsOutside(side, x, y, width, height, screen))
            {
                var (cx, cy) = Clamp(x, y, width, height, screen);
                return new ToolbarPosition(cx, cy, side, Inside: false);
            }
        }

        var centerX = selection.X + selection.Width / 2;
        var centerY = selection.Y + selection.Height / 2;
        var candidates = new[]
        {
            (preferred, InsideSelection(preferred, selection, width, height)),
            (Opposite(preferred), InsideSelection(Opposite(preferred), selection, width, height)),
            (preferred, AtScreenEdge(preferred, selection, width, height, screen)),
            (Opposite(preferred), AtScreenEdge(Opposite(preferred), selection, width, height, screen)),
        };
        foreach (var (side, (x, y)) in candidates)
        {
            var (cx, cy) = Clamp(x, y, width, height, screen);
            var coversCenter = centerX >= cx && centerX <= cx + width && centerY >= cy && centerY <= cy + height;
            if (!coversCenter) return new ToolbarPosition(cx, cy, side, Inside: true);
        }

        var (fx, fy) = Clamp(candidates[0].Item2.Item1, candidates[0].Item2.Item2, width, height, screen);
        return new ToolbarPosition(fx, fy, preferred, Inside: true);
    }

    private static (double X, double Y) Outside(ToolbarSide side, Bounds s, double w, double h) => side switch
    {
        ToolbarSide.Right => (s.X + s.Width + Gap, CenterY(s, h)),
        ToolbarSide.Left => (s.X - Gap - w, CenterY(s, h)),
        ToolbarSide.Bottom => (CenterX(s, w), s.Y + s.Height + Gap),
        _ => (CenterX(s, w), s.Y - Gap - h),
    };

    private static bool FitsOutside(ToolbarSide side, double x, double y, double w, double h, Bounds screen) => side switch
    {
        ToolbarSide.Right => x + w <= screen.X + screen.Width - Margin,
        ToolbarSide.Left => x >= screen.X + Margin,
        ToolbarSide.Bottom => y + h <= screen.Y + screen.Height - Margin,
        _ => y >= screen.Y + Margin,
    };

    private static (double X, double Y) InsideSelection(ToolbarSide side, Bounds s, double w, double h) => side switch
    {
        ToolbarSide.Right => (s.X + s.Width - Gap - w, CenterY(s, h)),
        ToolbarSide.Left => (s.X + Gap, CenterY(s, h)),
        ToolbarSide.Bottom => (CenterX(s, w), s.Y + s.Height - Gap - h),
        _ => (CenterX(s, w), s.Y + Gap),
    };

    private static (double X, double Y) AtScreenEdge(ToolbarSide side, Bounds s, double w, double h, Bounds screen) => side switch
    {
        ToolbarSide.Right => (screen.X + screen.Width - Margin - w, CenterY(s, h)),
        ToolbarSide.Left => (screen.X + Margin, CenterY(s, h)),
        ToolbarSide.Bottom => (CenterX(s, w), screen.Y + screen.Height - Margin - h),
        _ => (CenterX(s, w), screen.Y + Margin),
    };

    // The toolbar is centered on the selection along the side it sits on.
    private static double CenterX(Bounds s, double w) => s.X + (s.Width - w) / 2;

    private static double CenterY(Bounds s, double h) => s.Y + (s.Height - h) / 2;

    private static (double X, double Y) Clamp(double x, double y, double w, double h, Bounds screen)
    {
        var minX = screen.X + Margin;
        var minY = screen.Y + Margin;
        var maxX = Math.Max(minX, screen.X + screen.Width - Margin - w);
        var maxY = Math.Max(minY, screen.Y + screen.Height - Margin - h);
        return (Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY));
    }

    private static Bounds Scale(Bounds b, double factor) => new(b.X * factor, b.Y * factor, b.Width * factor, b.Height * factor);
}
