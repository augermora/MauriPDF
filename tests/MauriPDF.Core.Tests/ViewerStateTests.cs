using MauriPDF.Core.Viewing;
using Xunit;

namespace MauriPDF.Core.Tests;

public sealed class ViewerStateTests
{
    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    [InlineData(17, 16)]
    [InlineData(int.MaxValue, 16)]
    public void PageEntryIsClamped(int requested, int expectedIndex)
    {
        Assert.Equal(expectedIndex, new ViewerState(17).GoToPage(requested).PageIndex);
    }

    [Fact]
    public void NavigationStopsAtBothEnds()
    {
        ViewerState first = new(3);
        Assert.False(first.CanGoPrevious);
        Assert.Equal(first, first.PreviousPage());
        Assert.Equal(1, first.NextPage().PageIndex);
        ViewerState last = first.GoToPage(3);
        Assert.False(last.CanGoNext);
        Assert.Equal(last, last.NextPage());
        Assert.Equal(1, last.PreviousPage().PageIndex);
    }

    [Fact]
    public void SinglePageCannotNavigate()
    {
        ViewerState state = new(1);
        Assert.False(state.CanGoPrevious);
        Assert.False(state.CanGoNext);
    }

    [Theory]
    [InlineData(-100, 25)]
    [InlineData(100, 100)]
    [InlineData(1000, 500)]
    public void ZoomIsBounded(int requested, int expected)
    {
        Assert.Equal(expected, new ViewerState(1).SetZoom(requested).ZoomPercent);
    }

    [Fact]
    public void ZoomStepAndResetExitFitMode()
    {
        ViewerState fitted = new ViewerState(3).SetFitMode(ViewerZoomMode.FitWidth);
        Assert.Equal(125, fitted.ZoomIn().ZoomPercent);
        Assert.Equal(ViewerZoomMode.Manual, fitted.ZoomIn().ZoomMode);
        Assert.Equal(75, fitted.ZoomOut().ZoomPercent);
        Assert.Equal(100, fitted.SetZoom(100).ZoomPercent);
        Assert.Equal(ViewerZoomMode.Manual, fitted.SetZoom(100).ZoomMode);
        Assert.Equal(25, fitted.SetZoom(25).ZoomOut().ZoomPercent);
        Assert.Equal(500, fitted.SetZoom(500).ZoomIn().ZoomPercent);
    }

    [Fact]
    public void NewDocumentResetsStateAndNavigationPreservesZoom()
    {
        ViewerState changed = new ViewerState(5).SetZoom(200).GoToPage(4);
        Assert.Equal(200, changed.NextPage().ZoomPercent);
        ViewerState fresh = new(2);
        Assert.Equal(0, fresh.PageIndex);
        Assert.Equal(100, fresh.ZoomPercent);
        Assert.Equal(ViewerZoomMode.Manual, fresh.ZoomMode);
    }
}
