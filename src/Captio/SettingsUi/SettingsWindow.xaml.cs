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
using System.Windows.Threading;
using Captio.Core.Library;
using Captio.Core.Settings;
using Captio.Core.Storage;

namespace Captio.SettingsUi;

/// <summary>Every change applies immediately and the footer confirms it; there is nothing to save or cancel.</summary>
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
    private readonly DispatcherTimer _savedStamp = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private bool _loading = true;
    private bool _recordingHotkey;

    internal SettingsWindow(AppServices services, Func<bool> reapplyHotkeys)
    {
        InitializeComponent();
        _services = services;
        _reapplyHotkeys = reapplyHotkeys;

        VersionText.Text = "Captio " + typeof(SettingsWindow).Assembly.GetName().Version?.ToString(2);
        // Fit the content; scroll only when the screen is too short for it.
        MaxHeight = SystemParameters.WorkArea.Height - 48;
        BuildSideTiles();

        var settings = services.Settings;
        HotkeyButton.Content = HotkeyText.Format(settings.ImageHotkey);
        CopyAlsoSavesBox.IsChecked = settings.CopyAlsoSaves;
        (settings.DefaultDestination == Destination.Save ? EnterSave : EnterCopy).IsChecked = true;
        _sideButtons[settings.ToolbarSide].IsChecked = true;

        RefreshLaunchAtLogin();
        RefreshFolder();
        _loading = false;

        _savedStamp.Tick += (_, _) => ShowSavedStamp(false);
        services.LibraryChanged += OnLibraryChanged;
        Closed += (_, _) =>
        {
            services.LibraryChanged -= OnLibraryChanged;
            _savedStamp.Stop();
        };
        // Startup entries and folders can change outside Captio while this window is open.
        Activated += (_, _) =>
        {
            RefreshLaunchAtLogin();
            RefreshFolder();
        };
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    private void OnLibraryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshFolder);

    private void Update(Func<AppSettings, AppSettings> change)
    {
        _services.UpdateSettings(change);
        ShowSavedStamp(true);
    }

    private void ShowSavedStamp(bool saved)
    {
        _savedStamp.Stop();
        SavedGlyph.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
        SavedText.Text = saved ? "Guardado" : "Los cambios se aplican al momento.";
        if (saved) _savedStamp.Start();
    }

    // ---- Hotkey ------------------------------------------------------------------------------

    private void OnHotkeyClick(object sender, RoutedEventArgs e)
    {
        _recordingHotkey = !_recordingHotkey;
        HotkeyButton.Content = _recordingHotkey ? "Pulsa la combinación…" : HotkeyText.Format(_services.Settings.ImageHotkey);
        HotkeyDescription.Text = _recordingHotkey ? "Esc cancela." : "Congela la pantalla para elegir una zona.";
        if (_recordingHotkey) HotkeyButton.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recordingHotkey) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }
        Capture(key);
    }

    // Print Screen only reports key-up to applications.
    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_recordingHotkey && e.Key == Key.Snapshot)
        {
            e.Handled = true;
            Capture(Key.Snapshot);
        }
    }

    private void Capture(Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var modifiers = 0u;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= Hotkey.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= Hotkey.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= Hotkey.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= Hotkey.Win;
        ApplyHotkey(new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)));
        StopRecording();
    }

    private void StopRecording()
    {
        _recordingHotkey = false;
        HotkeyButton.Content = HotkeyText.Format(_services.Settings.ImageHotkey);
        HotkeyDescription.Text = "Congela la pantalla para elegir una zona.";
    }

    private void ApplyHotkey(Hotkey hotkey)
    {
        var previous = _services.Settings.ImageHotkey;
        HotkeyWarning.Visibility = Visibility.Collapsed;
        if (hotkey == previous) return;

        Update(s => s with { ImageHotkey = hotkey });
        if (_reapplyHotkeys()) return;

        _services.UpdateSettings(s => s with { ImageHotkey = previous });
        _reapplyHotkeys();
        ShowSavedStamp(false);
        HotkeyWarning.Text = hotkey.VirtualKey == Hotkey.PrintScreen
            ? $"Windows u otro programa ya usa {HotkeyText.Format(hotkey)}. Desactiva en Windows «Usar la tecla Imprimir pantalla para abrir la captura de pantalla» o elige otra."
            : $"Otro programa ya usa {HotkeyText.Format(hotkey)}. Se mantiene {HotkeyText.Format(previous)}.";
        HotkeyWarning.Visibility = Visibility.Visible;
    }

    // ---- Capture ------------------------------------------------------------------------------

    private void OnEnterDestinationChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var destination = EnterSave.IsChecked == true ? Destination.Save : Destination.Copy;
        Update(s => s with { DefaultDestination = destination });
    }

    private void BuildSideTiles()
    {
        foreach (var (side, label) in Sides)
        {
            var tile = new RadioButton { GroupName = "Side", Style = (Style)FindResource("SideTile"), ToolTip = $"Barra {label.ToLowerInvariant()}" };
            AutomationProperties.SetName(tile, $"Barra de edición: {label}");

            var content = new StackPanel();
            content.Children.Add(Diagram(side, tile));
            content.Children.Add(new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
            tile.Content = content;
            tile.Checked += (_, _) =>
            {
                if (!_loading) Update(s => s with { ToolbarSide = side });
            };
            _sideButtons[side] = tile;
            OrientationPanel.Children.Add(tile);
        }
    }

    /// <summary>A selection with Captio's toolbar on the given side, its buttons drawn as dots.</summary>
    private static Canvas Diagram(ToolbarSide side, Control owner)
    {
        var canvas = new Canvas { Width = 48, Height = 34 };
        var ink = new Binding(nameof(Control.Foreground)) { Source = owner };

        var selection = new Rectangle { Width = 24, Height = 18, StrokeThickness = 1.2, StrokeDashArray = [2.5, 1.5] };
        selection.SetBinding(Shape.StrokeProperty, ink);
        Canvas.SetLeft(selection, 12);
        Canvas.SetTop(selection, 8);
        canvas.Children.Add(selection);

        var vertical = side is ToolbarSide.Left or ToolbarSide.Right;
        var (x, y, w, h) = side switch
        {
            ToolbarSide.Right => (39.0, 6.0, 6.0, 22.0),
            ToolbarSide.Left => (3.0, 6.0, 6.0, 22.0),
            ToolbarSide.Top => (10.0, 0.5, 28.0, 6.0),
            _ => (10.0, 27.5, 28.0, 6.0),
        };
        var bar = new Rectangle { Width = w, Height = h, RadiusX = 2, RadiusY = 2 };
        bar.SetBinding(Shape.FillProperty, ink);
        Canvas.SetLeft(bar, x);
        Canvas.SetTop(bar, y);
        canvas.Children.Add(bar);

        for (var i = 0; i < 4; i++)
        {
            var dot = new Ellipse { Width = 2, Height = 2 };
            dot.SetResourceReference(Shape.FillProperty, "CardBackgroundFillColorDefaultBrush");
            Canvas.SetLeft(dot, vertical ? x + 2 : x + 4 + i * 6);
            Canvas.SetTop(dot, vertical ? y + 3 + i * 5 : y + 2);
            canvas.Children.Add(dot);
        }
        return canvas;
    }

    // ---- Saving -------------------------------------------------------------------------------

    private void OnCopyAlsoSavesClick(object sender, RoutedEventArgs e) =>
        Update(s => s with { CopyAlsoSaves = CopyAlsoSavesBox.IsChecked == true });

    private void RefreshFolder()
    {
        var check = _services.CurrentFolder();
        var exists = check.Path is not null && Directory.Exists(check.Path);
        var count = exists ? LibraryMigrator.CountCaptures(check.Path!) : 0;

        // The count leads so it survives when a long path is trimmed.
        FolderPath.Text = check.Path is null ? "Sin resolver" : $"{count:N0} {(count == 1 ? "imagen" : "imágenes")} en {check.Path}";
        FolderPath.ToolTip = check.Path;
        OpenFolderButton.IsEnabled = exists;

        // Only an existence check here; write access is verified when choosing or saving.
        var problem = check.Ok && check.Path is not null && !exists ? FolderProblem.NotFound : check.Problem;
        // A missing Windows folder is normal (it is created on the first save); anything else is a warning.
        var pendingCreation = problem == FolderProblem.NotFound && _services.Settings.CaptureFolder is null;
        FolderStatus.Visibility = problem == FolderProblem.None ? Visibility.Collapsed : Visibility.Visible;
        FolderStatus.Text = pendingCreation
            ? "Aún no existe; Windows la creará al guardar la primera captura."
            : (check with { Problem = problem }).Describe();
        FolderStatus.SetResourceReference(TextBlock.ForegroundProperty,
            pendingCreation ? "TextFillColorSecondaryBrush" : "SystemFillColorCautionBrush");

        // The reset row only exists while the folder differs from the Windows one.
        var custom = _services.Settings.CaptureFolder is not null;
        ResetRow.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        ResetText.Text = _services.UsingLegacyLibrary
            ? "Tus capturas siguen en la carpeta de la versión anterior."
            : "Usas una carpeta personalizada.";
        ResetButton.Content = _services.UsingLegacyLibrary ? "Moverlas a la de Windows" : "Volver a la de Windows";
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        if (CaptureFolderFlow.Choose(_services, this)) ShowSavedStamp(true);
        RefreshFolder();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (CaptureFolderFlow.Reset(_services, this)) ShowSavedStamp(true);
        RefreshFolder();
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (_services.CurrentFolder().Path is { } path && Directory.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // ---- System -------------------------------------------------------------------------------

    private void RefreshLaunchAtLogin() => LaunchAtLoginBox.IsChecked = _services.LaunchAtLogin.IsEnabled;

    private void OnLaunchAtLoginClick(object sender, RoutedEventArgs e)
    {
        var requested = LaunchAtLoginBox.IsChecked == true;
        LaunchAtLoginWarning.Visibility = Visibility.Collapsed;
        try
        {
            _services.LaunchAtLogin.SetEnabled(requested);
            Update(s => s with { LaunchAtLogin = requested });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            LaunchAtLoginWarning.Text = "Windows no permitió cambiarlo: " + ex.Message;
            LaunchAtLoginWarning.Visibility = Visibility.Visible;
        }
        // Show what the system really has, not what was requested.
        RefreshLaunchAtLogin();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
