using System.IO;
using System.Windows;
using Captio.Capture;
using Captio.Core.Library;
using Captio.Core.Settings;
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

    internal AppServices Services => _services!;

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

        _tray = new TrayIcon(StartCapture, StartClip, ShowLibrary, ShowSettings, Shutdown);
        _services = new AppServices(
            new SettingsStore(Path.Combine(roaming, "settings.json")),
            new LibraryStore(Path.Combine(local, "Library")),
            _tray.Notify);
        _hotkeys = new HotkeyManager();
        RegisterHotkeys();
    }

    internal void RegisterHotkeys()
    {
        _hotkeys!.UnregisterAll();
        var settings = _services!.Settings;
        var taken = new List<string>();
        if (!_hotkeys.TryRegister(settings.ImageHotkey, StartCapture)) taken.Add(HotkeyText.Format(settings.ImageHotkey));
        if (!_hotkeys.TryRegister(settings.ClipHotkey, StartClip)) taken.Add(HotkeyText.Format(settings.ClipHotkey));

        if (taken.Count > 0)
            _services.Notify($"Otro programa ya usa {string.Join(" y ", taken)}. Cámbialo en Configuración.");
    }

    private void StartCapture()
    {
        if (_activeCapture is not null) return;
        _activeCapture = new CaptureSession(_services!);
        _activeCapture.Ended += (_, _) => _activeCapture = null;
        _activeCapture.Start();
    }

    private void StartClip() => _services!.Notify("La grabación de clips todavía no está disponible en esta versión.");

    private void ShowLibrary()
    {
        if (_libraryWindow is null)
        {
            _libraryWindow = new LibraryWindow(_services!);
            _libraryWindow.Closed += (_, _) => _libraryWindow = null;
        }
        _libraryWindow.Show();
        _libraryWindow.Activate();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_services!, RegisterHotkeys);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
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
