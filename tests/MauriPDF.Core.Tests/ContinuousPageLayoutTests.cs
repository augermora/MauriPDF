using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class ContinuousPageLayoutTests
{
    [Fact]
    public void MixedPagesHaveIndependentGeometryAndConstantGaps()
    {
        ContinuousPageLayout layout = new([new(72, 144), new(144, 72)], new(2), 800, 600);
        Assert.Equal(new PageGeometry(0, 96, 192, 16), layout[0]);
        Assert.Equal(new PageGeometry(1, 192, 96, 224), layout[1]);
        Assert.Equal(336, layout.Height);
        Assert.Equal(224, layout.Width);
        Assert.Equal(352, layout.Left(0, 800));
    }

    [Fact]
    public void VisibleIntersectionExcludesEdgesAndIncludesPartialPages()
    {
        ContinuousPageLayout layout = Squares(3);
        Assert.Equal(new PageRange(0, 0), layout.Visible(0, 112));
        Assert.Equal(new PageRange(0, 1), layout.Visible(100, 50));
        Assert.Equal(new PageRange(0, -1), layout.Visible(112, 16));
        Assert.Equal(new PageRange(1, 1), layout.Visible(128, 1));
        Assert.Equal(new PageRange(0, -1), layout.Visible(10000, 100));
        Assert.Equal(new PageRange(0, -1), layout.Visible(20, 0));
    }

    [Fact]
    public void OverscanIsSmallAndClampedToDocument()
    {
        ContinuousPageLayout layout = Squares(5);
        Assert.Equal(new PageRange(0, 2), layout.Visible(128, 20, 1));
        Assert.Equal(new PageRange(0, 1), layout.Visible(16, 20, 1));
        Assert.Equal(new PageRange(3, 4), layout.Visible(464, 20, 1));
    }

    [Theory]
    [InlineData(16, 0)]
    [InlineData(119, 0)]
    [InlineData(120, 0)]
    [InlineData(121, 1)]
    [InlineData(128, 1)]
    [InlineData(10000, 2)]
    public void CurrentPageUsesCenterAndEarlierPageWinsGapTie(double center, int expected)
    {
        Assert.Equal(expected, Squares(3).CurrentPage(center - 10, 20));
    }

    [Fact]
    public void NavigationCentersShortPagesAndAlignsTallPagesAtTop()
    {
        ContinuousPageLayout layout = Squares(10);
        double shortTarget = layout.ScrollTarget(4, 200);
        Assert.Equal(4, layout.CurrentPage(shortTarget, 200));
        Assert.Equal(layout[4].Top + 48 - 100, shortTarget);
        Assert.Equal(layout[4].Top, layout.ScrollTarget(4, 50));
        Assert.Equal(-436, layout.ScrollTarget(0, 1000)); // Outer gutter keeps a short first page at the viewport center.
        Assert.Equal(0, layout.CurrentPage(layout.ScrollTarget(0, 1000), 1000));
    }

    [Fact]
    public void ZoomPreservesPageAndRelativeCenterPosition()
    {
        PdfPageSize[] pages = Enumerable.Repeat(new PdfPageSize(600, 800), 20).ToArray();
        ContinuousPageLayout original = new(pages, new(20), 800, 600);
        ReadingAnchor anchor = original.CaptureAnchor(original[8].Top + 100, 600);
        ContinuousPageLayout zoomed = new(pages, new ViewerState(20).SetZoom(200), 800, 600);
        ReadingAnchor restored = zoomed.CaptureAnchor(zoomed.RestoreAnchor(anchor, 600), 600);
        Assert.Equal(anchor.PageIndex, restored.PageIndex);
        Assert.Equal(anchor.Fraction, restored.Fraction, 10);
    }

    [Fact]
    public void FitWidthSizesEachMixedPageToUsableWidth()
    {
        ContinuousPageLayout layout = new([new(72, 144), new(144, 72)],
            new ViewerState(2).SetFitMode(ViewerZoomMode.FitWidth), 432, 600);
        Assert.Equal(400, layout[0].Width);
        Assert.Equal(800, layout[0].Height);
        Assert.Equal(400, layout[1].Width);
        Assert.Equal(200, layout[1].Height);
    }

    [Fact]
    public void FitPageAppliesReferenceScaleToEveryPage()
    {
        ContinuousPageLayout layout = new([new(100, 200), new(200, 100)],
            new ViewerState(2).SetFitMode(ViewerZoomMode.FitPage), 432, 432);
        Assert.Equal(200, layout[0].Width);
        Assert.Equal(400, layout[0].Height);
        Assert.Equal(400, layout[1].Width);
        Assert.Equal(200, layout[1].Height);
        ContinuousPageLayout referenceChanged = new([new(100, 200), new(200, 100)],
            new ViewerState(2).GoToPage(2).SetFitMode(ViewerZoomMode.FitPage), 432, 132);
        Assert.Equal(100, referenceChanged[0].Width);
        Assert.Equal(200, referenceChanged[1].Width);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1001)]
    public void LargeDocumentsHaveOnlyGeometryAndBoundedDemand(int count)
    {
        ContinuousPageLayout layout = Squares(count);
        Assert.Equal(count, layout.Count);
        PageRange range = VisiblePageDemand.Select(layout, layout.ScrollTarget(count / 2, 600), 600);
        Assert.InRange(range.Count, 1, 7);
        Assert.InRange(layout.CurrentPage(layout.ScrollTarget(count / 2, 600), 600), 0, count - 1);
    }

    [Fact]
    public void ExtremelyLargeDocumentExtentDoesNotOverflowInt32()
    {
        ContinuousPageLayout layout = new(Enumerable.Repeat(new PdfPageSize(72, 10_000_000), 1001).ToArray(),
            new(1001), 800, 600);
        Assert.True(layout.Height > int.MaxValue);
        Assert.Equal(1000, layout.CurrentPage(layout.ScrollTarget(1000, 600), 600));
    }

    [Fact]
    public void TinyPagesAndHugeTargetsRemainCountAndByteBounded()
    {
        ContinuousPageLayout tiny = new(Enumerable.Repeat(new PdfPageSize(1, 1), 1001).ToArray(), new(1001), 800, 10000);
        PageRange demand = VisiblePageDemand.Select(tiny, 1000, 10000);
        Assert.Equal(32, demand.Count);
        foreach (PageGeometry page in new[] { new PageGeometry(0, 100000, 100000, 0), new(0, 1, 1000000000, 0), new(0, 1000000000, 1, 0) })
        {
            RenderSize size = VisiblePageDemand.Target(page, demand.Count);
            Assert.InRange((long)size.Width * size.Height * 4 * demand.Count, 1, VisiblePageDemand.BitmapBudgetBytes);
            Assert.InRange(size.Width, 1, VisiblePageDemand.MaximumRasterDimension);
            Assert.InRange(size.Height, 1, VisiblePageDemand.MaximumRasterDimension);
        }
    }

    private static ContinuousPageLayout Squares(int count) =>
        new(Enumerable.Repeat(new PdfPageSize(72, 72), count).ToArray(), new(count), 800, 600);
}
