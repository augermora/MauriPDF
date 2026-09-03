namespace MauriPDF.Core.Rendering;

/// <summary>Owns an open PDF document and releases it when disposed.</summary>
public interface IPdfRenderSession : IDisposable
{
    int PageCount { get; }

    PdfPageSize GetPageSize(int pageIndex);

    /// <summary>Renders a page at the requested pixel size. The caller owns and must dispose the result.</summary>
    RenderedPage RenderPage(int pageIndex, int pixelWidth, int pixelHeight, Viewing.VisualRotation rotation = default);

    /// <summary>Extracts immutable text-layer data. The implementation closes all temporary native handles before return.</summary>
    Text.PdfTextPage ExtractText(int pageIndex);

    /// <summary>Copies a bounded outline snapshot while the document is alive. No native ownership escapes.</summary>
    Outline.PdfOutline ExtractOutline();
}
