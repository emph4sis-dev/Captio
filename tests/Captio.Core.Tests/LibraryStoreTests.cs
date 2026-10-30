using Captio.Core.Imaging;
using Captio.Core.Library;

namespace Captio.Core.Tests;

public sealed class LibraryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "captio-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Saved_items_persist_across_instances()
    {
        var store = new LibraryStore(_root);
        var item = store.AddImage(TestPng.Create(7, 5), DateTimeOffset.Now);

        var reopened = new LibraryStore(_root);

        var loaded = Assert.Single(reopened.Items);
        Assert.Equal(item.Id, loaded.Id);
        Assert.Equal((7, 5), (loaded.Width, loaded.Height));
        Assert.Equal(LibraryItemKind.Image, loaded.Kind);
    }

    [Fact]
    public void Same_timestamp_gets_unique_file_names()
    {
        var store = new LibraryStore(_root);
        var now = DateTimeOffset.Now;
        var a = store.AddImage(TestPng.Create(1, 1), now);
        var b = store.AddImage(TestPng.Create(1, 1), now);

        Assert.NotEqual(a.FileName, b.FileName);
    }

    [Fact]
    public void Rename_changes_display_name_only()
    {
        var store = new LibraryStore(_root);
        var item = store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now);

        store.Rename(item.Id, "  Factura  ");

        var reopened = Assert.Single(new LibraryStore(_root).Items);
        Assert.Equal("Factura", reopened.DisplayName);
        Assert.Equal(item.FileName, reopened.FileName);
    }

    [Fact]
    public void Delete_removes_file_and_entry()
    {
        var store = new LibraryStore(_root);
        var item = store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now);
        var path = store.GetPath(item);

        store.Delete(item.Id);

        Assert.False(File.Exists(path));
        Assert.Empty(new LibraryStore(_root).Items);
    }

    [Fact]
    public void Missing_files_are_dropped_from_index()
    {
        var store = new LibraryStore(_root);
        var item = store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now);
        File.Delete(store.GetPath(item));

        Assert.Empty(new LibraryStore(_root).Items);
    }

    [Fact]
    public void Corrupt_index_is_rebuilt_from_files()
    {
        var store = new LibraryStore(_root);
        store.AddImage(TestPng.Create(4, 3), DateTimeOffset.Now);
        File.WriteAllText(Path.Combine(_root, "index.json"), "{ broken");

        var rebuilt = Assert.Single(new LibraryStore(_root).Items);
        Assert.Equal((4, 3), (rebuilt.Width, rebuilt.Height));
    }
}

public class PngSanitizerTests
{
    [Fact]
    public void Strips_ancillary_chunks_and_keeps_image_chunks()
    {
        var png = TestPng.Create(2, 2, withText: true);
        Assert.Contains("tEXt", System.Text.Encoding.ASCII.GetString(png));

        var clean = PngSanitizer.Sanitize(png);

        var ascii = System.Text.Encoding.ASCII.GetString(clean);
        Assert.DoesNotContain("tEXt", ascii);
        Assert.Contains("IHDR", ascii);
        Assert.Contains("IDAT", ascii);
        Assert.EndsWith("IEND", ascii[..^4]);
        Assert.Equal((2, 2), PngSanitizer.ReadSize(clean));
    }

    [Fact]
    public void Rejects_non_png()
    {
        Assert.Throws<InvalidDataException>(() => PngSanitizer.Sanitize([1, 2, 3, 4, 5, 6, 7, 8, 9]));
    }
}
