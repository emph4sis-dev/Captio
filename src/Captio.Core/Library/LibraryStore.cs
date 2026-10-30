using Captio.Core.Imaging;

namespace Captio.Core.Library;

public enum LibraryItemKind
{
    Image,
    Video,
}

/// <param name="Width">Known when Captio wrote the file; otherwise read lazily with <see cref="LibraryStore.TryReadSize"/>.</param>
public sealed record LibraryItem(string FileName, DateTimeOffset CreatedAt, LibraryItemKind Kind, int? Width = null, int? Height = null)
{
    public string DisplayName => Path.GetFileNameWithoutExtension(FileName);
}

/// <summary>
/// The library is the capture folder itself: every PNG in it is an item, its file
/// name is its name. There is no separate index or internal copy.
/// Listing only reads directory entries, so folders with thousands of images stay fast;
/// saving never rescans the folder.
/// </summary>
public sealed class LibraryStore
{
    // Clips will add ".mp4" once recording exists.
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png" };

    private List<LibraryItem> _items = [];

    /// <summary>Does not touch the disk; call <see cref="Refresh"/> or <see cref="RefreshAsync"/> to list.</summary>
    public LibraryStore(string root) => Root = root;

    public string Root { get; }

    /// <summary>Newest first, as of the last refresh plus items saved since.</summary>
    public IReadOnlyList<LibraryItem> Items => _items;

    public bool Loaded { get; private set; }

    public event EventHandler? Changed;

    public string GetPath(LibraryItem item) => Path.Combine(Root, item.FileName);

    /// <summary>Rescans the folder, picking up files added or removed outside Captio.</summary>
    public void Refresh() => Apply(Scan(Root));

    /// <summary>Scans on a worker thread and applies the result on the caller's context.</summary>
    public async Task RefreshAsync()
    {
        var root = Root;
        Apply(await Task.Run(() => Scan(root)));
    }

    private void Apply(List<LibraryItem> items)
    {
        _items = items;
        Loaded = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<LibraryItem> Scan(string root)
    {
        if (!Directory.Exists(root)) return [];
        try
        {
            return new DirectoryInfo(root).EnumerateFiles()
                .Where(f => Extensions.Contains(f.Extension))
                .Select(f => new LibraryItem(f.Name, new DateTimeOffset(f.CreationTimeUtc, TimeSpan.Zero), LibraryItemKind.Image))
                .OrderByDescending(i => i.CreatedAt)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Pixel size from the PNG header, or null if the file is unreadable or not a PNG.</summary>
    public static (int Width, int Height)? TryReadSize(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[24];
            using var file = File.OpenRead(path);
            file.ReadExactly(header);
            return PngSanitizer.ReadSize(header);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Writes the PNG; never scans the folder.</summary>
    public LibraryItem AddImage(ReadOnlySpan<byte> png, DateTimeOffset createdAt)
    {
        var (width, height) = PngSanitizer.ReadSize(png);
        var fileName = UniqueFileName(Root, "Captura " + createdAt.ToLocalTime().ToString("yyyy-MM-dd HH-mm-ss"), ".png");
        var path = Path.Combine(Root, fileName);

        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            file.Write(png);
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        var item = new LibraryItem(fileName, createdAt, LibraryItemKind.Image, width, height);
        if (Loaded) _items.Insert(0, item);
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    /// <summary>Renames the file on disk, keeping its extension.</summary>
    public LibraryItem Rename(LibraryItem item, string newName)
    {
        newName = ValidateName(newName);
        var fileName = newName + Path.GetExtension(item.FileName);
        if (string.Equals(fileName, item.FileName, StringComparison.Ordinal)) return item;

        var target = Path.Combine(Root, fileName);
        var caseOnly = string.Equals(fileName, item.FileName, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && File.Exists(target)) throw new IOException($"Ya existe un archivo llamado «{fileName}».");

        File.Move(GetPath(item), target);
        var renamed = item with { FileName = fileName };
        var index = _items.FindIndex(i => i.FileName == item.FileName);
        if (index >= 0) _items[index] = renamed;
        Changed?.Invoke(this, EventArgs.Empty);
        return renamed;
    }

    /// <summary>Permanently deletes the file (no recycle bin).</summary>
    public void Delete(LibraryItem item)
    {
        var path = GetPath(item);
        if (File.Exists(path)) File.Delete(path);
        _items.RemoveAll(i => i.FileName == item.FileName);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static string ValidateName(string name)
    {
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0) throw new ArgumentException("El nombre no puede estar vacío.", nameof(name));
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("El nombre contiene caracteres no permitidos: \\ / : * ? \" < > |", nameof(name));
        return name;
    }

    internal static string UniqueFileName(string folder, string baseName, string extension, ISet<string>? reserved = null)
    {
        var name = baseName + extension;
        for (var n = 2; File.Exists(Path.Combine(folder, name)) || reserved?.Contains(name) == true; n++)
        {
            name = $"{baseName} ({n}){extension}";
        }
        return name;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
