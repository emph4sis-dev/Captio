using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Captio.Core.Settings;
using Captio.Upload;

namespace Captio.SettingsUi;

/// <summary>Every change applies immediately; there is nothing to confirm or cancel.</summary>
public partial class SettingsWindow : Window
{
    private static readonly (ToolbarSide Side, string Label)[] Sides =
    [
        (ToolbarSide.Top, "Arriba"),
        (ToolbarSide.Bottom, "Abajo"),
        (ToolbarSide.Left, "Izquierda"),
        (ToolbarSide.Right, "Derecha"),
    ];

    private readonly AppServices _services;
    private readonly Func<bool> _reapplyHotkeys;
    private readonly Dictionary<ToolbarSide, RadioButton> _sideButtons = [];
    private bool _loading = true;

    internal SettingsWindow(AppServices services, Func<bool> reapplyHotkeys)
    {
        InitializeComponent();
        _services = services;
        _reapplyHotkeys = reapplyHotkeys;

        BuildOrientationChoices();
        AttachHotkeyCapture();

        var settings = services.Settings;
        ImageHotkeyBox.Text = HotkeyText.Format(settings.ImageHotkey);
        CopyAlsoSavesBox.IsChecked = settings.CopyAlsoSaves;
        (settings.EffectiveDefaultDestination switch
        {
            Destination.Save => EnterSave,
            Destination.Imgur => EnterImgur,
            _ => EnterCopy,
        }).IsChecked = true;
        _sideButtons[settings.ToolbarSide].IsChecked = true;

        RefreshLaunchAtLogin();
        RefreshFolder();
        RefreshImgur();
        _loading = false;

        services.SettingsChanged += OnSettingsChanged;
        services.LibraryChanged += OnSettingsChanged;
        Closed += (_, _) =>
        {
            services.SettingsChanged -= OnSettingsChanged;
            services.LibraryChanged -= OnSettingsChanged;
        };
        // Startup entries and folders can change outside Captio while this window is open.
        Activated += (_, _) =>
        {
            RefreshLaunchAtLogin();
            RefreshFolder();
        };
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() =>
    {
        RefreshFolder();
        RefreshImgur();
    });

    // ---- General ------------------------------------------------------------------------------

    private void RefreshLaunchAtLogin() => LaunchAtLoginBox.IsChecked = _services.LaunchAtLogin.IsEnabled;

    private void OnLaunchAtLoginClick(object sender, RoutedEventArgs e)
    {
        var requested = LaunchAtLoginBox.IsChecked == true;
        try
        {
            _services.LaunchAtLogin.SetEnabled(requested);
            _services.UpdateSettings(s => s with { LaunchAtLogin = requested });
            LaunchAtLoginNote.Text = "Captio se abrirá en la bandeja del sistema, sin mostrar ventanas.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            LaunchAtLoginNote.Text = "Windows no permitió cambiar el inicio automático: " + ex.Message;
        }
        // Show what the system really has, not what was requested.
        RefreshLaunchAtLogin();
    }

    private void AttachHotkeyCapture()
    {
        void Capture(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Tab)
                return;

            e.Handled = true;
            var modifiers = 0u;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= Hotkey.Control;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= Hotkey.Alt;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= Hotkey.Shift;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= Hotkey.Win;
            ApplyHotkey(new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)));
        }

        ImageHotkeyBox.PreviewKeyDown += (_, e) => Capture(e);
        // Print Screen only reports key-up to applications.
        ImageHotkeyBox.PreviewKeyUp += (_, e) =>
        {
            if (e.Key == Key.Snapshot) Capture(e);
        };
    }

    private void ApplyHotkey(Hotkey hotkey)
    {
        var previous = _services.Settings.ImageHotkey;
        if (hotkey == previous) return;

        _services.UpdateSettings(s => s with { ImageHotkey = hotkey });
        if (_reapplyHotkeys())
        {
            HotkeyNote.Text = $"Listo: {HotkeyText.Format(hotkey)} captura una imagen.";
        }
        else
        {
            _services.UpdateSettings(s => s with { ImageHotkey = previous });
            _reapplyHotkeys();
            HotkeyNote.Text = $"Otro programa ya usa {HotkeyText.Format(hotkey)}. Se mantiene {HotkeyText.Format(previous)}.";
        }
        ImageHotkeyBox.Text = HotkeyText.Format(_services.Settings.ImageHotkey);
    }

    // ---- Capture and saving -------------------------------------------------------------------

    private void OnEnterDestinationChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var destination = EnterSave.IsChecked == true ? Destination.Save
            : EnterImgur.IsChecked == true ? Destination.Imgur
            : Destination.Copy;
        _services.UpdateSettings(s => s with { DefaultDestination = destination });
    }

    private void OnCopyAlsoSavesClick(object sender, RoutedEventArgs e) =>
        _services.UpdateSettings(s => s with { CopyAlsoSaves = CopyAlsoSavesBox.IsChecked == true });

    private void RefreshFolder()
    {
        var check = _services.CurrentFolder();
        var path = check.Path ?? "(sin resolver)";
        FolderPath.Text = path;
        FolderPath.ToolTip = path;

        // Only an existence check here; write access is verified when choosing or saving.
        var ready = check.Ok && check.Path is not null && !Directory.Exists(check.Path)
            ? check with { Problem = Core.Storage.FolderProblem.NotFound }
            : check;
        FolderStatus.Visibility = ready.Ok ? Visibility.Collapsed : Visibility.Visible;
        FolderStatus.Text = ready.Problem == Core.Storage.FolderProblem.NotFound && _services.Settings.CaptureFolder is null
            ? "La carpeta aún no existe; Windows la creará al guardar la primera captura."
            : ready.Describe() + " Elige otra carpeta.";

        LegacyBanner.Visibility = _services.UsingLegacyLibrary ? Visibility.Visible : Visibility.Collapsed;
        ResetButton.IsEnabled = _services.Settings.CaptureFolder is not null;
        OpenFolderButton.IsEnabled = check.Path is not null && Directory.Exists(check.Path);
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        CaptureFolderFlow.Choose(_services, this);
        RefreshFolder();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        CaptureFolderFlow.Reset(_services, this);
        RefreshFolder();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (_services.CurrentFolder().Path is { } path && Directory.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void BuildOrientationChoices()
    {
        foreach (var (side, label) in Sides)
        {
            var radio = new RadioButton
            {
                GroupName = "Side",
                Margin = new Thickness(0, 0, 14, 0),
                ToolTip = side switch
                {
                    ToolbarSide.Top => "Barra horizontal arriba",
                    ToolbarSide.Bottom => "Barra horizontal abajo",
                    ToolbarSide.Left => "Barra vertical a la izquierda",
                    _ => "Barra vertical a la derecha",
                },
            };
            AutomationProperties.SetName(radio, $"Barra: {label}");

            var content = new StackPanel();
            content.Children.Add(Diagram(side, radio));
            content.Children.Add(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            radio.Content = content;
            radio.Checked += (_, _) =>
            {
                if (!_loading) _services.UpdateSettings(s => s with { ToolbarSide = side });
            };
            _sideButtons[side] = radio;
            OrientationPanel.Children.Add(radio);
        }
    }

    /// <summary>A selection with the toolbar drawn on the given side.</summary>
    private static Canvas Diagram(ToolbarSide side, Control owner)
    {
        var canvas = new Canvas { Width = 56, Height = 40 };
        var selection = new Rectangle { Width = 28, Height = 20, StrokeThickness = 1.2, StrokeDashArray = [3, 2] };
        selection.SetBinding(Shape.StrokeProperty, new Binding(nameof(Control.Foreground)) { Source = owner });
        Canvas.SetLeft(selection, 14);
        Canvas.SetTop(selection, 10);

        var (x, y, w, h) = side switch
        {
            ToolbarSide.Right => (45.0, 9.0, 5.0, 22.0),
            ToolbarSide.Left => (6.0, 9.0, 5.0, 22.0),
            ToolbarSide.Top => (16.0, 2.0, 24.0, 5.0),
            _ => (16.0, 33.0, 24.0, 5.0),
        };
        var bar = new Rectangle { Width = w, Height = h, RadiusX = 1.5, RadiusY = 1.5 };
        bar.SetBinding(Shape.FillProperty, new Binding(nameof(Control.Foreground)) { Source = owner });
        Canvas.SetLeft(bar, x);
        Canvas.SetTop(bar, y);

        canvas.Children.Add(selection);
        canvas.Children.Add(bar);
        return canvas;
    }

    // ---- Imgur --------------------------------------------------------------------------------

    private void RefreshImgur()
    {
        var available = _services.ImgurClientId is not null;
        var consent = _services.Settings.ImgurConsentAccepted;
        ImgurStatus.Text = !available
            ? "Esta compilación no incluye un Client-ID de Imgur, así que la subida está desactivada."
            : consent
                ? "Aceptaste el aviso de privacidad. Las subidas son anónimas y ocultas, no privadas: cualquiera con el enlace puede ver la imagen."
                : "Se te pedirá aceptar el aviso de privacidad antes de la primera subida.";
        RevokeButton.IsEnabled = consent;

        var wasLoading = _loading;
        _loading = true;
        EnterImgur.IsEnabled = available && consent;
        if (!EnterImgur.IsEnabled && EnterImgur.IsChecked == true) EnterCopy.IsChecked = true;
        _loading = wasLoading;
        EnterImgur.ToolTip = EnterImgur.IsEnabled ? null : "Disponible después de aceptar el aviso de privacidad de Imgur.";
        ToolTipService.SetShowOnDisabled(EnterImgur, true);
    }

    private void OnShowNotice(object sender, RoutedEventArgs e) => new ConsentDialog(readOnly: true) { Owner = this }.ShowDialog();

    private void OnRevoke(object sender, RoutedEventArgs e) =>
        _services.UpdateSettings(s => s with
        {
            ImgurConsentAccepted = false,
            DefaultDestination = s.DefaultDestination == Destination.Imgur ? Destination.Copy : s.DefaultDestination,
        });

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
