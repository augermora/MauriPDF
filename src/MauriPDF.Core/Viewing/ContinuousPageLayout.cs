using MauriPDF.Core.Rendering;

namespace MauriPDF.Core.Viewing;

public readonly record struct PageGeometry(int PageIndex, int Width, int Height, double Top)
{
    public double Bottom => Top + Height;
}

public readonly record struct PageRange(int First, int Last)
{
    public int Count => Math.Max(0, Last - First + 1);
}

public readonly record struct ReadingAnchor(int PageIndex, double Fraction);

/// <summary>Geometry only. Double document coordinates avoid overflowing a native scroll extent.</summary>
public sealed class ContinuousPageLayout
{
    public const int Gap = 16;
    private readonly PageGeometry[] _pages;

    public ContinuousPageLayout(IReadOnlyList<PdfPageSize> pages, ViewerState state, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count != state.PageCount) throw new ArgumentException("Page count mismatch.", nameof(pages));
        int availableWidth = Math.Max(1, width - 2 * Gap);
        int availableHeight = Math.Max(1, height - 2 * Gap);
        PdfPageSize reference = pages[state.PageIndex];
        double fitScale = Math.Min(availableWidth / reference.WidthPoints, availableHeight / reference.HeightPoints);
        _pages = new PageGeometry[pages.Count];
        double top = Gap;
        for (int index = 0; index < pages.Count; index++)
        {
            PdfPageSize page = pages[index];
            if (!double.IsFinite(page.WidthPoints) || !double.IsFinite(page.HeightPoints)
                || page.WidthPoints <= 0 || page.HeightPoints <= 0) throw new ArgumentOutOfRangeException(nameof(pages));
            double scale = state.ZoomMode switch
            {
                ViewerZoomMode.FitWidth => availableWidth / page.WidthPoints,
                ViewerZoomMode.FitPage => fitScale,
                _ => 96.0 / 72 * state.ZoomPercent / 100
            };
            int w = Math.Max(1, checked((int)Math.Round(page.WidthPoints * scale)));
            int h = Math.Max(1, checked((int)Math.Round(page.HeightPoints * scale)));
            _pages[index] = new(index, w, h, top);
            Width = Math.Max(Width, (double)w + 2 * Gap);
            top += h + Gap;
        }
        Height = top;
    }

    public int Count => _pages.Length;
    public double Width { get; }
    public double Height { get; }
    public PageGeometry this[int index] => _pages[index];
    public double Left(int index, double viewportWidth) => (Math.Max(Width, viewportWidth) - _pages[index].Width) / 2;

    public PageRange Visible(double top, double height, int overscan = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(overscan);
        if (height <= 0) return new(0, -1);
        int first = LowerBound(top, bottom: true);
        int last = LowerBound(top + Math.Max(0, height), bottom: false) - 1;
        if (first > last) return new(0, -1);
        return new(Math.Max(0, first - overscan), Math.Min(Count - 1, last + overscan));
    }

    public int CurrentPage(double top, double viewportHeight)
    {
        double center = top + viewportHeight / 2;
        int next = LowerBound(center, bottom: true);
        if (next == Count) return Count - 1;
        if (next == 0 || center >= _pages[next].Top) return next;
        // A gap tie belongs to the earlier page.
        return center - _pages[next - 1].Bottom <= _pages[next].Top - center ? next - 1 : next;
    }

    public double ScrollTarget(int pageIndex, double viewportHeight) =>
        Math.Clamp(_pages[pageIndex].Top + Math.Min(_pages[pageIndex].Height, viewportHeight) / 2 - viewportHeight / 2,
            0, Math.Max(0, Height - viewportHeight));

    public ReadingAnchor CaptureAnchor(double top, double viewportHeight)
    {
        int index = CurrentPage(top, viewportHeight);
        return new(index, Math.Clamp((top + viewportHeight / 2 - _pages[index].Top) / _pages[index].Height, 0, 1));
    }

    public double RestoreAnchor(ReadingAnchor anchor, double viewportHeight) =>
        Math.Clamp(_pages[anchor.PageIndex].Top + _pages[anchor.PageIndex].Height * anchor.Fraction - viewportHeight / 2,
            0, Math.Max(0, Height - viewportHeight));

    private int LowerBound(double position, bool bottom)
    {
        int low = 0, high = Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (bottom ? _pages[middle].Bottom <= position : _pages[middle].Top < position) low = middle + 1;
            else high = middle;
        }
        return low;
    }
}
