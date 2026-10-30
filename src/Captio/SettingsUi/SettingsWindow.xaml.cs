using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Captio.Core.Settings;
using Captio.Upload;

namespace Captio.SettingsUi;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly Action _reapplyHotkeys;
    private Hotkey _imageHotkey;
    private Hotkey _clipHotkey;
    private bool _consent;

    internal SettingsWindow(AppServices services, Action reapplyHotkeys)
    {
        InitializeComponent();
        _services = services;
        _reapplyHotkeys = reapplyHotkeys;

        var settings = services.Settings;
        _imageHotkey = settings.ImageHotkey;
        _clipHotkey = settings.ClipHotkey;
        _consent = settings.ImgurConsentAccepted;

        ImageHotkeyBox.Text = HotkeyText.Format(_imageHotkey);
        ClipHotkeyBox.Text = HotkeyText.Format(_clipHotkey);
        AttachHotkeyCapture(ImageHotkeyBox, h => _imageHotkey = h);
        AttachHotkeyCapture(ClipHotkeyBox, h => _clipHotkey = h);

        (settings.EffectiveDefaultDestination switch
        {
            Destination.Save => EnterSave,
            Destination.Imgur => EnterImgur,
            _ => EnterCopy,
        }).IsChecked = true;

        LibraryPath.Text = services.Library.Root;
        UpdateImgurState();
    }

    private void UpdateImgurState()
    {
        var available = _services.ImgurClientId is not null;
        ImgurStatus.Text = !available
            ? "Esta compilación no incluye un Client-ID de Imgur, así que la subida está desactivada."
            : _consent
                ? "Aceptaste el aviso de privacidad. Las subidas son anónimas y ocultas, no privadas."
                : "Se te pedirá aceptar el aviso de privacidad antes de la primera subida.";
        EnterImgur.IsEnabled = available && _consent;
        if (!EnterImgur.IsEnabled && EnterImgur.IsChecked == true) EnterCopy.IsChecked = true;
        RevokeButton.IsEnabled = _consent;
    }

    private static void AttachHotkeyCapture(TextBox box, Action<Hotkey> assign)
    {
        void Capture(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;

            e.Handled = true;
            var modifiers = 0u;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= Hotkey.Control;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= Hotkey.Alt;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= Hotkey.Shift;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= Hotkey.Win;

            var hotkey = new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
            assign(hotkey);
            box.Text = HotkeyText.Format(hotkey);
        }

        box.PreviewKeyDown += (_, e) => Capture(e);
        // Print Screen only reports key-up to applications.
        box.PreviewKeyUp += (_, e) =>
        {
            if (e.Key == Key.Snapshot) Capture(e);
        };
    }

    private void OnShowNotice(object sender, RoutedEventArgs e) => new ConsentDialog(readOnly: true) { Owner = this }.ShowDialog();

    private void OnRevoke(object sender, RoutedEventArgs e)
    {
        _consent = false;
        UpdateImgurState();
    }

    private void OnOpenLibraryFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_services.Library.Root) { UseShellExecute = true });

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_imageHotkey == _clipHotkey)
        {
            MessageBox.Show(this, "Los dos atajos no pueden ser iguales.", "Captio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var destination = EnterSave.IsChecked == true ? Destination.Save
            : EnterImgur.IsChecked == true ? Destination.Imgur
            : Destination.Copy;
        _services.UpdateSettings(s => s with
        {
            ImageHotkey = _imageHotkey,
            ClipHotkey = _clipHotkey,
            DefaultDestination = destination,
            ImgurConsentAccepted = _consent,
        });
        _reapplyHotkeys();
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
