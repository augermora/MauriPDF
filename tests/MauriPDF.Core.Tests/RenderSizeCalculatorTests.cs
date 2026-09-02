using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class RenderSizeCalculatorTests
{
    [Theory]
    [InlineData(25, 24)]
    [InlineData(100, 96)]
    [InlineData(200, 192)]
    [InlineData(500, 480)]
    public void ManualZoomUses96Dpi(int percent, int expected)
    {
        Assert.Equal(new RenderSize(expected, expected), RenderSizeCalculator.Calculate(
            new PdfPageSize(72, 72), new ViewerState(1).SetZoom(percent), 800, 600));
    }

    [Theory]
    [InlineData(600, 800, 800, 600, 450, 600)]
    [InlineData(800, 600, 600, 800, 600, 450)]
    [InlineData(600, 800, 300, 400, 300, 400)]
    public void FitPageUsesLimitingDimension(double pw, double ph, int vw, int vh, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new RenderSize(expectedWidth, expectedHeight), RenderSizeCalculator.Calculate(
            new PdfPageSize(pw, ph), new ViewerState(1).SetFitMode(ViewerZoomMode.FitPage), vw, vh));
    }

    [Theory]
    [InlineData(800, 600, 780, 1040)]
    [InlineData(300, 600, 300, 400)]
    public void FitWidthReservesVerticalScrollbarOnlyWhenNeeded(int vw, int vh, int width, int height)
    {
        Assert.Equal(new RenderSize(width, height), RenderSizeCalculator.Calculate(
            new PdfPageSize(600, 800), new ViewerState(1).SetFitMode(ViewerZoomMode.FitWidth), vw, vh, 20));
    }

    [Fact]
    public void FractionalPagePreservesAspectRatioWithinOnePixel()
    {
        PdfPageSize page = new(595.28, 841.89);
        RenderSize size = RenderSizeCalculator.Calculate(page,
            new ViewerState(1).SetFitMode(ViewerZoomMode.FitPage), 713, 911);
        Assert.True(size.Width <= 713 && size.Height <= 911);
        Assert.InRange(Math.Abs(size.Width - size.Height * page.WidthPoints / page.HeightPoints), 0, 1);
    }

    [Fact]
    public void ResizeChangesFitButNotManualTarget()
    {
        PdfPageSize page = new(600, 800);
        ViewerState manual = new(1);
        ViewerState fit = manual.SetFitMode(ViewerZoomMode.FitPage);
        Assert.Equal(RenderSizeCalculator.Calculate(page, manual, 800, 600), RenderSizeCalculator.Calculate(page, manual, 400, 300));
        Assert.NotEqual(RenderSizeCalculator.Calculate(page, fit, 800, 600), RenderSizeCalculator.Calculate(page, fit, 400, 300));
    }

    [Theory]
    [InlineData(0, 72)]
    [InlineData(72, -1)]
    [InlineData(double.NaN, 72)]
    [InlineData(double.PositiveInfinity, 72)]
    public void InvalidPageSizeIsRejected(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RenderSizeCalculator.Calculate(new PdfPageSize(width, height), new ViewerState(1), 800, 600));
    }
}
