using MauriPDF.Core.Viewing;

namespace MauriPDF.Core.Documents;

/// <summary>Stable within an opened source document; never a position in the edited sequence.</summary>
public readonly record struct SourcePageIdentity(Guid SourceDocumentId, int SourcePageIndex);

/// <summary>Logical instance identity. Empty InstanceId denotes the original instance; imports get fresh IDs.</summary>
public readonly record struct DocumentPageId(Guid SourceDocumentId, int SourcePageIndex, Guid InstanceId = default)
{
    public SourcePageIdentity Source => new(SourceDocumentId, SourcePageIndex);
}

/// <summary>In-memory structural edit delta, distinct from temporary viewer rotation.</summary>
public readonly record struct StructuralPageRotation
{
    public StructuralPageRotation(int degrees)
    {
        if (degrees % 90 != 0) throw new ArgumentOutOfRangeException(nameof(degrees));
        Degrees = (degrees % 360 + 360) % 360;
    }

    public int Degrees { get; }
    public StructuralPageRotation Clockwise() => new(Degrees + 90);
    public StructuralPageRotation CounterClockwise() => new(Degrees - 90);
    // PDFium already includes intrinsic rotation; only these additional deltas go to its device API.
    public VisualRotation Compose(VisualRotation visual) => new(Degrees + visual.Degrees);
}

/// <summary>A retained source page. Contains no native resources, pixels, text, or view state.</summary>
public readonly record struct LogicalPageReference(DocumentPageId Id, StructuralPageRotation StructuralRotation)
{
    public int SourcePageIndex => Id.SourcePageIndex;
    public Guid SourceDocumentId => Id.SourceDocumentId;
    public SourcePageIdentity Source => Id.Source;
    public VisualRotation DisplayRotation(VisualRotation visual) => StructuralRotation.Compose(visual);
}
