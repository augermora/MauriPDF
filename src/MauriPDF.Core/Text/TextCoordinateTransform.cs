namespace MauriPDF.Core.Text;

/// <summary>Mapping between normalized page geometry and any display rectangle, never a Bitmap.</summary>
public static class TextCoordinateTransform
{
    public static TextPoint ToPage(double x, double y, double left, double top, double width, double height) =>
        new((x - left) / width, (y - top) / height);

    public static TextBounds ToDisplay(TextBounds box, double left, double top, double width, double height) =>
        new(left + box.Left * width, top + box.Top * height, left + box.Right * width, top + box.Bottom * height);
}
