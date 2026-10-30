using Captio.Core.Annotations;
using Captio.Core.Settings;

namespace Captio.Core.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = TempDir.Create();

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose() => TempDir.Delete(_dir);

    [Fact]
    public void A_removed_destination_falls_back_to_copy_and_keeps_the_rest()
    {
        // Written by a version that still had an upload destination.
        File.WriteAllText(SettingsPath, """
        { "DefaultDestination": "Imgur", "ImgurConsentAccepted": true, "CopyAlsoSaves": true, "ToolbarSide": "Left" }
        """);
        var store = new SettingsStore(SettingsPath);

        var s = store.Load();
        Assert.Equal(Destination.Copy, s.DefaultDestination);
        Assert.True(s.CopyAlsoSaves);
        Assert.Equal(ToolbarSide.Left, s.ToolbarSide);

        store.Save(s);
        Assert.DoesNotContain("Imgur", File.ReadAllText(SettingsPath));
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
