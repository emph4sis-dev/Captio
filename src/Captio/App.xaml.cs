using System.IO;
using System.Windows;
using Captio.Capture;
using Captio.Core.Library;
using Captio.Core.Settings;
using Captio.Core.Startup;
using Captio.Core.Storage;
using Captio.Interop;
using Captio.LibraryUi;
using Captio.SettingsUi;

namespace Captio;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkeys;
    private AppServices? _services;
    private CaptureSession? _activeCapture;
    private LibraryWindow? _libraryWindow;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Captio.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Captio");
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Captio");
        var legacyLibrary = LegacyLibrary.DefaultPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        _tray = new TrayIcon(StartCapture, ShowLibrary, ShowSettings, Shutdown);
        _services = new AppServices(
            new SettingsStore(Path.Combine(roaming, "settings.json")),
            new WindowsKnownFolders(),
            new RunKeyLaunchAtLogin(new WindowsStartupRegistry(), Environment.ProcessPath!),
            legacyLibrary,
            new LibraryMigrator(Path.Combine(local, "migration.json")),
            _tray.Notify);

        FinishInterruptedMigration();
        KeepLegacyLibraryReachable();

        _hotkeys = new HotkeyManager();
        RegisterHotkeys();
        // Started with --autostart or by hand, Captio only lives in the tray: no window opens.
        MemoryRelief.ReleaseWhenIdle(Dispatcher);
    }

    /// <summary>Completes or undoes a capture move cut short by a crash or shutdown.</summary>
    private void FinishInterruptedMigration()
    {
        var (outcome, destination) = _services!.Migrator.Recover();
        if (outcome != RecoveryOutcome.Completed || destination is null) return;

        var configured = CaptureFolderResolver.SamePath(destination, _services.Folders.DefaultFolder) ? null : destination;
        _services.UpdateSettings(s => s with { CaptureFolder = configured });
    }

    /// <summary>
    /// An install from version 0.1 kept captures in a private folder. Those stay visible
    /// until the user chooses to move them; a fresh install uses the Windows Screenshots folder.
    /// </summary>
    private void KeepLegacyLibraryReachable()
    {
        if (_services!.Settings.CaptureFolder is null && LegacyLibrary.HasCaptures(_services.LegacyLibraryPath))
            _services.UpdateSettings(s => s with { CaptureFolder = _services.LegacyLibraryPath });
    }

    /// <returns>false when another program already owns the capture hotkey.</returns>
    internal bool RegisterHotkeys(bool notify = true)
    {
        _hotkeys!.UnregisterAll();
        var hotkey = _services!.Settings.ImageHotkey;
        // Clip recording is not available yet, so its hotkey is deliberately not registered.
        if (_hotkeys.TryRegister(hotkey, StartCapture)) return true;

        if (notify) _services.Notify($"Otro programa ya usa {HotkeyText.Format(hotkey)}. Cámbialo en Configuración.");
        return false;
    }

    private void StartCapture()
    {
        if (_activeCapture is not null) return;
        _activeCapture = new CaptureSession(_services!);
        _activeCapture.Ended += (_, _) =>
        {
            _activeCapture = null;
            MemoryRelief.ReleaseWhenIdle(Dispatcher);
        };
        _activeCapture.Start();
    }

    private void ShowLibrary()
    {
        if (_libraryWindow is null)
        {
            _libraryWindow = new LibraryWindow(_services!, ShowSettings);
            _libraryWindow.Closed += (_, _) =>
            {
                _libraryWindow = null;
                MemoryRelief.ReleaseWhenIdle(Dispatcher);
            };
        }
        _libraryWindow.Show();
        _libraryWindow.Activate();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_services!, () => RegisterHotkeys(notify: false));
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                MemoryRelief.ReleaseWhenIdle(Dispatcher);
            };
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
