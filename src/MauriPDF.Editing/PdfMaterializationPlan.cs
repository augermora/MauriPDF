namespace MauriPDF.Editing;

public readonly record struct PdfPageMaterialization(int SourcePageIndex, int StructuralRotationDegrees);

/// <summary>Immutable, UI-free snapshot of the physical pages a writer must create.</summary>
public sealed class PdfMaterializationPlan
{
    private PdfMaterializationPlan(PdfPageMaterialization[] pages, long revision)
    {
        Pages = Array.AsReadOnly(pages);
        Revision = revision;
    }

    public IReadOnlyList<PdfPageMaterialization> Pages { get; }
    public long Revision { get; }

    public static PdfMaterializationPlan From(EditedDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state.Pages.Select(page => new PdfPageMaterialization(
            page.SourcePageIndex, page.StructuralRotation.Degrees)).ToArray(), state.Revision);
    }
}

public readonly record struct PdfMaterializationResult(int PageCount, long FileLength);

public interface IPdfDocumentMaterializer
{
    Task<PdfMaterializationResult> MaterializeAsync(
        string sourcePath, string destinationPath, PdfMaterializationPlan plan, CancellationToken cancellationToken = default);
}
