using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captio.Capture;
using Captio.Core.Annotations;
using Captio.Core.Library;
using Captio.Core.Settings;
using Captio.Core.Startup;
using Captio.Core.Storage;

namespace Captio.Tests;

/// <summary>
/// Renders the images used by the README and the website from the real editor, over a synthetic
/// demo screen: no real desktop, files or personal data. Always renders (catching regressions);
/// writes the PNGs only when CAPTIO_DOCS_IMAGES_DIR is set, for example:
///   $env:CAPTIO_DOCS_IMAGES_DIR = "website/assets"; dotnet test tests/Captio.Tests --filter DocumentationImages
/// </summary>
[Trait("Category", "Desktop")]
public sealed class DocumentationImagesTests : IDisposable
{
    private const int Width = 1600, Height = 900;
    private static readonly Typeface Regular = new("Segoe UI");
    private static readonly Typeface Semibold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "captio-docs-" + Guid.NewGuid().ToString("N"));

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
    public void Documentation_images_render_from_the_real_editor()
    {
        Directory.CreateDirectory(_root);
        var output = Environment.GetEnvironmentVariable("CAPTIO_DOCS_IMAGES_DIR");
        var sizes = new Dictionary<string, (int, int)>();

        UiThread.Run(() =>
        {
            var services = new AppServices(new SettingsStore(Path.Combine(_root, "settings.json")), new FixedFolder(_root),
                new NoLaunch(), Path.Combine(_root, "legacy"), new LibraryMigrator(Path.Combine(_root, "m.json")), _ => { });
            var session = new CaptureSession(services);
            var snapshot = new MonitorSnapshot(new Int32Rect(0, 0, Width, Height), DemoScreen());

            var window = new OverlayWindow(session, snapshot, new AppSettings());
            const uint red = 0xFFFF0000;
            window.Document.Add(new RectangleAnnotation(640, 560, 520, 64, red, 6));
            window.Document.Add(new ArrowAnnotation(1110, 724, 1010, 636, red, 6));
            window.Document.Add(new LineAnnotation(326, 300, 760, 300, red, 6));
            window.Document.Add(new RedactionAnnotation(496, 668, 196, 34));
            window.ShowRegionForPreview(new Int32Rect(290, 170, 900, 590));

            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(Width, Height));
            root.Arrange(new Rect(0, 0, Width, Height));
            root.UpdateLayout();
            var editor = Render(root, Width, Height);
            sizes["editor"] = (editor.PixelWidth, editor.PixelHeight);
            Save(editor, output, "editor.png");

            var social = SocialImage(editor);
            sizes["social"] = (social.PixelWidth, social.PixelHeight);
            Save(social, output, "social-preview.png");
            window.Close();
        });

        Assert.Equal((Width, Height), sizes["editor"]);
        Assert.Equal((1280, 640), sizes["social"]);
    }

    /// <summary>A neutral order summary on a plain desktop: invented, generic content.</summary>
    private static BitmapSource DemoScreen()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush(0x2B, 0x2F, 0x36), null, new Rect(0, 0, Width, Height));
            var window = new Rect(250, 110, 1100, 700);
            dc.DrawRoundedRectangle(Brushes.White, null, window, 10, 10);
            dc.DrawRoundedRectangle(Brush(0xF1, 0xF3, 0xF6), null, new Rect(window.X, window.Y, window.Width, 44), 10, 10);
            dc.DrawRectangle(Brush(0xF1, 0xF3, 0xF6), null, new Rect(window.X, window.Y + 30, window.Width, 14));
            Text(dc, "Resumen del pedido", 15, Regular, Brush(0x4A, 0x50, 0x5C), 274, 122);

            var ink = Brush(0x14, 0x16, 0x1B);
            var muted = Brush(0x5B, 0x62, 0x6E);
            Text(dc, "Pedido 4821", 40, Semibold, ink, 326, 240);
            Text(dc, "Material de oficina · 3 artículos", 18, Regular, muted, 328, 316);

            var rows = new[] { ("Cuaderno A5", "2", "9,80 €"), ("Bolígrafos, caja de 12", "1", "14,60 €"), ("Archivador de anillas", "4", "24,50 €") };
            var y = 380.0;
            Text(dc, "Artículo", 15, Semibold, muted, 330, y);
            Text(dc, "Cant.", 15, Semibold, muted, 820, y);
            Text(dc, "Importe", 15, Semibold, muted, 990, y);
            foreach (var (item, qty, amount) in rows)
            {
                y += 46;
                dc.DrawRectangle(Brush(0xE6, 0xE9, 0xEE), null, new Rect(326, y - 12, 760, 1));
                Text(dc, item, 18, Regular, ink, 330, y);
                Text(dc, qty, 18, Regular, ink, 830, y);
                Text(dc, amount, 18, Regular, ink, 990, y);
            }
            dc.DrawRectangle(Brush(0xE6, 0xE9, 0xEE), null, new Rect(326, 568, 760, 1));
            Text(dc, "Total", 22, Semibold, ink, 660, 578);
            Text(dc, "48,90 €", 22, Semibold, ink, 990, 578);
            Text(dc, "Correo del cliente:", 18, Regular, muted, 330, 674);
            Text(dc, "cliente@ejemplo.com", 18, Regular, ink, 500, 674);
        }
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>1280×640 card for link previews: the mark, the name, one line, and the real editor.</summary>
    private static BitmapSource SocialImage(BitmapSource editor)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush(0x0F, 0x11, 0x15), null, new Rect(0, 0, 1280, 640));
            DrawMark(dc, new Rect(72, 92, 72, 72));
            Text(dc, "Captio", 64, Semibold, Brush(0xEC, 0xEE, 0xF2), 164, 88);
            Text(dc, "Captura, señala y censura", 30, Regular, Brush(0xEC, 0xEE, 0xF2), 72, 214);
            Text(dc, "en Windows.", 30, Regular, Brush(0xEC, 0xEE, 0xF2), 72, 254);
            Text(dc, "Código abierto · Sin cuentas · Sin conexión", 20, Regular, Brush(0xA3, 0xA9, 0xB5), 72, 520);

            // The editor, cropped to the selection and toolbar, on the right.
            var crop = new CroppedBitmap(editor, new Int32Rect(250, 120, 1040, 680));
            var frame = new Rect(600, 70, 640, 640 * 680 / 1040.0);
            dc.PushClip(new RectangleGeometry(frame, 12, 12));
            dc.DrawImage(crop, frame);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(Brush(0x2A, 0x2F, 0x38), 1), frame, 12, 12);
        }
        var bitmap = new RenderTargetBitmap(1280, 640, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The app icon: viewfinder corners and a dot on the indigo tile.</summary>
    private static void DrawMark(DrawingContext dc, Rect r)
    {
        var s = r.Width / 32;
        dc.DrawRoundedRectangle(Brush(0x4F, 0x5B, 0xD5), null, r, 6 * s, 6 * s);
        var pen = new Pen(Brushes.White, 3 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        Polyline(dc, pen, r, s, (8, 14), (8, 8), (14, 8));
        Polyline(dc, pen, r, s, (18, 24), (24, 24), (24, 18));
        dc.DrawEllipse(Brushes.White, null, new Point(r.X + 16 * s, r.Y + 16 * s), 2 * s, 2 * s);
    }

    private static void Polyline(DrawingContext dc, Pen pen, Rect r, double s, params (double X, double Y)[] points)
    {
        for (var i = 1; i < points.Length; i++)
            dc.DrawLine(pen, new Point(r.X + points[i - 1].X * s, r.Y + points[i - 1].Y * s), new Point(r.X + points[i].X * s, r.Y + points[i].Y * s));
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static void Text(DrawingContext dc, string text, double size, Typeface face, Brush brush, double x, double y) =>
        dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("es-ES"), FlowDirection.LeftToRight, face, size, brush, 1), new Point(x, y));

    private static BitmapSource Render(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string? outputDir, string name)
    {
        if (string.IsNullOrEmpty(outputDir)) return;
        Directory.CreateDirectory(outputDir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(outputDir, name));
        encoder.Save(file);
    }
}
