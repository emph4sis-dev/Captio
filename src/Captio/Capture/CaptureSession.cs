using System.Windows;
using Captio.Core.Annotations;
using Captio.Core.Delivery;
using Captio.Core.Settings;
using Captio.Core.Storage;

namespace Captio.Capture;

/// <summary>
/// One capture from hotkey to destination. Owns an overlay per monitor; nothing is
/// written anywhere unless the user saves (or asked to keep a copy when copying).
/// </summary>
internal sealed class CaptureSession(AppServices services)
{
    private readonly List<OverlayWindow> _windows = [];

    public event EventHandler? Ended;

    public Destination DefaultDestination => services.Settings.DefaultDestination;

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

    /// <summary>Remembered immediately so the next capture and the next start reuse it.</summary>
    public void RememberColor(uint argb) => services.UpdateSettings(s => s with { ArrowArgb = argb });

    public void RememberShape(ShapeKind shape) => services.UpdateSettings(s => s with { LastShape = shape });

    public void Cancel() => End();

    public void Deliver(OverlayWindow source, Destination destination)
    {
        if (source.Region is not { } region) return;

        var image = ImageComposer.Flatten(source.Snapshot.Image, region, source.Document.Items);
        switch (destination)
        {
            case Destination.Copy:
                Copy(source, image);
                break;
            case Destination.Save:
                Save(source, image);
                break;
        }
    }

    private void Copy(OverlayWindow source, FlattenedImage image)
    {
        var outcome = CopyWorkflow.Run(image.Png, services.Settings.CopyAlsoSaves,
            copy: _ => ImageComposer.CopyToClipboard(image),
            save: services.SaveCapture);
        image.Wipe();

        switch (outcome)
        {
            case CopyFailed failed:
                MessageBox.Show(source, "No se pudo copiar la imagen al portapapeles: " + failed.Error.Message,
                    "Captio", MessageBoxButton.OK, MessageBoxImage.Error);
                return; // the editor stays open
            case CopiedButSaveFailed failed:
                services.Notify("La imagen se copió, pero no se pudo guardar la copia: " + failed.Error.Message);
                break;
        }
        End();
    }

    private void Save(OverlayWindow source, FlattenedImage image)
    {
        while (true)
        {
            try
            {
                services.SaveCapture(image.Png);
                image.Wipe();
                services.Notify("Captura guardada.");
                End();
                return;
            }
            catch (CaptureFolderException e)
            {
                var answer = MessageBox.Show(source, e.Message + "\n\n¿Quieres elegir otra carpeta de capturas?", "Captio",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes || !CaptureFolderFlow.Choose(services, source)) break;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(source, "No se pudo guardar la captura: " + e.Message, "Captio", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
            }
        }
        // Not saved: the editor stays open with the capture intact.
        image.Wipe();
    }

    private void End()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        Ended?.Invoke(this, EventArgs.Empty);
    }
}
