using Captio.Core.Library;

namespace Captio.Core.Delivery;

public abstract record CopyOutcome;

/// <summary>The clipboard rejected the image; nothing was saved and the editor stays open.</summary>
public sealed record CopyFailed(Exception Error) : CopyOutcome;

public sealed record Copied : CopyOutcome;

public sealed record CopiedAndSaved(LibraryItem Item) : CopyOutcome;

/// <summary>The image is on the clipboard but the extra copy could not be saved.</summary>
public sealed record CopiedButSaveFailed(Exception Error) : CopyOutcome;

public static class CopyWorkflow
{
    /// <summary>
    /// Copies the flattened PNG and, only if that worked and the user asked for it,
    /// saves the very same bytes. Never touches the network.
    /// </summary>
    public static CopyOutcome Run(byte[] png, bool alsoSave, Action<byte[]> copy, Func<byte[], LibraryItem> save)
    {
        try
        {
            copy(png);
        }
        catch (Exception e)
        {
            return new CopyFailed(e);
        }

        if (!alsoSave) return new Copied();

        try
        {
            return new CopiedAndSaved(save(png));
        }
        catch (Exception e)
        {
            return new CopiedButSaveFailed(e);
        }
    }
}
