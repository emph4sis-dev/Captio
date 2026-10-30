using Captio.Core.Annotations;

namespace Captio.Core.Tests;

public class AnnotationDocumentTests
{
    private static readonly RedactionAnnotation Box = new(0, 0, 10, 10);
    private static readonly ArrowAnnotation Arrow = new(0, 0, 100, 0, 0xFFFF0000, 4);

    [Fact]
    public void Undo_and_redo_walk_history()
    {
        var doc = new AnnotationDocument();
        doc.Add(Box);
        doc.Add(Arrow);

        doc.Undo();
        Assert.Equal([Box], doc.Items);
        doc.Redo();
        Assert.Equal([Box, Arrow], doc.Items);
    }

    [Fact]
    public void New_change_clears_redo()
    {
        var doc = new AnnotationDocument();
        doc.Add(Box);
        doc.Undo();
        doc.Add(Arrow);

        Assert.False(doc.CanRedo);
        Assert.Equal([Arrow], doc.Items);
    }

    [Fact]
    public void Hit_test_returns_topmost()
    {
        var doc = new AnnotationDocument();
        doc.Add(Box);
        doc.Add(Arrow);

        Assert.Equal(1, doc.HitTest(5, 1, 2));
        Assert.Equal(0, doc.HitTest(5, 8, 2));
        Assert.Equal(-1, doc.HitTest(50, 50, 2));
    }

    [Fact]
    public void Remove_is_undoable()
    {
        var doc = new AnnotationDocument();
        doc.Add(Box);
        doc.RemoveAt(0);
        Assert.Empty(doc.Items);
        doc.Undo();
        Assert.Equal([Box], doc.Items);
    }
}
