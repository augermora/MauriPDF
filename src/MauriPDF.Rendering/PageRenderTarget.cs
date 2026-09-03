using MauriPDF.Core.Viewing;

namespace MauriPDF.Rendering;

public readonly record struct PageRenderTarget(int PageIndex, RenderSize Size, VisualRotation Rotation = default);
