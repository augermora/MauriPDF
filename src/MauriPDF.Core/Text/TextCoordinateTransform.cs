using MauriPDF.Core.Viewing;

namespace MauriPDF.Core.Text;

/// <summary>Mapping between normalized page geometry and any display rectangle, never a Bitmap.</summary>
public static class TextCoordinateTransform
{
    public static TextPoint ToPage(double x, double y, double left, double top, double width, double height, VisualRotation rotation = default) =>
        rotation.ToPage(new((x - left) / width, (y - top) / height));

    public static TextBounds ToDisplay(TextBounds box, double left, double top, double width, double height, VisualRotation rotation = default)
    {
        TextPoint a = rotation.ToDisplay(new(box.Left, box.Top));
        TextPoint b = rotation.ToDisplay(new(box.Right, box.Bottom));
        return new(left + Math.Min(a.X, b.X) * width, top + Math.Min(a.Y, b.Y) * height,
            left + Math.Max(a.X, b.X) * width, top + Math.Max(a.Y, b.Y) * height);
    }
}
