using Captio.Core.Delivery;
using Captio.Core.Library;

namespace Captio.Core.Tests;

public class CopyWorkflowTests
{
    private static readonly LibraryItem Saved = new("Captura.png", DateTimeOffset.Now, LibraryItemKind.Image, 1, 1);

    [Fact]
    public void Copy_without_auto_save_never_saves()
    {
        var saves = 0;
        var outcome = CopyWorkflow.Run([1, 2], alsoSave: false, copy: _ => { }, save: _ => { saves++; return Saved; });

        Assert.IsType<Copied>(outcome);
        Assert.Equal(0, saves);
    }

    [Fact]
    public void Copy_with_auto_save_saves_the_same_flattened_bytes()
    {
        byte[] png = [1, 2, 3];
        byte[]? copied = null, saved = null;

        var outcome = CopyWorkflow.Run(png, alsoSave: true,
            copy: bytes => copied = bytes,
            save: bytes => { saved = bytes; return Saved; });

        Assert.Equal(Saved, Assert.IsType<CopiedAndSaved>(outcome).Item);
        Assert.Same(png, copied);
        Assert.Same(png, saved); // not re-rendered
    }

    [Fact]
    public void Failed_copy_does_not_save()
    {
        var saves = 0;
        var outcome = CopyWorkflow.Run([1], alsoSave: true,
            copy: _ => throw new System.Runtime.InteropServices.COMException("Portapapeles ocupado"),
            save: _ => { saves++; return Saved; });

        Assert.IsType<CopyFailed>(outcome);
        Assert.Equal(0, saves);
    }

    [Fact]
    public void Copy_succeeds_even_when_saving_fails()
    {
        var copies = 0;
        var outcome = CopyWorkflow.Run([1], alsoSave: true,
            copy: _ => copies++,
            save: _ => throw new IOException("Disco lleno"));

        Assert.Equal("Disco lleno", Assert.IsType<CopiedButSaveFailed>(outcome).Error.Message);
        Assert.Equal(1, copies);
    }
}
