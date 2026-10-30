using System.Windows.Threading;

namespace Captio.Tests;

/// <summary>
/// One STA thread with the app's resources loaded, shared by every test that builds
/// real windows: WPF allows a single Application per process, bound to its thread.
/// </summary>
internal static class UiThread
{
    private static readonly Lazy<Dispatcher> Dispatcher = new(Start);

    public static void Run(Action work) => Dispatcher.Value.Invoke(work);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new App { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
