using MauriPDF.Core.Rendering;

namespace MauriPDF.Core.Viewing;

public static class ThumbnailSizeCalculator
{
    public const int TargetWidth = 144;
    public const int MaximumHeight = 512;

    public static RenderSize Calculate(PdfPageSize page)
    {
        if (!double.IsFinite(page.WidthPoints) || !double.IsFinite(page.HeightPoints)
            || page.WidthPoints <= 0 || page.HeightPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        // Unusually tall pages shrink uniformly to keep thumbnail allocations lightweight.
        double scale = Math.Min(TargetWidth / page.WidthPoints, MaximumHeight / page.HeightPoints);
        return new RenderSize(Math.Max(1, (int)Math.Floor(page.WidthPoints * scale)),
            Math.Max(1, (int)Math.Floor(page.HeightPoints * scale)));
    }
}
