namespace MauriPDF.Core.Outline;

/// <summary>A document-order snapshot, containing no native resources and requiring no disposal.</summary>
public sealed class PdfOutline
{
    public const int MaximumNodes = 2048;
    public const int MaximumDepth = 32;
    public const int MaximumTitleLength = 512;

    public PdfOutline(IEnumerable<PdfOutlineNode> roots, bool wasLimited = false)
    {
        ArgumentNullException.ThrowIfNull(roots);
        Roots = Array.AsReadOnly(roots.ToArray());
        WasLimited = wasLimited;
    }

    public IReadOnlyList<PdfOutlineNode> Roots { get; }
    /// <summary>Some data was omitted/replaced because of limits or malformed structure/titles.</summary>
    public bool WasLimited { get; }
}
