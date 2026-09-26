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

public enum PdfDestinationPolicy
{
    OverwriteApproved,
    RequireExpectedIdentity,
    RequireMissing
}

public sealed record PdfMaterializationRequest(
    string SourcePath,
    string DestinationPath,
    PdfMaterializationPlan Plan,
    PdfDestinationPolicy DestinationPolicy,
    SavedFileIdentity? ExpectedDestinationIdentity = null);

public readonly record struct PdfMaterializationResult(
    int PageCount, long FileLength, SavedFileIdentity DestinationIdentity);

public enum PdfDestinationConflictKind { Changed, Missing, UnexpectedlyExists }

public sealed class PdfDestinationConflictException : IOException
{
    public PdfDestinationConflictException(PdfDestinationConflictKind kind, string message) : base(message) => Kind = kind;
    public PdfDestinationConflictKind Kind { get; }
}

public interface IPdfDocumentMaterializer
{
    Task<PdfMaterializationResult> MaterializeAsync(
        PdfMaterializationRequest request, CancellationToken cancellationToken = default);
}
