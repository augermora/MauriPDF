using System.Drawing.Imaging;
using MauriPDF.Core.Rendering;

namespace MauriPDF.App;

internal static class WinFormsImageConverter
{
    public static unsafe Bitmap CreateBitmap(RenderedPage renderedPage)
    {
        ArgumentNullException.ThrowIfNull(renderedPage);

        if (renderedPage.PixelFormat != RenderedPixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Unsupported rendered pixel format: {renderedPage.PixelFormat}.");
        }

        Bitmap bitmap = new(renderedPage.Width, renderedPage.Height, PixelFormat.Format32bppArgb);
        try
        {
            Rectangle bounds = new(0, 0, bitmap.Width, bitmap.Height);
            BitmapData bitmapData = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                ReadOnlySpan<byte> source = renderedPage.Pixels.Span;
                for (int row = 0; row < renderedPage.Height; row++)
                {
                    ReadOnlySpan<byte> sourceRow = source.Slice(row * renderedPage.Stride, renderedPage.Width * 4);
                    Span<byte> destinationRow = new(
                        (byte*)bitmapData.Scan0 + (row * bitmapData.Stride),
                        renderedPage.Width * 4);
                    sourceRow.CopyTo(destinationRow);
                }
            }

            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
