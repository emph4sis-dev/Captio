using Captio.Core.Annotations;
using Captio.Core.Settings;

namespace Captio.Core.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = TempDir.Create();

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose() => TempDir.Delete(_dir);

    [Fact]
    public void Imgur_default_requires_consent()
    {
        var settings = new AppSettings { DefaultDestination = Destination.Imgur };
        Assert.Equal(Destination.Copy, settings.EffectiveDefaultDestination);

        Assert.Equal(Destination.Imgur, (settings with { ImgurConsentAccepted = true }).EffectiveDefaultDestination);
    }

    [Fact]
    public void New_install_defaults_are_safe()
    {
        var s = new SettingsStore(SettingsPath).Load();

        Assert.False(s.LaunchAtLogin);
        Assert.False(s.CopyAlsoSaves);
        Assert.Null(s.CaptureFolder); // Windows Screenshots folder
        Assert.Equal(ToolbarSide.Right, s.ToolbarSide);
        Assert.Equal(ShapeKind.Arrow, s.LastShape);
        Assert.Equal(ShapePalette.Default, s.ArrowArgb);
        Assert.False(s.ImgurConsentAccepted);
        Assert.Equal(Destination.Copy, s.DefaultDestination);
    }

    [Fact]
    public void All_preferences_round_trip()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "Capturas")).FullName;
        var store = new SettingsStore(SettingsPath);
        var saved = new AppSettings
        {
            DefaultDestination = Destination.Save,
            LaunchAtLogin = true,
            CopyAlsoSaves = true,
            CaptureFolder = folder,
            ToolbarSide = ToolbarSide.Top,
            LastShape = ShapeKind.Ellipse,
            ArrowArgb = 0xFF8000FF,
            ArrowThickness = 9,
        };

        store.Save(saved);

        Assert.Equal(saved, store.Load());
    }

    [Fact]
    public void Settings_from_version_0_1_load_with_new_defaults()
    {
        File.WriteAllText(SettingsPath, """
        {
          "ImageHotkey": { "Modifiers": 0, "VirtualKey": 44 },
          "ClipHotkey": { "Modifiers": 4, "VirtualKey": 44 },
          "DefaultDestination": "Save",
          "ImgurConsentAccepted": true,
          "ArrowArgb": 3846719565,
          "ArrowThickness": 10
        }
        """);

        var s = new SettingsStore(SettingsPath).Load();

        Assert.Equal(Destination.Save, s.DefaultDestination);
        Assert.True(s.ImgurConsentAccepted);
        Assert.Equal(0xFFFF0000u, s.ArrowArgb); // old #E5484D snaps to palette red
        Assert.Equal(10, s.ArrowThickness);      // kept for a future Advanced section
        Assert.Equal(AppSettings.DefaultStrokeThickness, s.StrokeThickness); // but strokes stay fixed
        Assert.Equal(ToolbarSide.Right, s.ToolbarSide);
        Assert.Equal(ShapeKind.Arrow, s.LastShape);
        Assert.Null(s.CaptureFolder);
        Assert.False(s.CopyAlsoSaves);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("""{ "ToolbarSide": "Diagonal" }""")]
    [InlineData("""{ "ArrowThickness": "grueso" }""")]
    [InlineData("")]
    public void Corrupt_settings_fall_back_to_defaults(string content)
    {
        File.WriteAllText(SettingsPath, content);
        Assert.Equal(new AppSettings(), new SettingsStore(SettingsPath).Load());
    }

    [Fact]
    public void Out_of_range_values_are_normalized()
    {
        File.WriteAllText(SettingsPath, """
        { "ToolbarSide": 42, "LastShape": 9, "ArrowThickness": -3, "CaptureFolder": "relativa\\carpeta" }
        """);

        var s = new SettingsStore(SettingsPath).Load();

        Assert.Equal(ToolbarSide.Right, s.ToolbarSide);
        Assert.Equal(ShapeKind.Arrow, s.LastShape);
        Assert.Equal(AppSettings.DefaultStrokeThickness, s.ArrowThickness);
        Assert.Null(s.CaptureFolder); // relative paths are never kept
    }

    [Fact]
    public void Save_is_atomic_and_never_writes_relative_paths()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(new AppSettings { CaptureFolder = "relativa" });

        Assert.DoesNotContain("relativa", File.ReadAllText(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Settings_never_contain_upload_data()
    {
        var names = typeof(AppSettings).GetProperties().Select(p => p.Name.ToLowerInvariant());
        Assert.DoesNotContain(names, n => n.Contains("link") || n.Contains("url") || n.Contains("deletehash") || n.Contains("upload"));
    }
}
