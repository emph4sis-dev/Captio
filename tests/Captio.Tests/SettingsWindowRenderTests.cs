using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captio.Core.Library;
using Captio.Core.Settings;
using Captio.Core.Startup;
using Captio.Core.Storage;
using Captio.SettingsUi;

namespace Captio.Tests;

/// <summary>
/// Builds the real Settings window with the app's theme and renders it to PNG.
/// Catches XAML and resource errors, and leaves the image for visual review when
/// CAPTIO_RENDER_DIR is set.
/// </summary>
[Trait("Category", "Desktop")]
public sealed class SettingsWindowRenderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "captio-render-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FixedFolder(string path) : IKnownFolders
    {
        public string? GetScreenshotsFolder(bool create)
        {
            if (create) Directory.CreateDirectory(path);
            return path;
        }
    }

    private sealed class FakeLaunch : ILaunchAtLogin
    {
        public bool IsEnabled { get; private set; }

        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    [Fact]
    public void Settings_window_renders_without_errors()
    {
        var screenshots = Directory.CreateDirectory(Path.Combine(_root, "Screenshots")).FullName;
        for (var i = 0; i < 3; i++) File.WriteAllBytes(Path.Combine(screenshots, $"Captura {i}.png"), []);

        (int Width, int Height) size = default;
        UiThread.Run(() =>
        {
            var services = new AppServices(
                new SettingsStore(Path.Combine(_root, "settings.json")), new FixedFolder(screenshots), new FakeLaunch(),
                Path.Combine(_root, "legacy"), new LibraryMigrator(Path.Combine(_root, "migration.json")), _ => { });

            var window = new SettingsWindow(services, () => true)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000,
                Top = -20000,
                ShowActivated = false,
            };
            window.Show();
            window.UpdateLayout();
            size = Render(window, Environment.GetEnvironmentVariable("CAPTIO_RENDER_DIR"));
            window.Close();
        });

        Assert.True(size.Width > 400 && size.Height > 300, $"rendered {size.Width}x{size.Height}");
    }

    private static (int, int) Render(Window window, string? outputDir)
    {
        var content = (FrameworkElement)window.Content;
        // Lay out explicitly: on a shared UI thread the deferred layout pass may not have run yet.
        content.Measure(new Size(window.Width, double.PositiveInfinity));
        content.Arrange(new Rect(content.DesiredSize));
        var width = (int)Math.Ceiling(content.ActualWidth);
        var height = (int)Math.Ceiling(content.ActualHeight);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // The real window shows the system backdrop; paint the theme's base color in its place.
            var ground = window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
            dc.DrawRectangle(ground, null, new Rect(0, 0, width, height));

        }
        var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Render(content); // directly, so margins are kept

        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(outputDir, "settings.png"));
            encoder.Save(file);
        }
        return (width, height);
    }
}
