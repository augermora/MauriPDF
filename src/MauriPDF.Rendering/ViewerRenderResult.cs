using MauriPDF.Core.Rendering;

namespace MauriPDF.Rendering;

/// <summary>Caller-owned pixels, never shared with cache storage.</summary>
public sealed class ViewerRenderResult(RenderedPage pixels, bool fromCache) : IDisposable
{
    public RenderedPage Pixels { get; } = pixels;
    public bool FromCache { get; } = fromCache;
    public void Dispose() => Pixels.Dispose();
}
