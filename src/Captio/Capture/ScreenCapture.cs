using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Captio.Interop;

namespace Captio.Capture;

/// <param name="Bounds">Monitor rectangle in physical virtual-screen pixels.</param>
internal sealed record MonitorSnapshot(Int32Rect Bounds, BitmapSource Image);

internal static class ScreenCapture
{
    public static IReadOnlyList<Int32Rect> GetMonitorBounds()
    {
        var bounds = new List<Int32Rect>();
        Native.EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref Native.RECT _, nint _) =>
        {
            var info = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (Native.GetMonitorInfo(monitor, ref info))
            {
                var r = info.rcMonitor;
                bounds.Add(new Int32Rect(r.Left, r.Top, r.Width, r.Height));
            }
            return true;
        }, 0);
        return bounds;
    }

    /// <summary>Freezes every monitor at once so the overlays show a consistent moment.</summary>
    public static IReadOnlyList<MonitorSnapshot> CaptureAllMonitors() =>
        GetMonitorBounds().Select(b => new MonitorSnapshot(b, Capture(b))).ToList();

    private static BitmapSource Capture(Int32Rect bounds)
    {
        var screenDc = Native.GetDC(0);
        var memoryDc = Native.CreateCompatibleDC(screenDc);
        var bitmap = Native.CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
        var previous = Native.SelectObject(memoryDc, bitmap);
        try
        {
            Native.BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.X, bounds.Y, Native.SRCCOPY | Native.CAPTUREBLT);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            Native.SelectObject(memoryDc, previous);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memoryDc);
            Native.ReleaseDC(0, screenDc);
        }
    }
}
