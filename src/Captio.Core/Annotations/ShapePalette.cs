namespace Captio.Core.Annotations;

public sealed record PaletteColor(uint Argb, string Name);

/// <summary>Eight fully saturated hues at the same HSL lightness (50 %).</summary>
public static class ShapePalette
{
    public static readonly IReadOnlyList<PaletteColor> Colors =
    [
        new(0xFFFF0000, "Rojo"),
        new(0xFFFF8000, "Naranja"),
        new(0xFFFFFF00, "Amarillo"),
        new(0xFF00FF00, "Verde"),
        new(0xFF00FFFF, "Cian"),
        new(0xFF0000FF, "Azul"),
        new(0xFF8000FF, "Violeta"),
        new(0xFFFF00FF, "Magenta"),
    ];

    public static uint Default => Colors[0].Argb;

    public static PaletteColor? Find(uint argb) => Colors.FirstOrDefault(c => c.Argb == argb);

    /// <summary>Maps any color (for example one saved by an older version) to the closest palette hue.</summary>
    public static uint Nearest(uint argb)
    {
        if (Find(argb) is not null) return argb;
        var hue = Hue(argb);
        if (hue is null) return Default;
        return Colors.MinBy(c => HueDistance(hue.Value, Hue(c.Argb)!.Value))!.Argb;
    }

    private static double HueDistance(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>Hue in degrees, or null for grays.</summary>
    internal static double? Hue(uint argb)
    {
        double r = (byte)(argb >> 16) / 255.0, g = (byte)(argb >> 8) / 255.0, b = (byte)argb / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta < 0.02) return null;

        double hue;
        if (max == r) hue = 60 * ((g - b) / delta % 6);
        else if (max == g) hue = 60 * ((b - r) / delta + 2);
        else hue = 60 * ((r - g) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }
}
