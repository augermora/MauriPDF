namespace MauriPDF.Core.Rendering;

/// <summary>Owns an open PDF document and releases it when disposed.</summary>
public interface IPdfRenderSession : IDisposable
{
    int PageCount { get; }

    PdfPageSize GetPageSize(int pageIndex);

    /// <summary>Renders a page at the requested pixel size. The caller owns and must dispose the result.</summary>
    RenderedPage RenderPage(int pageIndex, int pixelWidth, int pixelHeight);
}
