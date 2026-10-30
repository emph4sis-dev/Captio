using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Captio.Interop;

/// <summary>
/// Captio spends almost all its time idle in the tray. After a capture or a window
/// closes, hand the large screenshot buffers and the working set back to Windows.
/// </summary>
internal static partial class MemoryRelief
{
    /// <summary>Runs once the UI has nothing else to do, so it never delays closing a window.</summary>
    public static void ReleaseWhenIdle(Dispatcher dispatcher) =>
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, Release);

    public static void Release()
    {
        // Screenshots live on the large object heap; compact it so the memory is returned, not just reusable.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        // WPF bitmaps free their native pixels in finalizers.
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Pages that are trimmed stay in RAM as standby and come back quickly on the next capture.
        SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);
}
