using MauriPDF.Core.Documents;

namespace MauriPDF.Editing;

/// <summary>UI-independent edit history. Entries retain affected page values, positions and revision IDs, not whole snapshots.</summary>
public sealed class DocumentEditSession
{
    public const int MaximumHistory = 100;
    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];
    private long _nextRevision;
    private long _baselineRevision;
    public const int MaximumLogicalPages = 100_000;
    public DocumentSourceRegistry Sources { get; } = new();
    public DocumentPageId? SuggestedCurrentPageId { get; private set; }

    public DocumentEditSession(PdfSourceDocument source) : this(source.PageCount, source.Id)
    {
        Sources.Add(source);
    }

    public DocumentEditSession(int sourcePageCount) : this(sourcePageCount, Guid.NewGuid()) { }

    private DocumentEditSession(int sourcePageCount, Guid source)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourcePageCount);
        if (sourcePageCount > MaximumLogicalPages) throw new InvalidOperationException("A composition is limited to 100,000 logical pages.");
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

    public void MarkSavedBaseline(long revision)
    {
        if (revision != State.Revision) throw new InvalidOperationException("Only the current edit snapshot can become the saved baseline.");
        _baselineRevision = revision;
    }

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

    /// <summary>One atomic range insertion and one history entry; validation precedes all mutation.</summary>
    public void Import(PdfSourceDocument source, int first, int count, int insertionIndex, DocumentPageId currentPage)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (first < 0 || count <= 0 || (long)first + count > source.PageCount) throw new ArgumentOutOfRangeException(nameof(first));
        if (insertionIndex < 0 || insertionIndex > State.Pages.Count) throw new ArgumentOutOfRangeException(nameof(insertionIndex));
        if (State.LogicalIndex(currentPage) is null) throw new ArgumentException("The current page must belong to the composition.", nameof(currentPage));
        if ((long)State.Pages.Count + count > MaximumLogicalPages) throw new InvalidOperationException("A composition is limited to 100,000 logical pages.");
        Sources.ValidateAddition(source);
        LogicalPageReference[] imported = Enumerable.Range(first, count)
            .Select(index => new LogicalPageReference(new(source.Id, index, Guid.NewGuid()), default)).ToArray();
        Entry entry = new(default, default, -1, insertionIndex, State.Revision, _nextRevision + 1, imported, currentPage);
        // Construct the immutable result before publishing registry/state/history changes.
        var pages = State.Pages.ToList();
        pages.InsertRange(insertionIndex, imported);
        EditedDocumentState next = new(State.SourceDocumentId, entry.AfterRevision, pages.ToArray());
        Sources.Add(source);
        _nextRevision++;
        State = next; EditGeneration++;
        SuggestedCurrentPageId = imported[0].Id;
        _undo.Add(entry);
        if (_undo.Count > MaximumHistory) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public PdfMaterializationPlan CreateMaterializationPlan() => PdfMaterializationPlan.From(State, Sources.Sources.Values);

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
        SuggestedCurrentPageId = null;
        if (entry.Imported is not null)
        {
            if (forward) pages.InsertRange(entry.AfterIndex, entry.Imported);
            else pages.RemoveRange(entry.AfterIndex, entry.Imported.Length);
            SuggestedCurrentPageId = forward ? entry.Imported[0].Id : entry.PreviousCurrent;
            State = new(State.SourceDocumentId, forward ? entry.AfterRevision : entry.BeforeRevision, pages.ToArray());
            EditGeneration++;
            return;
        }
        int remove = forward ? entry.BeforeIndex : entry.AfterIndex;
        int insert = forward ? entry.AfterIndex : entry.BeforeIndex;
        if (remove >= 0) pages.RemoveAt(remove);
        if (insert >= 0) pages.Insert(insert, forward ? entry.After : entry.Before);
        State = new(State.SourceDocumentId, forward ? entry.AfterRevision : entry.BeforeRevision, pages.ToArray());
        EditGeneration++;
    }

    private readonly record struct Entry(LogicalPageReference Before, LogicalPageReference After,
        int BeforeIndex, int AfterIndex, long BeforeRevision, long AfterRevision,
        LogicalPageReference[]? Imported = null, DocumentPageId PreviousCurrent = default);
}
