using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Captio.Core.Storage;

public interface IKnownFolders
{
    /// <summary>
    /// The Windows Screenshots known folder as Windows resolves it (redirection,
    /// OneDrive, profile location), or null if it cannot be resolved.
    /// </summary>
    /// <param name="create">Let Windows create the folder if it does not exist yet.</param>
    string? GetScreenshotsFolder(bool create);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsKnownFolders : IKnownFolders
{
    private static readonly Guid FolderIdScreenshots = new("B7BEDE81-DF94-4682-A7D8-57A52620B86F");
    private const uint KF_FLAG_CREATE = 0x00008000;
    private const uint KF_FLAG_DONT_VERIFY = 0x00004000;

    public string? GetScreenshotsFolder(bool create)
    {
        var id = FolderIdScreenshots;
        var hr = SHGetKnownFolderPath(ref id, create ? KF_FLAG_CREATE : KF_FLAG_DONT_VERIFY, 0, out var buffer);
        try
        {
            return hr >= 0 && buffer != 0 ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            if (buffer != 0) Marshal.FreeCoTaskMem(buffer);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, nint token, out nint path);
}

public enum FolderProblem
{
    None,
    /// <summary>Windows could not resolve its Screenshots folder.</summary>
    Unresolvable,
    NotAbsolute,
    NotFound,
    NotWritable,
}

public sealed record FolderCheck(string? Path, FolderProblem Problem, string? Detail = null)
{
    public bool Ok => Problem == FolderProblem.None;

    public string Describe() => Problem switch
    {
        FolderProblem.None => "La carpeta está disponible.",
        FolderProblem.Unresolvable => "Windows no pudo indicar su carpeta de capturas de pantalla.",
        FolderProblem.NotAbsolute => "La ruta debe ser completa, por ejemplo D:\\Capturas.",
        FolderProblem.NotFound => $"La carpeta no existe: {Path}",
        FolderProblem.NotWritable => $"No hay permiso para escribir en {Path}." + (Detail is null ? "" : $" ({Detail})"),
        _ => "Problema desconocido con la carpeta.",
    };
}

public sealed class CaptureFolderException(FolderCheck check) : IOException(check.Describe())
{
    public FolderCheck Check { get; } = check;
}

/// <summary>
/// Decides where captures are saved. Never falls back to another location: if the
/// configured or default folder is unusable the caller must ask the user.
/// </summary>
public sealed class CaptureFolderResolver(IKnownFolders knownFolders)
{
    public string? DefaultFolder => knownFolders.GetScreenshotsFolder(create: false);

    /// <param name="configured">Absolute custom folder, or null for the Windows Screenshots folder.</param>
    /// <param name="ensureReady">
    /// Create the Screenshots folder if missing (Windows creates exactly that folder) and
    /// check write access. Custom folders are never created.
    /// </param>
    public FolderCheck Resolve(string? configured, bool ensureReady)
    {
        if (configured is null)
        {
            var path = knownFolders.GetScreenshotsFolder(create: ensureReady);
            if (path is null) return new FolderCheck(null, FolderProblem.Unresolvable);
            return ensureReady ? Validate(path) : new FolderCheck(path, FolderProblem.None);
        }
        return ensureReady ? Validate(configured) : Shape(configured);
    }

    /// <summary>Exists, is absolute and accepts new files.</summary>
    public static FolderCheck Validate(string path)
    {
        var shaped = Shape(path);
        if (!shaped.Ok) return shaped;
        if (!Directory.Exists(path)) return new FolderCheck(path, FolderProblem.NotFound);

        var probe = System.IO.Path.Combine(path, $".captio-write-check-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            return new FolderCheck(path, FolderProblem.None);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return new FolderCheck(path, FolderProblem.NotWritable, e.Message);
        }
    }

    public static bool SamePath(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(a)),
            System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static FolderCheck Shape(string path) =>
        System.IO.Path.IsPathFullyQualified(path)
            ? new FolderCheck(path, FolderProblem.None)
            : new FolderCheck(path, FolderProblem.NotAbsolute);
}
