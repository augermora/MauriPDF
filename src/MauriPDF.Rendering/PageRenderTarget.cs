using MauriPDF.Core.Viewing;

namespace MauriPDF.Rendering;

/// <summary>PageIndex is always a SOURCE index, never a position in an edited document.</summary>
public readonly record struct PageRenderTarget(int PageIndex, RenderSize Size, VisualRotation Rotation = default)
{
    public static PageRenderTarget FromLogicalPage(Core.Documents.LogicalPageReference page, RenderSize size, VisualRotation visual) =>
        new(page.SourcePageIndex, size, page.DisplayRotation(visual));
}
