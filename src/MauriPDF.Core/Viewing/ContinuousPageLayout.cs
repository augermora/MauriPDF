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

public readonly record struct ReadingAnchor(int PageIndex, double Fraction, double HorizontalFraction = .5);

/// <summary>Geometry only. Double document coordinates avoid overflowing a native scroll extent.</summary>
public sealed class ContinuousPageLayout
{
    public const int Gap = 16;
    private readonly PageGeometry[] _pages;
    private readonly int _firstPage;
    public VisualRotation Rotation { get; }
    public ViewerDisplayMode DisplayMode { get; }

    public ContinuousPageLayout(IReadOnlyList<PdfPageSize> pages, ViewerState state, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count != state.PageCount) throw new ArgumentException("Page count mismatch.", nameof(pages));
        int availableWidth = Math.Max(1, width - 2 * Gap);
        int availableHeight = Math.Max(1, height - 2 * Gap);
        Rotation = state.Rotation;
        DisplayMode = state.DisplayMode;
        _firstPage = DisplayMode == ViewerDisplayMode.SinglePage ? state.PageIndex : 0;
        PdfPageSize reference = Rotation.EffectiveSize(pages[state.PageIndex]);
        double fitScale = Math.Min(availableWidth / reference.WidthPoints, availableHeight / reference.HeightPoints);
        _pages = new PageGeometry[DisplayMode == ViewerDisplayMode.SinglePage ? 1 : pages.Count];
        double top = Gap;
        for (int offset = 0; offset < _pages.Length; offset++)
        {
            int index = _firstPage + offset;
            PdfPageSize page = Rotation.EffectiveSize(pages[index]);
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
            if (DisplayMode == ViewerDisplayMode.SinglePage) top = Math.Max(Gap, (height - h) / 2.0);
            _pages[offset] = new(index, w, h, top);
            Width = Math.Max(Width, (double)w + 2 * Gap);
            top += h + Gap;
        }
        Height = DisplayMode == ViewerDisplayMode.SinglePage ? Math.Max(height, top) : top;
    }

    public int Count => _pages.Length;
    public double Width { get; }
    public double Height { get; }
    public bool ContainsPage(int index) => index >= _firstPage && index < _firstPage + Count;
    public PageGeometry this[int index] => _pages[index - _firstPage];
    public double Left(int index, double viewportWidth) => (Math.Max(Width, viewportWidth) - this[index].Width) / 2;
    public bool PageScrollNavigates(double viewportHeight) => DisplayMode == ViewerDisplayMode.SinglePage && _pages[0].Height <= viewportHeight;

    // Outer scroll gutters let even a tiny first/last page remain the viewport-center page.
    // Inter-page geometry and its binary search remain unchanged.
    public double MinimumScrollTop(double viewportHeight) => DisplayMode == ViewerDisplayMode.SinglePage ? 0
        : Math.Min(0, _pages[0].Top + _pages[0].Height / 2.0 - viewportHeight / 2);
    public double MaximumScrollTop(double viewportHeight) => DisplayMode == ViewerDisplayMode.SinglePage ? Math.Max(0, Height - viewportHeight)
        : Math.Max(Math.Max(0, Height - viewportHeight), _pages[^1].Top + _pages[^1].Height / 2.0 - viewportHeight / 2);

    public PageRange Visible(double top, double height, int overscan = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(overscan);
        if (height <= 0) return new(0, -1);
        int first = LowerBound(top, bottom: true);
        int last = LowerBound(top + Math.Max(0, height), bottom: false) - 1;
        if (first > last) return new(0, -1);
        return new(_firstPage + Math.Max(0, first - overscan), _firstPage + Math.Min(Count - 1, last + overscan));
    }

    public int CurrentPage(double top, double viewportHeight)
    {
        if (DisplayMode == ViewerDisplayMode.SinglePage) return _firstPage;
        double center = top + viewportHeight / 2;
        int next = LowerBound(center, bottom: true);
        if (next == Count) return Count - 1;
        if (next == 0 || center >= _pages[next].Top) return next;
        // A gap tie belongs to the earlier page.
        return center - _pages[next - 1].Bottom <= _pages[next].Top - center ? next - 1 : next;
    }

    public double ScrollTarget(int pageIndex, double viewportHeight) =>
        Math.Clamp(this[pageIndex].Top + Math.Min(this[pageIndex].Height, viewportHeight) / 2 - viewportHeight / 2,
            MinimumScrollTop(viewportHeight), MaximumScrollTop(viewportHeight));

    public ReadingAnchor CaptureAnchor(double top, double viewportHeight, double left = 0, double viewportWidth = 0)
    {
        int index = CurrentPage(top, viewportHeight);
        double x = viewportWidth == 0 ? .5 : Math.Clamp((left + viewportWidth / 2 - Left(index, viewportWidth)) / this[index].Width, 0, 1);
        double y = Math.Clamp((top + viewportHeight / 2 - this[index].Top) / this[index].Height, 0, 1);
        var neutral = Rotation.ToPage(new(x, y));
        return new(index, neutral.Y, neutral.X);
    }

    public double RestoreAnchor(ReadingAnchor anchor, double viewportHeight) =>
        Math.Clamp(this[anchor.PageIndex].Top + this[anchor.PageIndex].Height * Rotation.ToDisplay(new(anchor.HorizontalFraction, anchor.Fraction)).Y - viewportHeight / 2,
            MinimumScrollTop(viewportHeight), MaximumScrollTop(viewportHeight));

    public double RestoreHorizontalAnchor(ReadingAnchor anchor, double viewportWidth) =>
        Math.Clamp(Left(anchor.PageIndex, viewportWidth) + this[anchor.PageIndex].Width * Rotation.ToDisplay(new(anchor.HorizontalFraction, anchor.Fraction)).X - viewportWidth / 2,
            0, Math.Max(0, Width - viewportWidth));

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
