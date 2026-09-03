using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class ThumbnailSizeCalculatorTests
{
    [Theory]
    [InlineData(72, 100, 144, 200)]
    [InlineData(144, 72, 144, 72)]
    [InlineData(72, 72, 144, 144)]
    [InlineData(72, 720, 51, 512)]
    public void ThumbnailSizePreservesAspectRatioAndBounds(double width, double height, int expectedWidth, int expectedHeight)
    {
        RenderSize result = ThumbnailSizeCalculator.Calculate(new PdfPageSize(width, height));
        Assert.Equal(new RenderSize(expectedWidth, expectedHeight), result);
        Assert.InRange(result.Width, 1, 144);
        Assert.InRange(result.Height, 1, 512);
        Assert.InRange(Math.Abs(result.Width - result.Height * width / height), 0, 1);
    }

    [Theory]
    [InlineData(0, 72)]
    [InlineData(72, -1)]
    [InlineData(double.NaN, 72)]
    [InlineData(72, double.PositiveInfinity)]
    public void InvalidPageDimensionsAreRejected(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ThumbnailSizeCalculator.Calculate(new PdfPageSize(width, height)));
    }
}
