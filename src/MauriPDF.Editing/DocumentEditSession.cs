using MauriPDF.Core.Documents;

namespace MauriPDF.Editing;

/// <summary>UI-independent edit history. Entries contain only one page value, positions and revision IDs.</summary>
public sealed class DocumentEditSession
{
    public const int MaximumHistory = 100;
    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];
    private long _nextRevision;
    private readonly long _baselineRevision;

    public DocumentEditSession(int sourcePageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourcePageCount);
        Guid source = Guid.NewGuid();
        LogicalPageReference[] pages = new LogicalPageReference[sourcePageCount];
        for (int index = 0; index < pages.Length; index++) pages[index] = new(new(source, index), default);
        State = new(source, 0, pages);
        _baselineRevision = State.Revision;
    }

    public EditedDocumentState State { get; private set; }
    public bool IsDirty => State.Revision != _baselineRevision;
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    /// <summary>Monotonic invalidation version, unlike the history revision which rewinds on Undo.</summary>
    public long EditGeneration { get; private set; }

    public bool Execute(DocumentEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (State.LogicalIndex(edit.PageId) is not int index) return false;
        LogicalPageReference before = State.Pages[index], after = before;
        int target = index;
        switch (edit)
        {
            case DeletePageEdit:
                if (State.Pages.Count == 1) return false;
                target = -1;
                break;
            case MovePageEdit move:
                if (move.TargetIndex < 0 || move.TargetIndex >= State.Pages.Count || move.TargetIndex == index) return false;
                target = move.TargetIndex;
                break;
            case RotatePageEdit rotate:
                after = before with { StructuralRotation = rotate.Clockwise ? before.StructuralRotation.Clockwise() : before.StructuralRotation.CounterClockwise() };
                break;
            default: throw new ArgumentException("Unsupported edit operation.", nameof(edit));
        }

        Entry entry = new(before, after, index, target, State.Revision, ++_nextRevision);
        Apply(entry, forward: true);
        _undo.Add(entry);
        if (_undo.Count > MaximumHistory) _undo.RemoveAt(0);
        _redo.Clear();
        return true;
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        Entry entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Apply(entry, forward: false);
        _redo.Add(entry);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        Entry entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Apply(entry, forward: true);
        _undo.Add(entry);
        return true;
    }

    private void Apply(Entry entry, bool forward)
    {
        List<LogicalPageReference> pages = State.Pages.ToList();
        int remove = forward ? entry.BeforeIndex : entry.AfterIndex;
        int insert = forward ? entry.AfterIndex : entry.BeforeIndex;
        if (remove >= 0) pages.RemoveAt(remove);
        if (insert >= 0) pages.Insert(insert, forward ? entry.After : entry.Before);
        State = new(State.SourceDocumentId, forward ? entry.AfterRevision : entry.BeforeRevision, pages.ToArray());
        EditGeneration++;
    }

    private readonly record struct Entry(LogicalPageReference Before, LogicalPageReference After,
        int BeforeIndex, int AfterIndex, long BeforeRevision, long AfterRevision);
}
