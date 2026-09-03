namespace MauriPDF.Core.Outline;

/// <summary>Immutable bookmark data; a null page index means no supported local destination.</summary>
public sealed class PdfOutlineNode
{
    public PdfOutlineNode(string title, int? pageIndex, IEnumerable<PdfOutlineNode> children)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(children);
        if (pageIndex.HasValue) ArgumentOutOfRangeException.ThrowIfNegative(pageIndex.Value);
        Title = title;
        PageIndex = pageIndex;
        Children = Array.AsReadOnly(children.ToArray());
    }

    public string Title { get; }
    public int? PageIndex { get; }
    public IReadOnlyList<PdfOutlineNode> Children { get; }
}
