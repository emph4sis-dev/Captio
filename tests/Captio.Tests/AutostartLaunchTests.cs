using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Captio.Core.Startup;
using Xunit.Abstractions;

namespace Captio.Tests;

public class AutostartLaunchTests(ITestOutputHelper output)
{
    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    private static int VisibleWindows(int processId)
    {
        var count = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == processId && IsWindowVisible(hwnd)) count++;
            return true;
        }, 0);
        return count;
    }

    [Fact]
    public void Started_at_login_it_stays_in_the_tray_without_windows()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Captio.exe");
        Assert.True(File.Exists(exe), $"Captio.exe not found next to the tests: {exe}");

        using var process = Process.Start(new ProcessStartInfo(exe, StartupCommand.AutostartArgument) { UseShellExecute = false })!;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                Assert.Equal(0, VisibleWindows(process.Id));
                Thread.Sleep(200);
            }

            if (process.HasExited)
            {
                // Another Captio already owns the single-instance lock; it exits without any window.
                output.WriteLine("Another Captio instance is running; this launch exited immediately.");
                Assert.Equal(0, process.ExitCode);
            }
            else
            {
                Assert.Equal(0, VisibleWindows(process.Id));
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(); // only the process this test started
        }
    }
}
