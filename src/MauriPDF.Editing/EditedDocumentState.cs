using MauriPDF.Core.Documents;

namespace MauriPDF.Editing;

/// <summary>Immutable lightweight snapshot for presentation and future materialization, not a modified PDF.</summary>
public sealed class EditedDocumentState
{
    private readonly Dictionary<DocumentPageId, int> _positions;

    internal EditedDocumentState(Guid sourceDocumentId, long revision, LogicalPageReference[] pages)
    {
        SourceDocumentId = sourceDocumentId;
        Revision = revision;
        Pages = Array.AsReadOnly(pages); // Caller transfers its private array; never exposed for mutation.
        _positions = new(pages.Length);
        for (int index = 0; index < pages.Length; index++) _positions.Add(pages[index].Id, index);
    }

    public Guid SourceDocumentId { get; }
    public long Revision { get; }
    public IReadOnlyList<LogicalPageReference> Pages { get; }
    public int? LogicalIndex(DocumentPageId id) => _positions.TryGetValue(id, out int index) ? index : null;
    public int? LogicalIndexForSource(int sourcePageIndex) => LogicalIndex(new(SourceDocumentId, sourcePageIndex));

    /// <summary>Keep the current stable page; deletion falls forward, or backward at the end.</summary>
    public int ResolveCurrentPage(EditedDocumentState previous, int previousIndex) =>
        LogicalIndex(previous.Pages[previousIndex].Id) ?? Math.Min(previousIndex, Pages.Count - 1);

    public bool HasSameSequence(EditedDocumentState other) =>
        Pages.Count == other.Pages.Count && Pages.Select(page => page.Id).SequenceEqual(other.Pages.Select(page => page.Id));
}
