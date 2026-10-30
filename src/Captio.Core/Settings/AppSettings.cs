using System.Text.Json;
using System.Text.Json.Serialization;

namespace Captio.Core.Settings;

public enum Destination
{
    Copy,
    Save,
    Imgur,
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
    public Hotkey ImageHotkey { get; init; } = new(0, Hotkey.PrintScreen);

    public Hotkey ClipHotkey { get; init; } = new(Hotkey.Shift, Hotkey.PrintScreen);

    public Destination DefaultDestination { get; init; } = Destination.Copy;

    public bool ImgurConsentAccepted { get; init; }

    public uint ArrowArgb { get; init; } = 0xFFE5484D;

    public double ArrowThickness { get; init; } = 6;

    /// <summary>The destination Enter triggers. Imgur is never used without prior consent.</summary>
    public Destination EffectiveDefaultDestination =>
        DefaultDestination == Destination.Imgur && !ImgurConsentAccepted ? Destination.Copy : DefaultDestination;
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    public AppSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tempPath = Path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, Path, overwrite: true);
    }
}
