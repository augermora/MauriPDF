namespace MauriPDF.Editing;

public readonly record struct PdfPageMaterialization(int SourcePageIndex, int StructuralRotationDegrees, Guid SourceDocumentId = default);

/// <summary>Immutable, UI-free snapshot of the physical pages a writer must create.</summary>
public sealed class PdfMaterializationPlan
{
    private PdfMaterializationPlan(PdfPageMaterialization[] pages, long revision, IEnumerable<Core.Documents.PdfSourceDocument>? sources)
    {
        Pages = Array.AsReadOnly(pages);
        Revision = revision;
        Sources = new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, Core.Documents.PdfSourceDocument>(
            sources?.ToDictionary(source => source.Id) ?? []);
        if (Sources.Count == 0 && pages.Select(page => page.SourceDocumentId).Distinct().Skip(1).Any())
            throw new ArgumentException("A multi-source plan requires source descriptors.", nameof(sources));
    }

    public IReadOnlyList<PdfPageMaterialization> Pages { get; }
    public long Revision { get; }
    public IReadOnlyDictionary<Guid, Core.Documents.PdfSourceDocument> Sources { get; }
    public string SourcePath(Guid id, string legacySourcePath) => Sources.Count == 0 ? legacySourcePath
        : Sources.TryGetValue(id, out var source) ? source.Path : throw new InvalidDataException("The snapshot refers to an unknown source.");

    public static PdfMaterializationPlan From(EditedDocumentState state, IEnumerable<Core.Documents.PdfSourceDocument>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state.Pages.Select(page => new PdfPageMaterialization(
            page.SourcePageIndex, page.StructuralRotation.Degrees, page.SourceDocumentId)).ToArray(), state.Revision, sources);
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
