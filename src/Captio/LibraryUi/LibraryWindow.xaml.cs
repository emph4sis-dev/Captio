using System.Collections.Specialized;
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

internal sealed class LibraryEntry(LibraryItem item, string path)
{
    private ImageSource? _thumbnail;

    public LibraryItem Item { get; } = item;

    public string Path { get; } = path;

    public string Name => Item.DisplayName;

    public string Details =>
        $"{Item.CreatedAt.ToLocalTime():g} · " + (Item.Kind == LibraryItemKind.Image ? $"{Item.Width} × {Item.Height}" : "Video");

    public Visibility VideoGlyphVisibility => Item.Kind == LibraryItemKind.Video ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? Thumbnail => Item.Kind == LibraryItemKind.Image ? _thumbnail ??= LoadThumbnail() : null;

    private BitmapImage? LoadThumbnail()
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(Path);
            image.DecodePixelWidth = 400;
            image.CacheOption = BitmapCacheOption.OnLoad; // do not keep the file locked
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException)
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

public partial class LibraryWindow : Window
{
    private readonly AppServices _services;
    private Point _dragOrigin;

    internal LibraryWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        _services.Library.Changed += OnLibraryChanged;
        Closed += (_, _) => _services.Library.Changed -= OnLibraryChanged;
        Reload();
    }

    private LibraryEntry? Selected => Items.SelectedItem as LibraryEntry;

    private void OnLibraryChanged(object? sender, EventArgs e) => Dispatcher.Invoke(Reload);

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized && Items is not null) Reload();
    }

    private void Reload()
    {
        var selectedId = Selected?.Item.Id;
        var now = DateTimeOffset.Now;
        DateTimeOffset? since = PeriodFilter.SelectedIndex switch
        {
            1 => new DateTimeOffset(now.Date, now.Offset),
            2 => now.AddDays(-7),
            3 => now.AddDays(-30),
            _ => null,
        };
        LibraryItemKind? kind = KindFilter.SelectedIndex switch
        {
            1 => LibraryItemKind.Image,
            2 => LibraryItemKind.Video,
            _ => null,
        };

        var library = _services.Library;
        var entries = library.Items
            .Where(i => kind is null || i.Kind == kind)
            .Where(i => since is null || i.CreatedAt >= since)
            .Select(i => new LibraryEntry(i, library.GetPath(i)))
            .ToList();

        Items.ItemsSource = entries;
        Items.SelectedItem = entries.FirstOrDefault(e => e.Item.Id == selectedId);
        EmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Text = library.Items.Count == 0
            ? "Aún no hay capturas guardadas. En el editor, pulsa Ctrl+S para guardar una aquí."
            : "Ningún elemento coincide con los filtros.";
    }

    private void OnOpen(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } entry && File.Exists(entry.Path))
            Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        var data = new DataObject();
        data.SetFileDropList(new StringCollection { entry.Path });
        if (entry.Item.Kind == LibraryItemKind.Image)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(entry.Path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            data.SetImage(bitmap);
        }
        Clipboard.SetDataObject(data, copy: true);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        var extension = System.IO.Path.GetExtension(entry.Path);
        var dialog = new SaveFileDialog
        {
            FileName = MakeSafeFileName(entry.Name) + extension,
            Filter = entry.Item.Kind == LibraryItemKind.Image ? "Imagen PNG|*.png" : "Video MP4|*.mp4",
        };
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
        if (Selected is not { } entry) return;
        var dialog = new InputDialog("Renombrar", "Nombre", entry.Name) { Owner = this };
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Value))
            _services.Library.Rename(entry.Item.Id, dialog.Value);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        var answer = MessageBox.Show(this,
            $"«{entry.Name}» se borrará definitivamente. No irá a la papelera.",
            "Eliminar", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        try
        {
            _services.Library.Delete(entry.Item.Id);
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

    private static string MakeSafeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
