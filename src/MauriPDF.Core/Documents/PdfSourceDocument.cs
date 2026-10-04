using MauriPDF.Core.Rendering;

namespace MauriPDF.Core.Documents;

/// <summary>Immutable source metadata. No native handles, images or extracted text.</summary>
public sealed class PdfSourceDocument
{
    public PdfSourceDocument(Guid id, string path, IEnumerable<PdfPageSize> pages)
    {
        if (id == Guid.Empty) throw new ArgumentException("A source identity is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(pages);
        Id = id; Path = path;
        Pages = Array.AsReadOnly(pages.ToArray());
        if (Pages.Count == 0) throw new ArgumentException("A source must contain pages.", nameof(pages));
        if (Pages.Any(page => !double.IsFinite(page.WidthPoints) || !double.IsFinite(page.HeightPoints)
            || page.WidthPoints <= 0 || page.HeightPoints <= 0))
            throw new ArgumentException("Source page dimensions must be finite and positive.", nameof(pages));
    }
    public Guid Id { get; }
    public string Path { get; }
    // PDFium metadata already incorporates intrinsic orientation/CropBox. Structural rotation is additional.
    public IReadOnlyList<PdfPageSize> Pages { get; }
    public int PageCount => Pages.Count;
}
