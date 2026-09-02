namespace MauriPDF.Core.Rendering;

/// <summary>A PDF page size measured in points, where 72 points equal one inch.</summary>
public readonly record struct PdfPageSize(double WidthPoints, double HeightPoints);
