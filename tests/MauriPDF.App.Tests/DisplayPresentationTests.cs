using MauriPDF.App.Presentation;
using MauriPDF.Core.Rendering;
using MauriPDF.Core.Viewing;
using Xunit;

namespace MauriPDF.App.Tests;

public sealed class DisplayPresentationTests
{
    private static readonly int[] Dpis = [96, 120, 144, 192];
    [Fact]
    public void NarrowStatusLayoutKeepsNavigationAndStatusInSeparateRows() => ShellPresentationTests.InSta(() =>
    {
        using MauriPdfTheme theme = new();
        using ToolStripTextBox page = new() { AutoSize = false, Width = 55 };
        using ShellStatusBar status = new(theme, page);
        status.Width = 550;
        status.PerformLayout();
        TableLayoutPanel table = Assert.IsType<TableLayoutPanel>(status.Controls[0]);
        Assert.Equal(2, table.RowCount);
        Assert.Equal(56, status.Height);
        Assert.Equal(0, table.GetRow(table.Controls.OfType<Label>().First()));
        Assert.Equal(1, table.GetRow(table.Controls.OfType<ToolStrip>().Single()));
        status.Width = 1000;
        status.PerformLayout();
        Assert.Equal(1, table.RowCount);
        Assert.Equal(MauriPdfTheme.StatusHeight, status.Height);
    });
    [Fact]
    public void HighContrastSelectionUsesTheSystemForegroundBackgroundPair()
    {
        var highContrast = MauriPdfTheme.SelectionColors(true);
        Assert.Equal(SystemColors.Highlight, highContrast.Background);
        Assert.Equal(SystemColors.HighlightText, highContrast.Foreground);
        Assert.NotEqual(MauriPdfTheme.SelectionColors(false).Foreground, MauriPdfTheme.SelectionColors(false).Background);
    }
    [Theory]
    [InlineData(96, 22, 244)]
    [InlineData(120, 28, 256)]
    [InlineData(144, 33, 256)]
    [InlineData(192, 44, 256)]
    public void LogicalDimensionsHaveNoRoundTripDrift(int dpi, int icon, int row)
    {
        for (int pass = 0; pass < 10; pass++)
        {
            Assert.Equal(icon, DisplayMetrics.Scale(22, dpi));
            Assert.Equal(row, DisplayMetrics.ThumbnailRowHeight(dpi));
            Assert.Equal(22, DisplayMetrics.Scale(22, 96));
        }
    }

    [Theory]
    [InlineData(96)] [InlineData(120)] [InlineData(144)] [InlineData(192)]
    public void ViewportScalesManualZoomOnceAndFitUsesDeviceBounds(int dpi)
    {
        PdfPageSize[] sizes = [new(612, 792), new(792, 612)];
        ViewerState state = new ViewerState(2).SetZoom(125).RotateClockwise();
        ContinuousPageLayout layout = new(sizes, state, 900, 700, displayDpi: dpi);
        Assert.Equal((int)Math.Round(792 * dpi / 72.0 * 1.25), layout[0].Width);
        Assert.Equal(125, state.ZoomPercent);
        Assert.Equal(0, state.PageIndex);
        ContinuousPageLayout fit = new(sizes, state.SetFitMode(ViewerZoomMode.FitWidth), 900, 700, displayDpi: dpi);
        Assert.Equal(900 - 2 * DisplayMetrics.Scale(16, dpi), fit[0].Width);
        ContinuousPageLayout page = new(sizes, state.SetDisplayMode(ViewerDisplayMode.SinglePage)
            .SetFitMode(ViewerZoomMode.FitPage), 900, 700, displayDpi: dpi);
        Assert.True(page[0].Width <= 900 - 2 * DisplayMetrics.Scale(16, dpi));
        Assert.True(page[0].Height <= 700 - 2 * DisplayMetrics.Scale(16, dpi));
    }

    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void ReadingAnchorAndRotatedCoordinatesSurviveDpiRoundTrip(int degrees)
    {
        PdfPageSize[] sizes = [new(1000, 1200), new(1000, 1200)];
        ViewerState state = new(2);
        for (int step = 0; step < degrees / 90; step++) state = state.RotateClockwise();
        ContinuousPageLayout first = new(sizes, state, 500, 400);
        ReadingAnchor anchor = first.CaptureAnchor(500, 400, 250, 500);
        foreach (int dpi in new[] { 120, 144, 192, 96 })
        {
            ContinuousPageLayout next = new(sizes, state, 500, 400, displayDpi: dpi);
            ReadingAnchor restored = next.CaptureAnchor(next.RestoreAnchor(anchor, 400), 400,
                next.RestoreHorizontalAnchor(anchor, 500), 500);
            Assert.Equal(anchor.PageIndex, restored.PageIndex);
            Assert.Equal(anchor.Fraction, restored.Fraction, 6);
            Assert.Equal(anchor.HorizontalFraction, restored.HorizontalFraction, 6);
        }
    }

    [Fact]
    public void AccessibleSelectedCommandsAndTabsExposeStateAndDefaultAction() => ShellPresentationTests.InSta(() =>
    {
        using MauriPdfTheme theme = new();
        using CommandIcons icons = new();
        using ToolTip tooltip = new();
        using ToolStripButton source = new("Continuous") { Checked = true };
        using RibbonCommandButton button = new(source, "Continuous", CommandIcon.Continuous, false, theme, icons, tooltip);
        Assert.True(button.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        int calls = 0;
        source.Click += (_, _) => calls++;
        button.AccessibilityObject.DoDefaultAction();
        Assert.Equal(1, calls);
        source.Checked = false;
        button.RefreshCommand();
        Assert.False(button.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        using RibbonTabButton tab = new() { Selected = true };
        Assert.True(tab.AccessibilityObject.State.HasFlag(AccessibleStates.Selected));
    });

    [Fact]
    public void PaletteAndSizeVariantsStayAliveUntilTheirBorrowersReleaseThem() => ShellPresentationTests.InSta(() =>
    {
        using CommandIcons icons = new();
        Bitmap normal = icons.Get(CommandIcon.Open, 22, Color.Black);
        Bitmap contrast = icons.Get(CommandIcon.Open, 22, Color.White);
        Assert.NotSame(normal, contrast);
        Assert.Same(contrast, icons.Get(CommandIcon.Open, 22, Color.White));
        icons.Retain(new HashSet<Image> { contrast });
        Assert.Equal(22, contrast.Width);
        Bitmap recreated = icons.Get(CommandIcon.Open, 22, Color.Black);
        Assert.NotSame(normal, recreated);
        foreach (int dpi in Dpis)
            Assert.Equal(DisplayMetrics.Scale(32, dpi), icons.Get(CommandIcon.Print, DisplayMetrics.Scale(32, dpi)).Width);
    });
}
