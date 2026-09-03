namespace MauriPDF.Core.Viewing;

public enum ViewerDisplayMode
{
    Continuous,
    SinglePage
}

public enum ViewerZoomMode
{
    Manual,
    FitPage,
    FitWidth
}

/// <summary>Immutable navigation state for one open document; page indices are zero-based.</summary>
public sealed record ViewerState
{
    public const int MinimumZoom = 25;
    public const int MaximumZoom = 500;
    public const int ZoomStep = 25;

    public ViewerState(int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);
        PageCount = pageCount;
    }

    public int PageCount { get; }
    public int PageIndex { get; private init; }
    public int ZoomPercent { get; private init; } = 100;
    public ViewerZoomMode ZoomMode { get; private init; }
    public ViewerDisplayMode DisplayMode { get; private init; }
    public VisualRotation Rotation { get; private init; }
    public ViewerState RotateClockwise() => this with { Rotation = Rotation.Clockwise() };
    public ViewerState RotateCounterClockwise() => this with { Rotation = Rotation.CounterClockwise() };
    public ViewerState SetDisplayMode(ViewerDisplayMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return this with { DisplayMode = mode };
    }
    public bool CanGoPrevious => PageIndex > 0;
    public bool CanGoNext => PageIndex < PageCount - 1;

    public ViewerState GoToPage(int pageNumber) => this with
    {
        PageIndex = Math.Clamp(pageNumber, 1, PageCount) - 1
    };

    public ViewerState PreviousPage() => GoToPage(PageIndex);
    public ViewerState NextPage() => GoToPage(PageIndex + (CanGoNext ? 2 : 1));

    public ViewerState SetZoom(int percent) => this with
    {
        ZoomPercent = Math.Clamp(percent, MinimumZoom, MaximumZoom),
        ZoomMode = ViewerZoomMode.Manual
    };

    // Fit modes retain the last manual percentage; +/- resumes from that value.
    public ViewerState ZoomIn() => SetZoom(ZoomPercent + ZoomStep);
    public ViewerState ZoomOut() => SetZoom(ZoomPercent - ZoomStep);

    public ViewerState SetFitMode(ViewerZoomMode mode)
    {
        if (mode is not (ViewerZoomMode.FitPage or ViewerZoomMode.FitWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return this with { ZoomMode = mode };
    }
}
