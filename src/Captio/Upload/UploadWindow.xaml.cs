using System.Diagnostics;
using System.IO;
using System.Windows;
using Captio.Capture;
using Captio.Core.Imgur;

namespace Captio.Upload;

/// <summary>
/// Uploads one flattened capture held in memory. Closing the window discards the
/// image and the link; nothing about the upload is persisted.
/// </summary>
public partial class UploadWindow : Window
{
    private readonly AppServices _services;
    private FlattenedImage? _image;
    private Uri? _link;
    private CancellationTokenSource? _cancellation;

    internal UploadWindow(AppServices services, FlattenedImage image)
    {
        InitializeComponent();
        _services = services;
        _image = image;
        PreviewImage.Source = image.Bitmap;
        Loaded += async (_, _) => await UploadAsync();
    }

    private async Task UploadAsync()
    {
        if (_image is null || _services.ImgurClientId is not { } clientId) return;

        ShowPanel(UploadingPanel);
        _cancellation = new CancellationTokenSource();
        UploadResult result;
        using (var uploader = new ImgurUploader(clientId))
        {
            result = await uploader.UploadAsync(_image.Png, _cancellation.Token);
        }
        _cancellation.Dispose();
        _cancellation = null;
        if (!IsLoaded) return;

        switch (result)
        {
            case UploadSucceeded success:
                _link = success.Link;
                LinkBox.Text = success.Link.ToString();
                DiscardImage();
                ShowPanel(DonePanel);
                break;
            case UploadFailed failure:
                ErrorText.Text = Describe(failure);
                ShowPanel(FailedPanel);
                break;
        }
    }

    private static string Describe(UploadFailed failure) => failure.Kind switch
    {
        UploadFailureKind.Network => "No hay conexión con Imgur. Comprueba tu red.",
        UploadFailureKind.Timeout => "Imgur tardó demasiado en responder.",
        UploadFailureKind.RateLimited => "Imgur ha limitado temporalmente las subidas." + Wait(failure.RetryAfter),
        UploadFailureKind.ServerError => "Imgur tiene problemas en este momento." + Wait(failure.RetryAfter),
        UploadFailureKind.Rejected => "Imgur rechazó la imagen.",
        UploadFailureKind.Cancelled => "Subida cancelada.",
        _ => "Error desconocido.",
    };

    private static string Wait(TimeSpan? retryAfter) => retryAfter is { } wait
        ? $" Podrás intentarlo de nuevo en unos {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} min."
        : " Inténtalo más tarde.";

    private void ShowPanel(FrameworkElement panel)
    {
        UploadingPanel.Visibility = panel == UploadingPanel ? Visibility.Visible : Visibility.Collapsed;
        DonePanel.Visibility = panel == DonePanel ? Visibility.Visible : Visibility.Collapsed;
        FailedPanel.Visibility = panel == FailedPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCancelUpload(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private async void OnRetry(object sender, RoutedEventArgs e) => await UploadAsync();

    private void OnCopyLink(object sender, RoutedEventArgs e)
    {
        if (_link is not null) Clipboard.SetText(_link.ToString());
    }

    private void OnOpenLink(object sender, RoutedEventArgs e)
    {
        if (_link is not null) Process.Start(new ProcessStartInfo(_link.ToString()) { UseShellExecute = true });
    }

    private void OnCopyImage(object sender, RoutedEventArgs e)
    {
        if (_image is null) return;
        ImageComposer.CopyToClipboard(_image);
        Close();
    }

    private void OnSaveImage(object sender, RoutedEventArgs e)
    {
        if (_image is null) return;
        try
        {
            _services.Library.AddImage(_image.Png, DateTimeOffset.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "No se pudo guardar la captura: " + ex.Message, "Captio", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _services.Notify("Captura guardada en la biblioteca.");
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void DiscardImage()
    {
        _image?.Wipe();
        _image = null;
        PreviewImage.Source = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancellation?.Cancel();
        DiscardImage();
        _link = null;
        LinkBox.Text = string.Empty;
        base.OnClosed(e);
    }
}
