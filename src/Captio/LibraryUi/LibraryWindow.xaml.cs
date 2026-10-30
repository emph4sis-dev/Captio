using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Captio.Core.Library;
using Microsoft.Win32;

namespace Captio.LibraryUi;

/// <summary>One tile. Thumbnail and pixel size load in the background on first display.</summary>
internal sealed class LibraryEntry(LibraryItem item, string path) : INotifyPropertyChanged
{
    // A few decodes at a time keeps the disk and CPU responsive in folders with thousands of images.
    private static readonly SemaphoreSlim Decoders = new(3);

    private ImageSource? _thumbnail;
    private (int Width, int Height)? _size = item.Width is { } w && item.Height is { } h ? (w, h) : null;
    private bool _loading;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LibraryItem Item { get; } = item;

    public string Path { get; } = path;

    public string Name => Item.DisplayName;

    public string Details => $"{Item.CreatedAt.ToLocalTime():g}" + (_size is { } s ? $" · {s.Width} × {s.Height}" : "");

    public Visibility VideoGlyphVisibility => Item.Kind == LibraryItemKind.Video ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnail is null && !_loading && Item.Kind == LibraryItemKind.Image)
            {
                _loading = true;
                _ = LoadAsync();
            }
            return _thumbnail;
        }
    }

    private async Task LoadAsync()
    {
        await Decoders.WaitAsync();
        try
        {
            var (thumbnail, size) = await Task.Run(() => (Decode(Path), _size ?? LibraryStore.TryReadSize(Path)));
            _thumbnail = thumbnail;
            _size = size;
        }
        finally
        {
            Decoders.Release();
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details)));
    }

    private static BitmapImage? Decode(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 240;
            image.CacheOption = BitmapCacheOption.OnLoad; // do not keep the file locked
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

public sealed class NotNullConverter : IValueConverter
{
    public static readonly NotNullConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Shows the capture folder, newest first, a page at a time. Files added or removed
/// outside Captio appear when the window is activated.
/// </summary>
public partial class LibraryWindow : Window
{
    private const int PageSize = 60;

    private readonly AppServices _services;
    private readonly Action _showSettings;
    private readonly Dictionary<string, LibraryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private int _visible = PageSize;
    private bool _refreshing;
    private Point _dragOrigin;

    internal LibraryWindow(AppServices services, Action showSettings)
    {
        InitializeComponent();
        _services = services;
        _showSettings = showSettings;
        _services.LibraryChanged += OnLibraryChanged;
        Closed += (_, _) => _services.LibraryChanged -= OnLibraryChanged;
        Activated += async (_, _) => await RefreshFromDiskAsync();
        Reload();
    }

    private LibraryEntry? Selected => Items.SelectedItem as LibraryEntry;

    private void OnLibraryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Reload);

    private async Task RefreshFromDiskAsync()
    {
        if (_refreshing || _services.Library is not { } library) return;
        _refreshing = true;
        try
        {
            await library.RefreshAsync();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || Items is null) return;
        _visible = PageSize;
        Reload();
    }

    private void OnShowMore(object sender, RoutedEventArgs e)
    {
        _visible += PageSize;
        Reload();
    }

    private void Reload()
    {
        var folder = _services.CurrentFolder();
        FolderPath.Text = folder.Path ?? "Carpeta de capturas sin resolver";
        FolderPath.ToolTip = folder.Path;
        OpenFolderButton.IsEnabled = folder.Path is not null && Directory.Exists(folder.Path);

        var library = _services.Library;
        if (library is not null && !library.Loaded)
        {
            EmptyState.Text = "Cargando…";
            EmptyState.Visibility = Visibility.Visible;
            Items.ItemsSource = null;
            _ = RefreshFromDiskAsync();
            return;
        }

        var selectedName = Selected?.Item.FileName;
        var now = DateTimeOffset.Now;
        DateTimeOffset? since = PeriodFilter.SelectedIndex switch
        {
            1 => new DateTimeOffset(now.Date, now.Offset),
            2 => now.AddDays(-7),
            3 => now.AddDays(-30),
            _ => null,
        };

        var all = library?.Items ?? [];
        var matching = all.Where(i => since is null || i.CreatedAt >= since).ToList();
        var page = matching.Take(_visible).Select(i => EntryFor(library!, i)).ToList();

        Items.ItemsSource = page;
        Items.SelectedItem = page.FirstOrDefault(e => e.Item.FileName == selectedName);
        EmptyState.Visibility = page.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Text = library is null ? folder.Describe() + " Elige una carpeta en Configuración."
            : all.Count == 0 ? "Aún no hay capturas en esta carpeta. En el editor, pulsa Ctrl+S para guardar una aquí."
            : "Ningún elemento coincide con el filtro.";
        CountText.Text = matching.Count == 0 ? "" : $"Mostrando {page.Count} de {matching.Count}";
        ShowMoreButton.Visibility = matching.Count > page.Count ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Reuses tiles so thumbnails are not decoded again after every change.</summary>
    private LibraryEntry EntryFor(LibraryStore library, LibraryItem item)
    {
        var key = item.FileName + "|" + item.CreatedAt.UtcTicks;
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new LibraryEntry(item, library.GetPath(item));
            _entries[key] = entry;
        }
        return entry;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (_services.CurrentFolder().Path is { } path && Directory.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OnChangeFolder(object sender, RoutedEventArgs e) => _showSettings();

    private void OnOpen(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } entry && File.Exists(entry.Path))
            Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        try
        {
            var data = new DataObject();
            data.SetFileDropList(new StringCollection { entry.Path });
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(entry.Path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            data.SetImage(bitmap);
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "No se pudo copiar: " + ex.Message, "Captio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        var dialog = new SaveFileDialog { FileName = entry.Item.FileName, Filter = "Imagen PNG|*.png" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.Copy(entry.Path, dialog.FileName, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "No se pudo exportar: " + ex.Message, "Captio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry || _services.Library is not { } library) return;
        var dialog = new InputDialog("Renombrar", "Nombre del archivo", entry.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            library.Rename(entry.Item, dialog.Value);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "No se pudo renombrar: " + ex.Message, "Captio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry || _services.Library is not { } library) return;
        var answer = MessageBox.Show(this,
            $"«{entry.Name}» se borrará definitivamente. No irá a la papelera.",
            "Eliminar", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        try
        {
            library.Delete(entry.Item);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "No se pudo eliminar: " + ex.Message, "Captio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnItemsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) OnDelete(sender, e);
        else if (e.Key == Key.F2) OnRename(sender, e);
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) OnCopy(sender, e);
    }

    private void OnItemsMouseDown(object sender, MouseButtonEventArgs e) => _dragOrigin = e.GetPosition(Items);

    private void OnItemsMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Selected is not { } entry) return;
        var delta = e.GetPosition(Items) - _dragOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var data = new DataObject(DataFormats.FileDrop, new[] { entry.Path });
        DragDrop.DoDragDrop(Items, data, DragDropEffects.Copy);
    }
}
