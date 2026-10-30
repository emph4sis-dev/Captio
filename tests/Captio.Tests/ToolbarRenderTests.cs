using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captio.Capture;
using Captio.Core.Library;
using Captio.Core.Settings;
using Captio.Core.Startup;
using Captio.Core.Storage;

namespace Captio.Tests;

/// <summary>
/// Builds the real editing toolbar in both orientations and renders it to PNG.
/// Catches resource errors and leaves the images for review when CAPTIO_RENDER_DIR is set.
/// </summary>
[Trait("Category", "Desktop")]
public sealed class ToolbarRenderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "captio-toolbar-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FixedFolder(string path) : IKnownFolders
    {
        public string? GetScreenshotsFolder(bool create) => path;
    }

    private sealed class NoLaunch : ILaunchAtLogin
    {
        public bool IsEnabled => false;

        public void SetEnabled(bool enabled) { }
    }

    [Fact]
    public void Toolbar_renders_in_both_orientations()
    {
        Directory.CreateDirectory(_root);
        var sizes = new List<(double Width, double Height)>();
        UiThread.Run(() =>
        {
            var services = new AppServices(new SettingsStore(Path.Combine(_root, "settings.json")), new FixedFolder(_root),
                new NoLaunch(), Path.Combine(_root, "legacy"), new LibraryMigrator(Path.Combine(_root, "m.json")), _ => { });
            var session = new CaptureSession(services);

            var pixels = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[64], 16);
            pixels.Freeze();
            var snapshot = new MonitorSnapshot(new Int32Rect(0, 0, 4, 4), pixels);

            foreach (var side in new[] { ToolbarSide.Right, ToolbarSide.Top })
            {
                var window = new OverlayWindow(session, snapshot, new AppSettings { ToolbarSide = side });
                var toolbar = window.Toolbar;
                toolbar.Visibility = Visibility.Visible;
                toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                toolbar.Arrange(new Rect(new Point(8, 8), toolbar.DesiredSize));
                sizes.Add((toolbar.DesiredSize.Width, toolbar.DesiredSize.Height));
                Save(toolbar, Environment.GetEnvironmentVariable("CAPTIO_RENDER_DIR"), $"toolbar-{side}.png".ToLowerInvariant());
                window.Close();
            }
        });

        var (vw, vh) = sizes[0];
        var (hw, hh) = sizes[1];
        Assert.True(vh > vw * 5, "the right-side toolbar is vertical");
        Assert.True(hw > hh * 5, "the top toolbar is horizontal");
        Assert.InRange(vw, 30, 44); // narrow: about 38 DIP
    }

    private static void Save(FrameworkElement element, string? outputDir, string name)
    {
        if (string.IsNullOrEmpty(outputDir)) return;
        var width = (int)Math.Ceiling(element.ActualWidth) + 16;
        var height = (int)Math.Ceiling(element.ActualHeight) + 16;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x58)), null, new Rect(0, 0, width, height));
        }
        var bitmap = new RenderTargetBitmap(width * 3, height * 3, 288, 288, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Render(element); // arranged at (8, 8), so its shadow stays inside
        Directory.CreateDirectory(outputDir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(outputDir, name));
        encoder.Save(file);
    }
}
