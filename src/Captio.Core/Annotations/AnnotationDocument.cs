using System.Collections.Immutable;

namespace Captio.Core.Annotations;

/// <summary>
/// Ordered list of annotations with snapshot-based undo and redo.
/// </summary>
public sealed class AnnotationDocument
{
    private readonly Stack<ImmutableList<Annotation>> _undo = new();
    private readonly Stack<ImmutableList<Annotation>> _redo = new();

    public ImmutableList<Annotation> Items { get; private set; } = ImmutableList<Annotation>.Empty;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event EventHandler? Changed;

    public void Add(Annotation annotation) => Commit(Items.Add(annotation));

    public void Replace(int index, Annotation annotation) => Commit(Items.SetItem(index, annotation));

    public void RemoveAt(int index) => Commit(Items.RemoveAt(index));

    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Push(Items);
        Items = _undo.Pop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Push(Items);
        Items = _redo.Pop();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns the index of the topmost annotation under the point, or -1.</summary>
    public int HitTest(double x, double y, double tolerance)
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (Items[i].HitTest(x, y, tolerance)) return i;
        }
        return -1;
    }

    private void Commit(ImmutableList<Annotation> next)
    {
        _undo.Push(Items);
        _redo.Clear();
        Items = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
