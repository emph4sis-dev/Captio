using Captio.Core.Annotations;
using Captio.Core.Layout;
using Captio.Core.Settings;

namespace Captio.Core.Tests;

public class ToolbarPlacementTests
{
    private static readonly Bounds Screen = new(0, 0, 1920, 1080);
    private const double VW = 44, VH = 460; // vertical toolbar
    private const double HW = 520, HH = 44; // horizontal toolbar

    private static void AssertOnScreen(ToolbarPosition p, double w, double h, Bounds screen)
    {
        Assert.InRange(p.X, screen.X, screen.X + screen.Width - w);
        Assert.InRange(p.Y, screen.Y, screen.Y + screen.Height - h);
    }

    [Theory]
    [InlineData(ToolbarSide.Right)]
    [InlineData(ToolbarSide.Left)]
    [InlineData(ToolbarSide.Top)]
    [InlineData(ToolbarSide.Bottom)]
    public void Uses_the_preferred_side_when_it_fits(ToolbarSide side)
    {
        var selection = new Bounds(700, 300, 500, 400);
        var (w, h) = ToolbarPlacement.IsVertical(side) ? (VW, VH) : (HW, HH);

        var p = ToolbarPlacement.Place(selection, w, h, Screen, side);

        Assert.Equal(side, p.Side);
        Assert.False(p.Inside);
        switch (side)
        {
            case ToolbarSide.Right: Assert.Equal(selection.X + selection.Width + ToolbarPlacement.Gap, p.X); break;
            case ToolbarSide.Left: Assert.Equal(selection.X - ToolbarPlacement.Gap - w, p.X); break;
            case ToolbarSide.Top: Assert.Equal(selection.Y - ToolbarPlacement.Gap - h, p.Y); break;
            case ToolbarSide.Bottom: Assert.Equal(selection.Y + selection.Height + ToolbarPlacement.Gap, p.Y); break;
        }
        AssertOnScreen(p, w, h, Screen);
    }

    [Theory]
    [InlineData(ToolbarSide.Right)]
    [InlineData(ToolbarSide.Left)]
    [InlineData(ToolbarSide.Top)]
    [InlineData(ToolbarSide.Bottom)]
    public void Toolbar_is_centered_on_the_selection(ToolbarSide side)
    {
        var selection = new Bounds(700, 300, 500, 600);
        var (w, h) = ToolbarPlacement.IsVertical(side) ? (VW, VH) : (HW, HH);

        var p = ToolbarPlacement.Place(selection, w, h, Screen, side);

        if (ToolbarPlacement.IsVertical(side)) Assert.Equal(selection.Y + selection.Height / 2, p.Y + h / 2, precision: 6);
        else Assert.Equal(selection.X + selection.Width / 2, p.X + w / 2, precision: 6);
    }

    [Fact]
    public void Centering_is_clamped_to_the_screen()
    {
        // A short selection at the top: the centered vertical bar would start above the screen.
        var p = ToolbarPlacement.Place(new Bounds(800, 10, 300, 60), VW, VH, Screen, ToolbarSide.Right);
        Assert.Equal(ToolbarPlacement.Margin, p.Y);
    }

    [Fact]
    public void Left_and_right_are_vertical_top_and_bottom_horizontal()
    {
        Assert.True(ToolbarPlacement.IsVertical(ToolbarSide.Right));
        Assert.True(ToolbarPlacement.IsVertical(ToolbarSide.Left));
        Assert.False(ToolbarPlacement.IsVertical(ToolbarSide.Top));
        Assert.False(ToolbarPlacement.IsVertical(ToolbarSide.Bottom));
    }

    [Fact]
    public void Falls_back_to_the_opposite_side()
    {
        var atRightEdge = new Bounds(1500, 300, 410, 300);
        var p = ToolbarPlacement.Place(atRightEdge, VW, VH, Screen, ToolbarSide.Right);
        Assert.Equal(ToolbarSide.Left, p.Side);
        Assert.False(p.Inside);

        var atBottom = new Bounds(400, 700, 600, 375);
        var q = ToolbarPlacement.Place(atBottom, HW, HH, Screen, ToolbarSide.Bottom);
        Assert.Equal(ToolbarSide.Top, q.Side);
    }

    [Fact]
    public void Full_screen_selection_puts_toolbar_inside_without_covering_center()
    {
        var full = new Bounds(0, 0, 1920, 1080);
        foreach (var side in Enum.GetValues<ToolbarSide>())
        {
            var (w, h) = ToolbarPlacement.IsVertical(side) ? (VW, VH) : (HW, HH);
            var p = ToolbarPlacement.Place(full, w, h, Screen, side);

            Assert.True(p.Inside);
            AssertOnScreen(p, w, h, Screen);
            var coversCenter = 960 >= p.X && 960 <= p.X + w && 540 >= p.Y && 540 <= p.Y + h;
            Assert.False(coversCenter);
        }
    }

    [Fact]
    public void Never_leaves_the_screen()
    {
        var random = new Random(42);
        for (var i = 0; i < 2000; i++)
        {
            var x = random.Next(0, 1900);
            var y = random.Next(0, 1060);
            var selection = new Bounds(x, y, random.Next(2, 1920 - x), random.Next(2, 1080 - y));
            var side = (ToolbarSide)random.Next(4);
            var (w, h) = ToolbarPlacement.IsVertical(side) ? (VW, VH) : (HW, HH);

            AssertOnScreen(ToolbarPlacement.Place(selection, w, h, Screen, side), w, h, Screen);
        }
    }

    [Fact]
    public void Monitor_offsets_are_respected()
    {
        var secondary = new Bounds(1920, -200, 1280, 1024);
        var p = ToolbarPlacement.Place(new Bounds(2900, -150, 290, 200), VW, VH, secondary, ToolbarSide.Right);
        AssertOnScreen(p, VW, VH, secondary);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Placement_is_consistent_across_dpi_scales(double scale)
    {
        // The same physical selection on monitors with different scaling.
        var selectionPx = new Bounds(1400 * scale, 200 * scale, 300 * scale, 300 * scale);
        var screenPx = new Bounds(0, 0, 1920 * scale, 1080 * scale);

        var p = ToolbarPlacement.PlaceOnMonitor(selectionPx, VW, VH, screenPx, ToolbarSide.Right, scale);

        Assert.Equal(ToolbarSide.Right, p.Side);
        Assert.Equal(1400 + 300 + ToolbarPlacement.Gap, p.X, precision: 6);
        Assert.Equal(200 + (300 - VH) / 2, p.Y, precision: 6); // centered on the selection in DIPs
    }

    [Fact]
    public void High_dpi_shrinks_room_in_dips_and_triggers_fallback()
    {
        // 3840 px at 200 % is 1920 DIPs; a selection near the right edge leaves no room.
        var p = ToolbarPlacement.PlaceOnMonitor(new Bounds(3500, 400, 320, 600), VW, VH, new Bounds(0, 0, 3840, 2160), ToolbarSide.Right, 2.0);
        Assert.Equal(ToolbarSide.Left, p.Side);
        AssertOnScreen(p, VW, VH, new Bounds(0, 0, 1920, 1080));
    }
}
