using System.Buffers;
using System.Drawing.Printing;
using MauriPDF.App.Printing;
using MauriPDF.Core.Outline;
using MauriPDF.Core.Rendering;
using MauriPDF.Core.Text;
using MauriPDF.Core.Viewing;
using MauriPDF.Editing;
using Xunit;

namespace MauriPDF.App.Tests;

public sealed class PrintingTests
{
    [Theory]
    [InlineData(PrintRange.AllPages, 1, 10, 3, 10, 0, 9)]
    [InlineData(PrintRange.CurrentPage, 1, 10, 3, 10, 3, 3)]
    [InlineData(PrintRange.SomePages, 2, 5, 3, 10, 1, 4)]
    public void LogicalRangeIsExplicit(PrintRange range, int from, int to, int current, int count, int first, int last) =>
        Assert.Equal((first, last), PrintPageLayout.SelectRange(range, from, to, current, count));

    [Theory]
    [InlineData(0, 3)]
    [InlineData(4, 2)]
    [InlineData(2, 11)]
    public void InvalidRangeIsRejected(int from, int to) => Assert.Throws<ArgumentException>(() =>
        PrintPageLayout.SelectRange(PrintRange.SomePages, from, to, 0, 10));

    [Fact]
    public void MarginsUsePhysicalIntersectionAndPrinterOrigin()
    {
        RectangleF bounds = PrintPageLayout.PrintableBounds(new(35, 35, 700, 1000), new(50, 20, 650, 1100), 50, 20);
        Assert.Equal(new RectangleF(0, 15, 650, 1000), bounds);
        Assert.Throws<InvalidOperationException>(() => PrintPageLayout.PrintableBounds(new(0, 0, 1, 1), new(3, 3, 1, 1), 0, 0));
    }

    [Theory]
    [InlineData(700, 1000)]
    [InlineData(10000, 20000)]
    public void RasterFitsBudgetAndPreservesAspectRatio(float width, float height)
    {
        Size size = PrintPageLayout.RasterSize(new(0, 0, width, height));
        Assert.InRange((long)size.Width * size.Height, 1, PrintPageLayout.MaximumPixels);
        Assert.InRange(Math.Abs((double)size.Width / size.Height - width / height), 0, .002);
    }

    [Fact]
    public void PainterUsesSnapshotSourceAndStructuralRotationOnlyAndReleasesPixels()
    {
        DocumentEditSession edits = new(1001);
        var id = edits.State.Pages[998].Id;
        edits.Execute(new MovePageEdit(id, 0));
        edits.Execute(new RotatePageEdit(id, true));
        PdfMaterializationPlan snapshot = PdfMaterializationPlan.From(edits.State);
        edits.Execute(new DeletePageEdit(id));
        using Session session = new();
        using Bitmap target = new(300, 300);
        using Graphics graphics = Graphics.FromImage(target);
        PrintPagePainter.Draw(session, snapshot.Pages[0], graphics, new(0, 0, 200, 200));
        Assert.Equal(1, session.Renders);
        Assert.Equal(998, session.SourceIndex);
        Assert.Equal(90, session.Rotation.Degrees);
        Assert.True(session.PixelsDisposed);
        Assert.True(session.Width > session.Height); // Intrinsic portrait rotated to landscape.
        Assert.Equal(1001, snapshot.Pages.Count);
    }

    private sealed class Session : IPdfRenderSession
    {
        public int PageCount => 1001;
        public int Renders, SourceIndex, Width, Height;
        public VisualRotation Rotation;
        public bool PixelsDisposed;
        public PdfPageSize GetPageSize(int pageIndex) => new(200, 400);
        public RenderedPage RenderPage(int pageIndex, int pixelWidth, int pixelHeight, VisualRotation rotation = default)
        {
            Renders++; SourceIndex = pageIndex; Rotation = rotation; Width = pixelWidth; Height = pixelHeight;
            return new(pixelWidth, pixelHeight, pixelWidth * 4, RenderedPixelFormat.Bgra32,
                new Pixels(pixelWidth * pixelHeight * 4, () => PixelsDisposed = true));
        }
        public PdfTextPage ExtractText(int pageIndex) => throw new InvalidOperationException("Print must not extract text.");
        public PdfOutline ExtractOutline() => throw new InvalidOperationException("Print must not extract outlines.");
        public void Dispose() { }
    }
    private sealed class Pixels(int length, Action disposed) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory { get; } = new byte[length];
        public void Dispose() => disposed();
    }
}
