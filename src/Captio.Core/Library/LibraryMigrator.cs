using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Captio.Core.Library;

/// <summary>The private folder used by version 0.1 before captures lived in the capture folder.</summary>
public static class LegacyLibrary
{
    internal const string IndexFileName = "index.json";

    public static string DefaultPath(string localAppData) => Path.Combine(localAppData, "Captio", "Library");

    public static bool HasCaptures(string folder) => Directory.Exists(folder) && LibraryMigrator.EnumerateCaptures(folder).Any();

    internal sealed record IndexEntry(string FileName, string DisplayName, DateTimeOffset CreatedAt);

    /// <summary>Display names and dates from the old index, keyed by file name; empty if unreadable.</summary>
    internal static Dictionary<string, IndexEntry> ReadIndex(string folder)
    {
        var path = Path.Combine(folder, IndexFileName);
        try
        {
            if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
            var entries = JsonSerializer.Deserialize<List<IndexEntry>>(File.ReadAllText(path)) ?? [];
            return entries.Where(e => e.FileName is not null)
                .GroupBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}

public abstract record MigrationResult;

public sealed record MigrationSucceeded(int Moved) : MigrationResult;

/// <summary>Nothing was moved; the source is untouched.</summary>
public sealed record MigrationFailed(string Reason) : MigrationResult;

public enum RecoveryOutcome
{
    NothingPending,
    /// <summary>An interrupted copy was undone; captures remain in the source.</summary>
    RolledBack,
    /// <summary>Files were already verified at the destination; leftover sources were removed.</summary>
    Completed,
}

/// <summary>
/// Moves captures between folders without risking loss: copy, verify every file by
/// SHA-256, and only then delete the source. A journal makes an interrupted move
/// recoverable on the next start.
/// </summary>
public sealed class LibraryMigrator(string journalPath, Action<string, string>? copyFile = null)
{
    private const string PartialSuffix = ".captio-partial";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Action<string, string> _copyFile = copyFile ?? ((source, destination) => File.Copy(source, destination));

    internal enum JournalState { Copying, Verified }

    internal sealed record JournalItem(string Source, string Destination, string Sha256, DateTime CreatedUtc, DateTime ModifiedUtc);

    internal sealed record Journal(JournalState State, string Source, string Destination, List<JournalItem> Items);

    public static IEnumerable<string> EnumerateCaptures(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Where(p => LibraryStore.Extensions.Contains(Path.GetExtension(p)))
            : [];

    public static int CountCaptures(string folder) => EnumerateCaptures(folder).Count();

    /// <param name="onVerified">
    /// Runs after every file is verified at the destination and before any source is deleted,
    /// so the caller can switch its configuration first.
    /// </param>
    public MigrationResult Migrate(string source, string destination, Action? onVerified = null)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return new MigrationSucceeded(0);
        if (!Directory.Exists(destination)) return new MigrationFailed($"La carpeta de destino no existe: {destination}");

        Journal? journal = null;
        try
        {
            journal = Plan(source, destination);
            if (journal.Items.Count == 0)
            {
                onVerified?.Invoke();
                CleanUpSource(source);
                return new MigrationSucceeded(0);
            }
            WriteJournal(journal);

            foreach (var item in journal.Items) CopyVerified(item);
            foreach (var item in journal.Items)
            {
                if (Hash(item.Destination) != item.Sha256) throw new IOException($"La copia de «{Path.GetFileName(item.Source)}» no coincide con el original.");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (journal is not null) RollBack(journal);
            DeleteJournal();
            return new MigrationFailed(e.Message);
        }

        WriteJournal(journal with { State = JournalState.Verified });
        onVerified?.Invoke();
        DeleteSources(journal);
        return new MigrationSucceeded(journal.Items.Count);
    }

    /// <summary>Finishes or undoes a move interrupted by a crash or power loss.</summary>
    public (RecoveryOutcome Outcome, string? Destination) Recover()
    {
        var journal = ReadJournal();
        if (journal is null)
        {
            DeleteJournal();
            return (RecoveryOutcome.NothingPending, null);
        }

        if (journal.State == JournalState.Verified)
        {
            DeleteSources(journal);
            return (RecoveryOutcome.Completed, journal.Destination);
        }

        RollBack(journal);
        DeleteJournal();
        return (RecoveryOutcome.RolledBack, null);
    }

    private static Journal Plan(string source, string destination)
    {
        var index = LegacyLibrary.ReadIndex(source);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<JournalItem>();

        foreach (var path in EnumerateCaptures(source).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            var extension = Path.GetExtension(path);
            var baseName = Path.GetFileNameWithoutExtension(path);
            var created = File.GetCreationTimeUtc(path);
            if (index.TryGetValue(fileName, out var entry))
            {
                // Names given in the old library become file names.
                try { baseName = LibraryStore.ValidateName(entry.DisplayName); } catch (ArgumentException) { }
                created = entry.CreatedAt.UtcDateTime;
            }

            var target = LibraryStore.UniqueFileName(destination, baseName, extension, reserved);
            reserved.Add(target);
            items.Add(new JournalItem(path, Path.Combine(destination, target), Hash(path), created, File.GetLastWriteTimeUtc(path)));
        }
        return new Journal(JournalState.Copying, source, destination, items);
    }

    private void CopyVerified(JournalItem item)
    {
        var partial = item.Destination + PartialSuffix;
        _copyFile(item.Source, partial);
        if (Hash(partial) != item.Sha256) throw new IOException($"La copia de «{Path.GetFileName(item.Source)}» está dañada.");
        File.Move(partial, item.Destination); // never overwrites
        File.SetCreationTimeUtc(item.Destination, item.CreatedUtc);
        File.SetLastWriteTimeUtc(item.Destination, item.ModifiedUtc);
    }

    /// <summary>Deletes only destination files whose content proves they are our copies.</summary>
    private static void RollBack(Journal journal)
    {
        foreach (var item in journal.Items)
        {
            TryDelete(item.Destination + PartialSuffix);
            if (File.Exists(item.Destination) && File.Exists(item.Source) && SafeHash(item.Destination) == item.Sha256)
                TryDelete(item.Destination);
        }
    }

    private void DeleteSources(Journal journal)
    {
        foreach (var item in journal.Items)
        {
            if (File.Exists(item.Destination) && SafeHash(item.Destination) == item.Sha256) TryDelete(item.Source);
        }
        CleanUpSource(journal.Source);
        DeleteJournal();
    }

    /// <summary>Removes the old index and the folder itself when it was a legacy library left empty.</summary>
    private static void CleanUpSource(string source)
    {
        var index = Path.Combine(source, LegacyLibrary.IndexFileName);
        if (!File.Exists(index) || EnumerateCaptures(source).Any()) return;
        TryDelete(index);
        try
        {
            if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal void WriteJournal(Journal journal)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(journalPath)!);
        var temp = journalPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(journal, JsonOptions));
        File.Move(temp, journalPath, overwrite: true);
    }

    private Journal? ReadJournal()
    {
        try
        {
            return File.Exists(journalPath) ? JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath), JsonOptions) : null;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    private void DeleteJournal() => TryDelete(journalPath);

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string? SafeHash(string path)
    {
        try { return Hash(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
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
