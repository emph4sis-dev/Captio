using System.Text.Json;
using System.Text.Json.Serialization;
using Captio.Core.Annotations;

namespace Captio.Core.Settings;

public enum Destination
{
    Copy,
    Save,
    Imgur,
}

/// <summary>Preferred side of the selection for the editing toolbar.</summary>
public enum ToolbarSide
{
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>A global hotkey as Win32 MOD_* flags plus a virtual-key code.</summary>
public sealed record Hotkey(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 0x1;
    public const uint Control = 0x2;
    public const uint Shift = 0x4;
    public const uint Win = 0x8;

    public const uint PrintScreen = 0x2C;
}

public sealed record AppSettings
{
    /// <summary>Stroke width in device-independent pixels for arrows and outlines.</summary>
    public const double DefaultStrokeThickness = 6;

    /// <summary>
    /// The thickness picker is hidden; it may come back in an Advanced section.
    /// Until then every stroke uses <see cref="DefaultStrokeThickness"/>.
    /// </summary>
    public const bool StrokeThicknessCustomizable = false;

    public Hotkey ImageHotkey { get; init; } = new(0, Hotkey.PrintScreen);

    /// <summary>Reserved for clip recording, which is not available yet; never registered.</summary>
    public Hotkey ClipHotkey { get; init; } = new(Hotkey.Shift, Hotkey.PrintScreen);

    public Destination DefaultDestination { get; init; } = Destination.Copy;

    public bool ImgurConsentAccepted { get; init; }

    /// <summary>Last state the user asked for. The system's Run entry is authoritative.</summary>
    public bool LaunchAtLogin { get; init; }

    /// <summary>Also save the copied PNG to the capture folder.</summary>
    public bool CopyAlsoSaves { get; init; }

    /// <summary>Absolute folder for saved captures; null follows the Windows Screenshots known folder.</summary>
    public string? CaptureFolder { get; init; }

    public ToolbarSide ToolbarSide { get; init; } = ToolbarSide.Right;

    public ShapeKind LastShape { get; init; } = ShapeKind.Arrow;

    /// <summary>Color for arrows and outlines. The name predates rectangles and ellipses.</summary>
    public uint ArrowArgb { get; init; } = ShapePalette.Default;

    /// <summary>Kept for compatibility and a future Advanced section; see <see cref="StrokeThickness"/>.</summary>
    public double ArrowThickness { get; init; } = DefaultStrokeThickness;

    /// <summary>The destination Enter triggers. Imgur is never used without prior consent.</summary>
    [JsonIgnore]
    public Destination EffectiveDefaultDestination =>
        DefaultDestination == Destination.Imgur && !ImgurConsentAccepted ? Destination.Copy : DefaultDestination;

    [JsonIgnore]
    public double StrokeThickness => StrokeThicknessCustomizable ? ArrowThickness : DefaultStrokeThickness;

    /// <summary>Replaces out-of-range or unsafe values with defaults.</summary>
    public AppSettings Normalize()
    {
        var defaults = new AppSettings();
        return this with
        {
            ImageHotkey = ImageHotkey is { VirtualKey: > 0 } ? ImageHotkey : defaults.ImageHotkey,
            ClipHotkey = ClipHotkey is { VirtualKey: > 0 } ? ClipHotkey : defaults.ClipHotkey,
            DefaultDestination = Enum.IsDefined(DefaultDestination) ? DefaultDestination : defaults.DefaultDestination,
            CaptureFolder = NormalizeFolder(CaptureFolder),
            ToolbarSide = Enum.IsDefined(ToolbarSide) ? ToolbarSide : defaults.ToolbarSide,
            LastShape = Enum.IsDefined(LastShape) ? LastShape : defaults.LastShape,
            ArrowArgb = ShapePalette.Nearest(ArrowArgb),
            ArrowThickness = double.IsFinite(ArrowThickness) && ArrowThickness > 0 ? ArrowThickness : defaults.ArrowThickness,
        };
    }

    /// <summary>Only fully qualified paths are kept; anything else falls back to the known folder.</summary>
    public static string? NormalizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        try
        {
            return Path.IsPathFullyQualified(folder) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    /// <summary>Never throws: a missing or corrupt file yields defaults.</summary>
    public AppSettings Load()
    {
        try
        {
            var loaded = File.Exists(Path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions)
                : null;
            return (loaded ?? new AppSettings()).Normalize();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Writes to a temporary file first so a crash never leaves a half-written file.</summary>
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tempPath = Path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings.Normalize(), JsonOptions));
        File.Move(tempPath, Path, overwrite: true);
    }
}
