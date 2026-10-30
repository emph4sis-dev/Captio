using System.Text.Json;
using Captio.Core.Library;

namespace Captio.Core.Tests;

public sealed class LibraryMigratorTests : IDisposable
{
    private readonly string _root = TempDir.Create();
    private readonly string _source;
    private readonly string _destination;
    private readonly string _journal;

    public LibraryMigratorTests()
    {
        _source = Directory.CreateDirectory(Path.Combine(_root, "Captio", "Library")).FullName;
        _destination = Directory.CreateDirectory(Path.Combine(_root, "Screenshots")).FullName;
        _journal = Path.Combine(_root, "state", "migration.json");
    }

    public void Dispose() => TempDir.Delete(_root);

    /// <summary>A version 0.1 library: PNGs plus an index with display names.</summary>
    private void SeedLegacyLibrary(params (string File, string Name)[] items)
    {
        var index = items.Select((item, i) => new
        {
            Id = Guid.NewGuid(),
            FileName = item.File,
            DisplayName = item.Name,
            CreatedAt = new DateTimeOffset(2026, 10, 1 + i, 9, 0, 0, TimeSpan.Zero),
            Width = 2,
            Height = 2,
            Kind = "Image",
        });
        foreach (var (file, _) in items) File.WriteAllBytes(Path.Combine(_source, file), TestPng.Create(2, 2 + file.Length));
        File.WriteAllText(Path.Combine(_source, "index.json"), JsonSerializer.Serialize(index));
    }

    private static string[] Files(string folder) => Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToArray()!;

    [Fact]
    public void Moves_legacy_captures_using_their_names_and_removes_the_old_library()
    {
        SeedLegacyLibrary(("Captura 2026-10-01 09-00-00.png", "Factura"), ("Captura 2026-10-02 09-00-00.png", "Captura 2026-10-02 09-00-00"));
        var originals = Directory.GetFiles(_source, "*.png").ToDictionary(p => Path.GetFileName(p), LibraryMigrator.Hash);
        var verifiedCalled = false;

        var result = new LibraryMigrator(_journal).Migrate(_source, _destination, onVerified: () =>
        {
            verifiedCalled = true;
            Assert.Equal(2, Directory.GetFiles(_source, "*.png").Length); // sources still there when settings switch
        });

        Assert.Equal(2, Assert.IsType<MigrationSucceeded>(result).Moved);
        Assert.True(verifiedCalled);
        Assert.Equal(["Captura 2026-10-02 09-00-00.png", "Factura.png"], Files(_destination));
        Assert.Equal(originals["Captura 2026-10-01 09-00-00.png"], LibraryMigrator.Hash(Path.Combine(_destination, "Factura.png")));
        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), File.GetCreationTimeUtc(Path.Combine(_destination, "Factura.png")));
        Assert.False(Directory.Exists(_source));
        Assert.False(File.Exists(_journal));
    }

    [Fact]
    public void Existing_files_at_destination_are_never_overwritten()
    {
        SeedLegacyLibrary(("a.png", "Factura"));
        var existing = TestPng.Create(9, 9);
        File.WriteAllBytes(Path.Combine(_destination, "Factura.png"), existing);

        new LibraryMigrator(_journal).Migrate(_source, _destination);

        Assert.Equal(existing, File.ReadAllBytes(Path.Combine(_destination, "Factura.png")));
        Assert.True(File.Exists(Path.Combine(_destination, "Factura (2).png")));
    }

    [Fact]
    public void Failure_mid_copy_rolls_back_and_keeps_every_source()
    {
        SeedLegacyLibrary(("a.png", "A"), ("b.png", "B"), ("c.png", "C"));
        var before = Files(_source);
        var copies = 0;
        var migrator = new LibraryMigrator(_journal, (from, to) =>
        {
            if (++copies == 2) throw new IOException("Disco lleno");
            File.Copy(from, to);
        });

        var result = migrator.Migrate(_source, _destination);

        Assert.Contains("Disco lleno", Assert.IsType<MigrationFailed>(result).Reason);
        Assert.Equal(before, Files(_source));
        Assert.Empty(Directory.GetFiles(_destination));
        Assert.False(File.Exists(_journal));
    }

    [Fact]
    public void Corrupted_copy_is_detected_and_rolled_back()
    {
        SeedLegacyLibrary(("a.png", "A"));
        var migrator = new LibraryMigrator(_journal, (from, to) => File.WriteAllBytes(to, [1, 2, 3]));

        Assert.IsType<MigrationFailed>(migrator.Migrate(_source, _destination));
        Assert.Single(Directory.GetFiles(_source, "*.png"));
        Assert.Empty(Directory.GetFiles(_destination));
    }

    [Fact]
    public void Crash_during_copy_is_rolled_back_on_next_start()
    {
        SeedLegacyLibrary(("a.png", "A"), ("b.png", "B"));
        var migrator = new LibraryMigrator(_journal);
        // Simulate a crash: journal written, one file copied, process gone.
        var a = Path.Combine(_source, "a.png");
        var copied = Path.Combine(_destination, "A.png");
        File.Copy(a, copied);
        var userFile = Path.Combine(_destination, "B.png");
        File.WriteAllBytes(userFile, TestPng.Create(5, 5)); // the user's own file, same planned name
        migrator.WriteJournal(new LibraryMigrator.Journal(LibraryMigrator.JournalState.Copying, _source, _destination,
        [
            new(a, copied, LibraryMigrator.Hash(a), DateTime.UtcNow, DateTime.UtcNow),
            new(Path.Combine(_source, "b.png"), userFile, LibraryMigrator.Hash(Path.Combine(_source, "b.png")), DateTime.UtcNow, DateTime.UtcNow),
        ]));

        var (outcome, _) = migrator.Recover();

        Assert.Equal(RecoveryOutcome.RolledBack, outcome);
        Assert.False(File.Exists(copied));
        Assert.True(File.Exists(userFile)); // not ours: content differs, so it stays
        Assert.Equal(["a.png", "b.png", "index.json"], Files(_source));
        Assert.False(File.Exists(_journal));
    }

    [Fact]
    public void Crash_after_verification_finishes_the_move_on_next_start()
    {
        SeedLegacyLibrary(("a.png", "A"));
        var migrator = new LibraryMigrator(_journal);
        var a = Path.Combine(_source, "a.png");
        var copied = Path.Combine(_destination, "A.png");
        File.Copy(a, copied);
        migrator.WriteJournal(new LibraryMigrator.Journal(LibraryMigrator.JournalState.Verified, _source, _destination,
            [new(a, copied, LibraryMigrator.Hash(a), DateTime.UtcNow, DateTime.UtcNow)]));

        var (outcome, destination) = migrator.Recover();

        Assert.Equal(RecoveryOutcome.Completed, outcome);
        Assert.Equal(_destination, destination);
        Assert.True(File.Exists(copied));
        Assert.False(Directory.Exists(_source));
    }

    [Fact]
    public void Nothing_pending_without_journal()
    {
        Assert.Equal(RecoveryOutcome.NothingPending, new LibraryMigrator(_journal).Recover().Outcome);
    }

    [Fact]
    public void Plain_folder_move_keeps_names_and_leaves_folder()
    {
        var custom = Directory.CreateDirectory(Path.Combine(_root, "Mis capturas")).FullName;
        File.WriteAllBytes(Path.Combine(custom, "uno.png"), TestPng.Create(1, 1));
        File.WriteAllText(Path.Combine(custom, "otra cosa.txt"), "no es captura");

        var result = new LibraryMigrator(_journal).Migrate(custom, _destination);

        Assert.Equal(1, Assert.IsType<MigrationSucceeded>(result).Moved);
        Assert.Equal(["uno.png"], Files(_destination));
        Assert.Equal(["otra cosa.txt"], Files(custom)); // only captures move; the user's folder stays
    }

    [Fact]
    public void Legacy_detection_requires_captures()
    {
        Assert.False(LegacyLibrary.HasCaptures(_source));
        File.WriteAllText(Path.Combine(_source, "index.json"), "[]");
        Assert.False(LegacyLibrary.HasCaptures(_source));
        File.WriteAllBytes(Path.Combine(_source, "x.png"), TestPng.Create(1, 1));
        Assert.True(LegacyLibrary.HasCaptures(_source));
    }
}
