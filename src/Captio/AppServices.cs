using System.Reflection;
using System.Windows;
using Captio.Core.Library;
using Captio.Core.Settings;
using Captio.Upload;

namespace Captio;

/// <summary>State shared by the tray, capture sessions and windows.</summary>
internal sealed class AppServices(SettingsStore settingsStore, LibraryStore library, Action<string> notify)
{
    public AppSettings Settings { get; private set; } = settingsStore.Load();

    public LibraryStore Library { get; } = library;

    public string SettingsPath => settingsStore.Path;

    /// <summary>Public Client-ID baked in at build time; null when the build has none.</summary>
    public string? ImgurClientId { get; } = ResolveImgurClientId();

    public event EventHandler? SettingsChanged;

    public void Notify(string message) => notify(message);

    public void UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        Settings = change(Settings);
        settingsStore.Save(Settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the Imgur notice until the user accepts it once.</summary>
    public bool EnsureImgurConsent(Window? owner)
    {
        if (Settings.ImgurConsentAccepted) return true;

        var dialog = new ConsentDialog(readOnly: false);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        if (dialog.ShowDialog() != true) return false;

        UpdateSettings(s => s with { ImgurConsentAccepted = true });
        return true;
    }

    private static string? ResolveImgurClientId()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("CAPTIO_IMGUR_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim();

        var baked = typeof(AppServices).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "ImgurClientId")?.Value;
        return string.IsNullOrWhiteSpace(baked) ? null : baked.Trim();
    }
}
