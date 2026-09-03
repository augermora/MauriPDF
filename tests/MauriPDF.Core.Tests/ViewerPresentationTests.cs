using MauriPDF.Core.Rendering;
using MauriPDF.Core.Text;
using MauriPDF.Core.Viewing;
using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class ViewerPresentationTests
{
    [Theory]
    [InlineData(-450, 270)]
    [InlineData(-360, 0)]
    [InlineData(450, 90)]
    [InlineData(720, 0)]
    public void RotationNormalizesQuarterTurns(int input, int expected) => Assert.Equal(expected, new VisualRotation(input).Degrees);

    [Fact]
    public void RotationRejectsArbitraryAnglesAndCyclesBothWays()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VisualRotation(45));
        VisualRotation rotation = default;
        for (int turn = 0; turn < 4; turn++)
        {
            Assert.Equal(turn * 90, rotation.Degrees);
            Assert.Equal(rotation, rotation.Clockwise().CounterClockwise());
            rotation = rotation.Clockwise();
        }
        Assert.Equal(default, rotation);
        Assert.Equal(270, rotation.CounterClockwise().Degrees);
    }

    [Theory]
    [InlineData(0, .2, .3)]
    [InlineData(90, .7, .2)]
    [InlineData(180, .8, .7)]
    [InlineData(270, .3, .8)]
    public void TransformsAndInverseHitTestingAgree(int degrees, double x, double y)
    {
        VisualRotation rotation = new(degrees);
        TextPoint displayed = rotation.ToDisplay(new(.2, .3));
        Assert.Equal(x, displayed.X, 10);
        Assert.Equal(y, displayed.Y, 10);
        TextPoint page = TextCoordinateTransform.ToPage(17 + x * 300, 23 + y * 500, 17, 23, 300, 500, rotation);
        Assert.Equal(.2, page.X, 10);
        Assert.Equal(.3, page.Y, 10);
        PdfTextPage text = new([new(65, new(.1, .2, .3, .4))]);
        Assert.NotNull(text.HitTest(page, rotation.SwapsDimensions ? 500 : 300, rotation.SwapsDimensions ? 300 : 500, 0));
        TextBounds box = TextCoordinateTransform.ToDisplay(text[0].Bounds, 17, 23, 300, 500, rotation);
        Assert.InRange(17 + x * 300, box.Left, box.Right);
        Assert.InRange(23 + y * 500, box.Top, box.Bottom);
    }

    public static IEnumerable<object[]> LayoutCases()
    {
        foreach (int degrees in new[] { 0, 90, 180, 270 })
            foreach (ViewerDisplayMode mode in Enum.GetValues<ViewerDisplayMode>())
                foreach (ViewerZoomMode zoom in Enum.GetValues<ViewerZoomMode>())
                    yield return [degrees, mode, zoom];
    }

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void BothLayoutsUseRotatedGeometryForEveryZoomMode(int degrees, ViewerDisplayMode mode, ViewerZoomMode zoom)
    {
        ViewerState state = new ViewerState(2).GoToPage(2).SetDisplayMode(mode).SetZoom(150);
        for (int turn = 0; turn < degrees / 90; turn++) state = state.RotateClockwise();
        if (zoom != ViewerZoomMode.Manual) state = state.SetFitMode(zoom);
        ContinuousPageLayout layout = new([new(200, 300), new(400, 200)], state, 632, 432);
        PdfPageSize effective = state.Rotation.EffectiveSize(new(400, 200));
        double scale = zoom switch
        {
            ViewerZoomMode.FitWidth => 600 / effective.WidthPoints,
            ViewerZoomMode.FitPage => Math.Min(600 / effective.WidthPoints, 400 / effective.HeightPoints),
            _ => 2
        };
        Assert.Equal((int)Math.Round(effective.WidthPoints * scale), layout[1].Width);
        Assert.Equal((int)Math.Round(effective.HeightPoints * scale), layout[1].Height);
        if (mode == ViewerDisplayMode.SinglePage)
        {
            Assert.Equal(1, layout.Count);
            Assert.False(layout.ContainsPage(0));
            Assert.Equal(new PageRange(1, 1), layout.Visible(0, 432));
            Assert.Equal(1, layout.CurrentPage(10000, 432));
            if (zoom == ViewerZoomMode.FitPage) Assert.True(layout.PageScrollNavigates(432));
        }
        else
        {
            Assert.Equal(2, layout.Count);
            Assert.Equal(layout[0].Bottom + ContinuousPageLayout.Gap, layout[1].Top);
            Assert.False(layout.PageScrollNavigates(10000));
        }
    }

    [Fact]
    public void ModesPreservePageZoomAndRotationAndNewDocumentsResetThem()
    {
        ViewerState state = new ViewerState(1001).GoToPage(501).SetZoom(275).RotateClockwise();
        ViewerState single = state.SetDisplayMode(ViewerDisplayMode.SinglePage);
        Assert.Equal(500, single.PageIndex);
        Assert.Equal(275, single.ZoomPercent);
        Assert.Equal(90, single.Rotation.Degrees);
        Assert.Equal(state, single.SetDisplayMode(ViewerDisplayMode.Continuous));
        Assert.Equal(501, single.NextPage().PageIndex);
        Assert.Equal(499, single.PreviousPage().PageIndex);
        ViewerState replacement = new(1001);
        Assert.Equal(ViewerDisplayMode.Continuous, replacement.DisplayMode);
        Assert.Equal(0, replacement.Rotation.Degrees);
        Assert.Equal(100, replacement.ZoomPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public void ShortBoundaryPagesRemainCurrentWhenReturningToContinuous(int page)
    {
        PdfPageSize[] sizes = Enumerable.Repeat(new PdfPageSize(72, 72), 1001).ToArray();
        ViewerState state = new ViewerState(1001).GoToPage(page + 1).SetZoom(25).SetDisplayMode(ViewerDisplayMode.SinglePage);
        ContinuousPageLayout single = new(sizes, state, 800, 600);
        ReadingAnchor anchor = single.CaptureAnchor(0, 600, 0, 800);
        ContinuousPageLayout continuous = new(sizes, state.SetDisplayMode(ViewerDisplayMode.Continuous), 800, 600);
        Assert.Equal(page, continuous.CurrentPage(continuous.RestoreAnchor(anchor, 600), 600));
        Assert.Equal(page, continuous.CurrentPage(continuous.ScrollTarget(page, 600), 600));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void LargeDocumentsKeepBoundedDemandAndNeutralReadingAnchor(int degrees)
    {
        PdfPageSize[] sizes = Enumerable.Repeat(new PdfPageSize(600, 900), 1001).ToArray();
        ViewerState initial = new ViewerState(1001).GoToPage(501);
        ContinuousPageLayout original = new(sizes, initial, 200, 200);
        double top = original[500].Top + original[500].Height * .4 - 100;
        double left = original.Left(500, 200) + original[500].Width * .45 - 100;
        ReadingAnchor anchor = original.CaptureAnchor(top, 200, left, 200);
        ViewerState rotated = initial;
        for (int turn = 0; turn < degrees / 90; turn++) rotated = rotated.RotateClockwise();
        foreach (ViewerDisplayMode mode in Enum.GetValues<ViewerDisplayMode>())
        {
            ContinuousPageLayout layout = new(sizes, rotated.SetDisplayMode(mode), 200, 200);
            double restoredTop = layout.RestoreAnchor(anchor, 200);
            double restoredLeft = layout.RestoreHorizontalAnchor(anchor, 200);
            ReadingAnchor restored = layout.CaptureAnchor(restoredTop, 200, restoredLeft, 200);
            Assert.Equal(anchor.PageIndex, restored.PageIndex);
            Assert.Equal(anchor.Fraction, restored.Fraction, 10);
            Assert.Equal(anchor.HorizontalFraction, restored.HorizontalFraction, 10);
            PageRange demand = VisiblePageDemand.Select(layout, restoredTop, 200);
            Assert.InRange(demand.Count, 1, 32);
            Assert.False(layout.PageScrollNavigates(200));
        }
    }
}
