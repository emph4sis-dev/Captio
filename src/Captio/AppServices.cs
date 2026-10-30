using Captio.Core.Library;
using Captio.Core.Settings;
using Captio.Core.Startup;
using Captio.Core.Storage;

namespace Captio;

/// <summary>State shared by the tray, capture sessions and windows.</summary>
internal sealed class AppServices
{
    private readonly SettingsStore _settingsStore;
    private readonly Action<string> _notify;
    private LibraryStore? _library;

    public AppServices(
        SettingsStore settingsStore, IKnownFolders knownFolders, ILaunchAtLogin launchAtLogin,
        string legacyLibraryPath, LibraryMigrator migrator, Action<string> notify)
    {
        _settingsStore = settingsStore;
        _notify = notify;
        Settings = settingsStore.Load();
        Folders = new CaptureFolderResolver(knownFolders);
        LaunchAtLogin = launchAtLogin;
        LegacyLibraryPath = legacyLibraryPath;
        Migrator = migrator;
    }

    public AppSettings Settings { get; private set; }

    public CaptureFolderResolver Folders { get; }

    public ILaunchAtLogin LaunchAtLogin { get; }

    public LibraryMigrator Migrator { get; }

    /// <summary>Private folder used by version 0.1; kept readable until the user moves its captures.</summary>
    public string LegacyLibraryPath { get; }

    public bool UsingLegacyLibrary => CaptureFolderResolver.SamePath(Settings.CaptureFolder, LegacyLibraryPath);

    public event EventHandler? SettingsChanged;

    /// <summary>Raised when library contents change or the capture folder moves.</summary>
    public event EventHandler? LibraryChanged;

    public void Notify(string message) => _notify(message);

    public void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        var previousFolder = Settings.CaptureFolder;
        Settings = change(Settings).Normalize();
        _settingsStore.Save(Settings);
        if (!string.Equals(previousFolder, Settings.CaptureFolder, StringComparison.OrdinalIgnoreCase)) ReplaceLibrary();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Where captures go right now, without creating anything.</summary>
    public FolderCheck CurrentFolder() => Folders.Resolve(Settings.CaptureFolder, ensureReady: false);

    /// <summary>The library for the current capture folder, or null when it cannot be resolved.</summary>
    public LibraryStore? Library
    {
        get
        {
            if (_library is null && CurrentFolder() is { Path: { } path })
            {
                _library = new LibraryStore(path);
                _library.Changed += (_, _) => LibraryChanged?.Invoke(this, EventArgs.Empty);
            }
            return _library;
        }
    }

    /// <summary>
    /// Saves a flattened PNG in the capture folder. Throws <see cref="CaptureFolderException"/>
    /// when the folder is unusable; there is no fallback location.
    /// </summary>
    public LibraryItem SaveCapture(byte[] png)
    {
        var check = Folders.Resolve(Settings.CaptureFolder, ensureReady: true);
        if (!check.Ok) throw new CaptureFolderException(check);
        // Writing the file never lists the folder, however many images it holds.
        if (Library is { } library && !CaptureFolderResolver.SamePath(library.Root, check.Path)) ReplaceLibrary();
        return Library!.AddImage(png, DateTimeOffset.Now);
    }

    private void ReplaceLibrary()
    {
        _library = null;
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }
}

