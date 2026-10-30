using System.Text.Json;
using System.Text.Json.Serialization;
using Captio.Core.Imaging;

namespace Captio.Core.Library;

public enum LibraryItemKind
{
    Image,
    Video,
}

public sealed record LibraryItem(
    Guid Id, string FileName, string DisplayName, DateTimeOffset CreatedAt, int Width, int Height, LibraryItemKind Kind);

/// <summary>
/// Permanent local library of saved captures and clips: a folder plus an index.json.
/// </summary>
public sealed class LibraryStore
{
    private const string IndexFileName = "index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly List<LibraryItem> _items = [];

    public LibraryStore(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
        Load();
    }

    public string Root { get; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<LibraryItem> Items => _items;

    public event EventHandler? Changed;

    public string GetPath(LibraryItem item) => Path.Combine(Root, item.FileName);

    public LibraryItem AddImage(ReadOnlySpan<byte> png, DateTimeOffset createdAt)
    {
        var (width, height) = PngSanitizer.ReadSize(png);
        var baseName = "Captura " + createdAt.ToLocalTime().ToString("yyyy-MM-dd HH-mm-ss");
        var fileName = UniqueFileName(baseName, ".png");

        using (var file = new FileStream(Path.Combine(Root, fileName), FileMode.CreateNew, FileAccess.Write))
        {
            file.Write(png);
        }

        var item = new LibraryItem(Guid.NewGuid(), fileName, baseName, createdAt, width, height, LibraryItemKind.Image);
        _items.Insert(0, item);
        SaveIndex();
        return item;
    }

    public LibraryItem Rename(Guid id, string displayName)
    {
        displayName = displayName.Trim();
        if (displayName.Length == 0) throw new ArgumentException("Name cannot be empty.", nameof(displayName));

        var index = IndexOf(id);
        var renamed = _items[index] with { DisplayName = displayName };
        _items[index] = renamed;
        SaveIndex();
        return renamed;
    }

    /// <summary>Permanently deletes the file (no recycle bin) and its entry.</summary>
    public void Delete(Guid id)
    {
        var index = IndexOf(id);
        var path = GetPath(_items[index]);
        if (File.Exists(path)) File.Delete(path);
        _items.RemoveAt(index);
        SaveIndex();
    }

    private int IndexOf(Guid id)
    {
        var index = _items.FindIndex(i => i.Id == id);
        return index >= 0 ? index : throw new KeyNotFoundException($"No library item {id}.");
    }

    private void Load()
    {
        var indexPath = Path.Combine(Root, IndexFileName);
        List<LibraryItem>? loaded = null;
        if (File.Exists(indexPath))
        {
            try
            {
                loaded = JsonSerializer.Deserialize<List<LibraryItem>>(File.ReadAllText(indexPath), JsonOptions);
            }
            catch (JsonException)
            {
                loaded = null;
            }
        }

        var repaired = loaded is null;
        loaded ??= Rebuild();

        var present = loaded.Where(i => i.FileName == Path.GetFileName(i.FileName) && File.Exists(GetPath(i))).ToList();
        repaired |= present.Count != loaded.Count;

        _items.Clear();
        _items.AddRange(present.OrderByDescending(i => i.CreatedAt));
        if (repaired) SaveIndex(raiseChanged: false);
    }

    private List<LibraryItem> Rebuild()
    {
        var items = new List<LibraryItem>();
        var header = new byte[24];
        foreach (var path in Directory.EnumerateFiles(Root))
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".mp4")) continue;

            int width = 0, height = 0;
            if (extension == ".png")
            {
                try
                {
                    using var file = File.OpenRead(path);
                    file.ReadExactly(header);
                    (width, height) = PngSanitizer.ReadSize(header);
                }
                catch (Exception e) when (e is IOException or InvalidDataException)
                {
                    continue;
                }
            }

            items.Add(new LibraryItem(
                Guid.NewGuid(), Path.GetFileName(path), Path.GetFileNameWithoutExtension(path),
                new DateTimeOffset(File.GetCreationTimeUtc(path), TimeSpan.Zero), width, height,
                extension == ".png" ? LibraryItemKind.Image : LibraryItemKind.Video));
        }
        return items;
    }

    private string UniqueFileName(string baseName, string extension)
    {
        var name = baseName + extension;
        for (var n = 2; File.Exists(Path.Combine(Root, name)); n++)
        {
            name = $"{baseName} ({n}){extension}";
        }
        return name;
    }

    private void SaveIndex(bool raiseChanged = true)
    {
        var indexPath = Path.Combine(Root, IndexFileName);
        var tempPath = indexPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_items, JsonOptions));
        File.Move(tempPath, indexPath, overwrite: true);
        if (raiseChanged) Changed?.Invoke(this, EventArgs.Empty);
    }
}
