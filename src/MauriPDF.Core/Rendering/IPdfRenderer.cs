namespace MauriPDF.Core.Rendering;

/// <summary>Opens local PDF documents for rendering.</summary>
/// <remarks>Disposing the renderer prevents new sessions; already-open sessions retain their own engine lease.</remarks>
public interface IPdfRenderer : IDisposable
{
    IPdfRenderSession Open(string filePath);
}
