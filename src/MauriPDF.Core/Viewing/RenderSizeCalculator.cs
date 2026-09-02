using MauriPDF.Core.Rendering;

namespace MauriPDF.Core.Viewing;

public readonly record struct RenderSize(int Width, int Height);

public static class RenderSizeCalculator
{
    /// <summary>100% is 96 pixels per inch (PDF dimensions use 72 points per inch).</summary>
    public static RenderSize Calculate(
        PdfPageSize page, ViewerState state, int viewportWidth, int viewportHeight,
        int verticalScrollbarWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!double.IsFinite(page.WidthPoints) || !double.IsFinite(page.HeightPoints)
            || page.WidthPoints <= 0 || page.HeightPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewportHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(verticalScrollbarWidth);

        double scale;
        switch (state.ZoomMode)
        {
            case ViewerZoomMode.FitPage:
                scale = Math.Min(viewportWidth / page.WidthPoints, viewportHeight / page.HeightPoints);
                break;
            case ViewerZoomMode.FitWidth:
                int width = viewportWidth;
                if (page.HeightPoints * width / page.WidthPoints > viewportHeight)
                {
                    width = Math.Max(1, width - verticalScrollbarWidth);
                }

                scale = width / page.WidthPoints;
                break;
            default:
                scale = 96.0 / 72 * state.ZoomPercent / 100;
                break;
        }

        // Manual zoom uses nearest pixels; fit sizes round down to stay inside the viewport.
        double widthPixels = page.WidthPoints * scale;
        double heightPixels = page.HeightPoints * scale;
        return new RenderSize(
            Math.Max(1, checked((int)(state.ZoomMode == ViewerZoomMode.Manual ? Math.Round(widthPixels) : Math.Floor(widthPixels)))),
            Math.Max(1, checked((int)(state.ZoomMode == ViewerZoomMode.Manual ? Math.Round(heightPixels) : Math.Floor(heightPixels)))));
    }
}
