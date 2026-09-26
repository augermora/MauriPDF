namespace MauriPDF.App.Presentation;

/// <summary>Always derive device dimensions from immutable 96-DPI values, never previous bounds.</summary>
internal static class DisplayMetrics
{
    public static int Scale(int logical, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        return checked((int)Math.Round(logical * dpi / 96.0, MidpointRounding.AwayFromZero));
    }

    // The empty managed ImageList is only a native Details-row metric. Never exceed its safe limit.
    public static int ThumbnailRowHeight(int dpi) => Math.Min(256, Scale(244, dpi));
}
