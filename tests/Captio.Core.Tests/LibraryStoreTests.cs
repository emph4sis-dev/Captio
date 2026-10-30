using Captio.Core.Imaging;
using Captio.Core.Library;

namespace Captio.Core.Tests;

public sealed class LibraryStoreTests : IDisposable
{
    private readonly string _root = TempDir.Create();

    public void Dispose() => TempDir.Delete(_root);

    private LibraryStore Reopen()
    {
        var store = new LibraryStore(_root);
        store.Refresh();
        return store;
    }

    [Fact]
    public void Listing_never_opens_the_files()
    {
        var locked = Path.Combine(_root, "abierta en otro programa.png");
        File.WriteAllBytes(locked, TestPng.Create(4, 4));
        using var exclusive = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var item = Assert.Single(Reopen().Items);

        Assert.Equal("abierta en otro programa", item.DisplayName);
        Assert.Null(item.Width); // size is read lazily, not during the listing
    }

    [Fact]
    public void Saving_never_scans_the_folder()
    {
        for (var i = 0; i < 50; i++) File.WriteAllBytes(Path.Combine(_root, $"previa {i}.png"), TestPng.Create(1, 1));
        var store = new LibraryStore(_root);
        var changes = 0;
        store.Changed += (_, _) => changes++;

        var saved = store.AddImage(TestPng.Create(3, 2), DateTimeOffset.Now);

        Assert.False(store.Loaded);
        Assert.Empty(store.Items); // nothing listed: the save only wrote one file
        Assert.Equal(1, changes);
        Assert.Equal((3, 2), (saved.Width, saved.Height));
        Assert.Equal(51, Directory.GetFiles(_root, "*.png").Length);
    }

    [Fact]
    public void Saving_into_a_loaded_library_adds_it_on_top()
    {
        File.WriteAllBytes(Path.Combine(_root, "vieja.png"), TestPng.Create(1, 1));
        File.SetCreationTimeUtc(Path.Combine(_root, "vieja.png"), DateTime.UtcNow.AddDays(-1));
        var store = Reopen();

        var saved = store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now);

        Assert.Equal([saved.FileName, "vieja.png"], store.Items.Select(i => i.FileName));
    }

    [Fact]
    public async Task Async_refresh_lists_thousands_of_images_quickly()
    {
        var png = TestPng.Create(1, 1);
        for (var i = 0; i < 2000; i++) File.WriteAllBytes(Path.Combine(_root, $"captura {i:D4}.png"), png);
        var store = new LibraryStore(_root);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await store.RefreshAsync();

        Assert.True(store.Loaded);
        Assert.Equal(2000, store.Items.Count);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"listing took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Saved_items_persist_across_instances()
    {
        var store = new LibraryStore(_root);
        var item = store.AddImage(TestPng.Create(7, 5), DateTimeOffset.Now);

        var reopened = Assert.Single(Reopen().Items);
        Assert.Equal(item.FileName, reopened.FileName);
        Assert.Equal((7, 5), LibraryStore.TryReadSize(Path.Combine(_root, reopened.FileName)));
        Assert.Equal((7, 5), (item.Width, item.Height)); // known right away for files Captio writes
        Assert.Equal(LibraryItemKind.Image, reopened.Kind);
    }

    [Fact]
    public void Folder_is_the_source_of_truth_with_no_index_file()
    {
        var store = new LibraryStore(_root);
        store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now);
        File.WriteAllBytes(Path.Combine(_root, "Hecha fuera.png"), TestPng.Create(3, 3));
        File.WriteAllText(Path.Combine(_root, "notas.txt"), "no es una captura");

        store.Refresh();

        Assert.Equal(2, store.Items.Count);
        Assert.Contains(store.Items, i => i.DisplayName == "Hecha fuera");
        Assert.DoesNotContain(Directory.EnumerateFiles(_root), f => Path.GetFileName(f) == "index.json");
    }

    [Fact]
    public void Does_not_create_a_missing_folder()
    {
        var missing = Path.Combine(_root, "no-existe");
        var store = new LibraryStore(missing);

        Assert.Empty(store.Items);
        Assert.False(Directory.Exists(missing));
        Assert.Throws<DirectoryNotFoundException>(() => store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now));
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
    public void Rename_renames_the_file_and_keeps_extension()
    {
        var store = new LibraryStore(_root);
        var item = store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now);

        var renamed = store.Rename(item, "  Factura  ");

        Assert.Equal("Factura.png", renamed.FileName);
        Assert.True(File.Exists(Path.Combine(_root, "Factura.png")));
        Assert.False(File.Exists(Path.Combine(_root, item.FileName)));
        Assert.Equal("Factura", Assert.Single(Reopen().Items).DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("¿qué?")]
    public void Rename_rejects_invalid_names(string name)
    {
        var store = new LibraryStore(_root);
        var item = store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now);

        Assert.Throws<ArgumentException>(() => store.Rename(item, name));
    }

    [Fact]
    public void Rename_never_overwrites_another_capture()
    {
        var store = new LibraryStore(_root);
        var first = store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now);
        store.Rename(first, "Uno");
        var second = store.AddImage(TestPng.Create(2, 2), DateTimeOffset.Now);

        Assert.Throws<IOException>(() => store.Rename(second, "Uno"));
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        var store = new LibraryStore(_root);
        var item = store.AddImage(TestPng.Create(1, 1), DateTimeOffset.Now);

        store.Delete(item);

        Assert.False(File.Exists(Path.Combine(_root, item.FileName)));
        Assert.Empty(Reopen().Items);
    }

    [Fact]
    public void Files_that_are_not_real_pngs_have_no_size()
    {
        File.WriteAllText(Path.Combine(_root, "falso.png"), "texto");

        Assert.Single(Reopen().Items); // listed like any file in the folder
        Assert.Null(LibraryStore.TryReadSize(Path.Combine(_root, "falso.png")));
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

internal static class TempDir
{
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "captio-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
