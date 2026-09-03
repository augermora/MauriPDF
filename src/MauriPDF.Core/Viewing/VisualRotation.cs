using MauriPDF.Core.Rendering;
using MauriPDF.Core.Text;

namespace MauriPDF.Core.Viewing;

/// <summary>Additional clockwise view rotation, never a PDF edit or intrinsic page rotation.</summary>
public readonly record struct VisualRotation
{
    public VisualRotation(int degrees)
    {
        if (degrees % 90 != 0) throw new ArgumentOutOfRangeException(nameof(degrees));
        Degrees = (degrees % 360 + 360) % 360;
    }

    public int Degrees { get; }
    public int QuarterTurns => Degrees / 90;
    public bool SwapsDimensions => QuarterTurns % 2 != 0;
    public VisualRotation Clockwise() => new(Degrees + 90);
    public VisualRotation CounterClockwise() => new(Degrees - 90);
    public PdfPageSize EffectiveSize(PdfPageSize size) => SwapsDimensions ? new(size.HeightPoints, size.WidthPoints) : size;

    /// <summary>Normalized top-left coordinates, after the PDF's intrinsic orientation.</summary>
    public TextPoint ToDisplay(TextPoint point) => Degrees switch
    {
        90 => new(1 - point.Y, point.X),
        180 => new(1 - point.X, 1 - point.Y),
        270 => new(point.Y, 1 - point.X),
        _ => point
    };

    public TextPoint ToPage(TextPoint point) => new VisualRotation(-Degrees).ToDisplay(point);
}
