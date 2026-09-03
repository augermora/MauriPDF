namespace MauriPDF.Core.Viewing;

/// <summary>Bounds UI bitmap copies independently of the neutral render cache.</summary>
public static class VisiblePageDemand
{
    public const int MaximumPages = 32;
    public const long BitmapBudgetBytes = 64L * 1024 * 1024;
    public const int MaximumRasterDimension = 16_384;

    public static PageRange Select(ContinuousPageLayout layout, double top, double height)
    {
        PageRange visible = layout.Visible(top, height);
        if (visible.Count <= MaximumPages) return visible;
        int center = layout.CurrentPage(top, height);
        int first = Math.Clamp(center - MaximumPages / 2, visible.First, visible.Last - MaximumPages + 1);
        return new(first, first + MaximumPages - 1);
    }

    public static RenderSize Target(PageGeometry page, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        long pixelBudget = BitmapBudgetBytes / 4 / count;
        double scale = Math.Min(1, Math.Min(Math.Sqrt(pixelBudget / ((double)page.Width * page.Height)),
            (double)MaximumRasterDimension / Math.Max(page.Width, page.Height)));
        int width = Math.Max(1, (int)Math.Floor(page.Width * scale));
        int height = Math.Max(1, (int)Math.Floor(page.Height * scale));
        // Extremely thin pages may have rounded one dimension up to one pixel.
        width = (int)Math.Min(width, pixelBudget / height);
        if (width == 0) { width = 1; height = (int)pixelBudget; }
        return new(width, height);
    }
}
