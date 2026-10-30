using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captio.Core.Annotations;
using Captio.Core.Imaging;

namespace Captio.Capture;

/// <summary>A flattened, redacted capture held only in memory.</summary>
internal sealed class FlattenedImage(BitmapSource bitmap, byte[] png)
{
    public BitmapSource Bitmap { get; } = bitmap;

    /// <summary>Metadata-free PNG bytes.</summary>
    public byte[] Png { get; } = png;

    public void Wipe() => Array.Clear(Png);
}

internal static class ImageComposer
{
    /// <summary>
    /// Renders the region with its arrows, then overwrites redacted pixels with black.
    /// The returned image has no layers; the covered content is gone.
    /// </summary>
    public static FlattenedImage Flatten(BitmapSource monitorImage, Int32Rect region, IReadOnlyList<Annotation> annotations)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(new CroppedBitmap(monitorImage, region), new Rect(0, 0, region.Width, region.Height));
            dc.PushTransform(new TranslateTransform(-region.X, -region.Y));
            foreach (var arrow in annotations.OfType<ArrowAnnotation>()) AnnotationRenderer.Draw(dc, arrow);
            dc.Pop();
        }

        var target = new RenderTargetBitmap(region.Width, region.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var stride = region.Width * 4;
        var pixels = new byte[stride * region.Height];
        target.CopyPixels(pixels, stride, 0);
        target.Clear();

        Redactor.Apply(pixels, region.Width, region.Height, stride,
            annotations.OfType<RedactionAnnotation>(), region.X, region.Y);

        var bitmap = BitmapSource.Create(region.Width, region.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        Array.Clear(pixels);
        return new FlattenedImage(bitmap, EncodePng(bitmap));
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var raw = stream.GetBuffer().AsSpan(0, (int)stream.Length);
        var clean = PngSanitizer.Sanitize(raw);
        raw.Clear();
        return clean;
    }

    public static void CopyToClipboard(FlattenedImage image)
    {
        var data = new DataObject();
        data.SetImage(image.Bitmap);
        data.SetData("PNG", new MemoryStream(image.Png, writable: false));

        // Another process may hold the clipboard open for a moment.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (COMException) when (attempt < 5)
            {
                Thread.Sleep(40);
            }
        }
    }
}
