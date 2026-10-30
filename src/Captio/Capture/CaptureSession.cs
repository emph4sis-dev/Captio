using System.IO;
using System.Windows;
using Captio.Core.Settings;
using Captio.Upload;

namespace Captio.Capture;

/// <summary>
/// One capture from hotkey to destination. Owns an overlay per monitor; nothing is
/// written anywhere unless the user picks Save.
/// </summary>
internal sealed class CaptureSession(AppServices services)
{
    private readonly List<OverlayWindow> _windows = [];

    public event EventHandler? Ended;

    public bool ImgurAvailable => services.ImgurClientId is not null;

    public Destination DefaultDestination => services.Settings.EffectiveDefaultDestination;

    public void Start()
    {
        var snapshots = ScreenCapture.CaptureAllMonitors();
        var cursor = System.Windows.Forms.Cursor.Position;
        OverlayWindow? underCursor = null;

        foreach (var snapshot in snapshots)
        {
            var window = new OverlayWindow(this, snapshot, services.Settings);
            _windows.Add(window);
            window.Show();
            var b = snapshot.Bounds;
            if (cursor.X >= b.X && cursor.X < b.X + b.Width && cursor.Y >= b.Y && cursor.Y < b.Y + b.Height) underCursor = window;
        }

        var focus = underCursor ?? _windows.FirstOrDefault();
        focus?.Activate();
        focus?.Focus();
    }

    public void OnSelectionStarted(OverlayWindow source)
    {
        foreach (var window in _windows.Where(w => w != source)) window.ResetSelection();
        source.Activate();
    }

    public void Cancel() => End();

    public void Deliver(OverlayWindow source, Destination destination)
    {
        if (source.Region is not { } region) return;
        if (destination == Destination.Imgur && (!ImgurAvailable || !services.EnsureImgurConsent(source))) return;

        var image = ImageComposer.Flatten(source.Snapshot.Image, region, source.Document.Items);
        RememberArrowStyle(source);

        switch (destination)
        {
            case Destination.Copy:
                ImageComposer.CopyToClipboard(image);
                image.Wipe();
                End();
                break;

            case Destination.Save:
                try
                {
                    services.Library.AddImage(image.Png, DateTimeOffset.Now);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    MessageBox.Show(source, "No se pudo guardar la captura: " + e.Message, "Captio", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                finally
                {
                    image.Wipe();
                }
                services.Notify("Captura guardada en la biblioteca.");
                End();
                break;

            case Destination.Imgur:
                End();
                new UploadWindow(services, image).Show();
                break;
        }
    }

    private void RememberArrowStyle(OverlayWindow source)
    {
        var settings = services.Settings;
        if (settings.ArrowArgb != source.ArrowColor || settings.ArrowThickness != source.ArrowThickness)
            services.UpdateSettings(s => s with { ArrowArgb = source.ArrowColor, ArrowThickness = source.ArrowThickness });
    }

    private void End()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        Ended?.Invoke(this, EventArgs.Empty);
    }
}
